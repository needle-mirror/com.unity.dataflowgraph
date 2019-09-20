using NUnit.Framework;
using System;

namespace Unity.DataFlowGraph.Tests
{
    class TaskPortMessageTests
    {
        public struct MessageContent
        {
            public float content;
        }

        public struct NodeData : INodeData
        {
            public float content;
        }

        class MessageOutputNode : NodeDefinition<NodeData, MessageOutputNode.SimPorts>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                public MessageOutput<MessageOutputNode, MessageContent> Output;
#pragma warning restore 649
            }
        }

        interface IMessageTaskPort
            : ITaskPortMsgHandler<IMessageTaskPort, MessageContent>
        {
        }
        interface IOtherMessageTaskPort
            : ITaskPortMsgHandler<IOtherMessageTaskPort, MessageContent>
        {
        }

        class MessageTaskPortNode
            : NodeDefinition<NodeData, MessageTaskPortNode.SimPorts>
            , IMessageTaskPort
        {
            public struct SimPorts : ISimulationPortDefinition
            {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                public MessageInput<MessageTaskPortNode, MessageContent> Input;
#pragma warning restore 649
            }

            public InputPortID GetPort(NodeHandle node)
            {
                return (InputPortID)SimulationPorts.Input;
            }

            public void HandleMessage(in MessageContext ctx, in MessageContent msg)
            {
                Assert.That(ctx.Port == GetPort(ctx.Handle));
                ref var data = ref GetNodeData(ctx.Handle);
                data.content = msg.content;
            }
        }

        interface ITaskPortNoHandler : ITaskPort<ITaskPortNoHandler> { }

        class TaskPortNoHandlerNode
            : NodeDefinition<NodeData, TaskPortNoHandlerNode.SimPorts>
            , ITaskPortNoHandler
        {
            public struct SimPorts : ISimulationPortDefinition
            {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                public MessageInput<MessageTaskPortNode, MessageContent> Input;
#pragma warning restore 649
            }

            public InputPortID GetPort(NodeHandle node)
            {
                return (InputPortID)SimulationPorts.Input;
            }
        }

        [Test]
        public void SendMessage_WithInterfaceLink()
        {
            using (var set = new NodeSet())
            {
                NodeHandle<MessageOutputNode> a = set.Create<MessageOutputNode>();
                NodeHandle<MessageTaskPortNode> b = set.Create<MessageTaskPortNode>();

                var ps = set.Adapt(b).To<IMessageTaskPort>();

                set.Connect(a, MessageOutputNode.SimulationPorts.Output, ps);

                const float messageContent = 10f;

                set.SendMessage(ps, new MessageContent { content = messageContent });

                Assert.AreEqual(messageContent, set.GetNodeData<NodeData>(b).content);

                set.Destroy(a, b);
            }
        }

        [Test]
        public void SendMessage_WithInterfaceLink_NoDefinition()
        {
            using (var set = new NodeSet())
            {
                NodeHandle a = set.Create<MessageOutputNode>();
                NodeHandle b = set.Create<MessageTaskPortNode>();

                var ps = set.Adapt(b).To<IMessageTaskPort>();

                set.Connect(a, MessageOutputNode.SimulationPorts.Output.Port, ps);

                const float messageContent = 10f;

                set.SendMessage(ps, new MessageContent { content = messageContent });

                Assert.AreEqual(messageContent, set.GetNodeData<NodeData>(b).content);

                set.Destroy(a, b);
            }
        }

        [Test]
        public void SendMessage_WithInvalidInterfaceLink_ThrowsException()
        {
            using (var set = new NodeSet())
            {
                NodeHandle a = set.Create<MessageOutputNode>();
                NodeHandle b = set.Create<MessageTaskPortNode>();

                set.Connect(a, MessageOutputNode.SimulationPorts.Output.Port, set.Adapt(b).To<IMessageTaskPort>());

                const float messageContent = 10f;

                Assert.Throws<InvalidCastException>(() =>
                    set.SendMessage(set.Adapt(b).To<IOtherMessageTaskPort>(), new MessageContent { content = messageContent }));

                set.Destroy(a, b);
            }
        }
    }
}
