using System;

namespace Unity.Collections
{
    struct FreeList<T> : IDisposable
        where T : unmanaged
    {
        public BlitList<T> Values;
        public BlitList<int> Free;

        public bool IsCreated => Values.IsCreated;

        public int UncheckedCount => Values.Count;

        public int InUse => Values.Count - Free.Count;

        public FreeList(Allocator alloc)
        {
            Values = new BlitList<T>(0, alloc);
            Free = new BlitList<int>(0, alloc);
        }

        public int Allocate()
        {
            if (Free.Count > 0)
            {
                var index = Free[Free.Count - 1];
                Free.PopBack();
                return index;
            }

            var value = new T();
            Values.Add(value);

            return Values.Count - 1;
        }

        public ref T this[int index] => ref Values[index];

        public void Release(int index)
        {
            Free.Add(index);
        }

        public void Dispose()
        {
            Values.Dispose();
            Free.Dispose();
        }
    }
}