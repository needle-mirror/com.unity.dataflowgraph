using NUnit.Framework;
using Unity.Collections;
using System;
using System.Linq;
using System.Collections.Generic;

namespace Unity.DataFlowGraph.Library.Tests
{
    public unsafe class VersionedListTests
    {

        struct VersionedItem : IVersionedNode
        {
            public VersionedHandle VHandle { get; set; }

            public bool Valid { get; set; }
        }

        [Test]
        public void DefaultConstructedVersionedList_IsNotCreated()
        {
            Assert.IsFalse(new VersionedList<VersionedItem>().IsCreated);
        }

        [Test]
        public void ConstructedVersionedList_IsCreated()
        {
            var thing = new VersionedList<VersionedItem>(Allocator.Temp, 99);
            Assert.IsTrue(thing.IsCreated);
            Assert.DoesNotThrow(() => thing.Dispose());
            Assert.IsFalse(thing.IsCreated);
        }

        [Test]
        public void DefaultConstructedVersionedList_ThrowsOnUsingAPI()
        {
            VersionedList<VersionedItem> list = new VersionedList<VersionedItem>();
            VersionedItem whatever = default;
            Assert.Throws<ObjectDisposedException>(() => list.Allocate());

            Assert.Throws<IndexOutOfRangeException>(() => whatever = list[0]);
            Assert.Throws<IndexOutOfRangeException>(() => whatever = list[whatever.VHandle]);

            Assert.Throws<IndexOutOfRangeException>(() => list.Release(whatever.VHandle));
            Assert.Throws<IndexOutOfRangeException>(() => list.Release(whatever));

            Assert.Throws<ObjectDisposedException>(list.Dispose);
        }

        [Test]
        public void UsingScopeDeallocatesCorrectly()
        {
            using (var list = new VersionedList<VersionedItem>(Allocator.Temp, 99))
            {
            }
        }

        [Test]
        public void UsingScopeImproperlyThrows()
        {
            Assert.Throws<ObjectDisposedException>(
                () =>
                {
                    using (var list = new VersionedList<VersionedItem>())
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
                    using (var list = new VersionedList<VersionedItem>(Allocator.Temp, 99))
                        list.Dispose();
                }
            );
        }

        [Test]
        public void Allocating_IncreasesUncheckedCount()
        {
            using (var list = new VersionedList<VersionedItem>(Allocator.Temp, 99))
            {
                for (int i = 0; i < 100; ++i)
                {
                    list.Allocate();

                    Assert.AreEqual(i + 1, list.UncheckedCount);
                }
            }
        }

        [Test]
        public void VHandleIndex_MatchesAllocationNumber()
        {
            using (var list = new VersionedList<VersionedItem>(Allocator.Temp, 99))
            {
                for (int i = 0; i < 100; ++i)
                {
                    var item = list.Allocate();
                    Assert.AreEqual(i, item.VHandle.Index);
                }
            }
        }

        [Test]
        public void VHandleVersion_StartsAtOne()
        {
            using (var list = new VersionedList<VersionedItem>(Allocator.Temp, 99))
            {
                for (int i = 0; i < 100; ++i)
                {
                    var item = list.Allocate();
                    Assert.AreEqual(1, item.VHandle.Version);
                }
            }
        }

        [Test]
        public void RepeatedAllocationAndRelease_IncreasesVersionNumber()
        {
            using (var list = new VersionedList<VersionedItem>(Allocator.Temp, 99))
            {
                for (int i = 0; i < 100; ++i)
                {
                    var item = list.Allocate();
                    Assert.AreEqual(i + 1, item.VHandle.Version);
                    list.Release(item);
                }
            }
        }

        [Test]
        public void AllIndexers_ThrowOutOfRangeExceptions_ForInvalidItemIndices()
        {
            using (var list = new VersionedList<VersionedItem>(Allocator.Temp, 99))
            {
                for (int i = 0; i < 10; ++i)
                {
                    var item = list.Allocate();
                    Assert.Throws<IndexOutOfRangeException>(() => item = list[-1]);
                    Assert.Throws<IndexOutOfRangeException>(() => item = list[i + 1]);

                    VersionedHandle handlePlus = item.VHandle;
                    VersionedHandle handleMinus = item.VHandle;

                    handlePlus.Index++;
                    handleMinus.Index = -1;

                    Assert.Throws<IndexOutOfRangeException>(() => item = list.Resolve(handlePlus));
                    Assert.Throws<IndexOutOfRangeException>(() => item = list[handlePlus]);

                    Assert.Throws<IndexOutOfRangeException>(() => item = list.Resolve(handleMinus));
                    Assert.Throws<IndexOutOfRangeException>(() => item = list[handleMinus]);
                }
            }
        }

        [Test]
        public void ReleasingValidItem_Throws()
        {
            using (var list = new VersionedList<VersionedItem>(Allocator.Temp, 99))
            {
                ref var item = ref list.Allocate();
                item.Valid = true;
                var vhandle = item.VHandle;
                Assert.Throws<InvalidOperationException>(() => list.Release(vhandle));
            }
        }

        [Test]
        public void CleaningUpItems_IsNotRequired()
        {
            using (var list = new VersionedList<VersionedItem>(Allocator.Temp, 99))
            {
                list.Allocate();
            }
        }

        [Test]
        public void DefaultConstructedItem_DoesNotExist()
        {
            using (var list = new VersionedList<VersionedItem>(Allocator.Temp, 99))
            {
                var item = new VersionedItem();
                Assert.IsFalse(list.Exists(item.VHandle));
            }
        }

        [Test]
        public void VHandleOfFirstAllocation_DoesNotMatchDefaultConstructedItem()
        {
            using (var list = new VersionedList<VersionedItem>(Allocator.Temp, 99))
            {
                var item = new VersionedItem();
                var item2 = list.Allocate();

                Assert.AreNotEqual(item.VHandle, item2.VHandle);
                Assert.IsFalse(list.Exists(item.VHandle));
            }
        }

        [Test]
        public void VHandleFromOneList_DoesNotExistInAnother()
        {
            using (var list1 = new VersionedList<VersionedItem>(Allocator.Temp, 1))
            using (var list2 = new VersionedList<VersionedItem>(Allocator.Temp, 2))
            {
                var item1 = list1.Allocate();
                var item2 = list2.Allocate();

                Assert.AreNotEqual(item1.VHandle, item2.VHandle);
                Assert.IsFalse(list1.Exists(item2.VHandle));
                Assert.IsFalse(list2.Exists(item1.VHandle));
            }
        }

        [Test]
        public void AllocatedItem_DoesNotExist_AfterReleasingIt()
        {
            using (var list = new VersionedList<VersionedItem>(Allocator.Temp, 99))
            {
                var item = list.Allocate();

                Assert.IsTrue(list.Exists(item.VHandle));
                list.Release(item.VHandle);
                Assert.IsFalse(list.Exists(item.VHandle));
            }
        }

        [Test]
        unsafe public void AllIndexers_ReturnSameMemoryLocation()
        {
            using (var list = new VersionedList<VersionedItem>(Allocator.Temp, 99))
            {
                fixed (VersionedItem* item = &list.Allocate())
                {
                    fixed (VersionedItem* temp = &list[item->VHandle.Index])
                        Assert.IsTrue(item == temp);

                    fixed (VersionedItem* temp = &list[item->VHandle])
                        Assert.IsTrue(item == temp);

                    fixed (VersionedItem* temp = &list.Resolve(item->VHandle))
                        Assert.IsTrue(item == temp);
                }
            }
        }

        [Test]
        public void StressTest_CreatingAndDestroyingEntries_MakesCoherentIndex_AndUpdatesVersion_Appropriately([Values(1, 133, 13)] int seed)
        {
            const int k_Actions = 5000;

            var device = new Mathematics.Random(seed: (uint)seed);

            var versions = new Dictionary<int, int>();
            var destroyed = new List<int>();
            var existing = new List<int>();

            int upperBound = 0;

            using (var list = new VersionedList<VersionedItem>(Allocator.Temp, 99))
            {
                for (int i = 0; i < k_Actions; ++i)
                {
                    var shouldCreate = device.NextBool();

                    if (!shouldCreate && existing.Count == 0)
                        shouldCreate = true;

                    if (shouldCreate)
                    {
                        int expectedIndex = 0;

                        if (destroyed.Count == 0)
                        {
                            expectedIndex = upperBound++;
                        }
                        else
                        {
                            expectedIndex = destroyed.Last();
                            destroyed.RemoveAt(destroyed.Count - 1);
                        }

                        ref var item = ref list.Allocate();
                        Assert.AreEqual(expectedIndex, item.VHandle.Index, i.ToString());

                        if (!versions.ContainsKey(item.VHandle.Index))
                            versions[item.VHandle.Index] = item.VHandle.Version;

                        Assert.AreEqual(versions[item.VHandle.Index], item.VHandle.Version, i.ToString());

                        existing.Add(item.VHandle.Index);
                    }
                    else
                    {
                        var indirectIndexToRelease = device.NextInt(existing.Count);

                        var index = existing[indirectIndexToRelease];
                        existing.RemoveAt(indirectIndexToRelease);
                        destroyed.Add(index);

                        list[index].Valid = false;
                        list.Release(list[index].VHandle);
                        versions[index] = versions[index] + 1;

                    }
                }
            }

            versions.Clear();

        }

    }
}
