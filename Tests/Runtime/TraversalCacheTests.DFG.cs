using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine.TestTools;

namespace Unity.DataFlowGraph.Tests
{
    using Topology = TopologyAPI<ValidatedHandle, InputPortArrayID, OutputPortArrayID>;
    using Tools = TopologyTools<ValidatedHandle, InputPortArrayID, OutputPortArrayID>;

    class TraversalCacheTests_DFG
    {
        public enum ComputeType
        {
            NonJobified,
            Jobified
        }

        public class InOutTestNode : SimulationNodeDefinition<InOutTestNode.SimPorts>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                public MessageInput<InOutTestNode, Message> Input1, Input2, Input3;
                public MessageOutput<InOutTestNode, Message> Output1, Output2, Output3;
#pragma warning restore 649  // Assigned through internal DataFlowGraph reflection
            }

            struct Node : INodeData, IMsgHandler<Message>
            {
                public void HandleMessage(MessageContext ctx, in Message msg) { }
            }
        }

        public class OneMessageOneData : SimulationKernelNodeDefinition<OneMessageOneData.SimPorts, OneMessageOneData.KernelPortDefinition>
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

            struct KernelData : IKernelData { }

            [BurstCompile(CompileSynchronously = true)]
            struct Kernel : IGraphKernel<KernelData, KernelPortDefinition>
            {
                public void Execute(RenderContext ctx, in KernelData data, ref KernelPortDefinition ports)
                {
                }
            }

            struct Node : INodeData, IMsgHandler<int>
            {
                public void HandleMessage(MessageContext ctx, in int msg) { }
            }
        }

        class Test<TNodeType> : IDisposable
            where TNodeType : NodeDefinition
        {
            public NativeList<NodeHandle<TNodeType>> Nodes;
            public Topology.TraversalCache Cache;
            // Need to keep a separate clone of the topology indices, as the cache mutates them
            public FlatTopologyMap MapClone;
            public NodeSet Set;

            Topology.CacheAPI.ComputationOptions m_Options;

            public Test(ComputeType computingType)
            {
                Cache = new Topology.TraversalCache(0, Topology.TraversalCache.TraverseAllMask);
                Nodes = new NativeList<NodeHandle<TNodeType>>(10, Allocator.Temp);
                Set = new NodeSet();
                m_Options = Topology.CacheAPI.ComputationOptions.Create(computeJobified: computingType == ComputeType.Jobified);
            }

            public Test(ComputeType computingType, PortDescription.Category traversalCategory)
            {
                Cache = new Topology.TraversalCache(0, (uint)traversalCategory);
                Nodes = new NativeList<NodeHandle<TNodeType>>(10, Allocator.Temp);
                Set = new NodeSet();
                m_Options = Topology.CacheAPI.ComputationOptions.Create(computeJobified: computingType == ComputeType.Jobified);
            }

            public Topology.TraversalCache.Group GetGroupForNode(NodeHandle n)
            {
                return Cache.Groups[MapClone[Set.Validate(n)].GroupID];
            }

            public void UpdateCache() => GetWalker();

            public Tools.CacheWalker GetWalker()
            {
                if (MapClone.IsCreated)
                    MapClone.Dispose();

                MapClone = Set.GetTopologyMap_ForTesting().Clone();

                NativeList<ValidatedHandle> untypedNodes = new NativeList<ValidatedHandle>(10, Allocator.Temp);
                for (int i = 0; i < Nodes.Length; ++i)
                    untypedNodes.Add(Set.Validate(Nodes[i]));

                Topology.ComputationContext<FlatTopologyMap> context;

                var dependency = Topology.ComputationContext<FlatTopologyMap>.InitializeContext(
                    new JobHandle(),
                    out context,
                    Set.GetTopologyDatabase_ForTesting(),
                    MapClone,
                    Cache,
                    untypedNodes.AsArray(),
                    untypedNodes.Length,
                    Set.TopologyVersion
                );

                dependency.Complete();

                Topology.CacheAPI.UpdateCacheInline(Set.TopologyVersion, m_Options, ref context);

                context.Dispose();
                return new Tools.CacheWalker(Cache);
            }

            public void Dispose()
            {
                Cache.Dispose();
                MapClone.Dispose();

                for (int i = 0; i < Nodes.Length; ++i)
                    Set.Destroy(Nodes[i]);

                Nodes.Dispose();
                Set.Dispose();
            }

        }

        [Test]
        public void MultipleIsolatedGraphs_CanStillBeWalked_InOneCompleteOrder([Values] ComputeType jobified)
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
                        if (node.Vertex.ToPublicHandle() == test.Nodes[g * nodes]) // root ?
                        {
                            // walk root, and ensure each next child is ordered with respect to original connection order (conga line)
                            var parent = node;

                            for (int n = 0; n < nodes - 1; ++n)
                            {
                                foreach (var child in parent.GetChildren())
                                {
                                    Assert.AreEqual(child.Vertex.ToPublicHandle(), (NodeHandle)test.Nodes[n + 1 + g * nodes]);

                                    parent = child;
                                    break;
                                }
                            }
                        }
                    }
                }
            }
        }

        [Test]
        public void TraversalCache_ForUnrelatedNodes_StillContainAllNodes([Values] ComputeType jobified)
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
                    foundNodes.Add(node.Vertex.ToPublicHandle());
                }

                for (int i = 0; i < test.Nodes.Length; ++i)
                    CollectionAssert.Contains(foundNodes, (NodeHandle)test.Nodes[i]);

                Assert.AreEqual(test.Nodes.Length, foundNodes.Count);
            }
        }

        [Test]
        public void TraversalCacheWalkers_AreProperlyCleared_AfterAllNodesAreDestroyed([Values] Topology.SortingAlgorithm algorithm, [Values] ComputeType jobified)
        {
            using (var test = new Test<InOutTestNode>(jobified))
            {
                switch (algorithm)
                {
                    case Topology.SortingAlgorithm.GlobalBreadthFirst:
                        test.Set.RendererModel = NodeSet.RenderExecutionModel.MaximallyParallel;
                        break;
                    case Topology.SortingAlgorithm.LocalDepthFirst:
                        test.Set.RendererModel = NodeSet.RenderExecutionModel.Islands;
                        break;
                }

                for (int i = 0; i < 5; ++i)
                {
                    test.Nodes.Add(test.Set.Create<InOutTestNode>());
                }

                var foundNodes = new List<NodeHandle>();
                test.UpdateCache();

                for (int i = 0; i < 5; ++i)
                {
                    test.Set.Destroy(test.Nodes[i]);
                }

                test.Nodes.Clear();
                test.UpdateCache();

                foreach (var group in test.Cache.Groups)
                {
                    Assert.AreEqual(0, group.LeafCount);
                    Assert.AreEqual(0, group.RootCount);
                    Assert.AreEqual(0, group.TraversalCount);
                    Assert.AreEqual(0, group.ParentCount);
                    Assert.AreEqual(0, group.ChildCount);

                    Assert.AreEqual(0, new Topology.GroupWalker(group).Count);
                    Assert.AreEqual(0, new Topology.RootCacheWalker(group).Count);
                    Assert.AreEqual(0, new Topology.LeafCacheWalker(group).Count);
                }
            }
        }

        [Test]
        public void CanFindParentsAndChildren_ByPort([Values] ComputeType jobified)
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
                    if (node.Vertex.ToPublicHandle() == test.Nodes[2])
                    {
                        for (int i = 0; i < 2; ++i)
                            foreach (var parent in node.GetParentsByPort(new InputPortArrayID(i == 0 ? InOutTestNode.SimulationPorts.Input1.Port : InOutTestNode.SimulationPorts.Input2.Port)))
                                Assert.AreEqual((NodeHandle)test.Nodes[i], parent.Vertex.ToPublicHandle());

                        for (int i = 0; i < 2; ++i)
                            foreach (var child in node.GetChildrenByPort(new OutputPortArrayID(i == 0 ? InOutTestNode.SimulationPorts.Output1.Port : InOutTestNode.SimulationPorts.Output2.Port)))
                                Assert.AreEqual((NodeHandle)test.Nodes[i + 3], child.Vertex.ToPublicHandle());

                        centerNodeWasFound = true;
                        break;
                    }
                }

                Assert.IsTrue(centerNodeWasFound, "Couldn't find middle of graph");
            }
        }

        [Test]
        public void CanFindParentsAndChildren_ByPort_ThroughConnection([Values] ComputeType jobified)
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
                    if (node.Vertex.ToPublicHandle() == test.Nodes[2])
                    {
                        for (int i = 0; i < 2; ++i)
                            foreach (var parentConnection in node.GetParentConnectionsByPort(new InputPortArrayID(i == 0 ? InOutTestNode.SimulationPorts.Input1.Port : InOutTestNode.SimulationPorts.Input2.Port)))
                                Assert.AreEqual((NodeHandle)test.Nodes[i], parentConnection.Target.Vertex.ToPublicHandle());

                        for (int i = 0; i < 2; ++i)
                            foreach (var childConnection in node.GetChildConnectionsByPort(new OutputPortArrayID(i == 0 ? InOutTestNode.SimulationPorts.Output1.Port : InOutTestNode.SimulationPorts.Output2.Port)))
                                Assert.AreEqual((NodeHandle)test.Nodes[i + 3], childConnection.Target.Vertex.ToPublicHandle());

                        centerNodeWasFound = true;
                        break;
                    }
                }

                Assert.IsTrue(centerNodeWasFound, "Couldn't find middle of graph");
            }
        }

        [Test]
        public void PortIndices_OnCacheConnections_MatchesOriginalTopology([Values] ComputeType jobified)
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
                    if (node.Vertex.ToPublicHandle() == test.Nodes[2])
                    {
                        var inPorts = new[] { InOutTestNode.SimulationPorts.Input1, InOutTestNode.SimulationPorts.Input2 };
                        var outPorts = new[] { InOutTestNode.SimulationPorts.Output1, InOutTestNode.SimulationPorts.Output2 };
                        for (int i = 0; i < 2; ++i)
                            foreach (var parentConnection in node.GetParentConnectionsByPort(new InputPortArrayID(inPorts[i].Port)))
                            {
                                Assert.AreEqual(InOutTestNode.SimulationPorts.Output1.Port, parentConnection.OutputPort.PortID);
                                Assert.AreEqual(inPorts[i].Port, parentConnection.InputPort.PortID);
                            }

                        for (int i = 0; i < 2; ++i)
                            foreach (var childConnection in node.GetChildConnectionsByPort(new OutputPortArrayID(outPorts[i].Port)))
                            {
                                Assert.AreEqual(InOutTestNode.SimulationPorts.Input1.Port, childConnection.InputPort.PortID);
                                Assert.AreEqual(outPorts[i].Port, childConnection.OutputPort.PortID);
                            }

                        centerNodeWasFound = true;
                        break;
                    }
                }

                Assert.IsTrue(centerNodeWasFound, "Couldn't find middle of graph");
            }
        }

        [Test]
        public void ParentsAndChildrenWalkers_HasCorrectCounts([Values] ComputeType jobified)
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
                    if (node.Vertex.ToPublicHandle() == test.Nodes[2])
                    {
                        Assert.AreEqual(2, node.GetParents().Count);
                        Assert.AreEqual(1, node.GetParentsByPort(new InputPortArrayID(InOutTestNode.SimulationPorts.Input1.Port)).Count);
                        Assert.AreEqual(1, node.GetParentsByPort(new InputPortArrayID(InOutTestNode.SimulationPorts.Input2.Port)).Count);

                        Assert.AreEqual(2, node.GetChildren().Count);
                        Assert.AreEqual(1, node.GetChildrenByPort(new OutputPortArrayID(InOutTestNode.SimulationPorts.Output1.Port)).Count);
                        Assert.AreEqual(1, node.GetChildrenByPort(new OutputPortArrayID(InOutTestNode.SimulationPorts.Output2.Port)).Count);

                        centerNodeWasFound = true;
                        break;
                    }
                }

                Assert.IsTrue(centerNodeWasFound, "Couldn't find middle of graph");
            }
        }

        [Test]
        public void ParentAndChildConnections_HasCorrectCounts([Values] ComputeType jobified)
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
                    if (node.Vertex.ToPublicHandle() == test.Nodes[2])
                    {
                        Assert.AreEqual(2, node.GetParentConnections().Count);
                        Assert.AreEqual(1, node.GetParentConnectionsByPort(new InputPortArrayID(InOutTestNode.SimulationPorts.Input1.Port)).Count);
                        Assert.AreEqual(1, node.GetParentConnectionsByPort(new InputPortArrayID(InOutTestNode.SimulationPorts.Input2.Port)).Count);

                        Assert.AreEqual(2, node.GetChildConnections().Count);
                        Assert.AreEqual(1, node.GetChildConnectionsByPort(new OutputPortArrayID(InOutTestNode.SimulationPorts.Output1.Port)).Count);
                        Assert.AreEqual(1, node.GetChildConnectionsByPort(new OutputPortArrayID(InOutTestNode.SimulationPorts.Output2.Port)).Count);

                        centerNodeWasFound = true;
                        break;
                    }
                }

                Assert.IsTrue(centerNodeWasFound, "Couldn't find middle of graph");
            }
        }

        [Test]
        public void CanFindParentsAndChildren([Values] ComputeType jobified)
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
                    if (node.Vertex.ToPublicHandle() == test.Nodes[2])
                    {
                        var parents = new List<NodeHandle>();

                        foreach (var parent in node.GetParents())
                            parents.Add(parent.Vertex.ToPublicHandle());

                        var children = new List<NodeHandle>();

                        foreach (var child in node.GetChildren())
                            children.Add(child.Vertex.ToPublicHandle());

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

        [Test]
        public void RootsAndLeaves_InternalIndices_AreRegistrered([Values] ComputeType jobified)
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

                test.UpdateCache();

                var group = test.GetGroupForNode(test.Nodes[0]);

                var rootWalker = new Topology.RootCacheWalker(group);
                var leafWalker = new Topology.LeafCacheWalker(group);

                var roots = new List<NodeHandle>();
                var leaves = new List<NodeHandle>();

                Assert.AreEqual(rootWalker.Count, 2);
                Assert.AreEqual(leafWalker.Count, 2);

                foreach (var nodeCache in rootWalker)
                    roots.Add(nodeCache.Vertex.ToPublicHandle());

                foreach (var nodeCache in leafWalker)
                    leaves.Add(nodeCache.Vertex.ToPublicHandle());

                CollectionAssert.Contains(leaves, (NodeHandle)test.Nodes[0]);
                CollectionAssert.Contains(leaves, (NodeHandle)test.Nodes[1]);
                CollectionAssert.Contains(roots, (NodeHandle)test.Nodes[3]);
                CollectionAssert.Contains(roots, (NodeHandle)test.Nodes[4]);
            }
        }

        [Test]
        public void RootAndLeafCacheWalker_WalksRootsAndLeaves([Values] ComputeType jobified)
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

                test.UpdateCache();

                var group = test.GetGroupForNode(test.Nodes[0]);

                Assert.AreEqual(group.LeafCount, 2);
                Assert.AreEqual(group.RootCount, 2);

                var roots = new List<NodeHandle>();

                for (int i = 0; i < group.LeafCount; ++i)
                    roots.Add(group.IndexTraversal(group.IndexRoot(i)).Vertex.ToPublicHandle());

                var leaves = new List<NodeHandle>();

                for (int i = 0; i < group.RootCount; ++i)
                    leaves.Add(group.IndexTraversal(group.IndexLeaf(i)).Vertex.ToPublicHandle());

                CollectionAssert.Contains(leaves, (NodeHandle)test.Nodes[0]);
                CollectionAssert.Contains(leaves, (NodeHandle)test.Nodes[1]);
                CollectionAssert.Contains(roots, (NodeHandle)test.Nodes[3]);
                CollectionAssert.Contains(roots, (NodeHandle)test.Nodes[4]);
            }
        }

        [Test]
        public void IslandNodes_RegisterBothAsLeafAndRoot([Values] ComputeType jobified)
        {
            using (var test = new Test<InOutTestNode>(jobified))
            {
                test.Nodes.Add(test.Set.Create<InOutTestNode>());

                test.UpdateCache();

                var group = test.GetGroupForNode(test.Nodes[0]);

                Assert.AreEqual(group.LeafCount, 1);
                Assert.AreEqual(group.RootCount, 1);

                var roots = new List<NodeHandle>();

                for (int i = 0; i < group.RootCount; ++i)
                    roots.Add(group.IndexTraversal(group.IndexRoot(i)).Vertex.ToPublicHandle());

                var leaves = new List<NodeHandle>();

                for (int i = 0; i < group.LeafCount; ++i)
                    leaves.Add(group.IndexTraversal(group.IndexLeaf(i)).Vertex.ToPublicHandle());

                CollectionAssert.Contains(leaves, (NodeHandle)test.Nodes[0]);
                CollectionAssert.Contains(roots, (NodeHandle)test.Nodes[0]);
            }
        }

        [Test]
        public void CanCongaWalkAndDependenciesAreInCorrectOrder([Values] ComputeType jobified)
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
                    Assert.AreEqual(node.Vertex.ToPublicHandle(), (NodeHandle)test.Nodes[index]);

                    index++;
                }

            }
        }

        [Test]
        public void TestInternalIndices([Values] ComputeType jobified)
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
                            Assert.IsTrue(parent.Vertex.ToPublicHandle() == node2 || parent.Vertex.ToPublicHandle() == node3);
                        }
                    }

                    if (entryIndex == 0 || entryIndex == 1)
                    {
                        Assert.AreEqual(1, node.GetChildren().Count);

                        foreach (var child in node.GetChildren())
                        {
                            Assert.AreEqual((NodeHandle)node1, child.Vertex.ToPublicHandle());
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

        [Test]
        public void TraversalCache_DoesNotInclude_IgnoredTraversalTypes([Values] PortDescription.Category traversalType)
        {
            using (var test = new Test<OneMessageOneData>(ComputeType.NonJobified, traversalType))
            {
                int numChildren = traversalType == PortDescription.Category.Data ? 2 : 0;
                int numParents = traversalType == PortDescription.Category.Message ? 2 : 0;

                for (int i = 0; i < 5; ++i)
                {
                    test.Nodes.Add(test.Set.Create<OneMessageOneData>());
                }

                test.Set.Connect(test.Nodes[0], OneMessageOneData.SimulationPorts.MsgOutput, test.Nodes[2], OneMessageOneData.SimulationPorts.MsgInput);
                test.Set.Connect(test.Nodes[1], OneMessageOneData.SimulationPorts.MsgOutput, test.Nodes[2], OneMessageOneData.SimulationPorts.MsgInput);

                test.Set.Connect(test.Nodes[2], OneMessageOneData.KernelPorts.DataOutput, test.Nodes[3], OneMessageOneData.KernelPorts.DataInput);
                test.Set.Connect(test.Nodes[2], OneMessageOneData.KernelPorts.DataOutput, test.Nodes[4], OneMessageOneData.KernelPorts.DataInput);

                bool centerNodeWasFound = false;

                foreach (var node in test.GetWalker())
                {
                    if (node.Vertex.ToPublicHandle() == test.Nodes[2])
                    {
                        Assert.AreEqual(numParents, node.GetParentsByPort(new InputPortArrayID(OneMessageOneData.SimulationPorts.MsgInput.Port)).Count);
                        Assert.AreEqual(0, node.GetParentsByPort(new InputPortArrayID(OneMessageOneData.KernelPorts.DataInput.Port)).Count);

                        Assert.AreEqual(numChildren, node.GetChildrenByPort(new OutputPortArrayID(OneMessageOneData.KernelPorts.DataOutput.Port)).Count);
                        Assert.AreEqual(0, node.GetChildrenByPort(new OutputPortArrayID(OneMessageOneData.SimulationPorts.MsgOutput.Port)).Count);

                        centerNodeWasFound = true;
                        break;
                    }
                }

                Assert.IsTrue(centerNodeWasFound, "Couldn't find middle of graph");
            }
        }

        [Test]
        public void CompletelyCyclicDataGraph_ProducesDeferredError([Values] NodeSet.RenderExecutionModel model)
        {
            using (var set = new NodeSet())
            {
                set.RendererModel = model;
                var a = set.Create<OneMessageOneData>();
                var b = set.Create<OneMessageOneData>();

                set.Connect(a, OneMessageOneData.KernelPorts.DataOutput, b, OneMessageOneData.KernelPorts.DataInput);
                set.Connect(b, OneMessageOneData.KernelPorts.DataOutput, a, OneMessageOneData.KernelPorts.DataInput);

                set.Update();
                LogAssert.Expect(UnityEngine.LogType.Error, new Regex("The graph contains a cycle"));

                set.Destroy(a, b);
            }
        }

        [Test]
        public void PartlyCyclicDataGraph_ProducesDeferredError([Values] NodeSet.RenderExecutionModel model)
        {
            using (var set = new NodeSet())
            {
                set.RendererModel = model;
                var a = set.Create<KernelSumNode>();
                var b = set.Create<KernelSumNode>();
                var c = set.Create<KernelSumNode>();

                set.SetPortArraySize(a, KernelSumNode.KernelPorts.Inputs, 2);
                set.SetPortArraySize(b, KernelSumNode.KernelPorts.Inputs, 1);
                set.SetPortArraySize(c, KernelSumNode.KernelPorts.Inputs, 1);

                set.Connect(a, KernelSumNode.KernelPorts.Output, b, KernelSumNode.KernelPorts.Inputs, 0);
                set.Connect(b, KernelSumNode.KernelPorts.Output, a, KernelSumNode.KernelPorts.Inputs, 0);
                set.Connect(c, KernelSumNode.KernelPorts.Output, a, KernelSumNode.KernelPorts.Inputs, 1);

                set.Update();
                LogAssert.Expect(UnityEngine.LogType.Error, new Regex("The graph contains a cycle"));

                set.Destroy(a, b, c);
            }
        }

        [Test]
        public void DeepImplicitlyCyclicDataGraph_ProducesDeferredError([Values] NodeSet.RenderExecutionModel model, [Values(0, 1, 10, 13, 100)] int depth)
        {
            using (var set = new NodeSet())
            {
                set.RendererModel = model;
                var list = new List<NodeHandle<OneMessageOneData>>();

                // create three branches
                NodeHandle<OneMessageOneData>
                    a = set.Create<OneMessageOneData>(),
                    b = set.Create<OneMessageOneData>(),
                    c = set.Create<OneMessageOneData>();

                // intertwine
                set.Connect(a, OneMessageOneData.KernelPorts.DataOutput, b, OneMessageOneData.KernelPorts.DataInput);
                set.Connect(b, OneMessageOneData.KernelPorts.DataOutput, c, OneMessageOneData.KernelPorts.DataInput);

                list.Add(a);
                list.Add(b);
                list.Add(c);

                // fork off ->
                // o-o-o-o-o-o-o-o ...
                // |
                // o-o-o-o-o-o-o-o ...
                // |
                // o-o-o-o-o-o-o-o ...
                for (int i = 0; i < depth; ++i)
                {

                    a = set.Create<OneMessageOneData>();
                    b = set.Create<OneMessageOneData>();
                    c = set.Create<OneMessageOneData>();

                    set.Connect(list[i * 3 + 0], OneMessageOneData.KernelPorts.DataOutput, a, OneMessageOneData.KernelPorts.DataInput);
                    set.Connect(list[i * 3 + 1], OneMessageOneData.KernelPorts.DataOutput, b, OneMessageOneData.KernelPorts.DataInput);
                    set.Connect(list[i * 3 + 2], OneMessageOneData.KernelPorts.DataOutput, c, OneMessageOneData.KernelPorts.DataInput);

                    list.Add(a);
                    list.Add(b);
                    list.Add(c);
                }

                // connect very last node to start, forming a cycle
                // -> o-o-o-o-o-o-o-o->
                // |  |
                // |  o-o-o-o-o-o-o-o->
                // |  |
                // |  o-o-o-o-o-o-o-o
                // -----------------|
                set.Connect(list.Last(), OneMessageOneData.KernelPorts.DataOutput, list.First(), OneMessageOneData.KernelPorts.DataInput);

                set.Update();
                LogAssert.Expect(UnityEngine.LogType.Error, new Regex("The graph contains a cycle"));

                list.ForEach(n => set.Destroy(n));
            }
        }

        [Test]
        public void ComplexDAG_ProducesDeterministic_TraversalOrder([Values] NodeSet.RenderExecutionModel model, [Values] NodeSet.ConnectionType connectionType)
        {
            const int k_NumGraphs = 10;

            using (var set = new NodeSet())
            {
                set.RendererModel = model;

                var tests = new List<RenderGraphTests.DAGTest>(k_NumGraphs);

                for (int i = 0; i < k_NumGraphs; ++i)
                {
                    var test = new RenderGraphTests.DAGTest(set);
                    tests.Add(test);
                    if (connectionType == NodeSet.ConnectionType.Feedback)
                    {
                        // Introduce feedback connections from the test DAG's outputs back to its inputs (except for the last
                        // node which is expected not to work due to issue #331).
                        for (int j = 0; j < test.Roots.Length - 1; ++j)
                        {
                            set.Connect(
                                test.Roots[j], (set.GetDefinition(test.Roots[j]) as RenderGraphTests.IComputeNode).OutputPort,
                                test.Leaves[j], (InputPortID)RenderGraphTests.ANode.KernelPorts.ValueInput,
                                NodeSet.ConnectionType.Feedback
                            );
                        }
                    }
                }

                set.Update();
                set.DataGraph.SyncAnyRendering();

                var cache = set.DataGraph.Cache;

                // GlobalBreadthFirst == LocalDepthFirst currently.

                const string kExpectedIslandOrder =
                    "14, 28, 42, 56, 70, 84, 98, 112, 126, 140, " + // orphan group
                    "1, 2, 3, 9, 10, 11, 6, 7, 8, 4, 12, " + // primary island
                    "13, 5, " + // secondary island
                    "15, 16, 17, 23, 24, 25, 20, 21, 22, 18, 26, " + // primary island 2.. etc
                    "27, 19, " +
                    "29, 30, 31, 37, 38, 39, 34, 35, 36, 32, 40, " +
                    "41, 33, " +
                    "43, 44, 45, 51, 52, 53, 48, 49, 50, 46, 54, " +
                    "55, 47, " +
                    "57, 58, 59, 65, 66, 67, 62, 63, 64, 60, 68, " +
                    "69, 61, " +
                    "71, 72, 73, 79, 80, 81, 76, 77, 78, 74, 82, " +
                    "83, 75, " +
                    "85, 86, 87, 93, 94, 95, 90, 91, 92, 88, 96, " +
                    "97, 89, " +
                    "99, 100, 101, 107, 108, 109, 104, 105, 106, 102, 110, " +
                    "111, 103, " +
                    "113, 114, 115, 121, 122, 123, 118, 119, 120, 116, 124, " +
                    "125, 117, " +
                    "127, 128, 129, 135, 136, 137, 132, 133, 134, 130, 138, 139, 131";

                var traversalIndices = new List<string>();

                for (int g = 0; g < cache.Groups.Length; ++g)
                {
                    var group = cache.Groups[g];

                    for (int i = 0; i < group.TraversalCount; ++i)
                    {
                        traversalIndices.Add(group.IndexTraversal(i).Vertex.Versioned.Index.ToString());
                    }
                }

                var stringTraversalOrder = string.Join(", ", traversalIndices);

                switch (model)
                {
                    case NodeSet.RenderExecutionModel.MaximallyParallel:
                        Assert.AreEqual(kExpectedIslandOrder, stringTraversalOrder);
                        break;
                    case NodeSet.RenderExecutionModel.SingleThreaded:
                        Assert.AreEqual(kExpectedIslandOrder, stringTraversalOrder);
                        break;
                    case NodeSet.RenderExecutionModel.Synchronous:
                        Assert.AreEqual(kExpectedIslandOrder, stringTraversalOrder);
                        break;
                    case NodeSet.RenderExecutionModel.Islands:
                        Assert.AreEqual(kExpectedIslandOrder, stringTraversalOrder);
                        break;
                }

                tests.ForEach(t => t.Dispose());
            }
        }

        [Test]
        public void SimpleDAG_WithFeedback_ProducesDeterministic_TraversalOrder([Values] NodeSet.RenderExecutionModel model)
        {
            using (var set = new NodeSet())
            {
                set.RendererModel = model;

                /*  A ---------------- B (1)
                 *  A -------- C ----- B (2)
                 *           /   \
                 *  A - B - B      C = C (3)
                 *           \   /
                 *  A -------- C ----- B (4)
                 *  A                    (5)
                 *
                 *  with extra feedback connections going from B1 back to A3, and also B4 back to A2.
                 *
                 */

                var test = new RenderGraphTests.DAGTest(set);
                set.Connect(
                    test.Roots[0], (set.GetDefinition(test.Roots[0]) as RenderGraphTests.IComputeNode).OutputPort,
                    test.Leaves[2], (InputPortID)RenderGraphTests.ANode.KernelPorts.ValueInput,
                    NodeSet.ConnectionType.Feedback
                );
                set.Connect(
                    test.Roots[3], (set.GetDefinition(test.Roots[3]) as RenderGraphTests.IComputeNode).OutputPort,
                    test.Leaves[1], (InputPortID)RenderGraphTests.ANode.KernelPorts.ValueInput,
                    NodeSet.ConnectionType.Feedback
                );

                set.Update();
                set.DataGraph.SyncAnyRendering();

                var cache = set.DataGraph.Cache;

                // GlobalBreadthFirst == LocalDepthFirst currently.
                /*const string kExpectedMaximallyParallelOrder = "0, 2, 5, 8, 13, 1, 9, 10, 6, 3, 7, 11, 4, 12"; */
                const string kExpectedIslandOrder = "14, 1, 9, 10, 11, 6, 7, 3, 8, 4, 5, 12, 13, 2";

                var traversalIndices = new List<string>();

                for (int g = 0; g < cache.Groups.Length; ++g)
                {
                    var group = cache.Groups[g];

                    for (int i = 0; i < group.TraversalCount; ++i)
                    {
                        traversalIndices.Add(group.IndexTraversal(i).Vertex.Versioned.Index.ToString());
                    }
                }

                var stringTraversalOrder = string.Join(", ", traversalIndices);

                switch (model)
                {
                    case NodeSet.RenderExecutionModel.MaximallyParallel:
                        Assert.AreEqual(kExpectedIslandOrder, stringTraversalOrder);
                        break;
                    case NodeSet.RenderExecutionModel.SingleThreaded:
                        Assert.AreEqual(kExpectedIslandOrder, stringTraversalOrder);
                        break;
                    case NodeSet.RenderExecutionModel.Synchronous:
                        Assert.AreEqual(kExpectedIslandOrder, stringTraversalOrder);
                        break;
                    case NodeSet.RenderExecutionModel.Islands:
                        Assert.AreEqual(kExpectedIslandOrder, stringTraversalOrder);
                        break;
                }

                test.Dispose();
            }
        }

        // Issue #484
        // Note: The required width to cause stack overflow is smaller here than the non-NodeSet version of this test as
        // the types in the NodeSet topology DB are larger (sizeof(ValidatedHandle) > sizeof(Node),
        // sizeof(InputPortArrayID) > sizeof(InputPort), and sizeof(OutputPortID) > sizeof(OutputPort))
        [Test, Explicit]
        public void WideInterconnectedDAG_CanBeTraversed([Values] NodeSet.RenderExecutionModel model, [Values(5000)]int width)
        {
            using (var set = new NodeSet())
            {
                set.RendererModel = model;
                var nodes = new List<NodeHandle<NodeWithAllTypesOfPorts>>();

                // This DAG is only 2 nodes deep, but very wide.
                //  o-o
                //   /
                //  o-o
                //   /
                //  o-o
                //   /
                //...
                for (var i = 0; i < width; ++i)
                {
                    var a = set.Create<NodeWithAllTypesOfPorts>();
                    var b = set.Create<NodeWithAllTypesOfPorts>();

                    set.SetPortArraySize(b, NodeWithAllTypesOfPorts.KernelPorts.InputArrayScalar, 2);

                    set.Connect(
                        a, NodeWithAllTypesOfPorts.KernelPorts.OutputScalar,
                        b, NodeWithAllTypesOfPorts.KernelPorts.InputArrayScalar, 0
                    );

                    if (nodes.Count > 0)
                    {
                        set.Connect(
                            nodes.Last(), NodeWithAllTypesOfPorts.KernelPorts.OutputScalar,
                            b, NodeWithAllTypesOfPorts.KernelPorts.InputArrayScalar, 1
                        );
                    }

                    nodes.Add(a);
                    nodes.Add(b);
                }

                set.Update();

                nodes.ForEach(n => set.Destroy(n));
            }
        }

        [Test]
        public void CreatedConnections_OnAlreadyDestroyedNodes_AreNotReflected_InRenderingDatabase()
        {
            using (var set = new NodeSet())
            {
                var test = new RenderGraphTests.DAGTest(set);
                test.Dispose();

                set.Update();
                set.DataGraph.SyncAnyRendering();

                var database = set.DataGraph.GetDatabase_ForTesting();

                Assert.AreEqual(Topology.Database.InvalidConnection + 1, database.TotalConnections);
                Assert.Zero(database.FreeEdges);
            }
        }
    }
}
