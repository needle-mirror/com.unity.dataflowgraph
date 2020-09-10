using System;
using Unity.Collections;
using Unity.Entities;

namespace Unity.DataFlowGraph
{
    public static class NodeSetAPI_Deprecated_Ext
    {
        const string k_SendMessageDeprecationMessage =
            "If a node wants to send a message to a child node that it has created, it should use a private MessageOutput port, connect it to the child's MessageInput port and then EmitMessage() instead. (RemovedAfter 2020-10-27)";

        [Obsolete(k_SendMessageDeprecationMessage)]
        public static void SendMessage<TMsg>(this NodeSetAPI set, NodeHandle handle, InputPortID port, in TMsg msg)
            => set.SendMessage(handle, new InputPortArrayID(port), msg);

        [Obsolete(k_SendMessageDeprecationMessage)]
        public static void SendMessage<TMsg>(this NodeSetAPI set, NodeHandle handle, InputPortID portArray, int index, in TMsg msg)
            => set.SendMessage(handle, new InputPortArrayID(portArray, index), msg);

        [Obsolete(k_SendMessageDeprecationMessage)]
        public static void SendMessage<TMsg, TDefinition>(this NodeSetAPI set, NodeHandle<TDefinition> handle, MessageInput<TDefinition, TMsg> port, in TMsg msg)
            where TDefinition : NodeDefinition
            => set.SendMessage(handle, new InputPortArrayID((InputPortID)port), msg);

        [Obsolete(k_SendMessageDeprecationMessage)]
        public static void SendMessage<TMsg, TDefinition>(this NodeSetAPI set, NodeHandle<TDefinition> handle, PortArray<MessageInput<TDefinition, TMsg>> portArray, int index, in TMsg msg)
            where TDefinition : NodeDefinition
                => set.SendMessage(handle, new InputPortArrayID((InputPortID)portArray, index), msg);

        [Obsolete(k_SendMessageDeprecationMessage)]
        public static void SendMessage<TTask, TMsg, TDestination>(this NodeSetAPI set, NodeInterfaceLink<TTask, TDestination> handle, in TMsg msg)
            where TTask : ITaskPort<TTask>
            where TDestination : NodeDefinition, TTask, new()
                => set.SendMessage(handle, set.GetDefinition(handle.TypedHandle).GetPort(handle), msg);

        [Obsolete(k_SendMessageDeprecationMessage)]
        public static void SendMessage<TTask, TMsg>(this NodeSetAPI set, NodeInterfaceLink<TTask> handle, in TMsg msg)
            where TTask : ITaskPort<TTask>
        {
            var f = set.GetDefinition(handle);
            if (f is TTask task)
            {
                set.SendMessage(handle, task.GetPort(handle), msg);
            }
            else
            {
                throw new InvalidOperationException($"Cannot send message to destination. Destination not of type {typeof(TTask).Name}");
            }
        }

        private const string k_SetDataDeprecationMessage =
            "If a node wants to send data to a child node that it has created, it should use a private MessageOutput port, connect it to the child's DataInput port and then EmitMessage() instead. (RemovedAfter 2020-10-27)";

        [Obsolete(k_SetDataDeprecationMessage)]
        public static void SetData<TType>(this NodeSetAPI set, NodeHandle handle, InputPortID port, in TType data)
            where TType : struct
                => set.SetData(handle, new InputPortArrayID(port), data);

        [Obsolete(k_SetDataDeprecationMessage)]
        public static void SetData<TType>(this NodeSetAPI set, NodeHandle handle, InputPortID portArray, int index, in TType data)
            where TType : struct
                => set.SetData(handle, new InputPortArrayID(portArray, index), data);

        [Obsolete(k_SetDataDeprecationMessage)]
        public static void SetData<TType, TDefinition>(this NodeSetAPI set, NodeHandle<TDefinition> handle, DataInput<TDefinition, TType> port, in TType data)
            where TDefinition : NodeDefinition
            where TType : struct
                => set.SetData(handle, new InputPortArrayID(port.Port), data);

        [Obsolete(k_SetDataDeprecationMessage)]
        public static void SetData<TType, TDefinition>(this NodeSetAPI set, NodeHandle<TDefinition> handle, PortArray<DataInput<TDefinition, TType>> portArray, int index, in TType data)
            where TDefinition : NodeDefinition
            where TType : struct
                => set.SetData(handle, new InputPortArrayID(portArray.GetPortID(), index), data);

        private const string k_GetDefinitionDeprecationMessage =
            "GetDefinition() should not be used within InitContext, DestroyContext, UpdateContext, or MessageContext. You should not rely on NodeDefinition polymorphism in these contexts. (RemovedAfter 2020-10-27)";

        [Obsolete(k_GetDefinitionDeprecationMessage)]
        public static NodeDefinition GetDefinition(this NodeSetAPI set, NodeHandle handle)
            => set.GetDefinition(handle);

        [Obsolete(k_GetDefinitionDeprecationMessage)]
        public static TDefinition GetDefinition<TDefinition>(this NodeSetAPI set)
            where TDefinition : NodeDefinition, new()
                => set.GetDefinition<TDefinition>();

        [Obsolete(k_GetDefinitionDeprecationMessage)]
        public static TDefinition GetDefinition<TDefinition>(this NodeSetAPI set, NodeHandle<TDefinition> handle)
            where TDefinition : NodeDefinition, new()
                => set.GetDefinition(handle);

        [Obsolete("GetDSLHandler() should not be used within InitContext, DestroyContext, UpdateContext, or MessageContext. You should not rely on DSLHandler polymorphism in these contexts. (RemovedAfter 2020-10-27)")]
        public static TDSLHandler GetDSLHandler<TDSLHandler>(this NodeSetAPI set)
            where TDSLHandler : class, IDSLHandler
                => (TDSLHandler)set.GetDSLHandler(typeof(TDSLHandler));
    }

#if !ENTITIES_0_12_OR_NEWER
    static class ForwardCompatibility
    {
        static public ArchetypeChunkBufferType<T> GetBufferTypeHandle<T>(this ComponentSystemBase self, bool isReadOnly = false)
            where T : struct, IBufferElementData
                => self.GetArchetypeChunkBufferType<T>(isReadOnly);

        static public ArchetypeChunkEntityType GetEntityTypeHandle(this ComponentSystemBase self)
            => self.GetArchetypeChunkEntityType();

        static public ArchetypeChunkComponentType<T> GetComponentTypeHandle<T>(this ComponentSystemBase self, bool isReadOnly = false)
            where T : struct, IComponentData
                => self.GetArchetypeChunkComponentType<T>(isReadOnly);

        static public BufferAccessor<T> GetBufferAccessor<T>(this ArchetypeChunk self, BufferTypeHandle<T> bufferComponentType)
            where T : struct, IBufferElementData
                => self.GetBufferAccessor((ArchetypeChunkBufferType<T>)bufferComponentType);

        static public NativeArray<T> GetNativeArray<T>(this ArchetypeChunk self, ComponentTypeHandle<T> chunkComponentType)
            where T : struct, IComponentData
                => self.GetNativeArray((ArchetypeChunkComponentType<T>)chunkComponentType);
    }

    struct ComponentTypeHandle<T>
    {
        ArchetypeChunkComponentType<T> m_Value;

        public static implicit operator ArchetypeChunkComponentType<T>(ComponentTypeHandle<T> v)
            => v.m_Value;

        public static implicit operator ComponentTypeHandle<T>(ArchetypeChunkComponentType<T> v)
            => new ComponentTypeHandle<T> {m_Value = v};
    }

    struct BufferTypeHandle<T>
        where T : struct, IBufferElementData
    {
        ArchetypeChunkBufferType<T> m_Value;

        public static implicit operator ArchetypeChunkBufferType<T>(BufferTypeHandle<T> v)
            => v.m_Value;

        public static implicit operator BufferTypeHandle<T>(ArchetypeChunkBufferType<T> v)
            => new BufferTypeHandle<T> {m_Value = v};
    }

    struct EntityTypeHandle
    {
        ArchetypeChunkEntityType m_Value;

        public static implicit operator ArchetypeChunkEntityType(EntityTypeHandle v)
            => v.m_Value;

        public static implicit operator EntityTypeHandle(ArchetypeChunkEntityType v)
            => new EntityTypeHandle {m_Value = v};
    }
#endif
}
