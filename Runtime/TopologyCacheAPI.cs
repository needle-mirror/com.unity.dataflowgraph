using System;
using Unity.Collections;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;

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
            /// <see cref="TraversalCache.Group"/> is generated.
            /// If a graph has many similar structures at the same depth, this provides the best opportunities for
            /// keeping similar nodes adjacent in the <see cref="TraversalCache.OrderedTraversal"/>.
            /// </summary>
            GlobalBreadthFirst,
            /// <summary>
            /// Sorts by traversing from leaves, generating as many <see cref="TraversalCache.Group"/>s as there
            /// are connected components in the graph.
            /// This allows to generate a <see cref="TraversalCache"/> with groups that can run in parallel.
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
                    RecomputeTopology(ref context);

                    // Build connection table, and finalize.
                    context.Markers.BuildConnectionCache.Begin();
                    var error = RebakeFreshGroups(ref context);
                    context.Markers.BuildConnectionCache.End();

                    if (error != TraversalCache.Error.None)
                    {
                        context.Cache.Errors.Enqueue(error);
                        WipeGroupsAndReset(ref context);
                    }
                    else
                    {
                        AlignGroups(ref context);
                    }

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
                JobHandle deps,
                VersionTracker versionTracker,
                in ComputationContext<TTopologyFromVertex> context
            )
                where TTopologyFromVertex : Database.ITopologyFromVertex
            {
                if (IsCacheFresh(versionTracker, context.Cache))
                    return deps;

                ComputationContext<TTopologyFromVertex>.ComputeTopologyJob topologyJob;
                topologyJob.Context = context;
                topologyJob.NewVersion = versionTracker.Version;

                ComputationContext<TTopologyFromVertex>.UpdateGenealogyJob genealogyJob;
                genealogyJob.Cache = new MutableTopologyCache.ConcurrentIncrementalContext<TTopologyFromVertex>(
                    context,
                    context.Database,
                    context.Topologies
                );
                genealogyJob.NewVersion = versionTracker.Version;
                genealogyJob.BuildCacheMarker = context.Markers.BuildConnectionCache;

                ComputationContext<TTopologyFromVertex>.FinalizeTopologyJob finalJob;
                finalJob.Context = context;
                finalJob.NewVersion = versionTracker.Version;

                deps = topologyJob.Schedule(deps);
                deps = genealogyJob.Schedule(context.Cache.NewGroups, 1, deps);
                return finalJob.Schedule(deps);
            }

            /// <returns>
            /// The index of the cache entry
            /// </returns>
            static int AddNewCacheEntry(ref TraversalCache.Group group, TVertex node)
            {
                var cacheEntry = new TraversalCache.Slot
                {
                    Vertex = node,
                    ParentCount = 0,
                    ParentTableIndex = 0,
                    ChildCount = 0,
                    ChildTableIndex = 0
                };

                group.AddTraversalSlot(cacheEntry);
                return group.TraversalCount - 1;
            }

            static TraversalCache.Error BuildConnectionCache<TTopologyFromVertex>(
                uint combinedMask,
                ref TTopologyFromVertex topology,
                ref Database database,
                ref TraversalCache.Group group,
                int traversalIndex
            )
                where TTopologyFromVertex : Database.ITopologyFromVertex
            {
                var cacheEntry = group.IndexTraversal(traversalIndex);
                var index = topology[cacheEntry.Vertex];

                cacheEntry.ParentTableIndex = group.ParentCount;
                cacheEntry.ChildTableIndex = group.ChildCount;


                for (var it = index.InputHeadConnection; it != InvalidConnection; it = database[it].NextInputConnection)
                {
                    ref readonly var connection = ref database[it];

                    if ((connection.TraversalFlags & combinedMask) == 0)
                        continue;

                    var parentTopology = topology[connection.Source];

                    // TODO: Potential race condition (if groups are computed in parallel), even though it is an error
                    if (parentTopology.GroupID != index.GroupID)
                        return TraversalCache.Error.UnrelatedHierarchy;

                    group.AddParent(new TraversalCache.Connection
                    {
                        TraversalIndex = parentTopology.TraversalIndex,
                        InputPort = connection.DestinationInputPort,
                        OutputPort = connection.SourceOutputPort,
                        TraversalFlags = connection.TraversalFlags
                    });
                }

                cacheEntry.ParentCount = group.ParentCount - cacheEntry.ParentTableIndex;

                for (var it = index.OutputHeadConnection; it != InvalidConnection; it = database[it].NextOutputConnection)
                {
                    ref readonly var connection = ref database[it];

                    if ((connection.TraversalFlags & combinedMask) == 0)
                        continue;

                    var childTopology = topology[connection.Destination];

                    // TODO: Potential race condition (if groups are computed in parallel), even though it is an error
                    if (childTopology.GroupID != index.GroupID)
                        return TraversalCache.Error.UnrelatedHierarchy;

                    group.AddChild(new TraversalCache.Connection
                    {
                        TraversalIndex = childTopology.TraversalIndex,
                        OutputPort = connection.SourceOutputPort,
                        InputPort = connection.DestinationInputPort,
                        TraversalFlags = connection.TraversalFlags
                    });
                }

                cacheEntry.ChildCount = group.ChildCount - cacheEntry.ChildTableIndex;
                group.IndexTraversal(traversalIndex) = cacheEntry;

                // Clear temporary state from traversal computation.
                index.Resolved = false;
                index.CurrentlyResolving = false;
                topology[cacheEntry.Vertex] = index;

                return TraversalCache.Error.None;
            }


            static internal unsafe void RecomputeTopology<TTopologyFromVertex>(
                ref ComputationContext<TTopologyFromVertex> context
            )
                where TTopologyFromVertex : Database.ITopologyFromVertex
            {
                // TODO: Use Auto() when Burst supports it.
                context.Markers.ReallocateContext.Begin();
                context.Cache.ClearChangedGroups(context.Database.ChangedGroups);
                context.Cache.Errors.Clear();
                context.Markers.ReallocateContext.End();

                // TODO: Use Auto() when Burst supports it.
                context.Markers.ComputeLayout.Begin();
                var error = TraversalCache.Error.None;

                switch (context.Algorithm)
                {
                    case SortingAlgorithm.GlobalBreadthFirst:
                    case SortingAlgorithm.LocalDepthFirst:
                        error = ConnectedComponentSearch(ref context);
                        break;
                }

                context.Markers.ComputeLayout.End();

                if (error != TraversalCache.Error.None)
                {
                    context.Cache.Errors.Enqueue(error);
                    WipeGroupsAndReset(ref context);
                    return;
                }
            }

            internal static unsafe TraversalCache.Error RebakeFreshGroups<TTopologyFromVertex>(
                ref ComputationContext<TTopologyFromVertex> context
            )
                where TTopologyFromVertex : Database.ITopologyFromVertex
            {
                uint combinedMask = context.Cache.TraversalMask | context.Cache.AlternateMask;

                for (int g = 0; g < context.Cache.NewGroups.Length; ++g)
                {
                    var translated = context.Cache.NewGroups[g];
                    ref var group = ref context.Cache.GetGroup(translated);

                    var error = RebakeGroup(
                        combinedMask,
                        ref context.Topologies,
                        ref context.Database,
                        ref group
                    );

                    if(error != TraversalCache.Error.None)
                    {
                        return error;
                    }
                }

                return TraversalCache.Error.None;
            }

            internal static unsafe TraversalCache.Error RebakeGroup<TTopologyFromVertex>(
                uint combinedMask,
                ref TTopologyFromVertex topology,
                ref Database database,
                ref TraversalCache.Group group
            )
                where TTopologyFromVertex : Database.ITopologyFromVertex
            {
                for (var i = 0; i < group.TraversalCount; i++)
                {
                    var error = BuildConnectionCache(combinedMask, ref topology, ref database, ref group, i);

                    if(error != TraversalCache.Error.None)
                    {
                        return error;
                    }
                }

                return TraversalCache.Error.None;
            }

            /// <summary>
            /// Heuristic of average number of nodes that should go into each island
            /// </summary>
            static int DefaultTargetSize(int x)
            {
                // Always aim to produce at least one island for each potential thread.
                return x / JobsUtility.MaxJobThreadCount;
            }

            const int k_InvalidGroupSentinel = -1;

            static ref TraversalCache.Group GetOrCreateAppropriateGroup<TTopologyFromVertex>(
                ref ComputationContext<TTopologyFromVertex> context, int lastGroupHandle, int minimumGroupSize
            )
                where TTopologyFromVertex : Database.ITopologyFromVertex
            {
                if (lastGroupHandle != k_InvalidGroupSentinel)
                {
                    ref var lastGroup = ref context.Cache.GetGroup(lastGroupHandle);

                    if (lastGroup.TraversalCount <= minimumGroupSize)
                    {
                        return ref lastGroup;
                    }
                }

                // Just an arbitrary heuristic to avoid worst case memory usage all the time.
                return ref context.Cache.AllocateGroup(math.max(2, minimumGroupSize / 4));
            }

            static unsafe TraversalCache.Error ConnectedComponentSearch<TTopologyFromVertex>(
                ref ComputationContext<TTopologyFromVertex> context
            )
                where TTopologyFromVertex : Database.ITopologyFromVertex
            {
                var targetMinimum = context.TargetMinimumGroupSize == -1 ? DefaultTargetSize(context.Vertices.Length) : context.TargetMinimumGroupSize;

                int lastGroupHandle = k_InvalidGroupSentinel;

                for (int i = 0; i < context.Vertices.Length; ++i)
                {
                    var current = context.Vertices[i];
                    var index = context.Topologies[current];

                    if (index.Resolved)
                        continue;

                    // Only recompute nodes belonging to a touched area
                    // In particular, avoids reading nodes already saved and accumulated in orphan group
                    if (!context.Database.ChangedGroups[index.GroupID])
                       continue;

                    // Nodes with no connections go to the orphan group, to avoid a wasteland of islands.
                    bool hasAnyConnections = (index.InputHeadConnection | index.OutputHeadConnection) != Database.InvalidConnection;

                    if(hasAnyConnections)
                    {
                        ref var group = ref GetOrCreateAppropriateGroup(ref context, lastGroupHandle, targetMinimum);
                        lastGroupHandle = group.HandleToSelf;

                        RecursiveDependencySearch(ref context, ref group, current, new ConnectionHandle());
                    }
                    else
                    {
                        RecursiveDependencySearch(ref context, ref context.Cache.GetOrphanGroupForAccumulation(), current, new ConnectionHandle());
                    }
                }

                int collectedNodes = 0;
                for (int g = 0; g < context.Cache.GroupCount; ++g)
                {
                    collectedNodes += context.Cache.GetGroup(g).TraversalCount;
                }

                return collectedNodes == context.TotalNodes ? TraversalCache.Error.None : TraversalCache.Error.Cycles;
            }

            /// <returns>True if all dependencies were resolved, false if not.</returns>
            static bool RecursiveDependencySearch<TTopologyFromVertex>(
                ref ComputationContext<TTopologyFromVertex> context,
                ref TraversalCache.Group group,
                TVertex current,
                ConnectionHandle path
            )
                where TTopologyFromVertex : Database.ITopologyFromVertex
            {
                var currentIndex = context.Topologies[current];

                // Resolving this node is currently in process - we can't recurse into here.
                if (currentIndex.CurrentlyResolving)
                    return false;

                // Has this node already been visited?
                if (currentIndex.Resolved)
                    return true;

                // Reflect to everyone else that this is now being visited - requires early store to the visit cache
                currentIndex.CurrentlyResolving = true;
                context.Topologies[current] = currentIndex;

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
                        if (!RecursiveDependencySearch(ref context, ref group, connection.Source, it))
                        {
                            // Be sure to note we are not trying to resolve this node anymore, since our parents aren't ready.
                            currentIndex.CurrentlyResolving = false;
                            context.Topologies[current] = currentIndex;
                            return false;
                        }
                    }

                }

                // No more dependencies. We can safely add this node to the traversal cache.
                var traversalCacheIndex = AddNewCacheEntry(ref group, current);

                // Detect leaves.
                if (inputConnections == 0)
                    group.AddLeaf(traversalCacheIndex);

                currentIndex.TraversalIndex = traversalCacheIndex;
                // Reflect to everyone else that this is now visited - and can be safely referenced
                currentIndex.Resolved = true;
                // We are not "visiting" anymore, just processing remainder nodes.
                currentIndex.CurrentlyResolving = false;

                // Update group membership.
                currentIndex.GroupID = group.HandleToSelf;
                // write it back for simulation incremental changes
                context.Topologies[current] = currentIndex;


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
                        RecursiveDependencySearch(ref context, ref group, connection.Destination, it);
                    }
                }

                // Detect roots.
                if (outputConnections == 0)
                    group.AddRoot(traversalCacheIndex);

                return true;
            }

            internal static unsafe void AlignGroups<TTopologyFromVertex>(
                ref ComputationContext<TTopologyFromVertex> context
            )
                where TTopologyFromVertex : Database.ITopologyFromVertex
            {
                context.Database.ChangedGroups.Resize(context.Cache.GroupCount, NativeArrayOptions.UninitializedMemory);

                for (int i = 0; i < context.Database.ChangedGroups.Length; ++i)
                    context.Database.ChangedGroups[i] = false;
            }

            internal static void WipeGroupsAndReset<TTopologyFromVertex>(
                ref ComputationContext<TTopologyFromVertex> context
            )
                where TTopologyFromVertex : Database.ITopologyFromVertex
            {
                context.Cache.ClearAllGroups();
                AlignGroups(ref context);

                // Reset all group IDs for simulation.
                for (var i = 0; i < context.Vertices.Length; i++)
                {
                    var index = context.Topologies[context.Vertices[i]];
                    index.GroupID = 0;
                    context.Topologies[context.Vertices[i]] = index;
                }
            }
        }
    }
}
