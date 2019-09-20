using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;

namespace Unity.DataFlowGraph
{
    using BufferResizeCommands = BlitList<RenderGraph.BufferResizeStruct>;
    using InputPortUpdateCommands = BlitList<RenderGraph.InputPortUpdateStruct>;
    using UntypedPortArray = PortArray<DataInput<InvalidFunctionalitySlot, byte>>;

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

                SafetyManager = (AtomicSafetyManager*)UnsafeUtility.Malloc(sizeof(AtomicSafetyManager), UnsafeUtility.AlignOf<AtomicSafetyManager>(), Allocator.Persistent);
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
            public TraversalCache Cache;
            public BlitList<KernelNode> Nodes;
            public RenderExecutionModel RenderingMode;
            public NativeList<JobHandle> DependencyCombiner;
            public NativeList<JobHandle> IslandFences;
            public SharedData Shared;
            public JobHandle ExternalDependencies;

            public void Execute()
            {
                IslandFences.Clear();

                switch (RenderingMode)
                {
                    case RenderExecutionModel.SingleThreaded:
                        ScheduleSingle();
                        break;

                    case RenderExecutionModel.Synchronous:
                        ExecuteInPlace();
                        break;

                    case RenderExecutionModel.Islands:
                        ScheduleIslands();
                        break;

                    case RenderExecutionModel.MaximallyParallel:
                        ScheduleJobified();
                        break;

                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            void ExecuteInPlace()
            {
                ExternalDependencies.Complete();

                foreach (var nodeCache in new TopologyCacheWalker(Cache))
                {
                    var index = nodeCache.Handle.VHandle.Index;
                    ref var node = ref Nodes[index];
                    ref var traits = ref node.TraitsHandle.Resolve();
                    var ctx = new RenderContext(nodeCache.Handle, Shared.SafetyManager);
                    traits.VTable.KernelFunction.Invoke(ctx, node.Kernel, node.KernelData, node.KernelPorts);
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

                IslandFences.Add(job.Schedule(Cache.Islands.Length, 1, ExternalDependencies));
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
                foreach (var nodeCache in new TopologyCacheWalker(Cache))
                {
                    var index = nodeCache.Handle.VHandle.Index;
                    ref var node = ref Nodes[index];
                    ref var traits = ref node.TraitsHandle.Resolve();

                    DependencyCombiner.Clear();

                    var parents = nodeCache.GetParents();

                    foreach (var parentCache in parents)
                    {
                        var parentIndex = parentCache.Handle.VHandle.Index;
                        ref var parent = ref Nodes[parentIndex];
                        DependencyCombiner.Add(parent.Fence);
                    }

                    JobHandle inputDependencies;

                    if (DependencyCombiner.Length > 0)
                        inputDependencies = JobHandleUnsafeUtility.CombineDependencies((JobHandle*)DependencyCombiner.GetUnsafePtr(), DependencyCombiner.Length);
                    else
                        inputDependencies = ExternalDependencies;

                    node.Fence = traits.VTable.KernelFunction.Schedule(
                        inputDependencies,
                        new RenderContext(nodeCache.Handle, Shared.SafetyManager),
                        node.Kernel,
                        node.KernelData,
                        node.KernelPorts
                    );
                }
            }
        }

        [BurstCompile]
        unsafe struct AssignInputBatchJob : IJob
        {
            [NativeDisableUnsafePtrRestriction]
            public InputBatch.InstalledPorts* BatchInstall;
            public BlitList<KernelNode> Nodes;
            public SharedData Shared;

            [ReadOnly]
            public NativeArray<TransientInputBuffer> Transients;

            public void Execute()
            {
                BatchInstall->AllocatePorts(Transients);

                for (int i = 0; i < Transients.Length; ++i)
                {
                    var input = Transients[i];

                    // Hard to say whether this is an error.
                    if (!Exists(ref Nodes, input.Target))
                        continue;

                    // Patch install memory
                    ref var install = ref BatchInstall->GetPort(i);

                    install.m_Value =
                        new Buffer<byte>(input.Memory, input.Size, install.m_Value.OwnerNode);

                    ref var node = ref Nodes[input.Target.VHandle.Index];
                    ref var traits = ref node.TraitsHandle.Resolve();

                    for (var d = 0; d < traits.DataPorts.Inputs.Count; d++)
                    {
                        ref var port = ref traits.DataPorts.Inputs[d];

                        if (port.PortNumber != input.Port.PortID)
                            continue;

                        var patch = (void**)((byte*)node.KernelPorts + port.PatchOffset);
                        if (port.IsArray)
                        {
                            ref var array =
                                ref Unsafe.AsRef<PortArray<DataInput<InvalidFunctionalitySlot, byte>>>(patch);
                            patch =
                                (void**)((byte*)array.Ptr + input.Port.ArrayIndex * Unsafe.SizeOf<DataInput<InvalidFunctionalitySlot, byte>>());
                        }
                        ref var ownership = ref DataInputUtility.GetMemoryOwnership(patch);

                        if (ownership != DataInputUtility.Ownership.None)
                        {
                            Report_DoubleAssignError();
                            return;
                        }

                        if (*patch != Shared.BlankPage)
                        {
                            Report_AssignmentToConnectedPort();
                            return;
                        }

                        *patch = BatchInstall->GetPortMemory(i);

                        ownership = DataInputUtility.Ownership.OwnedByBatch;

                        break;
                    }

                }
            }

            // Burst doesn't support exceptions in players
#if !UNITY_EDITOR
            [BurstDiscard]
#endif
            void Report_DoubleAssignError()
            {
                throw new InvalidOperationException("Cannot assign a buffer to a port that has already been assigned");
            }

            // Burst doesn't support exceptions in players
#if !UNITY_EDITOR
            [BurstDiscard]
#endif
            void Report_AssignmentToConnectedPort()
            {
                throw new InvalidOperationException("Cannot assign a buffer to an input port on a node that is already connected to something else");
            }
        }

        [BurstCompile]
        unsafe struct RemoveInputBatchJob : IJob
        {
            public BlitList<KernelNode> Nodes;
            public SharedData Shared;

            [ReadOnly]
            public NativeArray<TransientInputBuffer> Transients;

            public void Execute()
            {
                for (int i = 0; i < Transients.Length; ++i)
                {
                    var input = Transients[i];

                    // Hard to say whether this is an error.
                    if (!Exists(ref Nodes, input.Target))
                        continue;

                    ref var node = ref Nodes[input.Target.VHandle.Index];
                    ref var traits = ref node.TraitsHandle.Resolve();

                    for (var d = 0; d < traits.DataPorts.Inputs.Count; d++)
                    {
                        ref var port = ref traits.DataPorts.Inputs[d];

                        if (port.PortNumber == input.Port.PortID)
                        {
                            var patch = (void**)((byte*)node.KernelPorts + port.PatchOffset);
                            if (port.IsArray)
                            {
                                ref var array =
                                    ref Unsafe.AsRef<PortArray<DataInput<InvalidFunctionalitySlot, byte>>>(patch);
                                patch =
                                    (void**)((byte*)array.Ptr + input.Port.ArrayIndex * Unsafe.SizeOf<DataInput<InvalidFunctionalitySlot, byte>>());
                            }
                            ref var flags = ref DataInputUtility.GetMemoryOwnership(patch);

                            // Any intruding connection or SetData will have reset the flags
                            // and the port memory, so we shouldn't reset the port to blank.
                            if (flags == DataInputUtility.Ownership.OwnedByBatch)
                            {
                                *patch = Shared.BlankPage;
                                flags = DataInputUtility.Ownership.None;
                                break;
                            }
                        }
                    }

                }
            }
        }

        [BurstCompile]
        unsafe struct FlushGraphValuesJob : IJobParallelFor
        {
            public BlitList<DataOutputValue> Values;
            public BlitList<KernelNode> Nodes;

            public void Execute(int index)
            {
                ref var value = ref Values[index];

                if (!value.IsAlive || !Exists(ref Nodes, value.Node))
                    return;

                ref var node = ref Nodes[value.Node.VHandle.Index];
                ref var traits = ref node.TraitsHandle.Resolve();

                // TODO: Right now with only the strongly typed API, it shouldn't 
                // be possible to NOT find the matching port. In any case,
                // any mismatch results in a null request.
                value.FutureMemory = null;

                for (var d = 0; d < traits.DataPorts.Outputs.Count; d++)
                {
                    ref var port = ref traits.DataPorts.Outputs[d];
                    if (port.PortNumber == value.Port)
                    {
                        value.FutureMemory = (byte*)node.KernelPorts + port.PatchOffset;
                        break;
                    }
                }
            }
        }

        [BurstCompile]
        struct CopyValueDependenciesJob : IJob
        {
            public BlitList<DataOutputValue> Values;
            public BlitList<KernelNode> Nodes;
            public NativeList<JobHandle> IslandFences;
            public RenderExecutionModel Model;

            public void Execute()
            {
                switch (Model)
                {
                    case RenderExecutionModel.MaximallyParallel:
                        for (var i = 0; i < Values.Count; i++)
                        {
                            ref var value = ref Values[i];

                            if (!value.IsAlive || !Exists(ref Nodes, value.Node))
                                continue;

                            value.Dependency = Nodes[value.Node.VHandle.Index].Fence;
                        }
                        break;

                    case RenderExecutionModel.Islands:
                    case RenderExecutionModel.SingleThreaded:

                        if (IslandFences.Length == 0)
                            break;

                        for (var i = 0; i < Values.Count; i++)
                        {
                            ref var value = ref Values[i];

                            if (!value.IsAlive || !Exists(ref Nodes, value.Node))
                                continue;

                            // Note: Only one fence exist right now, because we schedule
                            // either as a parallel for or a single job, both of which
                            // return only one fence.
                            // Need to update this if we change how Islands scheduling work.
                            value.Dependency = IslandFences[0];
                        }
                        break;

                    case RenderExecutionModel.Synchronous:
                        break;

                    default:
                        throw new ArgumentOutOfRangeException();
                }

            }
        }

        // TODO: With some considerations, we can change this into a IParallelForJob
        [BurstCompile]
        struct ComputeValueChunkAndPatchPortsJob : IJob
        {
            public TraversalCache Cache;
            public BlitList<KernelNode> Nodes;
            public SharedData Shared;

            public unsafe void Execute()
            {
                // It would make more sense to walk by node type, and batch all nodes for these types.
                // Requires sorting or ECS/whatever firstly, though.

                foreach (var nodeCache in new TopologyCacheWalker(Cache))
                {
                    var index = nodeCache.Handle.VHandle.Index;
                    ref var nodeKernel = ref Nodes[index];
                    ref var traits = ref nodeKernel.TraitsHandle.Resolve();

                    for (int i = 0; i < traits.DataPorts.Inputs.Count; ++i)
                    {
                        ref var portDecl = ref traits.DataPorts.Inputs[i];

                        void** inputPortPatch = (void**)((byte*)nodeKernel.KernelPorts + portDecl.PatchOffset);

                        if (!portDecl.IsArray)
                        {
                            var parentEnumerator = nodeCache.GetParentConnectionsByPort(portDecl.PortNumber);
                            PatchPort(ref parentEnumerator, inputPortPatch);
                        }
                        else
                        {
                            ref var portArray = ref Unsafe.AsRef<PortArray<DataInput<InvalidFunctionalitySlot, byte>>>(inputPortPatch);
                            inputPortPatch = (void**)portArray.Ptr;
                            for (ushort j = 0; j < portArray.Size; ++j, inputPortPatch = (void**)((byte*)inputPortPatch + Unsafe.SizeOf<DataInput<InvalidFunctionalitySlot, byte>>()))
                            {
                                var parentEnumerator = nodeCache.GetParentConnectionsByPort(portDecl.PortNumber, j);
                                PatchPort(ref parentEnumerator, inputPortPatch);
                            }
                        }
                    }
                }
            }

            unsafe void PatchPort(ref InputConnectionCacheWalker parentEnumerator, void** inputPortPatch)
            {
                ref var ownership = ref DataInputUtility.GetMemoryOwnership(inputPortPatch);

                switch (parentEnumerator.Count)
                {
                    case 0:
                        // No parents, have to create a default value (careful to preserve input allocations for
                        // unconnected inputs that have been messaged - not applicable to buffer inputs)
                        if (ownership == DataInputUtility.Ownership.None)
                            *inputPortPatch = Shared.BlankPage;

                        break;
                    case 1:
                        // A parent, link up with the output value

                        parentEnumerator.MoveNext();
                        var parentIndex = parentEnumerator.Current.TargetNode.Handle.VHandle.Index;
                        ref var parentKernel = ref Nodes[parentIndex];

                        ref var parentTraits = ref parentKernel.TraitsHandle.Resolve();

                        // Handle previously unconnected inputs that have been messaged and are now being
                        // connected for the first time (not applicable to buffer inputs)
                        if (ownership == DataInputUtility.Ownership.OwnedByPort)
                        {
                            UnsafeUtility.Free(*inputPortPatch, PortAllocator);
                        }

                        // Clears any batch | port ownership (ports are just freed, batches freed elsewhere)
                        ownership = DataInputUtility.Ownership.None;

                        var parentOutputs = parentTraits.DataPorts.Outputs;
                        *inputPortPatch = null;

                        for (int p = 0; p < parentOutputs.Count; ++p)
                        {
                            if (parentOutputs[p].PortNumber == parentEnumerator.Current.OutputPort)
                            {
                                ref var parentPortDecl = ref parentTraits.DataPorts.Outputs[p];
                                *inputPortPatch = (byte*)parentKernel.KernelPorts + parentPortDecl.PatchOffset;
                                break;
                            }
                        }

                        // TODO: Error finding parent - should not happen!
                        if (*inputPortPatch == null)
                            *inputPortPatch = Shared.BlankPage;

                        break;
                    default:
                        throw new InvalidOperationException("Cannot have multiple data inputs to the same port");
                }
            }
        }

        [BurstCompile]
        unsafe struct ResizeOutputDataPortBuffers : IJob
        {
            public BlitList<KernelNode> Nodes;
            public BufferResizeCommands OwnedCommands;

            public void Execute()
            {
                for (int i = 0; i < OwnedCommands.Count; ++i)
                {
                    ref var command = ref OwnedCommands[i];
                    var handle = command.Handle;

                    if (!Exists(ref Nodes, handle))
                        continue;

                    ref var nodeKernel = ref Nodes[handle.VHandle.Index];
                    ref var traits = ref nodeKernel.TraitsHandle.Resolve();

                    ref var portDecl = ref traits.DataPorts.Outputs[command.DataPortIndex];
                    ref var buffer = ref Unsafe.AsRef<Buffer<byte>>((byte*)nodeKernel.KernelPorts + portDecl.PatchOffset + command.BufferOffset);

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
                            buffer = new Buffer<byte>(buffer.Ptr, command.Size, buffer.OwnerNode);
                            continue;
                        }
                    }

                    var type = new SimpleType(command.ItemType.Size * command.Size, command.ItemType.Align);

                    // free the old one.
                    if (buffer.Ptr != null)
                    {
                        UnsafeUtility.Free(buffer.Ptr, PortAllocator);
                    }

                    buffer = new Buffer<byte>(command.Size == 0 ? null : (byte*)Utility.CAlloc(type, PortAllocator), command.Size, buffer.OwnerNode);
                }

                OwnedCommands.Dispose();
            }
        }

        [BurstCompile]
        unsafe struct UpdateInputDataPort : IJob
        {
            public BlitList<KernelNode> Nodes;
            public InputPortUpdateCommands OwnedCommands;
            public SharedData Shared;

            public void Execute()
            {
                for (int i = 0; i < OwnedCommands.Count; ++i)
                {
                    ref var command = ref OwnedCommands[i];

                    ref var nodeKernel = ref Nodes[command.Handle.VHandle.Index];
                    ref var traits = ref nodeKernel.TraitsHandle.Resolve();

                    ref var portDecl = ref traits.DataPorts.Inputs[command.DataPortIndex];

                    void** inputPortPatch =
                        (void**)((byte*)nodeKernel.KernelPorts + portDecl.PatchOffset);

                    switch (command.Operation)
                    {
                        case InputPortUpdateStruct.UpdateType.PortArrayResize:
                            ref var portArray = ref Unsafe.AsRef<UntypedPortArray>(inputPortPatch);
                            UntypedPortArray.Resize(ref portArray, command.SizeOrArrayIndex, Shared.BlankPage, PortAllocator);
                            break;

                        case InputPortUpdateStruct.UpdateType.RetainData:
                        case InputPortUpdateStruct.UpdateType.SetData:
                            if (portDecl.IsArray)
                            {
                                ref var array =
                                    ref Unsafe.AsRef<PortArray<DataInput<InvalidFunctionalitySlot, byte>>>(inputPortPatch);
                                inputPortPatch =
                                    (void**)((byte*)array.Ptr + command.SizeOrArrayIndex * Unsafe.SizeOf<DataInput<InvalidFunctionalitySlot, byte>>());
                            }

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

                OwnedCommands.Dispose();
            }
        }

        [BurstCompile]
        unsafe struct CopyDirtyRendererDataJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<NodeHandle> AliveNodes;

            public BlitList<KernelNode> KernelNodes;
            public BlitList<InternalNodeData> SimulationNodes;

            public void Execute(int index)
            {
                var nodeIndex = AliveNodes[index].VHandle.Index;

                UnsafeUtility.MemCpy(KernelNodes[nodeIndex].KernelData, SimulationNodes[nodeIndex].KernelData, KernelNodes[nodeIndex].KernelDataSize);
            }
        }

        [BurstCompile]
        unsafe struct SingleThreadedRenderer : IJob
        {
            public BlitList<KernelNode> Nodes;
            public TraversalCache Cache;
            public SharedData Shared;

            public void Execute()
            {
                foreach (var nodeCache in new TopologyCacheWalker(Cache))
                {
                    var index = nodeCache.Handle.VHandle.Index;
                    ref var node = ref Nodes[index];
                    ref var traits = ref node.TraitsHandle.Resolve();
                    traits.VTable.KernelFunction.Invoke(new RenderContext(nodeCache.Handle, Shared.SafetyManager), node.Kernel, node.KernelData, node.KernelPorts);
                }
            }
        }

        [BurstCompile]
        unsafe struct ParallelRenderer : IJobParallelFor
        {
            public BlitList<KernelNode> Nodes;
            public TraversalCache Cache;
            public SharedData Shared;

            public void Execute(int islandIndex)
            {
                foreach (var nodeCache in new TopologyCacheWalker(Cache, Cache.Islands[islandIndex]))
                {
                    var index = nodeCache.Handle.VHandle.Index;
                    ref var node = ref Nodes[index];
                    ref var traits = ref node.TraitsHandle.Resolve();
                    traits.VTable.KernelFunction.Invoke(new RenderContext(nodeCache.Handle, Shared.SafetyManager), node.Kernel, node.KernelData, node.KernelPorts);
                }
            }
        }

        [BurstCompile]
        unsafe struct AnalyseLiveNodes : IJob
        {
            public NativeArray<NodeHandle> LiveNodes;
            public BlitList<KernelNode> KernelNodes;

            public void Execute()
            {
                for (int i = 0, n = 0; i < LiveNodes.Length; ++i)
                {
                    // TODO: It seems inevitable that we have a full scan of all nodes each frame
                    // because there's no 1:1 equivalence between kernel nodes and normal nodes..
                    // Ideally this wouldn't happen since it's another O(n) operation
                    for (; n < KernelNodes.Count; ++n)
                    {
                        if (KernelNodes[n].AliveInRenderer)
                        {
                            LiveNodes[i] = KernelNodes[n++].Handle;
                            break;
                        }
                    }
                }
            }
        }
    }

}
