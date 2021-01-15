using System;
using NUnit.Framework;
using Unity.Collections;
namespace Unity.DataFlowGraph.Tests
{
    using Topology = TopologyAPI<ValidatedHandle, InputPortArrayID, OutputPortArrayID>;

    internal static class TopologyExtensions
    {
        internal static ValidatedHandle Validate(this NodeSet set, NodeHandle h)
            => set.Nodes.Validate(h.VHandle);

        internal static int Count<TMoveable>(this TMoveable walker)
            where TMoveable : Topology.Database.IMoveable
        {
            int count = 0;
            while (walker.MoveNext())
                count++;

            return count;
        }

        public static ValidatedHandle GetNthConnection(this Topology.Database.OutputTopologyEnumerable e, OutputPortID port, int index)
        {
            OutputPortArrayID id = new OutputPortArrayID(port);
            var currIndex = 0;

            foreach (var filtered in e[id])
            {
                if (currIndex == index)
                    return filtered;

                currIndex++;
            }

            throw new IndexOutOfRangeException("Index of connection or port does not exist");
        }

        public static ValidatedHandle GetNthConnection(this Topology.Database.InputTopologyEnumerable e, InputPortID port, int index)
        {
            InputPortArrayID id = new InputPortArrayID(port);
            var currIndex = 0;

            foreach (var filtered in e[id])
            {
                if (currIndex == index)
                    return filtered;

                currIndex++;
            }

            throw new IndexOutOfRangeException("Index of connection or port does not exist");
        }
    }

    public class TopologyWalkerTests
    {
        public class OneInOutNode : SimulationNodeDefinition<OneInOutNode.SimPorts>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
                public MessageInput<OneInOutNode, Message> Input;
                public MessageOutput<OneInOutNode, Message> Output;
            }

            struct Node : INodeData, IMsgHandler<Message>
            {
                public void HandleMessage(MessageContext ctx, in Message msg)
                {
                    throw new NotImplementedException();
                }
            }
        }

        public class ThreeInOutNode : SimulationNodeDefinition<ThreeInOutNode.SimPorts>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
                public MessageInput<ThreeInOutNode, Message> Input1, Input2, Input3;
                public MessageOutput<ThreeInOutNode, Message> Output1, Output2, Output3;
            }

            struct Node : INodeData, IMsgHandler<Message>
            {
                public void HandleMessage(MessageContext ctx, in Message msg)
                {
                    throw new NotImplementedException();
                }
            }
        }

        [Test]
        public void GetInputsOutputs_WorksFor_ActualNode()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<OneInOutNode>();

                set.GetInputs(set.Validate(node));
                set.GetOutputs(set.Validate(node));

                set.Destroy(node);
            }
        }

        [Test]
        public void GetInputsOutputs_ThrowsExceptionOn_DestroyedNode()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<OneInOutNode>();
                set.Destroy(node);

                Assert.Throws<ArgumentException>(() => set.GetInputs(set.Validate(node)));
                Assert.Throws<ArgumentException>(() => set.GetOutputs(set.Validate(node)));

            }
        }

        [Test]
        public void InputOutputCountsAreCorrect()
        {
            using (var set = new NodeSet())
            {
                var empty = set.Create<EmptyNode>();
                var one = set.Create<OneInOutNode>();

                Assert.AreEqual(0, set.GetInputs(set.Validate(empty)).GetEnumerator().Count());
                Assert.AreEqual(0, set.GetOutputs(set.Validate(empty)).GetEnumerator().Count());

                Assert.AreEqual(0, set.GetDefinition(empty).GetPortDescription(empty).Inputs.Count);
                Assert.AreEqual(0, set.GetDefinition(empty).GetPortDescription(empty).Outputs.Count);

                Assert.AreEqual(0, set.GetInputs(set.Validate(one)).GetEnumerator().Count());
                Assert.AreEqual(0, set.GetOutputs(set.Validate(one)).GetEnumerator().Count());

                Assert.AreEqual(1, set.GetDefinition(one).GetPortDescription(one).Inputs.Count);
                Assert.AreEqual(1, set.GetDefinition(one).GetPortDescription(one).Outputs.Count);

                set.Destroy(empty, one);
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

                Assert.AreEqual(set.Validate(b), set.GetOutputs(set.Validate(a)).GetNthConnection(OneInOutNode.SimulationPorts.Output.Port, 0));
                Assert.AreEqual(set.Validate(a), set.GetInputs(set.Validate(b)).GetNthConnection(OneInOutNode.SimulationPorts.Input.Port, 0));

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

                Assert.AreEqual(set.Validate(a),
                    set.GetOutputs(
                        set.GetOutputs(set.Validate(c)).GetNthConnection(OneInOutNode.SimulationPorts.Output.Port, 0)
                    ).GetNthConnection(OneInOutNode.SimulationPorts.Output.Port, 0)
                );

                Assert.AreEqual(set.Validate(c),
                    set.GetInputs(
                        set.GetInputs(set.Validate(a)).GetNthConnection(OneInOutNode.SimulationPorts.Input.Port, 0)
                    ).GetNthConnection(OneInOutNode.SimulationPorts.Input.Port, 0)
                );

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

                var filteredPorts = set.GetInputs(set.Validate(a))[new InputPortArrayID(OneInOutNode.SimulationPorts.Input.Port)];

                foreach (var node in filteredPorts)
                {
                    Assert.IsTrue(node.ToPublicHandle() == b || node.ToPublicHandle() == c);
                }

                Assert.AreEqual(set.Validate(a), set.GetOutputs(set.Validate(c)).GetNthConnection(OneInOutNode.SimulationPorts.Output.Port, 0));
                Assert.AreEqual(set.Validate(a), set.GetOutputs(set.Validate(b)).GetNthConnection(OneInOutNode.SimulationPorts.Output.Port, 0));

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

                Assert.AreEqual(set.Validate(b), set.GetInputs(set.Validate(a)).GetNthConnection(ThreeInOutNode.SimulationPorts.Input1.Port, 0));
                Assert.AreEqual(0, set.GetInputs(set.Validate(a))[new InputPortArrayID(ThreeInOutNode.SimulationPorts.Input2.Port)].Count());
                Assert.AreEqual(set.Validate(c), set.GetInputs(set.Validate(a)).GetNthConnection(ThreeInOutNode.SimulationPorts.Input3.Port, 0));

                Assert.AreEqual(set.Validate(a), set.GetOutputs(set.Validate(c)).GetNthConnection(OneInOutNode.SimulationPorts.Output.Port, 0));
                Assert.AreEqual(set.Validate(a), set.GetOutputs(set.Validate(b)).GetNthConnection(OneInOutNode.SimulationPorts.Output.Port, 0));

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

                Assert.AreEqual(0, set.GetOutputs(set.Validate(a))[new OutputPortArrayID(OneInOutNode.SimulationPorts.Output.Port)].Count());
                Assert.AreEqual(0, set.GetInputs(set.Validate(b))[new InputPortArrayID(OneInOutNode.SimulationPorts.Input.Port)].Count());

                set.Connect(a, OneInOutNode.SimulationPorts.Output, b, OneInOutNode.SimulationPorts.Input);

                Assert.AreEqual(1, set.GetOutputs(set.Validate(a))[new OutputPortArrayID(OneInOutNode.SimulationPorts.Output.Port)].Count());
                Assert.AreEqual(1, set.GetInputs(set.Validate(b))[new InputPortArrayID(OneInOutNode.SimulationPorts.Input.Port)].Count());

                set.Disconnect(a, OneInOutNode.SimulationPorts.Output, b, OneInOutNode.SimulationPorts.Input);

                Assert.AreEqual(0, set.GetOutputs(set.Validate(a))[new OutputPortArrayID(OneInOutNode.SimulationPorts.Output.Port)].Count());
                Assert.AreEqual(0, set.GetInputs(set.Validate(b))[new InputPortArrayID(OneInOutNode.SimulationPorts.Input.Port)].Count());

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
                    OutputPortID outPort = set.GetDefinition(a).GetPortDescription(a).Outputs[i];
                    InputPortID inPort = set.GetDefinition(a).GetPortDescription(a).Inputs[i];

                    set.Connect(b, outPort, a, inPort);
                    set.Connect(c, outPort, b, inPort);

                    Assert.AreEqual(0, set.GetOutputs(set.Validate(a))[new OutputPortArrayID(outPort)].Count());
                    Assert.AreEqual(1, set.GetInputs(set.Validate(a))[new InputPortArrayID(inPort)].Count());
                    Assert.AreEqual(1, set.GetOutputs(set.Validate(b))[new OutputPortArrayID(outPort)].Count());
                    Assert.AreEqual(1, set.GetInputs(set.Validate(b))[new InputPortArrayID(inPort)].Count());
                    Assert.AreEqual(1, set.GetOutputs(set.Validate(c))[new OutputPortArrayID(outPort)].Count());
                    Assert.AreEqual(0, set.GetInputs(set.Validate(c))[new InputPortArrayID(inPort)].Count());
                }

                set.DisconnectAll(b);

                foreach (var node in new[] { a, b, c })
                {
                    var inputs = set.GetInputs(set.Validate(node));
                    var inputPortCount = inputs.GetEnumerator().Count();
                    for (int i = 0; i < inputPortCount; ++i)
                        Assert.AreEqual(0, inputs[new InputPortArrayID(set.GetDefinition(node).GetPortDescription(node).Inputs[i])].Count());

                    var outputs = set.GetOutputs(set.Validate(node));
                    var outputPortCount = outputs.GetEnumerator().Count();
                    for (int i = 0; i < outputPortCount; ++i)
                        Assert.AreEqual(0, outputs[new OutputPortArrayID(set.GetDefinition(node).GetPortDescription(node).Outputs[i])].Count());
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
                    var temp = set.GetOutputs(set.Validate(a)).GetNthConnection(OneInOutNode.SimulationPorts.Output.Port, 0);
                    Assert.AreEqual(set.Validate(b), temp);
                    Assert.AreEqual(set.Validate(a), set.GetInputs(set.Validate(b)).GetNthConnection(OneInOutNode.SimulationPorts.Input.Port, 0));
                    b = a;
                    a = temp.ToPublicHandle();
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

                Assert.AreEqual(set.Validate(c), set.GetOutputs(set.Validate(a)).GetNthConnection(OneInOutNode.SimulationPorts.Output.Port, 0));
                Assert.AreEqual(set.Validate(b), set.GetOutputs(set.Validate(a)).GetNthConnection(OneInOutNode.SimulationPorts.Output.Port, 1));

                var byPort = set.GetOutputs(set.Validate(a))[new OutputPortArrayID(OneInOutNode.SimulationPorts.Output.Port)];
                byPort.MoveNext();
                Assert.AreEqual(set.Validate(c), byPort.Current);
                byPort.MoveNext();
                Assert.AreEqual(set.Validate(b), byPort.Current);


                set.Destroy(a, b, c);
            }
        }

    }
}
