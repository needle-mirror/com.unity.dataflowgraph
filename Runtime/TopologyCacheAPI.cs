using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace Unity.DataFlowGraph
{
    static class TopologyCacheAPI
    {
#pragma warning disable 660, 661  // We do not want Equals(object) nor GetHashCode()
        public struct VersionTracker
        {
            public int Version => m_Version;

            public static VersionTracker Create()
            {
                return new VersionTracker { m_Version = 1 };
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

        public struct TopologyComputationOptions
        {
            public bool ComputeJobified => m_Jobified;


            public static TopologyComputationOptions Create(bool computeJobified)
            {
                return new TopologyComputationOptions { m_Jobified = computeJobified };
            }

            bool m_Jobified;
        }

        struct PatchContext
        {
            public int CacheIndex;
            public NativeArray<TopologyComputationContext.NodeVisitCache> VisitCache;
            public int RunningParentTableIndex;
            public int RunningChildTableIndex;
        }

        [BurstCompile]
        struct ComputeTopologyJob : IJob
        {
            public TopologyComputationContext Context;
            public int NewVersion;

            public void Execute()
            {
                if (Context.Cache.Version[0] == NewVersion)
                    return;

                RecomputeCache(ref Context);
                Context.Cache.Version[0] = NewVersion;
            }
        }

        public static bool IsCacheFresh(VersionTracker versionTracker, in MutableTopologyCache cache)
        {
            return cache.Version[0] == versionTracker.Version;
        }

        internal static void UpdateCacheInline(VersionTracker versionTracker, TopologyComputationOptions options, ref TopologyComputationContext context)
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

        public static JobHandle ScheduleTopologyComputation(JobHandle inputDependencies, VersionTracker versionTracker, in TopologyComputationContext context)
        {
            ComputeTopologyJob job;
            job.Context = context;
            job.NewVersion = versionTracker.Version;

            return job.Schedule(inputDependencies);
        }

        // TODO: Should probably cache these values in the topology index additionally.
        static int CountInputConnections(ref TopologyComputationContext context, ref TopologyIndex top)
        {
            var currConnHandle = top.InputHeadConnection;
            int count = 0;

            while (true)
            {
                ref var connection = ref context.Connections[currConnHandle];

                if (!connection.Valid)
                    break;

                // skip connections not included in the flags
                if ((connection.ConnectionType & context.Cache.TraversalMode) != 0)
                {
                    count++;
                }

                currConnHandle = connection.NextInputConnection;
            }

            return count;
        }

        static void AddNewCacheEntry(ref MutableTopologyCache cache, NodeHandle node, int cacheIndex)
        {
            var cacheEntry = new TraversalCache.Slot
            {
                Handle = node,
                ParentCount = 0,
                ParentTableIndex = 0,
                ChildCount = 0,
                ChildTableIndex = 0
            };

            cache.OrderedTraversal[(int)cacheIndex] = cacheEntry;
        }

        static void VisitNodeCacheChildren(
            ref TopologyComputationContext context,
            int traversalCacheIndex,
            ref NativeArray<TopologyComputationContext.NodeVisitCache> visitCache,
            ref int runningCacheIndex)
        {
            var cacheEntry = context.Cache.OrderedTraversal[traversalCacheIndex];
            ref var nodeTop = ref context.Topologies[cacheEntry.Handle.VHandle.Index];

            var currOutputHandle = nodeTop.OutputHeadConnection;

            int numOutputConnections = 0;

            while (true)
            {
                ref var connection = ref context.Connections[currOutputHandle];

                if (!connection.Valid)
                    break;

                if ((connection.ConnectionType & context.Cache.TraversalMode) != 0)
                {
                    numOutputConnections++;
                    var inputHandle = connection.DestinationHandle;
                    var inputTop = context.Topologies[inputHandle.VHandle.Index];

                    // TODO: Convert to ref returns and remove all double assignments
                    var inputVisitCache = visitCache[inputTop.VisitCacheIndex];

                    if (++inputVisitCache.visitCount == inputVisitCache.parentCount)
                    {
                        inputVisitCache.TraversalIndex = runningCacheIndex++;
                        AddNewCacheEntry(ref context.Cache, inputHandle, inputVisitCache.TraversalIndex);
                    }

                    visitCache[inputTop.VisitCacheIndex] = inputVisitCache;
                }

                currOutputHandle = connection.NextOutputConnection;
            }

            // Root detected.
            if (numOutputConnections == 0)
            {
                context.Cache.Roots.Add(traversalCacheIndex);
            }
        }

        static void BuildConnectionCache(ref TopologyComputationContext context, ref PatchContext patchData)
        {
            var cacheEntry = context.Cache.OrderedTraversal[patchData.CacheIndex];
            var nodeTop = context.Topologies[cacheEntry.Handle.VHandle.Index];

            cacheEntry.ParentTableIndex = patchData.RunningParentTableIndex;
            cacheEntry.ChildTableIndex = patchData.RunningChildTableIndex;

            var currentConnectionHandle = nodeTop.InputHeadConnection;
            int count = 0;
            while (true)
            {
                ref var connection = ref context.Connections[currentConnectionHandle];

                if (!connection.Valid)
                    break;

                if ((connection.ConnectionType & context.Cache.TraversalMode) != 0)
                {
                    var parentTopology = context.Topologies[connection.SourceHandle.VHandle.Index];
                    // TODO: Shared data, won't work with multiple graphs
                    var parentTraversalIndex = patchData.VisitCache[parentTopology.VisitCacheIndex].TraversalIndex;

                    context.Cache.ParentTable.Add(new TraversalCache.Connection
                    {
                        TraversalIndex = parentTraversalIndex, InputPort = connection.DestinationInputPort, OutputPort = connection.SourceOutputPort
                    });

                    patchData.RunningParentTableIndex++;
                    count++;
                }

                currentConnectionHandle = connection.NextInputConnection;
            }

            cacheEntry.ParentCount = count;

            currentConnectionHandle = nodeTop.OutputHeadConnection;
            count = 0;

            while (true)
            {
                ref var connection = ref context.Connections[currentConnectionHandle];

                if (!connection.Valid)
                    break;

                if ((connection.ConnectionType & context.Cache.TraversalMode) != 0)
                {
                    var childTopology = context.Topologies[connection.DestinationHandle.VHandle.Index];
                    var childTraversalIndex = patchData.VisitCache[childTopology.VisitCacheIndex].TraversalIndex;

                    context.Cache.ChildTable.Add(new TraversalCache.Connection
                    {
                        TraversalIndex = childTraversalIndex, OutputPort = connection.SourceOutputPort, InputPort = connection.DestinationInputPort
                    });

                    patchData.RunningChildTableIndex++;
                    count++;
                }

                currentConnectionHandle = connection.NextOutputConnection;
            }
            cacheEntry.ChildCount = count;
            context.Cache.OrderedTraversal[patchData.CacheIndex] = cacheEntry;
        }


        static unsafe void RecomputeCache(ref TopologyComputationContext context)
        {
            context.Cache.Reset(context.Count);

            InitializeVisitCacheAndFindLeaves(ref context);

            switch (context.Model)
            {
                case RenderExecutionModel.Islands:
                    ConnectedComponentSearch(ref context);
                    break;
                default:
                    MaximalParallelSearch(ref context);
                    break;
            }

            // Build connection table.
            var patchContext = new PatchContext
            {
                VisitCache = context.VisitCache
            };

            for (var i = 0; i < context.Count; i++)
            {
                patchContext.CacheIndex = i;
                BuildConnectionCache(ref context, ref patchContext);
            }

        }

        static unsafe void MaximalParallelSearch(ref TopologyComputationContext context)
        {
            int runningTraversalCacheIndex = 0;

            // Start by adding all leaf visit caches into the traversal caches.
            // Leaves can always be visited immediately.
            for (var i = 0; i < context.VisitCacheLeafIndicies.Length; i++)
            {
                var leafIndex = context.VisitCacheLeafIndicies[i];

                var nodeVisitCache = context.VisitCache[leafIndex];
                nodeVisitCache.TraversalIndex = runningTraversalCacheIndex++;
                AddNewCacheEntry(ref context.Cache, context.Nodes[leafIndex], nodeVisitCache.TraversalIndex);
                context.Cache.Leaves.Add(nodeVisitCache.TraversalIndex);

                context.VisitCache[leafIndex] = nodeVisitCache;
            }

            // Visit every node so far in the traversal cache. It will progressively get filled up as we visit
            // every single node through their children.
            for (var i = 0; i < context.Count; i++)
            {
                VisitNodeCacheChildren(ref context, i, ref context.VisitCache, ref runningTraversalCacheIndex);
            }

            // This algorithm only produces one island.
            context.Cache.Islands.Add(
                new TraversalCache.Island
                {
                    TraversalIndexOffset = 0, Count = context.Count
                }
            );
        }

        static unsafe void ConnectedComponentSearch(ref TopologyComputationContext context)
        {
            int runningCacheIndex = 0, oldCacheIndex;

            for (int i = 0; i < context.VisitCacheLeafIndicies.Length; ++i)
            {
                var visitCacheIndex = context.VisitCacheLeafIndicies[i];

                oldCacheIndex = runningCacheIndex;
                RecursiveDependencySearch(ref context, ref runningCacheIndex, context.Nodes[visitCacheIndex], new ConnectionHandle());

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
        }

        /// <returns>True if all dependencies were resolved, false if not.</returns>
        static bool RecursiveDependencySearch(ref TopologyComputationContext context, ref int runningCacheIndex, NodeHandle current, ConnectionHandle path)
        {
            ref var currentTopology = ref context.Topologies[current.VHandle.Index];
            var visitIndex = currentTopology.VisitCacheIndex;
            var visitCache = context.VisitCache[visitIndex];

            // Resolving this node is currently in process - we can't recurse into here.
            if (visitCache.CurrentlyResolving != 0)
                return false;

            // Has this node already been visited?
            if (visitCache.visitCount != 0)
                return true;

            // Reflect to everyone else that this is now being visited - requires early store to the visit cache
            visitCache.CurrentlyResolving++;
            context.VisitCache[visitIndex] = visitCache;

            var currentConnectionHandle = currentTopology.InputHeadConnection;

            int outputConnections = 0, inputConnections = 0;

            // Walk all our parents.
            while (true)
            {
                ref var connection = ref context.Connections[currentConnectionHandle];

                if (!connection.Valid)
                    break;

                if ((connection.ConnectionType & context.Cache.TraversalMode) != 0)
                {
                    // TODO: We can build the connection table in place here.
                    inputConnections++;

                    // Ensure we don't walk the path we came here from.
                    if (currentConnectionHandle != path)
                    {
                        // If we can't resolve our parents, that means our parents will get back to us at some point.
                        if (!RecursiveDependencySearch(ref context, ref runningCacheIndex, connection.SourceHandle, currentConnectionHandle))
                        {
                            // Be sure to note we are not trying to resolve this node anymore, since our parents aren't ready.
                            visitCache.CurrentlyResolving = 0;
                            context.VisitCache[visitIndex] = visitCache;
                            return false;
                        }
                    }
                }

                currentConnectionHandle = connection.NextInputConnection;
            }

            // No more dependencies. We can safely add this node to the traversal cache.
            var traversalCacheIndex = runningCacheIndex++;
            AddNewCacheEntry(ref context.Cache, current, traversalCacheIndex);

            // Detect leaves.
            if (inputConnections == 0)
                context.Cache.Leaves.Add(traversalCacheIndex);

            visitCache.parentCount = inputConnections;
            visitCache.TraversalIndex = traversalCacheIndex;
            // Reflect to everyone else that this is now visited - and can be safely referenced
            visitCache.visitCount++;
            // We are not "visiting" anymore, just processing remainder nodes.
            visitCache.CurrentlyResolving = 0;

            context.VisitCache[visitIndex] = visitCache;

            currentConnectionHandle = currentTopology.OutputHeadConnection;

            // Walk all our children.
            while (true)
            {
                ref var connection = ref context.Connections[currentConnectionHandle];

                if (!connection.Valid)
                    break;

                if ((connection.ConnectionType & context.Cache.TraversalMode) != 0)
                {
                    // TODO: We can build the connection table in place here.
                    outputConnections++;

                    // Ensure we don't walk the path we came here from.
                    if (currentConnectionHandle != path)
                    {
                        // We don't have to worry about success here, since reason for abortion would be inability to completely visit ourself
                        // which is done by reaching to our parents, not our children.
                        RecursiveDependencySearch(ref context, ref runningCacheIndex, connection.DestinationHandle, currentConnectionHandle);
                    }
                }

                currentConnectionHandle = connection.NextOutputConnection;
            }

            // Detect roots.
            if (outputConnections == 0)
                context.Cache.Roots.Add(traversalCacheIndex);

            context.VisitCache[visitIndex] = visitCache;

            return true;
        }

        static unsafe void InitializeVisitCacheAndFindLeaves(ref TopologyComputationContext context)
        {
            for (var i = 0; i < context.Count; i++)
            {
                ref var topData = ref context.Topologies[context.Nodes[i].VHandle.Index];
                // TODO: Modifying global data
                // ^ Can be fixed with another temp work array of another size than visitcache
                topData.VisitCacheIndex = i;

                var nodeVisitCache = context.VisitCache[i];
                nodeVisitCache.visitCount = 0;
                nodeVisitCache.parentCount = CountInputConnections(ref context, ref topData);
                nodeVisitCache.TraversalIndex = 0;

                // No input connections?
                if (nodeVisitCache.parentCount == 0)
                {
                    context.VisitCacheLeafIndicies.Add(i);
                }

                context.VisitCache[i] = nodeVisitCache;
            }
        }
    }
}
