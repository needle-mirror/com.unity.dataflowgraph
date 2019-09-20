using System;

namespace Unity.Collections
{

#pragma warning disable 660, 661  // We do not want Equals(object) nor GetHashCode()
    struct VersionedHandle
    {
        public int Index, Version;

        public static bool operator ==(VersionedHandle left, VersionedHandle right)
        {
            return left.Version == right.Version && left.Index == right.Index;
        }

        public static bool operator !=(VersionedHandle left, VersionedHandle right)
        {
            return !(left == right);
        }

        public override string ToString()
        {
            return $"Index: {Index}, Version: {Version}";
        }
    }
#pragma warning restore 660, 661

    interface IVersionedNode
    {
        VersionedHandle VHandle { get; set; }
        bool Valid { get; }
    }

    struct VersionedList<T> : IDisposable
        where T : unmanaged, IVersionedNode
    {
        public int UncheckedCount => m_List.Values.Count;
        public bool IsCreated => m_List.IsCreated;

        FreeList<T> m_List;

        public VersionedList(Allocator alloc)
        {
            m_List = new FreeList<T>(alloc);
        }

        public ref T Allocate()
        {
            var index = m_List.Allocate();

            ref var value = ref m_List.Values[index];

            if (value.VHandle.Version == 0)
                value.VHandle = new VersionedHandle { Version = 1, Index = index };

            return ref value;
        }

        public ref T Resolve(VersionedHandle handle)
        {
            return ref this[handle.Index];
        }

        public ref T this[VersionedHandle handle] => ref Resolve(handle);
        public ref T this[int index] => ref m_List.Values[index];

        public bool Exists(VersionedHandle handle)
        {
            return (uint)handle.Index < m_List.Values.Count && handle.Version == m_List.Values[handle.Index].VHandle.Version;
        }

        public void Release(T item) => Release(item.VHandle);

        public void Release(VersionedHandle handle)
        {
            ref var value = ref Resolve(handle);
            // Assertion:
            if (value.Valid)
                throw new InvalidOperationException("Value should be invalid when destroyed");

            value.VHandle = new VersionedHandle { Version = handle.Version + 1, Index = handle.Index };
            m_List.Release(value.VHandle.Index);
        }

        public void Dispose()
        {
            m_List.Dispose();
        }
    }
}
