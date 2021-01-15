using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace Unity.DataFlowGraph
{
    using Topology = TopologyAPI<ValidatedHandle, InputPortArrayID, OutputPortArrayID>;


    /// <summary>
    /// This job should run on every frame, re-patching any structural changes in ECS back into DFG.
    /// It is a way of caching ECS pointers to component data, essentially.
    /// </summary>
    /// <remarks>
    /// This is not an optimal way of doing this. This job will run every frame, recaching even
    /// if not necessary. Instead, chunks should be collected and walked, checking for versions.
    /// </remarks>
    [BurstCompile]
    unsafe struct RepatchDFGInputsIfNeededJob : IJobChunk
    {
        public BlitList<RenderGraph.KernelNode> KernelNodes;
        [NativeDisableUnsafePtrRestriction]
        public EntityComponentStore* EntityStore;
        public RenderGraph.SharedData Shared;
        public int NodeSetID;

        [ReadOnly] public BufferTypeHandle<NodeSetAttachment> NodeSetAttachmentType;
        [ReadOnly] public EntityTypeHandle EntityType;

        public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
        {
            NativeArray<Entity> entities = chunk.GetNativeArray(EntityType);
            BufferAccessor<NodeSetAttachment> buffers = chunk.GetBufferAccessor(NodeSetAttachmentType);

            for (int c = 0; c < chunk.Count; c++)
            {
                //An individual dynamic buffer for a specific entity
                DynamicBuffer<NodeSetAttachment> attachmentBuffer = buffers[c];
                for (int b = 0; b < attachmentBuffer.Length; ++b)
                {
                    var attachment = attachmentBuffer[b];
                    if (NodeSetID == attachment.NodeSetID)
                    {
                        PatchDFGInputsFor(entities[c], attachment.Node);
                        break;
                    }
                }
            }
        }

        void PatchDFGInputsFor(Entity e, ValidatedHandle node)
        {
            ref var graphKernel = ref InternalComponentNode.GetGraphKernel(KernelNodes[node.Versioned.Index].Instance.Kernel);

            for (int i = 0; i < graphKernel.Outputs.Count; ++i)
            {
                ref readonly var output = ref graphKernel.Outputs[i];
                void* source = Shared.BlankPage;

                // This also checks if the entity still exists (this should also be included in the query for this job)
                if (EntityStore->HasComponent(e, output.ComponentType))
                {
                    // TODO: Fix for aggregates
                    if (TypeManager.IsBuffer(output.ComponentType))
                    {
                        var buffer = (BufferHeader*)EntityStore->GetComponentDataWithTypeRO(e, output.ComponentType);

#if DFG_ASSERTIONS
                        if(output.JITPortIndex == InternalComponentNode.OutputFromECS.InvalidDynamicPort)
                            throw new AssertionException("DFG input connected to non scalar aggregate for which no jit port exists");
#endif

                        var jitPort = graphKernel.JITPorts.Ref(output.JITPortIndex);
                        *jitPort = new BufferDescription(BufferHeader.GetElementPointer(buffer), buffer->Length, default);
                        source = jitPort;
                    }
                    else
                    {
                        source = EntityStore->GetComponentDataWithTypeRO(e, output.ComponentType);
                    }
                }

                *output.DFGPatch = new DataInputStorage(source);
            }
        }
    }

    /// <summary>
    /// This job runs before any port patching, to start with a clean state.
    /// </summary>
    [BurstCompile]
    unsafe struct ClearLocalECSInputsAndOutputsJob : IJobChunk
    {
        public BlitList<RenderGraph.KernelNode> KernelNodes;
        [NativeDisableUnsafePtrRestriction]
        public EntityComponentStore* EntityStore;
        public int NodeSetID;
        [ReadOnly] public BufferTypeHandle<NodeSetAttachment> NodeSetAttachmentType;
        [ReadOnly] public EntityTypeHandle EntityType;
        public FlatTopologyMap Map;
        [ReadOnly] public NativeList<bool> Filter;

        public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
        {
            BufferAccessor<NodeSetAttachment> buffers = chunk.GetBufferAccessor(NodeSetAttachmentType);

            for (int c = 0; c < chunk.Count; c++)
            {
                DynamicBuffer<NodeSetAttachment> attachmentBuffer = buffers[c];
                for (int b = 0; b < attachmentBuffer.Length; ++b)
                {
                    var attachment = attachmentBuffer[b];

                    if (NodeSetID != attachment.NodeSetID)
                        continue;

                    var group = Map[attachment.Node].GroupID;

                    if (!Filter[group])
                        continue;

                    ref var graphBuffers = ref InternalComponentNode.GetGraphKernel(KernelNodes[attachment.Node.Versioned.Index].Instance.Kernel);

                    graphBuffers.Clear();
                    break;
                }
            }
        }
    }

    partial class InternalComponentNode
    {
        internal unsafe static void RecordInputConnection(
            InputPortArrayID inputPort,
            OutputPortArrayID outputPort,
            ValidatedHandle handle,
            in KernelLayout.Pointers instance,
            BlitList<RenderGraph.KernelNode> nodes)
        {
            ref var inputs = ref GetGraphKernel(instance.Kernel).Inputs;

            var connectionType = inputPort.PortID.ECSType.TypeIndex;

            ref readonly var parentKernel = ref nodes[handle.Versioned.Index];
            ref readonly var parentTraits = ref parentKernel.TraitsHandle.Resolve();

            InputToECS input;
            if (!parentTraits.KernelStorage.IsComponentNode)
            {
                // DFG -> Entity
                ref readonly var port = ref parentTraits
                    .DataPorts
                    .FindOutputDataPort(outputPort.PortID);

                input = new InputToECS(port.Resolve(parentKernel.Instance.Ports, outputPort.ArrayIndex), connectionType, port.ElementOrType.Size);
            }
            else  // Handle entity -> entity connections..
            {
                ref readonly var kdata = ref GetEntityData(parentKernel.Instance.Data);

                // This is where we usually use ElementOrType. Turns out this can be
                // inferred from ECS type manager... Might revert the old PR.
                input = new InputToECS(kdata.Entity, connectionType, TypeManager.GetTypeInfo(connectionType).ElementSize);
            }

            // Check whether this connection type already exists.
            // This would also benefit from a sorted set
            for(int i = 0; i < inputs.Count; ++i)
            {
                if (inputs[i].ECSTypeIndex == connectionType)
                {
                    // Overwrite an already recorded input connection given a potentially "moved" output.
                    // Normally, the slate is wiped clean through ClearLocalECSInputsAndOutputsJob and ComputeValueChunkAndPatchPorts,
                    // thus never hitting this location. However certain operations like resizing output port arrays will repatch
                    // downstream children without resetting these arrays, to avoid a full repatch of the island.
                    inputs[i] = input;
                    return;
                }
            }
            inputs.Add(input);
        }

        internal unsafe static void RecordOutputConnection(DataInputStorage* patch, RenderKernelFunction.BaseKernel* baseKernel, OutputPortID port)
        {
            ref var kernel = ref GetGraphKernel(baseKernel);

            if (TypeManager.IsBuffer(port.ECSType.TypeIndex))
            {
                // For buffers / aggregates we need to allocate an intermediate "port"
                // so it's transparent whether it points to ECS or not
                kernel.Outputs.Add(new OutputFromECS(patch, port.ECSType.TypeIndex, kernel.AllocateJITPort()));
            }
            else
            {
                kernel.Outputs.Add(new OutputFromECS(patch, port.ECSType.TypeIndex));
            }
        }
    }
}

