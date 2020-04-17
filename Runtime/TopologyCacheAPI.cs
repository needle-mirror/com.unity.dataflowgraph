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
        /// <summary>
        /// Choice of method for topologically sorting a <see cref="Database"/>.
        /// </summary>
        public enum SortingAlgorithm
        {
            /// <summary>
            /// Sorts a into a single large breadth first traversal, prioritising early dependencies. As a result, only one 
            /// <see cref="TraversalCache.Island"/> is generated.
            /// If a graph has many similar structures at the same depth, this provides the best opportunities for 
            /// keeping similar nodes adjecant in the <see cref="TraversalCache.OrderedTraversal"/>.
            /// </summary>
            GlobalBreadthFirst,
            /// <summary>
            /// Sorts by traversing from leaves, generating as many <see cref="TraversalCache.Island"/> as there 
            /// are connected components in the graph. 
            /// This allows to generate a <see cref="TraversalCache"/> with "chunks" that can run in parallel.
            /// </summary>
            LocalDepthFirst
        }

        public static class CacheAPI
        {
            const int InvalidConnection = Database.InvalidConnection;

#pragma warning disable 660, 661  // We do not want Equals(object) nor GetHashCode()
            public struct VersionTracker
            {
                public int Version => m_Version;

                public static VersionTracker Create()
                {
                    return new VersionTracker {m_Version = 1};
                }

                public void SignalTopologyChanged()
                {
                    m_Version++;
                }

                public static bool operator ==(VersionTracker a, VersionTracker b) => a.m_Version == b.m_Version;
                public static bool operator !=(VersionTracker a, VersionTracker b) => !(a == b);

                int m_Version;
            }
#pragma warning restore 660, 661

            public struct ComputationOptions
            {
                public bool ComputeJobified => m_Jobified;


                public static ComputationOptions Create(bool computeJobified)
                {
                    return new ComputationOptions {m_Jobified = computeJobified};
                }

                bool m_Jobified;
            }

            struct PatchContext
            {
                public int CacheIndex;
                public NativeArray<VertexTools.VisitCache> VisitCache;
                public int RunningParentTableIndex;
                public int RunningChildTableIndex;
            }

            public static bool IsCacheFresh(VersionTracker versionTracker, in MutableTopologyCache cache)
            {
                return cache.Version[0] == versionTracker.Version;
            }

            internal static void UpdateCacheInline<TTopologyFromVertex>(
                VersionTracker versionTracker,
                ComputationOptions options,
                ref ComputationContext<TTopologyFromVertex> context
            )
                where TTopologyFromVertex : Database.ITopologyFromVertex
            {
                if (IsCacheFresh(versionTracker, context.Cache))
                    return;

                if (!options.ComputeJobified)
                {
                    RecomputeCache(ref context);
                    context.Cache.Version[0] = versionTracker.Version;
                }
                else
                {
                    ScheduleTopologyComputation(
                        new JobHandle(),
                        versionTracker,
                        context
                    ).Complete();
                }
            }

            public static JobHandle ScheduleTopologyComputation<TTopologyFromVertex>(
                JobHandle inputDependencies,
                VersionTracker versionTracker,
                in ComputationContext<TTopologyFromVertex> context
            )
                where TTopologyFromVertex : Database.ITopologyFromVertex
            {
                ComputationContext<TTopologyFromVertex>.ComputeTopologyJob job;
                job.Context = context;
                job.NewVersion = versionTracker.Version;

                return job.Schedule(inputDependencies);
            }

            // TODO: Should probably cache these values in the topology index additionally.
            static int CountInputConnections<TTopologyFromVertex>(
                ref ComputationContext<TTopologyFromVertex> context,
                ref TopologyIndex index
            )
                where TTopologyFromVertex : Database.ITopologyFromVertex
            {
                int count = 0;

                for (var it = index.InputHeadConnection; it != InvalidConnection; it = context.Database[it].NextInputConnection)
                {
                    ref readonly var connection = ref context.Database[it];

                    // skip connections not included in the flags
                    if ((connection.TraversalFlags & context.Cache.TraversalMask) != 0)
                        count++;
                }

                return count;
            }

            static void AddNewCacheEntry(ref MutableTopologyCache cache, TVertex node, int cacheIndex)
            {
                var cacheEntry = new TraversalCache.Slot
                {
                    Vertex = node,
                    ParentCount = 0,
                    ParentTableIndex = 0,
                    ChildCount = 0,
                    ChildTableIndex = 0
                };

                cache.OrderedTraversal[(int) cacheIndex] = cacheEntry;
            }

            static void VisitNodeCacheChildren<TTopologyFromVertex>(
                ref ComputationContext<TTopologyFromVertex> context,
                int traversalCacheIndex,
                ref NativeArray<VertexTools.VisitCache> visitCache,
                ref int runningCacheIndex
            )
                where TTopologyFromVertex : Database.ITopologyFromVertex
            {
                var parentCacheEntry = context.Cache.OrderedTraversal[traversalCacheIndex];

                int numOutputConnections = 0;

                for (
                    var it = context.Topologies[parentCacheEntry.Vertex].OutputHeadConnection;
                    it != InvalidConnection;
                    it = context.Database[it].NextOutputConnection)
                {
                    ref readonly var connection = ref context.Database[it];

                    if ((connection.TraversalFlags & context.Cache.TraversalMask) == 0)
                        continue;

                    numOutputConnections++;
                    var childVertex = connection.Destination;
                    var childTopologyIndex = context.Topologies[childVertex];

                    // TODO: Convert to ref returns and remove all double assignments
                    var childVisitCache = visitCache[childTopologyIndex.VisitCacheIndex];

                    if (++childVisitCache.VisitCount == childVisitCache.ParentCount)
                    {
                        childVisitCache.TraversalIndex = runningCacheIndex++;
                        AddNewCacheEntry(ref context.Cache, childVertex, childVisitCache.TraversalIndex);
                    }

                    visitCache[childTopologyIndex.VisitCacheIndex] = childVisitCache;
                }

                // Root detected.
                if (numOutputConnections == 0)
                {
                    context.Cache.Roots.Add(traversalCacheIndex);
                }
            }

            static void BuildConnectionCache<TTopologyFromVertex>(
                ref ComputationContext<TTopologyFromVertex> context,
                ref PatchContext patchData
            )
                where TTopologyFromVertex : Database.ITopologyFromVertex
            {
                var cacheEntry = context.Cache.OrderedTraversal[patchData.CacheIndex];
                var index = context.Topologies[cacheEntry.Vertex];

                cacheEntry.ParentTableIndex = patchData.RunningParentTableIndex;
                cacheEntry.ChildTableIndex = patchData.RunningChildTableIndex;

                int count = 0;

                uint combinedMask = context.Cache.TraversalMask | context.Cache.AlternateMask;
                for (var it = index.InputHeadConnection; it != InvalidConnection; it = context.Database[it].NextInputConnection)
                {
                    ref readonly var connection = ref context.Database[it];

                    if ((connection.TraversalFlags & combinedMask) == 0)
                        continue;

                    var parentTopology = context.Topologies[connection.Source];
                    // TODO: Shared data, won't work with multiple graphs
                    var parentTraversalIndex = patchData.VisitCache[parentTopology.VisitCacheIndex].TraversalIndex;

                    context.Cache.ParentTable.Add(new TraversalCache.Connection
                    {
                        TraversalIndex = parentTraversalIndex,
                        InputPort = connection.DestinationInputPort,
                        OutputPort = connection.SourceOutputPort,
                        TraversalFlags = connection.TraversalFlags
                    });

                    patchData.RunningParentTableIndex++;
                    count++;
                }

                cacheEntry.ParentCount = count;
                count = 0;

                for (var it = index.OutputHeadConnection; it != InvalidConnection; it = context.Database[it].NextOutputConnection)
                {
                    ref readonly var connection = ref context.Database[it];

                    if ((connection.TraversalFlags & combinedMask) == 0)
                        continue;

                    var childTopology = context.Topologies[connection.Destination];
                    var childTraversalIndex = patchData.VisitCache[childTopology.VisitCacheIndex].TraversalIndex;

                    context.Cache.ChildTable.Add(new TraversalCache.Connection
                    {
                        TraversalIndex = childTraversalIndex,
                        OutputPort = connection.SourceOutputPort,
                        InputPort = connection.DestinationInputPort,
                        TraversalFlags = connection.TraversalFlags
                    });

                    patchData.RunningChildTableIndex++;
                    count++;
                }

                cacheEntry.ChildCount = count;
                context.Cache.OrderedTraversal[patchData.CacheIndex] = cacheEntry;
            }


            static internal unsafe void RecomputeCache<TTopologyFromVertex>(
                ref ComputationContext<TTopologyFromVertex> context
            )
                where TTopologyFromVertex : Database.ITopologyFromVertex
            {
                // TODO: Use Auto() when Burst supports it.
                context.Markers.ReallocateContext.Begin();
                context.Cache.Reset(context.Vertices.Length);
                context.Markers.ReallocateContext.End();

                // TODO: Use Auto() when Burst supports it.
                context.Markers.BuildVisitationCache.Begin();
                InitializeVisitCacheAndFindLeaves(ref context);
                context.Markers.BuildVisitationCache.End();

                // TODO: Use Auto() when Burst supports it.
                context.Markers.ComputeLayout.Begin();
                var error = TraversalCache.Error.None;

                switch (context.Algorithm)
                {
                    case SortingAlgorithm.LocalDepthFirst:
                        error = ConnectedComponentSearch(ref context);
                        break;
                    default:
                        error = MaximalParallelSearch(ref context);
                        break;
                }

                context.Markers.ComputeLayout.End();

                if (error != TraversalCache.Error.None)
                {
                    context.Cache.Reset(0);
                    context.Cache.Errors.Add(error);
                    return;
                }

                // Build connection table.
                var patchContext = new PatchContext
                {
                    VisitCache = context.VisitCache
                };

                // TODO: Use Auto() when Burst supports it.
                context.Markers.BuildConnectionCache.Begin();
                for (var i = 0; i < context.Vertices.Length; i++)
                {
                    patchContext.CacheIndex = i;
                    BuildConnectionCache(ref context, ref patchContext);
                }
                context.Markers.BuildConnectionCache.End();

            }

            static unsafe TraversalCache.Error MaximalParallelSearch<TTopologyFromVertex>(
                ref ComputationContext<TTopologyFromVertex> context
            )
                where TTopologyFromVertex : Database.ITopologyFromVertex
            {
                int runningTraversalCacheIndex = 0;

                // Start by adding all leaf visit caches into the traversal caches.
                // Leaves can always be visited immediately.
                for (var i = 0; i < context.VisitCacheLeafIndicies.Length; i++)
                {
                    var leafIndex = context.VisitCacheLeafIndicies[i];

                    var nodeVisitCache = context.VisitCache[leafIndex];
                    nodeVisitCache.TraversalIndex = runningTraversalCacheIndex++;
                    AddNewCacheEntry(ref context.Cache, context.Vertices[leafIndex], nodeVisitCache.TraversalIndex);
                    context.Cache.Leaves.Add(nodeVisitCache.TraversalIndex);

                    context.VisitCache[leafIndex] = nodeVisitCache;
                }

                // Visit every node so far in the traversal cache. It will progressively get filled up as we visit
                // every single node through their children.
                for (var i = 0; i < context.Vertices.Length; i++)
                {
                    if(i >= runningTraversalCacheIndex)
                    {
                        // Trying to process a node that isn't yet in the traversal cache.
                        // This can only happen if visiting all leaves and descendants did not resolve
                        // all dependencies, ie. there is a cycle in the graph.
                        return TraversalCache.Error.Cycles;
                    }

                    VisitNodeCacheChildren(ref context, i, ref context.VisitCache, ref runningTraversalCacheIndex);
                }

                // This algorithm only produces one island.
                context.Cache.Islands.Add(
                    new TraversalCache.Island
                    {
                        TraversalIndexOffset = 0, Count = context.Vertices.Length
                    }
                );

                return context.Vertices.Length == runningTraversalCacheIndex ? TraversalCache.Error.None : TraversalCache.Error.Cycles;
            }

            static unsafe TraversalCache.Error ConnectedComponentSearch<TTopologyFromVertex>(
                ref ComputationContext<TTopologyFromVertex> context
            )
                where TTopologyFromVertex : Database.ITopologyFromVertex
            {
                int runningCacheIndex = 0, oldCacheIndex;

                for (int i = 0; i < context.VisitCacheLeafIndicies.Length; ++i)
                {
                    var visitCacheIndex = context.VisitCacheLeafIndicies[i];

                    oldCacheIndex = runningCacheIndex;
                    RecursiveDependencySearch(ref context, ref runningCacheIndex, context.Vertices[visitCacheIndex], new ConnectionHandle());

                    // New nodes processed - new island
                    if (oldCacheIndex != runningCacheIndex)
                    {
                        context.Cache.Islands.Add(
                            new TraversalCache.Island
                            {
                                TraversalIndexOffset = oldCacheIndex, Count = runningCacheIndex - oldCacheIndex
                            }
                        );
                    }
                }

                return runningCacheIndex == context.Vertices.Length ? TraversalCache.Error.None : TraversalCache.Error.Cycles;
            }

            /// <returns>True if all dependencies were resolved, false if not.</returns>
            static bool RecursiveDependencySearch<TTopologyFromVertex>(
                ref ComputationContext<TTopologyFromVertex> context,
                ref int runningCacheIndex,
                TVertex current,
                ConnectionHandle path
            )
                where TTopologyFromVertex : Database.ITopologyFromVertex
            {
                var currentIndex = context.Topologies[current];
                var visitIndex = currentIndex.VisitCacheIndex;
                var visitCache = context.VisitCache[visitIndex];

                // Resolving this node is currently in process - we can't recurse into here.
                if (visitCache.CurrentlyResolving != 0)
                    return false;

                // Has this node already been visited?
                if (visitCache.VisitCount != 0)
                    return true;

                // Reflect to everyone else that this is now being visited - requires early store to the visit cache
                visitCache.CurrentlyResolving++;
                context.VisitCache[visitIndex] = visitCache;

                int outputConnections = 0, inputConnections = 0;

                // Walk all our parents.
                for (var it = currentIndex.InputHeadConnection; it != InvalidConnection; it = context.Database[it].NextInputConnection)
                {
                    ref readonly var connection = ref context.Database[it];

                    if ((connection.TraversalFlags & context.Cache.TraversalMask) == 0)
                        continue;

                    // TODO: We can build the connection table in place here.
                    inputConnections++;

                    // Ensure we don't walk the path we came here from.
                    if (it != path)
                    {
                        // If we can't resolve our parents, that means our parents will get back to us at some point.
                        // This will also fail in case of cycles.
                        if (!RecursiveDependencySearch(ref context, ref runningCacheIndex, connection.Source, it))
                        {
                            // Be sure to note we are not trying to resolve this node anymore, since our parents aren't ready.
                            visitCache.CurrentlyResolving = 0;
                            context.VisitCache[visitIndex] = visitCache;
                            return false;
                        }
                    }

                }

                // No more dependencies. We can safely add this node to the traversal cache.
                var traversalCacheIndex = runningCacheIndex++;
                AddNewCacheEntry(ref context.Cache, current, traversalCacheIndex);

                // Detect leaves.
                if (inputConnections == 0)
                    context.Cache.Leaves.Add(traversalCacheIndex);

                visitCache.ParentCount = inputConnections;
                visitCache.TraversalIndex = traversalCacheIndex;
                // Reflect to everyone else that this is now visited - and can be safely referenced
                visitCache.VisitCount++;
                // We are not "visiting" anymore, just processing remainder nodes.
                visitCache.CurrentlyResolving = 0;

                context.VisitCache[visitIndex] = visitCache;

                for (var it = currentIndex.OutputHeadConnection; it != InvalidConnection; it = context.Database[it].NextOutputConnection)
                {
                    ref readonly var connection = ref context.Database[it];

                    if ((connection.TraversalFlags & context.Cache.TraversalMask) == 0)
                        continue;

                    // TODO: We can build the connection table in place here.
                    outputConnections++;

                    // Ensure we don't walk the path we came here from.
                    if (it != path)
                    {
                        // We don't have to worry about success here, since reason for abortion would be inability to completely visit ourself
                        // which is done by reaching to our parents, not our children.
                        RecursiveDependencySearch(ref context, ref runningCacheIndex, connection.Destination, it);
                    }
                }

                // Detect roots.
                if (outputConnections == 0)
                    context.Cache.Roots.Add(traversalCacheIndex);

                context.VisitCache[visitIndex] = visitCache;

                return true;
            }

            static unsafe void InitializeVisitCacheAndFindLeaves<TTopologyFromVertex>(
                ref ComputationContext<TTopologyFromVertex> context
            )
                where TTopologyFromVertex : Database.ITopologyFromVertex
            {
                for (var i = 0; i < context.Vertices.Length; i++)
                {
                    var index = context.Topologies[context.Vertices[i]];
                    // TODO: Modifying global data
                    // ^ Can be fixed with another temp work array of another size than visitcache
                    index.VisitCacheIndex = i;

                    var nodeVisitCache = context.VisitCache[i];
                    nodeVisitCache.VisitCount = 0;
                    nodeVisitCache.ParentCount = CountInputConnections(ref context, ref index);
                    nodeVisitCache.TraversalIndex = 0;

                    // No input connections?
                    if (nodeVisitCache.ParentCount == 0)
                    {
                        context.VisitCacheLeafIndicies.Add(i);
                    }

                    context.VisitCache[i] = nodeVisitCache;

                    context.Topologies[context.Vertices[i]] = index;
                }
            }
        }
    }
}
