using System;
using System.ComponentModel;
using Unity.Collections;
using Unity.Jobs;

namespace Unity.DataFlowGraph
{
    /// <summary>
    /// A context provided to a node's <see cref="NodeDefinition.OnUpdate"/> implementation.
    /// </summary>
    public readonly struct UpdateContext : Detail.IContext<UpdateContext>
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
                    $"that was not committed through {nameof(CommonContextAPI.UploadRequest)} " +
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
            m_RenderGraph.CopyWorlds(m_Diff, inputDependencies, InternalRendererModel, InternalRendererOptimizations);
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
        void Update(UpdateContext ctx);
    }
}
