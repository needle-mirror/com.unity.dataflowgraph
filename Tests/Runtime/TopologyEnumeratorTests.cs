using System;
using NUnit.Framework;
using Unity.Collections;

namespace Unity.DataFlowGraph.Tests
{
    using TopologyAPI = TopologyAPI<Node, InputPort, OutputPort>;
    using Database = TopologyAPI<Node, InputPort, OutputPort>.Database;

    partial struct TopologyTestDatabase : IDisposable
    {
        public Database.InputTopologyEnumerable GetInputs(Node source) =>
            Connections.GetInputs(Nodes[source]);
        public Database.OutputTopologyEnumerable GetOutputs(Node source) =>
            Connections.GetOutputs(Nodes[source]);
    }

    internal static class TopologyExt
    {
        internal static int Count<TMoveable>(this TMoveable walker)
            where TMoveable : Database.IMoveable
        {
            int count = 0;
            while (walker.MoveNext())
                count++;

            return count;
        }

        public static Node GetNthConnection(this Database.OutputTopologyEnumerable e, OutputPort port, int index)
        {
            var currIndex = 0;

            foreach (var filtered in e[port])
            {
                if (currIndex == index)
                    return filtered;

                currIndex++;
            }

            throw new IndexOutOfRangeException("Index of connection or port does not exist");
        }

        public static Node GetNthConnection(this Database.InputTopologyEnumerable e, InputPort port, int index)
        {
            var currIndex = 0;

            foreach (var filtered in e[port])
            {
                if (currIndex == index)
                    return filtered;

                currIndex++;
            }

            throw new IndexOutOfRangeException("Index of connection or port does not exist");
        }
    }

    public class TopologyEnumeratorTests
    {
        static readonly InputPort[] k_InputPorts =
            { new InputPort(234), new InputPort(345), new InputPort(456) };
        static readonly OutputPort[] k_OutputPorts =
            { new OutputPort(567), new OutputPort(678), new OutputPort(789) };

        [Test]
        public void GetInputsOutputs_WorksFor_ActualNode()
        {
            using (var topoDB = new TopologyTestDatabase(Allocator.Persistent))
            {
                var node = topoDB.CreateNode();

                topoDB.GetInputs(node);
                topoDB.GetOutputs(node);
            }
        }

        [Test]
        public void InputOutputCountsAreCorrect()
        {
            using (var topoDB = new TopologyTestDatabase(Allocator.Persistent))
            {
                var node = topoDB.CreateNode();

                Assert.AreEqual(0, topoDB.GetInputs(node).GetEnumerator().Count());
                Assert.AreEqual(0, topoDB.GetOutputs(node).GetEnumerator().Count());
            }
        }

        [Test]
        public void CanWalkNodesThroughConnections()
        {
            using (var topoDB = new TopologyTestDatabase(Allocator.Persistent))
            {
                var a = topoDB.CreateNode();
                var b = topoDB.CreateNode();

                topoDB.Connect(a, k_OutputPorts[0], b, k_InputPorts[0]);

                Assert.AreEqual(b, topoDB.GetOutputs(a).GetNthConnection(k_OutputPorts[0], 0));
                Assert.AreEqual(a, topoDB.GetInputs(b).GetNthConnection(k_InputPorts[0], 0));
            }
        }

        [Test]
        public void CanChainWalk_ForwardsAndBackwards()
        {
            using (var topoDB = new TopologyTestDatabase(Allocator.Persistent))
            {
                var a = topoDB.CreateNode();
                var b = topoDB.CreateNode();
                var c = topoDB.CreateNode();

                topoDB.Connect(b, k_OutputPorts[0], a, k_InputPorts[0]);
                topoDB.Connect(c, k_OutputPorts[0], b, k_InputPorts[0]);

                Assert.AreEqual(
                    a,
                    topoDB.GetOutputs(topoDB.GetOutputs(c).GetNthConnection(k_OutputPorts[0], 0))
                        .GetNthConnection(k_OutputPorts[0], 0)
                );

                Assert.AreEqual(
                    c,
                    topoDB.GetInputs(topoDB.GetInputs(a).GetNthConnection(k_InputPorts[0], 0))
                        .GetNthConnection(k_InputPorts[0], 0)
                );
            }
        }

        [Test] public void CanWalk_InputsAndOutputs_ForDoublyConnectedInput()
        {
            using (var topoDB = new TopologyTestDatabase(Allocator.Persistent))
            {
                var a = topoDB.CreateNode();
                var b = topoDB.CreateNode();
                var c = topoDB.CreateNode();

                topoDB.Connect(b, k_OutputPorts[0], a, k_InputPorts[0]);
                topoDB.Connect(c, k_OutputPorts[0], a, k_InputPorts[0]);

                var filteredPorts = topoDB.GetInputs(a)[k_InputPorts[0]];

                foreach (var node in filteredPorts)
                    Assert.That(node, Is.EqualTo(b).Or.EqualTo(c));

                Assert.AreEqual(a, topoDB.GetOutputs(c).GetNthConnection(k_OutputPorts[0], 0));
                Assert.AreEqual(a, topoDB.GetOutputs(b).GetNthConnection(k_OutputPorts[0], 0));
            }
        }

        [Test]
        public void CanWalk_InputsAndOutputs_ForSinglyConnected()
        {
            using (var topoDB = new TopologyTestDatabase(Allocator.Persistent))
            {
                var a = topoDB.CreateNode();
                var b = topoDB.CreateNode();
                var c = topoDB.CreateNode();

                topoDB.Connect(b, k_OutputPorts[0], a, k_InputPorts[0]);
                topoDB.Connect(c, k_OutputPorts[0], a, k_InputPorts[2]);

                Assert.AreEqual(b, topoDB.GetInputs(a).GetNthConnection(k_InputPorts[0], 0));
                Assert.AreEqual(0, topoDB.GetInputs(a)[k_InputPorts[1]].Count());
                Assert.AreEqual(c, topoDB.GetInputs(a).GetNthConnection(k_InputPorts[2], 0));

                Assert.AreEqual(a, topoDB.GetOutputs(c).GetNthConnection(k_OutputPorts[0], 0));
                Assert.AreEqual(a, topoDB.GetOutputs(b).GetNthConnection(k_OutputPorts[0], 0));
            }
        }

        [Test]
        public void CanDisconnectTopologyNodes()
        {
            using (var topoDB = new TopologyTestDatabase(Allocator.Persistent))
            {
                var a = topoDB.CreateNode();
                var b = topoDB.CreateNode();

                Assert.AreEqual(0, topoDB.GetOutputs(a)[k_OutputPorts[0]].Count());
                Assert.AreEqual(0, topoDB.GetInputs(b)[k_InputPorts[0]].Count());

                topoDB.Connect(a, k_OutputPorts[0], b, k_InputPorts[0]);

                Assert.AreEqual(1, topoDB.GetOutputs(a)[k_OutputPorts[0]].Count());
                Assert.AreEqual(1, topoDB.GetInputs(b)[k_InputPorts[0]].Count());

                topoDB.Disconnect(a, k_OutputPorts[0], b, k_InputPorts[0]);

                Assert.AreEqual(0, topoDB.GetOutputs(a)[k_OutputPorts[0]].Count());
                Assert.AreEqual(0, topoDB.GetInputs(b)[k_InputPorts[0]].Count());
            }
        }

        [Test]
        public void CanDisconnectAllTopologyNodes()
        {
            using (var topoDB = new TopologyTestDatabase(Allocator.Persistent))
            {
                var a = topoDB.CreateNode();
                var b = topoDB.CreateNode();
                var c = topoDB.CreateNode();

                for (int i = 0; i < 3; ++i)
                {
                    OutputPort outPort = k_OutputPorts[i];
                    InputPort inPort = k_InputPorts[i];

                    topoDB.Connect(b, outPort, a, inPort);
                    topoDB.Connect(c, outPort, b, inPort);

                    Assert.AreEqual(0, topoDB.GetOutputs(a)[outPort].Count());
                    Assert.AreEqual(1, topoDB.GetInputs(a)[inPort].Count());
                    Assert.AreEqual(1, topoDB.GetOutputs(b)[outPort].Count());
                    Assert.AreEqual(1, topoDB.GetInputs(b)[inPort].Count());
                    Assert.AreEqual(1, topoDB.GetOutputs(c)[outPort].Count());
                    Assert.AreEqual(0, topoDB.GetInputs(c)[inPort].Count());
                }

                topoDB.DisconnectAll(b);

                foreach (var node in new[] {a, b, c})
                {
                    var inputs = topoDB.GetInputs(node);
                    var inputPortCount = inputs.GetEnumerator().Count();
                    for (int i = 0; i < inputPortCount; ++i)
                        Assert.AreEqual(0, inputs[k_InputPorts[i]].Count());

                    var outputs = topoDB.GetOutputs(node);
                    var outputPortCount = outputs.GetEnumerator().Count();
                    for (int i = 0; i < outputPortCount; ++i)
                        Assert.AreEqual(0, outputs[k_OutputPorts[i]].Count());
                }
            }
        }

        [Test]
        public void CanCreateDirectedCyclicGraph_AndWalkInCircles()
        {
            using (var topoDB = new TopologyTestDatabase(Allocator.Persistent))
            {
                var a = topoDB.CreateNode();
                var b = topoDB.CreateNode();

                topoDB.Connect(a, k_OutputPorts[0], b, k_InputPorts[0]);
                topoDB.Connect(b, k_OutputPorts[0], a, k_InputPorts[0]);

                for (int i = 0; i < 100; ++i)
                {
                    var temp = topoDB.GetOutputs(a).GetNthConnection(k_OutputPorts[0], 0);
                    Assert.AreEqual(b, temp);
                    Assert.AreEqual(a, topoDB.GetInputs(b).GetNthConnection(k_InputPorts[0], 0));
                    b = a;
                    a = temp;
                }
            }
        }

        [Test]
        public void CanConnectTwoEdges_ToOnePort()
        {
            using (var topoDB = new TopologyTestDatabase(Allocator.Persistent))
            {
                var a = topoDB.CreateNode();
                var b = topoDB.CreateNode();
                var c = topoDB.CreateNode();

                topoDB.Connect(a, k_OutputPorts[0], b, k_InputPorts[0]);
                topoDB.Connect(a, k_OutputPorts[0], c, k_InputPorts[0]);

                // TODO: Fix: Topology is not stable with regards to insertion order.

                Assert.AreEqual(c, topoDB.GetOutputs(a).GetNthConnection(k_OutputPorts[0], 0));
                Assert.AreEqual(b, topoDB.GetOutputs(a).GetNthConnection(k_OutputPorts[0], 1));

                var byPort = topoDB.GetOutputs(a)[k_OutputPorts[0]];
                byPort.MoveNext();
                Assert.AreEqual(c, byPort.Current);
                byPort.MoveNext();
                Assert.AreEqual(b, byPort.Current);
            }
        }
    }
}
