using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;

namespace Unity.DataFlowGraph
{
    using Topology = TopologyAPI<ValidatedHandle, InputPortArrayID, OutputPortArrayID>;
    using InputPortUpdateCommands = BlitList<RenderGraph.InputPortUpdateStruct>;

    /// <summary>
    /// In order to support feedback connections, we set up a traversal cache with an "alternate" hierarchy which
    /// differs from the normal traversal hierarchy. This alternate hierarchy represents the port connectivity of
    /// the data graph which we use for patching. In general, it is identical to the traversal hierarchy except that
    /// feedback connections are reversed.
    /// </summary>
    static class VertexCacheExt
    {
        /// <summary>
        /// Get an enumerator to inputs connected to this port.
        /// </summary>
        /// <remarks>
        /// In DFG, it only makes sense to have one input to any input port, hence the naming.
        /// It should be checked.
        /// </remarks>
        public static Topology.InputConnectionCacheWalker GetInputForPatchingByPort(this Topology.VertexCache vertexCache, InputPortArrayID port)
            => vertexCache.GetParentConnectionsByPort(port, Topology.TraversalCache.Hierarchy.Alternate);

        /// <summary>
        /// Get an enumerator to all data inputs to a node.
        /// </summary>
        public static Topology.InputConnectionCacheWalker GetInputsForPatching(this Topology.VertexCache vertexCache)
            => vertexCache.GetParentConnections(Topology.TraversalCache.Hierarchy.Alternate);

    }

    partial class RenderGraph : IDisposable
    {
        internal unsafe struct SharedData : IDisposable
        {
            [NativeDisableUnsafePtrRestriction]
            public void* BlankPage;

            [NativeDisableUnsafePtrRestriction]
            public AtomicSafetyManager* SafetyManager;

            public SharedData(int alignment)
            {
                BlankPage = UnsafeUtility.Malloc(DataPortDeclarations.k_MaxInputSize, alignment, Allocator.Persistent);
                UnsafeUtility.MemClear(BlankPage, DataPortDeclarations.k_MaxInputSize);

                SafetyManager = Utility.CAlloc<AtomicSafetyManager>(Allocator.Persistent);
                *SafetyManager = AtomicSafetyManager.Create();
            }

            public void Dispose()
            {
                SafetyManager->Dispose();

                UnsafeUtility.Free(BlankPage, Allocator.Persistent);
                UnsafeUtility.Free(SafetyManager, Allocator.Persistent);
            }
        }

        unsafe struct WorldRenderingScheduleJob : IJob
        {
            public Topology.TraversalCache Cache;
            public BlitList<KernelNode> Nodes;
            public NodeSet.RenderExecutionModel RenderingMode;
            public NativeList<JobHandle> DependencyCombiner;
            public NativeList<JobHandle> IslandFences;
            public SharedData Shared;
            public JobHandle ExternalDependencies;
            public JobHandle TopologyDependencies;

            public void Execute()
            {
                IslandFences.Clear();

                switch (RenderingMode)
                {
                    case NodeSet.RenderExecutionModel.SingleThreaded:
                        ScheduleSingle();
                        break;

                    case NodeSet.RenderExecutionModel.Synchronous:
                        ExecuteInPlace();
                        break;

                    case NodeSet.RenderExecutionModel.Islands:
                        ScheduleIslands();
                        break;

                    case NodeSet.RenderExecutionModel.MaximallyParallel:
                        ScheduleJobified();
                        break;

                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            void ExecuteInPlace()
            {
                ExternalDependencies.Complete();

                for(int i = 0; i < Cache.Groups.Length; ++i)
                {
                    foreach (var nodeCache in new Topology.GroupWalker(Cache.Groups[i]))
                    {
                        var index = nodeCache.Vertex.Versioned.Index;
                        ref var node = ref Nodes[index];

                        if ((node.RunState & KernelNode.Flags.WillRun) > 0)
                        {
                            ref var traits = ref node.TraitsHandle.Resolve();
                            var ctx = new RenderContext(nodeCache.Vertex, Shared.SafetyManager);

#if DFG_PER_NODE_PROFILING
                            traits.VTable.KernelMarker.Begin();
#endif

                            traits.VTable.KernelFunction.Invoke(ctx, node.Instance);

#if DFG_PER_NODE_PROFILING
                            traits.VTable.KernelMarker.End();
#endif
                        }
                    }
                }
            }

            void ScheduleIslands()
            {
                var job = new ParallelRenderer
                {
                    Nodes = Nodes,
                    Cache = Cache,
                    Shared = Shared
                };

                IslandFences.Add(job.Schedule(Cache.Groups, 1, ExternalDependencies));
            }

            void ScheduleSingle()
            {
                var job = new SingleThreadedRenderer
                {
                    Nodes = Nodes,
                    Cache = Cache,
                    Shared = Shared
                };

                IslandFences.Add(job.Schedule(ExternalDependencies));
            }

            void ScheduleJobified()
            {
                Markers.WaitForSchedulingDependenciesProfilerMarker.Begin();
                TopologyDependencies.Complete();
                Markers.WaitForSchedulingDependenciesProfilerMarker.End();

                for (int i = 0; i < Cache.Groups.Length; ++i)
                {
                    foreach (var nodeCache in new Topology.GroupWalker(Cache.Groups[i]))
                    {
                        var index = nodeCache.Vertex.Versioned.Index;
                        ref var node = ref Nodes[index];

                        ref var traits = ref node.TraitsHandle.Resolve();

                        DependencyCombiner.Clear();

                        var parents = nodeCache.GetParents();

                        foreach (var parentCache in parents)
                        {
                            var parentIndex = parentCache.Vertex.Versioned.Index;
                            ref var parent = ref Nodes[parentIndex];
                            DependencyCombiner.Add(parent.Fence);
                        }

                        JobHandle inputDependencies;

                        if (DependencyCombiner.Length > 0)
                            inputDependencies = JobHandle.CombineDependencies(DependencyCombiner);
                        else
                            inputDependencies = ExternalDependencies;

                        if ((node.RunState & KernelNode.Flags.WillRun) != 0)
                        {
                            node.Fence = traits.VTable.KernelFunction.Schedule(
                                inputDependencies,
                                new RenderContext(nodeCache.Vertex, Shared.SafetyManager),
                                node.Instance
                            );
                        }
                        else
                        {
                            node.Fence = inputDependencies;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Can run in parallel as long as no islands overlap.
        /// Previously, patching of every node could run in parallel but this is no longer possible with component nodes
        /// (due to mutation of the I/O lists from multiple threads).
        /// </summary>
        [BurstCompile]
        unsafe struct ComputeValueChunkAndPatchPortsJob : IJobParallelForDefer
        {
            public Topology.TraversalCache Cache;
            public BlitList<KernelNode> Nodes;
            public SharedData Shared;

            public ProfilerMarker Marker;

            public void Execute(int newGroup)
            {
                Marker.Begin();

                var translatedGroup = Cache.NewGroups[newGroup];

                // It would make more sense to walk by node type, and batch all nodes for these types.
                // Requires sorting or ECS/whatever firstly, though.

                foreach (var nodeCache in new Topology.GroupWalker(Cache.Groups[translatedGroup]))
                {
                    var index = nodeCache.Vertex.Versioned.Index;
                    ref var nodeKernel = ref Nodes[index];
                    ref var traits = ref nodeKernel.TraitsHandle.Resolve();

                    if (traits.KernelStorage.IsComponentNode)
                    {
                        foreach (var c in nodeCache.GetInputsForPatching())
                        {
                            InternalComponentNode.RecordInputConnection(c.InputPort, c.OutputPort, c.Target.Vertex, nodeKernel.Instance, Nodes);
                        }
                        continue;
                    }

                    for (int i = 0; i < traits.DataPorts.Inputs.Count; ++i)
                    {
                        ref var portDecl = ref traits.DataPorts.Inputs[i];
                        var portID = traits.DataPorts.GetPortIDForInputIndex(i);

                        if (!portDecl.IsArray)
                        {
                            var inputEnumerator = nodeCache.GetInputForPatchingByPort(new InputPortArrayID(portID));
                            PatchPort(ref inputEnumerator, portDecl.GetStorageLocation(nodeKernel.Instance.Ports));
                        }
                        else
                        {
                            ref var portArray = ref portDecl.AsPortArray(nodeKernel.Instance.Ports);

                            for (ushort j = 0; j < portArray.Size; ++j)
                            {
                                var inputEnumerator = nodeCache.GetInputForPatchingByPort(new InputPortArrayID(portID, j));
                                PatchPort(ref inputEnumerator, portArray.NthInputStorage(j));
                            }
                        }
                    }
                }

                Marker.End();
            }

            void PatchPort(ref Topology.InputConnectionCacheWalker inputEnumerator, DataInputStorage* inputStorage)
            {
                switch (inputEnumerator.Count)
                {
                    case 0:

                        // No inputs, have to create a default value (careful to preserve input allocations for
                        // unconnected inputs that have been messaged - not applicable to buffer inputs)
                        if (!inputStorage->OwnsMemory())
                            *inputStorage = new DataInputStorage(Shared.BlankPage);

                        break;
                    case 1:

                        // An input, link up with the output value
                        inputEnumerator.MoveNext();
                        // Handle previously unconnected inputs that have been messaged and are now being
                        // connected for the first time (not applicable to buffer inputs)
                        inputStorage->FreeIfNeeded(PortAllocator);

                        PatchOrDeferInput(inputStorage, inputEnumerator.Current.Target.Vertex, inputEnumerator.Current.OutputPort);

                        break;
                    default:
                        throw new AssertionException("Cannot have multiple data inputs to the same port");
                }
            }

            void PatchOrDeferInput(DataInputStorage* patch, ValidatedHandle node, OutputPortArrayID port)
            {
                var parentIndex = node.Versioned.Index;
                ref var parentKernel = ref Nodes[parentIndex];
                ref var parentTraits = ref parentKernel.TraitsHandle.Resolve();

                if (!parentTraits.KernelStorage.IsComponentNode)
                {
                    // (Common case)
                    var outputPointer = parentTraits
                        .DataPorts
                        .FindOutputDataPort(port.PortID)
                        .Resolve(parentKernel.Instance.Ports, port.ArrayIndex);

                    *patch = new DataInputStorage(outputPointer);
                    return;
                }

                // Record the requirement of input data coming from ECS. The actual data pointer will be patched in
                // in a follow-up job.
                InternalComponentNode.RecordOutputConnection(patch, parentKernel.Instance.Kernel, port.PortID);
            }
        }

        [BurstCompile]
        unsafe struct ResizeOutputDataPortBuffers : IJob
        {
            public BlitList<KernelNode> Nodes;
            public BlitList<GraphDiff.BufferResizedTuple> Commands;

            public ProfilerMarker Marker;

            public void Execute()
            {
                Marker.Begin();

                for (int i = 0; i < Commands.Count; ++i)
                {
                    ref var command = ref Commands[i];
                    var handle = command.Handle;

                    if (!StillExists(ref Nodes, handle))
                        continue;

                    ref var buffer = ref GetBufferDescription(ref Nodes[handle.Versioned.Index], command);

                    // Adopt memory directly from the command?
                    if(command.PotentialMemory != null)
                    {
                        // free the old one.
                        buffer.FreeIfNeeded(PortAllocator);

                        buffer = new BufferDescription(command.PotentialMemory, command.NewSize, buffer.OwnerNode);
                        continue;
                    }

                    var oldSize = buffer.Size;

                    // We will need to realloc if the new size is larger than the previous size.
                    if (oldSize >= command.NewSize)
                    {
                        // If the new size is relatively close to the old size re-use the existing allocation.
                        // (Note that skipping realloc down to command.Size/2 is stable since command.Size/2 will remain fixed
                        // regardless of how many succesive downsizes are done. Note that we will not however benefit from
                        // future upsizes which would fit in the original memory allocation.)
                        if (oldSize / 2 < command.NewSize)
                        {
                            buffer = new BufferDescription(buffer.Ptr, command.NewSize, buffer.OwnerNode);
                            continue;
                        }
                    }

                    var type = new SimpleType(command.ItemType.Size * command.NewSize, command.ItemType.Align);

                    // free the old one.
                    buffer.FreeIfNeeded(PortAllocator);

                    buffer = new BufferDescription(
                        command.NewSize == 0 ? null : (byte*)Utility.CAlloc(type, PortAllocator),
                        command.NewSize,
                        buffer.OwnerNode
                    );
                }

                Marker.End();
            }

            static ref BufferDescription GetBufferDescription(ref KernelNode nodeKernel, in GraphDiff.BufferResizedTuple command)
            {
                if (command.IsKernelResize)
                {
                    return ref nodeKernel.GetKernelBuffer(command.KernelBufferIndex);
                }

                ref var traits = ref nodeKernel.TraitsHandle.Resolve();

                return ref traits
                    .DataPorts
                    .GetAggregateOutputBuffer(nodeKernel.Instance.Ports, traits.DataPorts.FindOutputDataPort(command.Port.PortID), command.PortBufferIndex, command.Port.ArrayIndex);
            }
        }

        [BurstCompile]
        unsafe struct UpdateInputDataPort : IJob
        {
            public BlitList<KernelNode> Nodes;
            public InputPortUpdateCommands OwnedCommands;
            public SharedData Shared;

            public ProfilerMarker Marker;

            public void Execute()
            {
                Marker.Begin();

                for (int i = 0; i < OwnedCommands.Count; ++i)
                {
                    ref var command = ref OwnedCommands[i];

                    ref var node = ref Nodes[command.Handle.Versioned.Index];
                    ref var traits = ref node.TraitsHandle.Resolve();

                    ref readonly var portDecl = ref traits.DataPorts.FindInputDataPort(command.Port);

                    switch (command.Operation)
                    {
                        case InputPortUpdateStruct.UpdateType.PortArrayResize:
                        {
                            portDecl.AsPortArray(node.Instance.Ports).Resize(command.SizeOrArrayIndex, Shared.BlankPage, PortAllocator);
                            break;
                        }
                        case InputPortUpdateStruct.UpdateType.RetainData:
                        case InputPortUpdateStruct.UpdateType.SetData:
                        {
                            var storage = portDecl.GetStorageLocation(node.Instance.Ports, command.SizeOrArrayIndex);
                            void* newData;

                            if (command.Operation == InputPortUpdateStruct.UpdateType.RetainData)
                                newData = AllocateAndCopyData(storage->Pointer, portDecl.Type);
                            else
                                newData = command.Data;

                            storage->FreeIfNeeded(PortAllocator);
                            *storage = new DataInputStorage(newData, DataInputStorage.Ownership.OwnedByPort);

                            break;
                        }
                    }
                }

                OwnedCommands.Dispose();

                Marker.End();
            }
        }


        [BurstCompile]
        unsafe struct ResizeOutputDataPortArraysJob : IJob
        {
            public BlitList<KernelNode> Nodes;
            public BlitList<GraphDiff.OutputPortArrayResizedTuple> Commands;
            public Topology.Database.Readonly Connections;
            public FlatTopologyMap Map;

            public ProfilerMarker Marker;

            public void Execute()
            {
                Marker.Begin();

                for (int i = 0; i < Commands.Count; ++i)
                {
                    ref var command = ref Commands[i];

                    if (!StillExists(ref Nodes, command.Destination.Handle))
                        continue;

                    ref var node = ref Nodes[command.Destination.Handle.Versioned.Index];
                    ref var traits = ref node.TraitsHandle.Resolve();

                    ref readonly var portDecl = ref traits.DataPorts.FindOutputDataPort(command.Destination.Port.PortID);

                    ref var portArray = ref portDecl.AsPortArray(node.Instance.Ports);
                    portArray.Resize(node.Instance.Ports, traits.DataPorts, portDecl, command.Destination.Handle, command.NewSize, PortAllocator);

                    if (command.NewSize > 0)
                    {
                        for (var it = Map.GetRef(node.Handle).OutputHeadConnection; it != FlatTopologyMap.InvalidConnection; it = Connections[it].NextOutputConnection)
                        {
                            ref readonly var conn = ref Connections[it];
                            if (conn.SourceOutputPort.PortID == command.Destination.Port.PortID)
                                RepatchInput(portDecl.Resolve(node.Instance.Ports, conn.SourceOutputPort.ArrayIndex), conn.DestinationInputPort, conn.SourceOutputPort, conn.Source, conn.Destination);
                        }
                    }
                }

                Marker.End();
            }

            void RepatchInput(void* patch, InputPortArrayID inputPort, OutputPortArrayID outputPort, ValidatedHandle parent, ValidatedHandle child)
            {
                ref var childNode = ref Nodes[child.Versioned.Index];
                ref var childTraits = ref childNode.TraitsHandle.Resolve();

                if (childTraits.KernelStorage.IsComponentNode)
                {
                    InternalComponentNode.RecordInputConnection(inputPort, outputPort, parent, childNode.Instance, Nodes);
                    return;
                }

                ref readonly var childPortDecl =
                    ref childTraits.DataPorts.FindInputDataPort(inputPort.PortID);

                var inputStorage = childPortDecl.GetStorageLocation(childNode.Instance.Ports, inputPort.ArrayIndex);

                if (inputStorage != null)
                    inputStorage->FreeIfNeeded(PortAllocator);

                *inputStorage = new DataInputStorage(patch);
            }
        }

        [BurstCompile]
        unsafe struct CopyDirtyRendererDataJob : IJobParallelFor
        {
            public BlitList<KernelNode> KernelNodes;
            public VersionedList<InternalNodeData> SimulationNodes;

            public void Execute(int nodeIndex)
            {
                var data = KernelNodes[nodeIndex].Instance.Data;
                if (data != null) // Alive ?
                    UnsafeUtility.MemCpy(data, SimulationNodes.UnvalidatedItemAt(nodeIndex).KernelData, KernelNodes[nodeIndex].KernelDataSize);
            }
        }

        [BurstCompile]
        unsafe struct SingleThreadedRenderer : IJob
        {
            public BlitList<KernelNode> Nodes;
            public Topology.TraversalCache Cache;
            public SharedData Shared;

            public void Execute()
            {
                for (int i = 0; i < Cache.Groups.Length; ++i)
                {
                    foreach (var nodeCache in new Topology.GroupWalker(Cache.Groups[i]))
                    {
                        var index = nodeCache.Vertex.Versioned.Index;
                        ref var node = ref Nodes[index];
                        ref var traits = ref node.TraitsHandle.Resolve();

                        if ((node.RunState & KernelNode.Flags.WillRun) > 0)
                        {
#if DFG_PER_NODE_PROFILING
                            traits.VTable.KernelMarker.Begin();
#endif

                            traits.VTable.KernelFunction.Invoke(new RenderContext(nodeCache.Vertex, Shared.SafetyManager), node.Instance);

#if DFG_PER_NODE_PROFILING
                            traits.VTable.KernelMarker.End();
#endif
                        }
                    }
                }
            }
        }

        [BurstCompile]
        unsafe struct ParallelRenderer : IJobParallelForDefer
        {
            public BlitList<KernelNode> Nodes;
            public Topology.TraversalCache Cache;
            public SharedData Shared;

            public void Execute(int islandIndex)
            {
                foreach (var nodeCache in new Topology.GroupWalker(Cache.Groups[islandIndex]))
                {
                    var index = nodeCache.Vertex.Versioned.Index;
                    ref var node = ref Nodes[index];

                    if((node.RunState & KernelNode.Flags.WillRun) > 0)
                    {
                        ref var traits = ref node.TraitsHandle.Resolve();

#if DFG_PER_NODE_PROFILING
                        traits.VTable.KernelMarker.Begin();
#endif

                        traits.VTable.KernelFunction.Invoke(new RenderContext(nodeCache.Vertex, Shared.SafetyManager), node.Instance);

#if DFG_PER_NODE_PROFILING
                        traits.VTable.KernelMarker.End();
#endif
                    }
                }
            }
        }

        [BurstCompile]
        internal struct AnalyseLiveNodes : IJob
        {
            public NativeList<ValidatedHandle> ChangedNodes;
            public Topology.Database Filter;
            public FlatTopologyMap Map;
            public BlitList<KernelNode> KernelNodes;
            public ProfilerMarker Marker;

            public void Execute()
            {
                Marker.Begin();
                // TODO: It seems inevitable that we have a full scan of all nodes each frame
                // because there's no 1:1 equivalence between kernel nodes and normal nodes..
                // Ideally this wouldn't happen since it's another O(n) operation
                for (int i = 0; i < KernelNodes.Count; ++i)
                {
                    ref readonly var node = ref KernelNodes[i];
                    if (!node.AliveInRenderer)
                        continue;

                    ref readonly var topology = ref Map.GetRef(node.Handle);

                    if(Filter.DidChange(topology.GroupID))
                    {
                        ChangedNodes.Add(node.Handle);
                    }
                }

                Marker.End();
            }
        }

        [BurstCompile]
        unsafe struct ComputeCullableIslandCandidates : IJob
        {
            public enum TopologyMode
            {
                Incremental, AllGroups
            }
            public Topology.TraversalCache Cache;
            public BlitList<GraphDiff.GraphValueObservationTuple> ChangedGraphValuesFromDiff;
            public BlitList<KernelNode> KernelNodes;
            public NativeList<int> ResultIslands;
            public FlatTopologyMap Map;
            public ProfilerMarker Marker;
            public TopologyMode Mode;

            public void Execute()
            {
                Marker.Begin();

                ResultIslands.Clear();

                if(Mode == TopologyMode.AllGroups)
                {
                    for (int i = 0; i < Cache.Groups.Length; ++i)
                    {
                        ResultIslands.Add(i);
                    }
                }
                else if (Mode == TopologyMode.Incremental)
                {
                    // Start with new islands, which is always a unique list
                    ResultIslands.AddRange(Cache.NewGroups);
                }
                // Only recompute islands where new graph values were created
                for(int i = 0; i < ChangedGraphValuesFromDiff.Count; ++i)
                {
                    var change = ChangedGraphValuesFromDiff[i];
                    var targetNode = change.SourceNode;
                    if (!StillExists(ref KernelNodes, targetNode))
                        continue;

                    KernelNodes[targetNode.Versioned.Index].ApplyObservation(change);
                    AddCandidateIfNotAlreadyThere(Map[targetNode].GroupID);
                }

                Marker.End();
            }

            void AddCandidateIfNotAlreadyThere(int index)
            {
                // Could also always add, sort, then remove duplicates at the end
                // (or use a set)
                if (!ResultIslands.Contains(index))
                    ResultIslands.Add(index);
            }
        }

        /// <summary>
        /// This resets the transient "run state" of all the nodes
        /// in the <see cref="IslandsToProcess"/>.
        ///
        /// Can be used to mark all nodes to run with <see cref="ORFlags"/>
        /// equal to <see cref="KernelNode.Flags.Enabled"/>, or use
        /// <see cref="ANDFlags"/> together with <see cref="KernelNode.Flags.ClearTransientMask"/>
        /// for disabling all nodes (and probably subsequently enabling some again in a culling pass).
        /// </summary>
        [BurstCompile]
        unsafe struct SweepGraphWithActiveState : IJobParallelForDefer
        {
            public Topology.TraversalCache Cache;
            public BlitList<KernelNode> Nodes;
            public ProfilerMarker Marker;
            [ReadOnly] public NativeList<int> IslandsToProcess;
            public KernelNode.Flags ORFlags;
            public KernelNode.Flags ANDFlags;

            public void Execute(int index)
            {
                Marker.Begin();

                var groupIndex = IslandsToProcess[index];

                foreach (var nodeCache in new Topology.GroupWalker(Cache.Groups[groupIndex]))
                {
                    var current = Nodes[nodeCache.Vertex.Versioned.Index].RunState;
                    Nodes[nodeCache.Vertex.Versioned.Index].RunState = (current & ANDFlags) | ORFlags;
                }

                Marker.End();
            }
        }

        [BurstCompile]
        unsafe struct BackpropagateObservabilityPass : IJobParallelForDefer
        {
            public Topology.TraversalCache Cache;
            public BlitList<KernelNode> Nodes;
            public ProfilerMarker IterativeMarker;
            [ReadOnly] public NativeList<int> IslandsToProcess;

            BlitList<Frame> m_Stack;
            uint m_AlternateFilter;

            public void Execute(int newIsland)
            {
                var groupIndex = IslandsToProcess[newIsland];
                var group = Cache.Groups[groupIndex];

                var worstCaseDepth = math.min(group.TraversalCount, group.ParentCount + group.ChildCount);
                m_AlternateFilter = Cache.GetMask(Topology.TraversalCache.Hierarchy.Alternate);

                m_Stack = new BlitList<Frame>(0, Allocator.Temp);
                m_Stack.Reserve((int)math.sqrt(worstCaseDepth));

                foreach (var nodeCache in new Topology.RootCacheWalker(group))
                {
                    IterativeMarker.Begin();
                    SweepAndMarkIteratively(nodeCache, group, KernelNode.Flags.Visited);
                    IterativeMarker.End();
                }

                m_Stack.Dispose();
            }

            /// <returns>true if traversal should be continued.</returns>
            bool VisitNode(ValidatedHandle handle, ref KernelNode.Flags pathFlags)
            {
                //Debug.Log($"Visiting {handle.Versioned} with {pathFlags}");
                ref var nodeKernel = ref Nodes[handle.Versioned.Index];

                // DFS ensures if we hit a node that already runs, we don't have to resweep any parents
                if ((nodeKernel.RunState & KernelNode.Flags.Enabled) != 0)
                    return false;

                var currentShouldRun = (nodeKernel.RunState & KernelNode.Flags.Observable) != 0;
                var alreadyVisited = (nodeKernel.RunState & KernelNode.Flags.Visited) != 0;

                pathFlags |= currentShouldRun ? KernelNode.Flags.Enabled : default;

                // Don't keep traversing this branch if we already went here,
                // and we're not painting anyway.
                if (alreadyVisited && (pathFlags & KernelNode.Flags.Enabled) == 0)
                    return false;

                // TODO: Could maybe not run any nodes that don't have any inputs.
                // eg. component nodes.
                nodeKernel.RunState |= pathFlags;

                return true;
            }

            struct Frame
            {
                public int TraversalSlot;
                public int CurrentParentIndex;
                public KernelNode.Flags Flags;
                public bool HasBeenVisited;

                public static Frame Visit(int traversalSlot, KernelNode.Flags flags)
                {
                    Frame f;
                    f.TraversalSlot = traversalSlot;
                    f.Flags = flags;
                    f.CurrentParentIndex = 0;
                    f.HasBeenVisited = false;

                    return f;
                }

                public bool MoveNextParent(ref Topology.TraversalCache.Group g, uint filter)
                {
                    ref var slot = ref g.IndexTraversal(TraversalSlot);

                    for(; CurrentParentIndex < slot.ParentCount;)
                    {
                        ref readonly var connection = ref g.IndexParent(slot.ParentTableIndex + CurrentParentIndex);

                        CurrentParentIndex++;

                        if ((connection.TraversalFlags & filter) == 0)
                            continue;

                        return true;
                    }

                    return false;
                }

                public ValidatedHandle Translate(ref Topology.TraversalCache.Group g)
                    => g.IndexTraversal(TraversalSlot).Vertex;

                public Frame CreateFrameForVisitingCurrentParent(ref Topology.TraversalCache.Group g)
                {
                    ref var slot = ref g.IndexTraversal(TraversalSlot);
                    ref readonly var parent = ref g.IndexParent(slot.ParentTableIndex + CurrentParentIndex - 1 /* because we already "moved" */);

                    return Visit(parent.TraversalIndex, Flags);
                }
            }

            void SweepAndMarkIteratively(Topology.VertexCache nodeCache, Topology.TraversalCache.Group g, KernelNode.Flags pathFlags)
            {
                m_Stack.Add(Frame.Visit(nodeCache.CacheIndex, pathFlags)); 

                while(m_Stack.Count != 0)
                {
                    ref var frame = ref m_Stack[m_Stack.Count - 1];

                    if(!frame.HasBeenVisited)
                    {
                        if (!VisitNode(frame.Translate(ref g), ref frame.Flags))
                        {
                            // Don't continue down this path
                            m_Stack.PopBack();
                            continue;
                        }

                        frame.HasBeenVisited = true;
                    }

                    if (frame.MoveNextParent(ref g, m_AlternateFilter))
                    {
                        m_Stack.Add(frame.CreateFrameForVisitingCurrentParent(ref g));
                    }
                    else
                    {
                        // Cannot traverse any further
                        m_Stack.PopBack();
                        continue;
                    }
                }

            }
        }
    }

}
