using System;
using System.Diagnostics;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Collections
{
    // TODO: Move into VersionedList<T> for stronger type checking,
    // once C# supports unmanaged constructed generics
#pragma warning disable 660, 661  // We do not want Equals(object) nor GetHashCode()
    readonly struct VersionedHandle
    {
        readonly public int Index;
        readonly public ushort Version, ContainerID;

        /// <summary>
        /// Only use this for testing. No guarantees about validity.
        /// </summary>
        static internal VersionedHandle Create_ForTesting(int index, ushort version, ushort containerID)
        {
            return new VersionedHandle(index, version, containerID);
        }

        /// <summary>
        /// Temporary until we have C# 8, so ValidatedHandle can be a nested and properly encapsulated type of
        /// <see cref="VersionedList{T}"/>.
        ///
        /// Do not use.
        /// </summary>
        static internal VersionedHandle Create_Internal(int index, ushort version, ushort id)
        {
            return new VersionedHandle(index, version, id);
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

        private VersionedHandle(int index, ushort version, ushort containerID)
        {
            Index = index;
            Version = version;
            ContainerID = containerID;
        }
    }

    /// <summary>
    /// A <see cref="ValidatedHandle"/> is a pre-checked <see cref="VersionedHandle"/> from this particular list,
    /// and can always be used to index a <see cref="VersionedList{T}"/> directly without any checks.
    /// 
    /// <seealso cref="VersionedList{T}.Validate(VersionedHandle)"/> 
    /// </summary>
    /// <remarks>
    /// A <see cref="ValidatedHandle"/> is not guaranteed to be "alive" / <see cref="IVersionedItem.Valid"/>, you 
    /// have to use <see cref="VersionedList{T}.StillExists(ValidatedHandle)"/> for this purpose.
    /// </remarks>
    [DebuggerDisplay("{VHandle, nq}")]
    readonly struct ValidatedHandle : IEquatable<ValidatedHandle>
    {
        internal readonly VersionedHandle Versioned;

        public static bool operator ==(ValidatedHandle left, ValidatedHandle right)
        {
            return left.Versioned == right.Versioned;
        }

        public static bool operator !=(ValidatedHandle left, ValidatedHandle right)
        {
            return left.Versioned != right.Versioned;
        }

        public bool Equals(ValidatedHandle other)
        {
            return this == other;
        }

        internal ValidatedHandle Bump()
        {
            return new ValidatedHandle(Versioned.Index, (ushort)(Versioned.Version + 1u), Versioned.ContainerID);
        }

        /// <summary>
        /// Only use this for testing. No guarantees about validity.
        /// </summary>
        static internal ValidatedHandle Create_ForTesting(VersionedHandle handle)
        {
            return new ValidatedHandle(handle);
        }

        /// <summary>
        /// Temporary until we have C# 8, so ValidatedHandle can be a nested and properly encapsulated type of
        /// <see cref="VersionedList{T}"/>.
        ///
        /// Do not use.
        /// </summary>
        static internal ValidatedHandle Create_FromVersionedList(int index, ushort version, ushort id)
        {
            return new ValidatedHandle(index, version, id);
        }

        /// <summary>
        /// Temporary until we have C# 8, so ValidatedHandle can be a nested and properly encapsulated type of
        /// <see cref="VersionedList{T}"/>.
        ///
        /// Do not use.
        /// </summary>
        static internal ValidatedHandle Create_FromVersionedList(VersionedHandle handle)
        {
            return new ValidatedHandle(handle);
        }

        ValidatedHandle(VersionedHandle handle)
        {
            Versioned = handle;
        }

        ValidatedHandle(int index, ushort version, ushort id)
        {
            Versioned = VersionedHandle.Create_Internal(index, version, id);
        }
    }
#pragma warning restore 660, 661


    // TODO: Move into VersionedList<T> for stronger type checking,
    // once C# supports unmanaged constructed generics
    interface IVersionedItem : IDisposable
    {
        ValidatedHandle Handle { get; set; }
        /// Valid must to return false after <see cref="VersionedList{T}.Release"/> has been invoked on a given item either as a
        /// precondition before that call or as a side-effect of <see cref="IVersionedItem.Dispose"/> being called on the item.
        bool Valid { get; }
    }

    struct VersionedList<T> : IDisposable
        where T : unmanaged, IVersionedItem
    {
        public static string ValidationFail_InvalidMessage = $"Handle to {typeof(T)} was invalid";
        public static string ValidationFail_ForeignMessage = $"Handle to {typeof(T)} was created in another container";
        public static string ValidationFail_DisposedMessage = $"Item of {typeof(T)} was disposed or doesn't exist anymore";

        public unsafe struct Enumerator
        {
            public ref T Current
            {
                get
                {
                    if (ItemsLeft <= 0)
                        throw new InvalidOperationException("Improper Enumerator usage");
                    return ref *Base;
                }
            }

            public bool MoveNext()
            {
                do
                {
                    Base++;
                } while (--ItemsLeft > 0 && !Base->Valid);

                return ItemsLeft > 0 && Base->Valid;
            }

            public Enumerator GetEnumerator() => this;

            internal T* Base;
            internal int ItemsLeft;
        }

        /// <summary>
        /// Directly access the node the <see cref="ValidatedHandle"/> points to.
        /// </summary>
        /// <remarks>No checks are performed, may crash.</remarks>
        public unsafe ref T this[ValidatedHandle handle] => ref *m_List.Values.Ref(handle.Versioned.Index);
        /// <summary>
        /// Shorthand for calling <see cref="Validate(VersionedHandle)"/> and <see cref="this[ValidatedHandle]"/>
        /// </summary>
        public ref T this[VersionedHandle handle] => ref this[Validate(handle)];

        public unsafe Enumerator Items => new Enumerator { Base = m_List.Values.Pointer - 1, ItemsLeft = m_List.Values.Count + 1 };

        /// <summary>
        /// Returns the current number of items in the pool (whether existing or not).
        /// Can be used together with <see cref="UnvalidatedItemAt(int)"/> and <see cref="IVersionedItem.Valid"/>
        /// to iterate over all items in a flat way.
        /// <seealso cref="Items"/> for enumerating valid items.
        /// </summary>
        public int UnvalidatedCount => m_List.Values.Count;

        public bool IsCreated => m_List.IsCreated;

        /// <summary>
        /// Testing purposes - First index is invalid.
        /// </summary>
        internal const int ValidOffset = 1;

        FreeList<T> m_List;
        readonly ushort m_ContainerID;

        public VersionedList(Allocator alloc, ushort containerID)
        {
            m_List = new FreeList<T>(alloc);
            m_ContainerID = containerID;
            // Always keep one invalid index, to allow [new ValidatedHandle()] to not be a crash.
            Allocate();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <exception cref="IndexOutOfRangeException">
        /// Thrown if <paramref name="index"/> is negative or equal/greater than <see cref="UnvalidatedCount"/>
        /// </exception>
        public ref T UnvalidatedItemAt(int index)
        {
            return ref m_List.Values[index];
        }

        public ref T Allocate()
        {
            var index = m_List.Allocate();

            ref var value = ref m_List.Values[index];

            if (value.Handle.Versioned.Version == 0)
                value.Handle = ValidatedHandle.Create_FromVersionedList(index, 1, m_ContainerID);

            return ref value;
        }

        /// <summary>
        /// Copies the entire pool of items to the <paramref name="list"/>, whether valid or not.
        /// <paramref name="list"/> will be resized to <see cref="UnvalidatedCount"/>.
        /// </summary>
        public unsafe void CopyUnvalidatedTo(NativeList<T> list)
        {
            list.ResizeUninitialized(UnvalidatedCount);
            UnsafeUtility.MemCpy(list.GetUnsafePtr(), m_List.Values.Pointer, UnvalidatedCount * sizeof(T));
        }

        public unsafe bool Exists(VersionedHandle handle)
        {
            return (uint)handle.Index < m_List.Values.Count && handle.ContainerID == m_ContainerID && StillExists(handle, ref *m_List.Values.Ref(handle.Index));
        }

        /// <remarks>No checks are performed, may crash.</remarks>
        public bool StillExists(ValidatedHandle handle)
        {
            return StillExists(handle.Versioned, ref this[handle]);
        }

        /// <exception cref="ArgumentException">
        /// If the <paramref name="handle"/> is invalid, or comes from another <see cref="VersionedList{T}"/>
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// If the <paramref name="handle"/> no longer exists
        /// </exception>
        public ValidatedHandle Validate(VersionedHandle handle)
        {
            if (Exists(handle))
                return ValidatedHandle.Create_FromVersionedList(handle);

            if (handle == default)
                throw new ArgumentException(ValidationFail_InvalidMessage);

            if (handle.ContainerID != m_ContainerID || handle.Index >= m_List.Values.Count)
                throw new ArgumentException(ValidationFail_ForeignMessage);

            throw new ArgumentException(ValidationFail_DisposedMessage);
        }

        public void Release(VersionedHandle handle)
        {
            ReleaseInternal(ref this[handle]);
        }

        /// <summary>
        /// Shorthand for <see cref="Release(ValidatedHandle)"/> together with
        /// <see cref="IVersionedItem.Handle"/>.
        /// </summary>
        public void Release(in T item)
        {
            ReleaseInternal(ref this[item.Handle]);
        }

        /// <remarks>No checks are performed, may crash.</remarks>
        public void Release(ValidatedHandle handle)
        {
            ref var value = ref this[handle];

            if (!StillExists(handle.Versioned, ref value))
                throw new ObjectDisposedException("TODO"); // Assertion

            ReleaseInternal(ref value);
        }

        public void Dispose()
        {
            m_List.Dispose();
        }

        void ReleaseInternal(ref T value)
        {
            var validated = value.Handle;
            value.Dispose();

            // Invariant:
            if (value.Valid)
                throw new InvalidOperationException("Value should be invalid when destroyed");

            // Should this be done?
            value = default;

            value.Handle = validated.Bump();
            m_List.Release(value.Handle.Versioned.Index);
        }

        bool StillExists(VersionedHandle handle, ref T value)
        {
            return handle.Version == value.Handle.Versioned.Version;
        }
    }
}
