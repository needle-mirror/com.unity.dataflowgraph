using System;
using Unity.Collections;
using Unity.Jobs;

namespace Unity.DataFlowGraph
{
    /// <summary>
    /// A context provided to a node's <see cref="NodeDefinition.OnUpdate"/> implementation.
    /// </summary>
    public readonly partial struct UpdateContext
    {
        readonly CommonContext m_Ctx;

        /// <summary>
        /// A handle to the node being updated.
        /// </summary>
        public NodeHandle Handle => m_Ctx.Handle;

        /// <summary>
        /// The <see cref="NodeSetAPI"/> associated with this context.
        /// </summary>
        public NodeSetAPI Set => m_Ctx.Set;

        /// <summary>
        /// Conversion operator for common API shared with other contexts.
        /// </summary>
        public static implicit operator CommonContext(in UpdateContext ctx) => ctx.m_Ctx;

        /// <summary>
        /// Emit a message from yourself on a port. Everything connected to it
        /// will receive your message.
        /// </summary>
        public void EmitMessage<T, TNodeDefinition>(MessageOutput<TNodeDefinition, T> port, in T msg)
            where TNodeDefinition : NodeDefinition
        {
            Set.EmitMessage(InternalHandle, new OutputPortArrayID(port.Port), msg);
        }

        /// <summary>
        /// Emit a message from yourself on a port array. Everything connected to it
        /// will receive your message.
        /// </summary>
        /// <exception cref="IndexOutOfRangeException">Thrown if the index is out of range with respect to the port array.</exception>
        public void EmitMessage<T, TNodeDefinition>(PortArray<MessageOutput<TNodeDefinition, T>> port, int arrayIndex, in T msg)
            where TNodeDefinition : NodeDefinition
        {
            Set.EmitMessage(InternalHandle, new OutputPortArrayID(port.GetPortID(), arrayIndex), msg);
        }

        /// <summary>
        /// Updates the contents of <see cref="Buffer{T}"/>s appearing in this node's <see cref="IGraphKernel{TKernelData,TKernelPortDefinition}"/>.
        /// Pass an instance of the node's <see cref="IGraphKernel{TKernelData,TKernelPortDefinition}"/> as the <paramref name="requestedContents"/>
        /// parameter with <see cref="Buffer{T}"/> instances within it having been set using <see cref="UploadRequest"/>, or
        /// <see cref="Buffer{T}.SizeRequest(int)"/>.
        /// Any <see cref="Buffer{T}"/> instances within the given struct that have default values will be unaffected by the call.
        /// </summary>
        public void UpdateKernelBuffers<TGraphKernel>(in TGraphKernel kernel)
            where TGraphKernel : struct, IGraphKernel
        {
            Set.UpdateKernelBuffers(InternalHandle, kernel);
        }

        /// <summary>
        /// The return value should be used together with <see cref="UpdateKernelBuffers"/> to change the contents
        /// of a kernel buffer living on a <see cref="IGraphKernel{TKernelData, TKernelPortDefinition}"/>.
        /// </summary>
        /// <remarks>
        /// This will resize the affected buffer to the same size as <paramref name="inputMemory"/>.
        /// Failing to include the return value in a call to <see cref="UpdateKernelBuffers"/> is an error and will result in a memory leak.
        /// </remarks>
        public Buffer<T> UploadRequest<T>(NativeArray<T> inputMemory, BufferUploadMethod method = BufferUploadMethod.Copy)
            where T : struct
                => Set.UploadRequest(InternalHandle, inputMemory, method);

        /// <summary>
        /// Updates the associated <typeparamref name="TKernelData"/> asynchronously,
        /// to be available in a <see cref="IGraphKernel"/> in the next render.
        /// </summary>
        public void UpdateKernelData<TKernelData>(in TKernelData data)
            where TKernelData : struct, IKernelData
        {
            Set.UpdateKernelData(InternalHandle, data);
        }

        /// <summary>
        /// Registers <see cref="Handle"/> for regular updates every time <see cref="NodeSet.Update"/> is called.
        /// This only takes effect after the next <see cref="NodeSet.Update"/>.
        /// <seealso cref="IUpdate.Update(in UpdateContext)"/>
        /// <seealso cref="RemoveFromUpdate()"/>
        /// </summary>
        /// <remarks>
        /// A node will automatically be removed from the update list when it is destroyed.
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        /// Thrown if <see cref="Handle"/> is already registered for updating.
        /// </exception>
        public void RegisterForUpdate() => Set.RegisterForUpdate(InternalHandle);

        /// <summary>
        /// Deregisters <see cref="Handle"/> from updating every time <see cref="NodeSet.Update"/> is called.
        /// This only takes effect after the next <see cref="NodeSet.Update"/>.
        /// <seealso cref="RegisterForUpdate()"/>
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown if <see cref="Handle"/> is not registered for updating.
        /// </exception>
        public void RemoveFromUpdate() => Set.RemoveFromUpdate(InternalHandle);

        internal ValidatedHandle InternalHandle => m_Ctx.InternalHandle;

        internal UpdateContext(NodeSetAPI set, in ValidatedHandle handle)
        {
            m_Ctx = new CommonContext(set, handle);
        }
    }

    public partial class NodeSetAPI
    {
        internal readonly struct UpdateRequest
        {
            public readonly ValidatedHandle Handle;
            public bool IsRegistration => m_PreviousIndex == 0;
            public int PreviousUpdateIndex
            {
                get
                {
#if DFG_ASSERTIONS
                    if (IsRegistration)
                        throw new AssertionException("This is not an deregistration");
#endif
                    return m_PreviousIndex - 1;
                }
            }
            readonly int m_PreviousIndex;

            public static UpdateRequest Register(ValidatedHandle handle)
            {
                return new UpdateRequest(handle, 0);
            }

            public static UpdateRequest Unregister(ValidatedHandle handle, int previousUpdateIndex)
            {
#if DFG_ASSERTIONS
                if (previousUpdateIndex < 0)
                    throw new AssertionException("Invalid unregistration index");
#endif
                return new UpdateRequest(handle, previousUpdateIndex + 1);
            }

            UpdateRequest(ValidatedHandle handle, int previousUpdateIndex)
            {
                m_PreviousIndex = previousUpdateIndex;
                Handle = handle;
            }
        }

        internal enum UpdateState : int
        {
            InvalidUpdateIndex = 0,
            NotYetApplied = 1,
            ValidUpdateOffset
        }


        internal FreeList<ValidatedHandle> GetUpdateIndices() => m_UpdateIndices;
        internal BlitList<UpdateRequest> GetUpdateQueue() => m_UpdateRequestQueue;

        FreeList<ValidatedHandle> m_UpdateIndices = new FreeList<ValidatedHandle>(Allocator.Persistent);
        BlitList<UpdateRequest> m_UpdateRequestQueue = new BlitList<UpdateRequest>(0, Allocator.Persistent);

        internal void RegisterForUpdate(ValidatedHandle handle)
        {
            ref var nodeData = ref Nodes[handle];

            if (m_Traits[nodeData.TraitsIndex].Resolve().SimulationStorage.IsScaffolded)
                throw new InvalidNodeDefinitionException($"Old-style node definitions cannot register for updates");

            if (m_NodeDefinitions[nodeData.TraitsIndex].VirtualTable.UpdateHandler == null)
                throw new InvalidNodeDefinitionException($"Node definition does not implement {typeof(IUpdate)}");

            if (nodeData.UpdateIndex != (int)UpdateState.InvalidUpdateIndex)
                throw new InvalidOperationException($"Node {handle} is already registered for updating");

            m_UpdateRequestQueue.Add(UpdateRequest.Register(handle));
            // Use a sentinel value here so removal of pending registrations is OK
            nodeData.UpdateIndex = (int)UpdateState.NotYetApplied;
        }

        internal void RemoveFromUpdate(ValidatedHandle handle)
        {
            ref var nodeData = ref Nodes[handle];

            if (m_Traits[nodeData.TraitsIndex].Resolve().SimulationStorage.IsScaffolded)
                throw new InvalidNodeDefinitionException($"Old-style node definitions cannot be removed from updates");

            if (nodeData.UpdateIndex == (int)UpdateState.InvalidUpdateIndex)
                throw new InvalidOperationException($"Node {handle} is not registered for updating");

            m_UpdateRequestQueue.Add(UpdateRequest.Unregister(handle, nodeData.UpdateIndex));
            // reset update index to detect removal without (pending) registration
            nodeData.UpdateIndex = (int)UpdateState.InvalidUpdateIndex;
        }

        void PlayBackUpdateCommandQueue()
        {
            for (int i = 0; i < m_UpdateRequestQueue.Count; ++i)
            {
                var handle = m_UpdateRequestQueue[i].Handle;

                if (!Nodes.StillExists(handle))
                    continue;

                ref var nodeData = ref Nodes[handle];

                if (m_UpdateRequestQueue[i].IsRegistration)
                {
#if DFG_ASSERTIONS
                    if (nodeData.UpdateIndex >= (int)UpdateState.ValidUpdateOffset)
                        throw new AssertionException($"Node {nodeData.Handle} to be added is in inconsistent update state {nodeData.UpdateIndex}");
#endif
                    nodeData.UpdateIndex = m_UpdateIndices.Allocate();
                    m_UpdateIndices[nodeData.UpdateIndex] = nodeData.Handle;
                }
                else
                {
                    var indexToRemove = m_UpdateRequestQueue[i].PreviousUpdateIndex;
#if DFG_ASSERTIONS
                    switch (indexToRemove)
                    {
                        case (int)UpdateState.InvalidUpdateIndex:
                            // This is a bug, at the time of submitting deregistration the node in question wasn't registered
                            throw new AssertionException($"Node {nodeData.Handle} to be removed is not registered at all {nodeData.UpdateIndex}");
                        case (int)UpdateState.NotYetApplied:
                            // This case is for users adding and removing in the same simulation update, so a request is there
                            // but not actually yet applied (at the time of registration)
                            // Bug if the node to be removed, at this point in time, does not actually have a valid update index
                            if (nodeData.UpdateIndex < (int)UpdateState.ValidUpdateOffset)
                                throw new AssertionException($"Node {nodeData.Handle} to be removed is not properly registered {nodeData.UpdateIndex}");

                            // This is for a case where the list is inconsistent (we can't use index from command - see below - as it doesn't yet exist).
                            if (m_UpdateIndices[nodeData.UpdateIndex] != nodeData.Handle)
                                throw new AssertionException($"Node {nodeData.Handle} corrupted the update list {nodeData.UpdateIndex}");

                            break;
                        default:
                            // This is for a case where the list is inconsistent given an properly registered node
                            if (m_UpdateIndices[indexToRemove] != nodeData.Handle)
                                throw new AssertionException($"Properly registered node {nodeData.Handle} corrupted the update list {nodeData.UpdateIndex}");
                            break;
                    }
#endif

                    // For deregistering partially registered nodes, we use the index from the current state.
                    // This index will be coherent since the registration is now complete.
                    if (indexToRemove == (int)UpdateState.NotYetApplied)
                        indexToRemove = nodeData.UpdateIndex;

                    m_UpdateIndices[indexToRemove] = default;
                    m_UpdateIndices.Release(indexToRemove);

                    nodeData.UpdateIndex = (int)UpdateState.InvalidUpdateIndex;
                }
            }

            m_UpdateRequestQueue.Clear();
        }

        void CheckForUserMemoryLeaks()
        {
            for(int i = 0; i < m_PendingBufferUploads.Count; ++i)
            {
                ref readonly var request = ref m_PendingBufferUploads[i];
                Debug.LogWarning(
                    $"Node {request.OwnerNode} requested a memory upload of size {request.Size} " +
                    $"that was not committed through {nameof(InitContext.UploadRequest)} " +
                    $"in the same {nameof(Update)} - this is potentially a memory leak"
                );
            }
        }

        protected void UpdateInternal(JobHandle inputDependencies)
        {
            CollectTestExceptions();

            m_FenceOutputConsumerProfilerMarker.Begin();
            FenceOutputConsumers();
            m_FenceOutputConsumerProfilerMarker.End();

            m_SimulateProfilerMarker.Begin();

            // Old style node defs
            for (int i = VersionedList<InternalNodeData>.ValidOffset; i < Nodes.UnvalidatedCount; ++i)
            {
                ref var node = ref Nodes.UnvalidatedItemAt(i);

                if (node.Valid && m_Traits[node.TraitsIndex].Resolve().SimulationStorage.IsScaffolded)
                    m_NodeDefinitions[node.TraitsIndex].UpdateInternal(new UpdateContext(this, node.Handle));
            }

            // new-style node definition updates
            for (int i = (int)UpdateState.ValidUpdateOffset; i < m_UpdateIndices.UncheckedCount; ++i)
            {
                var handle = m_UpdateIndices[i];

                if (!Nodes.StillExists(handle))
                    continue;

                ref var node = ref Nodes[handle];

                m_NodeDefinitions[node.TraitsIndex].UpdateInternal(new UpdateContext(this, node.Handle));
            }

            m_SimulateProfilerMarker.End();

            m_CopyWorldsProfilerMarker.Begin();
            m_RenderGraph.CopyWorlds(m_Diff, inputDependencies, InternalRendererModel, m_GraphValues);
            m_Diff = new GraphDiff(Allocator.Persistent); // TODO: Could be temp?
            m_CopyWorldsProfilerMarker.End();


            m_SwapGraphValuesProfilerMarker.Begin();
            SwapGraphValues();
            PlayBackUpdateCommandQueue();
            CheckForUserMemoryLeaks();
            m_SwapGraphValuesProfilerMarker.End();
        }
    }

    /// <summary>
    /// Interface for receiving update calls on <see cref="INodeData"/>,
    /// issued once for every call to <see cref="NodeSet.Update"/> if the
    /// implementing node in question has registered itself for updating
    /// - <see cref="MessageContext.RegisterForUpdate"/>,
    /// <see cref="UpdateContext.RegisterForUpdate"/> or
    /// <see cref="InitContext.RegisterForUpdate"/>.
    ///
    /// Note that there is *NO* implicit nor explicit ordering between
    /// nodes' <see cref="Update"/>, in addition it is not stable either.
    ///
    /// If you need updates to occur in topological order (trickled downstream) in simulation,
    /// you should emit a message downstream that other nodes react to through
    /// connections.
    ///
    /// This supersedes <see cref="NodeDefinition.OnUpdate(UpdateContext)"/>
    /// </summary>
    public interface IUpdate
    {
        /// <summary>
        /// Update function.
        /// <seealso cref="IUpdate"/>.
        /// <seealso cref="NodeDefinition.OnUpdate(in UpdateContext)"/>
        /// </summary>
        /// <remarks>
        /// It is undefined behaviour to throw an exception from this method.
        /// </remarks>
        void Update(in UpdateContext context);
    }
}
