using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Profiling;

namespace Unity.DataFlowGraph
{
    static partial class TopologyAPI<TVertex, TInputPort, TOutputPort>
        where TVertex : unmanaged, IEquatable<TVertex>
        where TInputPort : unmanaged, IEquatable<TInputPort>
        where TOutputPort : unmanaged, IEquatable<TOutputPort>
    {
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

                    CacheAPI.RecomputeTopology(ref Context);
                }
            }

            [BurstCompile]
            internal struct UpdateGenealogyJob : IJobParallelForDefer
            {
                public MutableTopologyCache.ConcurrentIncrementalContext<TTopologyFromVertex> Cache;
                public ProfilerMarker BuildCacheMarker;
                public int NewVersion;

                public void Execute(int changedGroupIndex)
                {
                    if (Cache.Version[0] == NewVersion || Cache.Errors.Count > 0)
                        return;

                    // Build connection table, and finalize.
                    BuildCacheMarker.Begin();

                    var error = CacheAPI.RebakeGroup(
                        Cache.TraversalMask | Cache.AlternateMask,
                        ref Cache.Topology,
                        ref Cache.Database,
                        ref Cache.GetNewGroup(changedGroupIndex)
                    );
                    
                    BuildCacheMarker.End();

                    if (error != TraversalCache.Error.None)
                        Cache.Errors.Enqueue(error);
                }
            }

            [BurstCompile]
            internal struct FinalizeTopologyJob : IJob
            {
                public ComputationContext<TTopologyFromVertex> Context;
                public int NewVersion;

                public void Execute()
                {
                    if (Context.Cache.Version[0] == NewVersion)
                        return;

                    Context.Cache.Version[0] = NewVersion;

                    if(Context.Cache.Errors.Count == 0)
                        CacheAPI.AlignGroups(ref Context);
                }
            }

            public bool IsCreated => Vertices != null;

            public TTopologyFromVertex Topologies;
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
            public SortingAlgorithm Algorithm;
            internal ProfilerMarkers Markers;

            internal int TargetMinimumGroupSize;
            internal int TotalNodes;

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
            /// <param name="totalNodes">
            /// The total amount of nodes in the graph that can be reached given all incremental updates.
            /// This is used for cycle detection.
            /// </param>
            /// <param name="targetMinimumGroupSize">
            /// A guide factor for how many nodes should approximately be placed in every group.
            /// Value of -1 means a default heuristic is used.
            /// A value of 0 means each group will contain exactly one connected set of nodes. This represents the largest
            /// possible fragmentation of the cache, but also the most incremental version. 
            /// Otherwise, connected sets of nodes will be coalesced into larger groups with at least this number of nodes. 
            /// </param>
            public static JobHandle InitializeContext(
                JobHandle inputDependencies,
                out ComputationContext<TTopologyFromVertex> context,
                Database connectionTable,
                TTopologyFromVertex topologies,
                TraversalCache cache,
                NativeArray<TVertex> sourceNodes,
                int totalNodes,
                CacheAPI.VersionTracker version,
                SortingAlgorithm algorithm = SortingAlgorithm.GlobalBreadthFirst,
                int targetMinimumGroupSize = -1
            )
            {

                if (sourceNodes.Length > totalNodes)
                    throw new ArgumentException("More changed nodes than total amount");

                context = default;
                context.Cache = new MutableTopologyCache(cache);

                context.Vertices = sourceNodes;
                context.TotalNodes = totalNodes;

                context.Database = connectionTable;
                context.Topologies = topologies;
                context.Algorithm = algorithm;

                context.Markers = ProfilerMarkers.Markers;

                context.TargetMinimumGroupSize = targetMinimumGroupSize;

                return inputDependencies;
            }


            public void Dispose()
            {
            }
        }
    }
}
