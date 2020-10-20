using System;
using System.Diagnostics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Unity.DataFlowGraph
{
    using Topology = TopologyAPI<ValidatedHandle, InputPortArrayID, OutputPortArrayID>;
    using BufferResizeCommands = BlitList<RenderGraph.BufferResizeStruct>;
    using InputPortUpdateCommands = BlitList<RenderGraph.InputPortUpdateStruct>;

    partial class NodeSet
    {
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

        /// <summary>
        /// The render execution scheduling mode use for launching the processing of
        /// <see cref="IGraphKernel{TKernelData,TKernelPortDefinition}"/>s in the node set.
        /// <seealso cref="NodeSet.Update"/>
        /// </summary>
        /// <remarks>Changing this can drastically alter the execution runtime of your graph. The best choice largely depends on the topological layout of the graph in question.</remarks>
        public RenderExecutionModel RendererModel
        {
            get => InternalRendererModel;
            set => InternalRendererModel = value;
        }
    }

    public partial class NodeSetAPI
    {
        internal NodeSet.RenderExecutionModel Model;

        internal NodeSet.RenderExecutionModel InternalRendererModel
        {
            get => Model;
            set
            {
                // trigger a topology recomputation
                if (Model != value)
                    m_TopologyVersion.SignalTopologyChanged();

                Model = value;
            }

        }
    }

    partial class RenderGraph : IDisposable
    {
        internal unsafe struct BufferResizeStruct
        {
            /// <summary>
            /// Special (invalid) value for DataPortIndex indicating that the buffer resize should affect a kernel buffer rather than a data port buffer.
            /// </summary>
            public const int KernelBufferResizeHint = -1;
            public ValidatedHandle Handle;
            public int DataPortIndex;
            public int LocalBufferOffset;
            public int Size;
            public SimpleType ItemType
            {
                get
                {
#if DFG_ASSERTIONS
                    if (PotentialMemory != null)
                        throw new AssertionException("Memory should be adopted");
#endif
                    return m_ItemType;
                }
                set => m_ItemType = value;
            }

            SimpleType m_ItemType;

            /// <summary>
            /// If this field isn't null, adopt this memory instead of using <see cref="ItemType"/> to allocate memory
            /// </summary>
            public void* PotentialMemory;
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
            public ValidatedHandle Handle;
            public int DataPortIndex;
            public ushort SizeOrArrayIndex;
            public void* Data;
        }

        const Allocator PortAllocator = Allocator.Persistent;

        [DebuggerDisplay("{DebugDisplay(), nq}")]
        [DebuggerTypeProxy(typeof(KernelNodeDebugView))]
        internal unsafe struct KernelNode
        {
            public bool AliveInRenderer => Instance.Kernel != null;
            public LLTraitsHandle TraitsHandle;
            public KernelLayout.Pointers Instance;
            public ValidatedHandle Handle;
            public JobHandle Fence;
            // TODO: Get from cached type traits
            public int KernelDataSize;

            public void FreeInplace()
            {
                ClearAllocations();
                this = new KernelNode();
            }

            public void ClearAllocations()
            {
                ref var traits = ref TraitsHandle.Resolve();

                if (traits.KernelStorage.IsComponentNode)
                {
                    InternalComponentNode.GetGraphKernel(Instance.Kernel).Dispose();
                }

                // we don't need to keep track, we can just walk over all output ports and free them.
                foreach (var offset in traits.DataPorts.OutputBufferOffsets)
                {
                    offset.AsUntyped(Instance.Ports).FreeIfNeeded(PortAllocator);
                }

                foreach (var offset in traits.KernelStorage.KernelBufferInfos)
                {
                    offset.Offset.AsUntyped(Instance.Kernel).FreeIfNeeded(PortAllocator);
                }

                for (int i = 0; i < traits.DataPorts.Inputs.Count; ++i)
                {
                    ref var portDecl = ref traits.DataPorts.Inputs[i];

                    if (!portDecl.IsArray)
                        portDecl.GetStorageLocation(Instance.Ports)->FreeIfNeeded(PortAllocator);
                    else
                        portDecl.AsPortArray(Instance.Ports).Free(PortAllocator);
                }

                traits.KernelLayout.Free(Instance, Allocator.Persistent);
            }

            public ref BufferDescription GetKernelBufferAt(int byteOffset)
            {
                return ref *(BufferDescription*)((byte*)Instance.Kernel + byteOffset);
            }

            string DebugDisplay() => KernelNodeDebugView.DebugDisplay(this);
        }

        // Note that this list and the one in NodeSet.m_Nodes alias each other
        // This means entries here will be sparse, but we avoid remapping tables (right now)
        BlitList<KernelNode> m_Nodes = new BlitList<KernelNode>(0);
        NativeList<JobHandle> m_IslandFences;
        NativeList<JobHandle> m_BackScheduledJobs;
        NativeList<ValidatedHandle> m_ChangedNodes;

        NodeSetAPI m_Set;
        internal Topology.TraversalCache Cache;
        internal SharedData m_SharedData;
        // TODO: Test this is in sync with # of kernel nodes
        internal int m_NumExistingNodes = 0;
        NodeSet.RenderExecutionModel m_Model;
        Topology.CacheAPI.VersionTracker m_PreviousVersion;
        bool m_IsRendering;
        AtomicSafetyManager.BufferProtectionScope m_BufferScope;
        EntityQuery m_NodeSetAttachmentQuery;
        Topology.Database m_Database;
        FlatTopologyMap m_Map;

        EntityQuery AttachmentQuery
        {
            get
            {
                // FIXME: In lieu of a proper ECS mechanism for ordering system creation, we initialize our query lazily to
                // give a chance for the HostSystem to be properly created through OnCreate().
                if (m_NodeSetAttachmentQuery == default)
                    m_NodeSetAttachmentQuery = CreateNodeSetAttachmentQuery(m_Set.HostSystem);

                return m_NodeSetAttachmentQuery;
            }
        }

        public RenderGraph(NodeSetAPI set)
        {
            m_Set = set;
            Cache = new Topology.TraversalCache(
                16,
                (uint)PortDescription.Category.Data | ((uint)PortDescription.Category.Data << (int)PortDescription.CategoryShift.BackConnection),
                (uint)PortDescription.Category.Data | ((uint)PortDescription.Category.Data << (int)PortDescription.CategoryShift.FeedbackConnection));
            m_SharedData = new SharedData(SimpleType.MaxAlignment);
            m_Model = NodeSet.RenderExecutionModel.MaximallyParallel;
            m_IslandFences = new NativeList<JobHandle>(16, Allocator.Persistent);
            m_BackScheduledJobs = new NativeList<JobHandle>(16, Allocator.Persistent);
            m_PreviousVersion = Topology.CacheAPI.VersionTracker.Create();
            m_BufferScope = AtomicSafetyManager.BufferProtectionScope.Create();
            m_ChangedNodes = new NativeList<ValidatedHandle>(16, Allocator.Persistent);
            m_Database = new Topology.Database(32, Allocator.Persistent);
            m_Map = new FlatTopologyMap(32, Allocator.Persistent);
        }

        public void CopyWorlds(GraphDiff ownedGraphDiff, JobHandle ecsExternalDependencies, NodeSet.RenderExecutionModel executionModel, VersionedList<DataOutputValue> values)
        {
            var topologyContext = new Topology.ComputationContext<FlatTopologyMap>();
            var bufferResizeCommands = new BufferResizeCommands();
            var inputPortUpdateCommands = new InputPortUpdateCommands();

            var internalDependencies = new JobHandle();

            JobHandle
                parallelResizeBuffers = default,
                parallelInputPortUpdates = default,
                parallelKernelBlit = default,
                parallelTopology = default,
                scheduleDeps = default;

            try
            {
                Markers.SyncPreviousRenderProfilerMarker.Begin();
                SyncAnyRendering();
                ChangeRendererModel(executionModel);
                Markers.SyncPreviousRenderProfilerMarker.End();

                Markers.AlignWorldProfilerMarker.Begin();
                AlignWorld(ref ownedGraphDiff, out bufferResizeCommands, out inputPortUpdateCommands);
                Markers.AlignWorldProfilerMarker.End();

                Markers.PrepareGraphProfilerMarker.Begin();
                parallelTopology = Topology.ComputationContext<FlatTopologyMap>.InitializeContext(
                    internalDependencies,
                    out topologyContext,
                    m_Database,
                    m_Map,
                    Cache,
                    m_ChangedNodes,
                    m_NumExistingNodes,
                    m_Set.TopologyVersion,
                    AlgorithmFromModel(executionModel)
                );

                parallelTopology = PreTopologyECSPreparation(parallelTopology, topologyContext);

                parallelKernelBlit = CopyDirtyRenderData(internalDependencies, ref ownedGraphDiff);

                parallelResizeBuffers = ResizeDataPortBuffers(internalDependencies, bufferResizeCommands);
                bufferResizeCommands = new BufferResizeCommands(); // (owned by above system now)

                parallelInputPortUpdates = InputPortUpdates(internalDependencies, inputPortUpdateCommands);
                inputPortUpdateCommands = new InputPortUpdateCommands(); // (owned by above system now)

                parallelTopology = RefreshTopology(parallelTopology, topologyContext);

                m_ExternalDependencies = ComputeValueChunkAndPatchPorts(
                    JobHandle.CombineDependencies(
                        parallelTopology, // Patching ports needs parental information
                        parallelInputPortUpdates, // Patching ports is ordered after input updates, so they're not necessarily overwritten if assigned
                        parallelKernelBlit // Compute chunk now reads from kernel data for ecs -> ecs connections
                    )
                );

                m_ExternalDependencies = JobHandle.CombineDependencies(
                    m_ExternalDependencies,
                    ecsExternalDependencies,
                    parallelResizeBuffers
                );

                Markers.PrepareGraphProfilerMarker.End();

                Markers.RenderWorldProfilerMarker.Begin();
                scheduleDeps = RenderWorld(
                    parallelTopology,
                    m_ExternalDependencies
                );
                Markers.RenderWorldProfilerMarker.End();

                Markers.PostScheduleTasksProfilerMarker.Begin();
                scheduleDeps = InjectValueDependencies(scheduleDeps, values);
                UpdateTopologyVersion(m_Set.TopologyVersion);
                Markers.PostScheduleTasksProfilerMarker.End();
            }
            catch (Exception e)
            {
                m_ExternalDependencies.Complete();
                parallelResizeBuffers.Complete();
                parallelInputPortUpdates.Complete();
                parallelKernelBlit.Complete();
                scheduleDeps.Complete();
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
                Markers.FinalizeParallelTasksProfilerMarker.Begin();
                parallelResizeBuffers.Complete();
                parallelKernelBlit.Complete();
                scheduleDeps.Complete();

                ownedGraphDiff.Dispose();
                if (topologyContext.IsCreated)
                    topologyContext.Dispose();

                // only happens if we had an exception, otherwise the job takes ownership.
                if (bufferResizeCommands.IsCreated)
                    bufferResizeCommands.Dispose();
                if (inputPortUpdateCommands.IsCreated)
                    inputPortUpdateCommands.Dispose();

                Markers.FinalizeParallelTasksProfilerMarker.End();
            }
        }

        void UpdateTopologyVersion(Topology.CacheAPI.VersionTracker setTopologyVersion)
        {
            m_PreviousVersion = setTopologyVersion;
        }

        public void Dispose()
        {
            SyncAnyRendering();
            ClearNodes();
            m_SharedData.Dispose();
            m_Nodes.Dispose();
            Cache.Dispose();
            m_IslandFences.Dispose();
            m_BufferScope.Dispose();
            m_BackScheduledJobs.Dispose();
            m_ChangedNodes.Dispose();
            m_Database.Dispose();
            m_Map.Dispose();
        }

        /// <param name="topologyDependencies">Dependencies for scheduling the graph</param>
        /// <param name="externalDeps">Dependencies for the scheduled graph</param>
        /// <returns></returns>
        JobHandle RenderWorld(JobHandle topologyDependencies, JobHandle externalDeps)
        {
            var job = new WorldRenderingScheduleJob();

            try
            {
                job.Cache = Cache;
                job.Nodes = m_Nodes;
                job.RenderingMode = m_Model;
                job.DependencyCombiner = new NativeList<JobHandle>(5, Allocator.TempJob);
                job.IslandFences = m_IslandFences;
                job.Shared = m_SharedData;
                job.ExternalDependencies = externalDeps;
                job.TopologyDependencies = topologyDependencies;

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

        void ChangeRendererModel(NodeSet.RenderExecutionModel model)
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

            while(Cache.Errors.Count > 0)
            {
                Debug.LogError($"NodeSet.RenderGraph.Traversal: {Topology.TraversalCache.FormatError(Cache.Errors.Dequeue())}");
            }
        }

        JobHandle InjectValueDependencies(JobHandle inputDependencies, VersionedList<DataOutputValue> values)
        {
            return new CopyValueDependenciesJob { Nodes = m_Nodes, Values = values, Model = m_Model, IslandFences = m_IslandFences, Marker = Markers.CopyValueDependencies }.Schedule(inputDependencies);
        }

        unsafe JobHandle CopyDirtyRenderData(JobHandle inputDependencies, /* in */ ref GraphDiff ownedGraphDiff)
        {
            CopyDirtyRendererDataJob job;
            job.KernelNodes = m_Nodes;
            job.SimulationNodes = m_Set.GetInternalData();

            return job.Schedule(m_Nodes.Count, math.max(10, m_Nodes.Count / JobsUtility.MaxJobThreadCount), inputDependencies);
        }

        JobHandle RefreshTopology(JobHandle dependency, in Topology.ComputationContext<FlatTopologyMap> context)
        {
            return Topology.CacheAPI.ScheduleTopologyComputation(dependency, m_Set.TopologyVersion, context);
        }

        unsafe JobHandle PreTopologyECSPreparation(JobHandle deps, in Topology.ComputationContext<FlatTopologyMap> context)
        {
            var world = m_Set.HostSystem?.World;

            // No need to schedule ComponentNode related jobs if there are no nodes in existence since they would be a
            // no-op. This is also a workaround to a problem with entity queries during ECS shutdown.
            bool performComponentNodeJobs = m_NumExistingNodes > 0 && world != null;

            if (m_Set.TopologyVersion == m_PreviousVersion || !performComponentNodeJobs)
                return deps;

            ClearLocalECSInputsAndOutputsJob clearJob;

            clearJob.EntityStore = world.EntityManager.GetCheckedEntityDataAccess()->EntityComponentStore;
            clearJob.KernelNodes = m_Nodes;
            clearJob.NodeSetID = m_Set.NodeSetID;
            clearJob.NodeSetAttachmentType = m_Set.HostSystem.GetBufferTypeHandle<NodeSetAttachment>(true);
            clearJob.EntityType = m_Set.HostSystem.GetEntityTypeHandle();
            clearJob.Filter = context.Database.ChangedGroups;
            clearJob.Map = context.Topologies;

            deps = clearJob.Schedule(AttachmentQuery, deps);

            return deps;
        }

        unsafe JobHandle ComputeValueChunkAndPatchPorts(JobHandle deps)
        {
            var world = m_Set.HostSystem?.World;

            // No need to schedule ComponentNode related jobs if there are no nodes in existence since they would be a
            // no-op. This is also a workaround to a problem with entity queries during ECS shutdown.
            bool performComponentNodeJobs = m_NumExistingNodes > 0 && world != null;

            if (m_Set.TopologyVersion != m_PreviousVersion)
            {
                ComputeValueChunkAndPatchPortsJob job;
                job.Cache = Cache;
                job.Nodes = m_Nodes;
                job.Shared = m_SharedData;
                job.Marker = Markers.ComputeValueChunkAndResizeBuffers;
                deps = job.Schedule(Cache.NewGroups, 1, deps);
            }

            if (performComponentNodeJobs)
            {
                RepatchDFGInputsIfNeededJob ecsPatchJob;

                ecsPatchJob.EntityStore = world.EntityManager.GetCheckedEntityDataAccess()->EntityComponentStore;
                ecsPatchJob.KernelNodes = m_Nodes;
                ecsPatchJob.NodeSetID = m_Set.NodeSetID;
                ecsPatchJob.Shared = m_SharedData;
                ecsPatchJob.NodeSetAttachmentType = m_Set.HostSystem.GetBufferTypeHandle<NodeSetAttachment>();
                ecsPatchJob.EntityType = m_Set.HostSystem.GetEntityTypeHandle();

                deps = ecsPatchJob.Schedule(AttachmentQuery, deps);
            }

            return deps;
        }

        JobHandle ResizeDataPortBuffers(JobHandle dependency, BufferResizeCommands bufferResizeCommands)
        {
            return new ResizeOutputDataPortBuffers { OwnedCommands = bufferResizeCommands, Nodes = m_Nodes, Marker = Markers.ResizeOutputDataBuffers }.Schedule(dependency);
        }

        JobHandle InputPortUpdates(JobHandle dependency, InputPortUpdateCommands inputPortUpdateCommands)
        {
            return new UpdateInputDataPort { OwnedCommands = inputPortUpdateCommands, Nodes = m_Nodes, Shared = m_SharedData, Marker = Markers.UpdateInputDataPorts }.Schedule(dependency);
        }

        public static unsafe void* AllocateAndCopyData(void* data, SimpleType type)
        {
            var dataCopy = UnsafeUtility.Malloc(type.Size, type.Align, PortAllocator);
            UnsafeUtility.MemCpy(dataCopy, data, type.Size);
            return dataCopy;
        }

        public static unsafe void* AllocateAndCopyData<TData>(in TData data)
            where TData : struct
        {
            return AllocateAndCopyData(UnsafeUtilityExtensions.AddressOf(data), SimpleType.Create<TData>());
        }

        unsafe void AlignWorld(/* in */ ref GraphDiff ownedGraphDiff, out BufferResizeCommands bufferResizeCommands, out InputPortUpdateCommands inputPortUpdateCommands)
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

                        // Avoid nodes that never existed, because they were deleted in the same batch...
                        if (!simulationNodes.StillExists(args.Handle))
                            break;

                        ref var node = ref simulationNodes[args.Handle];
                        ref var traits = ref llTraits[node.TraitsIndex].Resolve();

                        var portNumber = args.Port == default ? BufferResizeStruct.KernelBufferResizeHint : traits.DataPorts.FindOutputDataPortNumber(args.Port.PortID);

                        var resize = new BufferResizeStruct
                        {
                            Handle = args.Handle,
                            DataPortIndex = portNumber,
                            LocalBufferOffset = args.LocalBufferOffset,
                            Size = args.NewSize
                        };

                        if(args.PotentialMemory != null)
                            resize.PotentialMemory = args.PotentialMemory;
                        else
                            resize.ItemType = args.ItemType;

                        bufferResizeCommands.Add(resize);

                        break;
                    }
                    case GraphDiff.Command.ResizePortArray:
                    {
                        var args = ownedGraphDiff.ResizedPortArrays[ownedGraphDiff.Commands[i].ContainerIndex];

                        // Avoid nodes that never existed, because they were deleted in the same batch...
                        if (!simulationNodes.StillExists(args.Destination.Handle))
                            break;

                        ref var node = ref simulationNodes[args.Destination.Handle];
                        ref var traits = ref llTraits[node.TraitsIndex].Resolve();

                        var portNumber = traits.DataPorts.FindInputDataPortNumber(args.Destination.Port.PortID);

                        inputPortUpdateCommands.Add(
                            new InputPortUpdateStruct {
                                Operation = InputPortUpdateStruct.UpdateType.PortArrayResize,
                                Handle = args.Destination.Handle,
                                DataPortIndex = portNumber,
                                SizeOrArrayIndex = args.NewSize
                            }
                        );

                        break;
                    }
                    case GraphDiff.Command.Create:
                    {
                        var handle = ownedGraphDiff.CreatedNodes[ownedGraphDiff.Commands[i].ContainerIndex];
                        ref var node = ref simulationNodes[handle];

                        m_Map.EnsureSize(handle.Versioned.Index + 1);


                        // Avoid constructing nodes that never existed, because they were deleted in the same batch...
                        if (simulationNodes.StillExists(handle))
                        {

                            if (StillExists(handle))
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
                        if (StillExists(handleAndIndex.Handle))
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
                        if (!simulationNodes.StillExists(args.Destination.Handle))
                        {
                            UnsafeUtility.Free(args.msg, PortAllocator);
                            break;
                        }

                        ref var node = ref simulationNodes[args.Destination.Handle];
                        ref var traits = ref llTraits[node.TraitsIndex].Resolve();

                        var portNumber = traits.DataPorts.FindInputDataPortNumber(args.Destination.Port.PortID);

                        var op = args.msg == null ? InputPortUpdateStruct.UpdateType.RetainData : InputPortUpdateStruct.UpdateType.SetData;

                        inputPortUpdateCommands.Add(
                            new InputPortUpdateStruct {
                                Operation = op,
                                Handle = args.Destination.Handle,
                                DataPortIndex = portNumber,
                                SizeOrArrayIndex = args.Destination.Port.IsArray ? args.Destination.Port.ArrayIndex : (ushort)0,
                                Data = args.msg
                            }
                        );

                        break;
                    }
                    case GraphDiff.Command.CreatedConnection:
                    {
                        ref readonly var args = ref ownedGraphDiff.CreatedConnections[ownedGraphDiff.Commands[i].ContainerIndex];

                        if (m_Set.Nodes.StillExists(args.Destination) && m_Set.Nodes.StillExists(args.Source))
                            m_Database.Connect(ref m_Map, args.TraversalFlags, args.Source, args.SourceOutputPort, args.Destination, args.DestinationInputPort);

                        break;
                    }
                    case GraphDiff.Command.DeletedConnection:
                    {
                        ref readonly var args = ref ownedGraphDiff.DeletedConnections[ownedGraphDiff.Commands[i].ContainerIndex];

                        if (m_Set.Nodes.StillExists(args.Destination) && m_Set.Nodes.StillExists(args.Source))
                            m_Database.Disconnect(ref m_Map, args.Source, args.SourceOutputPort, args.Destination, args.DestinationInputPort);

                        break;
                    }
                    case GraphDiff.Command.GraphValueCreated:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            // It's just way faster in Burst.
            m_ChangedNodes.Clear();
            AnalyseLiveNodes liveNodesJob;
            liveNodesJob.KernelNodes = m_Nodes;
            liveNodesJob.ChangedNodes = m_ChangedNodes;
            liveNodesJob.Marker = Markers.AnalyseLiveNodes;
            liveNodesJob.Map = m_Map;
            liveNodesJob.Filter = m_Database;
            liveNodesJob.Run();
        }

        bool StillExists(ValidatedHandle handle)
        {
            return StillExists(ref m_Nodes, handle);
        }

        internal static bool StillExists(ref BlitList<KernelNode> nodes, ValidatedHandle handle)
        {
            if (handle.Versioned.Index >= nodes.Count)
                return false;

            ref var knode = ref nodes[handle.Versioned.Index];
            return knode.AliveInRenderer && knode.Handle == handle;
        }

        void Destruct(ValidatedHandle handle)
        {
            m_Database.DisconnectAll(ref m_Map, handle);
            m_Database.VertexDeleted(ref m_Map, handle);
            m_Map[handle] = default;
            m_Nodes[handle.Versioned.Index].FreeInplace();
            m_NumExistingNodes--;
        }

        unsafe void Construct((ValidatedHandle handle, int traitsIndex, LLTraitsHandle traitsHandle) args)
        {
            var index = args.handle.Versioned.Index;
            m_Nodes.EnsureSize(index + 1);

            ref var node = ref m_Nodes[index];
            ref var traits = ref args.traitsHandle.Resolve();

            node.Instance = traits.KernelLayout.Allocate(Allocator.Persistent);
            node.KernelDataSize = traits.KernelStorage.KernelData.Size;

            // Assign owner IDs to data output buffers
            foreach (var offset in traits.DataPorts.OutputBufferOffsets)
            {
                offset.AsUntyped(node.Instance.Ports) = new BufferDescription(null, 0, args.handle);
            }

            // Assign owner IDs to kernel state buffers
            foreach (var offset in traits.KernelStorage.KernelBufferInfos)
            {
                offset.Offset.AsUntyped(node.Instance.Kernel) = new BufferDescription(null, 0, args.handle);
            }

            // TODO: Investigate why this needs to happen. The job system doesn't seem to do proper version validation.
            node.Fence = new JobHandle();
            node.TraitsHandle = args.traitsHandle;
            node.Handle = args.handle;

            if(traits.KernelStorage.IsComponentNode)
            {
                InternalComponentNode.GetGraphKernel(node.Instance.Kernel).Create();
            }

            m_NumExistingNodes++;

            m_Database.VertexCreated(ref m_Map, args.handle);
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
            m_NumExistingNodes = 0;
        }

        // stuff exposed for tests.

        internal BlitList<KernelNode> GetInternalData() => m_Nodes;
        internal FlatTopologyMap GetMap_ForTesting() => m_Map;
        internal Topology.Database GetDatabase_ForTesting() => m_Database;

    }

}
