using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;

namespace Unity.DataFlowGraph
{

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
                    case RenderExecutionModel.MaximallyParallel:

                        var rootCacheWalker = new RootCacheWalker(m_Cache);

                        using (var tempHandles = new NativeList<JobHandle>(rootCacheWalker.Count, Allocator.Temp))
                        {
                            foreach (var nodeCache in rootCacheWalker)
                            {
                                ref var node = ref m_Nodes[nodeCache.Handle.VHandle.Index];
                                tempHandles.Add(node.Fence);
                            }

                            m_ComputedRootFence = JobHandleUnsafeUtility.CombineDependencies(
                                (JobHandle*)tempHandles.GetUnsafePtr(),
                                tempHandles.Length
                            );
                        }
                        break;

                    case RenderExecutionModel.SingleThreaded:
                    case RenderExecutionModel.Islands:

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
    }
}
