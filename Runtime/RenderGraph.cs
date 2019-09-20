using System;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Profiling;

namespace Unity.DataFlowGraph
{
    using BufferResizeCommands = BlitList<RenderGraph.BufferResizeStruct>;
    using InputPortUpdateCommands = BlitList<RenderGraph.InputPortUpdateStruct>;
    using UntypedPortArray = PortArray<DataInput<InvalidFunctionalitySlot, byte>>;

    public enum RenderExecutionModel
    {
        /// <summary>
        /// Every node of execution will be launched in a separate job
        /// </summary>
        MaximallyParallel = 0,
        /// <summary>
        /// All nodes are executed in a single job
        /// </summary>
        SingleThreaded,
        /// <summary>
        /// All nodes are executed on the calling thread
        /// </summary>
        Synchronous,
        /// <summary>
        /// Connected components in the graph will be executed in one job.
        /// </summary>
        Islands
    }

    partial class NodeSet
    {
        RenderExecutionModel m_Model;

        /// <summary>
        /// The render execution scheduling mode use for launching the processing of
        /// <see cref="IGraphKernel{TKernelData,TKernelPortDefinition}"/>s in the node set.
        /// <seealso cref="NodeSet.Update"/>
        /// </summary>
        /// <remarks>Changing this can drastically alter the execution runtime of your graph. The best choice largely depends on the topological layout of the graph in question.</remarks>
        public RenderExecutionModel RendererModel
        {
            get => m_Model;
            set
            {
                // trigger a topology recomputation
                if (m_Model != value)
                    m_TopologyVersion.SignalTopologyChanged();

                m_Model = value;
            }

        }
    }

    partial class RenderGraph : IDisposable
    {
        internal struct BufferResizeStruct
        {
            public NodeHandle Handle;
            public int DataPortIndex;
            public int BufferOffset;
            public int Size;
            public SimpleType ItemType;
        }

        internal unsafe struct InputPortUpdateStruct
        {
            public enum UpdateType
            {
                PortArrayResize,
                SetData,
                RetainData
            };

            public UpdateType Operation;
            public NodeHandle Handle;
            public int DataPortIndex;
            public ushort SizeOrArrayIndex;
            public void* Data;
        }

        const Allocator PortAllocator = Allocator.Persistent;

        internal unsafe struct KernelNode
        {
            public bool AliveInRenderer => Kernel != null && KernelData != null && KernelPorts != null;
            public NodeHandle Handle;
            public RenderKernelFunction.BaseKernel* Kernel;
            public RenderKernelFunction.BaseData* KernelData;
            public RenderKernelFunction.BasePort* KernelPorts;
            // TODO: Get from cached type traits
            public int KernelDataSize;
            public int TraitsIndex;
            public JobHandle Fence;
            public LLTraitsHandle TraitsHandle;

            public void FreeInplace()
            {
                ClearPortAllocations();
                UnsafeUtility.Free(KernelData, Allocator.Persistent);
                UnsafeUtility.Free(Kernel, Allocator.Persistent);
                UnsafeUtility.Free(KernelPorts, Allocator.Persistent);
                this = new KernelNode();
            }

            public void ClearPortAllocations()
            {
                // we don't need to keep track, we can just walk over all output ports and free them.
                ref var traits = ref TraitsHandle.Resolve();
                foreach (var offset in traits.DataPorts.OutputBufferOffsets)
                {
                    ref var buffer = ref Unsafe.AsRef<Buffer<byte>>((byte*)KernelPorts + offset);
                    UnsafeUtility.Free(buffer.Ptr, PortAllocator);
                }

                for (int i = 0; i < traits.DataPorts.Inputs.Count; ++i)
                {
                    ref var portDecl = ref traits.DataPorts.Inputs[i];
                    void** inputPortPatch = (void**)((byte*)KernelPorts + portDecl.PatchOffset);
                    if (portDecl.IsArray)
                    {
                        ref var portArray = ref Unsafe.AsRef<UntypedPortArray>(inputPortPatch);
                        UntypedPortArray.Free(ref portArray, PortAllocator);
                    }
                    else if (DataInputUtility.PortOwnsMemory(inputPortPatch))
                        UnsafeUtility.Free(*inputPortPatch, PortAllocator);
                }
            }
        }

        // Note that this list and the one in NodeSet.m_Nodes alias each other
        // This means entries here will be sparse, but we avoid remapping tables (right now)
        BlitList<KernelNode> m_Nodes = new BlitList<KernelNode>(0);
        NativeList<JobHandle> m_IslandFences;
        NativeList<JobHandle> m_BackScheduledJobs;

        NodeSet m_Set;
        TraversalCache m_Cache;
        internal SharedData m_SharedData;
        // TODO: Test this is in sync with # of kernel nodes
        internal int m_ExistingNodes = 0;
        RenderExecutionModel m_Model;
        TopologyCacheAPI.VersionTracker m_PreviousVersion;
        bool m_IsRendering;
        AtomicSafetyManager.BufferProtectionScope m_BufferScope;

        public RenderGraph(NodeSet set)
        {
            m_Set = set;
            m_Cache = new TraversalCache(16, TraversalFlags.DataFlow);
            m_SharedData = new SharedData(32);
            m_Model = RenderExecutionModel.MaximallyParallel;
            m_IslandFences = new NativeList<JobHandle>(16, Allocator.Persistent);
            m_BackScheduledJobs = new NativeList<JobHandle>(16, Allocator.Persistent);
            m_PreviousVersion = TopologyCacheAPI.VersionTracker.Create();
            m_BufferScope = AtomicSafetyManager.BufferProtectionScope.Create();
        }

        public void CopyWorlds(GraphDiff ownedGraphDiff, RenderExecutionModel executionModel, BlitList<DataOutputValue> values, VersionedList<InputBatch> batches)
        {
            TopologyComputationContext topologyContext = new TopologyComputationContext();
            NativeArray<NodeHandle> activeNodes = new NativeArray<NodeHandle>();
            var bufferResizeCommands = new BufferResizeCommands();
            var inputPortUpdateCommands = new InputPortUpdateCommands();

            var internalDependencies = new JobHandle();

            JobHandle
                parallelResizeBuffers = default,
                parallelInputPortUpdates = default,
                parallelKernelBlit = default,
                parallelTopology = default,
                scheduleDeps = default,
                asyncMemoryCopy = default;

            try
            {
                Profiler.BeginSample("NodeSet.RenderGraph.SyncPreviousRender");
                SyncAnyRendering();
                ChangeRendererModel(executionModel);
                Profiler.EndSample();

                Profiler.BeginSample("NodeSet.RenderGraph.AlignWorld");
                activeNodes = AlignWorld(ref ownedGraphDiff, out bufferResizeCommands, out inputPortUpdateCommands);
                Profiler.EndSample();

                Profiler.BeginSample("NodeSet.RenderGraph.PrepareGraph");
                parallelTopology = TopologyComputationContext.InitializeContext(
                    internalDependencies,
                    out topologyContext,
                    m_Set.GetInternalEdges(),
                    m_Set.GetInternalTopologyIndices(),
                    m_Cache,
                    new TopologyComputationContext.NodeArraySource(activeNodes),
                    m_Set.TopologyVersion,
                    executionModel
                );

                parallelKernelBlit = CopyDirtyRenderData(internalDependencies, ref ownedGraphDiff, activeNodes);

                parallelResizeBuffers = ResizeDataPortBuffers(internalDependencies, bufferResizeCommands);
                bufferResizeCommands = new BufferResizeCommands(); // (owned by above system now)

                parallelInputPortUpdates = InputPortUpdates(internalDependencies, inputPortUpdateCommands);
                inputPortUpdateCommands = new InputPortUpdateCommands(); // (owned by above system now)

                parallelTopology = RefreshTopology(parallelTopology, topologyContext);

                m_ExternalDependencies = ComputeValueChunkAndPatchPorts(
                    JobHandle.CombineDependencies(parallelTopology, parallelInputPortUpdates)
                );

                m_ExternalDependencies = AssignExternalInputDataToPorts(
                    JobHandle.CombineDependencies(CalculateExternalInputDependencies(batches), m_ExternalDependencies),
                    batches
                );

                asyncMemoryCopy = FlushAsyncRequestMemory(parallelResizeBuffers, values);

                m_ExternalDependencies = JobHandle.CombineDependencies(m_ExternalDependencies, parallelKernelBlit, asyncMemoryCopy);
                Profiler.EndSample();

                Profiler.BeginSample("NodeSet.RenderGraph.RenderWorld");
                scheduleDeps = RenderWorld(
                    parallelTopology,
                    m_ExternalDependencies
                );

                Profiler.EndSample();

                Profiler.BeginSample("NodeSet.RenderGraph.PostScheduleTasks");
                scheduleDeps = InjectValueDependencies(scheduleDeps, values);

                ComputeOutputDependenciesForInputBatches(scheduleDeps, batches);
                UpdateTopologyVersion(m_Set.TopologyVersion);
                Profiler.EndSample();
            }
            catch (Exception e)
            {
                m_ExternalDependencies.Complete();
                parallelResizeBuffers.Complete();
                parallelInputPortUpdates.Complete();
                parallelKernelBlit.Complete();
                parallelTopology.Complete();
                scheduleDeps.Complete();
                asyncMemoryCopy.Complete();
                // TODO: If we ever reach this point through an exception, the worlds are now out of sync
                // and cannot be safely diff'ed anymore...
                // Should do a safe-pass and copy the whole world again.
                ClearNodes();
                Debug.LogError("Error while diff'ing worlds, rendering reset");
                Debug.LogException(e);
                throw;
            }
            finally
            {
                Profiler.BeginSample("NodeSet.RenderGraph.FinalizeParallelTasks");
                parallelResizeBuffers.Complete();
                parallelKernelBlit.Complete();
                parallelTopology.Complete();
                scheduleDeps.Complete();
                asyncMemoryCopy.Complete();

                ownedGraphDiff.Dispose();
                if (topologyContext.IsCreated)
                    topologyContext.Dispose();

                if (activeNodes.IsCreated)
                    activeNodes.Dispose();

                // only happens if we had an exception, otherwise the job takes ownership.
                if (bufferResizeCommands.IsCreated)
                    bufferResizeCommands.Dispose();
                if (inputPortUpdateCommands.IsCreated)
                    inputPortUpdateCommands.Dispose();

                Profiler.EndSample();
            }
        }

        void UpdateTopologyVersion(TopologyCacheAPI.VersionTracker setTopologyVersion)
        {
            m_PreviousVersion = setTopologyVersion;
        }

        public void Dispose()
        {
            SyncAnyRendering();
            ClearNodes();
            m_SharedData.Dispose();
            m_Nodes.Dispose();
            m_Cache.Dispose();
            m_IslandFences.Dispose();
            m_BufferScope.Dispose();
            m_BackScheduledJobs.Dispose();
        }

        /// <param name="internalDeps">Dependencies for scheduling the graph</param>
        /// <param name="externalDeps">Dependencies for the scheduled graph</param>
        /// <returns></returns>
        JobHandle RenderWorld(JobHandle internalDeps, JobHandle externalDeps)
        {
            var job = new WorldRenderingScheduleJob();

            try
            {
                job.Cache = m_Cache;
                job.Nodes = m_Nodes;
                job.RenderingMode = m_Model;
                job.DependencyCombiner = new NativeList<JobHandle>(5, Allocator.TempJob);
                job.IslandFences = m_IslandFences;
                job.Shared = m_SharedData;
                job.ExternalDependencies = externalDeps;

                Profiler.BeginSample("NodeSet.RenderGraph.WaitForSchedulingDependencies");
                internalDeps.Complete();
                Profiler.EndSample();
                // TODO: Change to job.Run() to run it through burst (currently not supported due to faulty detected of main thread).
                // Next TODO: Change to job.Schedule() if we can ever schedule jobs from non-main thread. This would remove any trace of 
                // the render graph on the main thread, and still be completely deterministic (although the future logic in copy worlds
                // would have to be rewritten a bit).
                job.Execute();

                // TODO: Move into WorldRenderingScheduleJob once we have an indirect version tracker
                RenderVersion++;
            }
            finally
            {
                if (job.DependencyCombiner.IsCreated)
                    job.DependencyCombiner.Dispose();
            }

            m_IsRendering = true;
            return new JobHandle();
        }

        void ChangeRendererModel(RenderExecutionModel model)
        {
            m_Model = model;
        }

        internal unsafe void SyncAnyRendering()
        {
            if (!m_IsRendering)
                return;

            RootFence.Complete();

            m_ExternalDependencies.Complete();
            JobHandle.CompleteAll(m_BackScheduledJobs);
            m_BackScheduledJobs.Clear();
            m_SharedData.SafetyManager->BumpTemporaryHandleVersions();
            m_BufferScope.Bump();
            m_IsRendering = false;
        }

        unsafe JobHandle AssignExternalInputDataToPorts(JobHandle deps, VersionedList<InputBatch> batches)
        {
            if (batches.UncheckedCount == 0)
                return deps;

            AssignInputBatchJob add;
            add.Nodes = m_Nodes;
            add.Shared = m_SharedData;

            for (int i = 0; i < batches.UncheckedCount; ++i)
            {
                ref var batch = ref batches[i];

                if (!batch.Valid)
                    continue;

                if (batch.RenderVersion != RenderVersion)
                    continue;

                add.Transients = batch.GetDeferredTransientBuffer();

                // Safe; memory is natively managed.
                fixed (InputBatch.InstalledPorts* installMemory = &batch.GetInstallMemory())
                    add.BatchInstall = installMemory;

                deps = add.Schedule(deps);
            }

            return deps;
        }

        unsafe JobHandle CalculateExternalInputDependencies(VersionedList<InputBatch> batches)
        {
            using (var batchDeps = new NativeList<JobHandle>(batches.UncheckedCount, Allocator.Temp))
            {
                for (int i = 0; i < batches.UncheckedCount; ++i)
                {
                    ref var batch = ref batches[i];

                    if (!batch.Valid || batch.RenderVersion != RenderVersion)
                        continue;

                    batchDeps.Add(batch.InputDependency);
                }

                return JobHandleUnsafeUtility.CombineDependencies((JobHandle*)batchDeps.GetUnsafePtr(), batchDeps.Length);
            }
        }

        unsafe void ComputeOutputDependenciesForInputBatches(JobHandle scheduleDeps, VersionedList<InputBatch> batches)
        {
            scheduleDeps.Complete();

            RemoveInputBatchJob remove;
            remove.Shared = m_SharedData;
            remove.Nodes = m_Nodes;

            for (var i = 0; i < batches.UncheckedCount; i++)
            {
                ref var batch = ref batches[i];

                // At this point, render version has ticked forward (world scheduling already happened)
                if (!batch.Valid || batch.RenderVersion != RenderVersion - 1)
                    continue;

                // When the batch is deferred (ongoingly computed),
                // we cannot derive precise dependencies
                // as targets for batch is not yet known.
                // So batch inherits dependency on entire graph.
                // TODO: Include node targets up front in a batch,
                // before deferred assignment.
                remove.Transients = batch.GetDeferredTransientBuffer();
                batch.OutputDependency = remove.Schedule(RootFence);
                m_BackScheduledJobs.Add(batch.OutputDependency);
            }
        }

        JobHandle FlushAsyncRequestMemory(JobHandle inputDependencies, BlitList<DataOutputValue> values)
        {
            return new FlushGraphValuesJob { Nodes = m_Nodes, Values = values }.Schedule(values.Count, 2, inputDependencies);
        }

        JobHandle InjectValueDependencies(JobHandle inputDependencies, BlitList<DataOutputValue> values)
        {
            return new CopyValueDependenciesJob { Nodes = m_Nodes, Values = values, Model = m_Model, IslandFences = m_IslandFences }.Schedule(inputDependencies);
        }

        unsafe JobHandle CopyDirtyRenderData(JobHandle inputDependencies, /* in */ ref GraphDiff ownedGraphDiff, NativeArray<NodeHandle> aliveNodes)
        {
            CopyDirtyRendererDataJob job;
            job.AliveNodes = aliveNodes;
            job.KernelNodes = m_Nodes;
            job.SimulationNodes = m_Set.GetInternalData();

            return job.Schedule(aliveNodes.Length, Mathf.Max(10, aliveNodes.Length / JobsUtility.MaxJobThreadCount), inputDependencies);
        }

        JobHandle RefreshTopology(JobHandle dependency, in TopologyComputationContext context)
        {
            return TopologyCacheAPI.ScheduleTopologyComputation(dependency, m_Set.TopologyVersion, context);
        }

        JobHandle ComputeValueChunkAndPatchPorts(JobHandle input)
        {
            if (m_Set.TopologyVersion != m_PreviousVersion)
            {
                ComputeValueChunkAndPatchPortsJob job;
                job.Cache = m_Cache;
                job.Nodes = m_Nodes;
                job.Shared = m_SharedData;
                return job.Schedule(input);
            }

            return input;
        }

        JobHandle ResizeDataPortBuffers(JobHandle dependency, BufferResizeCommands bufferResizeCommands)
        {
            return new ResizeOutputDataPortBuffers { OwnedCommands = bufferResizeCommands, Nodes = m_Nodes }.Schedule(dependency);
        }

        JobHandle InputPortUpdates(JobHandle dependency, InputPortUpdateCommands inputPortUpdateCommands)
        {
            return new UpdateInputDataPort { OwnedCommands = inputPortUpdateCommands, Nodes = m_Nodes, Shared = m_SharedData }.Schedule(dependency);
        }

        static unsafe void* AllocateAndCopyData(void* data, SimpleType type)
        {
            var dataCopy = UnsafeUtility.Malloc(type.Size, type.Align, PortAllocator);
            UnsafeUtility.MemCpy(dataCopy, data, type.Size);
            return dataCopy;
        }

        public static unsafe void* AllocateAndCopyData<TData>(in TData data)
            where TData : struct
        {
            // This should work, but we don't have Unsafe.AsRef(in) yet.
            // return AllocateAndCopyData(Unsafe.AsPointer(ref Unsafe.AsRef(data)), UnsafeUtility.SizeOf<TData>());
            var dataCopy = UnsafeUtility.Malloc(UnsafeUtility.SizeOf<TData>(), UnsafeUtility.AlignOf<TData>(), PortAllocator);
            Unsafe.AsRef<TData>(dataCopy) = data;
            return dataCopy;
        }

        NativeArray<NodeHandle> AlignWorld(/* in */ ref GraphDiff ownedGraphDiff, out BufferResizeCommands bufferResizeCommands, out InputPortUpdateCommands inputPortUpdateCommands)
        {
            var simulationNodes = m_Set.GetInternalData();
            var llTraits = m_Set.GetLLTraits();

            bufferResizeCommands = new BufferResizeCommands(0, Allocator.TempJob);
            bufferResizeCommands.Reserve(ownedGraphDiff.ResizedDataBuffers.Count);

            inputPortUpdateCommands = new InputPortUpdateCommands(0, Allocator.TempJob);
            inputPortUpdateCommands.Reserve(ownedGraphDiff.ResizedPortArrays.Count + ownedGraphDiff.MessagesArrivingAtDataPorts.Count);

            for (int i = 0; i < ownedGraphDiff.Commands.Count; ++i)
            {
                switch (ownedGraphDiff.Commands[i].command)
                {
                    case GraphDiff.Command.ResizeBuffer:
                    {
                        var args = ownedGraphDiff.ResizedDataBuffers[ownedGraphDiff.Commands[i].ContainerIndex];

                        ref var node = ref simulationNodes[args.handle.VHandle.Index];
                        ref var traits = ref llTraits[node.TraitsIndex].Resolve();

                        var outputs = traits.DataPorts.Outputs;

                        // convert the "global" port id into local list of data port buffers
                        for (int p = 0; p < outputs.Count; ++p)
                        {
                            if (outputs[p].PortNumber == args.port)
                            {
                                bufferResizeCommands.Add(new BufferResizeStruct { Handle = args.handle, DataPortIndex = p, BufferOffset = args.BufferOffset, Size = args.NewSize, ItemType = args.ItemType });
                                break;
                            }
                        }
                        break;
                    }
                    case GraphDiff.Command.ResizePortArray:
                    {
                        var args = ownedGraphDiff.ResizedPortArrays[ownedGraphDiff.Commands[i].ContainerIndex];

                        // Avoid nodes that never existed, because they were deleted in the same batch...
                        if (!m_Set.Exists(args.handle))
                            break;

                        ref var node = ref simulationNodes[args.handle.VHandle.Index];
                        ref var traits = ref llTraits[node.TraitsIndex].Resolve();

                        var inputs = traits.DataPorts.Inputs;

                        // convert the "global" port id into local list of data port buffers
                        for (int p = 0; p < inputs.Count; ++p)
                        {
                            if (inputs[p].PortNumber == args.port)
                            {
                                inputPortUpdateCommands.Add(new InputPortUpdateStruct { Operation = InputPortUpdateStruct.UpdateType.PortArrayResize, Handle = args.handle, DataPortIndex = p, SizeOrArrayIndex = args.NewSize });
                                break;
                            }
                        }
                        break;
                    }
                    case GraphDiff.Command.Create:
                    {
                        var handle = ownedGraphDiff.CreatedNodes[ownedGraphDiff.Commands[i].ContainerIndex];
                        ref var node = ref simulationNodes[handle.VHandle.Index];

                        // Avoid constructing nodes that never existed, because they were deleted in the same batch...
                        if (m_Set.Exists(handle))
                        {

                            if (Exists(handle))
                            {
                                // TODO: This is an error condition that will only happen if worlds 
                                // are misaligned; provided not to crash right now
                                // but should be handled in another place.
                                Debug.LogError("Reconstructing already existing node");
                                Destruct(handle);
                            }

                            if (node.HasKernelData)
                                Construct((handle, node.TraitsIndex, llTraits[node.TraitsIndex]));
                        }
                        break;
                    }
                    case GraphDiff.Command.Destroy:
                    {
                        var handleAndIndex = ownedGraphDiff.DeletedNodes[ownedGraphDiff.Commands[i].ContainerIndex];

                        // Only destroy the ones that for sure exist in our set (destroyed nodes can also be 
                        // non-kernel, which we don't care about)
                        if (Exists(handleAndIndex.Handle))
                        {
                            Destruct(handleAndIndex.Handle);
                        }
                        break;
                    }
                    case GraphDiff.Command.MessageToData:
                    unsafe
                    {
                        var args = ownedGraphDiff.MessagesArrivingAtDataPorts[ownedGraphDiff.Commands[i].ContainerIndex];

                        // Avoid messaging nodes that never existed, because they were deleted in the same batch...
                        if (!m_Set.Exists(args.handle))
                        {
                            UnsafeUtility.Free(args.msg, PortAllocator);
                            break;
                        }

                        ref var node = ref simulationNodes[args.handle.VHandle.Index];
                        ref var traits = ref llTraits[node.TraitsIndex].Resolve();

                        var inputs = traits.DataPorts.Inputs;

                        // convert the "global" port id into local list of data port buffers
                        int p;
                        for (p = 0; p < inputs.Count; ++p)
                        {
                            if (inputs[p].PortNumber == args.port.PortID)
                            {
                                var op = args.msg == null
                                    ? InputPortUpdateStruct.UpdateType.RetainData
                                    : InputPortUpdateStruct.UpdateType.SetData;
                                inputPortUpdateCommands.Add(new InputPortUpdateStruct { Operation = op, Handle = args.handle, DataPortIndex = p, SizeOrArrayIndex = args.port.IsArray ? args.port.ArrayIndex : (ushort)0, Data = args.msg });
                                break;
                            }
                        }
                        // This should never hapen.
                        if (p >= inputs.Count)
                            throw new InvalidOperationException("Could not map global port id to local data port index");
                        break;
                    }
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            var array = new NativeArray<NodeHandle>(m_ExistingNodes, Allocator.TempJob);

            // It's just way faster in Burst.
            AnalyseLiveNodes liveNodesJob;
            liveNodesJob.KernelNodes = m_Nodes;
            liveNodesJob.LiveNodes = array;
            liveNodesJob.Schedule().Complete();

            return array;
        }

        bool Exists(NodeHandle handle)
        {
            return Exists(ref m_Nodes, handle);
        }

        internal static bool Exists(ref BlitList<KernelNode> nodes, NodeHandle handle)
        {
            if (handle.VHandle.Index >= nodes.Count)
                return false;

            ref var knode = ref nodes[handle.VHandle.Index];
            return knode.AliveInRenderer && knode.Handle == handle;
        }

        void Destruct(NodeHandle handle)
        {
            m_Nodes[handle.VHandle.Index].FreeInplace();
            m_ExistingNodes--;
        }

        unsafe void Construct((NodeHandle handle, int traitsIndex, LLTraitsHandle traitsHandle) args)
        {
            var index = args.handle.VHandle.Index;
            m_Nodes.EnsureSize(index + 1);
            ref var node = ref m_Nodes[index];
            ref var traits = ref args.traitsHandle.Resolve();

            // TODO: Compact into one allocation (maybe, and then offset ptrs)
            node.KernelData = (RenderKernelFunction.BaseData*)Utility.CAlloc(traits.Storage.KernelData, Allocator.Persistent);
            node.Kernel = (RenderKernelFunction.BaseKernel*)Utility.CAlloc(traits.Storage.Kernel, Allocator.Persistent);
            node.KernelPorts = (RenderKernelFunction.BasePort*)Utility.CAlloc(traits.Storage.KernelPorts, Allocator.Persistent);
            node.KernelDataSize = traits.Storage.KernelData.Size;

            // Assign owner IDs to data output buffers
            foreach (var offset in traits.DataPorts.OutputBufferOffsets)
            {
                ref var buffer = ref Unsafe.AsRef<Buffer<byte>>((byte*)node.KernelPorts + offset);
                buffer = new Buffer<byte>(null, 0, args.handle);
            }

            // TODO: Investigate why this needs to happen. The job system doesn't seem to do proper version validation.
            node.Fence = new JobHandle();
            // Remove this.
            node.TraitsIndex = args.traitsIndex;
            node.TraitsHandle = args.traitsHandle;
            node.Handle = args.handle;
            m_ExistingNodes++;
        }

        void ClearNodes()
        {
            for (int i = 0; i < m_Nodes.Count; ++i)
            {
                if (m_Nodes[i].AliveInRenderer)
                {
                    m_Nodes[i].FreeInplace();
                }
            }
            m_ExistingNodes = 0;
        }

        // stuff exposed for tests.

        internal BlitList<KernelNode> GetInternalData() => m_Nodes;
    }

}
