using System;
using System.Diagnostics;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;

namespace Unity.DataFlowGraph
{

    struct AtomicSafetyManager : IDisposable
    {

        public unsafe struct ECSTypeAndSafety : ISafetyHandleContainable
        {
            public ComponentType Type;
#if ENABLE_UNITY_COLLECTIONS_CHECKS

            public void CopySafetyHandle(ComponentSafetyHandles* handles)
            {
                SafetyHandle = handles->GetSafetyHandle(Type.TypeIndex, Type.AccessModeType == ComponentType.AccessMode.ReadOnly);
            }

            public AtomicSafetyHandle SafetyHandle { get; set; }
#endif
        }

        public unsafe struct DeferredBlittableArray
        {
            public static DeferredBlittableArray Create<T>(NativeList<T> list)
                where T : struct
            {
                var deferred = list.AsDeferredJobArray();
                DeferredBlittableArray ret;
                ret.m_Ptr = NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(deferred);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                ret.m_Safety = NativeArrayUnsafeUtility.GetAtomicSafetyHandle(deferred);
#endif

                return ret;
            }

            public NativeArray<T> Reconstruct<T>()
                where T : struct
            {
                // Same reconstruction NativeList does.
                var ret = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(m_Ptr, 0, Allocator.Invalid);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref ret, m_Safety);
#endif

                return ret;
            }

            public bool Valid => m_Ptr != null;

            private void* m_Ptr;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle m_Safety;
#endif
        }

        public interface ISafetyHandleContainable
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle SafetyHandle { get; set; }
#endif
        }

        [NativeContainer]
        public unsafe struct BufferProtectionScope : IDisposable
        {
            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            public void CheckWriteAccess()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (!m_IsCreated)
                    throw new ObjectDisposedException("BufferProtectionScope not initialized");

                AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            public void CheckReadAccess()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (!m_IsCreated)
                    throw new ObjectDisposedException("BufferProtectionScope not initialized");

                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
            }

            public void Dispose()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (!m_IsCreated)
                    throw new ObjectDisposedException("BufferProtectionScope not initialized");

                // We don't need to clean up m_IsCreated because AtomicSafetyHandle fails on
                // already-disposed / stale access with a nice error message.
                AtomicSafetyHandle.Release(m_Safety);
#endif
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            public void Bump()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                Dispose();
                this = Create();
#endif
            }

            public static BufferProtectionScope Create()
            {
                BufferProtectionScope scope;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                scope.m_Safety = AtomicSafetyHandle.Create();
                scope.m_IsCreated = true;
#endif
                return scope;
            }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle m_Safety;
            // AtomicSafetyHandle.CheckExistsAndThrow() will also crash if the handle hasn't originally been created
            // Need additional protection. Normal native containers have .IsCreated which they additionally protect.
            bool m_IsCreated;
#endif
        }

        [BurstCompile]
        struct ProtectOutputBuffersFromDataFlowGraph : IJob
        {
            public BufferProtectionScope WritableDataFlowGraphScope;

            public void Execute() => WritableDataFlowGraphScope.CheckWriteAccess();
        }

        [BurstCompile]
        struct TrackOneHandle : IJob
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            public const int k_NumBuffers = 1;

#pragma warning disable 649  // never assigned

            public NativeArray<int> Buffer0;

#pragma warning restore 649

            public unsafe void InitializeFrom<TSafetyHandleContainer>(TSafetyHandleContainer* handles, int where)
                where TSafetyHandleContainer : unmanaged, ISafetyHandleContainable
            {
                // Just to avoid Unity checks against null-pointer native arrays.
                // We're never going to dereference it.
                // Also add offset to avoid alias analysis issues.
                var invalidMemory = (void*)(0xDEAD0000 + where * 0x10);
                Buffer0 = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<int>(invalidMemory, 0, Allocator.Invalid);
                NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref Buffer0, handles[where].SafetyHandle);
            }
#endif

            public void Execute() { }
        }

        [BurstCompile]
        struct TrackFiveHandles : IJob
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            public const int k_NumBuffers = 5;

#pragma warning disable 649  // never assigned

            public TrackOneHandle Buffer0;
            public TrackOneHandle Buffer1;
            public TrackOneHandle Buffer2;
            public TrackOneHandle Buffer3;
            public TrackOneHandle Buffer4;

#pragma warning restore 649

            public unsafe void InitializeFrom<TSafetyHandleContainer>(TSafetyHandleContainer* handles, int where)
                where TSafetyHandleContainer : unmanaged, ISafetyHandleContainable
            {
                Buffer0.InitializeFrom(handles, where + 0);
                Buffer1.InitializeFrom(handles, where + 1);
                Buffer2.InitializeFrom(handles, where + 2);
                Buffer3.InitializeFrom(handles, where + 3);
                Buffer4.InitializeFrom(handles, where + 4);
            }
#endif

            public void Execute() { }
        }

        [BurstCompile]
        struct TrackTwentyFiveHandles : IJob
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            public const int k_NumBuffers = 25;

#pragma warning disable 649  // never assigned

            public TrackFiveHandles Buffer0;
            public TrackFiveHandles Buffer1;
            public TrackFiveHandles Buffer2;
            public TrackFiveHandles Buffer3;
            public TrackFiveHandles Buffer4;

#pragma warning restore 649

            public unsafe void InitializeFrom<TSafetyHandleContainer>(TSafetyHandleContainer* handles, int where)
                where TSafetyHandleContainer : unmanaged, ISafetyHandleContainable
            {
                Buffer0.InitializeFrom(handles, where + 0 * 5);
                Buffer1.InitializeFrom(handles, where + 1 * 5);
                Buffer2.InitializeFrom(handles, where + 2 * 5);
                Buffer3.InitializeFrom(handles, where + 3 * 5);
                Buffer4.InitializeFrom(handles, where + 4 * 5);
            }
#endif

            public void Execute() { }
        }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        AtomicSafetyHandle m_TemporaryRead, m_TemporaryReadWrite;
#endif
        int m_IsCreated;

        public static AtomicSafetyManager Create()
        {
            var ret = new AtomicSafetyManager();
            ret.CreateTemporaryHandles();
            ret.m_IsCreated = 1;
            return ret;
        }

        public void Dispose()
        {
            if (m_IsCreated == 0)
                throw new InvalidOperationException("Atomic Safety Manager already disposed");

            ReleaseTemporaryHandles();

            m_IsCreated = 0;
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        public void BumpTemporaryHandleVersions()
        {
            // TODO: There should be a better way to invalidate older versions...
            ReleaseTemporaryHandles();
            CreateTemporaryHandles();
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        public void MarkNativeArrayAsReadOnly<T>(ref NativeArray<T> array)
            where T : struct
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref array, m_TemporaryRead);
#endif
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        public void MarkNativeArrayAsReadWrite<T>(ref NativeArray<T> array)
            where T : struct
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref array, m_TemporaryReadWrite);
#endif
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        public void MarkNativeSliceAsReadOnly<T>(ref NativeSlice<T> slice)
            where T : struct
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeSliceUnsafeUtility.SetAtomicSafetyHandle(ref slice, m_TemporaryRead);
#endif
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        public void MarkNativeSliceAsReadWrite<T>(ref NativeSlice<T> array)
            where T : struct
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeSliceUnsafeUtility.SetAtomicSafetyHandle(ref array, m_TemporaryReadWrite);
#endif
        }

        public static JobHandle MarkScopeAsWrittenTo(JobHandle dependency, BufferProtectionScope scope)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS

            ProtectOutputBuffersFromDataFlowGraph protection;
            protection.WritableDataFlowGraphScope = scope;

            dependency = protection.Schedule(dependency);
#endif
            return dependency;
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        public static void CopySafetyHandle<T, TTarget>(in NativeArray<T> array, ref TTarget target)
            where T : struct
            where TTarget : ISafetyHandleContainable
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            target.SafetyHandle = NativeArrayUnsafeUtility.GetAtomicSafetyHandle(array);
#endif
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        public static void CheckReadAccess<TTarget>(in TTarget target)
            where TTarget : ISafetyHandleContainable
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(target.SafetyHandle);
#endif
        }

        /// <summary>
        /// Returns a job handle that until resolved protects the given handles
        /// by marking them in use.
        ///
        /// Should be used to mark any "buffers" exposed out of the <see cref="NodeSetAPI"/> for the duration
        /// of a "render" to detect whenever someone uses buffers concurrently erroneously,
        /// e.g. scheduling a job using a future buffer against a <see cref="NodeSetAPI"/>, but not properly inserting
        /// back dependencies such that rendering of that <see cref="NodeSetAPI"/> and that job could overlap.
        /// In that case, this system will throw a legible exception.
        /// </summary>
        public static unsafe JobHandle MarkHandlesAsUsed<TSafetyHandleContainer>(JobHandle dependency, TSafetyHandleContainer* handles, int handleCount)
            where TSafetyHandleContainer : unmanaged, ISafetyHandleContainable
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS

            if (handleCount == 0)
                return dependency;

            // There's no API on AtomicSafetyHandle for marking them "in use", which is what happens
            // when a job is scheduled with "buffers" on them.
            // Unfortunately, this is the primary safety feature on AtomicSafetyHandle (beyond R/W protection):
            // The race condition system that throws exceptions when you didn't schedule your jobs correctly.
            // So here we emulate it with a combination of faux jobs to handle a dynamic amount of
            // handles to be marked.

            int handledJobs = 0;

            var num25Jobs = handleCount / TrackTwentyFiveHandles.k_NumBuffers;
            handledJobs += num25Jobs * TrackTwentyFiveHandles.k_NumBuffers;

            var num5Jobs = (handleCount - handledJobs) / TrackFiveHandles.k_NumBuffers;
            handledJobs += num5Jobs * TrackFiveHandles.k_NumBuffers;

            var num1Jobs = (handleCount - handledJobs) / TrackOneHandle.k_NumBuffers;
            handledJobs += num1Jobs * TrackOneHandle.k_NumBuffers;


            var tempJobs = new NativeList<JobHandle>(handledJobs, Allocator.Temp);
            int pos = 0;

            for (int i = 0; i < num25Jobs; ++i)
            {
                TrackTwentyFiveHandles tracker = new TrackTwentyFiveHandles();
                tracker.InitializeFrom(handles, pos);

                tempJobs.Add(tracker.Schedule(dependency));

                pos += TrackTwentyFiveHandles.k_NumBuffers;
            }

            for (int i = 0; i < num5Jobs; ++i)
            {
                TrackFiveHandles tracker = new TrackFiveHandles();
                tracker.InitializeFrom(handles, pos);

                tempJobs.Add(tracker.Schedule(dependency));

                pos += TrackFiveHandles.k_NumBuffers;
            }

            for (int i = 0; i < num1Jobs; ++i)
            {
                TrackOneHandle tracker = new TrackOneHandle();
                tracker.InitializeFrom(handles, pos);

                tempJobs.Add(tracker.Schedule(dependency));

                pos += TrackOneHandle.k_NumBuffers;
            }

            dependency = JobHandleUnsafeUtility.CombineDependencies((JobHandle*)tempJobs.GetUnsafePtr(), tempJobs.Length);

#endif
            return dependency;
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CreateTemporaryHandles()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            // Handles are always writable. No need to do anything more.
            m_TemporaryReadWrite = AtomicSafetyHandle.Create();
            // Mark as read-only. Should be a better way to do this...
            m_TemporaryRead = AtomicSafetyHandle.Create();
            AtomicSafetyHandle.UseSecondaryVersion(ref m_TemporaryRead);
            AtomicSafetyHandle.SetAllowSecondaryVersionWriting(m_TemporaryRead, false);
#endif
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void ReleaseTemporaryHandles()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckDeallocateAndThrow(m_TemporaryRead);
            AtomicSafetyHandle.CheckDeallocateAndThrow(m_TemporaryReadWrite);

            AtomicSafetyHandle.Release(m_TemporaryRead);
            AtomicSafetyHandle.Release(m_TemporaryReadWrite);
#endif
        }
    }
}
