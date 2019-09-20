using NUnit.Framework;
using Unity.Collections;
using System;

namespace Unity.DataFlowGraph.Library.Tests
{
    public unsafe class FreeListTests
    {
        [Test]
        public void DefaultConstructedFreeList_IsNotCreated()
        {
            Assert.IsFalse(new FreeList<int>().IsCreated);
        }

        [Test]
        public void ConstructedFreeList_IsCreated()
        {
            var thing = new FreeList<int>(Allocator.Persistent);
            Assert.IsTrue(thing.IsCreated);
            Assert.DoesNotThrow(() => thing.Dispose());
            Assert.IsFalse(thing.IsCreated);
        }

        [Test]
        public void DefaultConstructedFreeList_ThrowsOnUsingAPI()
        {
            FreeList<int> list = new FreeList<int>();
            int whatever = 0;
            Assert.Throws<ObjectDisposedException>(() => list.Allocate());

            Assert.Throws<IndexOutOfRangeException>(() => whatever = list[0]);
            Assert.Throws<ObjectDisposedException>(() => list.Release(0));
            Assert.Throws<ObjectDisposedException>(list.Dispose);
        }

        [Test]
        public void UsingScopeDeallocatesCorrectly()
        {
            using (var list = new FreeList<int>(Allocator.Persistent))
            {
            }
        }

        [Test]
        public void UsingScopeImproperlyThrows()
        {
            Assert.Throws<ObjectDisposedException>(
                () =>
                {
                    using (var list = new FreeList<int>())
                    {
                    }
                }
            );
        }

        [Test]
        public void UsingScopeWithManualDisposeThrows()
        {
            Assert.Throws<ObjectDisposedException>(
                () =>
                {
                    using (var list = new FreeList<int>(Allocator.Persistent))
                        list.Dispose();
                }
            );
        }

        [Test]
        public void Allocating_IncreasesUncheckedCount()
        {
            using (var list = new FreeList<int>(Allocator.Persistent))
            {
                list.Allocate();

                Assert.GreaterOrEqual(list.UncheckedCount, 1);
                Assert.GreaterOrEqual(list.Values.Count, 1);

            }
        }

        [Test]
        public void ContinuousAllocation_ReturnsUniqueElements()
        {
            const int k_Times = 50;
            using (var list = new FreeList<int>(Allocator.Temp))
            {
                list.Allocate();

                for (int i = 0; i < k_Times; ++i)
                {
                    var newIndex = list.Allocate();
                    fixed (int* newlyAllocated = &list[newIndex])
                    {
                        long diff;
                        fixed (int* old = &list[newIndex - 1])
                            diff = newlyAllocated - old;

                        Assert.GreaterOrEqual(diff, 1);
                    }
                }
            }
        }


        FreeList<T> GenerateFreeListOfSize<T>(int sz, Allocator alloc)
            where T : unmanaged
        {
            var list = new FreeList<T>(alloc);

            for (int i = 0; i < sz; ++i)
                list.Allocate();

            return list;
        }

        [Test]
        public void ContinuousAllocationAndDeallocation_KeepsListMinimallyCompact()
        {
            const int k_Times = 50;

            using (var list = GenerateFreeListOfSize<int>(k_Times, Allocator.Temp))
            {
                for (int i = 0; i < list.UncheckedCount; ++i)
                    list.Release(i);

                for (int i = 0; i < k_Times; ++i)
                {
                    list.Allocate();
                }

                Assert.AreEqual(list.UncheckedCount, k_Times);
            }
        }

        [Test]
        public void AllocationAndDeallocation_KeepsReusingSameIndex()
        {
            const int k_Times = 10;

            using (var list = new FreeList<int>(Allocator.Temp))
            {
                for (int i = 0; i < k_Times; ++i)
                {
                    var index = list.Allocate();
                    list.Release(index);
                    var newIndex = list.Allocate();

                    Assert.AreEqual(index, newIndex);
                }
            }
        }


        [Test]
        public void IndiciesOutOfRangeThrowsErrors()
        {
            int whatever = 0;
            Assert.Throws<IndexOutOfRangeException>(() => whatever = new FreeList<int>()[0]);

            var zeroSizedList = new FreeList<int>(Allocator.Persistent);
            Assert.Throws<IndexOutOfRangeException>(() => whatever = zeroSizedList[0]);

            for (int i = 0; i < 20; ++i)
            {
                using (var list = GenerateFreeListOfSize<int>(i, Allocator.Temp))
                {
                    Assert.Throws<IndexOutOfRangeException>(() => list[i] = 0);
                    Assert.Throws<IndexOutOfRangeException>(() => list[-1] = 0);
                }
            }
        }

        struct Content
        {
            public float a;
            public double b;
        }

        [Test]
        public void ForcedReallocationPreservesContent()
        {
            using (var list = new FreeList<Content>(Allocator.Persistent))
            {
                for (int i = 0; i < 127; ++i)
                {
                    list[list.Allocate()] = new Content { a = 3 + i, b = 3 + i };
                    for (int z = 0; z < list.UncheckedCount; ++z)
                    {
                        Assert.AreEqual(list[z], new Content { a = 3 + z, b = 3 + z });
                    }
                }
            }
        }

        [Test]
        public void ReleasingPreviouslyAllocatedIndicies_Reallocates_InReleasedStackOrder([Values(1, 2, 5, 22)] int count)
        {
            using (var list = GenerateFreeListOfSize<int>(count, Allocator.Temp))
            {
                for (int i = 0; i < count; ++i)
                {
                    list.Release(i);
                }

                for (int i = 0; i < count; ++i)
                {
                    Assert.AreEqual(count - (i + 1), list.Allocate());
                }
            }
        }

        [Test]
        public void InUse_AlwaysReflects_ValueCount_Minus_FreeCount()
        {
            const int k_Upper = 0xFFF;
            using (var list = GenerateFreeListOfSize<int>(k_Upper, Allocator.Temp))
            {
                for (int i = 0; i < k_Upper; ++i)
                {
                    Assert.AreEqual(k_Upper - i, list.InUse);

                    list.Release(i);
                }
            }
        }

    }

}
