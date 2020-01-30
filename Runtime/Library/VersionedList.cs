using System;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Collections
{

#pragma warning disable 660, 661  // We do not want Equals(object) nor GetHashCode()
    struct VersionedHandle
    {
        public int Index;
        public ushort Version, ContainerID;

        public VersionedHandle(int index, ushort version, ushort containerID)
        {
            Index = index;
            Version = version;
            ContainerID = containerID;
        }

        public static bool operator ==(VersionedHandle left, VersionedHandle right)
        {
            return left.Version == right.Version && left.Index == right.Index && left.ContainerID == right.ContainerID;
        }

        public static bool operator !=(VersionedHandle left, VersionedHandle right)
        {
            return !(left == right);
        }

        public override string ToString()
        {
            return $"Index: {Index}, Version: {Version}, ContainerID: {ContainerID}";
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
        readonly ushort m_ContainerID;

        public VersionedList(Allocator alloc, ushort containerID)
        {
            m_List = new FreeList<T>(alloc);
            m_ContainerID = containerID;
        }

        public ref T Allocate()
        {
            var index = m_List.Allocate();

            ref var value = ref m_List.Values[index];

            if (value.VHandle.Version == 0)
                value.VHandle = new VersionedHandle(index, 1, m_ContainerID);

            return ref value;
        }

        public unsafe void CopyTo(NativeList<T> list)
        {
            list.ResizeUninitialized(UncheckedCount);
            UnsafeUtility.MemCpy(list.GetUnsafePtr(), m_List.Values.Pointer, UncheckedCount * sizeof(T));
        }

        public ref T Resolve(VersionedHandle handle)
        {
            return ref this[handle.Index];
        }

        public ref T this[VersionedHandle handle] => ref Resolve(handle);
        public ref T this[int index] => ref m_List.Values[index];

        public bool Exists(VersionedHandle handle)
        {
            return (uint)handle.Index < m_List.Values.Count && handle.Version == m_List.Values[handle.Index].VHandle.Version && handle.ContainerID == m_ContainerID;
        }

        public void Release(T item) => Release(item.VHandle);

        public void Release(VersionedHandle handle)
        {
            ref var value = ref Resolve(handle);
            // Assertion:
            if (value.Valid)
                throw new InvalidOperationException("Value should be invalid when destroyed");

            value.VHandle = new VersionedHandle(handle.Index, (ushort)(handle.Version + 1), handle.ContainerID);
            m_List.Release(value.VHandle.Index);
        }

        public void Dispose()
        {
            m_List.Dispose();
        }
    }
}
