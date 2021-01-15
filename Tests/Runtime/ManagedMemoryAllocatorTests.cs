using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.DataFlowGraph.Tests
{
    using ConformingAllocator = DefaultManagedAllocator<ManagedMemoryAllocatorTests.ManagedStruct>;

    public unsafe class ManagedMemoryAllocatorTests
    {
        const int k_DefaultObjectSize = 4;
        const int k_DefaultObjectPool = 4;

        public enum Parameter
        {
            Size, Pool
        }

        public static void RequestGarbageCollection()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        public static void AssertManagedObjectsReleased(Action action)
        {
            action();
            const int k_TimeOut = 5;
            var time = Time.realtimeSinceStartup;

            while (Time.realtimeSinceStartup - time < k_TimeOut)
            {
                if (ManagedObject.Instances == 0)
                    break;
                RequestGarbageCollection();
            }

            Assert.Zero(ManagedObject.Instances, "Managed object was not released by the GC in time");
        }

        [Test]
        public void DefaultConstructedAllocator_IsNotCreated()
        {
            ManagedMemoryAllocator allocator = new ManagedMemoryAllocator();
            Assert.IsFalse(allocator.IsCreated);
        }

        [Test]
        public void AllPublicAPI_ThrowsDisposedException_WhenNotCreated()
        {
            ManagedMemoryAllocator allocator = new ManagedMemoryAllocator();
            Assert.Throws<ObjectDisposedException>(() => allocator.Alloc());
            Assert.Throws<ObjectDisposedException>(() => allocator.Free(null));
            Assert.Throws<ObjectDisposedException>(() => allocator.Dispose());
        }

        public class ZeroSizedManagedMemoryAllocator : IManagedMemoryPoolAllocator
        {
            public int ObjectSize => 0;

            public void* AllocatePrepinnedGCArray(int count, out ulong gcHandle) => throw new NotImplementedException();
        }

        public class NegativeSizedManagedMemoryAllocator : IManagedMemoryPoolAllocator
        {
            public int ObjectSize => -1;
            public void* AllocatePrepinnedGCArray(int count, out ulong gcHandle) => throw new NotImplementedException();
        }

        [Test]
        public void CreationArguments_AreValidated_AndThrowExceptions()
        {
            // argument constraints are documented in the class documentation,
            // but every argument must be above 0.

            Assert.Throws<ArgumentNullException>(() => new ManagedMemoryAllocator(null, 1));
            Assert.Throws<ArgumentException>(() => new ManagedMemoryAllocator(new ZeroSizedManagedMemoryAllocator(), 1));
            Assert.Throws<ArgumentException>(() => new ManagedMemoryAllocator(new NegativeSizedManagedMemoryAllocator(), 1));

            Assert.Throws<ArgumentException>(() => new ManagedMemoryAllocator(new ConformingAllocator(), 0));
            Assert.Throws<ArgumentException>(() => new ManagedMemoryAllocator(new ConformingAllocator(), -1));
        }

        [Test]
        public void CreatingAndDisposingAllocator_Works()
        {
            using (var allocator = new ManagedMemoryAllocator(new ConformingAllocator(), k_DefaultObjectPool))
                Assert.IsTrue(allocator.IsCreated);
        }

        unsafe class NSizedItemAllocator : IManagedMemoryPoolAllocator
        {
            public const int k_MaxSize = 10;

            struct Size1 { fixed byte _[1]; }
            struct Size2 { fixed byte _[2]; }
            struct Size3 { fixed byte _[3]; }
            struct Size4 { fixed byte _[4]; }
            struct Size5 { fixed byte _[5]; }
            struct Size6 { fixed byte _[6]; }
            struct Size7 { fixed byte _[7]; }
            struct Size8 { fixed byte _[8]; }
            struct Size9 { fixed byte _[9]; }
            struct Size10 { fixed byte _[10]; }

            public int ObjectSize { get; private set; }

            public NSizedItemAllocator(int size)
            {
                ObjectSize = size;
            }

            public void* AllocatePrepinnedGCArray(int count, out ulong gcHandle)
            {
                Array array = null;

                switch(ObjectSize)
                {
                    case 1: array = new Size1[count]; break;
                    case 2: array = new Size2[count]; break;
                    case 3: array = new Size3[count]; break;
                    case 4: array = new Size4[count]; break;
                    case 5: array = new Size5[count]; break;
                    case 6: array = new Size6[count]; break;
                    case 7: array = new Size7[count]; break;
                    case 8: array = new Size8[count]; break;
                    case 9: array = new Size9[count]; break;
                    case 10: array = new Size10[count]; break;

                }

                return UnsafeUtility.PinGCArrayAndGetDataAddress(array, out gcHandle);
            }
        }

        [Test]
        public void PageParameters_AreConsistentWithAlignmentAndStorageRequirements_OverMultipleAllocations()
        {
            const int k_Allocations = 16;

            var sizes = Enumerable.Range(1, NSizedItemAllocator.k_MaxSize).ToArray();
            var aligns = new[] { 2, 4, 8, 16 };

            var pointers = stackalloc byte*[k_Allocations];


            for (int s = 0; s < sizes.Length; ++s)
            {
                for (int a = 0; a < aligns.Length; ++a)
                {
                    var size = sizes[s];

                    using (var allocator = new ManagedMemoryAllocator(new NSizedItemAllocator(size)))
                    {
                        ManagedMemoryAllocator.PageNode* head = allocator.GetHeadPage();

                        Assert.IsTrue(head != null);
                        ref var page = ref head->MemoryPage;

                        Assert.NotZero(page.StrongHandle);
                        Assert.NotZero(page.Capacity);
                        Assert.AreEqual(page.Capacity, page.FreeObjects);
                        Assert.GreaterOrEqual(page.ObjectSize, size);

                        for (int i = 0; i < k_Allocations; ++i)
                        {
                            var numAllocations = 0;

                            for (var current = head; current != null; current = current->Next)
                            {
                                numAllocations += current->MemoryPage.ObjectsInUse();
                            }

                            Assert.AreEqual(i, numAllocations);

                            pointers[i] = (byte*)allocator.Alloc();

                            bool foundAllocation = false;

                            for (var current = head; current != null; current = current->Next)
                            {
                                foundAllocation = current->MemoryPage.Contains(pointers[i]);
                                if (foundAllocation)
                                    break;
                            }

                            Assert.IsTrue(foundAllocation, "Could not find the allocation in any memory pages");

                            long intPtr = (long)pointers[i];
                        }

                        for (int i = 0; i < k_Allocations; ++i)
                        {
                            allocator.Free(pointers[i]);

                            var numAllocations = 0;

                            for (var current = head; current != null; current = current->Next)
                            {
                                numAllocations += current->MemoryPage.ObjectsInUse();
                            }

                            Assert.AreEqual(k_Allocations - i - 1, numAllocations);

                        }
                    }
                }

            }

        }

        [
            TestCase(Parameter.Size, 1), TestCase(Parameter.Size, 2), TestCase(Parameter.Size, 5), TestCase(Parameter.Size, 7), TestCase(Parameter.Size, 9),
            TestCase(Parameter.Pool, 1), TestCase(Parameter.Pool, 2), TestCase(Parameter.Pool, 4), TestCase(Parameter.Pool, 8), TestCase(Parameter.Pool, 16)
        ]
        public void CreatingAllocator_ForVaryingParameters_CanAllocateWriteAndFree(Parameter area, int param)
        {
            const int k_Allocations = 16;

            var size = area == Parameter.Size ? param : k_DefaultObjectSize;
            var pool = area == Parameter.Pool ? param : k_DefaultObjectPool;

            var pointers = stackalloc byte*[k_Allocations];

            using (var allocator = new ManagedMemoryAllocator(new NSizedItemAllocator(size), pool))
            {
                for (int i = 0; i < k_Allocations; ++i)
                {
                    pointers[i] = (byte*)allocator.Alloc();
                    for (int b = 0; b < size; ++b)
                        pointers[i][b] = (byte)b;
                }

                for (int i = 0; i < k_Allocations; ++i)
                {
                    allocator.Free(pointers[i]);
                }
            }
        }

        struct SimpleStruct
        {
            public int IValue;
            public float FValue;
        }

        [TestCase(1), TestCase(5), TestCase(33)]
        public void CanAliasManagedMemory_AsStruct_AndStoreRetrieveValues(int value)
        {
            using (var allocator = new ManagedMemoryAllocator(new DefaultManagedAllocator<SimpleStruct>()))
            {
                void* mem = allocator.Alloc();

                ref var alias = ref UnsafeUtility.AsRef<SimpleStruct>(mem);
                alias.FValue = value;
                alias.IValue = value;

                ref var secondAlias = ref UnsafeUtility.AsRef<SimpleStruct>(mem);

                Assert.AreEqual((int)secondAlias.FValue, value);
                Assert.AreEqual(secondAlias.IValue, value);

                allocator.Free(mem);
            }
        }

        [Test]
        public void MemoryLeaksReport_IsWritten_AfterDisposing()
        {
            using (var allocator = new ManagedMemoryAllocator(new DefaultManagedAllocator<SimpleStruct>()))
            {
                void* mem = allocator.Alloc();

                LogAssert.Expect(LogType.Warning, new Regex("found while disposing ManagedMemoryAllocator"));
            }
        }


        public class ManagedObject
        {
            public static long Instances => Interlocked.Read(ref s_Instances);

            static long s_Instances;

            public ManagedObject()
            {
                Interlocked.Increment(ref s_Instances);
            }

            ~ManagedObject()
            {
                Interlocked.Decrement(ref s_Instances);
            }
        }

        public struct ManagedStruct
        {
            public ManagedObject Object;
        }

        struct ManagedStructAllocator : IDisposable
        {
            ManagedMemoryAllocator m_Allocator;
            unsafe void* m_Allocation;

            public ManagedStructAllocator(int dummy)
            {
                m_Allocator = new ManagedMemoryAllocator(new DefaultManagedAllocator<ManagedStruct>());
                m_Allocation = m_Allocator.Alloc();
            }

            public ref ManagedStruct GetRef()
            {
                unsafe
                {
                    return ref UnsafeUtility.AsRef<ManagedStruct>(m_Allocation);
                }
            }

            public void Dispose()
            {
                m_Allocator.Free(m_Allocation);
                m_Allocator.Dispose();
            }
        }

        [Test]
        public void CanRetainManagedObject_InManagedMemory()
        {
            const int k_Loops = 3;

            Assert.Zero(ManagedObject.Instances);

            AssertManagedObjectsReleased(() =>
            {
                // (disposing the allocator release the allocation as well)
                using (var allocator = new ManagedStructAllocator(0))
                {
                    allocator.GetRef().Object = new ManagedObject();

                    for (var i = 0; i < k_Loops; ++i)
                    {
                        Assert.AreEqual(1, ManagedObject.Instances);
                        RequestGarbageCollection();
                    }
                }
            });
        }

        [Test]
        public void CanReleaseManagedObject_ThroughClearingReferenceField()
        {
            using (var allocator = new ManagedStructAllocator(0))
            {
                Assert.Zero(ManagedObject.Instances);

                AssertManagedObjectsReleased(() =>
                {
                    allocator.GetRef().Object = new ManagedObject();
                    Assert.AreEqual(1, ManagedObject.Instances);
                    allocator.GetRef().Object = null;
                });
            }
        }

        [Test]
        public void CanReleaseManagedObject_ThroughFreeingAllocation()
        {
            Assert.Zero(ManagedObject.Instances);

            AssertManagedObjectsReleased(() =>
            {
                // (disposing the allocator release the allocation as well)
                using (var allocator = new ManagedStructAllocator(0))
                {
                    allocator.GetRef().Object = new ManagedObject();
                    Assert.AreEqual(1, ManagedObject.Instances);
                }
            });
        }
    }
}
