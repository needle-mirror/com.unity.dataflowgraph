using System;
using System.Threading;
using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;

namespace Unity.DataFlowGraph.Tests
{
    public unsafe class AtomicSafetyManagerTests
    {
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
                        UtilityAssert.ThrowsEither<InvalidOperationException, ObjectDisposedException>(() => something = nativeArray[0]);
                        UtilityAssert.ThrowsEither<InvalidOperationException, ObjectDisposedException>(() => nativeArray[0] = something);
                    }
                }
                finally
                {
                    UnsafeUtility.Free(memory, Allocator.Temp);
                }
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
            if (!JobsUtility.JobDebuggerEnabled)
                Assert.Ignore("JobsDebugger is disabled");

            var handleContainer = new NativeArray<AtomicSafetyHandleContainer>(1, Allocator.TempJob);

            try
            {
                using (NativeArray<int> array = new NativeArray<int>(1, Allocator.TempJob))
                {
                    JobThatUsesNativeArray producer;
                    producer.array = array;

                    var dependency = producer.Schedule();
                    var handle = NativeArrayUnsafeUtility.GetAtomicSafetyHandle(array);

                    AtomicSafetyHandleContainer container = default;
                    container.SafetyHandle = handle;
                    handleContainer[0] = container;

                    var protectedDependency = AtomicSafetyManager.MarkHandlesAsUsed(dependency, (AtomicSafetyHandleContainer*)handleContainer.GetUnsafePtr(), handleContainer.Length);

                    JobThatUsesNativeArray consumer;
                    consumer.array = array;

                    try
                    {
                        JobHandle missingDependencyFromDataFlowGraph = default;

                        Assert.Throws<InvalidOperationException>(() => consumer.Schedule(missingDependencyFromDataFlowGraph));

                        Assert.DoesNotThrow(() => consumer.Schedule(protectedDependency).Complete());
                    }
                    finally
                    {
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
        public void MarkingZeroHandles_StillReturnsDependentJob()
        {
            if (!JobsUtility.JobDebuggerEnabled)
                Assert.Ignore("JobsDebugger is disabled");

            using (NativeArray<int> array = new NativeArray<int>(1, Allocator.TempJob))
            {
                JobThatUsesNativeArray producer;
                producer.array = array;

                var dependency = producer.Schedule();

                var protectedDependency = AtomicSafetyManager.MarkHandlesAsUsed(dependency, (AtomicSafetyHandleContainer*)null, 0);

                JobThatUsesNativeArray consumer;
                consumer.array = array;

                try
                {
                    JobHandle missingDependencyFromDataFlowGraph = default;

                    Assert.Throws<InvalidOperationException>(() => consumer.Schedule(missingDependencyFromDataFlowGraph));

                    Assert.DoesNotThrow(() => consumer.Schedule(protectedDependency).Complete());
                }
                finally
                {
                    protectedDependency.Complete();
                }
            }


        }

        [Test]
        public void TestThatMarkHandles_DetectsInvalidInputDependencies()
        {
            if (!JobsUtility.JobDebuggerEnabled)
                Assert.Ignore("JobsDebugger is disabled");

            var handleContainer = new NativeArray<AtomicSafetyHandleContainer>(1, Allocator.TempJob);

            try
            {
                using (NativeArray<int> array = new NativeArray<int>(1, Allocator.TempJob))
                {
                    JobThatUsesNativeArray producer;
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

                        Assert.DoesNotThrow(
                            () => AtomicSafetyManager.MarkHandlesAsUsed(dependency, (AtomicSafetyHandleContainer*)handleContainer.GetUnsafePtr(), handleContainer.Length).Complete()
                        );
                    }
                    finally
                    {
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

