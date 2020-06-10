using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;

namespace Unity.DataFlowGraph
{
    using Topology = TopologyAPI<ValidatedHandle, InputPortArrayID, OutputPortID>;

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
                                    ref var node = ref m_Nodes[nodeCache.Vertex.VHandle.Index];
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
            var temp = new NativeArray<JobHandle>(valuesToProtect.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            try
            {
                for (int i = 0; i < valuesToProtect.Length; ++i)
                {
                    temp[i] = valuesToProtect[i].Dependency;
                }

                var finalHandle = JobHandleUnsafeUtility.CombineDependencies((JobHandle*)temp.GetUnsafePtr(), valuesToProtect.Length);

                finalHandle = AtomicSafetyManager.MarkScopeAsWrittenTo(finalHandle, m_BufferScope);

                GraphValueResolver resolver;

                resolver.Manager = m_SharedData.SafetyManager;
                resolver.Values = valuesToProtect;
                resolver.ReadBuffersScope = m_BufferScope;
                resolver.KernelNodes = m_Nodes;

                return (resolver, finalHandle);
            }
            finally
            {
                temp.Dispose();
            }

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

        public static EntityQuery NodeSetAttachmentQuery(ComponentSystemBase system)
        {
            return system.GetEntityQuery(ComponentType.ReadOnly<NodeSetAttachment>());
        }
    }
}
