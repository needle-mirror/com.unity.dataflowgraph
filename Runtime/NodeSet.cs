using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Profiling;

namespace Unity.DataFlowGraph
{
    /// <summary>
    /// A node set is a set of instantiated user nodes connected together in
    /// some particular way, although not necessarily completely connected.
    /// Nodes can communicate through flowing data or messages, and the
    /// execution pattern is defined from the connections you establish.
    /// <seealso cref="NodeDefinition{TNodeData}"/>
    /// <seealso cref="Create{TDefinition}"/>
    /// <seealso cref="Connect(NodeHandle, OutputPortID, NodeHandle, InputPortID, ConnectionType)"/>
    /// <seealso cref="Update"/>
    /// </summary>
    public partial class NodeSetAPI
    {
        static ProfilerMarker m_CopyWorldsProfilerMarker = new ProfilerMarker("NodeSet.CopyWorlds");
        static ProfilerMarker m_FenceOutputConsumerProfilerMarker = new ProfilerMarker("NodeSet.FenceOutputConsumers");
        static ProfilerMarker m_SimulateProfilerMarker = new ProfilerMarker("NodeSet.Simulate");
        static ProfilerMarker m_SwapGraphValuesProfilerMarker = new ProfilerMarker("NodeSet.SwapGraphValues");


        internal RenderGraph DataGraph => m_RenderGraph;

        static InvalidDefinitionSlot s_InvalidDefinitionSlot = new InvalidDefinitionSlot();

        List<NodeDefinition> m_NodeDefinitions = new List<NodeDefinition>();

        internal VersionedList<InternalNodeData> Nodes;

        BlitList<LLTraitsHandle> m_Traits = new BlitList<LLTraitsHandle>(0);
        List<ManagedMemoryAllocator> m_ManagedAllocators = new List<ManagedMemoryAllocator>();

        GraphDiff m_Diff = new GraphDiff(Allocator.Persistent);

        RenderGraph m_RenderGraph;

        /// <summary>
        /// Unique ID for this particular instance.
        /// </summary>
        readonly internal ushort NodeSetID;

        static ushort s_NodeSetCounter;

        bool m_IsDisposed;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        DisposeSentinel m_Sentinel;
        AtomicSafetyHandle m_UnusedSafetyHandle;
#endif

        internal enum InternalDispatch { Tag }
        internal enum ComponentSystemDispatch { Tag }

        internal NodeSetAPI(ComponentSystemBase hostSystem, InternalDispatch _)
        {
            m_NodeDefinitions.Add(s_InvalidDefinitionSlot);

            var defaultTraits = LLTraitsHandle.Create();
            // stuff that needs the first slot to be invalid
            defaultTraits.Resolve() = new LowLevelNodeTraits();
            m_Traits.Add(defaultTraits);
            m_ForwardingTable.Allocate();
            m_ArraySizes.Allocate();
            m_ManagedAllocators.Add(default);

            for(int i = 0; i < (int)UpdateState.ValidUpdateOffset; ++i)
                m_UpdateIndices.Allocate();

            // (we don't need a zeroth invalid index for nodes, because they are versioned)

            HostSystem = hostSystem;
            InternalRendererModel = NodeSet.RenderExecutionModel.MaximallyParallel;
            m_RenderGraph = new RenderGraph(this);
            NodeSetID = ++s_NodeSetCounter;

            m_GraphValues = new VersionedList<DataOutputValue>(Allocator.Persistent, NodeSetID);
            Nodes = new VersionedList<InternalNodeData>(Allocator.Persistent, NodeSetID);
            DebugInfo.RegisterNodeSetCreation(this);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            DisposeSentinel.Create(out m_UnusedSafetyHandle, out m_Sentinel, 1, Allocator.Persistent);
#endif
        }

        /// <summary>
        /// Instantiates a particular type of node. If this is the first time
        /// this node type is created, the <typeparamref name="TDefinition"/>
        /// is instantiated as well.
        ///
        /// Remember to destroy the node again.
        /// <seealso cref="Destroy(NodeHandle)"/>
        /// </summary>
        /// <returns>
        /// A handle to a node, that uniquely identifies the instantiated node.
        /// The handle returned is "strongly" typed in that it is verified to
        /// refer to such a node type - see <see cref="NodeHandle{TDefinition}"/>
        /// for more information.
        /// This handle is the primary interface for all APIs on nodes.
        /// After the node has been destroyed, any copy of this handle is
        /// invalidated, see <see cref="Exists(NodeHandle)"/>.
        /// </returns>
        /// <exception cref="InvalidNodeDefinitionException">
        /// Thrown if the <typeparamref name="TDefinition"/> is not a valid
        /// node definition.
        /// </exception>
        public NodeHandle<TDefinition> Create<TDefinition>()
            where TDefinition : NodeDefinition, new()
        {
            return new NodeHandle<TDefinition>(CreateInternal<TDefinition>().Versioned);
        }

        /// <summary>
        /// Destroys a node, identified by the handle.
        /// This invokes <see cref="NodeDefinition.Destroy(DestroyContext context)"/>
        /// if implemented.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// Thrown if the node is already destroyed or invalid.
        /// </exception>
        public void Destroy(NodeHandle handle)
        {
            Destroy(ref Nodes[handle.VHandle]);
        }

        /// <summary>
        /// Convenience function for destroying a range of nodes.
        /// <seealso cref="Destroy(NodeHandle)"/>
        /// </summary>
        public void Destroy(params NodeHandle[] handles)
        {
            for (int i = 0; i < handles.Length; ++i)
            {
                Destroy(handles[i]);
            }
        }

        /// <summary>
        /// Tests whether the supplied node handle refers to a currently valid
        /// node instance.
        /// </summary>
        public bool Exists(NodeHandle handle)
            => Nodes.Exists(handle.VHandle);

        internal (NodeDefinition Definition, int Index) LookupDefinition<TDefinition>()
            where TDefinition : NodeDefinition, new()
        {
            var index = NodeDefinitionTypeIndex<TDefinition>.Index;

            // fill "sparse" table with the invalid definition.
            while (index >= m_NodeDefinitions.Count)
            {
                m_NodeDefinitions.Add(s_InvalidDefinitionSlot);
                // TODO: We can, instead of wasting allocations, just make .Resolve() throw errors.
                m_Traits.Add(LLTraitsHandle.Create());
                m_ManagedAllocators.Add(default);
            }

            var definition = m_NodeDefinitions[index];
            if (definition == s_InvalidDefinitionSlot)
            {
                // TODO: Instead of field injection, use constructor?
                LLTraitsHandle traitsHandle = new LLTraitsHandle();
                ManagedMemoryAllocator allocator = default;

                try
                {
                    definition = new TDefinition();
                    definition.Set = this;
                    definition.GeneratePortDescriptions();
                    RegisterECSPorts(definition.AutoPorts);

                    traitsHandle = definition.BaseTraits.CreateNodeTraits(typeof(TDefinition), definition.SimulationStorageTraits, definition.KernelStorageTraits);

                    ref var traits = ref traitsHandle.Resolve();

                    if (traits.SimulationStorage.NodeDataIsManaged)
                        allocator = new ManagedMemoryAllocator(definition.BaseTraits.ManagedAllocator);
                }
                catch
                {
                    if (traitsHandle.IsCreated)
                        traitsHandle.Dispose();

                    if (allocator.IsCreated)
                        allocator.Dispose();

                    if(definition != s_InvalidDefinitionSlot)
                        definition.Dispose();

                    throw;
                }

                m_NodeDefinitions[index] = definition;
                m_Traits[index] = traitsHandle;
                m_ManagedAllocators[index] = allocator;
            }

            return (definition, index);
        }

        ValidatedHandle CreateInternal<TDefinition>()
            where TDefinition : NodeDefinition, new()
        {
            var def = LookupDefinition<TDefinition>();

            ValidatedHandle handle;
            {
                ref var node = ref AllocateData();
                node.TraitsIndex = def.Index;
                handle = node.Handle;

                SetupNodeMemory(ref m_Traits[def.Index].Resolve(), ref node);

                m_Topology.GetRef(handle) = new TopologyIndex();
            }

            BlitList<ForwardedPort.Unchecked> forwardedConnections = default;
            var context = new InitContext(handle, def.Index, this, ref forwardedConnections);

            try
            {
                // To ensure consistency with Destroy(, false)
                m_Diff.NodeCreated(handle);
                m_Database.VertexCreated(ref m_Topology, handle);
                def.Definition.InitInternal(context);

                if (Nodes.StillExists(handle) && forwardedConnections.IsCreated && forwardedConnections.Count > 0)
                    MergeForwardConnectionsToTable(ref Nodes[handle], forwardedConnections);

                SignalTopologyChanged();
            }
            catch
            {
                Debug.LogError("Throwing exceptions from constructors is undefined behaviour");
                Destroy(ref Nodes[handle], false);
                throw;
            }
            finally
            {
                if (forwardedConnections.IsCreated)
                    forwardedConnections.Dispose();
            }

            return handle;
        }

        void Destroy(ref InternalNodeData node, bool callDestructor = true)
        {
            var index = node.TraitsIndex;

            if (callDestructor)
            {
                try
                {
                    m_NodeDefinitions[index].DestroyInternal(new DestroyContext(node.Handle, this));
                }
                catch (Exception e)
                {
                    // Nodes should never throw from destructors.
                    // Can't really propagate exception up: We may be inside the finalizer, completely smashing the clean up, or destroying a range of nodes which is atomic.
                    // Let the user know about it.
                    Debug.LogError($"Undefined behaviour when throwing exceptions from destructors (node type: {m_NodeDefinitions[index].GetType()})");
                    Debug.LogException(e);
                }
            }

            // Note: Moved after destructor call.
            UncheckedDisconnectAll(ref node);
            CleanupForwardedConnections(ref node);
            CleanupPortArraySizes(ref node);

            if (node.UpdateIndex != (int)UpdateState.InvalidUpdateIndex)
            {
                RemoveFromUpdate(node.Handle);
            }

            unsafe
            {
                if (node.UserData != null)
                {
                    if (!m_Traits[index].Resolve().SimulationStorage.NodeDataIsManaged)
                    {
                        UnsafeUtility.Free(node.UserData, Allocator.Persistent);
                    }
                    else
                    {
                        m_ManagedAllocators[node.TraitsIndex].Free(node.UserData);
                    }
                }

                if (node.KernelData != null)
                {
                    UnsafeUtility.Free(node.KernelData, Allocator.Persistent);
                }
            }

            m_Diff.NodeDeleted(node.Handle, index);

            m_Database.VertexDeleted(ref m_Topology, node.Handle);
            Nodes.Release(node);
            SignalTopologyChanged();
        }

        protected bool InternalIsCreated => !m_IsDisposed;

        protected void InternalDispose()
        {
            if (m_IsDisposed)
                return;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            DisposeSentinel.Dispose(ref m_UnusedSafetyHandle, ref m_Sentinel);
#endif

            m_IsDisposed = true;

            int leakedNodes = 0;
            foreach (ref var node in Nodes.Items)
            {
                Destroy(ref node);
                leakedNodes++;
            }

            int leakedGraphValues = 0;

            foreach(ref readonly var value in m_GraphValues.Items)
            {
                leakedGraphValues++;
                m_GraphValues.Release(value);
            }

            LogPendingTestExceptions();

            // Primarily used for diff'ing the RenderGraph
            // TODO: Assert/Test order of these statements - it's important that all handlers and definition is alive at this update statement
            UpdateInternal(m_LastJobifiedUpdateHandle);

            if (leakedNodes > 0 || leakedGraphValues > 0 || m_PendingBufferUploads.Count > 0)
            {
                Debug.LogError($"NodeSet leak warnings: " +
                    $"{leakedNodes} leaked node(s)," +
                    $"{leakedGraphValues} leaked graph value(s) and " +
                    $"{m_PendingBufferUploads.Count} leaked buffer upload requests left!"
                );
            }

#if DFG_ASSERTIONS
            // At this point, the forwarding table should be empty, except for the first, invalid index.
            int badForwardedPorts = m_ForwardingTable.InUse - 1;

            // After all nodes are destroyed, there should be no more array size entries.
            int badArraySizes = m_ArraySizes.InUse - 1;

            // if any connections are left are recorded as valid, the topology database is corrupted
            // (since destroying a node removes all its connections, thus all connections should be gone
            // after destroying all nodes)
            var badConnections = m_Database.CountEstablishedConnections();

            if (badConnections > 0 || badForwardedPorts > 0 || badArraySizes > 0)
            {
                Debug.LogError("NodeSet internal leaks: " +
                    $"{badForwardedPorts} corrupted forward definition(s), " +
                    $"{badArraySizes} dangling array size entry(s), " +
                    $"{badConnections} corrupted connections left!"
                );
            }
#endif

            DebugInfo.RegisterNodeSetDisposed(this);

            foreach (var handler in m_ConnectionHandlerMap)
            {
                handler.Value.Dispose();
            }

            foreach (var definition in m_NodeDefinitions)
            {
                if (definition != s_InvalidDefinitionSlot)
                    definition.Dispose();
            }

            for (int i = 0; i < m_Traits.Count; ++i)
                if (m_Traits[i].IsCreated)
                    m_Traits[i].Dispose();

            for (int i = 0; i < m_ManagedAllocators.Count; ++i)
                if (m_ManagedAllocators[i].IsCreated)
                    m_ManagedAllocators[i].Dispose();

            for (int i = 0; i < m_PendingBufferUploads.Count; ++i)
                m_PendingBufferUploads[i].FreeIfNeeded(Allocator.Persistent);

            m_Database.Dispose();

            m_Topology.Dispose();

            Nodes.Dispose();

            m_GraphValues.Dispose();

            m_PostRenderValues.Dispose();
            m_ReaderFences.Dispose();

            m_Diff.Dispose();
            m_RenderGraph.Dispose();

            m_Traits.Dispose();
            m_ManagedAllocators.Clear();

            m_ForwardingTable.Dispose();

            m_ArraySizes.Dispose();
            m_UpdateIndices.Dispose();
            m_UpdateRequestQueue.Dispose();
                m_PendingBufferUploads.Dispose();

            if (m_ActiveComponentTypes.IsCreated)
                m_ActiveComponentTypes.Dispose();
        }

        internal NodeDefinition GetDefinition(NodeHandle handle)
        {
            return GetDefinitionInternal(Nodes.Validate(handle.VHandle));
        }

        internal TDefinition GetDefinition<TDefinition>()
            where TDefinition : NodeDefinition, new()
        {
            return (TDefinition)LookupDefinition<TDefinition>().Definition;
        }

        internal TDefinition GetDefinition<TDefinition>(NodeHandle<TDefinition> handle)
            where TDefinition : NodeDefinition, new()
        {
            Nodes.Validate(handle.VHandle);
            return (TDefinition)LookupDefinition<TDefinition>().Definition;
        }

        /// <summary>
        /// Tests whether the node instance referred to by the <paramref name="handle"/>
        /// is a <typeparamref name="TDefinition"/>.
        /// </summary>
        public bool Is<TDefinition>(NodeHandle handle)
            where TDefinition : NodeDefinition
        {
            return GetDefinition(handle) is TDefinition;
        }

        /// <summary>
        /// Returns a nullable strongly typed node handle, which is valid if
        /// the node is a <typeparamref name="TDefinition"/>.
        /// <seealso cref="Is{TDefinition}(NodeHandle)"/>
        /// </summary>
        /// <exception cref="ArgumentException">
        /// If the <paramref name="handle"/> does not refer to a valid instance.
        /// </exception>
        public NodeHandle<TDefinition>? As<TDefinition>(NodeHandle handle)
            where TDefinition : NodeDefinition
        {
            if (!Is<TDefinition>(handle))
                return null;

            return new NodeHandle<TDefinition>(handle.VHandle);
        }

        /// <summary>
        /// Casts a untyped node handle to a strongly typed version.
        /// <seealso cref="Is{TDefinition}(NodeHandle)"/>
        /// </summary>
        /// <exception cref="ArgumentException">
        /// If the <paramref name="handle"/> does not refer to a valid instance.
        /// </exception>
        /// <exception cref="InvalidCastException">
        /// If the <paramref name="handle"/> is not a <typeparamref name="TDefinition"/>.
        /// </exception>
        public NodeHandle<TDefinition> CastHandle<TDefinition>(NodeHandle handle)
            where TDefinition : NodeDefinition
        {
            if (!Is<TDefinition>(handle))
                throw new InvalidCastException();

            return new NodeHandle<TDefinition>(handle.VHandle);
        }

        /// <summary>
        /// Set the size of a <see cref="Buffer{T}"/> appearing in a <see cref="DataOutput{D,TType}"/>.
        /// See
        /// <see cref="SetBufferSize{TDefinition, TType}(NodeHandle{TDefinition}, DataOutput{TDefinition, TType}, TType)"/>
        /// for more information.
        /// </summary>
        /// <exception cref="ArgumentException">If the <paramref name="handle"/> does not refer to a valid instance.</exception>
        /// <exception cref="InvalidOperationException">Thrown if the request is invalid.</exception>
        public void SetBufferSize<TType>(NodeHandle handle, OutputPortID port, in TType requestedSize)
            where TType : struct
        {
            SetBufferSize(new OutputPair(this, handle, new OutputPortArrayID(port)), requestedSize);
        }

        /// <summary>
        /// Set the size of a <see cref="Buffer{T}"/> appearing in a <see cref="DataOutput{D,TType}"/> of a <see cref="PortArray{TPort}"/>.
        /// See
        /// <see cref="SetBufferSize{TDefinition, TType}(NodeHandle{TDefinition}, DataOutput{TDefinition, TType}, TType)"/>
        /// for more information.
        /// </summary>
        /// <exception cref="ArgumentException">If the <paramref name="handle"/> does not refer to a valid instance.</exception>
        /// <exception cref="InvalidOperationException">Thrown if the request is invalid.</exception>
        /// <exception cref="IndexOutOfRangeException">Thrown if the index is out of range with respect to the port array.</exception>
        public void SetBufferSize<TType>(NodeHandle handle, OutputPortID port, int arrayIndex, in TType requestedSize)
            where TType : struct
        {
            SetBufferSize(new OutputPair(this, handle, new OutputPortArrayID(port, arrayIndex)), requestedSize);
        }

        /// <summary>
        /// Set the size of a <see cref="Buffer{T}"/> appearing in a <see cref="DataOutput{D,TType}"/>. If
        /// <typeparamref name="TType"/> is itself a <see cref="Buffer{T}"/>, pass the result of
        /// <see cref="Buffer{T}.SizeRequest(int)"/> as the requestedSize argument.
        ///
        /// If <typeparamref name="TType"/> is a struct containing one or multiple <see cref="Buffer{T}"/> instances,
        /// pass an instance of the struct as the requestedSize parameter with <see cref="Buffer{T}"/> instances within
        /// it having been set using <see cref="Buffer{T}.SizeRequest(int)"/>.
        /// Any <see cref="Buffer{T}"/> instances within the given struct that have not been set using
        /// <see cref="Buffer{T}.SizeRequest(int)"/> will be unaffected by the call.
        /// </summary>
        /// <exception cref="ArgumentException">If the <paramref name="handle"/> does not refer to a valid instance.</exception>
        /// <exception cref="InvalidOperationException">Thrown if the request is invalid.</exception>
        public void SetBufferSize<TDefinition, TType>(NodeHandle<TDefinition> handle, DataOutput<TDefinition, TType> port, in TType requestedSize)
            where TDefinition : NodeDefinition
            where TType : struct
        {
            var source = new OutputPair(this, handle, new OutputPortArrayID(port.Port));
            SetPortBufferSizeWithCorrectlyTypedSizeParameter(source, GetFormalPort(source), requestedSize);
        }

        /// <summary>
        /// Set the size of a <see cref="Buffer{T}"/> appearing in a <see cref="DataOutput{D,TType}"/> of a <see cref="PortArray{TPort}"/>. If
        /// <typeparamref name="TType"/> is itself a <see cref="Buffer{T}"/>, pass the result of
        /// <see cref="Buffer{T}.SizeRequest(int)"/> as the requestedSize argument.
        ///
        /// If <typeparamref name="TType"/> is a struct containing one or multiple <see cref="Buffer{T}"/> instances,
        /// pass an instance of the struct as the requestedSize parameter with <see cref="Buffer{T}"/> instances within
        /// it having been set using <see cref="Buffer{T}.SizeRequest(int)"/>.
        /// Any <see cref="Buffer{T}"/> instances within the given struct that have not been set using
        /// <see cref="Buffer{T}.SizeRequest(int)"/> will be unaffected by the call.
        /// </summary>
        /// <exception cref="ArgumentException">If the <paramref name="handle"/> does not refer to a valid instance.</exception>
        /// <exception cref="InvalidOperationException">Thrown if the request is invalid.</exception>
        /// <exception cref="IndexOutOfRangeException">Thrown if the index is out of range with respect to the port array.</exception>
        public void SetBufferSize<TDefinition, TType>(NodeHandle<TDefinition> handle, PortArray<DataOutput<TDefinition, TType>> port, int arrayIndex, in TType requestedSize)
            where TDefinition : NodeDefinition
            where TType : struct
        {
            var source = new OutputPair(this, handle, new OutputPortArrayID(port.GetPortID(), arrayIndex));
            SetPortBufferSizeWithCorrectlyTypedSizeParameter(source, GetFormalPort(source), requestedSize);
        }

        internal unsafe void UpdateKernelData<TKernelData>(ValidatedHandle h, in TKernelData data)
            where TKernelData : struct, IKernelData
        {
            ref readonly var node = ref Nodes[h];
            var hash = m_Traits[node.TraitsIndex].Resolve().KernelStorage.KernelDataHash;

            if (hash != TypeHash.Create<TKernelData>())
                throw new InvalidOperationException($"Updated kernel data type {typeof(TKernelData)} does not match expected kernel data type for node {h}");

            UnsafeUtility.MemCpy(node.KernelData, UnsafeUtilityExtensions.AddressOf(data), UnsafeUtility.SizeOf<TKernelData>());
        }

        void SetBufferSize<TType>(in OutputPair source, in TType requestedSize)
            where TType : struct
        {
            var portDescription = GetFormalPort(source);

            if (portDescription.Category != PortDescription.Category.Data)
                throw new InvalidOperationException("Cannot set size on a non DataOutput port");

            if (portDescription.Type != typeof(TType))
            {
                if (portDescription.Type.IsConstructedGenericType && portDescription.Type.GetGenericTypeDefinition() == typeof(Buffer<>))
                    throw new InvalidOperationException($"Expecting the return value of {portDescription.Type}.SizeRequest().");
                else
                    throw new InvalidOperationException($"Expecting an instance of {portDescription.Type}");
            }

            SetPortBufferSizeWithCorrectlyTypedSizeParameter(source, portDescription, requestedSize);
        }

        void SetPortBufferSizeWithCorrectlyTypedSizeParameter<TType>(in OutputPair source, in PortDescription.OutputPort port, in TType sizeRequest)
            where TType : struct
        {
            if (!port.HasBuffers)
                throw new InvalidOperationException("Cannot set size on a DataOutput port which contains no Buffer<T> instances in its type.");

            foreach (var bufferInfo in port.BufferInfos)
            {
                ref readonly var desc = ref bufferInfo.AsUntyped(sizeRequest);

                if(desc.HasSizeRequest)
                {
                    m_Diff.NodeBufferResized(source, bufferInfo.BufferIndex, desc.GetSizeRequest(), bufferInfo.ItemType);
                }
                else if(!desc.Equals(default))
                {
                    if (port.Type.IsConstructedGenericType && port.Type.GetGenericTypeDefinition() == typeof(Buffer<>))
                        throw new InvalidOperationException($"Expecting the return value of {port.Type}.SizeRequest().");
                    else
                        throw new InvalidOperationException($"Expecting the return value of Buffer<T>.SizeRequest() on individual fields of {port.Type} for sizes being set.");
                }
            }
        }

        internal unsafe void UpdateKernelBuffers<TGraphKernel>(ValidatedHandle handle, in TGraphKernel requested)
            where TGraphKernel : struct, IGraphKernel
        {
            var llTraits = m_Traits[Nodes[handle].TraitsIndex].Resolve();
            if (llTraits.KernelStorage.KernelHash != TypeHash.Create<TGraphKernel>())
                throw new ArgumentException($"Graph Kernel type {typeof(TGraphKernel)} does not match NodeDefinition");

            var kernel = (RenderKernelFunction.BaseKernel*)UnsafeUtilityExtensions.AddressOf(requested);

            foreach (var bufferInfo in llTraits.KernelStorage.KernelBufferInfos)
            {
                ref readonly var description = ref bufferInfo.Offset.AsUntyped(kernel);

                if(description.HasOwnedMemoryContents)
                {
                    if (description.OwnerNode != handle)
                        throw new InvalidOperationException(
                            $"Issue to {nameof(CommonContextAPI.UploadRequest)} " +
                            $"originated from a different node than the target owner"
                    );

                    var supposedIndex = FindPendingBufferIndex(description);

                    if (supposedIndex == -1)
                    {
                        throw new InvalidOperationException(
                            $"Issue to {nameof(CommonContextAPI.UploadRequest)} was submitted more than once," +
                            $" or came from an unknown place"
                        );
                    }

                    m_PendingBufferUploads.RemoveAtSwapBack(supposedIndex);

                    m_Diff.KernelBufferUpdated(handle, bufferInfo.BufferIndex, description.Size, description.Ptr);
                }
                else if(description.HasSizeRequest)
                {
                    m_Diff.KernelBufferResized(handle, bufferInfo.BufferIndex, description.GetSizeRequest(), bufferInfo.ItemType);
                }
                else if(!description.Equals(default))
                {
                    throw new InvalidOperationException(
                        $"Unexpected contents of kernel buffer at +{bufferInfo.Offset}, " +
                        $"expected return value of Buffer<T>.SizeRequest or Buffer<T>.UploadRequest"
                    );
                }
            }
        }

        unsafe void SetupNodeMemory(ref LowLevelNodeTraits traits, ref InternalNodeData node)
        {
            if (!traits.SimulationStorage.NodeDataIsManaged)
            {
                node.UserData = Utility.CAlloc(traits.SimulationStorage.NodeData, Allocator.Persistent);
            }
            else
            {
                node.UserData = m_ManagedAllocators[node.TraitsIndex].Alloc();
            }

            node.KernelData = null;
            if (traits.HasKernelData)
            {
                node.KernelData = (RenderKernelFunction.BaseData*)Utility.CAlloc(traits.KernelStorage.KernelData, Allocator.Persistent);
            }
        }

        internal NodeDefinition GetDefinitionInternal(ValidatedHandle handle) => m_NodeDefinitions[Nodes[handle].TraitsIndex];

        internal PortDescription.InputPort GetFormalPort(in InputPair destination)
            => GetDefinitionInternal(destination.Handle).GetFormalInput(destination.Handle, destination.Port);
        internal PortDescription.OutputPort GetFormalPort(in OutputPair source)
            => GetDefinitionInternal(source.Handle).GetFormalOutput(source.Handle, source.Port);

        internal PortDescription.InputPort GetVirtualPort(in InputPair destination)
            => GetDefinitionInternal(destination.Handle).GetVirtualInput(destination.Handle, destination.Port);
        internal PortDescription.OutputPort GetVirtualPort(in OutputPair source)
            => GetDefinitionInternal(source.Handle).GetVirtualOutput(source.Handle, source.Port);

        internal unsafe ref TNodeData GetNodeData<TNodeData>(NodeHandle handle)
            where TNodeData : struct, INodeData
        {
            ref readonly var node = ref Nodes[handle.VHandle];
#if DFG_ASSERTIONS
            var hash = m_Traits[node.TraitsIndex].Resolve().SimulationStorage.NodeDataHash;
            if (hash != TypeHash.Create<TNodeData>())
                throw new AssertionException($"Requested type {typeof(TNodeData)} does not match node data type for node {handle}");
#endif
            return ref UnsafeUtility.AsRef<TNodeData>(node.UserData);
        }

        internal void CheckNodeDataType<TNodeData>(NodeHandle handle)
        {
            ref readonly var node = ref Nodes[handle.VHandle];
            var hash = m_Traits[node.TraitsIndex].Resolve().SimulationStorage.NodeDataHash;
            if (hash != TypeHash.Create<TNodeData>())
                throw new InvalidOperationException($"Requested type {typeof(TNodeData)} does not match node data type for node {handle}");
        }

        internal unsafe void* GetNodeDataRaw(ValidatedHandle handle)
        {
            return Nodes[handle].UserData;
        }

        internal unsafe ref TKernelData GetKernelData<TKernelData>(NodeHandle handle)
            where TKernelData : struct, IKernelData
        {
            ref readonly var node = ref Nodes[handle.VHandle];
#if DFG_ASSERTIONS
            var hash = m_Traits[node.TraitsIndex].Resolve().KernelStorage.KernelDataHash;
            if (hash != TypeHash.Create<TKernelData>())
                throw new AssertionException($"Requested type {typeof(TKernelData)} does not match kernel data type for node {handle}");
#endif
            return ref UnsafeUtility.AsRef<TKernelData>(node.KernelData);
        }

        internal ref InternalNodeData AllocateData()
        {
            ref var data = ref Nodes.Allocate();
            data.TraitsIndex = InvalidTraitSlot;
            m_Topology.EnsureSize(Nodes.UnvalidatedCount);

            return ref data;
        }

        // For testing
        internal GraphDiff GetCurrentGraphDiff() => m_Diff;
        // For other API
        internal List<NodeDefinition> GetDefinitions() => m_NodeDefinitions;
        internal BlitList<LLTraitsHandle> GetLLTraits() => m_Traits;
        internal ref readonly LowLevelNodeTraits GetNodeTraits(NodeHandle handle) => ref m_Traits[Nodes[handle.VHandle].TraitsIndex].Resolve();
    }
}
