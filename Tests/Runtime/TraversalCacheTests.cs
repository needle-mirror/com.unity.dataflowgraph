using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine.TestTools;

namespace Unity.DataFlowGraph.Tests
{
    using Topology = TopologyAPI<Node, InputPort, OutputPort>;
    using Map = TopologyTestDatabase.NodeList;

    /*
     * TODO - tests:
     * - Tests of noise in the node list. Right now, list fed to topology cache == global list of nodes.
     */

    class TraversalCacheTests
    {
        static readonly InputPort k_InputOne = new InputPort(234);
        static readonly InputPort k_InputTwo = new InputPort(345);
        static readonly InputPort k_InputThree = new InputPort(666);
        static readonly OutputPort k_OutputOne = new OutputPort(456);
        static readonly OutputPort k_OutputTwo = new OutputPort(567);

        static readonly InputPort k_DifferentInput = new InputPort(2341);
        static readonly OutputPort k_DifferentOutput = new OutputPort(3145);

        public enum ComputeType
        {
            NonJobified,
            Jobified
        }

        [Flags]
        public enum TraversalType
        {
            Normal = 1 << 0,
            Different = 1 << 1
        }

        class Test : IDisposable
        {
            public NativeList<Node> Nodes;
            public Topology.TraversalCache Cache;
            public TopologyTestDatabase TestDatabase;
            public Topology.CacheAPI.VersionTracker Version;

            Topology.CacheAPI.ComputationOptions m_Options;
            Topology.SortingAlgorithm m_Algorithm;

            public Test(Topology.SortingAlgorithm algo, ComputeType computingType, uint traversalMask, uint alternateMask)
            {
                Cache = new Topology.TraversalCache(0, traversalMask, alternateMask);
                Nodes = new NativeList<Node>(10, Allocator.Temp);
                TestDatabase = new TopologyTestDatabase(Allocator.Temp);
                m_Options = Topology.CacheAPI.ComputationOptions.Create(computeJobified: computingType == ComputeType.Jobified);
                Version = Topology.CacheAPI.VersionTracker.Create();
                m_Algorithm = algo;
            }

            public Test(Topology.SortingAlgorithm algo, ComputeType computingType, uint traversalMask)
                : this(algo, computingType, traversalMask, traversalMask)
            {
            }

            public Test(Topology.SortingAlgorithm algo, ComputeType computingType)
                : this(algo, computingType, Topology.TraversalCache.TraverseAllMask)
            {
            }

            public Topology.TraversalCache GetUpdatedCache()
            {
                Version.SignalTopologyChanged();
                Topology.ComputationContext<Map> context;

                var dependency = Topology.ComputationContext<Map>.InitializeContext(
                    new JobHandle(),
                    out context,
                    TestDatabase.Connections,
                    TestDatabase.Nodes,
                    Cache,
                    Nodes.AsArray(),
                    Version,
                    m_Algorithm
                );

                dependency.Complete();
                Topology.CacheAPI.UpdateCacheInline(Version, m_Options, ref context);
                context.Dispose();

                return Cache;
            }

            public Topology.CacheWalker GetWalker()
            {
                return new Topology.CacheWalker(GetUpdatedCache());
            }

            public void Dispose()
            {
                Cache.Dispose();
                Nodes.Dispose();
                TestDatabase.Dispose();
            }

            Node CreateAndAddNewNode()
            {
                var ret = TestDatabase.CreateNode();
                Nodes.Add(ret);
                return ret;
            }

            public void CreateTestDAG()
            {
                /*  DAG test diagram.
                 *  Flow from left to right.
                 *  
                 *  A ---------------- B (1)
                 *  A -------- C ----- B (2)
                 *           /   \
                 *  A - B - B      C = C (3)
                 *           \   /
                 *  A -------- C ----- B (4)
                 *  A                    (5)
                 * 
                 *  Contains nodes not connected anywhere.
                 *  Contains multiple children (tree), and multiple parents (DAG).
                 *  Contains multiple connected components.
                 *  Contains diamond.
                 *  Contains more than one connection between the same nodes.
                 *  Contains opportunities for batching, and executing paths in series.
                 *  Contains multiple connections from the same output.
                 */

                var Leaves = new Node[5];

                // Part (1) of the graph.
                Leaves[0] = CreateAndAddNewNode();
                var b1 = CreateAndAddNewNode();
                TestDatabase.Connect(Leaves[0], k_OutputOne, b1, k_InputOne);

                // Part (2) of the graph.
                Leaves[1] = CreateAndAddNewNode();
                var c2 = CreateAndAddNewNode();
                var b2 = CreateAndAddNewNode();

                TestDatabase.Connect(Leaves[1], k_OutputOne, c2, k_InputOne);
                TestDatabase.Connect(c2, k_OutputOne, b2, k_InputOne);

                // Part (4) of the graph.
                Leaves[3] = CreateAndAddNewNode();
                var c4 = CreateAndAddNewNode();
                var b4 = CreateAndAddNewNode();

                TestDatabase.Connect(Leaves[3], k_OutputOne, c4, k_InputOne);
                TestDatabase.Connect(c4, k_OutputOne, b4, k_InputOne);

                // Part (3) of the graph.
                Leaves[2] = CreateAndAddNewNode();
                var b3_1 = CreateAndAddNewNode();
                var b3_2 = CreateAndAddNewNode();
                var c3_1 = CreateAndAddNewNode();
                var c3_2 = CreateAndAddNewNode();

                TestDatabase.Connect(Leaves[2], k_OutputOne, b3_1, k_InputOne);
                TestDatabase.Connect(b3_1, k_OutputOne, b3_2, k_InputOne);
                TestDatabase.Connect(b3_2, k_OutputOne, c2, k_InputTwo);
                TestDatabase.Connect(b3_2, k_OutputOne, c4, k_InputTwo);

                TestDatabase.Connect(c2, k_OutputOne, c3_1, k_InputOne);
                TestDatabase.Connect(c4, k_OutputOne, c3_1, k_InputTwo);

                TestDatabase.Connect(c3_1, k_OutputOne, c3_2, k_InputOne);
                TestDatabase.Connect(c3_1, k_OutputOne, c3_2, k_InputTwo);

                // Part (5) of the graph.
                Leaves[4] = CreateAndAddNewNode();
            }
        }

        [Test]
        public void MultipleIsolatedGraphs_CanStillBeWalked_InOneCompleteOrder([Values] Topology.SortingAlgorithm algo, [Values] ComputeType jobified)
        {
            const int graphs = 5, nodes = 5;

            using (var test = new Test(algo, jobified))
            {
                for (int g = 0; g < graphs; ++g)
                {
                    for (int n = 0; n < nodes; ++n)
                    {
                        test.Nodes.Add(test.TestDatabase.CreateNode());
                    }

                    for (int n = 0; n < nodes - 1; ++n)
                    {
                        test.TestDatabase.Connect(test.Nodes[0 + n + g * nodes], k_OutputOne, test.Nodes[1 + n + g * nodes], k_InputOne);
                    }

                }

                foreach (var node in test.GetWalker())
                {
                    for (int g = 0; g < graphs; ++g)
                    {
                        if (node.Vertex == test.Nodes[g * nodes]) // root ?
                        {
                            // walk root, and ensure each next child is ordered with respect to original connection order (conga line)
                            var parent = node;

                            for (int n = 0; n < nodes - 1; ++n)
                            {
                                foreach (var child in parent.GetChildren())
                                {
                                    Assert.AreEqual(child.Vertex, test.Nodes[n + 1 + g * nodes]);

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
        public void TraversalCache_ForUnrelatedNodes_StillContainAllNodes([Values] Topology.SortingAlgorithm algo, [Values] ComputeType jobified)
        {
            using (var test = new Test(algo, jobified))
            {
                for (int i = 0; i < 5; ++i)
                {
                    test.Nodes.Add(test.TestDatabase.CreateNode());
                }

                var foundNodes = new List<Node>();

                foreach (var node in test.GetWalker())
                {
                    foundNodes.Add(node.Vertex);
                }

                for (int i = 0; i < test.Nodes.Length; ++i)
                    CollectionAssert.Contains(foundNodes, test.Nodes[i]);

                Assert.AreEqual(test.Nodes.Length, foundNodes.Count);
            }
        }

        [Test]
        public void CacheUpdate_IsComputed_InExpectedExecutionVehicle([Values] Topology.SortingAlgorithm algo, [Values] ComputeType jobified)
        {
            using (var test = new Test(algo, jobified))
            {
                for (int i = 0; i < 5; ++i)
                    test.Nodes.Add(test.TestDatabase.CreateNode());

                test.GetUpdatedCache();

                Assert.AreNotEqual(BurstConfig.ExecutionResult.Undefined, test.TestDatabase.Nodes.GetLastExecutionEngine());

                if (!BurstConfig.IsBurstEnabled)
                    Assert.AreEqual(BurstConfig.ExecutionResult.InsideMono, test.TestDatabase.Nodes.GetLastExecutionEngine());
                else
                    Assert.AreEqual(jobified == ComputeType.Jobified, test.TestDatabase.Nodes.GetLastExecutionEngine() == BurstConfig.ExecutionResult.InsideBurst);
            }
        }

        [Test]
        public void TraversalCacheWalkers_AreProperlyCleared_AfterAllNodesAreDestroyed([Values] Topology.SortingAlgorithm algo, [Values] ComputeType jobified)
        {
            using (var test = new Test(algo, jobified))
            {
                for (int i = 0; i < 5; ++i)
                {
                    test.Nodes.Add(test.TestDatabase.CreateNode());
                }

                var foundNodes = new List<Node>();
                test.GetUpdatedCache();

                test.TestDatabase.DestroyAllNodes();

                test.Nodes.Clear();

                var cache = test.GetUpdatedCache();

                Assert.AreEqual(0, cache.Leaves.Length);
                Assert.AreEqual(0, cache.Roots.Length);
                Assert.AreEqual(0, cache.OrderedTraversal.Length);
                Assert.AreEqual(0, cache.ParentTable.Length);
                Assert.AreEqual(0, cache.ChildTable.Length);

                Assert.AreEqual(0, new Topology.CacheWalker(cache).Count);
                Assert.AreEqual(0, new Topology.RootCacheWalker(cache).Count);
                Assert.AreEqual(0, new Topology.LeafCacheWalker(cache).Count);
            }
        }

        [Test]
        public void CanFindParentsAndChildren_ByPort([Values] Topology.SortingAlgorithm algo, [Values] ComputeType jobified)
        {
            using (var test = new Test(algo, jobified))
            {
                for (int i = 0; i < 5; ++i)
                {
                    test.Nodes.Add(test.TestDatabase.CreateNode());
                }

                test.TestDatabase.Connect(test.Nodes[0], k_OutputOne, test.Nodes[2], k_InputOne);
                test.TestDatabase.Connect(test.Nodes[1], k_OutputOne, test.Nodes[2], k_InputTwo);

                test.TestDatabase.Connect(test.Nodes[2], k_OutputOne, test.Nodes[3], k_InputOne);
                test.TestDatabase.Connect(test.Nodes[2], k_OutputTwo, test.Nodes[4], k_InputOne);

                bool centerNodeWasFound = false;

                foreach (var node in test.GetWalker())
                {
                    if (node.Vertex == test.Nodes[2])
                    {
                        for (int i = 0; i < 2; ++i)
                            foreach (var parent in node.GetParentsByPort(i == 0 ? k_InputOne : k_InputTwo))
                                Assert.AreEqual(test.Nodes[i], parent.Vertex);

                        for (int i = 0; i < 2; ++i)
                            foreach (var child in node.GetChildrenByPort(i == 0 ? k_OutputOne : k_OutputTwo))
                                Assert.AreEqual(test.Nodes[i + 3], child.Vertex);

                        centerNodeWasFound = true;
                        break;
                    }
                }

                Assert.IsTrue(centerNodeWasFound, "Couldn't find middle of graph");
            }
        }

        [Test]
        public void CanFindParentsAndChildren_ByPort_ThroughConnection([Values] Topology.SortingAlgorithm algo, [Values] ComputeType jobified)
        {
            using (var test = new Test(algo, jobified))
            {
                for (int i = 0; i < 5; ++i)
                {
                    test.Nodes.Add(test.TestDatabase.CreateNode());
                }

                test.TestDatabase.Connect(test.Nodes[0], k_OutputOne, test.Nodes[2], k_InputOne);
                test.TestDatabase.Connect(test.Nodes[1], k_OutputOne, test.Nodes[2], k_InputTwo);

                test.TestDatabase.Connect(test.Nodes[2], k_OutputOne, test.Nodes[3], k_InputOne);
                test.TestDatabase.Connect(test.Nodes[2], k_OutputTwo, test.Nodes[4], k_InputOne);

                bool centerNodeWasFound = false;

                foreach (var node in test.GetWalker())
                {
                    if (node.Vertex == test.Nodes[2])
                    {
                        for (int i = 0; i < 2; ++i)
                            foreach (var parentConnection in node.GetParentConnectionsByPort(i == 0 ? k_InputOne : k_InputTwo))
                                Assert.AreEqual(test.Nodes[i], parentConnection.Target.Vertex);

                        for (int i = 0; i < 2; ++i)
                            foreach (var childConnection in node.GetChildConnectionsByPort(i == 0 ? k_OutputOne : k_OutputTwo))
                                Assert.AreEqual(test.Nodes[i + 3], childConnection.Target.Vertex);

                        centerNodeWasFound = true;
                        break;
                    }
                }

                Assert.IsTrue(centerNodeWasFound, "Couldn't find middle of graph");
            }
        }

        [Test]
        public void PortIndices_OnCacheConnections_MatchesOriginalTopology([Values] Topology.SortingAlgorithm algo, [Values] ComputeType jobified)
        {
            using (var test = new Test(algo, jobified))
            {
                for (int i = 0; i < 5; ++i)
                {
                    test.Nodes.Add(test.TestDatabase.CreateNode());
                }

                test.TestDatabase.Connect(test.Nodes[0], k_OutputOne, test.Nodes[2], k_InputOne);
                test.TestDatabase.Connect(test.Nodes[1], k_OutputOne, test.Nodes[2], k_InputTwo);

                test.TestDatabase.Connect(test.Nodes[2], k_OutputOne, test.Nodes[3], k_InputOne);
                test.TestDatabase.Connect(test.Nodes[2], k_OutputTwo, test.Nodes[4], k_InputOne);

                bool centerNodeWasFound = false;

                foreach (var node in test.GetWalker())
                {
                    if (node.Vertex == test.Nodes[2])
                    {
                        var inPorts = new[] { k_InputOne, k_InputTwo };
                        var outPorts = new[] { k_OutputOne, k_OutputTwo };
                        for (int i = 0; i < 2; ++i)
                            foreach (var parentConnection in node.GetParentConnectionsByPort(inPorts[i]))
                            {
                                Assert.AreEqual(k_OutputOne, parentConnection.OutputPort);
                                Assert.AreEqual(inPorts[i], parentConnection.InputPort);
                            }

                        for (int i = 0; i < 2; ++i)
                            foreach (var childConnection in node.GetChildConnectionsByPort(outPorts[i]))
                            {
                                Assert.AreEqual(k_InputOne, childConnection.InputPort);
                                Assert.AreEqual(outPorts[i], childConnection.OutputPort);
                            }

                        centerNodeWasFound = true;
                        break;
                    }
                }

                Assert.IsTrue(centerNodeWasFound, "Couldn't find middle of graph");
            }
        }

        [Test]
        public void ParentsAndChildrenWalkers_HasCorrectCounts([Values] Topology.SortingAlgorithm algo, [Values] ComputeType jobified)
        {
            using (var test = new Test(algo, jobified))
            {
                for (int i = 0; i < 5; ++i)
                {
                    test.Nodes.Add(test.TestDatabase.CreateNode());
                }

                test.TestDatabase.Connect(test.Nodes[0], k_OutputOne, test.Nodes[2], k_InputOne);
                test.TestDatabase.Connect(test.Nodes[1], k_OutputOne, test.Nodes[2], k_InputTwo);

                test.TestDatabase.Connect(test.Nodes[2], k_OutputOne, test.Nodes[3], k_InputOne);
                test.TestDatabase.Connect(test.Nodes[2], k_OutputTwo, test.Nodes[4], k_InputOne);

                bool centerNodeWasFound = false;

                foreach (var node in test.GetWalker())
                {
                    if (node.Vertex == test.Nodes[2])
                    {
                        Assert.AreEqual(2, node.GetParents().Count);
                        Assert.AreEqual(1, node.GetParentsByPort(k_InputOne).Count);
                        Assert.AreEqual(1, node.GetParentsByPort(k_InputTwo).Count);

                        Assert.AreEqual(2, node.GetChildren().Count);
                        Assert.AreEqual(1, node.GetChildrenByPort(k_OutputOne).Count);
                        Assert.AreEqual(1, node.GetChildrenByPort(k_OutputTwo).Count);

                        centerNodeWasFound = true;
                        break;
                    }
                }

                Assert.IsTrue(centerNodeWasFound, "Couldn't find middle of graph");
            }
        }

        [Test]
        public void ParentAndChildConnections_HasCorrectCounts([Values] Topology.SortingAlgorithm algo, [Values] ComputeType jobified)
        {
            using (var test = new Test(algo, jobified))
            {
                for (int i = 0; i < 5; ++i)
                {
                    test.Nodes.Add(test.TestDatabase.CreateNode());
                }

                test.TestDatabase.Connect(test.Nodes[0], k_OutputOne, test.Nodes[2], k_InputOne);
                test.TestDatabase.Connect(test.Nodes[1], k_OutputOne, test.Nodes[2], k_InputTwo);

                test.TestDatabase.Connect(test.Nodes[2], k_OutputOne, test.Nodes[3], k_InputOne);
                test.TestDatabase.Connect(test.Nodes[2], k_OutputTwo, test.Nodes[4], k_InputOne);

                bool centerNodeWasFound = false;

                foreach (var node in test.GetWalker())
                {
                    if (node.Vertex == test.Nodes[2])
                    {
                        Assert.AreEqual(2, node.GetParentConnections().Count);
                        Assert.AreEqual(1, node.GetParentConnectionsByPort(k_InputOne).Count);
                        Assert.AreEqual(1, node.GetParentConnectionsByPort(k_InputTwo).Count);

                        Assert.AreEqual(2, node.GetChildConnections().Count);
                        Assert.AreEqual(1, node.GetChildConnectionsByPort(k_OutputOne).Count);
                        Assert.AreEqual(1, node.GetChildConnectionsByPort(k_OutputTwo).Count);

                        centerNodeWasFound = true;
                        break;
                    }
                }

                Assert.IsTrue(centerNodeWasFound, "Couldn't find middle of graph");
            }
        }

        [Test]
        public void CanFindParentsAndChildren([Values] Topology.SortingAlgorithm algo, [Values] ComputeType jobified)
        {
            using (var test = new Test(algo, jobified))
            {
                for (int i = 0; i < 5; ++i)
                {
                    test.Nodes.Add(test.TestDatabase.CreateNode());
                }


                test.TestDatabase.Connect(test.Nodes[0], k_OutputOne, test.Nodes[2], k_InputOne);
                test.TestDatabase.Connect(test.Nodes[1], k_OutputOne, test.Nodes[2], k_InputTwo);

                test.TestDatabase.Connect(test.Nodes[2], k_OutputOne, test.Nodes[3], k_InputOne);
                test.TestDatabase.Connect(test.Nodes[2], k_OutputTwo, test.Nodes[4], k_InputOne);

                bool centerNodeWasFound = false;

                foreach (var node in test.GetWalker())
                {
                    if (node.Vertex == test.Nodes[2])
                    {
                        var parents = new List<Node>();

                        foreach (var parent in node.GetParents())
                            parents.Add(parent.Vertex);

                        var children = new List<Node>();

                        foreach (var child in node.GetChildren())
                            children.Add(child.Vertex);

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
        public void RootsAndLeaves_InternalIndices_AreRegistrered([Values] Topology.SortingAlgorithm algo, [Values] ComputeType jobified)
        {
            using (var test = new Test(algo, jobified))
            {

                for (int i = 0; i < 5; ++i)
                {
                    test.Nodes.Add(test.TestDatabase.CreateNode());
                }

                test.TestDatabase.Connect(test.Nodes[0], k_OutputOne, test.Nodes[2], k_InputOne);
                test.TestDatabase.Connect(test.Nodes[1], k_OutputOne, test.Nodes[2], k_InputTwo);

                test.TestDatabase.Connect(test.Nodes[2], k_OutputOne, test.Nodes[3], k_InputOne);
                test.TestDatabase.Connect(test.Nodes[2], k_OutputTwo, test.Nodes[4], k_InputOne);

                var cache = test.GetUpdatedCache();

                var rootWalker = new Topology.RootCacheWalker(cache);
                var leafWalker = new Topology.LeafCacheWalker(cache);

                var roots = new List<Node>();
                var leaves = new List<Node>();

                Assert.AreEqual(rootWalker.Count, 2);
                Assert.AreEqual(leafWalker.Count, 2);

                foreach (var nodeCache in rootWalker)
                    roots.Add(nodeCache.Vertex);

                foreach (var nodeCache in leafWalker)
                    leaves.Add(nodeCache.Vertex);

                CollectionAssert.Contains(leaves, test.Nodes[0]);
                CollectionAssert.Contains(leaves, test.Nodes[1]);
                CollectionAssert.Contains(roots, test.Nodes[3]);
                CollectionAssert.Contains(roots, test.Nodes[4]);
            }
        }

        [Test]
        public void RootAndLeafCacheWalker_WalksRootsAndLeaves([Values] Topology.SortingAlgorithm algo, [Values] ComputeType jobified)
        {
            using (var test = new Test(algo, jobified))
            {

                for (int i = 0; i < 5; ++i)
                {
                    test.Nodes.Add(test.TestDatabase.CreateNode());
                }

                test.TestDatabase.Connect(test.Nodes[0], k_OutputOne, test.Nodes[2], k_InputOne);
                test.TestDatabase.Connect(test.Nodes[1], k_OutputOne, test.Nodes[2], k_InputTwo);

                test.TestDatabase.Connect(test.Nodes[2], k_OutputOne, test.Nodes[3], k_InputOne);
                test.TestDatabase.Connect(test.Nodes[2], k_OutputTwo, test.Nodes[4], k_InputOne);

                var cache = test.GetUpdatedCache();

                Assert.AreEqual(cache.Leaves.Length, 2);
                Assert.AreEqual(cache.Roots.Length, 2);

                var roots = new List<Node>();

                for (int i = 0; i < cache.Leaves.Length; ++i)
                    roots.Add(cache.OrderedTraversal[cache.Roots[i]].Vertex);

                var leaves = new List<Node>();

                for (int i = 0; i < cache.Roots.Length; ++i)
                    leaves.Add(cache.OrderedTraversal[cache.Leaves[i]].Vertex);

                CollectionAssert.Contains(leaves, test.Nodes[0]);
                CollectionAssert.Contains(leaves, test.Nodes[1]);
                CollectionAssert.Contains(roots, test.Nodes[3]);
                CollectionAssert.Contains(roots, test.Nodes[4]);
            }
        }

        [Test]
        public void IslandNodes_RegisterBothAsLeafAndRoot([Values] Topology.SortingAlgorithm algo, [Values] ComputeType jobified)
        {
            using (var test = new Test(algo, jobified))
            {
                test.Nodes.Add(test.TestDatabase.CreateNode());

                var cache = test.GetUpdatedCache();

                Assert.AreEqual(cache.Leaves.Length, 1);
                Assert.AreEqual(cache.Roots.Length, 1);

                var roots = new List<Node>();

                for (int i = 0; i < cache.Leaves.Length; ++i)
                    roots.Add(cache.OrderedTraversal[cache.Roots[i]].Vertex);

                var leaves = new List<Node>();

                for (int i = 0; i < cache.Roots.Length; ++i)
                    leaves.Add(cache.OrderedTraversal[cache.Leaves[i]].Vertex);

                CollectionAssert.Contains(leaves, test.Nodes[0]);
                CollectionAssert.Contains(roots, test.Nodes[0]);
            }
        }

        [Test]
        public void CanCongaWalkAndDependenciesAreInCorrectOrder([Values] Topology.SortingAlgorithm algo, [Values] ComputeType jobified)
        {
            using (var test = new Test(algo, jobified))
            {
                var node1 = test.TestDatabase.CreateNode();
                var node2 = test.TestDatabase.CreateNode();
                var node3 = test.TestDatabase.CreateNode();

                test.Nodes.Add(node1);
                test.Nodes.Add(node2);
                test.Nodes.Add(node3);

                test.TestDatabase.Connect(node1, k_OutputOne, node2, k_InputOne);
                test.TestDatabase.Connect(node2, k_OutputOne, node3, k_InputOne);

                var index = 0;

                foreach (var node in test.GetWalker())
                {
                    Assert.AreEqual(node.CacheIndex, index);
                    Assert.AreEqual(node.Vertex, test.Nodes[index]);

                    index++;
                }

            }
        }

        [Test]
        public void TestInternalIndices([Values] Topology.SortingAlgorithm algo, [Values] ComputeType jobified)
        {
            using (var test = new Test(algo, jobified))
            {
                var node1 = test.TestDatabase.CreateNode();
                var node2 = test.TestDatabase.CreateNode();
                var node3 = test.TestDatabase.CreateNode();

                test.Nodes.Add(node1);
                test.Nodes.Add(node2);
                test.Nodes.Add(node3);

                Assert.DoesNotThrow(() => test.TestDatabase.Connect(node2, k_OutputOne, node1, k_InputOne));
                Assert.DoesNotThrow(() => test.TestDatabase.Connect(node3, k_OutputOne, node1, k_InputThree));

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
                            Assert.IsTrue(parent.Vertex == node2 || parent.Vertex == node3);
                        }
                    }

                    if (entryIndex == 0 || entryIndex == 1)
                    {
                        Assert.AreEqual(1, node.GetChildren().Count);

                        foreach (var child in node.GetChildren())
                        {
                            Assert.AreEqual(node1, child.Vertex);
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
        public void TraversalCache_DoesNotInclude_IgnoredTraversalTypes([Values] Topology.SortingAlgorithm algo, [Values] ComputeType computeType, [Values] TraversalType traversalType)
        {
            using (var test = new Test(algo, computeType, (uint)traversalType))
            {
                int numParents = traversalType == TraversalType.Different ? 2 : 0;
                int numChildren = traversalType == TraversalType.Normal ? 2 : 0;

                for (int i = 0; i < 5; ++i)
                {
                    test.Nodes.Add(test.TestDatabase.CreateNode());
                }

                test.TestDatabase.Connect((uint)TraversalType.Different, test.Nodes[0], k_DifferentOutput, test.Nodes[2], k_DifferentInput);
                test.TestDatabase.Connect((uint)TraversalType.Different, test.Nodes[1], k_DifferentOutput, test.Nodes[2], k_DifferentInput);

                test.TestDatabase.Connect((uint)TraversalType.Normal, test.Nodes[2], k_OutputOne, test.Nodes[3], k_InputOne);
                test.TestDatabase.Connect((uint)TraversalType.Normal, test.Nodes[2], k_OutputOne, test.Nodes[4], k_InputOne);

                bool centerNodeWasFound = false;

                foreach (var node in test.GetWalker())
                {
                    if (node.Vertex == test.Nodes[2])
                    {
                        Assert.AreEqual(0, node.GetParentsByPort(k_InputOne).Count);
                        Assert.AreEqual(numParents, node.GetParentsByPort(k_DifferentInput).Count);

                        Assert.AreEqual(0, node.GetChildrenByPort(k_DifferentOutput).Count);
                        Assert.AreEqual(numChildren, node.GetChildrenByPort(k_OutputOne).Count);

                        centerNodeWasFound = true;
                        break;
                    }
                }

                Assert.IsTrue(centerNodeWasFound, "Couldn't find middle of graph");
            }
        }

        void AssertAreSame(List<Node> nodes, Topology.InputVertexCacheWalker vertices)
        {
            Assert.AreEqual(nodes.Count, vertices.Count);
            foreach (var vertex in vertices)
                CollectionAssert.Contains(nodes, vertex.Vertex);
        }

        void AssertAreSame(List<Node> nodes, Topology.OutputVertexCacheWalker vertices)
        {
            Assert.AreEqual(nodes.Count, vertices.Count);
            foreach (var vertex in vertices)
                CollectionAssert.Contains(nodes, vertex.Vertex);
        }

        [Test]
        public void AlternateDependencies_CanDiffer_FromTraversal([Values] Topology.SortingAlgorithm algo, [Values] ComputeType computeType)
        {
            using (var test = new Test(algo, computeType, (uint) TraversalType.Normal, (uint) TraversalType.Different))
            {
                for (int i = 0; i < 4; ++i)
                    test.Nodes.Add(test.TestDatabase.CreateNode());

                // Setup normal traversal dependencies
                test.TestDatabase.Connect((uint)TraversalType.Normal, test.Nodes[0], k_OutputOne, test.Nodes[2], k_InputOne);
                test.TestDatabase.Connect((uint)TraversalType.Normal, test.Nodes[2], k_OutputOne, test.Nodes[3], k_InputOne);
                test.TestDatabase.Connect((uint)TraversalType.Normal, test.Nodes[0], k_OutputOne, test.Nodes[3], k_InputOne);

                // Setup an alternate dependency hierarchy
                test.TestDatabase.Connect((uint)TraversalType.Different, test.Nodes[3], k_OutputOne, test.Nodes[2], k_InputOne);
                test.TestDatabase.Connect((uint)TraversalType.Different, test.Nodes[2], k_OutputOne, test.Nodes[1], k_InputOne);
                test.TestDatabase.Connect((uint)TraversalType.Different, test.Nodes[1], k_OutputOne, test.Nodes[3], k_InputOne);

                foreach (var node in test.GetWalker())
                {
                    if (node.Vertex == test.Nodes[0])
                    {
                        Assert.Zero(node.GetParents().Count);
                        AssertAreSame(new List<Node>{ test.Nodes[2], test.Nodes[3] }, node.GetChildren());
                        Assert.Zero(node.GetParents(Topology.TraversalCache.Hierarchy.Alternate).Count);
                        Assert.Zero(node.GetChildren(Topology.TraversalCache.Hierarchy.Alternate).Count);
                    }
                    if (node.Vertex == test.Nodes[1])
                    {
                        Assert.Zero(node.GetParents().Count);
                        Assert.Zero(node.GetChildren().Count);
                        AssertAreSame(new List<Node>{ test.Nodes[2] }, node.GetParents(Topology.TraversalCache.Hierarchy.Alternate));
                        AssertAreSame(new List<Node>{ test.Nodes[3] }, node.GetChildren(Topology.TraversalCache.Hierarchy.Alternate));
                    }
                    if (node.Vertex == test.Nodes[2])
                    {
                        AssertAreSame(new List<Node>{ test.Nodes[0] }, node.GetParents());
                        AssertAreSame(new List<Node>{ test.Nodes[3] }, node.GetChildren());
                        AssertAreSame(new List<Node>{ test.Nodes[3] }, node.GetParents(Topology.TraversalCache.Hierarchy.Alternate));
                        AssertAreSame(new List<Node>{ test.Nodes[1] }, node.GetChildren(Topology.TraversalCache.Hierarchy.Alternate));
                    }
                    if (node.Vertex == test.Nodes[3])
                    {
                        AssertAreSame(new List<Node>{ test.Nodes[0], test.Nodes[2] }, node.GetParents());
                        Assert.Zero(node.GetChildren().Count);
                        AssertAreSame(new List<Node>{ test.Nodes[1] }, node.GetParents(Topology.TraversalCache.Hierarchy.Alternate));
                        AssertAreSame(new List<Node>{ test.Nodes[2] }, node.GetChildren(Topology.TraversalCache.Hierarchy.Alternate));
                    }
                }
            }
        }

        [Test]
        public void CompletelyCyclicDataGraph_ProducesAvailableError([Values] Topology.SortingAlgorithm algo, [Values] ComputeType computeType)
        {
            using (var test = new Test(algo, computeType))
            {
                test.Nodes.Add(test.TestDatabase.CreateNode());
                test.Nodes.Add(test.TestDatabase.CreateNode());

                test.TestDatabase.Connect(test.Nodes[0], k_OutputOne, test.Nodes[1], k_InputOne);
                test.TestDatabase.Connect(test.Nodes[1], k_OutputOne, test.Nodes[0], k_InputOne);

                var cache = test.GetUpdatedCache();

                Assert.AreEqual(1, cache.Errors.Length);
                Assert.AreEqual(Topology.TraversalCache.Error.Cycles, cache.Errors[0]);
            }
        }

        [Test]
        public void PartlyCyclicDataGraph_ProducesDeferredError([Values] Topology.SortingAlgorithm algo, [Values] ComputeType computeType)
        {
            using (var test = new Test(algo, computeType))
            {
                test.Nodes.Add(test.TestDatabase.CreateNode());
                test.Nodes.Add(test.TestDatabase.CreateNode());
                test.Nodes.Add(test.TestDatabase.CreateNode());

                test.TestDatabase.Connect(test.Nodes[0], k_OutputOne, test.Nodes[1], k_InputOne);
                test.TestDatabase.Connect(test.Nodes[1], k_OutputOne, test.Nodes[0], k_InputOne);
                test.TestDatabase.Connect(test.Nodes[2], k_OutputOne, test.Nodes[0], k_InputOne);

                var cache = test.GetUpdatedCache();

                Assert.AreEqual(1, cache.Errors.Length);
                Assert.AreEqual(Topology.TraversalCache.Error.Cycles, cache.Errors[0]);
            }
        }

        [Test]
        public void DeepImplicitlyCyclicDataGraph_ProducesDeferredError([Values] Topology.SortingAlgorithm algo, [Values] ComputeType computeType, [Values(0, 1, 10, 13, 100)] int depth)
        {
            using (var test = new Test(algo, computeType))
            {
                // create three branches
                Node
                    a = test.TestDatabase.CreateNode(),
                    b = test.TestDatabase.CreateNode(),
                    c = test.TestDatabase.CreateNode();

                // intertwine
                test.TestDatabase.Connect(a, k_OutputOne, b, k_InputOne);
                test.TestDatabase.Connect(b, k_OutputOne, c, k_InputOne);

                test.Nodes.Add(a);
                test.Nodes.Add(b);
                test.Nodes.Add(c);

                // fork off ->
                // o-o-o-o-o-o-o-o ...
                // |
                // o-o-o-o-o-o-o-o ...
                // |
                // o-o-o-o-o-o-o-o ...
                for (int i = 0; i < depth; ++i)
                {
                    a = test.TestDatabase.CreateNode();
                    b = test.TestDatabase.CreateNode();
                    c = test.TestDatabase.CreateNode();

                    test.TestDatabase.Connect(test.Nodes[i * 3 + 0], k_OutputOne, a, k_InputOne);
                    test.TestDatabase.Connect(test.Nodes[i * 3 + 1], k_OutputOne, b, k_InputOne);
                    test.TestDatabase.Connect(test.Nodes[i * 3 + 2], k_OutputOne, c, k_InputOne);

                    test.Nodes.Add(a);
                    test.Nodes.Add(b);
                    test.Nodes.Add(c);
                }

                // connect very last node to start, forming a cycle
                // -> o-o-o-o-o-o-o-o-> 
                // |  |
                // |  o-o-o-o-o-o-o-o-> 
                // |  |
                // |  o-o-o-o-o-o-o-o 
                // -----------------| 
                test.TestDatabase.Connect(test.Nodes[test.Nodes.Length - 1], k_OutputOne, test.Nodes[0], k_InputOne);

                var cache = test.GetUpdatedCache();

                Assert.AreEqual(1, cache.Errors.Length);
                Assert.AreEqual(Topology.TraversalCache.Error.Cycles, cache.Errors[0]);
            }
        }

        [Test]
        public void ComplexDAG_ProducesDeterministic_TraversalOrder([Values] Topology.SortingAlgorithm algo, [Values] ComputeType jobified)
        {
            const int k_NumGraphs = 10;

            using (var test = new Test(algo, jobified))
            {
                for (int i = 0; i < k_NumGraphs; ++i)
                {
                    test.CreateTestDAG();
                }

                var cache = test.GetUpdatedCache();

                const string kExpectedMaximallyParallelOrder = 
                    "0, 2, 5, 8, 13, 14, 16, 19, 22, 27, 28, 30, 33, 36, " +
                    "41, 42, 44, 47, 50, 55, 56, 58, 61, 64, 69, 70, 72, 75, " +
                    "78, 83, 84, 86, 89, 92, 97, 98, 100, 103, 106, 111, 112, 114, " +
                    "117, 120, 125, 126, 128, 131, 134, 139, 1, 9, 15, 23, 29, 37, " +
                    "43, 51, 57, 65, 71, 79, 85, 93, 99, 107, 113, 121, 127, " +
                    "135, 10, 24, 38, 52, 66, 80, 94, 108, 122, 136, 6, 3, 20, " +
                    "17, 34, 31, 48, 45, 62, 59, 76, 73, 90, 87, 104, 101, 118, " +
                    "115, 132, 129, 7, 11, 4, 21, 25, 18, 35, 39, 32, 49, 53, " +
                    "46, 63, 67, 60, 77, 81, 74, 91, 95, 88, 105, 109, 102, 119, " +
                    "123, 116, 133, 137, 130, 12, 26, 40, 54, 68, 82, 96, 110, 124, 138";

                const string kExpectedIslandOrder = 
                    "0, 1, 2, 8, 9, 10, 5, 6, 7, 3, 11, 12, 4, 13, " +
                    "14, 15, 16, 22, 23, 24, 19, 20, 21, 17, 25, 26, 18, 27, " +
                    "28, 29, 30, 36, 37, 38, 33, 34, 35, 31, 39, 40, 32, 41, " +
                    "42, 43, 44, 50, 51, 52, 47, 48, 49, 45, 53, 54, 46, 55, " +
                    "56, 57, 58, 64, 65, 66, 61, 62, 63, 59, 67, 68, 60, 69, " +
                    "70, 71, 72, 78, 79, 80, 75, 76, 77, 73, 81, 82, 74, 83, " +
                    "84, 85, 86, 92, 93, 94, 89, 90, 91, 87, 95, 96, 88, 97, " +
                    "98, 99, 100, 106, 107, 108, 103, 104, 105, 101, 109, 110, 102, 111, " +
                    "112, 113, 114, 120, 121, 122, 117, 118, 119, 115, 123, 124, 116, 125, " +
                    "126, 127, 128, 134, 135, 136, 131, 132, 133, 129, 137, 138, 130, 139";

                var traversalIndices = new List<string>();

                for(int i = 0; i < cache.OrderedTraversal.Length; ++i)
                {
                    traversalIndices.Add(cache.OrderedTraversal[i].Vertex.Id.ToString());
                }

                var stringTraversalOrder = string.Join(", ", traversalIndices);

                switch (algo)
                {
                    case Topology.SortingAlgorithm.GlobalBreadthFirst:
                        Assert.AreEqual(kExpectedMaximallyParallelOrder, stringTraversalOrder);
                        break;
                    case Topology.SortingAlgorithm.LocalDepthFirst:
                        Assert.AreEqual(kExpectedIslandOrder, stringTraversalOrder);
                        break;
                }
            }
        }
    }
}
