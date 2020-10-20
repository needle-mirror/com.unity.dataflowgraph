using Unity.Collections.LowLevel.Unsafe;

namespace Unity.DataFlowGraph
{
    unsafe struct MessageDelivery
    {
        public MessageContext Context;
        public void* Node;
        public void* Message;

        public delegate void Prototype(in MessageDelivery delivery);
    }

    unsafe struct InitDelivery
    {
        public InitContext Context;
        public void* Node;

        public delegate void Prototype(in InitDelivery delivery);
    }

    unsafe struct UpdateDelivery
    {
        public UpdateContext Context;
        public void* Node;

        public delegate void Prototype(in UpdateDelivery delivery);
    }

    unsafe struct DestroyDelivery
    {
        public DestroyContext Context;
        public void* Node;

        public delegate void Prototype(in DestroyDelivery delivery);
    }

    unsafe static class Trampolines<TNodeDefinition>
        where TNodeDefinition : NodeDefinition
    {
        public static void HandleMessage<TMessage, TNodeData>(in MessageDelivery delivery)
            where TNodeData : struct, INodeData, IMsgHandler<TMessage>
            where TMessage : struct
        {
            UnsafeUtility.AsRef<TNodeData>(delivery.Node).HandleMessage(delivery.Context, UnsafeUtility.AsRef<TMessage>(delivery.Message));
        }

        public static void HandleMessageGeneric<TMessage, TNodeData>(in MessageDelivery delivery)
            where TNodeData : struct, INodeData, IMsgHandlerGeneric<TMessage>
            where TMessage : struct
        {
            UnsafeUtility.AsRef<TNodeData>(delivery.Node).HandleMessage(delivery.Context, UnsafeUtility.AsRef<TMessage>(delivery.Message));
        }

        public static void HandleInit<TNodeData>(in InitDelivery delivery)
            where TNodeData : struct, INodeData, IInit
        {
            UnsafeUtility.AsRef<TNodeData>(delivery.Node).Init(delivery.Context);
        }

        public static void HandleUpdate<TNodeData>(in UpdateDelivery delivery)
            where TNodeData : struct, INodeData, IUpdate
        {
            UnsafeUtility.AsRef<TNodeData>(delivery.Node).Update(delivery.Context);
        }

        public static void HandleDestroy<TNodeData>(in DestroyDelivery delivery)
            where TNodeData : struct, INodeData, IDestroy
        {
            UnsafeUtility.AsRef<TNodeData>(delivery.Node).Destroy(delivery.Context);
        }
    }
}
