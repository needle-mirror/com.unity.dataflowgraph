using NUnit.Framework;
using Unity.Burst;
using Unity.Entities;

namespace Unity.DataFlowGraph.Tests
{
    public interface TestDSL { }

    public struct EmptyPorts : ISimulationPortDefinition { }

    public struct EmptyKernelData : IKernelData { }

    public class DSL : DSLHandler<TestDSL>
    {
        protected override void Connect(ConnectionInfo left, ConnectionInfo right) { }
        protected override void Disconnect(ConnectionInfo left, ConnectionInfo right) { }
    }
    public struct EmptyData : INodeData { }

    public struct ECSInt : IComponentData
    {
        public int Value;
        public static implicit operator int (ECSInt val) => val.Value;
        public static implicit operator ECSInt(int val) => new ECSInt { Value = val };
    }

    public class NodeWithAllTypesOfPorts
        : NodeDefinition<EmptyData, NodeWithAllTypesOfPorts.SimPorts, EmptyKernelData, NodeWithAllTypesOfPorts.KernelDefs, NodeWithAllTypesOfPorts.Kernel>
            , TestDSL
            , IMsgHandler<int>
    {
        public struct SimPorts : ISimulationPortDefinition
        {
            public MessageInput<NodeWithAllTypesOfPorts, int> MessageIn;
            public PortArray<MessageInput<NodeWithAllTypesOfPorts, int>> MessageArrayIn;
            public MessageOutput<NodeWithAllTypesOfPorts, int> MessageOut;
            public DSLInput<NodeWithAllTypesOfPorts, DSL, TestDSL> DSLIn;
            public DSLOutput<NodeWithAllTypesOfPorts, DSL, TestDSL> DSLOut;
        }

        public struct KernelDefs : IKernelPortDefinition
        {
            public DataInput<NodeWithAllTypesOfPorts, Buffer<int>> InputBuffer;
            public PortArray<DataInput<NodeWithAllTypesOfPorts, Buffer<int>>> InputArrayBuffer;
            public DataOutput<NodeWithAllTypesOfPorts, Buffer<int>> OutputBuffer;
            public DataInput<NodeWithAllTypesOfPorts, int> InputScalar;
            public PortArray<DataInput<NodeWithAllTypesOfPorts, int>> InputArrayScalar;
            public DataOutput<NodeWithAllTypesOfPorts, int> OutputScalar;
        }

        [BurstCompile(CompileSynchronously = true)]
        public struct Kernel : IGraphKernel<EmptyKernelData, KernelDefs>
        {
            public void Execute(RenderContext context, EmptyKernelData data, ref KernelDefs ports) { }
        }
        public void HandleMessage(in MessageContext ctx, in int msg) { }
    }

    public class NodeWithParametricPortType<T>
        : NodeDefinition<EmptyData, NodeWithParametricPortType<T>.SimPorts, EmptyKernelData, NodeWithParametricPortType<T>.KernelDefs, NodeWithParametricPortType<T>.Kernel>
        , IMsgHandler<T>
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

        // disabled due to AOT Burst seeing this kernel, but being unable to compile it (parametric node)
        // [BurstCompile(CompileSynchronously = true)]
        public struct Kernel : IGraphKernel<EmptyKernelData, KernelDefs>
        {
            public void Execute(RenderContext ctx, EmptyKernelData data, ref KernelDefs ports) { }
        }

        public void HandleMessage(in MessageContext ctx, in T msg) { }
    }

    public class KernelAdderNode : NodeDefinition<EmptyData, EmptyKernelData, KernelAdderNode.KernelDefs, KernelAdderNode.Kernel>
    {
        public struct KernelDefs : IKernelPortDefinition
        {
            public DataInput<KernelAdderNode, int> Input;
            public DataOutput<KernelAdderNode, int> Output;
        }

        [BurstCompile(CompileSynchronously = true)]
        public struct Kernel : IGraphKernel<EmptyKernelData, KernelDefs>
        {
            public void Execute(RenderContext ctx, EmptyKernelData data, ref KernelDefs ports)
            {
                ctx.Resolve(ref ports.Output) = ctx.Resolve(ports.Input) + 1;
            }
        }
    }

    public class KernelSumNode : NodeDefinition<EmptyData, EmptyKernelData, KernelSumNode.KernelDefs, KernelSumNode.Kernel>
    {
        public struct KernelDefs : IKernelPortDefinition
        {
            public PortArray<DataInput<KernelSumNode, ECSInt>> Inputs;
            public DataOutput<KernelSumNode, ECSInt> Output;
        }

        [BurstCompile(CompileSynchronously = true)]
        public struct Kernel : IGraphKernel<EmptyKernelData, KernelDefs>
        {
            public void Execute(RenderContext ctx, EmptyKernelData data, ref KernelDefs ports)
            {
                ref var sum = ref ctx.Resolve(ref ports.Output);
                sum = 0;
                var inputs = ctx.Resolve(ports.Inputs);
                for (int i = 0; i < inputs.Length; ++i)
                    sum += inputs[i];
            }
        }
    }

    public class PassthroughTest<T>
        : NodeDefinition<
            EmptyData,
            PassthroughTest<T>.SimPorts,
            EmptyKernelData,
            PassthroughTest<T>.KernelDefs,
            PassthroughTest<T>.Kernel>
        , IMsgHandler<T>
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

        public void HandleMessage(in MessageContext ctx, in T msg)
        {
            Assert.That(ctx.Port == SimulationPorts.Input);
            ctx.EmitMessage(SimulationPorts.Output, msg);
        }

        public struct Kernel : IGraphKernel<EmptyKernelData, KernelDefs>
        {
            public void Execute(RenderContext ctx, EmptyKernelData data, ref KernelDefs ports)
            {
                ctx.Resolve(ref ports.Output) = ctx.Resolve(ports.Input);
            }
        }
    }

    public abstract class DelegateMessageIONodeBase<TDerivedDefinition, TNodeData>
        : NodeDefinition<DelegateMessageIONodeBase<TDerivedDefinition, TNodeData>.NodeData, DelegateMessageIONodeBase<TDerivedDefinition, TNodeData>.SimPorts>
        , IMsgHandler<Message>
            where TDerivedDefinition : DelegateMessageIONodeBase<TDerivedDefinition, TNodeData>, new()
            where TNodeData : struct, INodeData
    {
        public delegate void InitHandler(InitContext ctx);
        public delegate void MessageHandler(in MessageContext ctx, in Message msg);
        public delegate void UpdateHandler(in UpdateContext ctx);
        public delegate void DestroyHandler(NodeHandle handle);

        [Managed]
        public struct NodeData : INodeData
        {
            public TNodeData CustomNodeData;
            public MessageHandler m_MessageHandler;
            public UpdateHandler m_UpdateHandler;
            public DestroyHandler m_DestroyHandler;
        }

        public static NodeHandle<TDerivedDefinition> Create(NodeSet set, InitHandler initHandler, MessageHandler messageHandler, UpdateHandler updateHandler, DestroyHandler destroyHandler)
        {
            Assert.IsNull(s_InitHandler);
            s_InitHandler = initHandler;
            NodeHandle<TDerivedDefinition> node;
            try
            {
                node = set.Create<TDerivedDefinition>();
            }
            finally
            {
                s_InitHandler = null;
            }

            if (set.Exists(node))
            {
                ref var nodeData = ref set.GetDefinition<TDerivedDefinition>().GetNodeData(node);
                nodeData.m_MessageHandler = messageHandler;
                nodeData.m_UpdateHandler = updateHandler;
                nodeData.m_DestroyHandler = destroyHandler;
            }

            return node;
        }

        public struct SimPorts : ISimulationPortDefinition
        {
            public MessageInput<TDerivedDefinition, Message> Input;
            public MessageOutput<TDerivedDefinition, Message> Output;
        }

        protected override void Init(InitContext ctx)
        {
            var initHandler = s_InitHandler;
            s_InitHandler = null;
            initHandler?.Invoke(ctx);
        }

        public void HandleMessage(in MessageContext ctx, in Message msg)
        {
            Assert.That(ctx.Port == SimulationPorts.Input);
            GetNodeData(ctx.Handle).m_MessageHandler?.Invoke(ctx, msg);
        }

        protected override void OnUpdate(in UpdateContext ctx)
        {
            GetNodeData(ctx.Handle).m_UpdateHandler?.Invoke(ctx);
        }

        protected override void Destroy(DestroyContext ctx)
        {
            GetNodeData(ctx.Handle).m_DestroyHandler?.Invoke(ctx.Handle);
        }

        static InitHandler s_InitHandler;
    }

    public class DelegateMessageIONode : DelegateMessageIONodeBase<DelegateMessageIONode, DelegateMessageIONode.EmptyData>
    {
        public struct EmptyData : INodeData {}
    }

    public class DelegateMessageIONode<TNodeData> : DelegateMessageIONodeBase<DelegateMessageIONode<TNodeData>, TNodeData>
        where TNodeData : struct, INodeData
    {
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
            where TNodeData : struct, INodeData
                => DelegateMessageIONode<TNodeData>.Create(set, initHandler, messageHandler, updateHandler, destroyHandler);

        public static NodeHandle<DelegateMessageIONode<TNodeData>> Create<TDelegateMessageIONode, TNodeData>(this NodeSet set, DelegateMessageIONode<TNodeData>.MessageHandler messageHandler, DelegateMessageIONode<TNodeData>.UpdateHandler updateHandler = null, DelegateMessageIONode<TNodeData>.DestroyHandler destroyHandler = null)
            where TDelegateMessageIONode : DelegateMessageIONode<TNodeData>
            where TNodeData : struct, INodeData
                => DelegateMessageIONode<TNodeData>.Create(set, null, messageHandler, updateHandler, destroyHandler);

        public static NodeHandle<DelegateMessageIONode<TNodeData>> Create<TDelegateMessageIONode, TNodeData>(this NodeSet set, DelegateMessageIONode<TNodeData>.UpdateHandler updateHandler, DelegateMessageIONode<TNodeData>.DestroyHandler destroyHandler = null)
            where TDelegateMessageIONode : DelegateMessageIONode<TNodeData>
            where TNodeData : struct, INodeData
                => DelegateMessageIONode<TNodeData>.Create(set, null, null, updateHandler, destroyHandler);

        public static NodeHandle<DelegateMessageIONode<TNodeData>> Create<TDelegateMessageIONode, TNodeData>(this NodeSet set, DelegateMessageIONode<TNodeData>.DestroyHandler destroyHandler)
            where TDelegateMessageIONode : DelegateMessageIONode<TNodeData>
            where TNodeData : struct, INodeData
                => DelegateMessageIONode<TNodeData>.Create(set, null, null, null, destroyHandler);
    }

    public abstract class ExternalKernelNode<TFinalNodeDefinition, TInput, TOutput, TKernel>
        : NodeDefinition<EmptyKernelData, ExternalKernelNode<TFinalNodeDefinition, TInput, TOutput, TKernel>.KernelDefs, TKernel>
        where TFinalNodeDefinition : NodeDefinition
        where TInput : struct
        where TOutput : struct
        where TKernel : struct, IGraphKernel<EmptyKernelData, ExternalKernelNode<TFinalNodeDefinition, TInput, TOutput, TKernel>.KernelDefs>
    {
        public struct KernelDefs : IKernelPortDefinition
        {
            public DataInput<TFinalNodeDefinition, TInput> Input;
            public DataOutput<TFinalNodeDefinition, TOutput> Output;
        }
    }
}
