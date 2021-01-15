using System;

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
            where TDSLHandler : class, IDSLHandler, new()
                => (TDSLHandler)set.GetDSLHandler(DSLTypeMap.RegisterDSL<TDSLHandler>());
    }

    public static partial class CommonContextAPI
    {
        [Obsolete("Renamed to UpdateKernelBuffers (RemovedAfter 2021-01-19)")]
        public static void SetKernelBufferSize<TContext, TGraphKernel>(ref this TContext self, in TGraphKernel requestedSize)
            where TContext : struct, Detail.IContext<TContext>
            where TGraphKernel : struct, IGraphKernel
                => self.Set.UpdateKernelBuffers(self.InternalHandle(), requestedSize);
    }

    [Obsolete("Derive from SimulationNodeDefinition<TSimulationPortDefinition> instead. Move any implementations of Init(), Destroy(), OnUpdate(), and HandleMessage() into an INodeData struct declared as a nested type within the class declaration (the struct is automatically discovered) to fill out the IInit, IDestroy, IUpdate, and IMsgHandler interfaces as appropriate. (RemovedAfter 2020-10-27)", true)]
    public abstract class NodeDefinition<TSimulationPortDefinition> { }

    [Obsolete("Derive from SimulationNodeDefinition<TSimulationPortDefinition> instead and ensure that your INodeData is declared as a nested type within the class declaration so that it is automatically discovered. Also move any implementations of Init(), Destroy(), OnUpdate(), and HandleMessage() into that INodeData struct to fill out the IInit, IDestroy, IUpdate, and IMsgHandler interfaces as appropriate. (RemovedAfter 2020-10-27)", true)]
    public abstract class NodeDefinition<TNodeData, TSimulationPortDefinition> { }

    [Obsolete("Derive from KernelNodeDefinition<TKernelPortDefinition> instead and ensure that your IKernelData and IGraphKernel are declared as nested types within the class declaration so that they are automatically discovered. Also move any implementations of Init(), Destroy(), OnUpdate(), and HandleMessage() into an INodeData struct declared as a nested type within the class declaration (also automatically discovered) to fill out the IInit, IDestroy, IUpdate, and IMsgHandler interfaces as appropriate. (RemovedAfter 2020-10-27)", true)]
    public abstract class NodeDefinition<TKernelData, TKernelPortDefinition, TKernel> { }

    [Obsolete("Derive from SimulationKernelNodeDefinition<TSimulationPortDefinition,TKernelPortDefinition> instead and ensure that your INodeData, IKernelData, and IGraphKernel are declared as nested types within the class declaration so that they are automatically discovered. Also move any implementations of Init(), Destroy(), OnUpdate(), and HandleMessage() into that INodeData struct to fill out the IInit, IDestroy, IUpdate, and IMsgHandler interfaces as appropriate. (RemovedAfter 2020-10-27)", true)]
    public abstract class NodeDefinition<TNodeData, TSimulationPortDefinition, TKernelData, TKernelPortDefinition, TKernel> { }

    [Obsolete("Derive from KernelNodeDefinition<TKernelPortDefinition> instead and ensure that your INodeData, IKernelData, and IGraphKernel are declared as nested types within the class declaration so that they are automatically discovered. Also move any implementations of Init(), Destroy(), OnUpdate(), and HandleMessage() into that INodeData struct to fill out the IInit, IDestroy, IUpdate, and IMsgHandler interfaces as appropriate. (RemovedAfter 2020-10-27)", true)]
    public abstract class NodeDefinition<TNodeData, TKernelData, TKernelPortDefinition, TKernel> { }

    public partial struct RenderContext
    {
        [Obsolete("Renamed ResolvedInputPortArray (RemovedAfter 2021-03-14)")]
        public readonly struct ResolvedPortArray<TDefinition, TType>
            where TType : struct
            where TDefinition : NodeDefinition
        {
            readonly ResolvedInputPortArray<TDefinition, TType> m_Resolved;

            public ResolvedPortArray(ResolvedInputPortArray<TDefinition, TType> v) => m_Resolved = v;
            public static implicit operator ResolvedPortArray<TDefinition, TType>(ResolvedInputPortArray<TDefinition, TType> v)
                => new ResolvedPortArray<TDefinition, TType>(v);

            public ushort Length => m_Resolved.Length;
            public TType this[int i] => m_Resolved[i];
        }
    }
}
