using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
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
    /// <seealso cref="Connect(NodeHandle, OutputPortID, NodeHandle, InputPortID)"/>
    /// <seealso cref="Update"/>
    /// </summary>
    public partial class NodeSet
    {
        internal RenderGraph DataGraph => m_RenderGraph;

        static InvalidFunctionalitySlot m_InvalidFunctionalitySlot = new InvalidFunctionalitySlot();

        List<INodeDefinition> m_NodeFunctionalities = new List<INodeDefinition>();

        // IWBN if nodes+topologies were in a SOA array, they follow exactly the
        // same array structure
        BlitList<InternalNodeData> m_Nodes = new BlitList<InternalNodeData>(0);
        BlitList<int> m_FreeNodes = new BlitList<int>(0);

        BlitList<LLTraitsHandle> m_Traits = new BlitList<LLTraitsHandle>(0);
        BlitList<ManagedMemoryAllocator> m_ManagedAllocators = new BlitList<ManagedMemoryAllocator>(0);

        GraphDiff m_Diff = new GraphDiff(Allocator.Persistent);

        RenderGraph m_RenderGraph;

        bool m_IsDisposed;

        /// <summary>
        /// Construct a node set. Remember to dispose it.
        /// <seealso cref="Dispose"/>
        /// </summary>
        public NodeSet()
        {
            m_NodeFunctionalities.Add(m_InvalidFunctionalitySlot);

            var defaultTraits = LLTraitsHandle.Create();
            // stuff that needs the first slot to be invalid
            defaultTraits.Resolve() = new LowLevelNodeTraits();
            m_Traits.Add(defaultTraits);
            m_ForwardingTable.Add(new ForwardedPort());
            m_ArraySizes.Allocate();
            m_ManagedAllocators.Add(new ManagedMemoryAllocator());
            // (we don't need a zeroth invalid index for nodes, because they are versioned)

            RendererModel = RenderExecutionModel.MaximallyParallel;
            m_RenderGraph = new RenderGraph(this);
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
            where TDefinition : INodeDefinition, new()
        {
            var def = LookupDefinition<TDefinition>();

            NodeHandle handle;
            {
                ref var node = ref AllocateData();
                node.TraitsIndex = def.Index;
                handle = new NodeHandle<TDefinition>(node.VHandle);

                SetupNodeMemory(ref m_Traits[def.Index].Resolve(), ref node);
                // TODO: Doesn't look right.
                SetupInitialTopology(def.Functionality, handle, ref m_Topology.Indexes[node.VHandle.Index]);
            }


            BlitList<ForwardedPort> forwardedConnections = default;
            var context = new InitContext(handle, def.Index, ref forwardedConnections);

            try
            {
                // To ensure consistency with Destroy(, false)
                m_Diff.NodeCreated(handle);

                def.Functionality.Init(context);

                if (forwardedConnections.IsCreated && forwardedConnections.Count > 0)
                    MergeForwardConnectionsToTable(ref m_Nodes[handle.VHandle.Index], forwardedConnections);

                SignalTopologyChanged();
            }
            catch
            {
                Debug.LogError("Throwing exceptions from constructors is undefined behaviour");
                Destroy(ref m_Nodes[handle.VHandle.Index], false);
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
        /// This invokes <see cref="INodeFunctionality.Destroy(NodeHandle)"/>
        /// if implemented.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// Thrown if the node is already destroyed or invalid.
        /// </exception>
        public void Destroy(NodeHandle handle)
        {
            NodeVersionCheck(handle.VHandle);
            Destroy(ref m_Nodes[handle.VHandle.Index]);
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
            return handle.VHandle.Index < m_Nodes.Count && handle.VHandle.Version == m_Nodes[handle.VHandle.Index].VHandle.Version;
        }

        internal (INodeDefinition Functionality, int Index) LookupDefinition<TDefinition>()
            where TDefinition : INodeDefinition, new()
        {
            var index = NodeDefinitionTypeIndex<TDefinition>.Index;

            // fill "sparse" table with the invalid functionality.
            while (index >= m_NodeFunctionalities.Count)
            {
                m_NodeFunctionalities.Add(m_InvalidFunctionalitySlot);
                // TODO: We can, instead of wasting allocations, just make .Resolve() throw errors.
                m_Traits.Add(LLTraitsHandle.Create());
                m_ManagedAllocators.Add(new ManagedMemoryAllocator());
            }

            var functionality = m_NodeFunctionalities[index];
            if (functionality == m_InvalidFunctionalitySlot)
            {
                // TODO: Instead of field injection, use constructor?
                LLTraitsHandle traitsHandle = new LLTraitsHandle();
                ManagedMemoryAllocator allocator = new ManagedMemoryAllocator();

                try
                {
                    functionality = new TDefinition();
                    functionality.BaseTraits.Set = functionality.Set = this;
                    functionality.GeneratePortDescriptions();
                    traitsHandle = functionality.BaseTraits.CreateNodeTraits(typeof(TDefinition));

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

                    functionality.Dispose();
                    throw;
                }

                m_NodeFunctionalities[index] = functionality;
                m_Traits[index] = traitsHandle;
                m_ManagedAllocators[index] = allocator;
            }

            return (functionality, index);
        }

        void Destroy(ref InternalNodeData node, bool callDestructor = true)
        {

            var index = m_Nodes[node.VHandle.Index].TraitsIndex;

            if (callDestructor)
            {
                try
                {
                    m_NodeFunctionalities[index].Destroy(new NodeHandle(node.VHandle));
                }
                catch (Exception e)
                {
                    // Nodes should never throw from destructors.
                    // Can't really propagate exception up: We may be inside the finalizer, completely smashing the clean up, or destroying a range of nodes which is atomic.
                    // Let the user know about it.
                    Debug.LogError($"Undefined behaviour when throwing exceptions from destructors (node type: {m_NodeFunctionalities[index].GetType()})");
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

            m_Diff.NodeDeleted(new NodeHandle(node.VHandle), index);

            node.VHandle.Version++;
            m_FreeNodes.Add(node.VHandle.Index);
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
                var leakedConnections = m_Topology.Connections.Count(c => c.Valid);

                int leakedGraphValues = 0;

                for (int i = 0; i < m_GraphValues.Count; ++i)
                {
                    if (m_GraphValues[i].IsAlive)
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

                // At this point, the forwarding table should be empty - meaning the free table is as large
                // as the forwarding table, except for the first, invalid index.
                int badForwardedPorts = (m_ForwardingTable.Count - 1) - m_FreeForwardingTables.Count;

                // After all nodes are destroyed, there should be no more array size entries.
                int badArraySizes = m_ArraySizes.InUse - 1;

                // Primarily used for diff'ing the RenderGraph
                // TODO: Assert/Test order of these statements - it's important that all handlers and functionality is alive at this update statement
                Update();

                // if any connections are left are recorded as valid, the topology database is corrupted
                // (since destroying a node removes all its connections, thus all connections should be gone
                // after destroying all nodes)
                var badConnections = m_Topology.Connections.Count(c => c.Valid);

                if (leakedConnections > 0 || leakedNodes > 0 || badConnections > 0 || leakedGraphValues > 0 || badForwardedPorts > 0 || badArraySizes > 0)
                {
                    Debug.LogError($"NodeSet leak warnings: " +
                        $"{leakedConnections} leaked connection(s), " +
                        $"{badForwardedPorts} corrupted forward definition(s), " +
                        $"{badArraySizes} dangling array size entry(s), " +
                        $"{leakedNodes} leaked node(s), " +
                        $"{leakedGraphValues} leaked graph value(s) and " +
                        $"{badConnections} corrupted connections left!"
                    );
                }

                foreach (var handler in m_ConnectionHandlerMap)
                {
                    handler.Value.Dispose();
                }

                foreach (var functionality in m_NodeFunctionalities)
                {
                    if (functionality != m_InvalidFunctionalitySlot)
                        functionality.Dispose();
                }

                for (int i = 0; i < m_Traits.Count; ++i)
                    if (m_Traits[i].IsCreated)
                        m_Traits[i].Dispose();

                for (int i = 0; i < m_ManagedAllocators.Count; ++i)
                    if (m_ManagedAllocators[i].IsCreated)
                        m_ManagedAllocators[i].Dispose();

                PostRenderBatchProcess(true);

                m_Topology.Dispose();
                m_Nodes.Dispose();
                m_FreeNodes.Dispose();

                m_GraphValues.Dispose();
                m_FreeGraphValues.Dispose();

                m_PostRenderValues.Dispose();
                m_ReaderFences.Dispose();

                m_Diff.Dispose();
                m_RenderGraph.Dispose();

                m_Traits.Dispose();
                m_ManagedAllocators.Dispose();

                m_Batches.Dispose();
                m_FreeForwardingTables.Dispose();
                m_ForwardingTable.Dispose();

                m_ArraySizes.Dispose();
            }

        }

        /// <summary>
        /// Looks up the functionality interface for this handle.
        /// <seealso cref="INodeFunctionality"/>
        /// </summary>
        /// <exception cref="ArgumentException">
        /// Thrown if the node handle does not refer to a valid instance.
        /// </exception>
        public INodeFunctionality GetFunctionality(NodeHandle handle)
        {
            NodeVersionCheck(handle.VHandle);

            var index = m_Nodes[handle.VHandle.Index].TraitsIndex;
            return m_NodeFunctionalities[index];
        }

        /// <summary>
        /// Looks up the specified functionality interface, creating it if it
        /// doesn't exist already.
        /// </summary>
        /// <exception cref="InvalidNodeDefinitionException">
        /// Thrown if the <typeparamref name="TDefinition"/> is not a valid
        /// node definition.
        /// </exception>
        public TDefinition GetFunctionality<TDefinition>()
            where TDefinition : INodeDefinition, new()
        {
            return (TDefinition)LookupDefinition<TDefinition>().Functionality;
        }

        /// <summary>
        /// Looks up the verified functionality interface for this handle.
        /// <seealso cref="INodeFunctionality"/>
        /// </summary>
        /// <exception cref="ArgumentException">
        /// Thrown if the node handle does not refer to a valid instance.
        /// </exception>
        public TDefinition GetFunctionality<TDefinition>(NodeHandle<TDefinition> handle)
            where TDefinition : INodeDefinition, new()
        {
            NodeVersionCheck(((NodeHandle)handle).VHandle);

            return (TDefinition)LookupDefinition<TDefinition>().Functionality;
        }

        /// <summary>
        /// Tests whether the node instance referred to by the <paramref name="handle"/>
        /// is a <typeparamref name="TDefinition"/>.
        /// </summary>
        public bool Is<TDefinition>(NodeHandle handle)
            where TDefinition : INodeDefinition
        {
            NodeVersionCheck(handle.VHandle);

            var traitsIndex = m_Nodes[handle.VHandle.Index].TraitsIndex;

            return m_NodeFunctionalities[traitsIndex] is TDefinition;
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
            where TDefinition : INodeDefinition
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
            where TDefinition : INodeDefinition
        {
            if (!Is<TDefinition>(handle))
                throw new InvalidCastException();

            return new NodeHandle<TDefinition>(handle.VHandle);
        }

        /// <summary>
        /// Updates the node set in two phases:
        /// 
        /// 1. A message phase (simulation) where nodes are updated and messages
        /// are passed around
        /// 2. Aligning the simulation world and the rendering world and initiate
        /// the rendering.
        /// 
        /// <seealso cref="RenderExecutionModel"/>
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Can be throw if invalid or missing dependencies were added through 
        /// <see cref="InjectDependencyFromConsumer(Jobs.JobHandle)"/>.
        /// </exception>
        public void Update()
        {
            Profiler.BeginSample("NodeSet.FenceOutputConsumers");
            FenceOutputConsumers();
            Profiler.EndSample();

            Profiler.BeginSample("NodeSet.Simulate");
            // FIXME: Make this topologically ordered.
            foreach (var node in m_Nodes)
                if (node.IsCreated) // TODO: Test that we don't update dead nodes.
                    m_NodeFunctionalities[node.TraitsIndex].OnUpdate(new NodeHandle(node.VHandle));
            Profiler.EndSample();

            Profiler.BeginSample("NodeSet.CopyWorlds");
            m_RenderGraph.CopyWorlds(m_Diff, RendererModel, m_GraphValues, m_Batches);
            PostRenderBatchProcess();
            m_Diff = new GraphDiff(Allocator.Persistent); // TODO: Could be temp?
            Profiler.EndSample();

            Profiler.BeginSample("NodeSet.SwapGraphValues");
            SwapGraphValues();
            Profiler.EndSample();
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
            var vHandle = ((NodeHandle)handle).VHandle;
            NodeVersionCheck(vHandle);

            var ports = m_NodeFunctionalities[m_Nodes[vHandle.Index].TraitsIndex].GetPortDescription(handle);

            if (ports.Outputs[port.Port].PortUsage != Usage.Data)
                throw new InvalidOperationException("Cannot set size on a non DataOutput port");

            if (ports.Outputs[port.Port].Type != typeof(TType))
            {
                if (ports.Outputs[port.Port].Type.IsConstructedGenericType && ports.Outputs[port.Port].Type.GetGenericTypeDefinition() == typeof(Buffer<>))
                    throw new InvalidOperationException($"Expecting the return value of {ports.Outputs[port.Port].Type}.SizeRequest().");
                else
                    throw new InvalidOperationException($"Expecting an instance of {ports.Outputs[port.Port].Type}");
            }

            SetBufferSizeWithCorrectlyTypedSizeParameter(handle, ports.Outputs[port.Port], requestedSize);
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
            where TDefinition : INodeDefinition
            where TType : struct
        {
            NodeVersionCheck(handle.VHandle);

            var ports = m_NodeFunctionalities[m_Nodes[handle.VHandle.Index].TraitsIndex].GetPortDescription(handle);
            SetBufferSizeWithCorrectlyTypedSizeParameter(handle, ports.Outputs[port.Port.Port], requestedSize);
        }

        unsafe void SetBufferSizeWithCorrectlyTypedSizeParameter<TType>(NodeHandle handle, in PortDescription.OutputPort port, TType sizeRequest)
            where TType : struct
        {
            if (!port.HasBuffers)
                throw new InvalidOperationException("Cannot set size on a DataOutput port which contains no Buffer<T> instances in its type.");

            OutputPortID resolvedOutput = port;
            ResolvePublicSource(ref handle, ref resolvedOutput);

            foreach (var bufferInfo in port.BufferInfos)
            {
                ref var requestedSizeBuf = ref Unsafe.AsRef<Buffer<byte>>((byte*)Unsafe.AsPointer(ref sizeRequest) + bufferInfo.Offset);
                var requestedSize = requestedSizeBuf.GetSizeRequest();
                if (!requestedSize.IsValid)
                {
                    if (port.Type.IsConstructedGenericType && port.Type.GetGenericTypeDefinition() == typeof(Buffer<>))
                        throw new InvalidOperationException($"Expecting the return value of {port.Type}.SizeRequest().");
                    else
                        throw new InvalidOperationException($"Expecting the return value of Buffer<T>.SizeRequest() on individual fields of {port.Type} for sizes being set.");
                }
                if (requestedSize.Size >= 0)
                    m_Diff.NodeBufferResized(handle, resolvedOutput, bufferInfo.Offset, requestedSize.Size, bufferInfo.ItemType);
            }
        }

        internal void SetupInitialTopology(INodeFunctionality functionality, NodeHandle handle, ref TopologyIndex topology)
        {
            topology.InputHeadConnection = 0;
            topology.OutputHeadConnection = 0;

            // TODO: Seems dangerous, node isn't really constructed at this point...
            var ports = functionality.GetPortDescription(handle);

            topology.InputPortCount = (ushort)ports.Inputs.Count;
            topology.OutputPortCount = (ushort)ports.Outputs.Count;
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

        internal unsafe ref TNode GetNodeData<TNode>(NodeHandle handle)
            where TNode : struct, INodeData
        {
            NodeVersionCheck(handle.VHandle);

            return ref Unsafe.AsRef<TNode>(m_Nodes[handle.VHandle.Index].UserData);
        }

        internal unsafe ref TKernel GetKernelData<TKernel>(NodeHandle handle)
            where TKernel : struct, IKernelData
        {
            NodeVersionCheck(handle.VHandle);
            return ref Unsafe.AsRef<TKernel>(m_Nodes[handle.VHandle.Index].KernelData);
        }

        internal ref InternalNodeData AllocateData()
        {
            if (m_FreeNodes.Count > 0)
            {
                var index = m_FreeNodes[m_FreeNodes.Count - 1];
                m_FreeNodes.PopBack();
                return ref m_Nodes[index];
            }

            InternalNodeData data = new InternalNodeData();
            data.VHandle.Index = m_Nodes.Count;
            data.VHandle.Version = 1;
            data.TraitsIndex = 0; // (will index into invalid class)

            m_Nodes.Add(data);
            m_Topology.EnsureSize(m_Nodes.Count);

            return ref m_Nodes[data.VHandle.Index];
        }

        internal void NodeVersionCheck(VersionedHandle handle)
        {
            // TODO: Implement in terms of Exists()
            if (handle.Index >= m_Nodes.Count || handle.Version != m_Nodes[handle.Index].VHandle.Version)
            {
                throw new ArgumentException("Node is disposed or invalid");
            }
        }

        // For testing
        internal GraphDiff GetCurrentGraphDiff() => m_Diff;
        // For other API
        internal List<INodeDefinition> GetDefinitions() => m_NodeFunctionalities;
        internal BlitList<LLTraitsHandle> GetLLTraits() => m_Traits;
        internal VersionedList<InputBatch> GetInputBatches() => m_Batches;

    }
}
