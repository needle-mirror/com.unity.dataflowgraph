using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace Unity.Collections
{
    using size_t = System.Int32;

    [DebuggerDisplay("Size = {Count}")]
    [DebuggerTypeProxy(typeof(BlitListDebugView<>))]
    unsafe struct BlitList<T> : IDisposable, IEnumerable<T>
        where T : unmanaged
    {
        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            return new Enumerator(this);
        }

        public Enumerator GetEnumerator()
        {
            return new Enumerator(this);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public struct Enumerator : IEnumerator<T>
        {
            private BlitList<T> array;
            private int index;

            public Enumerator(in BlitList<T> array)
            {
                this.array = array;
                this.index = -1;
            }

            public void Dispose()
            {
            }

            public bool MoveNext()
            {
                index++;
                return index < array.Count;
            }

            public void Reset()
            {
                index = -1;
            }

            public T Current
            {
                get
                {
                    return array[index];
                }
            }

            object IEnumerator.Current
            {
                get { return Current; }
            }
        }

        public size_t Count
        {
            get { return m_Size; }
        }

        public size_t Capacity
        {
            get { return m_Capacity; }
        }

        public Allocator AllocationLabel { get; private set; }

        public T* Pointer
        {
            get
            {
                CheckAlive();

                return m_Memory;
            }
        }

        size_t Alignment => UnsafeUtility.AlignOf<T>();

        public bool IsCreated { get { return AllocationLabel != Allocator.Invalid; } }

        public struct ReadOnly
        {
            public size_t Count { get { return m_Size; } }
            T* m_Array;
            size_t m_Size;

            internal ReadOnly(T* Array, size_t activeElements)
            {
                m_Array = Array;
                m_Size = activeElements;
            }

            public ref T this[size_t index]
            {
                get
                {
                    if (m_Size > (uint)index)
                        return ref m_Array[index];

                    throw new IndexOutOfRangeException();
                }
            }
        }

        public ReadOnly AsReadOnly()
        {
            return new ReadOnly(Pointer, m_Size);
        }

        [NativeDisableUnsafePtrRestriction]
        private T* m_Memory;

        private size_t m_Size, m_Capacity;

        public ref T this[size_t index]
        {
            get
            {
                if (m_Size > (uint)index)
                    return ref m_Memory[index];

                throw new IndexOutOfRangeException();
            }
        }

        /// <summary>
        /// PERFORMS NO CHECKING
        /// </summary>
        public T* Ref(size_t index)
        {
            return m_Memory + index;
        }

        public void Add(T item)
        {
            CheckAlive();
            Reserve(m_Size + 1);
            this[m_Size++] = item;
        }

        public void PopBack()
        {
            CheckAlive();
            if (m_Size < 1)
                throw new IndexOutOfRangeException();
            m_Size--;
        }


        public void Remove(size_t index, size_t count)
        {
            CheckAlive();

            if (index + count > m_Size)
                throw new IndexOutOfRangeException();

            size_t missing = m_Size - (index + count);
            for (size_t i = 0; i < missing; ++i)
                this[index + i] = this[index + i + count];

            m_Size -= count;
        }

        /// <summary>
        /// Removes the value at index, swapping the tail into the position, reducing
        /// the size by one and disturbing the order. O(1)
        /// </summary>
        public void RemoveAtSwapBack(size_t index)
        {
            CheckAlive();

            this[index] = this[m_Size - 1];
            m_Size--;
        }

        public void EnsureSize(size_t size)
        {
            CheckAlive();

            Resize(Math.Max(size, m_Size));
        }

        public void Dispose()
        {
            if (IsCreated)
            {
                if(m_Memory != null)
                    UnsafeUtility.Free(m_Memory, AllocationLabel);
                m_Size = m_Capacity = 0;
                AllocationLabel = Allocator.Invalid;
                m_Memory = null;
            }
            else
            {
                throw new ObjectDisposedException("");
            }
        }

        public BlitList(size_t count, Allocator allocationLabel = Allocator.Persistent)
        {
            m_Size = m_Capacity = 0;
            AllocationLabel = allocationLabel;
            m_Memory = null;
            Resize(count);
        }

        public BlitList(BlitList<T> other)
        {
            this = other.Copy();
        }

        public BlitList(NativeArray<T> other, Allocator allocationLabel = Allocator.Persistent)
        {
            m_Size = m_Capacity = 0;
            AllocationLabel = allocationLabel;
            m_Memory = null;
            Resize(other.Length);
            UnsafeUtility.MemCpy(m_Memory, other.GetUnsafeReadOnlyPtr(), m_Size * sizeof(T));
        }

        public BlitList(T[] other, Allocator allocationLabel = Allocator.Persistent)
        {
            m_Size = m_Capacity = 0;
            AllocationLabel = allocationLabel;
            m_Memory = null;
            Resize(other.Length);

            fixed (T* source = other)
            {
                UnsafeUtility.MemCpy(m_Memory, source, m_Size * sizeof(T));
            }
        }

        public BlitList<T> Copy()
        {
            if (!IsCreated)
                throw new ObjectDisposedException("");

            BlitList<T> ret = new BlitList<T>
            {
                m_Size = m_Size,
                m_Capacity = m_Capacity,
                AllocationLabel = AllocationLabel
            };

            ret.m_Memory = (T*)UnsafeUtility.Malloc(m_Capacity * sizeof(T), Alignment, AllocationLabel);
            UnsafeUtility.MemCpy(ret.m_Memory, m_Memory, m_Size * sizeof(T));

            return ret;
        }

        /// <summary>
        /// Copies min(size, other.size) objects from other to this
        /// </summary>
        public void BlitSharedPortion(BlitList<T> other)
        {
            var count = math.min(other.Count, Count);
            UnsafeUtility.MemCpy(Pointer, other.Pointer, count * sizeof(T));
        }

        /// <summary>
        /// Sets <see cref="Count"/> to zero, without changing
        /// <see cref="Capacity"/>
        /// </summary>
        public void Clear() => Resize(0);

        public void Resize(size_t newSize)
        {
            CheckAlive();

            if (m_Size == newSize)
                return;

            if (newSize < m_Size)
            {
                // shrink, kill ctors?
                // for(var i = m_Size - newSize; i < m_Size; ++i)
                //    m_Array[i].~();

                m_Size = newSize;
            }
            else if (newSize < m_Capacity)
            {
                // ctors
                UnsafeUtility.MemClear((void*)Ref(m_Size), (newSize - m_Size) * sizeof(T));
                m_Size = newSize;
            }
            else
            {
                // relocate
                m_Capacity = math.ceilpow2((int)newSize);

                var temp = (T*)UnsafeUtility.Malloc(m_Capacity * sizeof(T), Alignment, AllocationLabel);

                // copy construct
                UnsafeUtility.MemCpy(temp, m_Memory, m_Size * sizeof(T));

                // delete old
                //for (i = 0; i < m_Size; ++i)
                //{
                //    m_Array[i].~();
                //}

                // default construct new
                UnsafeUtility.MemClear(temp + m_Size, (newSize - m_Size) * sizeof(T));

                /*for (i = m_Size; i < newSize; ++i)
                    UnsafeUtility.WriteArrayElement(temp, i, new T()); */


                if (m_Memory != null)
                    UnsafeUtility.Free(m_Memory, AllocationLabel);

                m_Memory = temp;
                m_Size = newSize;
            }
        }

        public void Reserve(size_t newSize)
        {
            CheckAlive();

            if (newSize <= m_Capacity)
                return;

            // relocate
            m_Capacity = math.ceilpow2((int)newSize);

            var temp = (T*)UnsafeUtility.Malloc(m_Capacity * sizeof(T), Alignment, AllocationLabel);

            // copy construct
            UnsafeUtility.MemCpy(temp, m_Memory, m_Size * sizeof(T));

            // delete old
            //for (i = 0; i < m_Size; ++i)
            //{
            //    m_Array[i].~();
            //}

            if (m_Memory != null)
                UnsafeUtility.Free(m_Memory, AllocationLabel);

            m_Memory = temp;
        }

        void CheckAlive()
        {
            if (!IsCreated)
                throw new ObjectDisposedException("NativeList<> not created");
        }

    }

    /// <summary>
    /// DebuggerTypeProxy for <see cref="BlitList{T}"/>
    /// </summary>
    internal sealed class BlitListDebugView<T> where T : unmanaged
    {
        private BlitList<T> m_Array;

        public BlitListDebugView(BlitList<T> array)
        {
            m_Array = array;
        }

        public T[] Items
        {
            get { return m_Array.ToArray(); }
        }
    }
}
