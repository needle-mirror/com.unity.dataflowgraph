using System;
using Unity.Collections;

namespace Unity.DataFlowGraph
{
    struct TraversalCache : IDisposable
    {
        public struct Slot
        {
            public NodeHandle Handle;

            public int UnorderedIndex;

            public int ParentCount;
            public int ParentTableIndex;

            public int ChildCount;
            public int ChildTableIndex;
        }

        public struct Connection
        {
            public int TraversalIndex;
            public OutputPortID OutputPort;
            public InputPortArrayID InputPort;
        }

        public struct Island
        {
            public int TraversalIndexOffset;
            public int Count;
        }

        public TraversalCache(int size, TraversalFlags mode, Allocator allocator = Allocator.Persistent)
        {
            OrderedTraversal = new NativeList<Slot>(size, allocator);
            ParentTable = new NativeList<Connection>(size, allocator);
            ChildTable = new NativeList<Connection>(size, allocator);
            Leaves = new NativeList<int>(1, allocator);
            Roots = new NativeList<int>(1, allocator);
            Version = new NativeArray<int>(1, allocator);
            Islands = new NativeList<Island>(1, allocator);
            Version[0] = TopologyCacheAPI.VersionTracker.Create().Version;
            TraversalMode = mode;
        }

        // TODO: Change to BlitList for efficiency - ?
        [NativeDisableParallelForRestriction, ReadOnly] public NativeList<Slot> OrderedTraversal;
        [NativeDisableParallelForRestriction, ReadOnly] public NativeList<Connection> ParentTable;
        [NativeDisableParallelForRestriction, ReadOnly] public NativeList<Connection> ChildTable;
        [NativeDisableParallelForRestriction, ReadOnly] public NativeList<Island> Islands;
        [NativeDisableParallelForRestriction, ReadOnly] public NativeList<int> Leaves;
        [NativeDisableParallelForRestriction, ReadOnly] public NativeList<int> Roots;
        [NativeDisableParallelForRestriction, ReadOnly] public NativeArray<int> Version;
        public readonly TraversalFlags TraversalMode;

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
        }
    }

    struct MutableTopologyCache
    {
        public NativeList<TraversalCache.Slot> OrderedTraversal;
        public NativeList<TraversalCache.Connection> ParentTable;
        public NativeList<TraversalCache.Connection> ChildTable;
        public NativeList<TraversalCache.Island> Islands;
        public NativeList<int> Leaves;
        public NativeList<int> Roots;
        public NativeArray<int> Version;
        public readonly TraversalFlags TraversalMode;

        public MutableTopologyCache(in TraversalCache cache)
        {
            OrderedTraversal = cache.OrderedTraversal;
            ParentTable = cache.ParentTable;
            ChildTable = cache.ChildTable;
            Islands = cache.Islands;
            Leaves = cache.Leaves;
            Roots = cache.Roots;
            Version = cache.Version;
            TraversalMode = cache.TraversalMode;
        }

        internal void Reset(int size)
        {
            OrderedTraversal.ResizeUninitialized(size);
            Leaves.Clear();
            Roots.Clear();
            ChildTable.Clear();
            ParentTable.Clear();
            Islands.Clear();
        }
    }
}
