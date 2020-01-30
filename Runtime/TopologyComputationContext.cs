using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace Unity.DataFlowGraph
{
    static partial class TopologyAPI<TVertex, TInputPort, TOutputPort>
        where TVertex : unmanaged, IEquatable<TVertex>
        where TInputPort : unmanaged, IEquatable<TInputPort>
        where TOutputPort : unmanaged, IEquatable<TOutputPort>
    {
        public static class VertexTools
        {
            public struct VisitCache
            {
                public int VisitCount;
                public int ParentCount;
                public int TraversalIndex;
                public int CurrentlyResolving;
            }
        }

        public struct ComputationContext<TTopologyFromVertex> : IDisposable
            where TTopologyFromVertex : Database.ITopologyFromVertex
        {
            [BurstCompile]
            internal struct ComputeTopologyJob : IJob
            {
                public ComputationContext<TTopologyFromVertex> Context;
                public int NewVersion;

                public void Execute()
                {
                    if (Context.Cache.Version[0] == NewVersion)
                        return;

                    CacheAPI.RecomputeCache(ref Context);
                    Context.Cache.Version[0] = NewVersion;
                }
            }

            public bool IsCreated => Vertices != null;

            public TTopologyFromVertex Topologies;
            [ReadOnly]
            public Database Database;

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
            [ReadOnly]
            public NativeArray<TVertex> Vertices;

            public MutableTopologyCache Cache;

            public NativeArray<VertexTools.VisitCache> VisitCache;
            public NativeList<int> VisitCacheLeafIndicies;

            public SortingAlgorithm Algorithm;

            internal ProfilerMarkers Markers;

            /// <summary>
            /// Note that no ownership is taken over the following:
            /// - Cache
            /// - Topology
            /// - Connections
            /// - Nodes
            /// Hence they must survive the context's lifetime.
            /// The returned context must ONLY be used after the jobhandle is completed. Additionally, this must happen
            /// in the current scope.
            /// </summary>
            public static JobHandle InitializeContext(
                JobHandle inputDependencies,
                out ComputationContext<TTopologyFromVertex> context,
                Database connectionTable,
                TTopologyFromVertex topologies,
                TraversalCache cache,
                NativeArray<TVertex> sourceNodes,
                CacheAPI.VersionTracker version,
                SortingAlgorithm algorithm = SortingAlgorithm.GlobalBreadthFirst
            )
            {
                context = default;
                context.Cache = new MutableTopologyCache(cache);

                context.Vertices = sourceNodes;

                context.Database = connectionTable;
                context.Topologies = topologies;

                context.VisitCache =
                    new NativeArray<VertexTools.VisitCache>(context.Vertices.Length, Allocator.TempJob);
                context.VisitCacheLeafIndicies = new NativeList<int>(10, Allocator.TempJob);
                context.Algorithm = algorithm;

                context.Markers = ProfilerMarkers.Markers;

                return inputDependencies;
            }


            public void Dispose()
            {
                VisitCache.Dispose();
                VisitCacheLeafIndicies.Dispose();
            }
        }
    }
}
