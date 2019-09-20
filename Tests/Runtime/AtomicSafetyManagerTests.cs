using System;
using System.Threading;
using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace Unity.DataFlowGraph.Tests
{
    public unsafe class AtomicSafetyManagerTests
    {
        internal struct WaitingStaticFlagStructure
        {
            public static bool Wait { get => Interlocked.Read(ref s_StartFlag) == 0; set => Interlocked.Exchange(ref s_StartFlag, !value ? 1 : 0); }
            public static bool Done { get => Interlocked.Read(ref s_DoneFlag) > 0; set => Interlocked.Exchange(ref s_DoneFlag, value ? 1 : 0); }

            static long s_StartFlag;
            static long s_DoneFlag;

            static public void Execute()
            {
                // Avoid potential deadlocks.
                for (int i = 0; i < 1000 && Wait; ++i)
                    Thread.Sleep(10);

                Done = true;
            }

            static public void Reset()
            {
                Wait = true;
                Done = false;
            }
        }

        [Test]
        public void CanAllocateAndDispose_AtomicSafetyManager_OnTheStack()
        {
            var manager = AtomicSafetyManager.Create();
            manager.Dispose();
        }

        [Test]
        public void CanAllocateAndDispose_AtomicSafetyManager_OnTheUnmanagedHeap()
        {
            var manager = (AtomicSafetyManager*)UnsafeUtility.Malloc(sizeof(AtomicSafetyManager), UnsafeUtility.AlignOf<AtomicSafetyManager>(), Allocator.Temp);
            *manager = AtomicSafetyManager.Create();
            manager->Dispose();

            UnsafeUtility.Free(manager, Allocator.Temp);
        }

        [Test]
        public void CannotDispose_NonCreated_AtomicSafetyManager()
        {
            var manager = new AtomicSafetyManager();
            Assert.Throws<InvalidOperationException>(() => manager.Dispose());
        }

        [Test]
        public void CannotDoubleDispose_CreatedAtomicSafetyManager()
        {
            var manager = AtomicSafetyManager.Create();
            manager.Dispose();
            Assert.Throws<InvalidOperationException>(() => manager.Dispose());
        }

        [Test]
        public void CanAlways_MarkConvertedNativeArray_ToBeReadable_OutsideOf_Defines()
        {
            using (var manager = AtomicSafetyManager.Create())
            {
                var memory = UnsafeUtility.Malloc(sizeof(int), UnsafeUtility.AlignOf<int>(), Allocator.Temp);

                try
                {
                    var nativeArray = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<int>(memory, 4, Allocator.Invalid);
                    int something = 0;

                    manager.MarkNativeArrayAsReadOnly(ref nativeArray);
                    something = nativeArray[0];
                }
                finally
                {
                    UnsafeUtility.Free(memory, Allocator.Temp);
                }
            }
        }

#if ENABLE_UNITY_COLLECTIONS_CHECKS

        [Test]
        public void CanMarkExisting_NativeArray_AsReadOnly_ThenAsWritable()
        {
            using (var manager = AtomicSafetyManager.Create())
            {
                var nativeArray = new NativeArray<int>(1, Allocator.Temp);
                var oldHandle = NativeArrayUnsafeUtility.GetAtomicSafetyHandle(nativeArray);

                try
                {
                    // read, write possible by default:
                    nativeArray[0] = nativeArray[0];
                    // Remove W rights
                    manager.MarkNativeArrayAsReadOnly(ref nativeArray);
                    // R rights still exists
                    int something = nativeArray[0];
                    Assert.Throws<InvalidOperationException>(() => nativeArray[0] = something);

                    // restore W rights
                    manager.MarkNativeArrayAsReadWrite(ref nativeArray);
                    nativeArray[0] = something;
                }
                finally
                {
                    NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref nativeArray, oldHandle);
                    nativeArray.Dispose();
                }
            }
        }

        [Test]
        public void CanMarkConvertedNativeArray_AsReadOnly_ThenAsWritable()
        {
            using (var manager = AtomicSafetyManager.Create())
            {
                var memory = UnsafeUtility.Malloc(sizeof(int), UnsafeUtility.AlignOf<int>(), Allocator.Temp);

                try
                {
                    var nativeArray = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<int>(memory, 4, Allocator.Invalid);

                    // No atomic safety handle assigned yet
                    Assert.Throws<NullReferenceException>(() => nativeArray[0] = nativeArray[0]);

                    // Remove W rights
                    manager.MarkNativeArrayAsReadOnly(ref nativeArray);
                    // R rights still exists
                    int something = nativeArray[0];
                    Assert.Throws<InvalidOperationException>(() => nativeArray[0] = something);

                    // restore R+W rights
                    manager.MarkNativeArrayAsReadWrite(ref nativeArray);
                    nativeArray[0] = nativeArray[0];
                }
                finally
                {
                    UnsafeUtility.Free(memory, Allocator.Temp);
                }
            }
        }

        [Test]
        public void CanInvalidate_PreviouslyValidNativeArray_ThroughBumpingSafetyManager()
        {
            using (var manager = AtomicSafetyManager.Create())
            {
                var memory = UnsafeUtility.Malloc(sizeof(int), UnsafeUtility.AlignOf<int>(), Allocator.Temp);

                try
                {
                    var nativeArray = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<int>(memory, 4, Allocator.Invalid);
                    int something = 0;

                    for (int i = 0; i < 20; ++i)
                    {
                        manager.MarkNativeArrayAsReadWrite(ref nativeArray);
                        nativeArray[0] = nativeArray[0];

                        manager.BumpTemporaryHandleVersions();

                        // R, W gone:
                        Assert.Throws<InvalidOperationException>(() => something = nativeArray[0]);
                        Assert.Throws<InvalidOperationException>(() => nativeArray[0] = something);
                    }
                }
                finally
                {
                    UnsafeUtility.Free(memory, Allocator.Temp);
                }
            }
        }

        struct JobThatProducesNativeArray : IJob
        {
            public NativeArray<int> array;

            public static bool Wait { get => WaitingStaticFlagStructure.Wait; set => WaitingStaticFlagStructure.Wait = value; }
            public static bool Done { get => WaitingStaticFlagStructure.Done; set => WaitingStaticFlagStructure.Done = value; }
            public static void Reset() => WaitingStaticFlagStructure.Reset();

            public void Execute()
            {
                WaitingStaticFlagStructure.Execute();
            }
        }

        struct JobThatUsesNativeArray : IJob
        {
            public NativeArray<int> array;

            public void Execute()
            {
            }
        }

        struct AtomicSafetyHandleContainer : AtomicSafetyManager.ISafetyHandleContainable
        {
            public AtomicSafetyHandle SafetyHandle { get; set; }
        }


        [Test]
        public void TestThatMarkHandles_DetectsMissingOutputDependencies()
        {
            JobThatProducesNativeArray.Reset();

            var handleContainer = new NativeArray<AtomicSafetyHandleContainer>(1, Allocator.TempJob);

            try
            {
                using (NativeArray<int> array = new NativeArray<int>(1, Allocator.TempJob))
                {

                    JobThatProducesNativeArray producer;
                    producer.array = array;

                    var dependency = producer.Schedule();
                    var handle = NativeArrayUnsafeUtility.GetAtomicSafetyHandle(array);

                    AtomicSafetyHandleContainer container = default;
                    container.SafetyHandle = handle;
                    handleContainer[0] = container;

                    var protectedDependency = AtomicSafetyManager.MarkHandlesAsUsed(dependency, (AtomicSafetyHandleContainer*)handleContainer.GetUnsafePtr(), handleContainer.Length);

                    JobThatUsesNativeArray consumer;
                    consumer.array = array;

                    Assert.True(JobThatProducesNativeArray.Wait);

                    try
                    {
                        JobHandle missingDependencyFromDataFlowGraph = default;

                        Assume.That(JobThatProducesNativeArray.Done, Is.False);
                        Assert.Throws<InvalidOperationException>(() => consumer.Schedule(missingDependencyFromDataFlowGraph));
                        JobThatProducesNativeArray.Wait = false;

                        Assert.DoesNotThrow(() => consumer.Schedule(protectedDependency).Complete());
                        Assert.True(JobThatProducesNativeArray.Done);
                    }
                    finally
                    {
                        JobThatProducesNativeArray.Wait = false;
                        protectedDependency.Complete();
                    }
                }
            }
            finally
            {
                handleContainer.Dispose();
            }

        }


        [Test]
        public void TestThatMarkHandles_DetectsInvalidInputDependencies()
        {
            JobThatProducesNativeArray.Reset();

            var handleContainer = new NativeArray<AtomicSafetyHandleContainer>(1, Allocator.TempJob);

            try
            {
                using (NativeArray<int> array = new NativeArray<int>(1, Allocator.TempJob))
                {

                    JobThatProducesNativeArray producer;
                    producer.array = array;

                    var dependency = producer.Schedule();
                    try
                    {
                        var handle = NativeArrayUnsafeUtility.GetAtomicSafetyHandle(array);

                        AtomicSafetyHandleContainer container = default;
                        container.SafetyHandle = handle;
                        handleContainer[0] = container;

                        // This represents a faulty input dependency from the user.
                        JobHandle invalidDependency = default;

                        Assert.Throws<InvalidOperationException>(
                            () => AtomicSafetyManager.MarkHandlesAsUsed(invalidDependency, (AtomicSafetyHandleContainer*)handleContainer.GetUnsafePtr(), handleContainer.Length)
                        );

                        Assume.That(JobThatProducesNativeArray.Done, Is.False);
                        JobThatProducesNativeArray.Wait = false;

                        Assert.DoesNotThrow(
                            () => AtomicSafetyManager.MarkHandlesAsUsed(dependency, (AtomicSafetyHandleContainer*)handleContainer.GetUnsafePtr(), handleContainer.Length).Complete()
                        );
                    }
                    finally
                    {
                        JobThatProducesNativeArray.Wait = false;
                        dependency.Complete();
                    }

                }
            }
            finally
            {
                handleContainer.Dispose();
            }

        }
#endif
    }

}

