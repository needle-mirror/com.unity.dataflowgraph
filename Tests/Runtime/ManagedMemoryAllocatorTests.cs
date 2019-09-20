using System;
using System.Collections;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.DataFlowGraph.Tests
{
    public unsafe class ManagedMemoryAllocatorTests
    {
        const int k_DefaultObjectSize = 4;
        const int k_DefaultObjectAlign = 4;
        const int k_DefaultObjectPool = 4;

        public enum Parameter
        {
            Size, Align, Pool
        }

        public static IEnumerator AssertManagedObjectsReleasedInTime()
        {
            const int k_TimeOut = 5;
            var time = Time.realtimeSinceStartup;

            while (Time.realtimeSinceStartup - time < k_TimeOut)
            {
                if (ManagedObject.Instances == 0)
                    break;

                GC.Collect();
                yield return null;
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

        [Test]
        public void CreationArguments_AreValidated_AndThrowExceptions()
        {
            // argument constraints are documented in the class documentation,
            // but every argument must be above 0.

            Assert.Throws<ArgumentException>(() => new ManagedMemoryAllocator(0, 1));
            Assert.Throws<ArgumentException>(() => new ManagedMemoryAllocator(1, 0));
            Assert.Throws<ArgumentException>(() => new ManagedMemoryAllocator(-1, 1));
            Assert.Throws<ArgumentException>(() => new ManagedMemoryAllocator(1, -1));
            Assert.Throws<ArgumentException>(() => new ManagedMemoryAllocator(1, 1, 0));
            Assert.Throws<ArgumentException>(() => new ManagedMemoryAllocator(1, 1, -1));
        }

        [TestCase(3), TestCase(5), TestCase(7), TestCase(9), TestCase(13), TestCase(31)]
        public void NonPowerOfTwoAlignment_ThrowsException(int align)
        {
            Assert.Throws<ArgumentException>(() => new ManagedMemoryAllocator(4, align, 4));
        }

        [Test]
        public void CreatingAndDisposingAllocator_Works()
        {
            using (var allocator = new ManagedMemoryAllocator(k_DefaultObjectSize, k_DefaultObjectAlign, k_DefaultObjectPool))
                Assert.IsTrue(allocator.IsCreated);
        }

        [Test]
        public void PageParameters_AreConsistentWithAlignmentAndStorageRequirements_OverMultipleAllocations()
        {
            const int k_Allocations = 16;

            var sizes = Enumerable.Range(1, 17).ToArray();
            var aligns = new[] { 2, 4, 8, 16 };

            var pointers = stackalloc byte*[k_Allocations];


            for (int s = 0; s < sizes.Length; ++s)
            {
                for (int a = 0; a < aligns.Length; ++a)
                {
                    var size = sizes[s];
                    var align = aligns[a];

                    using (var allocator = new ManagedMemoryAllocator(size, align))
                    {
                        ManagedMemoryAllocator.PageNode* head = allocator.GetHeadPage();

                        Assert.IsTrue(head != null);
                        ref var page = ref head->MemoryPage;

                        Assert.NotZero(page.m_StrongHandle);
                        Assert.NotZero(page.m_Capacity);
                        Assert.AreEqual(page.m_Capacity, page.m_FreeObjects);
                        Assert.Zero(page.m_ObjectSizeAligned % align, $"Aligned object size ({page.m_ObjectSizeAligned}) check failed for size {size} and align {align}");
                        Assert.GreaterOrEqual(page.m_ObjectSizeAligned, size);

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

                            Assert.Zero(intPtr % align, "Actual pointer is not aligned");
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
            TestCase(Parameter.Size, 1), TestCase(Parameter.Size, 2), TestCase(Parameter.Size, 5), TestCase(Parameter.Size, 7), TestCase(Parameter.Size, 14),
            TestCase(Parameter.Align, 1), TestCase(Parameter.Align, 2), TestCase(Parameter.Align, 4), TestCase(Parameter.Align, 8), TestCase(Parameter.Align, 16),
            TestCase(Parameter.Pool, 1), TestCase(Parameter.Pool, 2), TestCase(Parameter.Pool, 4), TestCase(Parameter.Pool, 8), TestCase(Parameter.Pool, 16)
        ]
        public void CreatingAllocator_ForVaryingParameters_CanAllocateWriteAndFree(Parameter area, int param)
        {
            const int k_Allocations = 16;

            var size = area == Parameter.Size ? param : k_DefaultObjectSize;
            var align = area == Parameter.Align ? param : k_DefaultObjectAlign;
            var pool = area == Parameter.Pool ? param : k_DefaultObjectPool;

            var pointers = stackalloc byte*[k_Allocations];

            using (var allocator = new ManagedMemoryAllocator(size, align, pool))
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
            using (var allocator = new ManagedMemoryAllocator(sizeof(SimpleStruct), UnsafeUtility.AlignOf<SimpleStruct>()))
            {
                void* mem = allocator.Alloc();

                ref var alias = ref Unsafe.AsRef<SimpleStruct>(mem);
                alias.FValue = value;
                alias.IValue = value;

                ref var secondAlias = ref Unsafe.AsRef<SimpleStruct>(mem);

                Assert.AreEqual((int)secondAlias.FValue, value);
                Assert.AreEqual(secondAlias.IValue, value);

                allocator.Free(mem);
            }
        }

        [Test]
        public void MemoryLeaksReport_IsWritten_AfterDisposing()
        {
            using (var allocator = new ManagedMemoryAllocator(sizeof(SimpleStruct), UnsafeUtility.AlignOf<SimpleStruct>()))
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
                m_Allocator = new ManagedMemoryAllocator(UnsafeUtility.SizeOf<ManagedStruct>(), UnsafeUtility.AlignOf<ManagedStruct>());
                m_Allocation = m_Allocator.Alloc();
            }

            public ref ManagedStruct GetRef()
            {
                unsafe
                {
                    return ref Unsafe.AsRef<ManagedStruct>(m_Allocation);
                }
            }

            public void Dispose()
            {
                m_Allocator.Free(m_Allocation);
                m_Allocator.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator CanRetainManagedObject_InManagedMemory()
        {
            const int k_TimeOut = 3;

            using (var allocator = new ManagedStructAllocator(0))
            {
                Assert.Zero(ManagedObject.Instances);

                allocator.GetRef().Object = new ManagedObject();

                var time = Time.realtimeSinceStartup;

                while (Time.realtimeSinceStartup - time < k_TimeOut)
                {
                    Assert.AreEqual(1, ManagedObject.Instances);
                    GC.Collect();
                    yield return null;
                }
            }

            // (disposing the allocator release the allocation as well)
            yield return AssertManagedObjectsReleasedInTime();
        }

        [UnityTest]
        public IEnumerator CanReleaseManagedObject_ThroughClearingReferenceField()
        {
            using (var allocator = new ManagedStructAllocator(0))
            {
                Assert.Zero(ManagedObject.Instances);

                allocator.GetRef().Object = new ManagedObject();
                Assert.AreEqual(1, ManagedObject.Instances);
                allocator.GetRef().Object = null;

                yield return AssertManagedObjectsReleasedInTime();
            }
        }

        [UnityTest]
        public IEnumerator CanReleaseManagedObject_ThroughFreeingAllocation()
        {
            using (var allocator = new ManagedStructAllocator(0))
            {
                Assert.Zero(ManagedObject.Instances);

                allocator.GetRef().Object = new ManagedObject();
                Assert.AreEqual(1, ManagedObject.Instances);
            }

            // (disposing the allocator release the allocation as well)
            yield return AssertManagedObjectsReleasedInTime();
        }
    }
}
