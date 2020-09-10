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

        public struct MessageContent
        {
        }

        public struct NodeData : INodeData
        {
        }

        public class MessageTaskPortHandlerNode
            : NodeDefinition<NodeData, MessageTaskPortHandlerNode.SimPorts>
            , ITaskPortMsgHandler<MessageTaskPortHandlerNode, MessageContent>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
                public MessageInput<MessageTaskPortHandlerNode, MessageContent> Input;
            }

            public InputPortID GetPort(NodeHandle node)
            {
                return (InputPortID)SimulationPorts.Input;
            }

            public void HandleMessage(in MessageContext ctx, in MessageContent msg)
            {
            }
        }

        public class MessageOutputNode : NodeDefinition<NodeData, MessageOutputNode.SimPorts>
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

        interface IMessageTaskPort : ITaskPortMsgHandler<IMessageTaskPort, MessageContent>
        {
        }

        public class MessageTaskPortNode
            : NodeDefinition<NodeData, MessageTaskPortNode.SimPorts>
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

            public void HandleMessage(in MessageContext ctx, in MessageContent msg)
            { }
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

        public class DataOutputNode : NodeDefinition<NodeData, DataOutputNode.KernelData, DataOutputNode.KernelPortDefinition, DataOutputNode.Kernel>
        {
            public struct KernelData : IKernelData
            {
            }

            public struct KernelPortDefinition : IKernelPortDefinition
            {
                public DataOutput<DataOutputNode, MessageContent> Output;
            }

            [BurstCompile(CompileSynchronously = true)]
            public struct Kernel : IGraphKernel<KernelData, KernelPortDefinition>
            {
                public void Execute(RenderContext ctx, KernelData data, ref KernelPortDefinition ports)
                {
                }
            }
        }

        interface IDataTaskPort : ITaskPort<IDataTaskPort>
        {
        }

        public class DataInputTaskNode
            : NodeDefinition<NodeData, DataInputTaskNode.KernelData, DataInputTaskNode.KernelPortDefinition, DataInputTaskNode.Kernel>
            , IDataTaskPort
        {
            public struct KernelData : IKernelData
            {
            }

            public struct KernelPortDefinition : IKernelPortDefinition
            {
                public DataInput<DataInputTaskNode, MessageContent> Input;
            }

            [BurstCompile(CompileSynchronously = true)]
            public struct Kernel : IGraphKernel<KernelData, KernelPortDefinition>
            {
                public void Execute(RenderContext ctx, KernelData data, ref KernelPortDefinition ports)
                {
                }
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

        class DSLOutputNode : NodeDefinition<NodeData, DSLOutputNode.SimPorts>, TestDSL
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
            : NodeDefinition<NodeData, DSLInputTaskNode.SimPorts>
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

        interface IOtherMessageTaskPort
            : ITaskPortMsgHandler<IOtherMessageTaskPort, MessageContent>
        {
        }

        public class MultipleNodeMessageTaskPortNode
            : NodeDefinition<NodeData, MultipleNodeMessageTaskPortNode.SimPorts>
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

            public void HandleMessage(in MessageContext ctx, in MessageContent msg)
            {
                Assert.That(
                    ctx.Port == (this as IMessageTaskPort).GetPort(ctx.Handle) ||
                    ctx.Port == (this as IOtherMessageTaskPort).GetPort(ctx.Handle)
                );
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

        interface IFloatTask
            : ITaskPortMsgHandler<IFloatTask, float>
        {
        }

        public class MultipleMessageTypesTaskPortNode
            : NodeDefinition<NodeData, MultipleMessageTypesTaskPortNode.SimPorts>
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

            public void HandleMessage(in MessageContext ctx, in MessageContent msg)
            {
                Assert.That(ctx.Port == (this as IMessageTaskPort).GetPort(ctx.Handle));
            }

            public void HandleMessage(in MessageContext ctx, in float msg)
            {
                Assert.That(ctx.Port == (this as IFloatTask).GetPort(ctx.Handle));
            }
        }

        public class MultipleMessageTypeOutputNode : NodeDefinition<NodeData, MultipleMessageTypeOutputNode.SimPorts>
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
            : NodeDefinition<NodeData, OtherMessageTaskPortNode.SimPorts>
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
            public void HandleMessage(in MessageContext ctx, in MessageContent msg)
            {
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

