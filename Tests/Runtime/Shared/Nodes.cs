using NUnit.Framework;
using Unity.Burst;
using Unity.Entities;

namespace Unity.DataFlowGraph.Tests
{
    public interface TestDSL { }

    public class DSL : DSLHandler<TestDSL>
    {
        protected override void Connect(ConnectionInfo left, ConnectionInfo right) { }
        protected override void Disconnect(ConnectionInfo left, ConnectionInfo right) { }
    }

    public struct ECSInt : IComponentData
    {
        public int Value;
        public static implicit operator int (ECSInt val) => val.Value;
        public static implicit operator ECSInt(int val) => new ECSInt { Value = val };
    }

    public class EmptyNode : SimulationNodeDefinition<EmptyNode.EmptyPorts>
    {
        public struct EmptyPorts : ISimulationPortDefinition { }
    }

    public class EmptyNodeAndData : SimulationNodeDefinition<EmptyNodeAndData.EmptyPorts>
    {
        public struct EmptyPorts : ISimulationPortDefinition { }

        public struct EmptyData : INodeData {}
    }

    public class EmptyNode2 : SimulationNodeDefinition<EmptyNode2.EmptyPorts>
    {
        public struct EmptyPorts : ISimulationPortDefinition { }
    }

    public class NodeWithAllTypesOfPorts
        : SimulationKernelNodeDefinition<NodeWithAllTypesOfPorts.SimPorts, NodeWithAllTypesOfPorts.KernelDefs>
        , TestDSL
    {
        public struct SimPorts : ISimulationPortDefinition
        {
            public MessageInput<NodeWithAllTypesOfPorts, int> MessageIn;
            public PortArray<MessageInput<NodeWithAllTypesOfPorts, int>> MessageArrayIn;
            public MessageOutput<NodeWithAllTypesOfPorts, int> MessageOut;
            public PortArray<MessageOutput<NodeWithAllTypesOfPorts, int>> MessageArrayOut;
            public DSLInput<NodeWithAllTypesOfPorts, DSL, TestDSL> DSLIn;
            public DSLOutput<NodeWithAllTypesOfPorts, DSL, TestDSL> DSLOut;
        }

        public struct KernelDefs : IKernelPortDefinition
        {
            public DataInput<NodeWithAllTypesOfPorts, Buffer<int>> InputBuffer;
            public PortArray<DataInput<NodeWithAllTypesOfPorts, Buffer<int>>> InputArrayBuffer;
            public DataOutput<NodeWithAllTypesOfPorts, Buffer<int>> OutputBuffer;
            public PortArray<DataOutput<NodeWithAllTypesOfPorts, Buffer<int>>> OutputArrayBuffer;
            public DataInput<NodeWithAllTypesOfPorts, int> InputScalar;
            public PortArray<DataInput<NodeWithAllTypesOfPorts, int>> InputArrayScalar;
            public DataOutput<NodeWithAllTypesOfPorts, int> OutputScalar;
            public PortArray<DataOutput<NodeWithAllTypesOfPorts, int>> OutputArrayScalar;
        }

        struct Node : INodeData, IMsgHandler<int>
        {
            public void HandleMessage(MessageContext ctx, in int msg) { }
        }

        struct EmptyKernelData : IKernelData { }

        [BurstCompile(CompileSynchronously = true)]
        struct Kernel : IGraphKernel<EmptyKernelData, KernelDefs>
        {
            public void Execute(RenderContext ctx, in EmptyKernelData data, ref KernelDefs ports) { }
        }
    }

    public class NodeWithParametricPortType<T>
        : SimulationKernelNodeDefinition<NodeWithParametricPortType<T>.SimPorts, NodeWithParametricPortType<T>.KernelDefs>
            where T : struct
    {
        public static int IL2CPP_ClassInitializer = 0;

#pragma warning disable 649 // non-public unassigned default value
        public struct SimPorts : ISimulationPortDefinition
        {
            public MessageInput<NodeWithParametricPortType<T>, T> MessageIn;
            public MessageOutput<NodeWithParametricPortType<T>, T> MessageOut;
        }

        public struct KernelDefs : IKernelPortDefinition
        {
            public DataInput<NodeWithParametricPortType<T>, T> Input;
            public DataOutput<NodeWithParametricPortType<T>, T> Output;
        }

        struct Node : INodeData, IMsgHandler<T>
        {
            public void HandleMessage(MessageContext ctx, in T msg) { }
        }

        struct EmptyKernelData : IKernelData { }

        // disabled due to AOT Burst seeing this kernel, but being unable to compile it (parametric node)
        // [BurstCompile(CompileSynchronously = true)]
        struct Kernel : IGraphKernel<EmptyKernelData, KernelDefs>
        {
            public void Execute(RenderContext ctx, in EmptyKernelData data, ref KernelDefs ports) { }
        }
    }

    public class KernelAdderNode : KernelNodeDefinition<KernelAdderNode.KernelDefs>
    {
        public struct KernelDefs : IKernelPortDefinition
        {
            public DataInput<KernelAdderNode, int> Input;
            public DataOutput<KernelAdderNode, int> Output;
        }

        struct EmptyKernelData : IKernelData { }

        [BurstCompile(CompileSynchronously = true)]
        struct Kernel : IGraphKernel<EmptyKernelData, KernelDefs>
        {
            public void Execute(RenderContext ctx, in EmptyKernelData data, ref KernelDefs ports)
            {
                ctx.Resolve(ref ports.Output) = ctx.Resolve(ports.Input) + 1;
            }
        }
    }

    public class KernelSumNode : KernelNodeDefinition<KernelSumNode.KernelDefs>
    {
        public struct KernelDefs : IKernelPortDefinition
        {
            public PortArray<DataInput<KernelSumNode, ECSInt>> Inputs;
            public DataOutput<KernelSumNode, ECSInt> Output;
        }

        struct EmptyKernelData : IKernelData { }

        [BurstCompile(CompileSynchronously = true)]
        struct Kernel : IGraphKernel<EmptyKernelData, KernelDefs>
        {
            public void Execute(RenderContext ctx, in EmptyKernelData data, ref KernelDefs ports)
            {
                ref var sum = ref ctx.Resolve(ref ports.Output);
                sum = 0;
                var inputs = ctx.Resolve(ports.Inputs);
                for (int i = 0; i < inputs.Length; ++i)
                    sum += inputs[i];
            }
        }
    }

    public class KernelArrayOutputNode : KernelNodeDefinition<KernelArrayOutputNode.KernelDefs>
    {
        public struct KernelDefs : IKernelPortDefinition
        {
            public PortArray<DataOutput<KernelArrayOutputNode, ECSInt>> Outputs;
        }

        struct EmptyKernelData : IKernelData { }

        [BurstCompile(CompileSynchronously = true)]
        struct Kernel : IGraphKernel<EmptyKernelData, KernelDefs>
        {
            public void Execute(RenderContext ctx, in EmptyKernelData data, ref KernelDefs ports)
            {
                var outputs = ctx.Resolve(ref ports.Outputs);
                for (int i = 0; i < outputs.Length; ++i)
                    outputs[i] = i + outputs.Length;
            }
        }
    }

    public class PassthroughTest<T>
        : SimulationKernelNodeDefinition<PassthroughTest<T>.SimPorts, PassthroughTest<T>.KernelDefs>
            where T : struct
    {
        public struct KernelDefs : IKernelPortDefinition
        {
            public DataInput<PassthroughTest<T>, T> Input;
            public DataOutput<PassthroughTest<T>, T> Output;
        }

        public struct SimPorts : ISimulationPortDefinition
        {
            public MessageInput<PassthroughTest<T>, T> Input;
            public MessageOutput<PassthroughTest<T>, T> Output;
        }

        public struct NodeData : INodeData, IMsgHandler<T>
        {
            public T LastReceivedMsg;

            public void HandleMessage(MessageContext ctx, in T msg)
            {
                Assert.That(ctx.Port == SimulationPorts.Input);
                LastReceivedMsg = msg;
                ctx.EmitMessage(SimulationPorts.Output, msg);
            }
        }

        struct EmptyKernelData : IKernelData { }

        struct Kernel : IGraphKernel<EmptyKernelData, KernelDefs>
        {
            public void Execute(RenderContext ctx, in EmptyKernelData data, ref KernelDefs ports)
            {
                ctx.Resolve(ref ports.Output) = ctx.Resolve(ports.Input);
            }
        }
    }

    public class DelegateMessageIONode : SimulationNodeDefinition<DelegateMessageIONode.SimPorts>
    {
        public struct SimPorts : ISimulationPortDefinition
        {
            public MessageInput<DelegateMessageIONode, Message> Input;
            public MessageOutput<DelegateMessageIONode, Message> Output;
        }

        public delegate void InitHandler(InitContext ctx);
        public delegate void MessageHandler(MessageContext ctx, in Message msg);
        public delegate void UpdateHandler(UpdateContext ctx);
        public delegate void DestroyHandler(DestroyContext ctx);

        struct Handlers
        {
            public InitHandler Init;
            public MessageHandler Message;
            public UpdateHandler Update;
            public DestroyHandler Destroy;
        }

        static Handlers s_Handlers;

        [Managed]
        struct NodeData : INodeData, IInit, IDestroy, IUpdate, IMsgHandler<Message>
        {
            Handlers m_Handlers;

            public void Init(InitContext ctx)
            {
                m_Handlers = s_Handlers;

                if (m_Handlers.Update != null)
                    ctx.RegisterForUpdate();

                s_Handlers = default;
                m_Handlers.Init?.Invoke(ctx);
            }

            public void HandleMessage(MessageContext ctx, in Message msg) => m_Handlers.Message?.Invoke(ctx, msg);
            public void Update(UpdateContext ctx) => m_Handlers.Update(ctx);
            public void Destroy(DestroyContext ctx) => m_Handlers.Destroy?.Invoke(ctx);
        }

        public static NodeHandle<DelegateMessageIONode> Create(NodeSet set, InitHandler initHandler, MessageHandler messageHandler, UpdateHandler updateHandler, DestroyHandler destroyHandler)
        {
            Assert.AreEqual(new Handlers(), s_Handlers);
            s_Handlers = new Handlers {Init = initHandler, Message = messageHandler, Update = updateHandler, Destroy = destroyHandler};
            try
            {
                return set.Create<DelegateMessageIONode>();
            }
            finally
            {
                s_Handlers = default;
            }
        }
    }

    public class DelegateMessageIONode<TNodeData> : SimulationNodeDefinition<DelegateMessageIONode<TNodeData>.SimPorts>
        where TNodeData : struct
    {
        public struct SimPorts : ISimulationPortDefinition
        {
            public MessageInput<DelegateMessageIONode<TNodeData>, Message> Input;
            public MessageOutput<DelegateMessageIONode<TNodeData>, Message> Output;
        }

        public delegate void InitHandler(InitContext ctx, ref TNodeData data);
        public delegate void MessageHandler(MessageContext ctx, in Message msg, ref TNodeData data);
        public delegate void UpdateHandler(UpdateContext ctx, ref TNodeData data);
        public delegate void DestroyHandler(DestroyContext ctx, ref TNodeData data);

        struct Handlers
        {
            public InitHandler Init;
            public MessageHandler Message;
            public UpdateHandler Update;
            public DestroyHandler Destroy;
        }

        static Handlers s_Handlers;

        [Managed]
        public struct NodeData : INodeData, IInit, IDestroy, IUpdate, IMsgHandler<Message>
        {
            public TNodeData CustomNodeData;
            Handlers m_Handlers;

            public void Init(InitContext ctx)
            {
                m_Handlers = s_Handlers;

                if (m_Handlers.Update != null)
                    ctx.RegisterForUpdate();

                s_Handlers = default;
                m_Handlers.Init?.Invoke(ctx, ref CustomNodeData);
            }

            public void HandleMessage(MessageContext ctx, in Message msg) => m_Handlers.Message?.Invoke(ctx, msg, ref CustomNodeData);
            public void Update(UpdateContext ctx) => m_Handlers.Update(ctx, ref CustomNodeData);
            public void Destroy(DestroyContext ctx) => m_Handlers.Destroy?.Invoke(ctx, ref CustomNodeData);
        }

        public static NodeHandle<DelegateMessageIONode<TNodeData>> Create(NodeSet set, InitHandler initHandler, MessageHandler messageHandler, UpdateHandler updateHandler, DestroyHandler destroyHandler)
        {
            Assert.AreEqual(new Handlers(), s_Handlers);
            s_Handlers = new Handlers {Init = initHandler, Message = messageHandler, Update = updateHandler, Destroy = destroyHandler};
            try
            {
                return set.Create<DelegateMessageIONode<TNodeData>>();
            }
            finally
            {
                s_Handlers = default;
            }
        }
    }

    public static class DelegateMessageIONode_NodeSet_Ex
    {
        public static NodeHandle<DelegateMessageIONode> Create<TDelegateMessageIONode>(this NodeSet set, DelegateMessageIONode.InitHandler initHandler, DelegateMessageIONode.MessageHandler messageHandler = null, DelegateMessageIONode.UpdateHandler updateHandler = null, DelegateMessageIONode.DestroyHandler destroyHandler = null)
            where TDelegateMessageIONode : DelegateMessageIONode
                => DelegateMessageIONode.Create(set, initHandler, messageHandler, updateHandler, destroyHandler);

        public static NodeHandle<DelegateMessageIONode> Create<TDelegateMessageIONode>(this NodeSet set, DelegateMessageIONode.MessageHandler messageHandler, DelegateMessageIONode.UpdateHandler updateHandler = null, DelegateMessageIONode.DestroyHandler destroyHandler = null)
            where TDelegateMessageIONode : DelegateMessageIONode
                => DelegateMessageIONode.Create(set, null, messageHandler, updateHandler, destroyHandler);

        public static NodeHandle<DelegateMessageIONode> Create<TDelegateMessageIONode>(this NodeSet set, DelegateMessageIONode.UpdateHandler updateHandler, DelegateMessageIONode.DestroyHandler destroyHandler = null)
            where TDelegateMessageIONode : DelegateMessageIONode
                => DelegateMessageIONode.Create(set, null, null, updateHandler, destroyHandler);

        public static NodeHandle<DelegateMessageIONode> Create<TDelegateMessageIONode>(this NodeSet set, DelegateMessageIONode.DestroyHandler destroyHandler)
            where TDelegateMessageIONode : DelegateMessageIONode
                => DelegateMessageIONode.Create(set, null, null, null, destroyHandler);

        public static NodeHandle<DelegateMessageIONode<TNodeData>> Create<TDelegateMessageIONode, TNodeData>(this NodeSet set, DelegateMessageIONode<TNodeData>.InitHandler initHandler, DelegateMessageIONode<TNodeData>.MessageHandler messageHandler = null, DelegateMessageIONode<TNodeData>.UpdateHandler updateHandler = null, DelegateMessageIONode<TNodeData>.DestroyHandler destroyHandler = null)
            where TDelegateMessageIONode : DelegateMessageIONode<TNodeData>
            where TNodeData : struct
                => DelegateMessageIONode<TNodeData>.Create(set, initHandler, messageHandler, updateHandler, destroyHandler);

        public static NodeHandle<DelegateMessageIONode<TNodeData>> Create<TDelegateMessageIONode, TNodeData>(this NodeSet set, DelegateMessageIONode<TNodeData>.MessageHandler messageHandler, DelegateMessageIONode<TNodeData>.UpdateHandler updateHandler = null, DelegateMessageIONode<TNodeData>.DestroyHandler destroyHandler = null)
            where TDelegateMessageIONode : DelegateMessageIONode<TNodeData>
            where TNodeData : struct
                => DelegateMessageIONode<TNodeData>.Create(set, null, messageHandler, updateHandler, destroyHandler);

        public static NodeHandle<DelegateMessageIONode<TNodeData>> Create<TDelegateMessageIONode, TNodeData>(this NodeSet set, DelegateMessageIONode<TNodeData>.UpdateHandler updateHandler, DelegateMessageIONode<TNodeData>.DestroyHandler destroyHandler = null)
            where TDelegateMessageIONode : DelegateMessageIONode<TNodeData>
            where TNodeData : struct
                => DelegateMessageIONode<TNodeData>.Create(set, null, null, updateHandler, destroyHandler);

        public static NodeHandle<DelegateMessageIONode<TNodeData>> Create<TDelegateMessageIONode, TNodeData>(this NodeSet set, DelegateMessageIONode<TNodeData>.DestroyHandler destroyHandler)
            where TDelegateMessageIONode : DelegateMessageIONode<TNodeData>
            where TNodeData : struct
                => DelegateMessageIONode<TNodeData>.Create(set, null, null, null, destroyHandler);
    }

    public abstract class ExternalKernelNode<TFinalNodeDefinition, TInput, TOutput, TKernel>
        : KernelNodeDefinition<ExternalKernelNode<TFinalNodeDefinition, TInput, TOutput, TKernel>.KernelDefs>
        where TFinalNodeDefinition : NodeDefinition
        where TInput : struct
        where TOutput : struct
        where TKernel : struct, IGraphKernel<ExternalKernelNode<TFinalNodeDefinition, TInput, TOutput, TKernel>.EmptyKernelData, ExternalKernelNode<TFinalNodeDefinition, TInput, TOutput, TKernel>.KernelDefs>
    {
        public struct EmptyKernelData : IKernelData { }

        public struct KernelDefs : IKernelPortDefinition
        {
            public DataInput<TFinalNodeDefinition, TInput> Input;
            public DataOutput<TFinalNodeDefinition, TOutput> Output;
        }
    }
}
