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

        class MessageOutputNode : SimulationNodeDefinition<MessageOutputNode.SimPorts>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                public MessageOutput<MessageOutputNode, MessageContent> Output;
#pragma warning restore 649
            }

            internal struct Node : INodeData
            {
#pragma warning disable 649
                public float content;
#pragma warning restore 649
            }
        }

        interface IMessageTaskPort
            : ITaskPort<IMessageTaskPort>
        {
        }
        interface IOtherMessageTaskPort
            : ITaskPort<IOtherMessageTaskPort>
        {
        }

        class MessageTaskPortNode
            : SimulationNodeDefinition<MessageTaskPortNode.SimPorts>
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

            public struct Node : INodeData, IMsgHandler<MessageContent>
            {
                public float content;

                public void HandleMessage(MessageContext ctx, in MessageContent msg)
                {
                    Assert.That(ctx.Port == SimulationPorts.Input);
                    content = msg.content;
                }
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

                set.SendTest(b, (MessageTaskPortNode.Node data) => Assert.AreEqual(messageContent, data.content));

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

                set.SendTest(b, (MessageTaskPortNode.Node data) => Assert.AreEqual(messageContent, data.content));

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
