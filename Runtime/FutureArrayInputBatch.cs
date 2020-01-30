using System;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace Unity.DataFlowGraph
{

    unsafe struct TransientInputBuffer
    {
        public InputPair Destination;
        public void* Memory;
        public int Size;
    }

    /// <summary>
    /// Note that port forwarding is NOT resolved, it is expected to be done before using any API here.
    /// <seealso cref="MemoryInputSystem{TNodeMemoryInput, TBufferToMove}.MainThreadChecker.OnUpdate"/>
    /// </summary>
    struct FutureArrayInputBatch : IDisposable
    {
        NativeList<TransientInputBuffer> m_RecordedInputBuffers;

        public FutureArrayInputBatch(int capacity, Allocator allocator)
        {
            m_RecordedInputBuffers = new NativeList<TransientInputBuffer>(capacity, allocator);
        }

        public unsafe void SetTransientBuffer<TType>(in InputPair destination, NativeArray<TType> buffer)
            where TType : struct
        {
            var transient = new TransientInputBuffer();
            transient.Destination = destination;
            transient.Memory = buffer.GetUnsafeReadOnlyPtr();
            transient.Size = buffer.Length;

            m_RecordedInputBuffers.Add(transient);
        }

        public void Clear()
        {
            m_RecordedInputBuffers.Clear();
        }


        public void Dispose()
        {
            m_RecordedInputBuffers.Dispose();
        }

        internal NativeList<TransientInputBuffer> GetTransientBatch() => m_RecordedInputBuffers;
    }

    struct BatchHandle
    {
        internal VersionedHandle VHandle;
    }

    unsafe struct InputBatch : IVersionedNode
    {
        public struct InstalledPorts : IDisposable
        {
            public ref DataOutput<InvalidDefinitionSlot, BufferDescription> GetPort(int index)
            {
                return ref Unsafe.AsRef<DataOutput<InvalidDefinitionSlot, BufferDescription>>(GetPortMemory(index));
            }

            public void* GetPortMemory(int index)
            {
#if DFG_ASSERTIONS
                if ((uint)index >= m_InstallCount)
                    throw new IndexOutOfRangeException();

                if (m_InstalledMemory == null)
                    throw new AssertionException("Batch not installed yet");
#endif

                return (byte*)m_InstalledMemory + UnsafeUtility.SizeOf<DataOutput<InvalidDefinitionSlot, BufferDescription>>() * index;
            }

            public void AllocatePorts(NativeArray<TransientInputBuffer> transients)
            {
#if DFG_ASSERTIONS
                if (m_InstalledMemory != null)
                    throw new AssertionException("Batch already installed");
#endif

                var type = SimpleType.Create<DataOutput<InvalidDefinitionSlot, BufferDescription>>(transients.Length);

                m_InstalledMemory = Utility.CAlloc(type, k_Alloc);
                m_InstallCount = transients.Length;
            }

            public void Dispose()
            {
                if (m_InstalledMemory != null)
                    UnsafeUtility.Free(m_InstalledMemory, k_Alloc);

                m_InstalledMemory = null;
            }

            [NativeDisableContainerSafetyRestriction]
            void* m_InstalledMemory;
            int m_InstallCount;
        }

        const Allocator k_Alloc = Allocator.Persistent;

        public ref InstalledPorts GetInstallMemory()
        {
            if (m_InstallMemory == null)
                throw new ObjectDisposedException("Assertion: Input batch not created");

            return ref *m_InstallMemory;
        }

        public NativeArray<TransientInputBuffer> GetDeferredTransientBuffer() => m_DeferredList.Reconstruct<TransientInputBuffer>();

        public VersionedHandle VHandle { get; set; }
        public bool Valid => m_DeferredList.Valid;

        public JobHandle InputDependency;
        public JobHandle OutputDependency;
        public int RenderVersion;

        InstalledPorts* m_InstallMemory;
        AtomicSafetyManager.DeferredBlittableArray m_DeferredList;

        public void InitializeFrom(NativeList<TransientInputBuffer> list, int renderVersion, JobHandle dependencies)
        {
            m_DeferredList = AtomicSafetyManager.DeferredBlittableArray.Create(list);
            InputDependency = dependencies;
            RenderVersion = renderVersion;
            m_InstallMemory = (InstalledPorts*)Utility.CAlloc(SimpleType.Create<InstalledPorts>(), k_Alloc);
        }


        public void Destroy()
        {
            var handle = VHandle;
            if (m_InstallMemory != null)
            {
                m_InstallMemory->Dispose();
                UnsafeUtility.Free(m_InstallMemory, k_Alloc);
            }

            this = new InputBatch();
            VHandle = handle;
        }

    }

    public partial class NodeSet
    {
        VersionedList<InputBatch> m_Batches;

        /// <summary>
        /// The returned handle can be used to acquire a JobHandle after the next update, 
        /// signifying when the <see cref="NodeSet"/> is done reading from the input batch.
        /// 
        /// The handle is automatically recycled.
        /// </summary>
        /// <remarks>
        /// Since the output dependencies are not known up front,
        /// the batch takes an dependency on the entire graph.
        /// </remarks>
        internal BatchHandle SubmitDeferredInputBatch(JobHandle batchDependency, FutureArrayInputBatch userBatch)
        {
            var recordedBatch = userBatch.GetTransientBatch();

            ref var batch = ref m_Batches.Allocate();
            batch.InitializeFrom(recordedBatch, DataGraph.RenderVersion, batchDependency);

            return new BatchHandle { VHandle = batch.VHandle };
        }

        internal JobHandle GetBatchDependencies(BatchHandle handle)
        {
            if (!m_Batches.Exists(handle.VHandle))
                return new JobHandle();

            ref var batch = ref m_Batches[handle.VHandle];

            if (batch.RenderVersion == DataGraph.RenderVersion)
            {
                throw new InvalidOperationException("Querying batch for dependency before next render has been scheduled");
            }

            return batch.OutputDependency;
        }

        void PostRenderBatchProcess(bool forceCleanup = false)
        {
            for (int i = 0; i < m_Batches.UncheckedCount; ++i)
            {
                ref var batch = ref m_Batches[i];

                // Exists and older than one frame? Then dispose.
                // Anything older than one frame is always fenced anyway.
                if (batch.Valid && (forceCleanup || batch.RenderVersion < DataGraph.RenderVersion - 1))
                {
                    batch.OutputDependency.Complete();
                    batch.Destroy();
                    m_Batches.Release(batch.VHandle);
                }
            }
        }
    }
}
