using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Unity.DataFlowGraph.Tests
{
    public class FeedbackTests
    {
        [Test]
        public void CanCreate_FeedbackConnection()
        {
            using (var set = new NodeSet())
            {
                var node1 = set.Create<KernelAdderNode>();
                var node2 = set.Create<KernelAdderNode>();

                set.Connect(node1, KernelAdderNode.KernelPorts.Output, node2, KernelAdderNode.KernelPorts.Input, NodeSet.ConnectionType.Feedback);

                set.Destroy(node1, node2);
            }
        }

        [Test]
        public void CanDisconnect_FeedbackConnection()
        {
            using (var set = new NodeSet())
            {
                var node1 = set.Create<KernelAdderNode>();
                var node2 = set.Create<KernelAdderNode>();

                set.Connect(node1, KernelAdderNode.KernelPorts.Output, node2, KernelAdderNode.KernelPorts.Input, NodeSet.ConnectionType.Feedback);
                set.Disconnect(node1, KernelAdderNode.KernelPorts.Output, node2, KernelAdderNode.KernelPorts.Input);

                set.Destroy(node1, node2);
            }
        }

        [Test]
        public void CannotCreate_FeedbackConnection_AndRegularConnection()
        {
            using (var set = new NodeSet())
            {
                var node1 = set.Create<KernelAdderNode>();
                var node2 = set.Create<KernelAdderNode>();

                set.Connect(node1, KernelAdderNode.KernelPorts.Output, node2, KernelAdderNode.KernelPorts.Input, NodeSet.ConnectionType.Feedback);
                Assert.Throws<ArgumentException>(() => set.Connect(node1, KernelAdderNode.KernelPorts.Output, node2, KernelAdderNode.KernelPorts.Input));

                set.Destroy(node1, node2);
            }
        }

        [Test]
        public void CanCreate_MultipleFeedbackConnections_BetweenTwoNodes()
        {
            using (var set = new NodeSet())
            {
                var node1 = set.Create<NodeWithAllTypesOfPorts>();
                var node2 = set.Create<NodeWithAllTypesOfPorts>();

                set.Connect(node1, NodeWithAllTypesOfPorts.KernelPorts.OutputScalar, node2, NodeWithAllTypesOfPorts.KernelPorts.InputScalar, NodeSet.ConnectionType.Feedback);
                set.Connect(node1, NodeWithAllTypesOfPorts.KernelPorts.OutputBuffer, node2, NodeWithAllTypesOfPorts.KernelPorts.InputBuffer, NodeSet.ConnectionType.Feedback);
                set.Disconnect(node1, NodeWithAllTypesOfPorts.KernelPorts.OutputScalar, node2, NodeWithAllTypesOfPorts.KernelPorts.InputScalar);
                set.Disconnect(node1, NodeWithAllTypesOfPorts.KernelPorts.OutputBuffer, node2, NodeWithAllTypesOfPorts.KernelPorts.InputBuffer);

                set.Destroy(node1, node2);
            }
        }

        [Test]
        public void FeedbackTraversalOrder_IsCoherent([Values] NodeSet.RenderExecutionModel model)
        {
            using (var set = new NodeSet())
            {
                set.RendererModel = model;

                var node1 = set.Create<KernelAdderNode>();
                var node2 = set.Create<KernelAdderNode>();
                GraphValue<int> node1GV = set.CreateGraphValue(node1, KernelAdderNode.KernelPorts.Output);
                GraphValue<int> node2GV = set.CreateGraphValue(node2, KernelAdderNode.KernelPorts.Output);

                set.Connect(node1, KernelAdderNode.KernelPorts.Output, node2, KernelAdderNode.KernelPorts.Input, NodeSet.ConnectionType.Feedback);

                for (int i = 0; i < 10; ++i)
                {
                    set.SetData(node1, KernelAdderNode.KernelPorts.Input, i);
                    set.Update();
                    Assert.AreEqual(i+1, set.GetValueBlocking(node1GV));
                    Assert.AreEqual(i+1, set.GetValueBlocking(node2GV));
                }

                set.ReleaseGraphValue(node1GV);
                set.ReleaseGraphValue(node2GV);
                set.Destroy(node1, node2);
            }
        }

        [Test, Explicit] // Does not work due to issue #331. Do we even want it to work?
        public void SingleNodeFeedbackLoop_Works([Values] NodeSet.RenderExecutionModel model)
        {
            using (var set = new NodeSet())
            {
                set.RendererModel = model;

                var node = set.Create<KernelAdderNode>();
                GraphValue<int> nodeGV = set.CreateGraphValue(node, KernelAdderNode.KernelPorts.Output);

                set.Connect(node, KernelAdderNode.KernelPorts.Output, node, KernelAdderNode.KernelPorts.Input, NodeSet.ConnectionType.Feedback);

                for (int i = 0; i < 10; ++i)
                {
                    set.Update();
                    Assert.AreEqual(i+1, set.GetValueBlocking(nodeGV));
                }

                set.ReleaseGraphValue(nodeGV);
                set.Destroy(node);
            }
        }

        [Test]
        public void TwoNodeFeedbackLoop_Works([Values] NodeSet.RenderExecutionModel model)
        {
            using (var set = new NodeSet())
            {
                set.RendererModel = model;

                var node1 = set.Create<KernelAdderNode>();
                var node2 = set.Create<KernelAdderNode>();
                GraphValue<int> node1GV = set.CreateGraphValue(node1, KernelAdderNode.KernelPorts.Output);
                GraphValue<int> node2GV = set.CreateGraphValue(node2, KernelAdderNode.KernelPorts.Output);

                set.Connect(node1, KernelAdderNode.KernelPorts.Output, node2, KernelAdderNode.KernelPorts.Input);
                set.Connect(node2, KernelAdderNode.KernelPorts.Output, node1, KernelAdderNode.KernelPorts.Input, NodeSet.ConnectionType.Feedback);

                // After the first update, we expect GVs 1,2. On the next update, the 2 should be fedback into node1, so we expect 3,4. Next, 5,6...
                for (int i = 0; i < 10; ++i)
                {
                    set.Update();
                    Assert.AreEqual(i*2+1, set.GetValueBlocking(node1GV));
                    Assert.AreEqual(i*2+2, set.GetValueBlocking(node2GV));
                }

                set.ReleaseGraphValue(node1GV);
                set.ReleaseGraphValue(node2GV);
                set.Destroy(node1, node2);
            }
        }

        [Test]
        public void NthOrderFeedbackSystem_Works([Values] NodeSet.RenderExecutionModel model, [Values(2,5,100)] int numNodes)
        {
            using (var set = new NodeSet())
            {
                set.RendererModel = model;

                var nodes = new List<NodeHandle<KernelSumNode>>();
                var nodeGVs = new List<GraphValue<ECSInt>>();
                var expected = new List<ECSInt>();

                nodes.Add(set.Create<KernelSumNode>());
                nodeGVs.Add(set.CreateGraphValue(nodes[0], KernelSumNode.KernelPorts.Output));
                expected.Add(1);
                for (int i = 1; i < numNodes; ++i)
                {
                    var node = set.Create<KernelSumNode>();
                    nodeGVs.Add(set.CreateGraphValue(node, KernelSumNode.KernelPorts.Output));
                    expected.Add(1);
                    nodes.Add(node);
                    set.SetPortArraySize(node, KernelSumNode.KernelPorts.Inputs, 1);
                    set.Connect(nodes[i-1], KernelSumNode.KernelPorts.Output, node, KernelSumNode.KernelPorts.Inputs, 0);
                    set.SetPortArraySize(nodes[i-1], KernelSumNode.KernelPorts.Inputs, 2);
                    set.Connect(node, KernelSumNode.KernelPorts.Output, nodes[i-1], KernelSumNode.KernelPorts.Inputs, 1, NodeSet.ConnectionType.Feedback);
                }

                set.SetData(nodes[0], KernelSumNode.KernelPorts.Inputs, 0, 1);
                for (int i = 0; i < 10; ++i)
                {
                    set.Update();
                    for (int j = 0; j < numNodes; ++j)
                    {
                       Assert.AreEqual(expected[j], set.GetValueBlocking(nodeGVs[j]));
                    }

                    expected[0] = expected[1] + 1;
                    for (int j = 1; j < numNodes - 1; ++j)
                    {
                        expected[j] = expected[j - 1] + expected[j + 1];
                    }
                    expected[numNodes - 1] = expected[numNodes - 2];
                }

                nodeGVs.ForEach(n => set.ReleaseGraphValue(n));
                nodes.ForEach(n => set.Destroy(n));
            }
        }
    }
}
