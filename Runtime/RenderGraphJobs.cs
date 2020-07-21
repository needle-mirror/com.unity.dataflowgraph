using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Profiling;

namespace Unity.DataFlowGraph
{
    using Topology = TopologyAPI<ValidatedHandle, InputPortArrayID, OutputPortArrayID>;
    using BufferResizeCommands = BlitList<RenderGraph.BufferResizeStruct>;
    using InputPortUpdateCommands = BlitList<RenderGraph.InputPortUpdateStruct>;
    using UntypedPortArray = PortArray<DataInput<InvalidDefinitionSlot, byte>>;

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
                        var index = nodeCache.Vertex.VHandle.Index;
                        ref var node = ref Nodes[index];
                        ref var traits = ref node.TraitsHandle.Resolve();
                        var ctx = new RenderContext(nodeCache.Vertex, Shared.SafetyManager);
                        traits.VTable.KernelFunction.Invoke(ctx, node.Instance);
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

                IslandFences.Add(job.Schedule(Cache.Groups.Length, 1, ExternalDependencies));
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
                for (int i = 0; i < Cache.Groups.Length; ++i)
                {
                    foreach (var nodeCache in new Topology.GroupWalker(Cache.Groups[i]))
                    {
                        var index = nodeCache.Vertex.VHandle.Index;
                        ref var node = ref Nodes[index];
                        ref var traits = ref node.TraitsHandle.Resolve();

                        DependencyCombiner.Clear();

                        var parents = nodeCache.GetParents();

                        foreach (var parentCache in parents)
                        {
                            var parentIndex = parentCache.Vertex.VHandle.Index;
                            ref var parent = ref Nodes[parentIndex];
                            DependencyCombiner.Add(parent.Fence);
                        }

                        JobHandle inputDependencies;

                        if (DependencyCombiner.Length > 0)
                            inputDependencies = JobHandle.CombineDependencies(DependencyCombiner);
                        else
                            inputDependencies = ExternalDependencies;

                        node.Fence = traits.VTable.KernelFunction.Schedule(
                            inputDependencies,
                            new RenderContext(nodeCache.Vertex, Shared.SafetyManager),
                            node.Instance
                        );
                    }
                }
            }
        }

        [BurstCompile]
        unsafe struct ResolveDataOutputsToGraphValuesJob : IJobParallelFor
        {
            public VersionedList<DataOutputValue> Values;
            public BlitList<KernelNode> Nodes;

            public void Execute(int index)
            {
                // TODO: This actually only needs to run once, per value...
                ref var value = ref Values[index];

                if (!value.Valid || !StillExists(ref Nodes, value.Source.Handle))
                    return;

                ref readonly var node = ref Nodes[value.Source.Handle.VHandle.Index];

                value.FutureMemory = node
                    .TraitsHandle
                    .Resolve()
                    .DataPorts
                    .FindOutputDataPort(value.Source.Port.PortID)
                    .Resolve(node.Instance.Ports);
            }
        }

        [BurstCompile]
        struct CopyValueDependenciesJob : IJob
        {
            public VersionedList<DataOutputValue> Values;
            public BlitList<KernelNode> Nodes;
            public NativeList<JobHandle> IslandFences;
            public NodeSet.RenderExecutionModel Model;

            public ProfilerMarker Marker;

            public void Execute()
            {
                Marker.Begin();

                switch (Model)
                {
                    case NodeSet.RenderExecutionModel.MaximallyParallel:
                        for (var i = 0; i < Values.UncheckedCount; i++)
                        {
                            ref var value = ref Values[i];

                            if (!value.Valid || !StillExists(ref Nodes, value.Source.Handle))
                                continue;

                            value.Dependency = Nodes[value.Source.Handle.VHandle.Index].Fence;
                        }
                        break;

                    case NodeSet.RenderExecutionModel.Islands:
                    case NodeSet.RenderExecutionModel.SingleThreaded:

                        if (IslandFences.Length == 0)
                            break;

                        for (var i = 0; i < Values.UncheckedCount; i++)
                        {
                            ref var value = ref Values[i];

                            if (!value.Valid || !StillExists(ref Nodes, value.Source.Handle))
                                continue;

                            // Note: Only one fence exist right now, because we schedule
                            // either as a parallel for or a single job, both of which
                            // return only one fence.
                            // Need to update this if we change how Islands scheduling work.
                            value.Dependency = IslandFences[0];
                        }
                        break;

                    case NodeSet.RenderExecutionModel.Synchronous:
                        break;

                    default:
                        throw new ArgumentOutOfRangeException();
                }

                Marker.End();
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
                    var index = nodeCache.Vertex.VHandle.Index;
                    ref var nodeKernel = ref Nodes[index];
                    ref var traits = ref nodeKernel.TraitsHandle.Resolve();

                    if (traits.Storage.IsComponentNode)
                    {
                        InternalComponentNode.RecordInputConnections(
                            nodeCache.GetInputsForPatching(),
                            nodeKernel.Instance,
                            Nodes
                        );

                        continue;
                    }

                    for (int i = 0; i < traits.DataPorts.Inputs.Count; ++i)
                    {
                        ref var portDecl = ref traits.DataPorts.Inputs[i];

                        if (!portDecl.IsArray)
                        {
                            var inputEnumerator = nodeCache.GetInputForPatchingByPort(new InputPortArrayID(portDecl.PortNumber));
                            PatchPort(ref inputEnumerator, portDecl.GetPointerToPatch(nodeKernel.Instance.Ports));
                        }
                        else
                        {
                            ref var portArray = ref portDecl.AsPortArray(nodeKernel.Instance.Ports);

                            for (ushort j = 0; j < portArray.Size; ++j)
                            {
                                var inputEnumerator = nodeCache.GetInputForPatchingByPort(new InputPortArrayID(portDecl.PortNumber, j));
                                PatchPort(ref inputEnumerator, portArray.NthInputPortPointer(j));
                            }
                        }
                    }
                }

                Marker.End();
            }

            void PatchPort(ref Topology.InputConnectionCacheWalker inputEnumerator, void** inputPortPatch)
            {
                ref var ownership = ref DataInputUtility.GetMemoryOwnership(inputPortPatch);

                switch (inputEnumerator.Count)
                {
                    case 0:

                        // No inputs, have to create a default value (careful to preserve input allocations for
                        // unconnected inputs that have been messaged - not applicable to buffer inputs)
                        if (ownership == DataInputUtility.Ownership.None)
                            *inputPortPatch = Shared.BlankPage;

                        break;
                    case 1:

                        // An input, link up with the output value
                        inputEnumerator.MoveNext();
                        // Handle previously unconnected inputs that have been messaged and are now being
                        // connected for the first time (not applicable to buffer inputs)
                        if (ownership == DataInputUtility.Ownership.OwnedByPort)
                        {
                            UnsafeUtility.Free(*inputPortPatch, PortAllocator);
                        }

                        // Clears any batch | port ownership (ports are just freed, batches freed elsewhere)
                        ownership = DataInputUtility.Ownership.None;

                        PatchOrDeferInput(inputPortPatch, inputEnumerator.Current.Target.Vertex, inputEnumerator.Current.OutputPort.PortID);

                        break;
                    default:
                        throw new InvalidOperationException("Cannot have multiple data inputs to the same port");
                }
            }

            void PatchOrDeferInput(void** patch, ValidatedHandle node, OutputPortID port)
            {
                var parentIndex = node.VHandle.Index;
                ref var parentKernel = ref Nodes[parentIndex];
                ref var parentTraits = ref parentKernel.TraitsHandle.Resolve();

                if (!parentTraits.Storage.IsComponentNode)
                {
                    // (Common case)
                    *patch = parentTraits
                        .DataPorts
                        .FindOutputDataPort(port)
                        .Resolve(parentKernel.Instance.Ports);

                    return;
                }

                // Record the requirement of input data coming from ECS. The actual data pointer will be patched in
                // in a follow-up job.
                InternalComponentNode.RecordOutputConnection(patch, parentKernel.Instance.Kernel, port);
            }
        }

        [BurstCompile]
        unsafe struct ResizeOutputDataPortBuffers : IJob
        {
            public BlitList<KernelNode> Nodes;
            public BufferResizeCommands OwnedCommands;

            public ProfilerMarker Marker;

            public void Execute()
            {
                Marker.Begin();

                for (int i = 0; i < OwnedCommands.Count; ++i)
                {
                    ref var command = ref OwnedCommands[i];
                    var handle = command.Handle;

                    if (!StillExists(ref Nodes, handle))
                        continue;

                    ref var buffer = ref GetBufferDescription(ref Nodes[handle.VHandle.Index], ref command);
                    var oldSize = buffer.Size;

                    // We will need to realloc if the new size is larger than the previous size.
                    if (oldSize >= command.Size)
                    {
                        // If the new size is relatively close to the old size re-use the existing allocation.
                        // (Note that skipping realloc down to command.Size/2 is stable since command.Size/2 will remain fixed
                        // regardless of how many succesive downsizes are done. Note that we will not however benefit from
                        // future upsizes which would fit in the original memory allocation.)
                        if (oldSize / 2 < command.Size)
                        {
                            buffer = new BufferDescription(buffer.Ptr, command.Size, buffer.OwnerNode);
                            continue;
                        }
                    }

                    var type = new SimpleType(command.ItemType.Size * command.Size, command.ItemType.Align);

                    // free the old one.
                    if (buffer.Ptr != null)
                    {
                        UnsafeUtility.Free(buffer.Ptr, PortAllocator);
                    }

                    buffer = new BufferDescription(
                        command.Size == 0 ? null : (byte*)Utility.CAlloc(type, PortAllocator),
                        command.Size,
                        buffer.OwnerNode
                    );
                }

                OwnedCommands.Dispose();

                Marker.End();
            }

            static ref BufferDescription GetBufferDescription(ref KernelNode nodeKernel, ref BufferResizeStruct command)
            {
                if (command.DataPortIndex == BufferResizeStruct.KernelBufferResizeHint)
                {
                    return ref nodeKernel.GetKernelBufferAt(command.LocalBufferOffset);
                }

                ref var traits = ref nodeKernel.TraitsHandle.Resolve();
                return ref traits
                    .DataPorts
                    .Outputs[command.DataPortIndex]
                    .GetAggregateBufferAt(nodeKernel.Instance.Ports, command.LocalBufferOffset);
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

                    ref var node = ref Nodes[command.Handle.VHandle.Index];
                    ref var traits = ref node.TraitsHandle.Resolve();

                    ref var portDecl = ref traits.DataPorts.Inputs[command.DataPortIndex];

                    switch (command.Operation)
                    {
                        case InputPortUpdateStruct.UpdateType.PortArrayResize:
                        {
                            UntypedPortArray.Resize(ref portDecl.AsPortArray(node.Instance.Ports), command.SizeOrArrayIndex, Shared.BlankPage, PortAllocator);
                            break;
                        }
                        case InputPortUpdateStruct.UpdateType.RetainData:
                        case InputPortUpdateStruct.UpdateType.SetData:
                        {
                            var inputPortPatch = portDecl.GetPointerToPatch(node.Instance.Ports, command.SizeOrArrayIndex);

                            ref var ownership = ref DataInputUtility.GetMemoryOwnership(inputPortPatch);
                            if (ownership == DataInputUtility.Ownership.OwnedByPort)
                                UnsafeUtility.Free(*inputPortPatch, PortAllocator);

                            if (command.Operation == InputPortUpdateStruct.UpdateType.RetainData)
                                *inputPortPatch = AllocateAndCopyData(*inputPortPatch, portDecl.Type);
                            else
                                *inputPortPatch = command.Data;

                            ownership = DataInputUtility.Ownership.OwnedByPort;
                            break;
                        }
                    }
                }

                OwnedCommands.Dispose();

                Marker.End();
            }
        }

        [BurstCompile]
        unsafe struct CopyDirtyRendererDataJob : IJobParallelFor
        {
            public BlitList<KernelNode> KernelNodes;
            public BlitList<InternalNodeData> SimulationNodes;

            public void Execute(int nodeIndex)
            {
                var data = KernelNodes[nodeIndex].Instance.Data;
                if (data != null) // Alive ?
                    UnsafeUtility.MemCpy(data, SimulationNodes[nodeIndex].KernelData, KernelNodes[nodeIndex].KernelDataSize);
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
                        var index = nodeCache.Vertex.VHandle.Index;
                        ref var node = ref Nodes[index];
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
        unsafe struct ParallelRenderer : IJobParallelFor
        {
            public BlitList<KernelNode> Nodes;
            public Topology.TraversalCache Cache;
            public SharedData Shared;

            public void Execute(int islandIndex)
            {
                foreach (var nodeCache in new Topology.GroupWalker(Cache.Groups[islandIndex]))
                {
                    var index = nodeCache.Vertex.VHandle.Index;
                    ref var node = ref Nodes[index];
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

        [BurstCompile]
        internal unsafe struct AnalyseLiveNodes : IJob
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
    }

}
