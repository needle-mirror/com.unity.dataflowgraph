using NUnit.Framework;
using Unity.Collections;
using System;
using System.Linq;
using System.Collections.Generic;

namespace Unity.DataFlowGraph.Library.Tests
{
    using ValidatedHandle = Collections.ValidatedHandle;

    public unsafe class VersionedListTests
    {
        struct VersionedItem : IVersionedItem
        {
            public ValidatedHandle Handle { get; set; }

            public bool Valid { get; set; }

            public void Dispose() { Valid = false; }
        }

        struct VersionedItem_ThatDoesNotCleanUp : IVersionedItem
        {
            public ValidatedHandle Handle { get; set; }

            public bool Valid { get; set; }

            public void Dispose() { }
        }

        struct VersionedItem_ThatDoesCleanUp : IVersionedItem
        {
            public ValidatedHandle Handle { get; set; }

            public bool Valid { get; set; }

            public void Dispose() { Valid = false; }
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
        public void DefaultConstructedVersionedList_ThrowsOnUsingSafeAPI()
        {
            VersionedList<VersionedItem> list = new VersionedList<VersionedItem>();
            VersionedItem whatever = default;

            Assert.Throws<ObjectDisposedException>(() => list.Allocate());
            Assert.Throws<IndexOutOfRangeException>(() => whatever = list.UnvalidatedItemAt(0));
            Assert.Throws<ObjectDisposedException>(list.Dispose);
        }

        [Test, Explicit]
        public void DefaultConstructedVersionedList_ThrowsOnUsingUnsafeAPI()
        {
            // TODO: Decide on fate for this behaviour
            VersionedList<VersionedItem> list = new VersionedList<VersionedItem>();
            VersionedItem whatever = default;
            // Following two are technically a crash.. since no validation.
            Assert.Throws<NullReferenceException>(() => whatever = list[whatever.Handle]);
            Assert.Throws<NullReferenceException>(() => whatever = list[new ValidatedHandle()]);
            Assert.Throws<NullReferenceException>(() => list.Release(whatever.Handle));
        }

        [Test]
        public void Default_ValidatedHandle_IsOkayToIndex_ButIsNotValid()
        {
            using (VersionedList<VersionedItem> list = new VersionedList<VersionedItem>(Allocator.Temp, 99))
            {
                ValidatedHandle handle = default;
                Assert.False(list[handle].Valid);
                Assert.False(list.StillExists(handle));
            }
        }

        [Test]
        public void Default_VersionedHandle_ThrowsOnIndexing()
        {
            using (VersionedList<VersionedItem> list = new VersionedList<VersionedItem>(Allocator.Temp, 99))
            {
                VersionedHandle handle = default;
                bool _ = false;
                Assert.Throws<ArgumentException>(() => _ = list[handle].Valid);
                Assert.False(list.Exists(handle));
            }
        }

        [Test]
        public void Validation_ThrowsExpectedErrorMessages()
        {
            using (VersionedList<VersionedItem> list = new VersionedList<VersionedItem>(Allocator.Temp, 99))
            {
                Exception e;
                // test default constructed handles
                VersionedHandle handle = default;

                e = Assert.Throws<ArgumentException>(() => list.Validate(handle));
                StringAssert.Contains(VersionedList<VersionedItem>.ValidationFail_InvalidMessage, e.Message);

                var valid = list.Allocate().Handle.Versioned;

                // test a handle coming from another list
                using (VersionedList<VersionedItem> listForeign = new VersionedList<VersionedItem>(Allocator.Temp, 100))
                {
                    handle = listForeign.Allocate().Handle.Versioned;

                    e = Assert.Throws<ArgumentException>(() => list.Validate(handle));
                    StringAssert.Contains(VersionedList<VersionedItem>.ValidationFail_ForeignMessage, e.Message);
                }

                // artificially make an out of bounds handle; should be detected as foreign.
                handle = VersionedHandle.Create_ForTesting(valid.Index + 20, valid.Version, valid.ContainerID);
                e = Assert.Throws<ArgumentException>(() => list.Validate(handle));
                StringAssert.Contains(VersionedList<VersionedItem>.ValidationFail_ForeignMessage, e.Message);

                // now, dispose and check it is caught.

                handle = valid;
                list.Release(valid);
                e = Assert.Throws<ArgumentException>(() => list.Validate(handle));
                StringAssert.Contains(VersionedList<VersionedItem>.ValidationFail_DisposedMessage, e.Message);
            }
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

        struct VItem_WithDisposeCounter : IVersionedItem
        {
            public static int Counter;
            public ValidatedHandle Handle { get; set; }

            public bool Valid { get;set; }

            public void Dispose()
            {
                Counter++;
            }
        }

        [Test]
        public void Dispose_IsCalledOnNodes()
        {
            VItem_WithDisposeCounter.Counter = 0;

            using (var list = new VersionedList<VItem_WithDisposeCounter>(Allocator.Temp, 99))
            {
                for (int i = 0; i < 10; ++i)
                {
                    list.Allocate();

                    Assert.AreEqual(0, VItem_WithDisposeCounter.Counter);
                }

                for (int i = 0; i < 10; ++i)
                {
                    list.Release(list.UnvalidatedItemAt(i).Handle);
                    Assert.AreEqual(i + 1, VItem_WithDisposeCounter.Counter);
                }
            }
        }

        struct VItem_WithPayload : IVersionedItem
        {
            public int Payload;
            public ValidatedHandle Handle { get; set; }

            public bool Valid { get; set; }

            public void Dispose() { }
        }

        [Test]
        public void NodeMemory_IsCleanedUp()
        {
            using (var list = new VersionedList<VItem_WithPayload>(Allocator.Temp, 99))
            {
                ref var item = ref list.Allocate();
                item.Payload = 15;
                list.Release(item.Handle);
                Assert.AreEqual(0, item.Payload);
            }
        }

        [Test]
        public void NodesAreNotDisposedAutomatically_WhenDisposingList()
        {
            VItem_WithDisposeCounter.Counter = 0;
            using (var list = new VersionedList<VItem_WithDisposeCounter>(Allocator.Temp, 99))
            {
                ref var item = ref list.Allocate();
            }

            Assert.AreEqual(0, VItem_WithDisposeCounter.Counter);
        }

        [Test]
        public void Allocating_IncreasesUncheckedCount()
        {
            using (var list = new VersionedList<VersionedItem>(Allocator.Temp, 99))
            {
                for (int i = VersionedList<VersionedItem>.ValidOffset; i < 100; ++i)
                {
                    list.Allocate();

                    Assert.AreEqual(i + 1, list.UnvalidatedCount);
                }
            }
        }

        [Test]
        public void VHandleIndex_MatchesAllocationNumber()
        {
            using (var list = new VersionedList<VersionedItem>(Allocator.Temp, 99))
            {
                for (int i = VersionedList<VersionedItem>.ValidOffset; i < 100; ++i)
                {
                    var item = list.Allocate();
                    Assert.AreEqual(i, item.Handle.Versioned.Index);
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
                    Assert.AreEqual(1, item.Handle.Versioned.Version);
                }
            }
        }

        [Test]
        public void VHandleIndex_StartsAtOne()
        {
            using (var list = new VersionedList<VersionedItem>(Allocator.Temp, 99))
            {
                var item = list.Allocate();
                Assert.AreEqual(1, item.Handle.Versioned.Index);
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
                    Assert.AreEqual(i + 1, item.Handle.Versioned.Version);
                    list.Release(item.Handle.Versioned);
                }
            }
        }

        [Test]
        public void At_ThrowOutOfRangeExceptions_ForInvalidItemIndices()
        {
            using (var list = new VersionedList<VersionedItem>(Allocator.Temp, 99))
            {
                for (int i = VersionedList<VersionedItem>.ValidOffset; i < 10; ++i)
                {
                    var item = list.Allocate();
                    Assert.Throws<IndexOutOfRangeException>(() => item = list.UnvalidatedItemAt(-1));
                    Assert.Throws<IndexOutOfRangeException>(() => item = list.UnvalidatedItemAt(i + 1));
                }
            }
        }

        [Test]
        public void VersionedIndexers_ThrowArgumentException_ForInvalidItemIndices()
        {
            // This test is temporary - in the future user shouldn't be able to manipulate
            // indices or versions.
            // The versioned list probably still needs to throw on alien arguments.

            using (var list = new VersionedList<VersionedItem>(Allocator.Temp, 99))
            {
                for (int i = 0; i < 10; ++i)
                {
                    var item = list.Allocate();

                    var handlePlus = VersionedHandle.Create_ForTesting(
                        item.Handle.Versioned.Index + 1,
                        item.Handle.Versioned.Version,
                        item.Handle.Versioned.ContainerID
                    );

                    var handleMinus = VersionedHandle.Create_ForTesting(
                        -1,
                        item.Handle.Versioned.Version,
                        item.Handle.Versioned.ContainerID
                    );

                    Assert.Throws<ArgumentException>(() => item = list[handlePlus]);
                    Assert.Throws<ArgumentException>(() => item = list[handleMinus]);
                }
            }
        }

        [Test]
        public unsafe void ValidatedIndexers_DoNotThrowArgumentException_ForInvalidItemIndices()
        {
            // See comment in above test as well
            using (var list = new VersionedList<VersionedItem>(Allocator.Temp, 99))
            {
                for (int i = 0; i < 10; ++i)
                {
                    var item = list.Allocate();

                    var handlePlus = VersionedHandle.Create_ForTesting(
                        item.Handle.Versioned.Index + 1,
                        item.Handle.Versioned.Version,
                        item.Handle.Versioned.ContainerID
                    );

                    var handleMinus = VersionedHandle.Create_ForTesting(
                        -1,
                        item.Handle.Versioned.Version,
                        item.Handle.Versioned.ContainerID
                    );

                    var validatedPlus = ValidatedHandle.Create_ForTesting(handlePlus);
                    var validatedMinus = ValidatedHandle.Create_ForTesting(handlePlus);

                    // These pointers are invalid - point to plus one and minus one.
                    // Dereferencing them is UB.
                    // The test here confirms we don't do bounds checking on validated handles.
                    fixed (VersionedItem* pointer = &list[validatedPlus])
                    {

                    }

                    fixed (VersionedItem* pointer = &list[validatedMinus])
                    {

                    }
                }
            }
        }

        [Test]
        public void ReleasingValidItem_ThatDoesNotCleanUpInDispose_Throws()
        {
            using (var list = new VersionedList<VersionedItem_ThatDoesNotCleanUp>(Allocator.Temp, 99))
            {
                ref var item = ref list.Allocate();
                item.Valid = true;
                var handle = item.Handle;
                Assert.Throws<InvalidOperationException>(() => list.Release(handle));
            }
        }

        [Test]
        public void ReleasingValidItem_ThatDoesCleanUp_IsOK()
        {
            using (var list = new VersionedList<VersionedItem_ThatDoesCleanUp>(Allocator.Temp, 99))
            {
                ref var item = ref list.Allocate();
                item.Valid = true;
                list.Release(item);
            }
        }

        [Test]
        public void DoubleReleasing_AllocatedItem_Throws()
        {
            using (var list = new VersionedList<VersionedItem>(Allocator.Temp, 99))
            {
                ref var item = ref list.Allocate();
                var self = item.Handle;
                list.Release(self);
                Assert.Throws<ObjectDisposedException>(() => list.Release(self));
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
                Assert.IsFalse(list.Exists(item.Handle.Versioned));
                Assert.IsFalse(list.StillExists(item.Handle));
            }
        }

        [Test]
        public void DefaultConstructedEnumerator_ThrowsException_OnDereference()
        {
            VersionedItem dummy;
            Assert.Throws<InvalidOperationException>(() => dummy = new VersionedList<VersionedItem>.Enumerator().Current);
        }

        [Test]
        public void VHandleOfFirstAllocation_DoesNotMatchDefaultConstructedItem()
        {
            using (var list = new VersionedList<VersionedItem>(Allocator.Temp, 99))
            {
                var item = new VersionedItem();
                var item2 = list.Allocate();

                Assert.AreNotEqual(item.Handle, item2.Handle);
                Assert.IsFalse(list.Exists(item.Handle.Versioned));
                Assert.IsFalse(list.StillExists(item.Handle));
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

                Assert.AreNotEqual(item1.Handle, item2.Handle);
                Assert.IsFalse(list1.Exists(item2.Handle.Versioned));
                Assert.IsFalse(list2.Exists(item1.Handle.Versioned));
            }
        }

        [Test]
        public void AllocatedItem_DoesNotExist_AfterReleasingIt()
        {
            using (var list = new VersionedList<VersionedItem>(Allocator.Temp, 99))
            {
                var item = list.Allocate();

                Assert.IsTrue(list.StillExists(item.Handle));
                list.Release(item.Handle);
                Assert.IsFalse(list.StillExists(item.Handle));
            }
        }

        [Test]
        unsafe public void AllIndexers_ReturnSameMemoryLocation()
        {
            using (var list = new VersionedList<VersionedItem>(Allocator.Temp, 99))
            {
                fixed (VersionedItem* item = &list.Allocate())
                {
                    fixed (VersionedItem* temp = &list.UnvalidatedItemAt(item->Handle.Versioned.Index))
                        Assert.IsTrue(item == temp);

                    fixed (VersionedItem* temp = &list[item->Handle])
                        Assert.IsTrue(item == temp);

                    fixed (VersionedItem* temp = &list[list.Validate(item->Handle.Versioned)])
                        Assert.IsTrue(item == temp);
                }
            }
        }

        [Test]
        public void ValidEnumerator_EnumeratesAllValidItems()
        {
            const int Items = 50;

            var validItems = new HashSet<ValidatedHandle>();

            using (var list = new VersionedList<VersionedItem>(Allocator.Temp, 99))
            {
                for(int i = 0; i < Items; ++i)
                {
                    ref var item = ref list.Allocate();
                    item.Valid = true;
                    validItems.Add(item.Handle);
                }

                foreach (var item in list.Items)
                    Assert.IsTrue(validItems.Contains(item.Handle));
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

            int upperBound = VersionedList<VersionedItem>.ValidOffset;

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
                        Assert.AreEqual(expectedIndex, item.Handle.Versioned.Index, i.ToString());

                        if (!versions.ContainsKey(item.Handle.Versioned.Index))
                            versions[item.Handle.Versioned.Index] = item.Handle.Versioned.Version;

                        Assert.AreEqual(versions[item.Handle.Versioned.Index], item.Handle.Versioned.Version, i.ToString());

                        existing.Add(item.Handle.Versioned.Index);
                    }
                    else
                    {
                        var indirectIndexToRelease = device.NextInt(existing.Count);

                        var index = existing[indirectIndexToRelease];
                        existing.RemoveAt(indirectIndexToRelease);
                        destroyed.Add(index);

                        list.Release(list.UnvalidatedItemAt(index).Handle.Versioned);
                        versions[index] = versions[index] + 1;

                    }
                }
            }

            versions.Clear();

        }

    }
}
