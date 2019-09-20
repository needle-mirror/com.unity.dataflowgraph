using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace Unity.DataFlowGraph
{
    unsafe struct TopologyComputationContext : IDisposable
    {
        public struct NodeVisitCache
        {
            public int visitCount;
            public int parentCount;
            public int TraversalIndex;
            public int CurrentlyResolving;
        }

        public struct OrderedNodeHandle
        {
            public NodeHandle Node;
            public int Index;
        }

        public unsafe struct NodeArraySource
        {
            public NodeArraySource(NativeArray<NodeHandle> nodes)
            {
                Source = (NodeHandle*)nodes.GetUnsafeReadOnlyPtr();
                Count = nodes.Length;
            }

            public NodeArraySource(NativeList<NodeHandle> nodes)
            {
                Source = (NodeHandle*)nodes.GetUnsafePtr();
                Count = nodes.Length;
            }

            public NodeArraySource(BlitList<NodeHandle> nodes)
            {
                Source = (NodeHandle*)nodes.Pointer;
                Count = nodes.Count;
            }

            internal readonly NodeHandle* Source;
            internal readonly int Count;
        }

        public bool IsCreated => Nodes != null;
        /// <summary>
        /// 1:1 with # of nodes and matches those
        /// </summary>
        public BlitList<TopologyIndex> Topologies;
        /// <summary>
        /// 1:1 to connection table in set
        /// </summary>
        [ReadOnly]
        public BlitList<Connection> Connections;

        /// <summary>
        /// Input nodes to calculate topology system for.
        /// Is a pointer so we can support any kind of input 
        /// array.
        /// 
        /// Number of items is in <see cref="Count"/>
        /// </summary>
        /// <remarks>
        /// TODO: change to System.Span{T}
        /// </remarks>
        [NativeDisableUnsafePtrRestriction]
        public NodeHandle* Nodes;
        public int Count;

        public MutableTopologyCache Cache;

        public NativeArray<NodeVisitCache> VisitCache;
        public NativeList<int> VisitCacheLeafIndicies;

        public RenderExecutionModel Model;

        [BurstCompile]
        struct PrepareJob : IJob
        {
            public BlitList<Connection> SourceConnections;
            public BlitList<TopologyIndex> SourceTopologies;

            public TopologyComputationContext Context;
            public TopologyCacheAPI.VersionTracker Version;

            public void Execute()
            {
                // TODO: Make a system that doesn't need to allocate arrays for no-op
                // topology jobs.
                if (TopologyCacheAPI.IsCacheFresh(Version, Context.Cache))
                    return;
                // TODO: Figure out a better system than just copying everything
                // ^ Edit: Or don't, and properly double-buffer these structures
                Context.Connections.BlitSharedPortion(SourceConnections);
                Context.Topologies.BlitSharedPortion(SourceTopologies);
            }
        }

        /// <summary>
        /// The cache and nodes are not taking ownership of, and must survive this context's lifetime.
        /// The returned context must ONLY be used after the jobhandle is completed. Additionally, this must happen
        /// in the current scope.
        /// </summary>
        public static JobHandle InitializeContext(
            JobHandle inputDependencies,
            out TopologyComputationContext context,
            BlitList<Connection> connectionTable,
            BlitList<TopologyIndex> topologies,
            TraversalCache cache,
            NodeArraySource sourceNodes,
            TopologyCacheAPI.VersionTracker version,
            RenderExecutionModel model = RenderExecutionModel.MaximallyParallel
            )
        {
            context = new TopologyComputationContext();
            context.Cache = new MutableTopologyCache(cache);

            PrepareJob job;
            job.Version = version;

            job.SourceConnections = connectionTable;
            job.SourceTopologies = topologies;

            context.Nodes = sourceNodes.Source;
            context.Count = sourceNodes.Count;

            context.Connections = new BlitList<Connection>(job.SourceConnections.Count, Allocator.TempJob);
            context.Topologies = new BlitList<TopologyIndex>(job.SourceTopologies.Count, Allocator.TempJob);

            context.VisitCache = new NativeArray<NodeVisitCache>(context.Count, Allocator.TempJob);
            context.VisitCacheLeafIndicies = new NativeList<int>(10, Allocator.TempJob);
            context.Model = model;
            job.Context = context;

            return job.Schedule(inputDependencies);
        }


        public void Dispose()
        {
            Topologies.Dispose();
            Connections.Dispose();
            VisitCache.Dispose();
            VisitCacheLeafIndicies.Dispose();
        }
    }
}
