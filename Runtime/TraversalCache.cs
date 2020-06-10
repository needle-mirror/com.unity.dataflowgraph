using System;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.DataFlowGraph
{
    static partial class TopologyAPI<TVertex, TInputPort, TOutputPort>
        where TVertex : unmanaged, IEquatable<TVertex>
        where TInputPort : unmanaged, IEquatable<TInputPort>
        where TOutputPort : unmanaged, IEquatable<TOutputPort>
    {
        public struct TraversalCache : IDisposable
        {
            /// <summary>
            /// Potential deferred errors for computing a traversal order for a DAG.
            /// <seealso cref="FormatError(Error)"/>
            /// </summary>
            public enum Error
            {
                /// <summary>
                /// No error
                /// </summary>
                None,
                /// <summary>
                /// Cycles were detected in the graph, which is not allowed.
                /// There's no formal solution to a traversal order for a DAG.
                /// Contents of the traversal cache is cleared in this case.
                /// </summary>
                Cycles,
                /// <summary>
                /// Vertices connected with <see cref="Hierarchy.Alternate"/> were not completely reachable with
                /// the topology formed by <see cref="Hierarchy.Traversal"/>.
                /// Contents of the traversal cache is cleared in this case.
                /// This is only a problem in <see cref="SortingAlgorithm.LocalDepthFirst"/>.
                /// </summary>
                UnrelatedHierarchy
            }

            /// <summary>
            /// A <see cref="TraversalCache"/> is always constructed with a traversal bit mask which filters which
            /// connections are considered to form the traversal hierarchy within the graph. This ultimately defines the
            /// traversal order which will be computed. Optionally, a second bit mask can be supplied to the
            /// <see cref="TraversalCache"/> at construction time to define an alternate supplementary hierarchy which
            /// can be queried while walking the computed traversal.
            /// </summary>
            public enum Hierarchy
            {
                Traversal,
                Alternate
            }

            public const uint TraverseAllMask = 0xFFFFFFFF;

            public struct Slot
            {
                public TVertex Vertex;

                public int ParentCount;
                public int ParentTableIndex;

                public int ChildCount;
                public int ChildTableIndex;
            }

            public struct Connection
            {
                public int TraversalIndex;
                public uint TraversalFlags;
                public TOutputPort OutputPort;
                public TInputPort InputPort;
            }

            public unsafe struct Group : IDisposable
            {
                UnsafeList
                    m_OrderedTraversal,
                    m_ParentTable,
                    m_ChildTable,
                    m_Leaves,
                    m_Roots;

                public readonly int HandleToSelf;

                // TODO: These are now duplicated in a lot of places, as every traverser level asks for this information
                readonly uint TraversalMask;
                readonly uint AlternateMask;

                public ref /*TODO: Use readonly in later refactors: readonly*/ Slot IndexTraversal(int index)
                {
                    if (m_OrderedTraversal.Length > (uint)index)
                        return ref Unsafe.AsRef<Slot>((byte*)m_OrderedTraversal.Ptr + UnsafeUtility.SizeOf<Slot>() * index);

                    throw new ArgumentOutOfRangeException($"Index {index} is out of range of list length {m_OrderedTraversal.Length}");
                }

                public int TraversalCount => m_OrderedTraversal.Length;
                internal void AddTraversalSlot(in Slot s) => m_OrderedTraversal.Add(s);

                // -----------------------------------------------

                public ref readonly Connection IndexParent(int index)
                {
                    if (m_ParentTable.Length > (uint)index)
                        return ref Unsafe.AsRef<Connection>((byte*)m_ParentTable.Ptr + UnsafeUtility.SizeOf<Connection>() * index);

                    throw new ArgumentOutOfRangeException($"Index {index} is out of range of list length {m_ParentTable.Length}");
                }

                public int ParentCount => m_ParentTable.Length;
                internal void AddParent(in Connection c) => m_ParentTable.Add(c);

                // -----------------------------------------------

                public ref readonly Connection IndexChild(int index)
                {
                    if (m_ChildTable.Length > (uint)index)
                        return ref Unsafe.AsRef<Connection>((byte*)m_ChildTable.Ptr + UnsafeUtility.SizeOf<Connection>() * index);

                    throw new ArgumentOutOfRangeException($"Index {index} is out of range of list length {m_ChildTable.Length}");
                }

                public int ChildCount => m_ChildTable.Length;
                internal void AddChild(in Connection c) => m_ChildTable.Add(c);

                // -----------------------------------------------

                public int IndexLeaf(int index)
                {
                    if (m_Leaves.Length > (uint)index)
                        return UnsafeUtility.ReadArrayElement<int>(m_Leaves.Ptr, index);

                    throw new ArgumentOutOfRangeException($"Index {index} is out of range of list length {m_Leaves.Length}");
                }

                public int LeafCount => m_Leaves.Length;
                internal void AddLeaf(int l) => m_Leaves.Add(l);

                // -----------------------------------------------

                public int IndexRoot(int index)
                {
                    if (m_Roots.Length > (uint)index)
                        return UnsafeUtility.ReadArrayElement<int>(m_Roots.Ptr, index);

                    throw new ArgumentOutOfRangeException($"Index {index} is out of range of list length {m_Roots.Length}");
                }

                public int RootCount => m_Roots.Length;
                internal void AddRoot(int l) => m_Roots.Add(l);

                public uint GetMask(Hierarchy hierarchy) =>
                    hierarchy == Hierarchy.Traversal ? TraversalMask : AlternateMask;

                public void Dispose()
                {
                    m_OrderedTraversal.Dispose();
                    m_ParentTable.Dispose();
                    m_ChildTable.Dispose();
                    m_Leaves.Dispose();
                    m_Roots.Dispose();
                }

                public Group(int capacity, int handle, uint traversalMask, uint alternateMask, Allocator allocator)
                {
                    m_OrderedTraversal = new UnsafeList(UnsafeUtility.SizeOf<Slot>(), UnsafeUtility.AlignOf<Slot>(), capacity, allocator);
                    m_ParentTable = new UnsafeList(allocator);
                    m_ChildTable = new UnsafeList(allocator);
                    m_Leaves = new UnsafeList(allocator);
                    m_Roots = new UnsafeList(allocator);

                    HandleToSelf = handle;
                    TraversalMask = traversalMask;
                    AlternateMask = alternateMask;
                }

                public void Clear()
                {
                    m_OrderedTraversal.Clear();
                    m_ParentTable.Clear();
                    m_ChildTable.Clear();
                    m_Leaves.Clear();
                    m_Roots.Clear();
                }
            }

            [NativeDisableParallelForRestriction, ReadOnly] public NativeList<Group> Groups;
            [NativeDisableParallelForRestriction, ReadOnly] public NativeArray<int> GlobalVersion;
            [NativeDisableParallelForRestriction, ReadOnly] public NativeList<Error> Errors;

            readonly uint TraversalMask;
            readonly uint AlternateMask;

            internal readonly Allocator Allocator;

            public TraversalCache(int size, uint traversalMask, Allocator allocator = Allocator.Persistent)
                : this(size, traversalMask, 0, allocator)
            {
            }

            public TraversalCache(int groupCapacity, uint traversalMask, uint alternateMask, Allocator allocator = Allocator.Persistent)
            {
                Allocator = allocator;
                GlobalVersion = new NativeArray<int>(1, allocator);
                Groups = new NativeList<Group>(groupCapacity, allocator);
                NewGroups = new NativeList<int>(0, allocator);
                DeletedGroups = new NativeList<int>(0, allocator);
                // Create the always existing orphan group.
                Groups.Add(new Group(1, 0, traversalMask, alternateMask, allocator)); 
                GlobalVersion[0] = CacheAPI.VersionTracker.Create().Version;
                FreeGroups = new NativeList<int>(allocator);
                Errors = new NativeList<Error>(1, allocator);
                TraversalMask = traversalMask;
                AlternateMask = alternateMask;
            }
            
            [NativeDisableParallelForRestriction, ReadOnly] public NativeList<int> NewGroups;
            [NativeDisableParallelForRestriction, ReadOnly] public NativeList<int> DeletedGroups;
            [NativeDisableParallelForRestriction, ReadOnly] internal NativeList<int> FreeGroups;
            public int ComputeNumRoots()
            {
                int count = 0;

                for(int g = 0; g < Groups.Length; ++g)
                {
                    count += Groups[g].RootCount;
                }

                return count;
            }

            public int ComputeNumLeaves()
            {
                int count = 0;

                for (int g = 0; g < Groups.Length; ++g)
                {
                    count += Groups[g].LeafCount;
                }

                return count;
            }

            public void Dispose()
            {
                if (!GlobalVersion.IsCreated)
                    throw new ObjectDisposedException("TraversalCache not created or disposed");

                for(int g = 0; g < Groups.Length; ++g)
                {
                    Groups[g].Dispose();
                }

                GlobalVersion.Dispose();
                Groups.Dispose();
                NewGroups.Dispose();
                DeletedGroups.Dispose();
                FreeGroups.Dispose();
                Errors.Dispose();
            }

            public uint GetMask(Hierarchy hierarchy) =>
                hierarchy == Hierarchy.Traversal ? TraversalMask : AlternateMask;

            public static string FormatError(Error error)
            {
                switch (error)
                {
                    case Error.None:
                        return "No error";
                    case Error.Cycles:
                        return $"The graph contains a cycle; {typeof(TraversalCache)} only works with directed acyclic graphs";
                    case Error.UnrelatedHierarchy:
                        return $"The graph contains non-intersecting topology between different masks, " +
                            $"sorted using {nameof(SortingAlgorithm.LocalDepthFirst)}";
                    default:
                        throw new ArgumentOutOfRangeException(nameof(error));
                }
            }
        }

        public struct MutableTopologyCache
        {
            public NativeList<TraversalCache.Group> Groups;
            public NativeArray<int> Version;
            public NativeList<int> NewGroups;
            public NativeList<int> DeletedGroups;
            public NativeList<TraversalCache.Error> Errors;

            internal NativeList<int> FreeGroups;


            public readonly uint TraversalMask;
            public readonly uint AlternateMask;
            readonly Allocator m_Allocator;

            /// <summary>
            /// When nodes are orphaned, they move *back* into the orphan group.
            /// This is not detectable through the group changed system (only notifies
            /// of changed *old* groups).
            /// Thus, we have to special case stuff moving back in here.
            /// Perhaps that's generally a bad idea, and orphaned stuff should stay together
            /// with whatever it was originally related to.
            /// Although it's not entirely clear how that should be computed.
            /// </summary>
            bool m_OrphanGroupMutated;

            public MutableTopologyCache(in TraversalCache cache)
            {
                Groups = cache.Groups;
                Version = cache.GlobalVersion;
                TraversalMask = cache.GetMask(TraversalCache.Hierarchy.Traversal);
                NewGroups = cache.NewGroups;
                DeletedGroups = cache.DeletedGroups;
                FreeGroups = cache.FreeGroups;
                AlternateMask = cache.GetMask(TraversalCache.Hierarchy.Alternate);
                Errors = cache.Errors;
                m_Allocator = cache.Allocator;
                m_OrphanGroupMutated = false;
            }

            internal void ClearChangedGroups(NativeList<bool> changedGroups)
            {
                NewGroups.Clear();
                DeletedGroups.Clear();

                for(int i = Database.OrphanGroupID + 1; i < changedGroups.Length; ++i)
                {
                    if (changedGroups[i])
                    {
                        GetGroup(i).Clear();
                        FreeGroups.Add(i);
                        DeletedGroups.Add(i);
                    }
                }

                if (changedGroups[Database.OrphanGroupID])
                {
                    GetOrphanGroupForAccumulation().Clear();
                }
            }

            internal void ClearAllGroups()
            {
                DeletedGroups.Clear();
                NewGroups.Clear();
                FreeGroups.Clear();

                for (int i = Database.OrphanGroupID + 1; i < Groups.Length; ++i)
                {
                    DeletedGroups.Add(i);
                    ref var g = ref GetGroup(i);
                    g.Clear();
                    FreeGroups.Add(i);
                }

                var orphanage = GetOrphanGroupForAccumulation();
                orphanage.Clear();

                Groups.Resize(1, NativeArrayOptions.ClearMemory);
                Groups[0] = orphanage;
            }

            internal unsafe ref TraversalCache.Group GetGroup(int index)
            {
                return ref Unsafe.AsRef<TraversalCache.Group>(
                    (byte*)Groups.GetUnsafePtr() + UnsafeUtility.SizeOf<TraversalCache.Group>() * index
                );
            }

            unsafe internal ref TraversalCache.Group AllocateGroup(int capacity)
            {
                int groupIndex = -1;
                if(FreeGroups.Length > 0)
                {
                    groupIndex = FreeGroups[FreeGroups.Length - 1];
                    FreeGroups.ResizeUninitialized(FreeGroups.Length - 1);
                }
                else
                {
                    var group = new TraversalCache.Group(capacity, Groups.Length, TraversalMask, AlternateMask, m_Allocator);
                    Groups.Add(group);

                    groupIndex = Groups.Length - 1;
                }

                NewGroups.Add(groupIndex);

                return ref GetGroup(groupIndex);
            }

            internal ref TraversalCache.Group GetOrphanGroupForAccumulation()
            {
                ref var group = ref GetGroup(Database.OrphanGroupID);

                if (!m_OrphanGroupMutated)
                {
                    // Missing .Clear() here enables free accumulation
                    m_OrphanGroupMutated = true;
                    DeletedGroups.Add(Database.OrphanGroupID);
                    NewGroups.Add(Database.OrphanGroupID);
                }

                return ref group;
            }
        }
    }
}
