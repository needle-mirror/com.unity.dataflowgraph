using System;
using NUnit.Framework;

namespace Unity.DataFlowGraph.Tests
{
    public class TopologyWalkerTests
    {
        public struct Node : INodeData { }

        public class OneInOutNode : NodeDefinition<Node, OneInOutNode.SimPorts>, IMsgHandler<Message>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
                public MessageInput<OneInOutNode, Message> Input;
                public MessageOutput<OneInOutNode, Message> Output;
            }

            public void HandleMessage(in MessageContext ctx, in Message msg)
            {
                throw new NotImplementedException();
            }
        }

        public class ThreeInOutNode : NodeDefinition<Node, ThreeInOutNode.SimPorts>, IMsgHandler<Message>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
                public MessageInput<ThreeInOutNode, Message> Input1, Input2, Input3;
                public MessageOutput<ThreeInOutNode, Message> Output1, Output2, Output3;
            }

            public void HandleMessage(in MessageContext ctx, in Message msg)
            {
                throw new NotImplementedException();
            }
        }

        class EmptyNode : NodeDefinition<Node>
        {
        }

        [Test]
        public void GetInputsOutputs_WorksFor_ActualNode()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<OneInOutNode>();

                set.GetInputs(node);
                set.GetOutputs(node);

                set.Destroy(node);
            }
        }

        [Test]
        public void GetInputsOutputs_ThrowsExceptionOn_NullHandleAndDestroyedNode()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<OneInOutNode>();
                set.Destroy(node);

                Assert.Throws<ArgumentException>(() => set.GetInputs(node));
                Assert.Throws<ArgumentException>(() => set.GetOutputs(node));
                Assert.Throws<ArgumentException>(() => set.GetInputs(new NodeHandle()));
                Assert.Throws<ArgumentException>(() => set.GetOutputs(new NodeHandle()));

            }
        }

        [Test]
        public void InputOutputCountsAreCorrect()
        {
            using (var set = new NodeSet())
            {
                var empty = set.Create<EmptyNode>();
                var one = set.Create<OneInOutNode>();

                Assert.AreEqual(0, set.GetInputs(empty).Count);
                Assert.AreEqual(0, set.GetInputs(empty).Count);

                Assert.AreEqual(0, set.GetFunctionality(empty).GetPortDescription(empty).Inputs.Count);
                Assert.AreEqual(0, set.GetFunctionality(empty).GetPortDescription(empty).Outputs.Count);

                Assert.AreEqual(1, set.GetInputs(one).Count);
                Assert.AreEqual(1, set.GetInputs(one).Count);

                Assert.AreEqual(1, set.GetFunctionality(one).GetPortDescription(one).Inputs.Count);
                Assert.AreEqual(1, set.GetFunctionality(one).GetPortDescription(one).Outputs.Count);

                set.Destroy(empty, one);
            }
        }

        [Test]
        public void EmptyPortsInputsOutputsIsCorrect()
        {
            using (var set = new NodeSet())
            {
                var one = set.Create<OneInOutNode>();

                foreach (var port in set.GetInputs(one))
                    Assert.AreEqual(0, port.Count);

                foreach (var port in set.GetOutputs(one))
                    Assert.AreEqual(0, port.Count);

                set.Destroy(one);
            }
        }

        [Test]
        public void CanWalkNodesThroughMessageConnections()
        {
            using (var set = new NodeSet())
            {
                NodeHandle<OneInOutNode>
                    a = set.Create<OneInOutNode>(),
                    b = set.Create<OneInOutNode>();

                set.Connect(a, OneInOutNode.SimulationPorts.Output, b, OneInOutNode.SimulationPorts.Input);

                Assert.AreEqual((NodeHandle)b, set.GetOutputs(a)[OneInOutNode.SimulationPorts.Output.Port, 0]);
                Assert.AreEqual((NodeHandle)a, set.GetInputs(b)[OneInOutNode.SimulationPorts.Input.Port, 0]);

                set.Destroy(a, b);
            }
        }

        [Test]
        public void CanChainWalk_ForwardsAndBackwards()
        {
            using (var set = new NodeSet())
            {
                NodeHandle<OneInOutNode>
                    a = set.Create<OneInOutNode>(),
                    b = set.Create<OneInOutNode>(),
                    c = set.Create<OneInOutNode>();

                set.Connect(b, OneInOutNode.SimulationPorts.Output, a, OneInOutNode.SimulationPorts.Input);
                set.Connect(c, OneInOutNode.SimulationPorts.Output, b, OneInOutNode.SimulationPorts.Input);

                Assert.AreEqual((NodeHandle)a, set.GetOutputs(set.GetOutputs(c)[OneInOutNode.SimulationPorts.Output.Port, 0])[OneInOutNode.SimulationPorts.Output.Port, 0]);
                Assert.AreEqual((NodeHandle)c, set.GetInputs(set.GetInputs(a)[OneInOutNode.SimulationPorts.Input.Port, 0])[OneInOutNode.SimulationPorts.Input.Port, 0]);

                set.Destroy(a, b, c);
            }

        }

        // TODO: @wayne needs to annotate what these tests do, I just ported them
        [Test]
        public void RandomTopologyTest1()
        {
            using (var set = new NodeSet())
            {
                NodeHandle<OneInOutNode>
                    a = set.Create<OneInOutNode>(),
                    b = set.Create<OneInOutNode>(),
                    c = set.Create<OneInOutNode>();

                set.Connect(b, OneInOutNode.SimulationPorts.Output, a, OneInOutNode.SimulationPorts.Input);
                set.Connect(c, OneInOutNode.SimulationPorts.Output, a, OneInOutNode.SimulationPorts.Input);

                var port = set.GetInputs(a)[OneInOutNode.SimulationPorts.Input.Port];

                foreach (var node in port)
                {
                    Assert.IsTrue(node == b || node == c);
                }

                Assert.AreEqual((NodeHandle)a, set.GetOutputs(c)[OneInOutNode.SimulationPorts.Output.Port, 0]);
                Assert.AreEqual((NodeHandle)a, set.GetOutputs(b)[OneInOutNode.SimulationPorts.Output.Port, 0]);

                set.Destroy(a, b, c);
            }

        }

        // TODO: @wayne needs to annotate what these tests do, I just ported them
        [Test]
        public void RandomTopologyTest2()
        {
            using (var set = new NodeSet())
            {
                NodeHandle<ThreeInOutNode>
                    a = set.Create<ThreeInOutNode>();
                NodeHandle<OneInOutNode>
                    b = set.Create<OneInOutNode>(),
                    c = set.Create<OneInOutNode>();

                set.Connect(b, OneInOutNode.SimulationPorts.Output, a, ThreeInOutNode.SimulationPorts.Input1);
                set.Connect(c, OneInOutNode.SimulationPorts.Output, a, ThreeInOutNode.SimulationPorts.Input3);

                Assert.AreEqual((NodeHandle)b, set.GetInputs(a)[ThreeInOutNode.SimulationPorts.Input1.Port, 0]);
                Assert.AreEqual(0, set.GetInputs(a)[ThreeInOutNode.SimulationPorts.Input2.Port].Count);
                Assert.AreEqual((NodeHandle)c, set.GetInputs(a)[ThreeInOutNode.SimulationPorts.Input3.Port, 0]);

                Assert.AreEqual((NodeHandle)a, set.GetOutputs(c)[OneInOutNode.SimulationPorts.Output.Port, 0]);
                Assert.AreEqual((NodeHandle)a, set.GetOutputs(b)[OneInOutNode.SimulationPorts.Output.Port, 0]);

                set.Destroy(a, b, c);
            }
        }

        [Test]
        public void CanDisconnectTopologyNodes()
        {
            using (var set = new NodeSet())
            {
                NodeHandle<OneInOutNode>
                    a = set.Create<OneInOutNode>(),
                    b = set.Create<OneInOutNode>();

                Assert.AreEqual(0, set.GetOutputs(a)[OneInOutNode.SimulationPorts.Output.Port].Count);
                Assert.AreEqual(0, set.GetInputs(b)[OneInOutNode.SimulationPorts.Input.Port].Count);

                set.Connect(a, OneInOutNode.SimulationPorts.Output, b, OneInOutNode.SimulationPorts.Input);

                Assert.AreEqual(1, set.GetOutputs(a)[OneInOutNode.SimulationPorts.Output.Port].Count);
                Assert.AreEqual(1, set.GetInputs(b)[OneInOutNode.SimulationPorts.Input.Port].Count);

                set.Disconnect(a, OneInOutNode.SimulationPorts.Output, b, OneInOutNode.SimulationPorts.Input);

                Assert.AreEqual(0, set.GetOutputs(a)[OneInOutNode.SimulationPorts.Output.Port].Count);
                Assert.AreEqual(0, set.GetInputs(b)[OneInOutNode.SimulationPorts.Input.Port].Count);

                set.Destroy(a, b);
            }

        }

        [Test]
        public void CanDisconnectAllTopologyNodes()
        {
            using (var set = new NodeSet())
            {
                NodeHandle
                    a = set.Create<ThreeInOutNode>(),
                    b = set.Create<ThreeInOutNode>(),
                    c = set.Create<ThreeInOutNode>();

                for (int i = 0; i < 3; ++i)
                {
                    OutputPortID outPort = set.GetFunctionality(a).GetPortDescription(a).Outputs[i];
                    InputPortID inPort = set.GetFunctionality(a).GetPortDescription(a).Inputs[i];

                    set.Connect(b, outPort, a, inPort);
                    set.Connect(c, outPort, b, inPort);

                    Assert.AreEqual(0, set.GetOutputs(a).Connections(outPort));
                    Assert.AreEqual(1, set.GetInputs(a).Connections(inPort));
                    Assert.AreEqual(1, set.GetOutputs(b).Connections(outPort));
                    Assert.AreEqual(1, set.GetInputs(b).Connections(inPort));
                    Assert.AreEqual(1, set.GetOutputs(c).Connections(outPort));
                    Assert.AreEqual(0, set.GetInputs(c).Connections(inPort));
                }

                set.DisconnectAll(b);

                foreach (var node in new[] { a, b, c })
                {
                    var inputs = set.GetInputs(node);
                    var inputPortCount = inputs.Count;
                    for (int i = 0; i < inputPortCount; ++i)
                        Assert.AreEqual(0, inputs.Connections(set.GetFunctionality(node).GetPortDescription(node).Inputs[i]));

                    var outputs = set.GetOutputs(node);
                    var outputPortCount = outputs.Count;
                    for (int i = 0; i < outputPortCount; ++i)
                        Assert.AreEqual(0, outputs.Connections(set.GetFunctionality(node).GetPortDescription(node).Outputs[i]));
                }

                set.Destroy(a, b, c);
            }

        }

        [Test]
        public void CanCreateDirectedCyclicGraph_AndWalkInCircles()
        {
            using (var set = new NodeSet())
            {
                NodeHandle<OneInOutNode>
                    at = set.Create<OneInOutNode>(),
                    bt = set.Create<OneInOutNode>();

                set.Connect(at, OneInOutNode.SimulationPorts.Output, bt, OneInOutNode.SimulationPorts.Input);
                set.Connect(bt, OneInOutNode.SimulationPorts.Output, at, OneInOutNode.SimulationPorts.Input);

                NodeHandle a = at, b = bt;

                for (int i = 0; i < 100; ++i)
                {
                    var temp = set.GetOutputs(a)[OneInOutNode.SimulationPorts.Output.Port, 0];
                    Assert.AreEqual(b, temp);
                    Assert.AreEqual(a, set.GetInputs(b)[OneInOutNode.SimulationPorts.Input.Port, 0]);
                    b = a;
                    a = temp;
                }

                set.Destroy(a, b);
            }
        }

        [Test]
        public void CanConnectTwoEdges_ToOnePort()
        {
            using (var set = new NodeSet())
            {
                NodeHandle<OneInOutNode>
                    a = set.Create<OneInOutNode>(),
                    b = set.Create<OneInOutNode>(),
                    c = set.Create<OneInOutNode>();


                set.Connect(a, OneInOutNode.SimulationPorts.Output, b, OneInOutNode.SimulationPorts.Input);
                set.Connect(a, OneInOutNode.SimulationPorts.Output, c, OneInOutNode.SimulationPorts.Input);

                // TODO: Fix: Topology is not stable with regards to insertion order.

                Assert.AreEqual((NodeHandle)c, set.GetOutputs(a)[OneInOutNode.SimulationPorts.Output.Port, 0]);
                Assert.AreEqual((NodeHandle)b, set.GetOutputs(a)[OneInOutNode.SimulationPorts.Output.Port, 1]);

                Assert.AreEqual((NodeHandle)c, set.GetOutputs(a)[OneInOutNode.SimulationPorts.Output.Port][0]);
                Assert.AreEqual((NodeHandle)b, set.GetOutputs(a)[OneInOutNode.SimulationPorts.Output.Port][1]);


                set.Destroy(a, b, c);
            }
        }

    }
}
