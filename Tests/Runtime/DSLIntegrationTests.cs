using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.DataFlowGraph.Tests
{
    class DSLIntegrationTests
    {
        public interface TestDSL { }

        class DSL : DSLHandler<TestDSL>
        {
            public int ConnectCalls = 0, DisconnectCalls = 0;

            protected override void Connect(ConnectionInfo left, ConnectionInfo right)
            {
                ConnectCalls++;
            }

            protected override void Disconnect(ConnectionInfo left, ConnectionInfo right)
            {
                DisconnectCalls++;
            }
        }

        class DSL2 : DSLHandler<TestDSL>
        {
            public int ConnectCalls = 0, DisconnectCalls = 0;

            protected override void Connect(ConnectionInfo left, ConnectionInfo right)
            {
                ConnectCalls++;
            }

            protected override void Disconnect(ConnectionInfo left, ConnectionInfo right)
            {
                DisconnectCalls++;
            }
        }

        struct Data : IKernelData { }

        class NodeWithDSL
            : SimulationNodeDefinition<NodeWithDSL.SimPorts>, TestDSL
        {
            public struct SimPorts : ISimulationPortDefinition
            {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                public DSLInput<NodeWithDSL, DSL, TestDSL> DSLIn;
                public DSLOutput<NodeWithDSL, DSL, TestDSL> DSLOut;

                public DSLInput<NodeWithDSL, DSL2, TestDSL> DSLIn2;
                public DSLOutput<NodeWithDSL, DSL2, TestDSL> DSLOut2;
#pragma warning restore 649
            }
        }

        [Test]
        public void DSLDefinitionsAreUniqueAndSingletonsPerSet()
        {
            using (var a = new NodeSet())
            using (var b = new NodeSet())
            {
                var dslA = a.GetDSLHandler<DSL>();
                var dslATwo = a.GetDSLHandler<DSL>();

                Assert.AreEqual(dslA, dslATwo);

                var dslB = b.GetDSLHandler<DSL>();
                var dslBTwo = b.GetDSLHandler<DSL>();

                Assert.AreEqual(dslB, dslBTwo);

                Assert.AreNotEqual(dslB, dslA);

            }
        }

        [Test]
        public void CanHaveMultipleDSLsInOneSet()
        {
            using (var a = new NodeSet())
            {
                var dslA = a.GetDSLHandler<DSL>();
                var dslB = a.GetDSLHandler<DSL2>();

                Assert.IsNotNull(dslA);
                Assert.IsNotNull(dslB);
            }
        }

        (NodeSet Set, List<NodeHandle<NodeWithDSL>> Nodes) SetupNodesetWithDSLs()
        {
            var set = new NodeSet();
            var dslA = set.GetDSLHandler<DSL>();
            var dslB = set.GetDSLHandler<DSL2>();

            var nodes = new List<NodeHandle<NodeWithDSL>>();

            for (int i = 0; i < 100; ++i)
            {
                nodes.Add(set.Create<NodeWithDSL>());
            }

            var nodeOffset = nodes.Count / 2 - 1;

            for (int i = 0; i < nodes.Count / 2; ++i)
            {
                if (i % 2 == 0)
                {
                    Assert.AreEqual(i / 2, dslA.ConnectCalls);
                    set.Connect(nodes[i], NodeWithDSL.SimulationPorts.DSLOut, nodes[i + nodeOffset], NodeWithDSL.SimulationPorts.DSLIn);
                    Assert.AreEqual(i / 2 + 1, dslA.ConnectCalls);
                    Assert.Zero(dslA.DisconnectCalls);
                }
                else
                {
                    Assert.AreEqual(i / 2, dslB.ConnectCalls);
                    set.Connect(nodes[i], NodeWithDSL.SimulationPorts.DSLOut2, nodes[i + nodeOffset], NodeWithDSL.SimulationPorts.DSLIn2);
                    Assert.AreEqual(i / 2 + 1, dslB.ConnectCalls);
                    Assert.Zero(dslB.DisconnectCalls);
                }
            }

            return (set, nodes);
        }

        [Test]
        public void DSLConnectionCallbacks_AreInvoked_WhenMakingDSLConnections()
        {
            var nodesAndSet = SetupNodesetWithDSLs();
            var set = nodesAndSet.Set;
            var nodes = nodesAndSet.Nodes;

            using (set)
            {
                nodes.ForEach(n => set.Destroy(n));
            }
        }

        [Test]
        public void DSLDisconnectCallbacks_AreInvoked_WhenManuallyDisconnecting()
        {
            var nodesAndSet = SetupNodesetWithDSLs();
            var set = nodesAndSet.Set;
            var nodes = nodesAndSet.Nodes;

            using (set)
            {
                var dslA = set.GetDSLHandler<DSL>();
                var dslB = set.GetDSLHandler<DSL2>();

                var nodeOffset = nodes.Count / 2 - 1;

                for (int i = 0; i < nodes.Count / 2; ++i)
                {
                    if (i % 2 == 0)
                    {
                        Assert.AreEqual(i / 2, dslA.DisconnectCalls);
                        set.Disconnect(nodes[i], NodeWithDSL.SimulationPorts.DSLOut, nodes[i + nodeOffset], NodeWithDSL.SimulationPorts.DSLIn);
                        Assert.AreEqual(i / 2 + 1, dslA.DisconnectCalls);
                        Assert.AreEqual(25, dslB.ConnectCalls);
                    }
                    else
                    {
                        Assert.AreEqual(i / 2, dslB.DisconnectCalls);
                        set.Disconnect(nodes[i], NodeWithDSL.SimulationPorts.DSLOut2, nodes[i + nodeOffset], NodeWithDSL.SimulationPorts.DSLIn2);
                        Assert.AreEqual(i / 2 + 1, dslB.DisconnectCalls);
                        Assert.AreEqual(25, dslA.ConnectCalls);
                    }
                }

                nodes.ForEach(n => set.Destroy(n));

                Assert.AreEqual(25, dslA.ConnectCalls);
                Assert.AreEqual(25, dslB.DisconnectCalls);
                Assert.AreEqual(25, dslA.ConnectCalls);
                Assert.AreEqual(25, dslB.DisconnectCalls);
            }
        }

        [Test]
        public void DSLDisconnectCallbacks_AreInvoked_WhenManuallyDestroyingNodes()
        {
            var nodesAndSet = SetupNodesetWithDSLs();
            var set = nodesAndSet.Set;
            var nodes = nodesAndSet.Nodes;

            using (set)
            {
                var dslA = set.GetDSLHandler<DSL>();
                var dslB = set.GetDSLHandler<DSL2>();

                var nodeOffset = nodes.Count / 2 - 1;
                nodes.ForEach(n => set.Destroy(n));

                Assert.AreEqual(25, dslA.ConnectCalls);
                Assert.AreEqual(25, dslB.DisconnectCalls);
                Assert.AreEqual(25, dslA.ConnectCalls);
                Assert.AreEqual(25, dslB.DisconnectCalls);

            }
        }

        [Test]
        public void DSLDisconnectCallbacks_AreInvoked_WhenManuallyDisposingSet()
        {
            var nodesAndSet = SetupNodesetWithDSLs();
            var set = nodesAndSet.Set;
            var nodes = nodesAndSet.Nodes;

            using (set)
            {
                var dslA = set.GetDSLHandler<DSL>();
                var dslB = set.GetDSLHandler<DSL2>();

                LogAssert.Expect(LogType.Error, new Regex("NodeSet leak warnings"));
                set.Dispose();

                Assert.AreEqual(25, dslA.ConnectCalls);
                Assert.AreEqual(25, dslB.DisconnectCalls);
                Assert.AreEqual(25, dslA.ConnectCalls);
                Assert.AreEqual(25, dslB.DisconnectCalls);

            }
        }

        class IndexConnectedDSL : DSLHandler<TestDSL>
        {
            public KeyValuePair<NodeHandle, ushort> leftConnectionIndex;
            public KeyValuePair<NodeHandle, ushort> rightConnectionIndex;

            protected override void Connect(ConnectionInfo left, ConnectionInfo right)
            {
                leftConnectionIndex = new KeyValuePair<NodeHandle, ushort>(left.Handle, left.DSLPortIndex);
                rightConnectionIndex = new KeyValuePair<NodeHandle, ushort>(right.Handle, right.DSLPortIndex);
            }

            protected override void Disconnect(ConnectionInfo left, ConnectionInfo right)
            {
                Assert.AreEqual(leftConnectionIndex.Key, left.Handle);
                Assert.AreEqual(leftConnectionIndex.Value, left.DSLPortIndex);
                Assert.AreEqual(rightConnectionIndex.Key, right.Handle);
                Assert.AreEqual(rightConnectionIndex.Value, right.DSLPortIndex);
            }
        }

        class IndexConnectedDSL2 : IndexConnectedDSL
        {
        }

        class NodeWithInterleavedDSLPorts
            : SimulationNodeDefinition<NodeWithInterleavedDSLPorts.SimPorts>, TestDSL
        {
            public struct SimPorts : ISimulationPortDefinition
            {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                public DSLInput<NodeWithInterleavedDSLPorts, IndexConnectedDSL, TestDSL>  DSL1In1;    // DSL1 Input Index 0
                public DSLInput<NodeWithInterleavedDSLPorts, IndexConnectedDSL2, TestDSL> DSL2In1;    // DSL2 Input Index 0
                public DSLInput<NodeWithInterleavedDSLPorts, IndexConnectedDSL, TestDSL>  DSL1In2;    // DSL1 Input Index 1
                public DSLInput<NodeWithInterleavedDSLPorts, IndexConnectedDSL2, TestDSL> DSL2In2;    // DSL2 Input Index 1

                public DSLOutput<NodeWithInterleavedDSLPorts, IndexConnectedDSL, TestDSL>  DSL1Out1;  // DSL1 Output Index 0
                public DSLOutput<NodeWithInterleavedDSLPorts, IndexConnectedDSL2, TestDSL> DSL2Out1;  // DSL2 Output Index 0
                public DSLOutput<NodeWithInterleavedDSLPorts, IndexConnectedDSL, TestDSL>  DSL1Out2;  // DSL1 Output Index 1
                public DSLOutput<NodeWithInterleavedDSLPorts, IndexConnectedDSL2, TestDSL> DSL2Out2;  // DSL2 Output Index 1
#pragma warning restore 649
            }
        }

        [Test]
        public void DSLConnect_FollowDSLPortIndices_WhenDSLPortsAreInterleaved()
        {
            using (var set = new NodeSet())
            {
                var node1 = set.Create<NodeWithInterleavedDSLPorts>();
                var node2 = set.Create<NodeWithInterleavedDSLPorts>();

                set.Connect(node1, NodeWithInterleavedDSLPorts.SimulationPorts.DSL1Out1, node2, NodeWithInterleavedDSLPorts.SimulationPorts.DSL1In2);  // 0 -> 1
                set.Connect(node1, NodeWithInterleavedDSLPorts.SimulationPorts.DSL2Out2, node2, NodeWithInterleavedDSLPorts.SimulationPorts.DSL2In1);  // 1 -> 0

                var dsl1 = set.GetDSLHandler<IndexConnectedDSL>();
                var dsl2 = set.GetDSLHandler<IndexConnectedDSL2>();

                Assert.AreEqual(dsl1.leftConnectionIndex.Key, (NodeHandle)node1);
                Assert.AreEqual(dsl1.leftConnectionIndex.Value, 0);
                Assert.AreEqual(dsl2.leftConnectionIndex.Key, (NodeHandle)node1);
                Assert.AreEqual(dsl2.leftConnectionIndex.Value, 1);

                Assert.AreEqual(dsl1.rightConnectionIndex.Key, (NodeHandle)node2);
                Assert.AreEqual(dsl1.rightConnectionIndex.Value, 1);
                Assert.AreEqual(dsl2.rightConnectionIndex.Key, (NodeHandle)node2);
                Assert.AreEqual(dsl2.rightConnectionIndex.Value, 0);

                set.Destroy(node1);
                set.Destroy(node2);
            }
        }

        class NodeWithInterleavedDSLAndMessagePorts
            : SimulationNodeDefinition<NodeWithInterleavedDSLAndMessagePorts.SimPorts>, TestDSL
        {
            public struct SimPorts : ISimulationPortDefinition
            {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                public DSLInput<NodeWithInterleavedDSLAndMessagePorts, IndexConnectedDSL, TestDSL>  DSLIn1;    // DSL Input Index 0
                public MessageInput<NodeWithInterleavedDSLAndMessagePorts, float> MsgInput;
                public DSLInput<NodeWithInterleavedDSLAndMessagePorts, IndexConnectedDSL, TestDSL>  DSLIn2;    // DSL Input Index 1

                public DSLOutput<NodeWithInterleavedDSLAndMessagePorts, IndexConnectedDSL, TestDSL>  DSLOut1;  // DSL Output Index 0
                public MessageOutput<NodeWithInterleavedDSLAndMessagePorts, float> MsgOutput;
                public DSLOutput<NodeWithInterleavedDSLAndMessagePorts, IndexConnectedDSL, TestDSL>  DSLOut2;  // DSL Output Index 1
#pragma warning restore 649
            }

            struct Node : INodeData, IMsgHandler<float>
            {
                public void HandleMessage(MessageContext ctx, in float msg) { }
            }
        }

        [Test]
        public void DSLConnect_FollowDSLPortIndices_WhenMessagePortsAndDSLPortsAreInterleaved()
        {
            using (var set = new NodeSet())
            {
                var node1 = set.Create<NodeWithInterleavedDSLAndMessagePorts>();
                var node2 = set.Create<NodeWithInterleavedDSLAndMessagePorts>();

                set.Connect(node1, NodeWithInterleavedDSLAndMessagePorts.SimulationPorts.DSLOut2, node2, NodeWithInterleavedDSLAndMessagePorts.SimulationPorts.DSLIn1);     // 1 -> 0
                set.Connect(node1, NodeWithInterleavedDSLAndMessagePorts.SimulationPorts.MsgOutput, node2, NodeWithInterleavedDSLAndMessagePorts.SimulationPorts.MsgInput);

                var dsl = set.GetDSLHandler<IndexConnectedDSL>();

                Assert.AreEqual(dsl.leftConnectionIndex.Key, (NodeHandle)node1);
                Assert.AreEqual(dsl.leftConnectionIndex.Value, 1);

                Assert.AreEqual(dsl.rightConnectionIndex.Key, (NodeHandle)node2);
                Assert.AreEqual(dsl.rightConnectionIndex.Value, 0);

                set.Destroy(node1);
                set.Destroy(node2);
            }
        }
    }

}
