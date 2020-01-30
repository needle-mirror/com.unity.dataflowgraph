using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;

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
    public partial class NodeSet
    {
        static ProfilerMarker m_CopyWorldsProfilerMarker = new ProfilerMarker("NodeSet.CopyWorlds");
        static ProfilerMarker m_FenceOutputConsumerProfilerMarker = new ProfilerMarker("NodeSet.FenceOutputConsumers");
        static ProfilerMarker m_SimulateProfilerMarker = new ProfilerMarker("NodeSet.Simulate");
        static ProfilerMarker m_SwapGraphValuesProfilerMarker = new ProfilerMarker("NodeSet.SwapGraphValues");
        

        internal RenderGraph DataGraph => m_RenderGraph;

        static InvalidDefinitionSlot s_InvalidDefinitionSlot = new InvalidDefinitionSlot();

        List<NodeDefinition> m_NodeDefinitions = new List<NodeDefinition>();

        // IWBN if nodes+topologies were in a SOA array, they follow exactly the
        // same array structure
        // TODO: Rework to new indexers
        BlitList<InternalNodeData> m_Nodes = new BlitList<InternalNodeData>(0);
        BlitList<int> m_FreeNodes = new BlitList<int>(0);

        BlitList<LLTraitsHandle> m_Traits = new BlitList<LLTraitsHandle>(0);
        BlitList<ManagedMemoryAllocator> m_ManagedAllocators = new BlitList<ManagedMemoryAllocator>(0);

        GraphDiff m_Diff = new GraphDiff(Allocator.Persistent);

        RenderGraph m_RenderGraph;

        /// <summary>
        /// Unique ID for this particular instance.
        /// </summary>
        readonly internal ushort NodeSetID;
        static ushort s_NodeSetCounter;

        bool m_IsDisposed;

        /// <summary>
        /// Construct a node set. Remember to dispose it.
        /// <seealso cref="Dispose"/>
        /// </summary>
        public NodeSet()
        {
            m_NodeDefinitions.Add(s_InvalidDefinitionSlot);

            var defaultTraits = LLTraitsHandle.Create();
            // stuff that needs the first slot to be invalid
            defaultTraits.Resolve() = new LowLevelNodeTraits();
            m_Traits.Add(defaultTraits);
            m_ForwardingTable.Allocate();
            m_ArraySizes.Allocate();
            m_ManagedAllocators.Add(new ManagedMemoryAllocator());
            // (we don't need a zeroth invalid index for nodes, because they are versioned)

            RendererModel = RenderExecutionModel.MaximallyParallel;
            m_RenderGraph = new RenderGraph(this);
            NodeSetID = ++s_NodeSetCounter;

            m_Batches = new VersionedList<InputBatch>(Allocator.Persistent, NodeSetID);
            m_GraphValues = new VersionedList<DataOutputValue>(Allocator.Persistent, NodeSetID);
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
            var def = LookupDefinition<TDefinition>();

            ValidatedHandle handle;
            {
                ref var node = ref AllocateData();
                node.TraitsIndex = def.Index;
                handle = node.Self;

                SetupNodeMemory(ref m_Traits[def.Index].Resolve(), ref node);

                m_Topology.GetRef(handle) = new TopologyIndex();
            }

            BlitList<ForwardedPort.Unchecked> forwardedConnections = default;
            var context = new InitContext(handle, def.Index, ref forwardedConnections);

            try
            {
                // To ensure consistency with Destroy(, false)
                m_Diff.NodeCreated(handle);

                def.Definition.Init(context);

                if (StillExists(handle) && forwardedConnections.IsCreated && forwardedConnections.Count > 0)
                    MergeForwardConnectionsToTable(ref GetNode(handle), forwardedConnections);

                SignalTopologyChanged();
            }
            catch
            {
                Debug.LogError("Throwing exceptions from constructors is undefined behaviour");
                Destroy(ref GetNode(handle), false);
                throw;
            }
            finally
            {
                if (forwardedConnections.IsCreated)
                    forwardedConnections.Dispose();
            }

            return new NodeHandle<TDefinition>(handle.VHandle);
        }

        /// <summary>
        /// Destroys a node, identified by the handle.
        /// This invokes <see cref="NodeDefinition.Destroy(NodeHandle)"/>
        /// if implemented.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// Thrown if the node is already destroyed or invalid.
        /// </exception>
        public void Destroy(NodeHandle handle)
        {
            Destroy(ref GetNode(Validate(handle)));
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
        {
            return
                handle.NodeSetID == NodeSetID &&
                handle.VHandle.Index < m_Nodes.Count &&
                handle.VHandle.Version == m_Nodes[handle.VHandle.Index].Self.VHandle.Version;
        }

        internal unsafe bool StillExists(ValidatedHandle handle)
        {
            return handle.VHandle.Version == m_Nodes.Ref(handle.VHandle.Index)->Self.VHandle.Version;
        }

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
                m_ManagedAllocators.Add(new ManagedMemoryAllocator());
            }

            var definition = m_NodeDefinitions[index];
            if (definition == s_InvalidDefinitionSlot)
            {
                // TODO: Instead of field injection, use constructor?
                LLTraitsHandle traitsHandle = new LLTraitsHandle();
                ManagedMemoryAllocator allocator = new ManagedMemoryAllocator();

                try
                {
                    definition = new TDefinition();
                    definition.BaseTraits.Set = definition.Set = this;
                    definition.GeneratePortDescriptions();
                    traitsHandle = definition.BaseTraits.CreateNodeTraits(typeof(TDefinition));

                    ref var traits = ref traitsHandle.Resolve();

                    if (traits.Storage.NodeDataIsManaged)
                        allocator = new ManagedMemoryAllocator(traits.Storage.NodeData.Size, traits.Storage.NodeData.Align);
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

        void Destroy(ref InternalNodeData node, bool callDestructor = true)
        {
            var index = node.TraitsIndex;

            if (callDestructor)
            {
                try
                {
                    m_NodeDefinitions[index].Destroy(node.Self.ToPublicHandle());
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

            unsafe
            {
                if (node.UserData != null)
                {
                    if (!m_Traits[index].Resolve().Storage.NodeDataIsManaged)
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

                node.UserData = null;
                node.KernelData = null;
            }

            m_Diff.NodeDeleted(node.Self, index);

            ValidatedHandle.Bump(ref node.Self);
            m_FreeNodes.Add(node.Self.VHandle.Index);
            SignalTopologyChanged();
        }

        ~NodeSet()
        {
            Debug.LogError("Leaked NodeSet - remember to call .Dispose() on it!");
            Dispose(false);
        }

        /// <summary>
        /// Cleans up the node set, and releasing any resources associated with it.
        /// </summary>
        /// <remarks>
        /// It's expected that the node set is completely cleaned up, i.e. no nodes
        /// exist in the set, together with any <see cref="GraphValue{T}"/>.
        /// </remarks>
        public void Dispose()
        {
            Dispose(true);
        }

        internal void Dispose(bool isDisposing)
        {
            if (m_IsDisposed)
                return;

            m_IsDisposed = true;

            if (isDisposing)
                // TODO: It seems to be ever-more unsafe to dispose through a GC Finalizer...
                GC.SuppressFinalize(this);

            unsafe
            {
                int leakedGraphValues = 0;

                for (int i = 0; i < m_GraphValues.UncheckedCount; ++i)
                {
                    if (m_GraphValues[i].Valid)
                    {
                        leakedGraphValues++;
                        DestroyValue(ref m_GraphValues[i]);
                    }
                }

                int leakedNodes = 0;
                for (int i = 0; i < m_Nodes.Count; ++i)
                {
                    if (m_Nodes[i].UserData != null)
                    {
                        Destroy(ref m_Nodes[i]);
                        leakedNodes++;
                    }
                }

                // Primarily used for diff'ing the RenderGraph
                // TODO: Assert/Test order of these statements - it's important that all handlers and definition is alive at this update statement
                UpdateInternal(m_LastJobifiedUpdateHandle);

                if (leakedNodes > 0 || leakedGraphValues > 0)
                {
                    Debug.LogError($"NodeSet leak warnings: {leakedNodes} leaked node(s) and {leakedGraphValues} leaked graph value(s) left!");
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

                PostRenderBatchProcess(true);

                m_Database.Dispose();

                m_Topology.Dispose();

                m_Nodes.Dispose();
                m_FreeNodes.Dispose();

                m_GraphValues.Dispose();

                m_PostRenderValues.Dispose();
                m_ReaderFences.Dispose();

                m_Diff.Dispose();
                m_RenderGraph.Dispose();

                m_Traits.Dispose();
                m_ManagedAllocators.Dispose();

                m_Batches.Dispose();
                m_ForwardingTable.Dispose();

                m_ArraySizes.Dispose();

                if (m_ActiveComponentTypes.IsCreated)
                    m_ActiveComponentTypes.Dispose();
            }

        }

        /// <summary>
        /// Looks up the node definition for this handle.
        /// <seealso cref="NodeDefinition"/>
        /// </summary>
        /// <exception cref="ArgumentException">
        /// Thrown if the node handle does not refer to a valid instance.
        /// </exception>
        public NodeDefinition GetDefinition(NodeHandle handle)
        {
            return GetDefinitionInternal(Validate(handle));
        }

        /// <summary>
        /// Looks up the specified node definition, creating it if it
        /// doesn't exist already.
        /// </summary>
        /// <exception cref="InvalidNodeDefinitionException">
        /// Thrown if the <typeparamref name="TDefinition"/> is not a valid
        /// node definition.
        /// </exception>
        public TDefinition GetDefinition<TDefinition>()
            where TDefinition : NodeDefinition, new()
        {
            return (TDefinition)LookupDefinition<TDefinition>().Definition;
        }

        /// <summary>
        /// Looks up the verified node definition for this handle.
        /// <seealso cref="NodeDefinition"/>
        /// </summary>
        /// <exception cref="ArgumentException">
        /// Thrown if the node handle does not refer to a valid instance.
        /// </exception>
        public TDefinition GetDefinition<TDefinition>(NodeHandle<TDefinition> handle)
            where TDefinition : NodeDefinition, new()
        {
            Validate(handle);

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
        /// <see cref="SetBufferSize{TDefinition, TType}(NodeHandle{TDefinition}, DataOutputBuffer{TDefinition, TType}, int)"/>
        /// for more information.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// If the <paramref name="handle"/> does not refer to a valid instance.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the request is invalid.
        /// </exception>
        public void SetBufferSize<TType>(NodeHandle handle, OutputPortID port, in TType requestedSize)
            where TType : struct
        {
            var source = new OutputPair(this, handle, port);

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

            SetBufferSizeWithCorrectlyTypedSizeParameter(source, portDescription, requestedSize);
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
        /// <exception cref="ArgumentException">
        /// If the <paramref name="handle"/> does not refer to a valid instance.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the request is invalid.
        /// </exception>
        public void SetBufferSize<TDefinition, TType>(NodeHandle<TDefinition> handle, DataOutput<TDefinition, TType> port, in TType requestedSize)
            where TDefinition : NodeDefinition
            where TType : struct
        {
            var source = new OutputPair(this, handle, port.Port);
            SetBufferSizeWithCorrectlyTypedSizeParameter(source, GetFormalPort(source), requestedSize);
        }

        unsafe void SetBufferSizeWithCorrectlyTypedSizeParameter<TType>(in OutputPair source, in PortDescription.OutputPort port, TType sizeRequest)
            where TType : struct
        {
            if (!port.HasBuffers)
                throw new InvalidOperationException("Cannot set size on a DataOutput port which contains no Buffer<T> instances in its type.");

            OutputPortID resolvedOutput = port;

            foreach (var bufferInfo in port.BufferInfos)
            {
                var requestedSizeBuf = (BufferDescription*)((byte*)Unsafe.AsPointer(ref sizeRequest) + bufferInfo.Offset);
                var requestedSize = requestedSizeBuf->GetSizeRequest();
                if (!requestedSize.IsValid)
                {
                    if (port.Type.IsConstructedGenericType && port.Type.GetGenericTypeDefinition() == typeof(Buffer<>))
                        throw new InvalidOperationException($"Expecting the return value of {port.Type}.SizeRequest().");
                    else
                        throw new InvalidOperationException($"Expecting the return value of Buffer<T>.SizeRequest() on individual fields of {port.Type} for sizes being set.");
                }
                if (requestedSize.Size >= 0)
                    m_Diff.NodeBufferResized(source, bufferInfo.Offset, requestedSize.Size, bufferInfo.ItemType);
            }
        }

        unsafe void SetupNodeMemory(ref LowLevelNodeTraits traits, ref InternalNodeData node)
        {
            if (!traits.Storage.NodeDataIsManaged)
            {
                node.UserData = Utility.CAlloc(traits.Storage.NodeData, Allocator.Persistent);
            }
            else
            {
                node.UserData = m_ManagedAllocators[node.TraitsIndex].Alloc();
            }

            node.KernelData = null;
            if (traits.HasKernelData)
            {
                node.KernelData = (RenderKernelFunction.BaseData*)Utility.CAlloc(traits.Storage.KernelData, Allocator.Persistent);
            }
        }

        unsafe internal ref InternalNodeData GetNodeChecked(NodeHandle handle) => ref *m_Nodes.Ref(Validate(handle).VHandle.Index);
        unsafe internal ref InternalNodeData GetNode(ValidatedHandle handle) => ref *m_Nodes.Ref(handle.VHandle.Index);
        internal NodeDefinition GetDefinitionInternal(ValidatedHandle handle) => m_NodeDefinitions[GetNode(handle).TraitsIndex];

        internal PortDescription.InputPort GetFormalPort(in InputPair destination) 
            => GetDefinitionInternal(destination.Handle).GetFormalInput(destination.Handle, destination.Port);
        internal PortDescription.OutputPort GetFormalPort(in OutputPair source)
            => GetDefinitionInternal(source.Handle).GetFormalOutput(source.Handle, source.Port);

        internal PortDescription.InputPort GetVirtualPort(in InputPair destination)
            => GetDefinitionInternal(destination.Handle).GetVirtualInput(destination.Handle, destination.Port);
        internal PortDescription.OutputPort GetVirtualPort(in OutputPair source)
            => GetDefinitionInternal(source.Handle).GetVirtualOutput(source.Handle, source.Port);

        internal unsafe ref TNode GetNodeData<TNode>(NodeHandle handle)
            where TNode : struct, INodeData
        {
            return ref Unsafe.AsRef<TNode>(GetNodeChecked(handle).UserData);
        }

        internal unsafe ref TKernel GetKernelData<TKernel>(NodeHandle handle)
            where TKernel : struct, IKernelData
        {
            return ref Unsafe.AsRef<TKernel>(GetNodeChecked(handle).KernelData);
        }

        internal ref InternalNodeData AllocateData()
        {
            if (m_FreeNodes.Count > 0)
            {
                var index = m_FreeNodes[m_FreeNodes.Count - 1];
                m_FreeNodes.PopBack();
                return ref m_Nodes[index];
            }

            var data = new InternalNodeData();
            data.Self = ValidatedHandle.Create(m_Nodes.Count, NodeSetID);
            data.TraitsIndex = InvalidTraitSlot;

            m_Nodes.Add(data);
            m_Topology.EnsureSize(m_Nodes.Count);

            return ref GetNode(data.Self);
        }

        /// <summary>
        /// Converts and checks that the handle is valid, meaning the handle is safely
        /// indexable into the <see cref="m_Nodes"/> array, and that the versions match.
        /// 
        /// A <see cref="ValidatedHandle"/> is the only way to use internal API.
        /// 
        /// Note that a <see cref="ValidatedHandle"/> may grow stale (mismatching versions),
        /// but it will always be safe to index with this handle (you may just get a stale / newer node).
        /// 
        /// If you need to check the handle refers to the same <see cref="InternalNodeData"/>,
        /// use <see cref="StillExists(ValidatedHandle)"/>
        /// </summary>
        internal ValidatedHandle Validate(NodeHandle handle)
        {
            return ValidatedHandle.CheckAndConvert(this, handle);
        }

        // For testing
        internal GraphDiff GetCurrentGraphDiff() => m_Diff;
        // For other API
        internal List<NodeDefinition> GetDefinitions() => m_NodeDefinitions;
        internal BlitList<LLTraitsHandle> GetLLTraits() => m_Traits;
        internal ref readonly LowLevelNodeTraits GetNodeTraits(NodeHandle handle) => ref m_Traits[GetNodeChecked(handle).TraitsIndex].Resolve();
        internal VersionedList<InputBatch> GetInputBatches() => m_Batches;

    }
}
