using System;
using NUnit.Framework;
using Unity.Burst;

namespace Unity.DataFlowGraph.Tests
{
    public class TaskPortTests
    {
        // Test every class of connect/disconnect
        // Test that Port API and untyped API works the same, by checking internal structures.
        // Test invalidation of customly instantiated port declarations
        // Test that you cannot connect multiple outputs to a single input for data kernels

        public static (NodeHandle<T> Node, T Class) GetNodeAndClass<T>(NodeSet set)
            where T : NodeDefinition, new()
        {
            return (set.Create<T>(), set.GetDefinition<T>());
        }

        public struct MessageContent { }

        public class MessageTaskPortHandlerNode
            : SimulationNodeDefinition<MessageTaskPortHandlerNode.SimPorts>
            , ITaskPort<MessageTaskPortHandlerNode>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
                public MessageInput<MessageTaskPortHandlerNode, MessageContent> Input;
            }

            public InputPortID GetPort(NodeHandle node)
            {
                return (InputPortID)SimulationPorts.Input;
            }

            struct NodeData : INodeData, IMsgHandler<MessageContent>
            {
                public void HandleMessage(MessageContext ctx, in MessageContent msg) { }
            }
        }

        public class MessageOutputNode : SimulationNodeDefinition<MessageOutputNode.SimPorts>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
                public MessageOutput<MessageOutputNode, MessageContent> Output;
            }
        }

        [Test]
        public void Connect_UsingPortIndices_WithITaskPort()
        {
            using (var set = new NodeSet())
            {
                var a = GetNodeAndClass<MessageOutputNode>(set);
                NodeHandle b = set.Create<MessageTaskPortHandlerNode>();

                var f = set.GetDefinition<MessageTaskPortHandlerNode>();
                set.Connect(a.Node, a.Class.GetPortDescription(a.Node).Outputs[0], b, f.GetPort(b));

                set.Destroy(a.Node, b);
            }
        }

        interface IMessageTaskPort : ITaskPort<IMessageTaskPort>
        {
        }

        public class MessageTaskPortNode
            : SimulationNodeDefinition<MessageTaskPortNode.SimPorts>
            , IMessageTaskPort
        {
            public struct SimPorts : ISimulationPortDefinition
            {
                public MessageInput<MessageTaskPortNode, MessageContent> Input;
            }

            public InputPortID GetPort(NodeHandle node)
            {
                return SimulationPorts.Input.Port;
            }

            struct NodeData : INodeData, IMsgHandler<MessageContent>
            {
                public void HandleMessage(MessageContext ctx, in MessageContent msg) { }
            }
        }

        [Test]
        public void Connect_UsingPortIndices_WithInterfaceLink()
        {
            using (var set = new NodeSet())
            {
                var a = GetNodeAndClass<MessageOutputNode>(set);
                NodeHandle b = set.Create<MessageTaskPortNode>();

                set.Connect(a.Node, a.Class.GetPortDescription(a.Node).Outputs[0], set.Adapt(b).To<IMessageTaskPort>());

                set.Destroy(a.Node, b);
            }
        }

        [Test]
        public void Connect_UsingMessagePorts_WithInterfaceLink()
        {
            using (var set = new NodeSet())
            {
                NodeHandle<MessageOutputNode> a = set.Create<MessageOutputNode>();
                NodeHandle<MessageTaskPortNode> b = set.Create<MessageTaskPortNode>();

                set.Connect(a, MessageOutputNode.SimulationPorts.Output, set.Adapt(b).To<IMessageTaskPort>());
                set.Destroy(a, b);
            }
        }

        public class DataOutputNode : KernelNodeDefinition<DataOutputNode.KernelPortDefinition>
        {
            public struct KernelPortDefinition : IKernelPortDefinition
            {
                public DataOutput<DataOutputNode, MessageContent> Output;
            }

            struct KernelData : IKernelData { }

            [BurstCompile(CompileSynchronously = true)]
            struct Kernel : IGraphKernel<KernelData, KernelPortDefinition>
            {
                public void Execute(RenderContext ctx, in KernelData data, ref KernelPortDefinition ports) { }
            }
        }

        interface IDataTaskPort : ITaskPort<IDataTaskPort>
        {
        }

        public class DataInputTaskNode
            : KernelNodeDefinition<DataInputTaskNode.KernelPortDefinition>
            , IDataTaskPort
        {
            public struct KernelPortDefinition : IKernelPortDefinition
            {
                public DataInput<DataInputTaskNode, MessageContent> Input;
            }

            struct KernelData : IKernelData { }

            [BurstCompile(CompileSynchronously = true)]
            struct Kernel : IGraphKernel<KernelData, KernelPortDefinition>
            {
                public void Execute(RenderContext ctx, in KernelData data, ref KernelPortDefinition ports) { }
            }

            public InputPortID GetPort(NodeHandle node)
            {
                return (InputPortID)KernelPorts.Input;
            }
        }

        [Test]
        public void Connect_UsingDataPorts_WithInterfaceLink()
        {
            using (var set = new NodeSet())
            {
                NodeHandle<DataOutputNode> a = set.Create<DataOutputNode>();
                NodeHandle<DataInputTaskNode> b = set.Create<DataInputTaskNode>();

                set.Connect(a, DataOutputNode.KernelPorts.Output, set.Adapt(b).To<IDataTaskPort>());

                set.Destroy(a, b);
            }
        }

        public interface TestDSL { }

        class DSL : DSLHandler<TestDSL>
        {
            protected override void Connect(ConnectionInfo left, ConnectionInfo right)
            {
            }

            protected override void Disconnect(ConnectionInfo left, ConnectionInfo right)
            {
            }
        }

        class DSLOutputNode : SimulationNodeDefinition<DSLOutputNode.SimPorts>, TestDSL
        {
            public struct SimPorts : ISimulationPortDefinition
            {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                public DSLOutput<DSLOutputNode, DSL, TestDSL> Output;
#pragma warning restore 649
            }
        }

        interface IDSLTaskPort : ITaskPort<IDSLTaskPort>
        {
        }

        class DSLInputTaskNode
            : SimulationNodeDefinition<DSLInputTaskNode.SimPorts>
            , IDSLTaskPort
            , TestDSL
        {
            public struct SimPorts : ISimulationPortDefinition
            {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                public DSLInput<DSLInputTaskNode, DSL, TestDSL> Input;
#pragma warning restore 649
            }

            public InputPortID GetPort(NodeHandle node)
            {
                return (InputPortID)SimulationPorts.Input;
            }
        }

        [Test]
        public void Connect_UsingDSLPorts_WithInterfaceLink()
        {
            using (var set = new NodeSet())
            {
                NodeHandle<DSLOutputNode> a = set.Create<DSLOutputNode>();
                NodeHandle<DSLInputTaskNode> b = set.Create<DSLInputTaskNode>();

                set.Connect(a, DSLOutputNode.SimulationPorts.Output, set.Adapt(b).To<IDSLTaskPort>());

                set.Destroy(a, b);
            }
        }

        [Test]
        public void Connect_UsingPortIndices_WithInvalidInterfaceLink_ThrowsException()
        {
            using (var set = new NodeSet())
            {
                var a = GetNodeAndClass<MessageOutputNode>(set);
                NodeHandle b = set.Create<EmptyNode>();

                Assert.Throws<InvalidCastException>(
                    () => set.Connect(a.Node, a.Class.GetPortDescription(a.Node).Outputs[0], set.Adapt(b).To<IMessageTaskPort>())
                );

                set.Destroy(a.Node, b);
            }
        }

        interface IOtherMessageTaskPort : ITaskPort<IOtherMessageTaskPort> { }

        public class MultipleNodeMessageTaskPortNode
            : SimulationNodeDefinition<MultipleNodeMessageTaskPortNode.SimPorts>
            , IMessageTaskPort
            , IOtherMessageTaskPort
        {
            public struct SimPorts : ISimulationPortDefinition
            {
                public MessageInput<MultipleNodeMessageTaskPortNode, MessageContent> FirstInput;
                public MessageInput<MultipleNodeMessageTaskPortNode, MessageContent> SecondInput;
            }

            InputPortID ITaskPort<IMessageTaskPort>.GetPort(NodeHandle node)
            {
                return (InputPortID)SimulationPorts.FirstInput;
            }

            InputPortID ITaskPort<IOtherMessageTaskPort>.GetPort(NodeHandle node)
            {
                return (InputPortID)SimulationPorts.SecondInput;
            }

            struct NodeData : INodeData, IMsgHandler<MessageContent>
            {
                public void HandleMessage(MessageContext ctx, in MessageContent msg)
                {
                    Assert.That(ctx.Port == SimulationPorts.FirstInput || ctx.Port == SimulationPorts.SecondInput);
                }
            }
        }

        [Test]
        public void Connect_UsingMessagePorts_WithMultipleInterfaces_WithSameMessageType()
        {
            using (var set = new NodeSet())
            {
                NodeHandle<MessageOutputNode> a = set.Create<MessageOutputNode>();
                NodeHandle<MultipleNodeMessageTaskPortNode> b =
                    set.Create<MultipleNodeMessageTaskPortNode>();

                set.Connect(a, MessageOutputNode.SimulationPorts.Output, set.Adapt(b).To<IMessageTaskPort>());
                set.Connect(a, MessageOutputNode.SimulationPorts.Output, set.Adapt(b).To<IOtherMessageTaskPort>());

                set.Destroy(a, b);
            }
        }

        interface IFloatTask : ITaskPort<IFloatTask> { }

        public class MultipleMessageTypesTaskPortNode
            : SimulationNodeDefinition<MultipleMessageTypesTaskPortNode.SimPorts>
            , IMessageTaskPort
            , IFloatTask
        {
            public struct SimPorts : ISimulationPortDefinition
            {
                public MessageInput<MultipleMessageTypesTaskPortNode, MessageContent> NodeMessageInput;
                public MessageInput<MultipleMessageTypesTaskPortNode, float> FloatInput;
            }

            InputPortID ITaskPort<IMessageTaskPort>.GetPort(NodeHandle node)
            {
                return (InputPortID)SimulationPorts.NodeMessageInput;
            }

            InputPortID ITaskPort<IFloatTask>.GetPort(NodeHandle node)
            {
                return (InputPortID)SimulationPorts.FloatInput;
            }

            struct NodeData : INodeData, IMsgHandler<MessageContent>, IMsgHandler<float>
            {
                public void HandleMessage(MessageContext ctx, in MessageContent msg)
                {
                    Assert.That(ctx.Port == SimulationPorts.NodeMessageInput);
                }

                public void HandleMessage(MessageContext ctx, in float msg)
                {
                    Assert.That(ctx.Port == SimulationPorts.FloatInput);
                }
            }
        }

        public class MultipleMessageTypeOutputNode : SimulationNodeDefinition<MultipleMessageTypeOutputNode.SimPorts>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
                public MessageOutput<MultipleMessageTypeOutputNode, MessageContent> NodeMessageOutput;
                public MessageOutput<MultipleMessageTypeOutputNode, float> FloatOutput;
            }
        }

        [Test]
        public void Connect_UsingMessagePorts_WithMultipleInterfaces_WithMultipleMessageTypes()
        {
            using (var set = new NodeSet())
            {
                NodeHandle<MultipleMessageTypeOutputNode> a = set.Create<MultipleMessageTypeOutputNode>();
                NodeHandle<MultipleMessageTypesTaskPortNode> b =
                    set.Create<MultipleMessageTypesTaskPortNode>();

                set.Connect(a, MultipleMessageTypeOutputNode.SimulationPorts.NodeMessageOutput, set.Adapt(b).To<IMessageTaskPort>());
                set.Connect(a, MultipleMessageTypeOutputNode.SimulationPorts.FloatOutput, set.Adapt(b).To<IFloatTask>());

                set.Destroy(a, b);
            }
        }

        [Test]
        public void Disconnect_UsingDataPorts_WithInterfaceLink()
        {
            using (var set = new NodeSet())
            {
                var a = GetNodeAndClass<DataOutputNode>(set);
                NodeHandle b = set.Create<DataInputTaskNode>();

                var ps = set.Adapt(b).To<IDataTaskPort>();

                set.Connect(a.Node, a.Class.GetPortDescription(a.Node).Outputs[0], ps);
                set.Disconnect(a.Node, a.Class.GetPortDescription(a.Node).Outputs[0], ps);

                set.Destroy(a.Node, b);
            }
        }

        [Test]
        public void Disconnect_UsingDSLPorts_WithInterfaceLink()
        {
            using (var set = new NodeSet())
            {
                var a = GetNodeAndClass<DSLOutputNode>(set);
                NodeHandle b = set.Create<DSLInputTaskNode>();

                var ps = set.Adapt(b).To<IDSLTaskPort>();

                set.Connect(a.Node, a.Class.GetPortDescription(a.Node).Outputs[0], ps);
                set.Disconnect(a.Node, a.Class.GetPortDescription(a.Node).Outputs[0], ps);

                set.Destroy(a.Node, b);
            }
        }

        public class OtherMessageTaskPortNode
            : SimulationNodeDefinition<OtherMessageTaskPortNode.SimPorts>
            , IOtherMessageTaskPort
        {
            public struct SimPorts : ISimulationPortDefinition
            {
                public MessageInput<OtherMessageTaskPortNode, MessageContent> Input;
            }

            public InputPortID GetPort(NodeHandle node)
            {
                return (InputPortID)SimulationPorts.Input;
            }

            struct NodeData : INodeData, IMsgHandler<MessageContent>
            {
                public void HandleMessage(MessageContext ctx, in MessageContent msg) { }
            }
        }

        [Test]
        public void Disconnect_UsingPortIndices_WithInterfaceLink()
        {
            using (var set = new NodeSet())
            {
                var a = GetNodeAndClass<MessageOutputNode>(set);
                NodeHandle b = set.Create<OtherMessageTaskPortNode>();

                var ps = set.Adapt(b).To<IOtherMessageTaskPort>();

                set.Connect(a.Node, a.Class.GetPortDescription(a.Node).Outputs[0], ps);
                set.Disconnect(a.Node, a.Class.GetPortDescription(a.Node).Outputs[0], ps);

                set.Destroy(a.Node, b);
            }
        }

        [Test]
        public void Disconnect_UsingPortIndices_WithInvalidInterfaceLink_ThrowsException()
        {
            using (var set = new NodeSet())
            {
                var a = GetNodeAndClass<MessageOutputNode>(set);
                NodeHandle b = set.Create<OtherMessageTaskPortNode>();

                set.Connect(a.Node, a.Class.GetPortDescription(a.Node).Outputs[0], set.Adapt(b).To<IOtherMessageTaskPort>());

                Assert.Throws<InvalidCastException>(() =>
                        set.Disconnect(a.Node, a.Class.GetPortDescription(a.Node).Outputs[0], set.Adapt(b).To<IMessageTaskPort>()));

                set.Destroy(a.Node, b);
            }
        }

        [Test]
        public void Disconnect_UsingMessagePort_WithInterfaceLink()
        {
            using (var set = new NodeSet())
            {
                NodeHandle<MessageOutputNode> a = set.Create<MessageOutputNode>();
                NodeHandle<OtherMessageTaskPortNode> b = set.Create<OtherMessageTaskPortNode>();

                var ps = set.Adapt(b).To<IOtherMessageTaskPort>();

                set.Connect(a, MessageOutputNode.SimulationPorts.Output, ps);
                set.Disconnect(a, MessageOutputNode.SimulationPorts.Output, ps);

                set.Destroy(a, b);
            }
        }

        [Test]
        public void Disconnect_UsingMessagePort_WithInterfaceLink_WithInvalidConnection_ThrowsException()
        {
            using (var set = new NodeSet())
            {
                NodeHandle<MessageOutputNode> a = set.Create<MessageOutputNode>();
                NodeHandle<OtherMessageTaskPortNode> b = set.Create<OtherMessageTaskPortNode>();

                Assert.Throws<ArgumentException>(() =>
                        set.Disconnect(a, MessageOutputNode.SimulationPorts.Output, set.Adapt(b).To<IOtherMessageTaskPort>()));

                set.Destroy(a, b);
            }
        }

        [Test]
        public void Disconnect_UsingMessagePort_WithInterfaceLink_WithDisconnectedNode_ThrowsException()
        {
            using (var set = new NodeSet())
            {
                NodeHandle<MessageOutputNode> a = set.Create<MessageOutputNode>();
                NodeHandle<OtherMessageTaskPortNode> b = set.Create<OtherMessageTaskPortNode>();

                var ps = set.Adapt(b).To<IOtherMessageTaskPort>();

                set.Connect(a, MessageOutputNode.SimulationPorts.Output, ps);
                set.Disconnect(a, MessageOutputNode.SimulationPorts.Output, ps);

                Assert.Throws<ArgumentException>(() =>
                        set.Disconnect(a, MessageOutputNode.SimulationPorts.Output, ps));

                set.Destroy(a, b);
            }
        }
    }
}

