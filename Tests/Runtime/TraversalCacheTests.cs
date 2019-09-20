using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace Unity.DataFlowGraph.Tests
{
    class TraversalCacheTests
    {
        public enum ComputeType
        {
            NonJobified,
            Jobified
        }

        public struct Node : INodeData { }

        public class InOutTestNode : NodeDefinition<Node, InOutTestNode.SimPorts>, IMsgHandler<Message>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                public MessageInput<InOutTestNode, Message> Input1, Input2, Input3;
                public MessageOutput<InOutTestNode, Message> Output1, Output2, Output3;
#pragma warning restore 649  // Assigned through internal DataFlowGraph reflection
            }

            public void HandleMessage(in MessageContext ctx, in Message msg)
            {
            }
        }

        public class OneMessageOneData : NodeDefinition<Node, OneMessageOneData.SimPorts, OneMessageOneData.KernelData, OneMessageOneData.KernelPortDefinition, OneMessageOneData.Kernel>, IMsgHandler<int>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                public MessageInput<OneMessageOneData, int> MsgInput;
                public MessageOutput<OneMessageOneData, int> MsgOutput;
#pragma warning restore 649  // Assigned through internal DataFlowGraph reflection
            }

            public struct KernelPortDefinition : IKernelPortDefinition
            {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                public DataInput<OneMessageOneData, int> DataInput;
                public DataOutput<OneMessageOneData, int> DataOutput;
#pragma warning restore 649  // Assigned through internal DataFlowGraph reflection
            }

            public struct KernelData : IKernelData
            {
            }

            [BurstCompile(CompileSynchronously = true)]
            public struct Kernel : IGraphKernel<KernelData, KernelPortDefinition>
            {
                public void Execute(RenderContext ctx, KernelData data, ref KernelPortDefinition ports)
                {
                }
            }

            public void HandleMessage(in MessageContext ctx, in int msg)
            {
            }
        }

        class Test<TNodeType> : IDisposable
            where TNodeType : INodeDefinition
        {
            public NativeList<NodeHandle<TNodeType>> Nodes;
            public TraversalCache Cache;
            public NodeSet Set;

            TopologyCacheAPI.TopologyComputationOptions m_Options;

            public Test(ComputeType computingType, TraversalFlags traversalFlags = TraversalFlags.All)
            {
                Cache = new TraversalCache(0, traversalFlags);
                Nodes = new NativeList<NodeHandle<TNodeType>>(10, Allocator.Temp);
                Set = new NodeSet();
                m_Options = TopologyCacheAPI.TopologyComputationOptions.Create(computeJobified: computingType == ComputeType.Jobified);
            }

            public TopologyCacheWalker GetWalker()
            {
                NativeList<NodeHandle> untypedNodes = new NativeList<NodeHandle>(10, Allocator.Temp);
                for (int i = 0; i < Nodes.Length; ++i)
                    untypedNodes.Add(Nodes[i]);

                TopologyComputationContext context;

                var dependency = TopologyComputationContext.InitializeContext(
                    new JobHandle(),
                    out context,
                    Set.GetInternalEdges(),
                    Set.GetInternalTopologyIndices(),
                    Cache,
                    new TopologyComputationContext.NodeArraySource(untypedNodes),
                    Set.TopologyVersion
                );

                dependency.Complete();

                TopologyCacheAPI.UpdateCacheInline(Set.TopologyVersion, m_Options, ref context);

                context.Dispose();
                return new TopologyCacheWalker(Cache);
            }

            public void Dispose()
            {
                Cache.Dispose();
                for (int i = 0; i < Nodes.Length; ++i)
                    Set.Destroy(Nodes[i]);

                Nodes.Dispose();
                Set.Dispose();
            }

        }

        [TestCase(ComputeType.NonJobified)]
        [TestCase(ComputeType.Jobified)]
        public void MultipleIsolatedGraphs_CanStillBeWalked_InOneCompleteOrder(ComputeType jobified)
        {
            const int graphs = 5, nodes = 5;

            using (var test = new Test<InOutTestNode>(jobified))
            {
                for (int g = 0; g < graphs; ++g)
                {
                    for (int n = 0; n < nodes; ++n)
                    {
                        test.Nodes.Add(test.Set.Create<InOutTestNode>());
                    }

                    for (int n = 0; n < nodes - 1; ++n)
                    {
                        test.Set.Connect(test.Nodes[0 + n + g * nodes], InOutTestNode.SimulationPorts.Output1, test.Nodes[1 + n + g * nodes], InOutTestNode.SimulationPorts.Input1);
                    }

                }

                foreach (var node in test.GetWalker())
                {
                    for (int g = 0; g < graphs; ++g)
                    {
                        if (node.Handle == test.Nodes[g * nodes]) // root ?
                        {
                            // walk root, and ensure each next child is ordered with respect to original connection order (conga line)
                            var parent = node;

                            for (int n = 0; n < nodes - 1; ++n)
                            {
                                foreach (var child in parent.GetChildren())
                                {
                                    Assert.AreEqual(child.Handle, (NodeHandle)test.Nodes[n + 1 + g * nodes]);

                                    parent = child;
                                    break;
                                }
                            }
                        }
                    }
                }
            }
        }

        [TestCase(ComputeType.NonJobified)]
        [TestCase(ComputeType.Jobified)]
        public void TraversalCache_ForUnrelatedNodes_StillContainAllNodes(ComputeType jobified)
        {
            using (var test = new Test<InOutTestNode>(jobified))
            {
                for (int i = 0; i < 5; ++i)
                {
                    test.Nodes.Add(test.Set.Create<InOutTestNode>());
                }

                var foundNodes = new List<NodeHandle>();

                foreach (var node in test.GetWalker())
                {
                    foundNodes.Add(node.Handle);
                }

                for (int i = 0; i < test.Nodes.Length; ++i)
                    CollectionAssert.Contains(foundNodes, (NodeHandle)test.Nodes[i]);

                Assert.AreEqual(test.Nodes.Length, foundNodes.Count);
            }
        }

        [TestCase(ComputeType.NonJobified)]
        [TestCase(ComputeType.Jobified)]
        public void TraversalCacheWalkers_AreProperlyCleared_AfterAllNodesAreDestroyed(ComputeType jobified)
        {
            using (var test = new Test<InOutTestNode>(jobified))
            {
                for (int i = 0; i < 5; ++i)
                {
                    test.Nodes.Add(test.Set.Create<InOutTestNode>());
                }

                var foundNodes = new List<NodeHandle>();
                test.GetWalker();

                for (int i = 0; i < 5; ++i)
                {
                    test.Set.Destroy(test.Nodes[i]);
                }

                test.Nodes.Clear();
                // refreshes cache
                test.GetWalker();

                Assert.AreEqual(0, test.Cache.Leaves.Length);
                Assert.AreEqual(0, test.Cache.Roots.Length);
                Assert.AreEqual(0, test.Cache.OrderedTraversal.Length);
                Assert.AreEqual(0, test.Cache.ParentTable.Length);
                Assert.AreEqual(0, test.Cache.ChildTable.Length);

                Assert.AreEqual(0, new TopologyCacheWalker(test.Cache).Count);
                Assert.AreEqual(0, new RootCacheWalker(test.Cache).Count);
                Assert.AreEqual(0, new LeafCacheWalker(test.Cache).Count);
            }
        }

        [TestCase(ComputeType.NonJobified)]
        [TestCase(ComputeType.Jobified)]
        public void CanFindParentsAndChildren_ByPort(ComputeType jobified)
        {
            using (var test = new Test<InOutTestNode>(jobified))
            {
                for (int i = 0; i < 5; ++i)
                {
                    test.Nodes.Add(test.Set.Create<InOutTestNode>());
                }

                test.Set.Connect(test.Nodes[0], InOutTestNode.SimulationPorts.Output1, test.Nodes[2], InOutTestNode.SimulationPorts.Input1);
                test.Set.Connect(test.Nodes[1], InOutTestNode.SimulationPorts.Output1, test.Nodes[2], InOutTestNode.SimulationPorts.Input2);

                test.Set.Connect(test.Nodes[2], InOutTestNode.SimulationPorts.Output1, test.Nodes[3], InOutTestNode.SimulationPorts.Input1);
                test.Set.Connect(test.Nodes[2], InOutTestNode.SimulationPorts.Output2, test.Nodes[4], InOutTestNode.SimulationPorts.Input1);

                bool centerNodeWasFound = false;

                foreach (var node in test.GetWalker())
                {
                    if (node.Handle == test.Nodes[2])
                    {
                        for (int i = 0; i < 2; ++i)
                            foreach (var parent in node.GetParentsByPort(i == 0 ? InOutTestNode.SimulationPorts.Input1.Port : InOutTestNode.SimulationPorts.Input2.Port))
                                Assert.AreEqual((NodeHandle)test.Nodes[i], parent.Handle);

                        for (int i = 0; i < 2; ++i)
                            foreach (var child in node.GetChildrenByPort(i == 0 ? InOutTestNode.SimulationPorts.Output1.Port : InOutTestNode.SimulationPorts.Output2.Port))
                                Assert.AreEqual((NodeHandle)test.Nodes[i + 3], child.Handle);

                        centerNodeWasFound = true;
                        break;
                    }
                }

                Assert.IsTrue(centerNodeWasFound, "Couldn't find middle of graph");
            }
        }

        [TestCase(ComputeType.NonJobified)]
        [TestCase(ComputeType.Jobified)]
        public void CanFindParentsAndChildren_ByPort_ThroughConnection(ComputeType jobified)
        {
            using (var test = new Test<InOutTestNode>(jobified))
            {
                for (int i = 0; i < 5; ++i)
                {
                    test.Nodes.Add(test.Set.Create<InOutTestNode>());
                }

                test.Set.Connect(test.Nodes[0], InOutTestNode.SimulationPorts.Output1, test.Nodes[2], InOutTestNode.SimulationPorts.Input1);
                test.Set.Connect(test.Nodes[1], InOutTestNode.SimulationPorts.Output1, test.Nodes[2], InOutTestNode.SimulationPorts.Input2);

                test.Set.Connect(test.Nodes[2], InOutTestNode.SimulationPorts.Output1, test.Nodes[3], InOutTestNode.SimulationPorts.Input1);
                test.Set.Connect(test.Nodes[2], InOutTestNode.SimulationPorts.Output2, test.Nodes[4], InOutTestNode.SimulationPorts.Input1);

                bool centerNodeWasFound = false;

                foreach (var node in test.GetWalker())
                {
                    if (node.Handle == test.Nodes[2])
                    {
                        for (int i = 0; i < 2; ++i)
                            foreach (var parentConnection in node.GetParentConnectionsByPort(i == 0 ? InOutTestNode.SimulationPorts.Input1.Port : InOutTestNode.SimulationPorts.Input2.Port))
                                Assert.AreEqual((NodeHandle)test.Nodes[i], parentConnection.TargetNode.Handle);

                        for (int i = 0; i < 2; ++i)
                            foreach (var childConnection in node.GetChildConnectionsByPort(i == 0 ? InOutTestNode.SimulationPorts.Output1.Port : InOutTestNode.SimulationPorts.Output2.Port))
                                Assert.AreEqual((NodeHandle)test.Nodes[i + 3], childConnection.TargetNode.Handle);

                        centerNodeWasFound = true;
                        break;
                    }
                }

                Assert.IsTrue(centerNodeWasFound, "Couldn't find middle of graph");
            }
        }

        [TestCase(ComputeType.NonJobified)]
        [TestCase(ComputeType.Jobified)]
        public void PortIndices_OnCacheConnections_MatchesOriginalTopology(ComputeType jobified)
        {
            using (var test = new Test<InOutTestNode>(jobified))
            {
                for (int i = 0; i < 5; ++i)
                {
                    test.Nodes.Add(test.Set.Create<InOutTestNode>());
                }

                test.Set.Connect(test.Nodes[0], InOutTestNode.SimulationPorts.Output1, test.Nodes[2], InOutTestNode.SimulationPorts.Input1);
                test.Set.Connect(test.Nodes[1], InOutTestNode.SimulationPorts.Output1, test.Nodes[2], InOutTestNode.SimulationPorts.Input2);

                test.Set.Connect(test.Nodes[2], InOutTestNode.SimulationPorts.Output1, test.Nodes[3], InOutTestNode.SimulationPorts.Input1);
                test.Set.Connect(test.Nodes[2], InOutTestNode.SimulationPorts.Output2, test.Nodes[4], InOutTestNode.SimulationPorts.Input1);

                bool centerNodeWasFound = false;

                foreach (var node in test.GetWalker())
                {
                    if (node.Handle == test.Nodes[2])
                    {
                        var inPorts = new[] { InOutTestNode.SimulationPorts.Input1, InOutTestNode.SimulationPorts.Input2 };
                        var outPorts = new[] { InOutTestNode.SimulationPorts.Output1, InOutTestNode.SimulationPorts.Output2 };
                        for (int i = 0; i < 2; ++i)
                            foreach (var parentConnection in node.GetParentConnectionsByPort(inPorts[i].Port))
                            {
                                Assert.AreEqual(InOutTestNode.SimulationPorts.Output1.Port, parentConnection.OutputPort);
                                Assert.AreEqual(inPorts[i].Port, parentConnection.InputPort);
                            }

                        for (int i = 0; i < 2; ++i)
                            foreach (var childConnection in node.GetChildConnectionsByPort(outPorts[i].Port))
                            {
                                Assert.AreEqual(InOutTestNode.SimulationPorts.Input1.Port, childConnection.InputPort);
                                Assert.AreEqual(outPorts[i].Port, childConnection.OutputPort);
                            }

                        centerNodeWasFound = true;
                        break;
                    }
                }

                Assert.IsTrue(centerNodeWasFound, "Couldn't find middle of graph");
            }
        }


        [TestCase(ComputeType.NonJobified)]
        [TestCase(ComputeType.Jobified)]
        public void ParentsAndChildrenWalkers_HasCorrectCounts(ComputeType jobified)
        {
            using (var test = new Test<InOutTestNode>(jobified))
            {
                for (int i = 0; i < 5; ++i)
                {
                    test.Nodes.Add(test.Set.Create<InOutTestNode>());
                }

                test.Set.Connect(test.Nodes[0], InOutTestNode.SimulationPorts.Output1, test.Nodes[2], InOutTestNode.SimulationPorts.Input1);
                test.Set.Connect(test.Nodes[1], InOutTestNode.SimulationPorts.Output1, test.Nodes[2], InOutTestNode.SimulationPorts.Input2);

                test.Set.Connect(test.Nodes[2], InOutTestNode.SimulationPorts.Output1, test.Nodes[3], InOutTestNode.SimulationPorts.Input1);
                test.Set.Connect(test.Nodes[2], InOutTestNode.SimulationPorts.Output2, test.Nodes[4], InOutTestNode.SimulationPorts.Input1);

                bool centerNodeWasFound = false;

                foreach (var node in test.GetWalker())
                {
                    if (node.Handle == test.Nodes[2])
                    {
                        Assert.AreEqual(2, node.GetParents().Count);
                        Assert.AreEqual(1, node.GetParentsByPort(InOutTestNode.SimulationPorts.Input1.Port).Count);
                        Assert.AreEqual(1, node.GetParentsByPort(InOutTestNode.SimulationPorts.Input2.Port).Count);

                        Assert.AreEqual(2, node.GetChildren().Count);
                        Assert.AreEqual(1, node.GetChildrenByPort(InOutTestNode.SimulationPorts.Output1.Port).Count);
                        Assert.AreEqual(1, node.GetChildrenByPort(InOutTestNode.SimulationPorts.Output2.Port).Count);

                        centerNodeWasFound = true;
                        break;
                    }
                }

                Assert.IsTrue(centerNodeWasFound, "Couldn't find middle of graph");
            }
        }

        [TestCase(ComputeType.NonJobified)]
        [TestCase(ComputeType.Jobified)]
        public void ParentAndChildConnections_HasCorrectCounts(ComputeType jobified)
        {
            using (var test = new Test<InOutTestNode>(jobified))
            {
                for (int i = 0; i < 5; ++i)
                {
                    test.Nodes.Add(test.Set.Create<InOutTestNode>());
                }

                test.Set.Connect(test.Nodes[0], InOutTestNode.SimulationPorts.Output1, test.Nodes[2], InOutTestNode.SimulationPorts.Input1);
                test.Set.Connect(test.Nodes[1], InOutTestNode.SimulationPorts.Output1, test.Nodes[2], InOutTestNode.SimulationPorts.Input2);

                test.Set.Connect(test.Nodes[2], InOutTestNode.SimulationPorts.Output1, test.Nodes[3], InOutTestNode.SimulationPorts.Input1);
                test.Set.Connect(test.Nodes[2], InOutTestNode.SimulationPorts.Output2, test.Nodes[4], InOutTestNode.SimulationPorts.Input1);

                bool centerNodeWasFound = false;

                foreach (var node in test.GetWalker())
                {
                    if (node.Handle == test.Nodes[2])
                    {
                        Assert.AreEqual(2, node.GetParentConnections().Count);
                        Assert.AreEqual(1, node.GetParentConnectionsByPort(InOutTestNode.SimulationPorts.Input1.Port).Count);
                        Assert.AreEqual(1, node.GetParentConnectionsByPort(InOutTestNode.SimulationPorts.Input2.Port).Count);

                        Assert.AreEqual(2, node.GetChildConnections().Count);
                        Assert.AreEqual(1, node.GetChildConnectionsByPort(InOutTestNode.SimulationPorts.Output1.Port).Count);
                        Assert.AreEqual(1, node.GetChildConnectionsByPort(InOutTestNode.SimulationPorts.Output2.Port).Count);

                        centerNodeWasFound = true;
                        break;
                    }
                }

                Assert.IsTrue(centerNodeWasFound, "Couldn't find middle of graph");
            }
        }


        [TestCase(ComputeType.NonJobified)]
        [TestCase(ComputeType.Jobified)]
        public void CanFindParentsAndChildren(ComputeType jobified)
        {
            using (var test = new Test<InOutTestNode>(jobified))
            {

                for (int i = 0; i < 5; ++i)
                {
                    test.Nodes.Add(test.Set.Create<InOutTestNode>());
                }

                test.Set.Connect(test.Nodes[0], InOutTestNode.SimulationPorts.Output1, test.Nodes[2], InOutTestNode.SimulationPorts.Input1);
                test.Set.Connect(test.Nodes[1], InOutTestNode.SimulationPorts.Output1, test.Nodes[2], InOutTestNode.SimulationPorts.Input2);

                test.Set.Connect(test.Nodes[2], InOutTestNode.SimulationPorts.Output1, test.Nodes[3], InOutTestNode.SimulationPorts.Input1);
                test.Set.Connect(test.Nodes[2], InOutTestNode.SimulationPorts.Output2, test.Nodes[4], InOutTestNode.SimulationPorts.Input1);

                bool centerNodeWasFound = false;

                foreach (var node in test.GetWalker())
                {
                    if (node.Handle == test.Nodes[2])
                    {
                        var parents = new List<NodeHandle>();

                        foreach (var parent in node.GetParents())
                            parents.Add(parent.Handle);

                        var children = new List<NodeHandle>();

                        foreach (var child in node.GetChildren())
                            children.Add(child.Handle);

                        Assert.AreEqual(2, children.Count);
                        Assert.AreEqual(2, parents.Count);

                        Assert.IsTrue(parents.Exists(e => e == test.Nodes[0]));
                        Assert.IsTrue(parents.Exists(e => e == test.Nodes[1]));

                        Assert.IsTrue(children.Exists(e => e == test.Nodes[3]));
                        Assert.IsTrue(children.Exists(e => e == test.Nodes[4]));

                        centerNodeWasFound = true;
                        break;
                    }
                }

                Assert.IsTrue(centerNodeWasFound, "Couldn't find middle of graph");
            }
        }

        [TestCase(ComputeType.NonJobified)]
        [TestCase(ComputeType.Jobified)]
        public void RootsAndLeaves_InternalIndices_AreRegistrered(ComputeType jobified)
        {
            using (var test = new Test<InOutTestNode>(jobified))
            {

                for (int i = 0; i < 5; ++i)
                {
                    test.Nodes.Add(test.Set.Create<InOutTestNode>());
                }

                test.Set.Connect(test.Nodes[0], InOutTestNode.SimulationPorts.Output1, test.Nodes[2], InOutTestNode.SimulationPorts.Input1);
                test.Set.Connect(test.Nodes[1], InOutTestNode.SimulationPorts.Output1, test.Nodes[2], InOutTestNode.SimulationPorts.Input2);

                test.Set.Connect(test.Nodes[2], InOutTestNode.SimulationPorts.Output1, test.Nodes[3], InOutTestNode.SimulationPorts.Input1);
                test.Set.Connect(test.Nodes[2], InOutTestNode.SimulationPorts.Output2, test.Nodes[4], InOutTestNode.SimulationPorts.Input1);

                // easiest way to update cache.
                test.GetWalker();

                var rootWalker = new RootCacheWalker(test.Cache);
                var leafWalker = new LeafCacheWalker(test.Cache);

                var roots = new List<NodeHandle>();
                var leaves = new List<NodeHandle>();

                Assert.AreEqual(rootWalker.Count, 2);
                Assert.AreEqual(leafWalker.Count, 2);

                foreach (var nodeCache in rootWalker)
                    roots.Add(nodeCache.Handle);

                foreach (var nodeCache in leafWalker)
                    leaves.Add(nodeCache.Handle);

                CollectionAssert.Contains(leaves, (NodeHandle)test.Nodes[0]);
                CollectionAssert.Contains(leaves, (NodeHandle)test.Nodes[1]);
                CollectionAssert.Contains(roots, (NodeHandle)test.Nodes[3]);
                CollectionAssert.Contains(roots, (NodeHandle)test.Nodes[4]);
            }
        }

        [TestCase(ComputeType.NonJobified)]
        [TestCase(ComputeType.Jobified)]
        public void RootAndLeafCacheWalker_WalksRootsAndLeaves(ComputeType jobified)
        {
            using (var test = new Test<InOutTestNode>(jobified))
            {

                for (int i = 0; i < 5; ++i)
                {
                    test.Nodes.Add(test.Set.Create<InOutTestNode>());
                }

                test.Set.Connect(test.Nodes[0], InOutTestNode.SimulationPorts.Output1, test.Nodes[2], InOutTestNode.SimulationPorts.Input1);
                test.Set.Connect(test.Nodes[1], InOutTestNode.SimulationPorts.Output1, test.Nodes[2], InOutTestNode.SimulationPorts.Input2);

                test.Set.Connect(test.Nodes[2], InOutTestNode.SimulationPorts.Output1, test.Nodes[3], InOutTestNode.SimulationPorts.Input1);
                test.Set.Connect(test.Nodes[2], InOutTestNode.SimulationPorts.Output2, test.Nodes[4], InOutTestNode.SimulationPorts.Input1);

                // easiest way to update cache.
                test.GetWalker();

                Assert.AreEqual(test.Cache.Leaves.Length, 2);
                Assert.AreEqual(test.Cache.Roots.Length, 2);

                var roots = new List<NodeHandle>();

                for (int i = 0; i < test.Cache.Leaves.Length; ++i)
                    roots.Add(test.Cache.OrderedTraversal[test.Cache.Roots[i]].Handle);

                var leaves = new List<NodeHandle>();

                for (int i = 0; i < test.Cache.Roots.Length; ++i)
                    leaves.Add(test.Cache.OrderedTraversal[test.Cache.Leaves[i]].Handle);

                CollectionAssert.Contains(leaves, (NodeHandle)test.Nodes[0]);
                CollectionAssert.Contains(leaves, (NodeHandle)test.Nodes[1]);
                CollectionAssert.Contains(roots, (NodeHandle)test.Nodes[3]);
                CollectionAssert.Contains(roots, (NodeHandle)test.Nodes[4]);
            }
        }

        [TestCase(ComputeType.NonJobified)]
        [TestCase(ComputeType.Jobified)]
        public void IslandNodes_RegisterBothAsLeafAndRoot(ComputeType jobified)
        {
            using (var test = new Test<InOutTestNode>(jobified))
            {
                test.Nodes.Add(test.Set.Create<InOutTestNode>());

                // easiest way to update cache.
                test.GetWalker();

                Assert.AreEqual(test.Cache.Leaves.Length, 1);
                Assert.AreEqual(test.Cache.Roots.Length, 1);

                var roots = new List<NodeHandle>();

                for (int i = 0; i < test.Cache.Leaves.Length; ++i)
                    roots.Add(test.Cache.OrderedTraversal[test.Cache.Roots[i]].Handle);

                var leaves = new List<NodeHandle>();

                for (int i = 0; i < test.Cache.Roots.Length; ++i)
                    leaves.Add(test.Cache.OrderedTraversal[test.Cache.Leaves[i]].Handle);

                CollectionAssert.Contains(leaves, (NodeHandle)test.Nodes[0]);
                CollectionAssert.Contains(roots, (NodeHandle)test.Nodes[0]);
            }
        }

        [TestCase(ComputeType.NonJobified)]
        [TestCase(ComputeType.Jobified)]
        public void CanCongaWalkAndDependenciesAreInCorrectOrder(ComputeType jobified)
        {
            using (var test = new Test<InOutTestNode>(jobified))
            {
                var node1 = test.Set.Create<InOutTestNode>();
                var node2 = test.Set.Create<InOutTestNode>();
                var node3 = test.Set.Create<InOutTestNode>();

                test.Nodes.Add(node1);
                test.Nodes.Add(node2);
                test.Nodes.Add(node3);

                test.Set.Connect(node1, InOutTestNode.SimulationPorts.Output1, node2, InOutTestNode.SimulationPorts.Input1);
                test.Set.Connect(node2, InOutTestNode.SimulationPorts.Output1, node3, InOutTestNode.SimulationPorts.Input1);

                var index = 0;

                foreach (var node in test.GetWalker())
                {
                    Assert.AreEqual(node.CacheIndex, index);
                    Assert.AreEqual(node.Handle, (NodeHandle)test.Nodes[index]);

                    index++;
                }

            }
        }

        [TestCase(ComputeType.NonJobified)]
        [TestCase(ComputeType.Jobified)]
        public void TestInternalIndices(ComputeType jobified)
        {
            using (var test = new Test<InOutTestNode>(jobified))
            {
                var node1 = test.Set.Create<InOutTestNode>();
                var node2 = test.Set.Create<InOutTestNode>();
                var node3 = test.Set.Create<InOutTestNode>();

                test.Nodes.Add(node1);
                test.Nodes.Add(node2);
                test.Nodes.Add(node3);

                Assert.DoesNotThrow(() => test.Set.Connect(node2, InOutTestNode.SimulationPorts.Output1, node1, InOutTestNode.SimulationPorts.Input1));
                Assert.DoesNotThrow(() => test.Set.Connect(node3, InOutTestNode.SimulationPorts.Output1, node1, InOutTestNode.SimulationPorts.Input3));

                var entryIndex = 0;

                foreach (var node in test.GetWalker())
                {
                    if (entryIndex == 0 || entryIndex == 1)
                    {
                        Assert.AreEqual(0, node.GetParents().Count);
                    }
                    else
                    {
                        Assert.AreEqual(2, node.GetParents().Count);
                        foreach (var parent in node.GetParents())
                        {
                            Assert.IsTrue(parent.Handle == node2 || parent.Handle == node3);
                        }
                    }

                    if (entryIndex == 0 || entryIndex == 1)
                    {
                        Assert.AreEqual(1, node.GetChildren().Count);

                        foreach (var child in node.GetChildren())
                        {
                            Assert.AreEqual((NodeHandle)node1, child.Handle);
                        }
                    }
                    else
                    {
                        Assert.AreEqual(0, node.GetChildren().Count);
                    }

                    entryIndex++;
                }

            }

        }

        [TestCase(TraversalFlags.DataFlow)]
        [TestCase(TraversalFlags.DSL)]
        [TestCase(TraversalFlags.Message)]
        public void TraversalCache_DoesNotInclude_IgnoredTraversalTypes(TraversalFlags traversalType)
        {
            using (var test = new Test<OneMessageOneData>(ComputeType.NonJobified, traversalType))
            {
                int numParents = traversalType == TraversalFlags.DataFlow ? 2 : 0;
                int numChildren = traversalType == TraversalFlags.Message ? 2 : 0;

                for (int i = 0; i < 5; ++i)
                {
                    test.Nodes.Add(test.Set.Create<OneMessageOneData>());
                }

                test.Set.Connect(test.Nodes[0], OneMessageOneData.KernelPorts.DataOutput, test.Nodes[2], OneMessageOneData.KernelPorts.DataInput);
                test.Set.Connect(test.Nodes[1], OneMessageOneData.KernelPorts.DataOutput, test.Nodes[2], OneMessageOneData.KernelPorts.DataInput);

                test.Set.Connect(test.Nodes[2], OneMessageOneData.SimulationPorts.MsgOutput, test.Nodes[3], OneMessageOneData.SimulationPorts.MsgInput);
                test.Set.Connect(test.Nodes[2], OneMessageOneData.SimulationPorts.MsgOutput, test.Nodes[4], OneMessageOneData.SimulationPorts.MsgInput);

                bool centerNodeWasFound = false;

                foreach (var node in test.GetWalker())
                {
                    if (node.Handle == test.Nodes[2])
                    {
                        Assert.AreEqual(0, node.GetParentsByPort(OneMessageOneData.SimulationPorts.MsgInput.Port).Count);
                        Assert.AreEqual(numParents, node.GetParentsByPort(OneMessageOneData.KernelPorts.DataInput.Port).Count);

                        Assert.AreEqual(0, node.GetChildrenByPort(OneMessageOneData.KernelPorts.DataOutput.Port).Count);
                        Assert.AreEqual(numChildren, node.GetChildrenByPort(OneMessageOneData.SimulationPorts.MsgOutput.Port).Count);

                        centerNodeWasFound = true;
                        break;
                    }
                }

                Assert.IsTrue(centerNodeWasFound, "Couldn't find middle of graph");
            }
        }

    }
}
