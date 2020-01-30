using System;
using Unity.Collections;

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

            /// <summary>
            /// Represents one connected component inside a larger graph.
            /// <seealso cref="Islands"/>
            /// </summary>
            public struct Island
            {
                /// <summary>
                /// The offset into <see cref="OrderedTraversal"/> that this <see cref="Island"/> starts at.
                /// </summary>
                public int TraversalIndexOffset;
                /// <summary>
                /// The number of vertices from <see cref="TraversalIndexOffset"/> that are a member of this <see cref="Island"/>.
                /// </summary>
                public int Count;
            }

            public TraversalCache(int size, uint traversalMask, Allocator allocator = Allocator.Persistent)
                : this(size, traversalMask, 0, allocator)
            {
            }

            public TraversalCache(int size, uint traversalMask, uint alternateMask, Allocator allocator = Allocator.Persistent)
            {
                OrderedTraversal = new NativeList<Slot>(size, allocator);
                ParentTable = new NativeList<Connection>(size, allocator);
                ChildTable = new NativeList<Connection>(size, allocator);
                Leaves = new NativeList<int>(1, allocator);
                Roots = new NativeList<int>(1, allocator);
                Version = new NativeArray<int>(1, allocator);
                Islands = new NativeList<Island>(1, allocator);
                Version[0] = CacheAPI.VersionTracker.Create().Version;
                Errors = new NativeList<Error>(1, allocator);
                TraversalMask = traversalMask;
                AlternateMask = alternateMask;
            }

            // TODO: Change to BlitList for efficiency - ?
            [NativeDisableParallelForRestriction, ReadOnly] public NativeList<Slot> OrderedTraversal;
            [NativeDisableParallelForRestriction, ReadOnly] public NativeList<Connection> ParentTable;
            [NativeDisableParallelForRestriction, ReadOnly] public NativeList<Connection> ChildTable;
            [NativeDisableParallelForRestriction, ReadOnly] public NativeList<Island> Islands;
            [NativeDisableParallelForRestriction, ReadOnly] public NativeList<int> Leaves;
            [NativeDisableParallelForRestriction, ReadOnly] public NativeList<int> Roots;
            [NativeDisableParallelForRestriction, ReadOnly] public NativeArray<int> Version;
            [NativeDisableParallelForRestriction, ReadOnly] public NativeList<Error> Errors;

            readonly uint TraversalMask;
            readonly uint AlternateMask;

            public void Dispose()
            {
                if (!Version.IsCreated)
                    throw new ObjectDisposedException("TraversalCache not created or disposed");

                OrderedTraversal.Dispose();
                ParentTable.Dispose();
                ChildTable.Dispose();
                Leaves.Dispose();
                Roots.Dispose();
                Version.Dispose();
                Islands.Dispose();
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
                    default:
                        throw new ArgumentOutOfRangeException(nameof(error));
                }
            }
        }

        public struct MutableTopologyCache
        {
            public NativeList<TraversalCache.Slot> OrderedTraversal;
            public NativeList<TraversalCache.Connection> ParentTable;
            public NativeList<TraversalCache.Connection> ChildTable;
            public NativeList<TraversalCache.Island> Islands;
            public NativeList<int> Leaves;
            public NativeList<int> Roots;
            public NativeArray<int> Version;
            public NativeList<TraversalCache.Error> Errors;

            public readonly uint TraversalMask;
            public readonly uint AlternateMask;

            public MutableTopologyCache(in TraversalCache cache)
            {
                OrderedTraversal = cache.OrderedTraversal;
                ParentTable = cache.ParentTable;
                ChildTable = cache.ChildTable;
                Islands = cache.Islands;
                Leaves = cache.Leaves;
                Roots = cache.Roots;
                Version = cache.Version;
                TraversalMask = cache.GetMask(TraversalCache.Hierarchy.Traversal);
                AlternateMask = cache.GetMask(TraversalCache.Hierarchy.Alternate);
                Errors = cache.Errors;
            }

            internal void Reset(int size)
            {
                OrderedTraversal.ResizeUninitialized(size);
                Leaves.Clear();
                Roots.Clear();
                ChildTable.Clear();
                ParentTable.Clear();
                Islands.Clear();
                Errors.Clear();
            }
        }
    }
}
