using System;
using NUnit.Framework;
using Unity.Burst;

namespace Unity.DataFlowGraph.Tests
{
    public class MessagingTests
    {
        public struct Node : INodeData
        {
            public int Contents;
            public float OtherContents;
        }

        public class SimpleMessageNode : NodeDefinition<Node, SimpleMessageNode.SimPorts>, IMsgHandler<GenericMessage<int>>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
                public MessageInput<SimpleMessageNode, GenericMessage<int>> Input;
                public MessageOutput<SimpleMessageNode, GenericMessage<int>> Output;
            }

            public void HandleMessage(in MessageContext ctx, in GenericMessage<int> msg)
            {
                ref var data = ref GetNodeData(ctx.Handle);

                Assert.That(ctx.Port == SimulationPorts.Input);
                data.Contents = msg.Contents;
                ctx.EmitMessage(SimulationPorts.Output, data.Contents + 20);
            }

            protected internal override void OnUpdate(in UpdateContext ctx)
            {
                ref var data = ref GetNodeData(ctx.Handle);
                data.Contents += 1;
                ctx.EmitMessage(SimulationPorts.Output, data.Contents + 20);
            }
        }

        [Test]
        public void TestSimpleMessageSending()
        {
            using (var set = new NodeSet())
            {
                NodeHandle<SimpleMessageNode>
                    a = set.Create<SimpleMessageNode>(),
                    b = set.Create<SimpleMessageNode>();

                set.Connect(a, SimpleMessageNode.SimulationPorts.Output, b, SimpleMessageNode.SimulationPorts.Input);
                set.SendMessage(a, SimpleMessageNode.SimulationPorts.Input, 10);

                set.SendTest(a, (Node data) => Assert.AreEqual(10, data.Contents));
                set.SendTest(b, (Node data) => Assert.AreEqual(30, data.Contents));

                set.Destroy(a, b);
            }
        }

        [Test]
        public void TestSimpleMessageEmitting_ToChildren_OnInit()
        {
            const int k_MagicMessage = 77;

            using (var set = new NodeSet())
            {
                var node = set.Create<DelegateMessageIONode<NodeHandle<PassthroughTest<Message>>>, NodeHandle<PassthroughTest<Message>>>(
                    initHandler: (InitContext ctx, ref NodeHandle<PassthroughTest<Message>> Child) =>
                    {
                        Child = ctx.Set.Create<PassthroughTest<Message>>();
                        var self = ctx.Set.CastHandle<DelegateMessageIONode<NodeHandle<PassthroughTest<Message>>>>(ctx.Handle);
                        ctx.Set.Connect(
                            self, DelegateMessageIONode<NodeHandle<PassthroughTest<Message>>>.SimulationPorts.Output,
                            Child, PassthroughTest<Message>.SimulationPorts.Input);
                        ctx.EmitMessage(DelegateMessageIONode<NodeHandle<PassthroughTest<Message>>>.SimulationPorts.Output, k_MagicMessage);
                    },
                    destroyHandler: (DestroyContext ctx, ref NodeHandle<PassthroughTest<Message>> Child) => ctx.Set.Destroy(Child)
                );

                set.SendTest<DelegateMessageIONode<NodeHandle<PassthroughTest<Message>>>.NodeData>(node,
                    ctx => ctx.SendTest(ctx.NodeData.CustomNodeData,
                        (PassthroughTest<Message>.NodeData data) => Assert.AreEqual(k_MagicMessage, data.LastReceivedMsg.Contents)));

                set.Destroy(node);
            }
        }

        [Test]
        public void TestSimpleMessageEmitting_OnUpdate()
        {
            using (var set = new NodeSet())
            {
                NodeHandle<SimpleMessageNode>
                    a = set.Create<SimpleMessageNode>(),
                    b = set.Create<SimpleMessageNode>();

                set.Connect(a, SimpleMessageNode.SimulationPorts.Output, b, SimpleMessageNode.SimulationPorts.Input);
                set.SendMessage(a, SimpleMessageNode.SimulationPorts.Input, 10);

                for (var i = 1; i < 10; ++i)
                {
                    set.Update();
                    set.SendTest(a, (Node data) => Assert.AreEqual(10 + i, data.Contents));
                    set.SendTest(b, (Node data) => Assert.AreEqual(30 + i + 1, data.Contents));
                }

                set.Destroy(a, b);
            }
        }

        [Test]
        public void EmitMessage_OnHandleMessage_AfterDestroy_Throws()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<DelegateMessageIONode>(
                    (in MessageContext ctx, in Message msg) =>
                    {
                        set.Destroy(ctx.Handle);
                        ctx.EmitMessage(DelegateMessageIONode.SimulationPorts.Output, msg);
                    }
                );
                Assert.Throws<InvalidOperationException>(
                    () => set.SendMessage(node, DelegateMessageIONode.SimulationPorts.Input, new Message()));
            }
        }

        [Test]
        public void EmitMessage_OnUpdate_AfterDestroy_Throws()
        {
            using (var set = new NodeSet())
            {
                set.Create<DelegateMessageIONode>(
                    (in UpdateContext ctx) =>
                    {
                        set.Destroy(ctx.Handle);
                        ctx.EmitMessage(DelegateMessageIONode.SimulationPorts.Output, new Message());
                    }
                );

                // new-style update registration only happens after the next update
                set.Update();

                Assert.Throws<InvalidOperationException>(() => set.Update());
            }
        }

        class SimpleMessageArrayNode : NodeDefinition<Node, SimpleMessageArrayNode.SimPorts>, IMsgHandler<int>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                public PortArray<MessageInput<SimpleMessageArrayNode, int>> Inputs;
                public PortArray<MessageOutput<SimpleMessageArrayNode, int>> Outputs;
#pragma warning restore 649
            }

            public void HandleMessage(in MessageContext ctx, in int msg)
            {
                ref var data = ref GetNodeData(ctx.Handle);

                Assert.That(ctx.Port == SimulationPorts.Inputs);
                ushort index = ctx.ArrayIndex;
                data.Contents = msg + index;
                ctx.EmitMessage(SimulationPorts.Outputs, index, index + 30);
            }
        }

        [Test]
        public void TestSimpleMessageArrayIO()
        {
            using (var set = new NodeSet())
            {
                NodeHandle<SimpleMessageArrayNode>
                    a = set.Create<SimpleMessageArrayNode>(),
                    b = set.Create<SimpleMessageArrayNode>(),
                    c = set.Create<SimpleMessageArrayNode>();

                set.SetPortArraySize(a, SimpleMessageArrayNode.SimulationPorts.Inputs, 2);
                set.SetPortArraySize(a, SimpleMessageArrayNode.SimulationPorts.Outputs, 3);
                set.SetPortArraySize(b, SimpleMessageArrayNode.SimulationPorts.Inputs, 4);
                set.SetPortArraySize(b, SimpleMessageArrayNode.SimulationPorts.Outputs, 5);
                set.SetPortArraySize(c, SimpleMessageArrayNode.SimulationPorts.Inputs, 6);
                set.SetPortArraySize(c, SimpleMessageArrayNode.SimulationPorts.Outputs, 7);

                set.Connect(a, SimpleMessageArrayNode.SimulationPorts.Outputs, 1, b, SimpleMessageArrayNode.SimulationPorts.Inputs, 2);
                set.Connect(b, SimpleMessageArrayNode.SimulationPorts.Outputs, 2, c, SimpleMessageArrayNode.SimulationPorts.Inputs, 4);
                set.SendMessage(a, SimpleMessageArrayNode.SimulationPorts.Inputs, 1, 10);

                set.SendTest(a, (Node data) => Assert.AreEqual(11, data.Contents));
                set.SendTest(b, (Node data) => Assert.AreEqual(33, data.Contents));
                set.SendTest(c, (Node data) => Assert.AreEqual(36, data.Contents));

                set.Disconnect(a, SimpleMessageArrayNode.SimulationPorts.Outputs, 1, b, SimpleMessageArrayNode.SimulationPorts.Inputs, 2);
                set.Disconnect(b, SimpleMessageArrayNode.SimulationPorts.Outputs, 2, c, SimpleMessageArrayNode.SimulationPorts.Inputs, 4);
                set.SendMessage(a, SimpleMessageArrayNode.SimulationPorts.Inputs, 1, 20);

                // Only the contents of the first node should have changed since downstream nodes have been disconnected.
                set.SendTest(a, (Node data) => Assert.AreEqual(21, data.Contents));
                set.SendTest(b, (Node data) => Assert.AreEqual(33, data.Contents));
                set.SendTest(c, (Node data) => Assert.AreEqual(36, data.Contents));

                set.Destroy(a, b, c);
            }
        }

        [Test]
        public void CanEnqueueMultipleMessagesAndConsumeInSteps()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<DelegateMessageIONode<Node>, Node>(
                    (in MessageContext ctx, in Message msg, ref Node data) => data.Contents += msg.Contents);

                set.SendTest(node, (DelegateMessageIONode<Node>.NodeData data) =>
                    Assert.AreEqual(0, data.CustomNodeData.Contents));

                for (int mc = 0; mc < 10; ++mc)
                {
                    for (int i = 0; i < 10; ++i)
                    {
                        set.SendMessage(node, DelegateMessageIONode<Node>.SimulationPorts.Input, new Message(10));
                    }

                    set.SendTest(node, (DelegateMessageIONode<Node>.NodeData data) => {
                        var contents = data.CustomNodeData.Contents;
                        Assert.AreEqual((mc + 1) * 10 * 10, contents);
                    });
                }

                set.Destroy(node);
            }
        }

        public class MulticastTestNode : NodeDefinition<Node, MulticastTestNode.SimPorts>, IMsgHandler<Message>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
                public MessageInput<MulticastTestNode, Message> Input1, Input2;
                public MessageOutput<MulticastTestNode, Message> Output1, Output2;
            }

            public void HandleMessage(in MessageContext ctx, in Message msg)
            {
                ref var data = ref GetNodeData(ctx.Handle);

                Assert.That(ctx.Port == SimulationPorts.Input1 || ctx.Port == SimulationPorts.Input2);
                data.Contents += msg.Contents;
                ctx.EmitMessage(SimulationPorts.Output1, new Message(data.Contents + 1));
            }
        }

        [Test]
        public void TestDiamondMessageSending()
        {
            using (var set = new NodeSet())
            {
                NodeHandle<MulticastTestNode>
                    a = set.Create<MulticastTestNode>(),
                    b = set.Create<MulticastTestNode>(),
                    c = set.Create<MulticastTestNode>(),
                    d = set.Create<MulticastTestNode>();

                set.Connect(a, MulticastTestNode.SimulationPorts.Output1, b, MulticastTestNode.SimulationPorts.Input1);
                set.Connect(a, MulticastTestNode.SimulationPorts.Output1, c, MulticastTestNode.SimulationPorts.Input1);
                set.Connect(b, MulticastTestNode.SimulationPorts.Output1, d, MulticastTestNode.SimulationPorts.Input1);
                set.Connect(c, MulticastTestNode.SimulationPorts.Output1, d, MulticastTestNode.SimulationPorts.Input1);

                set.SendMessage(a, MulticastTestNode.SimulationPorts.Input1, new Message(10));

                set.SendTest(a, (Node data) => Assert.AreEqual(10, data.Contents));
                set.SendTest(b, (Node data) => Assert.AreEqual(11, data.Contents));
                set.SendTest(c, (Node data) => Assert.AreEqual(11, data.Contents));
                set.SendTest(d, (Node data) => Assert.AreEqual(24, data.Contents));

                set.Destroy(a, b, c, d);
            }
        }

        [Test]
        public void TestPortMessageIsolation()
        {
            using (var set = new NodeSet())
            {
                NodeHandle<MulticastTestNode>
                    a = set.Create<MulticastTestNode>(),
                    b = set.Create<MulticastTestNode>(),
                    c = set.Create<MulticastTestNode>(),
                    d = set.Create<MulticastTestNode>();

                set.Connect(a, MulticastTestNode.SimulationPorts.Output1, b, MulticastTestNode.SimulationPorts.Input1);
                set.Connect(a, MulticastTestNode.SimulationPorts.Output2, c, MulticastTestNode.SimulationPorts.Input1);
                set.Connect(b, MulticastTestNode.SimulationPorts.Output1, d, MulticastTestNode.SimulationPorts.Input1);
                set.Connect(c, MulticastTestNode.SimulationPorts.Output1, d, MulticastTestNode.SimulationPorts.Input1);

                set.SendMessage(a, MulticastTestNode.SimulationPorts.Input1, new Message(10));

                set.SendTest(a, (Node data) => Assert.AreEqual(10, data.Contents));
                set.SendTest(b, (Node data) => Assert.AreEqual(11, data.Contents));
                // multicaster sends message on port 0, but c is connected through port 1, so it never updates its data.
                set.SendTest(c, (Node data) => Assert.AreEqual(0, data.Contents));
                set.SendTest(d, (Node data) => Assert.AreEqual(12, data.Contents));

                set.Destroy(a, b, c, d);
            }
        }

        [Test]
        public void TestMulticastMessageSending()
        {
            using (var set = new NodeSet())
            {
                NodeHandle<MulticastTestNode>
                    a = set.Create<MulticastTestNode>(),
                    b = set.Create<MulticastTestNode>(),
                    c = set.Create<MulticastTestNode>();

                set.Connect(a, MulticastTestNode.SimulationPorts.Output1, b, MulticastTestNode.SimulationPorts.Input1);
                set.Connect(a, MulticastTestNode.SimulationPorts.Output1, c, MulticastTestNode.SimulationPorts.Input1);

                set.SendMessage(a, MulticastTestNode.SimulationPorts.Input1, new Message(10));

                set.SendTest(a, (Node data) => Assert.AreEqual(10, data.Contents));
                set.SendTest(b, (Node data) => Assert.AreEqual(11, data.Contents));
                set.SendTest(c, (Node data) => Assert.AreEqual(11, data.Contents));

                set.Destroy(a, b, c);
            }
        }

        public class PassMessageThroughNextPort : NodeDefinition<Node, PassMessageThroughNextPort.SimPorts>, IMsgHandler<Message>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
                public MessageInput<PassMessageThroughNextPort, Message> Input1, Input2, Input3, Input4, Input5, Input6, Input7, Input8;
                public MessageOutput<PassMessageThroughNextPort, Message> Output1, Output2, Output3, Output4, Output5, Output6, Output7, Output8;
            }

            public void HandleMessage(in MessageContext ctx, in Message msg)
            {
                ref var data = ref GetNodeData(ctx.Handle);

                data.Contents =
                    msg.Contents +
                    (ctx.Port == SimulationPorts.Input1 ? 0 :
                     ctx.Port == SimulationPorts.Input2 ? 1 :
                     ctx.Port == SimulationPorts.Input3 ? 2 :
                     ctx.Port == SimulationPorts.Input4 ? 3 :
                     ctx.Port == SimulationPorts.Input5 ? 4 :
                     ctx.Port == SimulationPorts.Input6 ? 5 :
                     ctx.Port == SimulationPorts.Input7 ? 6 : 7);
                ctx.EmitMessage(
                    ctx.Port == SimulationPorts.Input1 ? SimulationPorts.Output2 :
                    ctx.Port == SimulationPorts.Input2 ? SimulationPorts.Output3 :
                    ctx.Port == SimulationPorts.Input3 ? SimulationPorts.Output4 :
                    ctx.Port == SimulationPorts.Input4 ? SimulationPorts.Output5 :
                    ctx.Port == SimulationPorts.Input5 ? SimulationPorts.Output6 :
                    ctx.Port == SimulationPorts.Input6 ? SimulationPorts.Output7 :
                        SimulationPorts.Output8,
                    new Message(msg.Contents + 1)
                );
            }
        }

        [Test]
        public void TestMessagePortCascading()
        {
            using (var set = new NodeSet())
            {
                NodeHandle<PassMessageThroughNextPort>
                    a = set.Create<PassMessageThroughNextPort>(),
                    b = set.Create<PassMessageThroughNextPort>(),
                    c = set.Create<PassMessageThroughNextPort>(),
                    d = set.Create<PassMessageThroughNextPort>();

                set.Connect(a, PassMessageThroughNextPort.SimulationPorts.Output2, b, PassMessageThroughNextPort.SimulationPorts.Input3);
                set.Connect(b, PassMessageThroughNextPort.SimulationPorts.Output4, c, PassMessageThroughNextPort.SimulationPorts.Input5);
                set.Connect(c, PassMessageThroughNextPort.SimulationPorts.Output6, d, PassMessageThroughNextPort.SimulationPorts.Input7);

                set.SendMessage(a, PassMessageThroughNextPort.SimulationPorts.Input1, new Message(10));

                set.SendTest(a, (Node data) => Assert.AreEqual(10, data.Contents));
                set.SendTest(b, (Node data) => Assert.AreEqual(13, data.Contents));
                set.SendTest(c, (Node data) => Assert.AreEqual(16, data.Contents));
                set.SendTest(d, (Node data) => Assert.AreEqual(19, data.Contents));

                set.Destroy(a, b, c, d);
            }
        }

        public class DifferentHandlers
            : NodeDefinition<Node, DifferentHandlers.SimPorts>
            , IMsgHandler<int>
            , IMsgHandler<float>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
                public MessageInput<DifferentHandlers, int> Input1;
                public MessageInput<DifferentHandlers, float> Input2;
                public MessageOutput<DifferentHandlers, Message> Output1, Output2;
            }

            public void HandleMessage(in MessageContext ctx, in int msg)
            {
                ref var data = ref GetNodeData(ctx.Handle);
                data.Contents = msg + 1 + (ctx.Port == SimulationPorts.Input1 ? 0 : 1);
            }

            public void HandleMessage(in MessageContext ctx, in float msg)
            {
                ref var data = ref GetNodeData(ctx.Handle);
                data.OtherContents = msg + 2 + (ctx.Port == SimulationPorts.Input1 ? 0 : 1);
            }
        }


        [Test]
        public void TestDifferentHandlers()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<DifferentHandlers>();

                set.SendMessage(node, DifferentHandlers.SimulationPorts.Input1, 5);
                set.SendMessage(node, DifferentHandlers.SimulationPorts.Input2, 5f);

                set.SendTest(node, (Node data) => Assert.AreEqual(6, data.Contents));
                set.SendTest(node, (Node data) => Assert.AreEqual(8, data.OtherContents));

                set.Destroy(node);
            }
        }

        [Test]
        public void CannotSendMessageToWrongPort()
        {
            using (var set = new NodeSet())
            {
                NodeHandle node = set.Create<DifferentHandlers>();

                // Try sending to port but using the wrong type.
                Assert.Throws<InvalidOperationException>(() => set.SendMessage(node, (InputPortID)DifferentHandlers.SimulationPorts.Input1, 5f));

                // Try sending to port of a type which node doesn't supports.
                var otherNodesMessagePort = (InputPortID)SimpleMessageNode.SimulationPorts.Input;
                Assume.That(otherNodesMessagePort == (InputPortID)DifferentHandlers.SimulationPorts.Input1);
                Assert.Throws<InvalidOperationException>(() => set.SendMessage(node, otherNodesMessagePort, new Message(10)));

                // Try sending to port of a type which the node supports but incorrect port ID.
                var otherNodesFloatPort = (InputPortID)NodeWithParametricPortType<float>.SimulationPorts.MessageIn;
                Assume.That(otherNodesFloatPort != (InputPortID)DifferentHandlers.SimulationPorts.Input2);
                Assert.Throws<InvalidOperationException>(() => set.SendMessage(node, otherNodesFloatPort, 5f));

                set.SendTest(node, (Node data) => Assert.AreEqual(0, data.Contents));
                set.SendTest(node, (Node data) => Assert.AreEqual(0, data.OtherContents));

                set.Destroy(node);
            }
        }

        public class KernelNode : NodeDefinition<KernelNode.Node, KernelNode.Data, KernelNode.KernelDefs, KernelNode.Kernel>
        {
            public struct Node : INodeData { }

            public struct Data : IKernelData { }

            public struct KernelDefs : IKernelPortDefinition
            {
                public DataInput<KernelNode, int> Input1, Input2;
                public DataOutput<KernelNode, int> Output;
            }

            [BurstCompile(CompileSynchronously = true)]
            public struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                public void Execute(RenderContext ctx, Data data, ref KernelDefs ports)
                {
                }
            }
        }

        [Test]
        public void CannotSetData_OnConnectedDataInput()
        {
            using (var set = new NodeSet())
            {
                var node1 = set.Create<KernelNode>();
                var node2 = set.Create<KernelNode>();

                set.Connect(node1, KernelNode.KernelPorts.Output, node2, KernelNode.KernelPorts.Input1);

                set.SetData(node1, KernelNode.KernelPorts.Input1, 5);
                set.SetData(node1, KernelNode.KernelPorts.Input2, 10);

                Assert.Throws<InvalidOperationException>(() => set.SetData(node2, KernelNode.KernelPorts.Input1, 15));
                set.SetData(node2, KernelNode.KernelPorts.Input2, 20);

                set.Disconnect(node1, KernelNode.KernelPorts.Output, node2, KernelNode.KernelPorts.Input1);
                set.Connect(node1, KernelNode.KernelPorts.Output, node2, KernelNode.KernelPorts.Input2);

                set.SetData(node2, KernelNode.KernelPorts.Input1, 25);
                Assert.Throws<InvalidOperationException>(() => set.SetData(node2, KernelNode.KernelPorts.Input2, 30));

                set.Destroy(node1);
                set.Destroy(node2);
            }
        }

        public enum APIType
        {
            StronglyTyped,
            WeaklyTyped
        }

        [Test]
        public void CanConnect_MessagePort_ToDataPort([Values] APIType apiType)
        {
            using (var set = new NodeSet())
            {
                var msgNode = set.Create<PassthroughTest<int>>();
                var dataNode = set.Create<PassthroughTest<int>>();

                GraphValue<int> gv = set.CreateGraphValue(dataNode, PassthroughTest<int>.KernelPorts.Output);

                if (apiType == APIType.StronglyTyped)
                    set.Connect(msgNode, PassthroughTest<int>.SimulationPorts.Output, dataNode, PassthroughTest<int>.KernelPorts.Input);
                else
                    set.Connect(msgNode, (OutputPortID)PassthroughTest<int>.SimulationPorts.Output, dataNode, (InputPortID)PassthroughTest<int>.KernelPorts.Input);

                set.Update();
                Assert.AreEqual(0, set.GetValueBlocking(gv));

                set.SendMessage(msgNode, PassthroughTest<int>.SimulationPorts.Input, 5);
                set.Update();
                Assert.AreEqual(5, set.GetValueBlocking(gv));

                set.ReleaseGraphValue(gv);

                set.Destroy(msgNode, dataNode);
            }
        }

        public enum DisconnectApiType
        {
            StronglyTyped,
            WeaklyTyped,
            WeaklyTypedRetain
        }

        [Test]
        public void CanConnect_Multiple_MessagePorts_ToDataPort()
        {
            using (var set = new NodeSet())
            {
                var msgNode1 = set.Create<PassthroughTest<int>>();
                var msgNode2 = set.Create<PassthroughTest<int>>();
                var dataNode = set.Create<PassthroughTest<int>>();

                GraphValue<int> gv = set.CreateGraphValue(dataNode, PassthroughTest<int>.KernelPorts.Output);

                set.Connect(msgNode1, PassthroughTest<int>.SimulationPorts.Output, dataNode, PassthroughTest<int>.KernelPorts.Input);
                set.Connect(msgNode2, PassthroughTest<int>.SimulationPorts.Output, dataNode, PassthroughTest<int>.KernelPorts.Input);

                set.Update();
                Assert.AreEqual(0, set.GetValueBlocking(gv));

                set.SendMessage(msgNode1, PassthroughTest<int>.SimulationPorts.Input, 5);
                set.Update();
                Assert.AreEqual(5, set.GetValueBlocking(gv));

                set.SendMessage(msgNode2, PassthroughTest<int>.SimulationPorts.Input, 7);
                set.Update();
                Assert.AreEqual(7, set.GetValueBlocking(gv));

                set.ReleaseGraphValue(gv);

                set.Destroy(msgNode1, msgNode2, dataNode);
            }
        }

        [Test]
        public void Disconnect_MessagePort_FromDataPort_RetainsData([Values] DisconnectApiType apiType)
        {
            using (var set = new NodeSet())
            {
                var msgNode = set.Create<PassthroughTest<int>>();
                var dataNode = set.Create<PassthroughTest<int>>();

                GraphValue<int> gv = set.CreateGraphValue(dataNode, PassthroughTest<int>.KernelPorts.Output);

                set.Connect(msgNode, PassthroughTest<int>.SimulationPorts.Output, dataNode, PassthroughTest<int>.KernelPorts.Input);

                set.SendMessage(msgNode, PassthroughTest<int>.SimulationPorts.Input, 5);

                // Note: For a Message->Data connection, disconnection _always_ just leaves the last value transmitted to
                // the DataInput in place. Thus, there is no strong API DisconnectAndRetain (it would be redundant).
                if (apiType == DisconnectApiType.StronglyTyped)
                    set.Disconnect(msgNode, PassthroughTest<int>.SimulationPorts.Output, dataNode, PassthroughTest<int>.KernelPorts.Input);
                else if (apiType == DisconnectApiType.WeaklyTypedRetain)
                    set.DisconnectAndRetainValue(msgNode, (OutputPortID)PassthroughTest<int>.SimulationPorts.Output, dataNode, (InputPortID)PassthroughTest<int>.KernelPorts.Input);
                else
                    set.Disconnect(msgNode, (OutputPortID)PassthroughTest<int>.SimulationPorts.Output, dataNode, (InputPortID)PassthroughTest<int>.KernelPorts.Input);

                set.Update();
                Assert.AreEqual(5, set.GetValueBlocking(gv));

                set.ReleaseGraphValue(gv);

                set.Destroy(msgNode, dataNode);
            }
        }

        [Test]
        public void CannotConnect_MessagePort_ToAlreadyConnected_DataPort()
        {
            using (var set = new NodeSet())
            {
                var msgNode = set.Create<PassthroughTest<int>>();
                var dataNode1 = set.Create<PassthroughTest<int>>();
                var dataNode2 = set.Create<PassthroughTest<int>>();

                set.Connect(dataNode1, PassthroughTest<int>.KernelPorts.Output, dataNode2, PassthroughTest<int>.KernelPorts.Input);
                Assert.Throws<ArgumentException>(() => set.Connect(msgNode, PassthroughTest<int>.SimulationPorts.Output, dataNode2, PassthroughTest<int>.KernelPorts.Input));

                set.Destroy(msgNode, dataNode1, dataNode2);
            }
        }

        [Test]
        public void CannotConnect_DataPort_ToAlreadyConnected_MessageToDataPort()
        {
            using (var set = new NodeSet())
            {
                var msgNode = set.Create<PassthroughTest<int>>();
                var dataNode1 = set.Create<PassthroughTest<int>>();
                var dataNode2 = set.Create<PassthroughTest<int>>();

                set.Connect(msgNode, PassthroughTest<int>.SimulationPorts.Output, dataNode2, PassthroughTest<int>.KernelPorts.Input);
                Assert.Throws<ArgumentException>(() => set.Connect(dataNode1, PassthroughTest<int>.KernelPorts.Output, dataNode2, PassthroughTest<int>.KernelPorts.Input));

                set.Destroy(msgNode, dataNode1, dataNode2);
            }
        }

        public class NewStyleMessageNode : SimulationNodeDefinition<NewStyleMessageNode.MyPorts>
        {
            public struct MyPorts : ISimulationPortDefinition
            {
                public MessageInput<NewStyleMessageNode, int> IntInput;
                public MessageInput<NewStyleMessageNode, float> FloatInput;
            }

            internal struct NodeData : INodeData, IMsgHandler<int>, IMsgHandler<float>
            {
                public int IntResult;
                public float FloatResult;

                public void HandleMessage(in MessageContext ctx, in int msg)
                {
                    IntResult = msg;
                }

                public void HandleMessage(in MessageContext ctx, in float msg)
                {
                    FloatResult = msg;
                }
            }
        }

        [Test]
        public void CanCallNewStyle_CodeGenerated_MessageHandlers()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<NewStyleMessageNode>();

                set.SendMessage(node, NewStyleMessageNode.SimulationPorts.IntInput, 10);
                set.SendTest(node, (NewStyleMessageNode.NodeData data) => Assert.AreEqual(10, data.IntResult));

                set.SendMessage(node, NewStyleMessageNode.SimulationPorts.FloatInput, 10);
                set.SendTest(node, (NewStyleMessageNode.NodeData data) => Assert.AreEqual(10.0f, data.FloatResult));

                set.Destroy(node);
            }
        }

        public class GenericHiddenNewStyleMessageNode<T> : SimulationNodeDefinition<GenericHiddenNewStyleMessageNode<T>.MyPorts>
            where T : struct
        {
            interface HiddenInterface : IMsgHandler<T> { }

            interface MoreHiddenInterface : HiddenInterface { }

            internal struct NodeData : INodeData, MoreHiddenInterface
            {
                public T Result;

                public void HandleMessage(in MessageContext ctx, in T msg)
                {
                    Result = msg;
                }
            }

            public struct MyPorts : ISimulationPortDefinition
            {
                public MessageInput<GenericHiddenNewStyleMessageNode<T>, T> Input;

            }
        }

        [Test]
        public void CanCallNewStyle_CodeGenerated_DoubleHidden_MessageHandlers_ThatAreAlsoGeneric()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<GenericHiddenNewStyleMessageNode<float>>();

                set.SendMessage(node, GenericHiddenNewStyleMessageNode<float>.SimulationPorts.Input, 10.0f);
                set.SendTest(node, (GenericHiddenNewStyleMessageNode<float>.NodeData nodeData) =>
                    Assert.AreEqual(10, nodeData.Result));

                set.Destroy(node);
            }
        }

        public class NewStyleInitHandler : SimulationNodeDefinition<NewStyleInitHandler.MyPorts>
        {
            public struct MyPorts : ISimulationPortDefinition { }

            internal struct NodeData : INodeData, IInit
            {
                public bool Called;

                public void Init(InitContext ctx)
                {
                    Called = true;
                }
            }
        }

        [Test]
        public void CanCallNewStyle_CodeGenerated_InitHandler()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<NewStyleInitHandler>();

                set.SendTest(node, (NewStyleInitHandler.NodeData data) => Assert.True(data.Called));

                set.Destroy(node);
            }
        }

        public class MixedGenericInputNode<T> : SimulationNodeDefinition<MixedGenericInputNode<T>.Ports>
        {
            public struct Ports : ISimulationPortDefinition
            {
                public MessageInput<MixedGenericInputNode<T>, T> InputT;
                public MessageInput<MixedGenericInputNode<T>, int> InputInt;
            }

            public struct Data : INodeData, IMsgHandler<int>, IMsgHandlerGeneric<T>
            {
                public InputPortID LastReceivedInput;

                public void HandleMessage(in MessageContext ctx, in int msg)
                {
                    LastReceivedInput = ctx.Port;
                }

                public void HandleMessage(in MessageContext ctx, in T msg)
                {
                    LastReceivedInput = ctx.Port;
                }
            }
        }

        [Test]
        public void CanHandle_GenericMsgInput_AndConcreteMsgInput()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<MixedGenericInputNode<int>>();
                Assert.AreEqual(set.GetNodeData<MixedGenericInputNode<int>.Data>(node).LastReceivedInput, default(InputPortID));

                set.SendMessage(node, MixedGenericInputNode<int>.SimulationPorts.InputInt, 1);
                Assert.AreEqual(set.GetNodeData<MixedGenericInputNode<int>.Data>(node).LastReceivedInput, (InputPortID)MixedGenericInputNode<int>.SimulationPorts.InputInt);

                set.SendMessage(node, MixedGenericInputNode<int>.SimulationPorts.InputT, 1);
                Assert.AreEqual(set.GetNodeData<MixedGenericInputNode<int>.Data>(node).LastReceivedInput, (InputPortID)MixedGenericInputNode<int>.SimulationPorts.InputT);

                set.Destroy(node);
            }
        }

        public abstract class MixedGenericInputNodeCRTP<TFinalNodeDefinition, T>
            : SimulationNodeDefinition<MixedGenericInputNodeCRTP<TFinalNodeDefinition, T>.Ports>
                where TFinalNodeDefinition : NodeDefinition
        {
            public struct Ports : ISimulationPortDefinition
            {
                public MessageInput<TFinalNodeDefinition, T> InputT;
                public MessageInput<TFinalNodeDefinition, int> InputInt;
            }

            public struct Data : INodeData, IMsgHandler<int>, IMsgHandlerGeneric<T>
            {
                public InputPortID LastReceivedInput;

                public void HandleMessage(in MessageContext ctx, in int msg)
                {
                    LastReceivedInput = ctx.Port;
                }

                public void HandleMessage(in MessageContext ctx, in T msg)
                {
                    LastReceivedInput = ctx.Port;
                }
            }
        }

        public class MixedGenericInputNodeCRTPForFloat : MixedGenericInputNodeCRTP<MixedGenericInputNodeCRTPForFloat, float> {}

        [Test]
        public void CanHandle_GenericMsgInput_AndConcreteMsgInput_WithCRTPPattern()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<MixedGenericInputNodeCRTPForFloat>();
                Assert.AreEqual(set.GetNodeData<MixedGenericInputNodeCRTPForFloat.Data>(node).LastReceivedInput, default(InputPortID));

                set.SendMessage(node, MixedGenericInputNodeCRTPForFloat.SimulationPorts.InputInt, 1);
                Assert.AreEqual(set.GetNodeData<MixedGenericInputNodeCRTPForFloat.Data>(node).LastReceivedInput, (InputPortID)MixedGenericInputNode<int>.SimulationPorts.InputInt);

                set.SendMessage(node, MixedGenericInputNodeCRTPForFloat.SimulationPorts.InputT, 1);
                Assert.AreEqual(set.GetNodeData<MixedGenericInputNodeCRTPForFloat.Data>(node).LastReceivedInput, (InputPortID)MixedGenericInputNode<int>.SimulationPorts.InputT);

                set.Destroy(node);
            }
        }
    }
}
