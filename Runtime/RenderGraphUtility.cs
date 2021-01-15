using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;

namespace Unity.DataFlowGraph
{
    using Topology = TopologyAPI<ValidatedHandle, InputPortArrayID, OutputPortArrayID>;

    partial class RenderGraph
    {
        public int RenderVersion { get; private set; }

        /// <summary>
        /// Computed on first request for this frame.
        /// Depends on topology computation, but no sync is done
        /// - exception will be thrown if you don't have access though.
        /// </summary>
        unsafe public JobHandle RootFence
        {
            get
            {
                if (m_ComputedRootFenceVersion == RenderVersion)
                    return m_ComputedRootFence;

                switch (m_Model)
                {
                    case NodeSet.RenderExecutionModel.MaximallyParallel:

                        using (var tempHandles = new NativeList<JobHandle>(Cache.ComputeNumRoots(), Allocator.Temp))
                        {
                            for(int g = 0; g < Cache.Groups.Length; ++g)
                            {
                                foreach (var nodeCache in new Topology.RootCacheWalker(Cache.Groups[g]))
                                {
                                    ref var node = ref m_Nodes[nodeCache.Vertex.Versioned.Index];
                                    tempHandles.Add(node.Fence);
                                }
                            }
                            
                            m_ComputedRootFence = JobHandleUnsafeUtility.CombineDependencies(
                                (JobHandle*)tempHandles.GetUnsafePtr(),
                                tempHandles.Length
                            );
                        }
                        break;

                    case NodeSet.RenderExecutionModel.SingleThreaded:
                    case NodeSet.RenderExecutionModel.Islands:

                        m_ComputedRootFence = JobHandleUnsafeUtility.CombineDependencies(
                            (JobHandle*)m_IslandFences.GetUnsafePtr(),
                            m_IslandFences.Length
                        );

                        break;
                }

                m_ComputedRootFenceVersion = RenderVersion;

                // TODO: For empty graphs & maximally parallel, computed fence is empty and doesn't have external dependencies as per usual
                m_ComputedRootFence = JobHandle.CombineDependencies(m_ComputedRootFence, m_ExternalDependencies);

                return m_ComputedRootFence;
            }
        }

        JobHandle m_ComputedRootFence;
        /// <summary>
        /// Dependencies that chained to external jobs.
        /// Avoid fencing these if possible, except in the 
        /// next frame.
        /// </summary>
        JobHandle m_ExternalDependencies;
        int m_ComputedRootFenceVersion = -1;

        public unsafe (GraphValueResolver Resolver, JobHandle Dependency) CombineAndProtectDependencies(NativeList<DataOutputValue> valuesToProtect)
        {
            var finalHandle = ComputeDependency(valuesToProtect);

            finalHandle = AtomicSafetyManager.MarkScopeAsWrittenTo(finalHandle, m_BufferScope);

            GraphValueResolver resolver;

            resolver.Manager = m_SharedData.SafetyManager;
            resolver.Values = valuesToProtect;
            resolver.ReadBuffersScope = m_BufferScope;
            resolver.KernelNodes = m_Nodes;

            return (resolver, finalHandle);
        }

        internal JobHandle ComputeDependency(in DataOutputValue value)
        {
            switch (m_Model)
            {
                case NodeSet.RenderExecutionModel.MaximallyParallel:
                {
                    if (!StillExists(ref m_Nodes, value.Source))
                        break;

                    return m_Nodes[value.Source.Versioned.Index].Fence;
                }

                case NodeSet.RenderExecutionModel.Islands:
                case NodeSet.RenderExecutionModel.SingleThreaded:

                    if (m_IslandFences.Length == 0)
                        break;

                    return m_IslandFences[0];
            }

            return default;
        }

        unsafe JobHandle ComputeDependency(NativeList<DataOutputValue> values)
        {
            switch (m_Model)
            {
                case NodeSet.RenderExecutionModel.MaximallyParallel:
                {
                    var fences = new NativeArray<JobHandle>(values.Length, Allocator.Temp, NativeArrayOptions.ClearMemory);

                    try
                    {
                        // TODO: Impossible to implement without synchronising against scheduling dependencies (see comment on RootFence as well).....
                        // Also potentially includes more dependencies than you want (for stale graph values)
                        for(int i = 0; i < values.Length; ++i)
                        {
                            if (!StillExists(ref m_Nodes, values[i].Source))
                                continue;

                            fences[i] = m_Nodes[values[i].Source.Versioned.Index].Fence;
                        }

                        return JobHandleUnsafeUtility.CombineDependencies((JobHandle*)fences.GetUnsafeReadOnlyPtr(), fences.Length);

                    }
                    finally
                    {
                        fences.Dispose();
                    }
                }

                case NodeSet.RenderExecutionModel.Islands:
                case NodeSet.RenderExecutionModel.SingleThreaded:

                    if (m_IslandFences.Length == 0)
                        break;

                    return m_IslandFences[0];
            }

            return default;
        }

        static Topology.SortingAlgorithm AlgorithmFromModel(NodeSet.RenderExecutionModel model)
        {
            switch (model)
            {
                case NodeSet.RenderExecutionModel.Islands:
                    return Topology.SortingAlgorithm.LocalDepthFirst;
                default:
                    return Topology.SortingAlgorithm.GlobalBreadthFirst;
            }
        }

        void CompleteGraph()
        {
            RootFence.Complete();

#if DFG_ASSERTIONS

            // Assure all nodes have been fenced. Culling could be expressed as inserting defaulted job handles into downstream
            // nodes, which could cause midstream nodes to be unfenced due to RootFence only combining roots.

            for (int i = 0; i < Cache.Groups.Length; ++i)
            {
                foreach (var nodeCache in new Topology.GroupWalker(Cache.Groups[i]))
                {
                    var index = nodeCache.Vertex.Versioned.Index;
                    ref var node = ref m_Nodes[index];

                    if (!node.Fence.IsCompleted)
                        throw new AssertionException($"Unfenced node {nodeCache.Vertex.ToPublicHandle()}");
                }
            }
#endif
        }

        public static EntityQuery CreateNodeSetAttachmentQuery(ComponentSystemBase system)
        {
            return system.GetEntityQuery(ComponentType.ReadOnly<NodeSetAttachment>());
        }
    }
}
