using NUnit.Framework;
using Unity.Collections;
using System;
using System.Linq;

namespace Unity.DataFlowGraph.Library.Tests
{
    public unsafe class BlitListTests
    {
        [Test]
        public void DefaultConstructedBlitListIsNotCreated()
        {
            Assert.IsFalse(new BlitList<int>().IsCreated);
        }

        [Test]
        public void ConstructedBlitListIsCreated()
        {
            var thing = new BlitList<int>(1);
            Assert.IsTrue(thing.IsCreated);
            Assert.DoesNotThrow(() => thing.Dispose());
            Assert.IsFalse(thing.IsCreated);
        }

        [Test]
        public void DefaultConstructedBlitListThrows()
        {
            BlitList<int> list = new BlitList<int>();
            int* whatever;
            Assert.Throws<ObjectDisposedException>(
                () => whatever = list.Pointer
            );

            Assert.Throws<ObjectDisposedException>(list.PopBack);
            Assert.Throws<ObjectDisposedException>(() => list.Resize(0));
            Assert.Throws<ObjectDisposedException>(() => list.Reserve(0));
            Assert.Throws<ObjectDisposedException>(() => list.Copy());
            Assert.Throws<ObjectDisposedException>(() => list.AsReadOnly());
            Assert.Throws<ObjectDisposedException>(() => list.Remove(0, 0));
            Assert.Throws<ObjectDisposedException>(() => list.Add(0));
            Assert.Throws<ObjectDisposedException>(() => list.RemoveAtSwapBack(0));
            Assert.Throws<ObjectDisposedException>(() => list.EnsureSize(0));

            Assert.Throws<ObjectDisposedException>(list.Dispose);
        }

        [Test]
        public void UsingScopeDeallocatesCorrectly()
        {
            using (var list = new BlitList<int>(1, Allocator.Persistent))
            {
            }
        }

        [Test]
        public void UsingScopeCopyDeallocatesCorrectly()
        {
            using (var list = new BlitList<int>(1, Allocator.Persistent))
            using (var copy = list.Copy())
            {
            }
        }

        [Test]
        public void UsingScopeImproperlyThrows()
        {
            Assert.Throws<ObjectDisposedException>(
                () =>
                {
                    using (var list = new BlitList<int>())
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
                    using (var list = new BlitList<int>(1, Allocator.Persistent))
                        list.Dispose();
                }
            );
        }

        [Test]
        public void CopyConstructedListCanBeDisposed()
        {
            using (var list = new BlitList<int>(1, Allocator.Persistent))
            {
                var copy = list.Copy();
                Assert.DoesNotThrow(() => copy.Dispose());
                Assert.Throws<ObjectDisposedException>(() => copy.Dispose());
            }
        }

        [Test]
        public void ConstructedBlitListNonMutatorsDoesNotThrow()
        {
            BlitList<int> list = new BlitList<int>(1, Allocator.Persistent);
            int* whatever;
            Assert.DoesNotThrow(() => whatever = list.Pointer);

            Assert.DoesNotThrow(() => list.Ref(0));

            Assert.DoesNotThrow(() => list.PopBack());
            Assert.DoesNotThrow(() => list.Copy().Dispose());

            Assert.DoesNotThrow(() => list.AsReadOnly());

            Assert.DoesNotThrow(() => list.Dispose());

            Assert.Throws<ObjectDisposedException>(() => list.Dispose());
        }

        [Test]
        public void ConstructedSizeMatchesActualSize()
        {
            for (int i = 0; i < 20; ++i)
            {
                int size = UnityEngine.Random.Range(0, 35);
                using (var list = new BlitList<int>(size, Allocator.Persistent))
                {
                    Assert.AreEqual(size, list.Count);
                }
            }
        }

        [Test]
        public void CopiedListIsCreated()
        {
            using (var list = new BlitList<int>(1, Allocator.Persistent))
            {
                using (var copy = list.Copy())
                {
                    Assert.IsTrue(copy.IsCreated);
                }
            }
        }

        [Test]
        public void CopyConstructedListIsCreated()
        {
            using (var list = new BlitList<int>(1, Allocator.Persistent))
            {
                using (var copy = new BlitList<int>(list))
                {
                    Assert.IsTrue(copy.IsCreated);
                }
            }
        }

        [Test]
        public void CopyConstructedListHasEqualSizeAndCapacity()
        {
            for (int i = 0; i < 20; ++i)
            {
                using (var list = new BlitList<int>(UnityEngine.Random.Range(0, 35), Allocator.Persistent))
                {
                    using (var copy = new BlitList<int>(list))
                    {
                        Assert.AreEqual(list.Count, copy.Count);
                        Assert.AreEqual(list.Capacity, copy.Capacity);
                    }
                }
            }
        }

        [Test]
        public void CopiedListHasEqualSizeAndCapacity()
        {
            for (int i = 0; i < 20; ++i)
            {
                using (var list = new BlitList<int>(UnityEngine.Random.Range(0, 35), Allocator.Persistent))
                {
                    using (var copy = list.Copy())
                    {
                        Assert.AreEqual(list.Count, copy.Count);
                        Assert.AreEqual(list.Capacity, copy.Capacity);
                    }
                }
            }
        }

        // mutations

        [Test]
        public void ResizeSizeMatchesActualSize()
        {
            using (var list = new BlitList<int>(1, Allocator.Persistent))
            {
                for (int i = 0; i < 20; ++i)
                {
                    int size = UnityEngine.Random.Range(0, 35);
                    {
                        list.Resize(size);
                        Assert.AreEqual(size, list.Count);
                    }
                }
            }
        }

        [Test]
        public void ReserveSizeIsLessOrEqualToCapacity()
        {
            using (var list = new BlitList<int>(1, Allocator.Persistent))
            {
                for (int i = 0; i < 20; ++i)
                {
                    int size = UnityEngine.Random.Range(0, 35);
                    {
                        list.Reserve(size);
                        Assert.LessOrEqual(size, list.Capacity);
                    }
                }
            }
        }

        [Test]
        public void PopBackDecreasesSizeByOne()
        {
            for (int i = 0; i < 20; ++i)
            {
                int size = UnityEngine.Random.Range(1, 35);
                using (var list = new BlitList<int>(size, Allocator.Persistent))
                {
                    list.PopBack();
                    Assert.AreEqual(size - 1, list.Count);
                }
            }
        }

        [Test]
        public void PopBackKeepsCapacity()
        {
            for (int i = 0; i < 20; ++i)
            {
                int size = UnityEngine.Random.Range(1, 35);
                using (var list = new BlitList<int>(size, Allocator.Persistent))
                {
                    var currentCapacity = list.Capacity;
                    list.PopBack();
                    Assert.AreEqual(currentCapacity, list.Capacity);
                }
            }
        }

        [Test]
        public void RemoveSwapBackDecreasesSizeByOne()
        {
            for (int i = 0; i < 20; ++i)
            {
                int size = UnityEngine.Random.Range(1, 35);
                using (var list = new BlitList<int>(size, Allocator.Persistent))
                {
                    list.RemoveAtSwapBack(list.Count - 1);
                    Assert.AreEqual(size - 1, list.Count);
                }
            }
        }

        [Test]
        public void CanAccessAllIndiciesReadWriteBackWithoutErrors()
        {
            for (int i = 0; i < 20; ++i)
            {
                int size = UnityEngine.Random.Range(1, 35);
                var list = new BlitList<int>(size, Allocator.Persistent);
                {
                    for (int z = 0; z < size; ++z)
                    {
                        list[z] = z;
                        list.Ref(z);
                        Assert.AreEqual(list[z], z);
                    }
                }
                list.Dispose();
            }

            for (int i = 0; i < 20; ++i)
            {
                int size = UnityEngine.Random.Range(1, 35);
                var list = new BlitList<double>(size, Allocator.Persistent);
                {
                    for (int z = 0; z < size; ++z)
                    {
                        list[z] = z;
                        list.Ref(z);
                        Assert.AreEqual(list[z], z);
                    }
                }
                list.Dispose();
            }
        }

        [Test]
        public void ZeroSizedListIsValid()
        {
            Assert.DoesNotThrow(
                () =>
                {
                    using (var list = new BlitList<int>(0, Allocator.Persistent))
                    {
                    }
                }
            );
        }

        [Test]
        public void IndiciesOutOfRangeThrowsErrors()
        {
            int whatever = 0;
            Assert.Throws<IndexOutOfRangeException>(() => whatever = new BlitList<int>()[0]);

            var zeroSizedList = new BlitList<int>(0, Allocator.Persistent);
            Assert.Throws<IndexOutOfRangeException>(() => whatever = zeroSizedList[0]);

            for (int i = 0; i < 20; ++i)
            {
                int size = UnityEngine.Random.Range(1, 35);
                using (var list = new BlitList<int>(size, Allocator.Persistent))
                {
                    Assert.Throws<IndexOutOfRangeException>(() => list[size] = 0);
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
            using (var list = new BlitList<Content>(0, Allocator.Persistent))
            {
                for (int i = 0; i < 127; ++i)
                {
                    list.Add(new Content { a = 3 + i, b = 3 + i });
                    for (int z = 0; z < list.Count; ++z)
                    {
                        Assert.AreEqual(list[z], new Content { a = 3 + z, b = 3 + z });
                    }
                }
            }
        }

        [Test]
        public void ResizedBlitList_IsZeroInitialized()
        {
            using (var source = new BlitList<Content>(0, Allocator.Temp))
            {
                source.Reserve(16);
                for (int i = 0; i < 16; ++i)
                {
                    source.EnsureSize(i + 1);
                    Assert.Zero(source[i].a);
                    Assert.Zero(source[i].b);
                }
            }
        }

        [Test]
        public void ForEachTest()
        {
            const int listSize = 20;
            using (var nativeList = new BlitList<int>(listSize))
            {
                for (int i = 0; i < listSize; ++i)
                    nativeList[i] = i;

                int counter = 0;
                foreach (int nativeListIt in nativeList)
                    Assert.AreEqual(nativeListIt, counter++);

                Assert.AreEqual(listSize, counter);
            }



        }

        [Test]
        public void InitiallySizedBlitList_IsZeroInitialized()
        {
            using (var source = new BlitList<int>(15, Allocator.Temp))
            {
                for (int i = 0; i < source.Count; ++i)
                    Assert.Zero(source[i]);

                foreach (var item in source)
                    Assert.Zero(item);
            }
        }

        [Test]
        public void InplaceBlit_CopiesValidRanges()
        {
            using (var source = new BlitList<int>(Enumerable.Range(0, 15).ToArray(), Allocator.Temp))
            {
                using (var equalDest = new BlitList<int>(15, Allocator.Temp))
                {
                    equalDest.BlitSharedPortion(source);

                    for (int i = 0; i < source.Count; ++i)
                        Assert.AreEqual(source[i], equalDest[i]);
                }

                using (var lesserDest = new BlitList<int>(10, Allocator.Temp))
                {
                    lesserDest.BlitSharedPortion(source);

                    for (int i = 0; i < lesserDest.Count; ++i)
                        Assert.AreEqual(source[i], lesserDest[i]);
                }

                using (var largerDest = new BlitList<int>(20, Allocator.Temp))
                {
                    largerDest.BlitSharedPortion(source);

                    for (int i = 0; i < source.Count; ++i)
                        Assert.AreEqual(source[i], largerDest[i]);
                }
            }
        }

        [TestCase(1), TestCase(15), TestCase(300)]
        public void ManagedSourceCopyInitialization_ContainsEqualItems(int count)
        {
            var managedList = Enumerable.Range(0, count).ToArray();
            using (var list = new BlitList<int>(managedList, Allocator.Persistent))
            {
                for (int i = 0; i < list.Count; ++i)
                {
                    Assert.AreEqual(managedList[i], list[i]);
                }
            }
        }

        [TestCase(1), TestCase(15), TestCase(300)]
        public void NativeArraySourceCopyInitialization_ContainsEqualItems(int count)
        {
            using (var nativeArray = new NativeArray<int>(Enumerable.Range(0, count).ToArray(), Allocator.Temp))
            {
                using (var list = new BlitList<int>(nativeArray, Allocator.Persistent))
                {
                    for (int i = 0; i < list.Count; ++i)
                    {
                        Assert.AreEqual(nativeArray[i], list[i]);
                    }
                }
            }
        }
    }
}
