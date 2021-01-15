using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using System.ComponentModel;

namespace Unity.DataFlowGraph
{
    // Assume all dynamically allocated just-in-time ports are single buffers for now.
    using JITPort = BufferDescription;

    namespace Detail
    {
        [EditorBrowsable(EditorBrowsableState.Never)]
        public struct IsComponent<T>
            where T : IComponentData
        {

        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public struct IsBuffer<T>
            where T : IBufferElementData
        {

        }
    }

    /// <summary>
    /// A <see cref="ComponentNode"/> gives access to component data from a specific <see cref="Entity"/> in the rendering graph.
    /// <see cref="IComponentData"/> and <see cref="IBufferElementData"/> components are modeled as
    /// <see cref="DataInput{TDefinition, TType}"/> or <see cref="DataOutput{TDefinition, TType}"/>,
    /// and <see cref="DataInput{TDefinition, Buffer{TType}}"/> or <see cref="DataOutput{TDefinition, Buffer{TType}}"/> respectively.
    ///
    /// Using <see cref="Input{ComponentType}"/> and <see cref="Output{ComponentType}"/> you can create ports
    /// that can be connected to normal nodes, and the data will be readable and writable as usual in the rendering graph.
    /// <seealso cref="NodeSetAPI.Connect(NodeHandle, OutputPortID, NodeHandle, InputPortID, NodeSetAPI.ConnectionType)"/>.
    ///
    /// The data will be committed after the returned <see cref="JobHandle"/> from <see cref="NodeSet.Update(Jobs.JobHandle)"/>
    /// is completed.
    ///
    /// <see cref="ComponentNode"/>s behave as normal nodes and thus any normal API on the <see cref="NodeSetAPI"/> is accessible to
    /// them, with the exception that a <see cref="ComponentNode"/> does not have any ports predefined.
    ///
    /// <seealso cref="NodeDefinition.GetPortDescription(NodeHandle)"/>
    ///
    /// <see cref="ComponentNode"/>s have to be created through <see cref="NodeSetAPI.CreateComponentNode(Entity)"/>, and cannot
    /// be instantiated through <see cref="NodeSetAPI.Create{TDefinition}"/>.
    ///
    /// A <see cref="ComponentNode"/> doesn't do anything itself and cannot be extended - it merely offers a topological
    /// dynamic read/write interface through ports to ECS.
    /// </summary>
    /// <remarks>
    /// If the target <see cref="Entity"/> is destroyed, or if any referenced component data is missing, any I/O to this
    /// particular node or port is defaulted (but still continues to work). It's the user's responsibility to destroy the
    /// <see cref="ComponentNode"/> through <see cref="NodeSetAPI.Destroy(NodeHandle)"/> (like any normal node), regardless of
    /// whether the target <see cref="Entity"/> exists or not.
    ///
    /// This API is only available if the hosting <see cref="NodeSet"/> was created with a companion <see cref="ComponentSystemBase"/>.
    /// <seealso cref="NodeSet(ComponentSystemBase)"/>.
    ///
    /// When a <see cref="DataOutput{TDefinition, Buffer{TType}}"/> is linked to a <see cref="ComponentNode"/>'s
    /// <see cref="IBufferElementData"/>, it is the user's responsibility to ensure that sizes match between DFG and ECS.
    ///
    /// <see cref="PortArray{TInputPort}"/> have no equivalent on <see cref="ComponentNode"/>s.
    ///
    /// In order to implement in place read-modify-write systems of ECS data, connections with
    /// <see cref="NodeSetAPI.ConnectionType.Feedback"/> need to be used in the following fashion.
    ///
    /// <see cref="ComponentNode"/>s appearing downstream in a graph and connected back to parent node(s) via
    /// <see cref="NodeSetAPI.ConnectionType.Feedback"/> connections can be understood to feed their "previous" component data
    /// to those parent nodes, and, update their component data at the end of any given <see cref="NodeSet.Update(Jobs.JobHandle)"/>.
    /// The "previous" component data will be the most current value from the ECS point of view, so this does not represent
    /// a frame delay.
    ///
    /// Therefore, if the desire is to model a graph that will read information from an <see cref="Entity"/>'s component data,
    /// modify it, and then write it back to ECS, <see cref="ComponentNode"/>s should appear downstream in the graph and have
    /// <see cref="NodeSetAPI.ConnectionType.Feedback"/> connections to the upstream nodes.
    /// </remarks>
    public abstract class ComponentNode : NodeDefinition
    {
        /// <summary>
        /// Create an <see cref="InputPortID"/> matching a particular <see cref="ComponentType"/>.
        /// An input to a <see cref="ComponentNode"/> represents a write back to the respective component
        /// on the <see cref="Entity"/>.
        /// If the component doesn't exist, the write back doesn't do anything.
        /// <remarks>
        /// This is the untyped version of <see cref="Input{TType}"/>.
        /// Untyped port ids from <see cref="ComponentNode"/>s have overhead in usage together with
        /// <see cref="NodeSetAPI.Connect(NodeHandle, OutputPortID, NodeHandle, InputPortID, NodeSetAPI.ConnectionType)"/>.
        /// To implement in-place read-modify-write of ECS data, see documentation of <see cref="ComponentNode"/>.
        /// </remarks>
        /// </summary>
        /// <exception cref="ArgumentException">
        /// Thrown if the <paramref name="type"/> doesn't have write access.
        /// </exception>
        public static InputPortID Input(ComponentType type)
        {
            if (type.AccessModeType != ComponentType.AccessMode.ReadWrite)
            {
                throw new ArgumentException(
                    $"Access mode {type.AccessModeType} was unexpected; " +
                    $"inputs are read-write and use {ComponentType.AccessMode.ReadWrite}"
                );
            }

            return new InputPortID(new PortStorage(type));
        }

        /// <summary>
        /// Create an <see cref="OutputPortID"/> matching a particular <see cref="ComponentType"/>.
        /// An output from a <see cref="ComponentNode"/> represents a read from the respective component
        /// on the <see cref="Entity"/>.
        /// Anyone connected to a non-existing component on this <see cref="Entity"/> will read default
        /// values.
        /// <remarks>
        /// This is the untyped version of <see cref="Output{TType}"/>.
        /// Untyped port ids from <see cref="ComponentNode"/>s have overhead in usage together with
        /// <see cref="NodeSetAPI.Connect(NodeHandle, OutputPortID, NodeHandle, InputPortID, NodeSetAPI.ConnectionType)"/>.
        /// To implement in-place read-modify-write of ECS data, see documentation of <see cref="ComponentNode"/>.
        /// </remarks>
        /// </summary>
        /// <exception cref="ArgumentException">
        /// Thrown if the <paramref name="type"/> doesn't have read access.
        /// </exception>
        public static OutputPortID Output(ComponentType type)
        {
            if (type.AccessModeType != ComponentType.AccessMode.ReadOnly)
            {
                throw new ArgumentException(
                    $"Access mode {type.AccessModeType} was unexpected; " +
                    $"outputs are read-only and use {ComponentType.AccessMode.ReadOnly}"
                );
            }

            return new OutputPortID(new PortStorage(type));
        }

        /// <summary>
        /// This is a strongly typed version of <see cref="Output(ComponentType)"/>,
        /// with otherwise identical semantics.
        /// </summary>
        public static DataOutput<ComponentNode, TType> Output<TType>(Detail.IsComponent<TType> _ = default)
            where TType : struct, IComponentData
        {
            var output = new DataOutput<ComponentNode, TType>();
            output.Port = Output(ComponentType.ReadOnly<TType>());

            return output;
        }

        /// <summary>
        /// This is a strongly typed version of <see cref="Input(ComponentType)"/>,
        /// with otherwise identical semantics.
        /// </summary>
        public static DataInput<ComponentNode, TType> Input<TType>(Detail.IsComponent<TType> _ = default)
            where TType : struct, IComponentData
        {
            unsafe
            {
                return new DataInput<ComponentNode, TType>(Input(ComponentType.ReadWrite<TType>()));
            }
        }

        /// <summary>
        /// This is a strongly typed version of <see cref="Output(ComponentType)"/>,
        /// specifically for components implementing <see cref="IBufferElementData"/>.
        /// The data (coming from a <see cref="DynamicBuffer{T}"/>) can be connected to a
        /// <see cref="Buffer{T}"/> on a <see cref="NodeDefinition"/>'s <see cref="IKernelPortDefinition"/>.
        ///
        /// <seealso cref="Output{TType}"/>
        /// </summary>
        public static DataOutput<ComponentNode, Buffer<TType>> Output<TType>(Detail.IsBuffer<TType> _ = default)
            where TType : struct, IBufferElementData
        {
            var output = new DataOutput<ComponentNode, Buffer<TType>>();
            output.Port = Output(ComponentType.ReadOnly<TType>());

            return output;
        }

        /// <summary>
        /// This is a strongly typed version of <see cref="Input(ComponentType)"/>,
        /// specifically for components implementing <see cref="IBufferElementData"/>.
        /// The data (coming from a <see cref="Buffer{T}"/>) can be connected to a
        /// <see cref="DynamicBuffer{T}"/> on a <see cref="ComponentNode"/>.
        ///
        /// <seealso cref="Input{TType}"/>
        /// </summary>
        public static DataInput<ComponentNode, Buffer<TType>> Input<TType>(Detail.IsBuffer<TType> _ = default)
            where TType : struct, IBufferElementData
        {
            unsafe
            {
                return new DataInput<ComponentNode, Buffer<TType>>(Input(ComponentType.ReadWrite<TType>()));
            }
        }
    }

    /// <summary>
    /// Non-abstract version removes ability for user to instantiate a <see cref="ComponentNode"></see>.
    /// </summary>
    partial class InternalComponentNode : ComponentNode
    {
        /// <summary>
        /// Represents a blit back to ECS from a DFG source, that has to happen
        /// every time the <see cref="ComponentNode"/> has been "executed"
        /// </summary>
        [StructLayout(LayoutKind.Explicit)]
        internal readonly unsafe struct InputToECS
        {
            internal const int InternalFlag = PortStorage.IsECSPortFlag;

            /// <summary>
            /// Create an input into an entity from a random location
            /// </summary>
            public InputToECS(void* sourceMemory, int targetComponentType, int elementSize)
            {
                m_SourceEntity = default;
                m_ReadOnlyInputMemory = sourceMemory;
                m_ComponentType = targetComponentType & ~InternalFlag;
                SizeOf = elementSize;
            }

            /// <summary>
            /// Create an input into an entity from an entity
            /// </summary>
            public InputToECS(Entity sourceEntity, int sharedComponentType, int elementSize)
            {
                m_ReadOnlyInputMemory = default;
                m_SourceEntity = sourceEntity;
                m_ComponentType = sharedComponentType | InternalFlag;
                SizeOf = (int)elementSize;
            }

            public int ECSTypeIndex => m_ComponentType & ~InternalFlag;
            internal bool IsECSSource => (m_ComponentType & InternalFlag) != 0;
            internal Entity GetAsEntity_ForTesting() => m_SourceEntity;

            public void* Resolve(EntityComponentStore* store)
            {
                if(!IsECSSource)
                {
                    return m_ReadOnlyInputMemory;
                }
                else
                {
                    if (!store->HasComponent(m_SourceEntity, ECSTypeIndex))
                        return null;

                    return store->GetComponentDataWithTypeRO(m_SourceEntity, ECSTypeIndex);
                }
            }

            [FieldOffset(0)]
            readonly void* m_ReadOnlyInputMemory;
            [FieldOffset(0)]
            readonly Entity m_SourceEntity;

            [FieldOffset(8)]
            public readonly int m_ComponentType;
            [FieldOffset(12)]
            public readonly int SizeOf;

            // TODO: Can maybe live without - Need to figure out if it is valid for multiple frames
            // public int TypeLookupCache;
        }

        /// <summary>
        /// Represents a volatile connection from ECS -> DFG.
        /// In this case, normal input port patching done in DFG <see cref="RenderGraph.ComputeValueChunkAndPatchPortsJob"/>
        /// is "deferred", and instead done when necessary inside <see cref="RepatchDFGInputsIfNeededJob"/> (TBD).
        /// </summary>
        internal readonly unsafe struct OutputFromECS
        {
            public const int InvalidDynamicPort = -1;

            public OutputFromECS(DataInputStorage* dfgPatch, int type)
            {
#if DFG_ASSERTIONS
                if (TypeManager.IsBuffer(type))
                    throw new AssertionException("Creating a scalar output from something that requires an aggregate");
#endif
                DFGPatch = dfgPatch;
                ComponentType = type;
                JITPortIndex = InvalidDynamicPort;
            }

            /// <summary>
            /// Constructor to use for an indirect port (buffer / aggregate).
            /// </summary>
            /// <param name="dfgPatch"></param>
            /// <param name="type"></param>
            public OutputFromECS(DataInputStorage* dfgPatch, int type, int dynamicDFGPort)
            {
#if DFG_ASSERTIONS
                if (!TypeManager.IsBuffer(type))
                    throw new AssertionException("Creating a dynamic output from something that isn't an buffer");
#endif
                DFGPatch = dfgPatch;
                ComponentType = type;
                JITPortIndex = dynamicDFGPort;
            }

            public readonly DataInputStorage* DFGPatch;
            public readonly int ComponentType;
            public readonly int JITPortIndex;
        }


        internal unsafe struct KernelData : IKernelData
        {
            public Entity Entity;
            public EntityComponentStore* EntityStore;
        }

        internal struct KernelDefs : IKernelPortDefinition { }

        [BurstCompile]
        internal unsafe struct GraphKernel : IGraphKernel<KernelData, KernelDefs>, IDisposable
        {
            public BlitList<InputToECS> Inputs;
            public BlitList<OutputFromECS> Outputs;
            public BlitList<JITPort> JITPorts;

            public void Create()
            {
                Inputs = new BlitList<InputToECS>(0, Allocator.Persistent);
                Outputs = new BlitList<OutputFromECS>(0, Allocator.Persistent);
                JITPorts = new BlitList<JITPort>(0, Allocator.Persistent);
            }

            public void Dispose()
            {
                Inputs.Dispose();
                Outputs.Dispose();
                JITPorts.Dispose();
            }

            public int AllocateJITPort()
            {
                JITPorts.Add(default);
                return JITPorts.Count - 1;
            }

            public void Execute(RenderContext ctx, in KernelData data, ref KernelDefs kernelPorts)
            {
                for(int i = 0; i < Inputs.Count; ++i)
                {
                    ref readonly var ecsInput = ref Inputs[i];

                    if (!data.EntityStore->HasComponent(data.Entity, ecsInput.ECSTypeIndex))
                        continue;

                    void* source = ecsInput.Resolve(data.EntityStore);

                    if (source == null)
                    {
                        // The source can only be null if the connection refers to a dead entity / destroyed component
#if DFG_ASSERTIONS
                        if (!ecsInput.IsECSSource)
                            throw new AssertionException("Null data source from DFG");
#endif
                        continue;
                    }

                    if (TypeManager.IsBuffer(ecsInput.ECSTypeIndex))
                    {
                        var ecsBuffer = (BufferHeader*)data.EntityStore->GetComponentDataWithTypeRW(
                            data.Entity,
                            ecsInput.ECSTypeIndex,
                            data.EntityStore->GlobalSystemVersion/*,
                            ref ports[i].TypeLookupCache */
                        );

                        int sourceSize = 0;

                        if(!ecsInput.IsECSSource)
                        {
                            // DFG Buffer -> ECS buffer
                            var dfgBuffer = (BufferDescription*)source;
                            sourceSize = dfgBuffer->Size;
                            source = dfgBuffer->Ptr;
                        }
                        else
                        {
                            // ECS Buffer -> ECS buffer
                            var ecsSource = (BufferHeader*)source;
                            sourceSize = ecsSource->Length;
                            source = BufferHeader.GetElementPointer(ecsSource);
                        }

                        var sharedSize = math.min(ecsBuffer->Length, sourceSize);

                        UnsafeUtility.MemCpy(
                            BufferHeader.GetElementPointer(ecsBuffer),
                            source,
                            sharedSize * ecsInput.SizeOf
                        );
                    }
                    else
                    {
                        // Generic ECS/DFG data -> ECS
                        var ecsMemory = data.EntityStore->GetComponentDataWithTypeRW(
                            data.Entity,
                            ecsInput.ECSTypeIndex,
                            data.EntityStore->GlobalSystemVersion /*,
                            ref ports[i].TypeLookupCache */
                        );

                        UnsafeUtility.MemCpy(ecsMemory, source, ecsInput.SizeOf);
                    }
                }
            }

            internal void Clear()
            {
                Inputs.Clear();
                Outputs.Clear();
                JITPorts.Clear();
            }
        }

        static PortDescription ZeroPorts => new PortDescription
        {
            Inputs = new List<PortDescription.InputPort>(),
            Outputs = new List<PortDescription.OutputPort>()
        };

        // TODO: Use API on KernelLayout
        internal static unsafe ref KernelData GetEntityData(RenderKernelFunction.BaseData* data)
            => ref UnsafeUtility.AsRef<KernelData>(data);

        internal static unsafe ref GraphKernel GetGraphKernel(RenderKernelFunction.BaseKernel* kernel)
            => ref UnsafeUtility.AsRef<GraphKernel>(kernel);

        internal override PortDescription.InputPort GetVirtualInput(ValidatedHandle h, InputPortArrayID port)
        {
            var ecsType = port.PortID.Storage.ReadOnlyComponentType;
            Type managedType = ecsType.GetManagedType();

            if(ecsType.IsBuffer)
                managedType = typeof(Buffer<>).MakeGenericType(managedType);

            return PortDescription.InputPort.Data(managedType, port.PortID, hasBuffers: false, isPortArray: false, isPublic: true, name: null /* #52 */);
        }

        internal override PortDescription.OutputPort GetVirtualOutput(ValidatedHandle h, OutputPortArrayID port)
        {
            var ecsType = port.PortID.Storage.ReadWriteComponentType;
            Type managedType = ecsType.GetManagedType();

            if (ecsType.IsBuffer)
                managedType = typeof(Buffer<>).MakeGenericType(managedType);

            return PortDescription.OutputPort.Data(managedType, port.PortID, null, isPortArray: false, isPublic: true, name: null /* #52 */);
        }

        internal override PortDescription.InputPort GetFormalInput(ValidatedHandle h, InputPortArrayID port)
            => throw new NotImplementedException(
                $"The requested API is not supported; {nameof(ComponentNode)}s do not have formal data flow graph ports");

        internal override PortDescription.OutputPort GetFormalOutput(ValidatedHandle h, OutputPortArrayID port)
            => throw new NotImplementedException(
                $"The requested API is not supported; {nameof(ComponentNode)}s do not have formal data flow graph ports");

        internal override PortDescription GetPortDescriptionInternal(ValidatedHandle handle)
        {
            return ZeroPorts;
        }

        internal struct NodeData : INodeData, IDestroy
        {
            public unsafe void Destroy(DestroyContext ctx)
            {
                var contents = ctx.Set.GetKernelData<KernelData>(ctx.Handle);

#if DFG_ASSERTIONS
                if (contents.Entity == default || contents.EntityStore == default)
                    throw new AssertionException($"{nameof(ComponentNode)} was not properly initialized");
#endif
                var em = ctx.Set.HostSystem.World.EntityManager;

                // TODO: This fences due to how GetBuffer works.
                // Can potentially be redone with a hash map.
                var attachments = em.GetBuffer<NodeSetAttachment>(contents.Entity);

                for (int i = 0; i < attachments.Length; ++i)
                {
                    if (attachments[i].NodeSetID == ctx.Set.NodeSetID)
                    {
                        attachments.RemoveAt(i);

                        if (attachments.Length == 0)
                            em.RemoveComponent(contents.Entity, ComponentType.ReadWrite<NodeSetAttachment>());

                        return;
                    }
                }

                // TODO: Test
                throw new InvalidOperationException("Internal inconsistency error");
            }
        }
    }

    /// <summary>
    /// Entities can be attached to many <see cref="NodeSet"/>s.
    /// </summary>
    [InternalBufferCapacity(4)] // Don't do heap allocations unless we have > 4 attached nodesets
    struct NodeSetAttachment : ISystemStateBufferElementData
    {
        public ValidatedHandle Node;
        public int NodeSetID => Node.Versioned.ContainerID;
    }

    public partial class NodeSetAPI
    {
        /// <summary>
        /// Instantiate an <see cref="ComponentNode"/>.
        /// </summary>
        /// <remarks>
        /// A structural change will happen the first time you create a <see cref="ComponentNode"/> from this particular
        /// <see cref="ComponentNode"/>.
        ///
        /// This API is only available if the hosting <see cref="NodeSet"/> was created with a companion
        /// <see cref="ComponentSystemBase"/>.
        /// <seealso cref="NodeSet(ComponentSystemBase)"/>.
        /// <seealso cref="Create{TDefinition}"/>
        /// </remarks>
        /// <exception cref="ArgumentException">
        /// Thrown if the <paramref name="entity"/> is invalid.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown if a <see cref="ComponentNode"/> already exists for this <paramref name="entity"/> in this
        /// <see cref="NodeSet"/>.
        /// </exception>
        /// <exception cref="NullReferenceException">
        /// Thrown if this <see cref="NodeSet"/> was not created together with ECS.
        /// <see cref="NodeSet(ComponentSystemBase)"/>.
        /// </exception>
        public NodeHandle<ComponentNode> CreateComponentNode(Entity entity)
        {
            var world = HostSystem.World;

            if (!world.EntityManager.Exists(entity))
                throw new ArgumentException("Entity doesn't exist");

            unsafe
            {
                var attachments = world.EntityManager.AddBuffer<NodeSetAttachment>(entity);

                for (int i = 0; i < attachments.Length; ++i)
                {
                    if (attachments[i].NodeSetID == NodeSetID)
                        throw new InvalidOperationException(
                            $"A {nameof(ComponentNode)} already exists for the particular entity {entity} in this node set"
                        );
                }

                var node = CreateInternal<InternalComponentNode>();

                var attachment = new NodeSetAttachment { Node = node };
                attachments.Add(attachment);

                ref var kernelData = ref GetSimulationSide_KernelData(node);
                kernelData.Entity = entity;
                kernelData.EntityStore = world.EntityManager.GetCheckedEntityDataAccess()->EntityComponentStore;

                return new NodeHandle<ComponentNode>(node.Versioned);
            }

        }

        /// <summary>
        /// Returns the simulation side version of the kernel data for a <see cref="ComponentNode"/>.
        /// This is the only version that can be mutated, as the kernel version is being replaced every frame.
        /// </summary>
        internal unsafe ref InternalComponentNode.KernelData GetSimulationSide_KernelData(ValidatedHandle node)
            => ref UnsafeUtility.AsRef<InternalComponentNode.KernelData>(Nodes[node].KernelData);
    }
}
