using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.TestTools;

namespace Unity.DataFlowGraph.Tests
{
    using Topology = TopologyAPI<Node, InputPort, OutputPort>;
    using Tools = TopologyTools<Node, InputPort, OutputPort>;
    using Map = TopologyTestDatabase.NodeList;

    /*
     * TODO - tests:
     * - Tests of noise in the node list. Right now, list fed to topology cache == global list of nodes.
     * - Test hotswapping topology sorting algorithm
     * - Disposed members of TopologyDatabase
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

            public Topology.TraversalCache.Group GetGroupForNode(Node n)
            {
                return Cache.Groups[TestDatabase.Nodes[n].GroupID];
            }

            public Topology.TraversalCache.Group GetOrphanGroup()
            {
                return Cache.Groups[Topology.Database.OrphanGroupID];
            }

            public void UpdateCache_AndIgnoreErrors(int minimumGroupSize = -1)
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
                    Nodes.Length,
                    Version,
                    m_Algorithm,
                    minimumGroupSize
                );

                dependency.Complete();
                Topology.CacheAPI.UpdateCacheInline(Version, m_Options, ref context);
                context.Dispose();
            }

            public void UpdateCache_WithSpecificNodes(NativeArray<Node> nodes, int minimumGroupSize = -1)
            {
                Version.SignalTopologyChanged();
                Topology.ComputationContext<Map> context;

                var dependency = Topology.ComputationContext<Map>.InitializeContext(
                    new JobHandle(),
                    out context,
                    TestDatabase.Connections,
                    TestDatabase.Nodes,
                    Cache,
                    nodes,
                    Nodes.Length,
                    Version,
                    m_Algorithm,
                    minimumGroupSize
                );

                dependency.Complete();
                Topology.CacheAPI.UpdateCacheInline(Version, m_Options, ref context);
                context.Dispose();

                CollectionAssert.IsEmpty(GetTopologyErrors(), "Traversal cache computation had errors!");
            }

            public Topology.TraversalCache.Error[] GetTopologyErrors()
            {
                var errors = new List<Topology.TraversalCache.Error>();

                while (Cache.Errors.Count > 0)
                    errors.Add(Cache.Errors.Dequeue());

                return errors.ToArray();
            }

            public Topology.TraversalCache GetUpdatedCache(int minimumGroupSize = -1)
            {
                UpdateCache_AndIgnoreErrors(minimumGroupSize);
                CollectionAssert.IsEmpty(GetTopologyErrors(), "Traversal cache computation had errors!");
                return Cache;
            }

            public Tools.CacheWalker GetWalker(int minimumGroupSize = -1)
            {
                return new Tools.CacheWalker(GetUpdatedCache(minimumGroupSize));
            }

            public void UpdateCache(int minimumGroupSize = -1)
            {
                GetUpdatedCache(minimumGroupSize);
            }

            public void Dispose()
            {
                Cache.Dispose();
                Nodes.Dispose();
                TestDatabase.Dispose();
            }

            public Node CreateAndAddNewNode()
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
        public void DeletingNodes_AlreadyInOrphanGroup_CausesOrphanGroup_ToBeUpdatedAnyway([Values] ComputeType jobified)
        {
            const int nodes = 10;
            const int updates = 5;

            using (var test = new Test(Topology.SortingAlgorithm.LocalDepthFirst, jobified))
            {
                for (int u = 0; u < updates; ++u)
                {
                    for (int n = 0; n < nodes; ++n)
                    {
                        test.CreateAndAddNewNode();
                    }

                    test.UpdateCache();

                    Assert.AreEqual(nodes, test.GetOrphanGroup().TraversalCount, $"[{u}]");

                    for (int n = 0; n < nodes; ++n)
                    {
                        var group = test.GetGroupForNode(test.Nodes[n]);
                        Assert.AreEqual(test.GetOrphanGroup().HandleToSelf, group.HandleToSelf, $"[{u}]");
                    }

                    test.TestDatabase.DestroyAllNodes();
                    test.Nodes.Clear();

                    test.UpdateCache();
                    Assert.Zero(test.GetOrphanGroup().TraversalCount);
                }
            }
        }

        // TODO: Generalize to all sorting algorithms?
        [Test]
        public void TrashingPopulateOrphanGroup_ByOrphaningNodes_RetainsOrphanGroup([Values] ComputeType jobified)
        {
            const int k_Nodes = 10;

            using (var test = new Test(Topology.SortingAlgorithm.LocalDepthFirst, jobified))
            {

                for (int n = 0; n < k_Nodes; ++n)
                {
                    test.CreateAndAddNewNode();
                }

                test.GetWalker();

                Assert.AreEqual(k_Nodes, test.GetOrphanGroup().TraversalCount);

                for (int n = 2 /* leave one node in orphan group */; n < k_Nodes; ++n)
                {
                    test.TestDatabase.Connect(test.Nodes[n - 1], k_OutputOne, test.Nodes[n], k_InputOne);
                }

                for (int n = 2; n < k_Nodes; ++n)
                {
                    test.GetWalker();
                    Assert.AreEqual(n - 1, test.GetOrphanGroup().TraversalCount);
                    test.TestDatabase.Disconnect(test.Nodes[n - 1], k_OutputOne, test.Nodes[n], k_InputOne);
                }

                test.GetWalker();
                Assert.AreEqual(k_Nodes, test.GetOrphanGroup().TraversalCount);

                test.TestDatabase.DestroyAllNodes();
                test.Nodes.Clear();

                test.GetWalker();
                Assert.Zero(test.GetOrphanGroup().TraversalCount);
            }
            
        }

        [Test]
        public void CanUse_CacheWalker_ToWalkEntireGroupedCache_ContainingEmpty_OrphanGroup([Values] Topology.SortingAlgorithm algo, [Values] ComputeType jobified)
        {
            const int nodes = 30;

            using (var test = new Test(algo, jobified))
            {
                for (int n = 0; n < nodes; ++n)
                {
                    test.CreateAndAddNewNode();
                }

                // Connect all nodes together.
                for (int n = 0; n < nodes; n += 2)
                {
                    test.TestDatabase.Connect(test.Nodes[n], k_OutputOne, test.Nodes[n + 1], k_InputOne);
                }

                test.UpdateCache();

                var list = new List<Node>();

                foreach(var node in test.GetWalker())
                {
                    list.Add(node.Vertex);
                }
            }
        }

        public enum WalkerMode
        {
            ByEntireCache,
            ByGroups
        }

        [Test]
        public void CanUse_CacheWalker_ToWalkGroupedCache_WithEmptyGroups([Values] WalkerMode mode)
        {
            const int k_Nodes = 30;
            const int k_Groups = 5;

            using (var cache = new Topology.TraversalCache(k_Nodes, 1, Allocator.Temp))
            {
                var list = new List<Node>(k_Nodes * k_Groups);

                {
                    var mutable = new Topology.MutableTopologyCache(cache);

                    for (int i = 0; i < k_Groups; ++i)
                    {
                        mutable.AllocateGroup(0);
                        mutable.AllocateGroup(0);
                        ref var g = ref mutable.AllocateGroup(k_Nodes);

                        for (int n = 0; n < k_Nodes; ++n)
                        {
                            var node = new Node(n * i);
                            list.Add(node);
                            var slot = new Topology.TraversalCache.Slot();
                            slot.Vertex = node;

                            g.AddTraversalSlot(slot);
                        }
                    }
                }

                var foundList = new List<Node>(k_Nodes * k_Groups);

                switch (mode)
                {
                    case WalkerMode.ByEntireCache:

                        foreach (var vertex in new Tools.CacheWalker(cache))
                        {
                            foundList.Add(vertex.Vertex);
                        }
                        break;

                    case WalkerMode.ByGroups:

                        for(int i = 0; i < cache.Groups.Length; ++i)
                        {
                            foreach (var vertex in new Topology.GroupWalker(cache.Groups[i]))
                            {
                                foundList.Add(vertex.Vertex);
                            }
                        }
                        break;
                }
                
                CollectionAssert.AreEquivalent(list, foundList);
            }
        }

        [Test]
        public void NodesOriginate_InOrphanGroup_AndMoveBackAndForth_AfterConnectingAndDisconnecting([Values] ComputeType jobified)
        {
            const int nodes = 30;
            const int updates = 10;

            using (var test = new Test(Topology.SortingAlgorithm.LocalDepthFirst, jobified))
            {
                for (int n = 0; n < nodes; ++n)
                {
                    test.CreateAndAddNewNode();
                }

                for (int u = 0; u < updates; ++u)
                {
                    test.UpdateCache();

                    // All lone nodes should reside in the orphan group
                    for (int n = 0; n < nodes; ++n)
                    {
                        var group = test.GetGroupForNode(test.Nodes[n]);
                        Assert.AreEqual(test.GetOrphanGroup().HandleToSelf, group.HandleToSelf, $"[{u}]");
                    }

                    for (int n = 0; n < nodes; n += 2)
                    {
                        test.TestDatabase.Connect(test.Nodes[n], k_OutputOne, test.Nodes[n + 1], k_InputOne);
                    }

                    test.UpdateCache();
                    
                    // All lone nodes should now reside in different islands
                    for (int n = 0; n < nodes; ++n)
                    {
                        var group = test.GetGroupForNode(test.Nodes[n]);
                        Assert.AreNotEqual(test.GetOrphanGroup().HandleToSelf, group.HandleToSelf, $"[{u}, {n}]");
                    }

                    for (int n = 0; n < nodes; n += 2)
                    {
                        test.TestDatabase.Disconnect(test.Nodes[n], k_OutputOne, test.Nodes[n + 1], k_InputOne);
                    }
                }
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
                        test.CreateAndAddNewNode();
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
        public void Islands_CreateDifferent_AndUniqueGroups([Values] ComputeType jobified)
        {
            const int graphs = 5, nodesPerGraph = 5;

            using (var test = new Test(Topology.SortingAlgorithm.LocalDepthFirst, jobified))
            {
                for (int g = 0; g < graphs; ++g)
                {
                    for (int n = 0; n < nodesPerGraph; ++n)
                    {
                        test.CreateAndAddNewNode();
                    }

                    for (int n = 0; n < nodesPerGraph - 1; ++n)
                    {
                        test.TestDatabase.Connect(test.Nodes[0 + n + g * nodesPerGraph], k_OutputOne, test.Nodes[1 + n + g * nodesPerGraph], k_InputOne);
                    }

                }

                // Update cache - don't fold groups together
                test.GetWalker(minimumGroupSize : 0);

                Assert.AreEqual(graphs + 1 /* orphan group */, test.TestDatabase.Connections.ChangedGroups.Length);

                // Test each island gets a unique ID.
                var exclusives = new SortedSet<int>();

                var managedNodeList = test.Nodes.AsArray().ToList();

                for (int g = 0; g < graphs; ++g)
                {
                    var nodesSupposedlyInThisGroup = managedNodeList.GetRange(g * nodesPerGraph, nodesPerGraph);
                    var memberships = nodesSupposedlyInThisGroup.Select(n => test.TestDatabase.Nodes[n].GroupID);

                    // Test all the nodes are a member of the same group
                    Assert.AreEqual(1, memberships.Distinct().Count());

                    var group = memberships.First();

                    // Assert that the membership doesn't already exist
                    CollectionAssert.DoesNotContain(exclusives, group);

                    exclusives.Add(group);
                }
            }
        }

        [Test]
        public void ConnectingGroupsTogether_IncreasesVersion_OfPreviousGroups_AndProducesExpectedNumber_OfGroups([Values] ComputeType jobified)
        {
            const int graphs = 5, nodesPerGraph = 5, updates = 5;

            using (var test = new Test(Topology.SortingAlgorithm.LocalDepthFirst, jobified))
            {
                for (int g = 0; g < graphs; ++g)
                {
                    for (int n = 0; n < nodesPerGraph; ++n)
                    {
                        test.CreateAndAddNewNode();
                    }

                    for (int n = 0; n < nodesPerGraph - 1; ++n)
                    {
                        test.TestDatabase.Connect(test.Nodes[0 + n + g * nodesPerGraph], k_OutputOne, test.Nodes[1 + n + g * nodesPerGraph], k_InputOne);
                    }

                }

                var versions = test.TestDatabase.Connections.ChangedGroups;

                for (int u = 0; u < updates; ++u)
                {
                    // Update cache - don't concatenate together any groups
                    test.UpdateCache(minimumGroupSize: 0);
                    
                    // connect all islands together, assert that all group versions were touched.
                    for (int g = 1; g < graphs; ++g)
                    {
                        test.TestDatabase.Connect(test.Nodes[g * nodesPerGraph - 1], k_OutputOne, test.Nodes[g * nodesPerGraph], k_InputOne);
                    }

                    for (int g = 1; g < graphs; ++g)
                    {
                        var root = test.Nodes[g * nodesPerGraph];
                        var group = test.GetGroupForNode(root);
                        Assert.True(
                            versions[group.HandleToSelf], 
                            $"[{u}]Group {g} wasn't touched by connecting everything together"
                        );
                    }

                    // The orphan group shouldn't have been touched by this.
                    Assert.False(versions[test.GetOrphanGroup().HandleToSelf]);

                    var cache = test.GetUpdatedCache(minimumGroupSize: 0);
                    
                    for (int i = 0; i < versions.Length; ++i)
                        Assert.False(versions[i]);

                    // disconnect all islands together
                    for (int g = 1; g < graphs; ++g)
                    {
                        test.TestDatabase.Disconnect(test.Nodes[g * nodesPerGraph - 1], k_OutputOne, test.Nodes[g * nodesPerGraph], k_InputOne);
                    }

                    // assert that all groups containing nodes have been touched.
                    for(int g = 0; g < cache.Groups.Length; ++g)
                    {
                        var group = cache.Groups[g];

                        if(group.TraversalCount > 0)
                        {
                            Assert.True(test.TestDatabase.Connections.ChangedGroups[g]);
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
                    test.CreateAndAddNewNode();
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
#if UNITY_EDITOR
            if (BurstConfig.IsBurstEnabled && !TestRunConfig.EnableBurstCompileSynchronously)
                Assert.Ignore("Burst is not compiling synchronously");
#endif

            using (var test = new Test(algo, jobified))
            {
                for (int i = 0; i < 5; ++i)
                    test.CreateAndAddNewNode();

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
                    test.CreateAndAddNewNode();
                }

                var foundNodes = new List<Node>();

                test.GetUpdatedCache();
                test.TestDatabase.DestroyAllNodes();
                test.Nodes.Clear();
                test.GetUpdatedCache();

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
        public void CanFindParentsAndChildren_ByPort([Values] Topology.SortingAlgorithm algo, [Values] ComputeType jobified)
        {
            using (var test = new Test(algo, jobified))
            {
                for (int i = 0; i < 5; ++i)
                {
                    test.CreateAndAddNewNode();
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
                    test.CreateAndAddNewNode();
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
                    test.CreateAndAddNewNode();
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
                    test.CreateAndAddNewNode();
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
                    test.CreateAndAddNewNode();
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
                    test.CreateAndAddNewNode();
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
                    test.CreateAndAddNewNode();
                }

                test.TestDatabase.Connect(test.Nodes[0], k_OutputOne, test.Nodes[2], k_InputOne);
                test.TestDatabase.Connect(test.Nodes[1], k_OutputOne, test.Nodes[2], k_InputTwo);

                test.TestDatabase.Connect(test.Nodes[2], k_OutputOne, test.Nodes[3], k_InputOne);
                test.TestDatabase.Connect(test.Nodes[2], k_OutputTwo, test.Nodes[4], k_InputOne);

                var cache = test.GetUpdatedCache();

                var group = test.GetGroupForNode(test.Nodes[0]);

                var rootWalker = new Topology.RootCacheWalker(group);
                var leafWalker = new Topology.LeafCacheWalker(group);

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
                    test.CreateAndAddNewNode();
                }

                test.TestDatabase.Connect(test.Nodes[0], k_OutputOne, test.Nodes[2], k_InputOne);
                test.TestDatabase.Connect(test.Nodes[1], k_OutputOne, test.Nodes[2], k_InputTwo);

                test.TestDatabase.Connect(test.Nodes[2], k_OutputOne, test.Nodes[3], k_InputOne);
                test.TestDatabase.Connect(test.Nodes[2], k_OutputTwo, test.Nodes[4], k_InputOne);

                var cache = test.GetUpdatedCache();

                var group = test.GetGroupForNode(test.Nodes[0]);

                Assert.AreEqual(group.LeafCount, 2);
                Assert.AreEqual(group.RootCount, 2);

                var roots = new List<Node>();

                for (int i = 0; i < group.RootCount; ++i)
                    roots.Add(group.IndexTraversal(group.IndexRoot(i)).Vertex);

                var leaves = new List<Node>();

                for (int i = 0; i < group.LeafCount; ++i)
                    leaves.Add(group.IndexTraversal(group.IndexLeaf(i)).Vertex);

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
                var node = test.CreateAndAddNewNode();

                test.UpdateCache();

                var group = test.GetGroupForNode(node);

                Assert.AreEqual(group.LeafCount, 1);
                Assert.AreEqual(group.RootCount, 1);

                var roots = new List<Node>();

                for (int i = 0; i < group.RootCount; ++i)
                    roots.Add(group.IndexTraversal(group.IndexRoot(i)).Vertex);

                var leaves = new List<Node>();

                for (int i = 0; i < group.LeafCount; ++i)
                    leaves.Add(group.IndexTraversal(group.IndexLeaf(i)).Vertex);

                CollectionAssert.Contains(leaves, test.Nodes[0]);
                CollectionAssert.Contains(roots, test.Nodes[0]);
            }
        }

        [Test]
        public void CanCongaWalkAndDependenciesAreInCorrectOrder([Values] Topology.SortingAlgorithm algo, [Values] ComputeType jobified)
        {
            using (var test = new Test(algo, jobified))
            {
                var node1 = test.CreateAndAddNewNode();
                var node2 = test.CreateAndAddNewNode();
                var node3 = test.CreateAndAddNewNode();
                
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
                var node1 = test.CreateAndAddNewNode();
                var node2 = test.CreateAndAddNewNode();
                var node3 = test.CreateAndAddNewNode();

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
                    test.CreateAndAddNewNode();
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

        void AssertAreSame(IList<Node> nodes, Topology.InputVertexCacheWalker vertices)
        {
            Assert.AreEqual(nodes.Count, vertices.Count);
            foreach (var vertex in vertices)
                CollectionAssert.Contains(nodes, vertex.Vertex);
        }

        void AssertAreSame(IList<Node> nodes, Topology.OutputVertexCacheWalker vertices)
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
                    test.CreateAndAddNewNode();

                // Setup normal traversal dependencies
                test.TestDatabase.Connect((uint)TraversalType.Normal, test.Nodes[0], k_OutputOne, test.Nodes[2], k_InputOne);
                test.TestDatabase.Connect((uint)TraversalType.Normal, test.Nodes[2], k_OutputOne, test.Nodes[3], k_InputOne);
                test.TestDatabase.Connect((uint)TraversalType.Normal, test.Nodes[0], k_OutputOne, test.Nodes[3], k_InputOne);
                // Ensure the topologies intersect (added after segmentation of islands)
                test.TestDatabase.Connect((uint)TraversalType.Normal, test.Nodes[0], k_OutputOne, test.Nodes[1], k_InputOne);

                // Setup an alternate dependency hierarchy
                // This is a cycle in dependency topology, supported??
                test.TestDatabase.Connect((uint)TraversalType.Different, test.Nodes[3], k_OutputOne, test.Nodes[2], k_InputOne);
                test.TestDatabase.Connect((uint)TraversalType.Different, test.Nodes[2], k_OutputOne, test.Nodes[1], k_InputOne);
                test.TestDatabase.Connect((uint)TraversalType.Different, test.Nodes[1], k_OutputOne, test.Nodes[3], k_InputOne);

                var cache = test.GetUpdatedCache();
                
                foreach (var node in new Tools.CacheWalker(cache))
                {
                    if (node.Vertex == test.Nodes[0])
                    {
                        Assert.Zero(node.GetParents().Count);
                        AssertAreSame(new [] { test.Nodes[1], test.Nodes[2], test.Nodes[3] }, node.GetChildren());
                        Assert.Zero(node.GetParents(Topology.TraversalCache.Hierarchy.Alternate).Count);
                        Assert.Zero(node.GetChildren(Topology.TraversalCache.Hierarchy.Alternate).Count);
                    }

                    if (node.Vertex == test.Nodes[1])
                    {
                        Assert.AreEqual(1, node.GetParents().Count);
                        AssertAreSame(new[] { test.Nodes[0] }, node.GetParents());

                        Assert.Zero(node.GetChildren().Count);
                        AssertAreSame(new [] { test.Nodes[2] }, node.GetParents(Topology.TraversalCache.Hierarchy.Alternate));
                        AssertAreSame(new [] { test.Nodes[3] }, node.GetChildren(Topology.TraversalCache.Hierarchy.Alternate));
                    }

                    if (node.Vertex == test.Nodes[2])
                    {
                        AssertAreSame(new [] { test.Nodes[0] }, node.GetParents());
                        AssertAreSame(new [] { test.Nodes[3] }, node.GetChildren());
                        AssertAreSame(new [] { test.Nodes[3] }, node.GetParents(Topology.TraversalCache.Hierarchy.Alternate));
                        AssertAreSame(new [] { test.Nodes[1] }, node.GetChildren(Topology.TraversalCache.Hierarchy.Alternate));
                    }

                    if (node.Vertex == test.Nodes[3])
                    {
                        AssertAreSame(new [] { test.Nodes[0], test.Nodes[2] }, node.GetParents());
                        Assert.Zero(node.GetChildren().Count);
                        AssertAreSame(new [] { test.Nodes[1] }, node.GetParents(Topology.TraversalCache.Hierarchy.Alternate));
                        AssertAreSame(new [] { test.Nodes[2] }, node.GetChildren(Topology.TraversalCache.Hierarchy.Alternate));
                    }
                }
            }
        }

        [Test]
        public void NonIntersecting_TraversalAndAlternateHierarchy_ProducesDeferredError_InDFSSearch([Values] ComputeType computeType)
        {
            using (var test = new Test(Topology.SortingAlgorithm.LocalDepthFirst, computeType, (uint)TraversalType.Normal, (uint)TraversalType.Different))
            {
                for (int i = 0; i < 4; ++i)
                    test.CreateAndAddNewNode();

                // Setup normal traversal dependencies
                test.TestDatabase.Connect((uint)TraversalType.Normal, test.Nodes[0], k_OutputOne, test.Nodes[2], k_InputOne);
                test.TestDatabase.Connect((uint)TraversalType.Normal, test.Nodes[2], k_OutputOne, test.Nodes[3], k_InputOne);
                test.TestDatabase.Connect((uint)TraversalType.Normal, test.Nodes[0], k_OutputOne, test.Nodes[3], k_InputOne);

                // Setup an alternate dependency hierarchy. Node[1] is not reachable by traversal hierarchy.
                test.TestDatabase.Connect((uint)TraversalType.Different, test.Nodes[3], k_OutputOne, test.Nodes[2], k_InputOne);
                test.TestDatabase.Connect((uint)TraversalType.Different, test.Nodes[2], k_OutputOne, test.Nodes[1], k_InputOne);
                test.TestDatabase.Connect((uint)TraversalType.Different, test.Nodes[1], k_OutputOne, test.Nodes[3], k_InputOne);

                // Don't fold together groups
                test.UpdateCache_AndIgnoreErrors(minimumGroupSize: 0);

                CollectionAssert.Contains(
                    test.GetTopologyErrors(),
                    Topology.TraversalCache.Error.UnrelatedHierarchy
                );
            }
        }

        [Test]
        public void CompletelyCyclicDataGraph_ProducesAvailableError([Values] Topology.SortingAlgorithm algo, [Values] ComputeType computeType)
        {
            using (var test = new Test(algo, computeType))
            {
                test.CreateAndAddNewNode();
                test.CreateAndAddNewNode();

                test.TestDatabase.Connect(test.Nodes[0], k_OutputOne, test.Nodes[1], k_InputOne);
                test.TestDatabase.Connect(test.Nodes[1], k_OutputOne, test.Nodes[0], k_InputOne);

                test.UpdateCache_AndIgnoreErrors();
                CollectionAssert.AreEqual(test.GetTopologyErrors(), new[] { Topology.TraversalCache.Error.Cycles });
            }
        }

        [Test]
        public void PartlyCyclicDataGraph_ProducesDeferredError([Values] Topology.SortingAlgorithm algo, [Values] ComputeType computeType)
        {
            using (var test = new Test(algo, computeType))
            {
                test.CreateAndAddNewNode();
                test.CreateAndAddNewNode();
                test.CreateAndAddNewNode();

                test.TestDatabase.Connect(test.Nodes[0], k_OutputOne, test.Nodes[1], k_InputOne);
                test.TestDatabase.Connect(test.Nodes[1], k_OutputOne, test.Nodes[0], k_InputOne);
                test.TestDatabase.Connect(test.Nodes[2], k_OutputOne, test.Nodes[0], k_InputOne);

                test.UpdateCache_AndIgnoreErrors();
                CollectionAssert.AreEqual(test.GetTopologyErrors(), new [] { Topology.TraversalCache.Error.Cycles });
            }
        }

        [Test]
        public void DeepImplicitlyCyclicDataGraph_ProducesDeferredError([Values] Topology.SortingAlgorithm algo, [Values] ComputeType computeType, [Values(0, 1, 10, 13, 100)] int depth)
        {
            using (var test = new Test(algo, computeType))
            {
                // create three branches
                Node
                    a = test.CreateAndAddNewNode(),
                    b = test.CreateAndAddNewNode(),
                    c = test.CreateAndAddNewNode();

                // intertwine
                test.TestDatabase.Connect(a, k_OutputOne, b, k_InputOne);
                test.TestDatabase.Connect(b, k_OutputOne, c, k_InputOne);
                
                // fork off ->
                // o-o-o-o-o-o-o-o ...
                // |
                // o-o-o-o-o-o-o-o ...
                // |
                // o-o-o-o-o-o-o-o ...
                for (int i = 0; i < depth; ++i)
                {
                    a = test.CreateAndAddNewNode();
                    b = test.CreateAndAddNewNode();
                    c = test.CreateAndAddNewNode();

                    test.TestDatabase.Connect(test.Nodes[i * 3 + 0], k_OutputOne, a, k_InputOne);
                    test.TestDatabase.Connect(test.Nodes[i * 3 + 1], k_OutputOne, b, k_InputOne);
                    test.TestDatabase.Connect(test.Nodes[i * 3 + 2], k_OutputOne, c, k_InputOne);
                }

                // connect very last node to start, forming a cycle
                // -> o-o-o-o-o-o-o-o-> 
                // |  |
                // |  o-o-o-o-o-o-o-o-> 
                // |  |
                // |  o-o-o-o-o-o-o-o 
                // -----------------| 
                test.TestDatabase.Connect(test.Nodes[test.Nodes.Length - 1], k_OutputOne, test.Nodes[0], k_InputOne);

                test.UpdateCache_AndIgnoreErrors();
                CollectionAssert.AreEqual(test.GetTopologyErrors(), new[] { Topology.TraversalCache.Error.Cycles });
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

                // GlobalBreadthFirst == LocalDepthFirst currently.
                /*const string kExpectedMaximallyParallelOrder = 
                    "0, 2, 5, 8, 13, 14, 16, 19, 22, 27, 28, 30, 33, 36, " +
                    "41, 42, 44, 47, 50, 55, 56, 58, 61, 64, 69, 70, 72, 75, " +
                    "78, 83, 84, 86, 89, 92, 97, 98, 100, 103, 106, 111, 112, 114, " +
                    "117, 120, 125, 126, 128, 131, 134, 139, 1, 9, 15, 23, 29, 37, " +
                    "43, 51, 57, 65, 71, 79, 85, 93, 99, 107, 113, 121, 127, " +
                    "135, 10, 24, 38, 52, 66, 80, 94, 108, 122, 136, 6, 3, 20, " +
                    "17, 34, 31, 48, 45, 62, 59, 76, 73, 90, 87, 104, 101, 118, " +
                    "115, 132, 129, 7, 11, 4, 21, 25, 18, 35, 39, 32, 49, 53, " +
                    "46, 63, 67, 60, 77, 81, 74, 91, 95, 88, 105, 109, 102, 119, " +
                    "123, 116, 133, 137, 130, 12, 26, 40, 54, 68, 82, 96, 110, 124, 138"; */

                const string kExpectedIslandOrder =
                    "13, 27, 41, 55, 69, 83, 97, 111, 125, 139, " + // Orphan group
                    "0, 1, 2, 8, 9, 10, 5, 6, 7, 3, 11, " + // primary island
                    "12, 4, " + // secondary island
                    "14, 15, 16, 22, 23, 24, 19, 20, 21, 17, 25, " + // primary island 2.. etc
                    "26, 18, " +
                    "28, 29, 30, 36, 37, 38, 33, 34, 35, 31, 39, " +
                    "40, 32, " +
                    "42, 43, 44, 50, 51, 52, 47, 48, 49, 45, 53, " +
                    "54, 46, " +
                    "56, 57, 58, 64, 65, 66, 61, 62, 63, 59, 67, " +
                    "68, 60, " +
                    "70, 71, 72, 78, 79, 80, 75, 76, 77, 73, 81, " +
                    "82, 74, " +
                    "84, 85, 86, 92, 93, 94, 89, 90, 91, 87, 95, " +
                    "96, 88, " +
                    "98, 99, 100, 106, 107, 108, 103, 104, 105, 101, 109, " +
                    "110, 102, " +
                    "112, 113, 114, 120, 121, 122, 117, 118, 119, 115, 123, " +
                    "124, 116, " +
                    "126, 127, 128, 134, 135, 136, 131, 132, 133, 129, 137, 138, 130";

                var traversalIndices = new List<string>();

                for (int g = 0; g < cache.Groups.Length; ++g)
                {
                    var group = cache.Groups[g];

                    for (int i = 0; i < group.TraversalCount; ++i)
                    {
                        traversalIndices.Add(group.IndexTraversal(i).Vertex.Id.ToString());
                    }
                }

                var stringTraversalOrder = string.Join(", ", traversalIndices);

                switch (algo)
                {
                    case Topology.SortingAlgorithm.GlobalBreadthFirst:
                        Assert.AreEqual(kExpectedIslandOrder, stringTraversalOrder);
                        break;
                    case Topology.SortingAlgorithm.LocalDepthFirst:
                        Assert.AreEqual(kExpectedIslandOrder, stringTraversalOrder);
                        break;
                }
            }
        }

        [Test]
        public void CacheWithErrors_StillAssignsValidGroupIDs_ToTopologyDatabase([Values] Topology.SortingAlgorithm algo, [Values] ComputeType computeType)
        {
            using (var test = new Test(algo, computeType))
            {
                test.CreateAndAddNewNode();
                test.CreateAndAddNewNode();

                // Make a cycle.
                test.TestDatabase.Connect(test.Nodes[0], k_OutputOne, test.Nodes[1], k_InputOne);
                test.TestDatabase.Connect(test.Nodes[1], k_OutputOne, test.Nodes[0], k_InputOne);

                test.UpdateCache_AndIgnoreErrors();

                // Cache in error state will revert every node to belong to the orphan group (but still be empty).
                Assert.AreEqual(1, test.TestDatabase.Connections.ChangedGroups.Length);

                foreach(var n in test.Nodes)
                {
                    Assert.AreEqual(Topology.Database.OrphanGroupID, test.TestDatabase.Nodes[n].GroupID);
                }
            }
        }

        [Test]
        public void IncrementallyTouchingGroups_OnlyRecomputesAffectedGroups([Values] Topology.SortingAlgorithm algo, [Values] ComputeType computeType, [Values(-1, 0, 5)] int minimumGroupSize)
        {
            const int k_Groups = 100;
            const int k_Seed = 0xAF;
            const int k_Updates = 25;

            using (var test = new Test(algo, computeType))
            {
                for(int i = 0; i < k_Groups; ++i)
                {
                    var a = test.CreateAndAddNewNode();
                    var b = test.CreateAndAddNewNode();

                    test.TestDatabase.Connect(a, k_OutputOne, b, k_InputOne);
                }

                var device = new Mathematics.Random(k_Seed);

                var cache = test.GetUpdatedCache(minimumGroupSize: 0);

                var actualGroups = test.Cache.Groups.Length;

                var changed = new List<int>();
                var originalVertices = new List<Node>();
                var relocatedNodes = new List<Node>();

                // Breadth first used to dirty the single group it resides in, even though
                // it was not touched by the user. 
                var offset = 0;

                for (int i = 0; i < k_Updates; ++i)
                {
                    changed.Clear();
                    originalVertices.Clear();
                    relocatedNodes.Clear();

                    Assert.AreEqual(test.Cache.Groups.Length, test.TestDatabase.Connections.ChangedGroups.Length);

                    var touchCount = offset + (i & 1) + device.NextInt(actualGroups / 10);
                    
                    for(int t = 0; t < touchCount; ++t)
                    {
                        var groupIndex = device.NextInt(actualGroups);
                        test.TestDatabase.Connections.ChangedGroups[groupIndex] = true;

                        foreach(var node in new Topology.GroupWalker(cache.Groups[groupIndex]))
                        {
                            originalVertices.Add(node.Vertex);
                        }

                        if (changed.IndexOf(groupIndex) == -1)
                            changed.Add(groupIndex);
                    }
                    
                    cache = test.GetUpdatedCache(minimumGroupSize: 0);

                    // Right now, we only we get told what new groups appeared.
                    // In the future, we can test that the appropriate dirty groups got deleted as well, like so:
                    CollectionAssert.AreEquivalent(cache.DeletedGroups.AsArray().ToArray(), changed);


                    // Test that all nodes in an originally touched group now belong to a group that has been
                    // changed.
                    foreach (var node in originalVertices)
                    {
                        var newMembership = test.TestDatabase.Nodes[node].GroupID;
                        CollectionAssert.Contains(cache.NewGroups.AsArray().ToArray(), newMembership);
                    }

                    // concatenate all relocated nodes
                    for (int g = 0; g < cache.NewGroups.Length; ++g)
                    {
                        var newGroup = cache.NewGroups[g];
                        var group = cache.Groups[newGroup];

                        for (int n = 0; n < group.TraversalCount; ++n)
                            relocatedNodes.Add(group.IndexTraversal(n).Vertex);
                    }

                    CollectionAssert.AreEquivalent(originalVertices, relocatedNodes);
                }
                
            }
        }

        [Test]
        public void ChangedGroupsIncludeAllGroups_OnErrors_InCache([Values] Topology.SortingAlgorithm algo, [Values] ComputeType computeType)
        {
            const int k_Groups = 10;

            using (var test = new Test(algo, computeType))
            {
                for (int i = 0; i < k_Groups; ++i)
                {
                    var a = test.CreateAndAddNewNode();
                    var b = test.CreateAndAddNewNode();

                    test.TestDatabase.Connect(a, k_OutputOne, b, k_InputOne);
                }

                var cache = test.GetUpdatedCache(minimumGroupSize: 0);
                var numGroups = test.Cache.Groups.Length;

                // Make a cycle.
                test.TestDatabase.Connect(test.Nodes[test.Nodes.Length - 1], k_OutputOne, test.Nodes[test.Nodes.Length - 2], k_InputOne);

                test.UpdateCache_AndIgnoreErrors(minimumGroupSize: 0);

                Assert.AreEqual(1, cache.Groups.Length);
                Assert.AreEqual(Topology.TraversalCache.Error.Cycles, cache.Errors.Dequeue());
                Assert.AreEqual(1 /* orphan group */ + numGroups - cache.Groups.Length, cache.DeletedGroups.Length);
                Assert.AreEqual(1, cache.NewGroups.Length);

                CollectionAssert.AreEquivalent(Enumerable.Range(0, k_Groups + 1 /* orphan */), cache.DeletedGroups.AsArray().ToArray());
            }
        }

        [Test]
        public void TouchingOnlyConnectedRoots_RecomputesWholeComponentAnyway_AndDontEndUp_InOrphanGroup([Values] ComputeType computeType)
        {
            // * - Test root nodes do not get added to orphan group (changed head | tail conditional)

            const int k_Groups = 100;

            using (var test = new Test(Topology.SortingAlgorithm.LocalDepthFirst, computeType))
            {
                for (int i = 0; i < k_Groups; ++i)
                {
                    var a = test.CreateAndAddNewNode();
                    var b = test.CreateAndAddNewNode();
                    
                    test.TestDatabase.Connect(a, k_OutputOne, b, k_InputOne);
                }

                test.UpdateCache(minimumGroupSize: 0);

                Assert.AreEqual(1 + k_Groups, test.TestDatabase.Connections.ChangedGroups.Length);

                using (var roots = new NativeList<Node>(Allocator.TempJob))
                {
                    for (int g = 0; g < test.Cache.Groups.Length; ++g)
                    {
                        foreach (var node in new Topology.RootCacheWalker(test.Cache.Groups[g]))
                        {
                            roots.Add(node.Vertex);
                            test.TestDatabase.Connections.ChangedGroups[g] = true;
                        }
                    }

                    test.UpdateCache_WithSpecificNodes(roots, minimumGroupSize: 0);
                }

                Assert.AreEqual(1 + k_Groups, test.TestDatabase.Connections.ChangedGroups.Length);
                Assert.AreEqual(k_Groups, test.Cache.NewGroups.Length);
                Assert.AreEqual(k_Groups, test.Cache.DeletedGroups.Length);

                Assert.Zero(test.Cache.Groups[Topology.Database.OrphanGroupID].TraversalCount);

            }
        }

        [Test]
        public void AccessingOrphanGroup_FromMutableTraversalCache_CausesItToBeDirtied()
        {
            using (var cache = new Topology.TraversalCache(0, 0))
            {
                var mutable = new Topology.MutableTopologyCache(cache);

                Assert.Zero(cache.NewGroups.Length);
                Assert.Zero(cache.DeletedGroups.Length);

                mutable.GetOrphanGroupForAccumulation();

                CollectionAssert.Contains(cache.NewGroups.AsArray().ToArray(), Topology.Database.OrphanGroupID);
                CollectionAssert.Contains(cache.DeletedGroups.AsArray().ToArray(), Topology.Database.OrphanGroupID);
            }
        }

        // Issue #484
        [Test, Explicit]
        public void WideInterconnectedDAG_CanBeTraversed([Values] Topology.SortingAlgorithm algo, [Values] ComputeType jobified, [Values(15000)]int width)
        {
            using (var test = new Test(algo, jobified))
            {
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
                    test.Nodes.Add(test.TestDatabase.CreateNode());
                    test.Nodes.Add(test.TestDatabase.CreateNode());
                    test.TestDatabase.Connect(test.Nodes[i*2], k_OutputOne, test.Nodes[i*2+1], k_InputOne);
                    if (i>0)
                        test.TestDatabase.Connect(test.Nodes[i*2-1], k_OutputOne, test.Nodes[i*2+1], k_InputTwo);
                }

                test.GetUpdatedCache();
            }
        }
    }
}
