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
            ref var graphKernel = ref InternalComponentNode.GetGraphKernel(KernelNodes[node.VHandle.Index].Instance.Kernel);

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

                *output.DFGPatch = source;
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

                    ref var graphBuffers = ref InternalComponentNode.GetGraphKernel(KernelNodes[attachment.Node.VHandle.Index].Instance.Kernel);

                    graphBuffers.Clear();
                    break;
                }
            }
        }
    }

    partial class InternalComponentNode
    {
        internal unsafe static void RecordInputConnections(
            Topology.InputConnectionCacheWalker incomingConnections, 
            in KernelLayout.Pointers instance,
            BlitList<RenderGraph.KernelNode> nodes)
        {
            ref var inputs = ref GetGraphKernel(instance.Kernel).Inputs;

            foreach (var c in incomingConnections)
            {
                var connectionType = c.InputPort.PortID.ECSType.TypeIndex;

                // Test whether this connection type already exists
                // (equivalent to multiple inputs to one port in DFG model)
                // This could be under ENABLE_UNITY_COLLECTIONS_CHECKS
                // This would also benefit from a sorted set
                for(int i = 0; i < inputs.Count; ++i)
                {
                    if(inputs[i].ECSTypeIndex == connectionType)
                        throw new InvalidOperationException("Cannot have multiple data inputs to the same port");
                }

                ref readonly var parentKernel = ref nodes[c.Target.Vertex.VHandle.Index];
                ref readonly var parentTraits = ref parentKernel.TraitsHandle.Resolve();

                if (!parentTraits.Storage.IsComponentNode)
                {
                    // DFG -> Entity
                    ref var port = ref parentKernel
                        .TraitsHandle
                        .Resolve()
                        .DataPorts
                        .FindOutputDataPort(c.OutputPort.PortID);
                    
                    inputs.Add(new InputToECS(port.Resolve(parentKernel.Instance.Ports), connectionType, port.ElementOrType.Size));
                }
                else  // Handle entity -> entity connections..
                {
                    ref readonly var kdata = ref GetEntityData(parentKernel.Instance.Data);

                    // This is where we usually use ElementOrType. Turns out this can be 
                    // inferred from ECS type manager... Might revert the old PR.
                    inputs.Add(new InputToECS(kdata.Entity, connectionType, TypeManager.GetTypeInfo(connectionType).ElementSize));
                }
            }
        }

        internal unsafe static void RecordOutputConnection(void** patch, RenderKernelFunction.BaseKernel* baseKernel, OutputPortID port)
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

