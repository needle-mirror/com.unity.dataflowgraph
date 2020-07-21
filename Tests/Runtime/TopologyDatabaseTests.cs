using System;
using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.DataFlowGraph.Tests
{
    using Topology = TopologyAPI<Node, InputPort, OutputPort>;

    /*
     * Missing tests:
     * - Disposed tests of GetInputs() / GetOutputs(), Disconnect, DisconnectAndRelease, FindConnection
     * - Tests of different traversal types
     * 
     * 
     */

#pragma warning disable CS0660 // Type defines operator == or operator != but does not override Object.Equals(object o)
#pragma warning disable CS0661 // Type defines operator == or operator != but does not override Object.GetHashCode()
    partial struct Node : IEquatable<Node>
    {
        public int Id;
        public Node(int id) { Id = id; }
        public bool Equals(Node other) => Id == other.Id;
        public static bool operator == (Node a, Node b) => a.Equals(b);
        public static bool operator != (Node a, Node b) => a.Equals(b);

        public override string ToString()
        {
            return $"Node[{Id.ToString()}]";
        }
    }
#pragma warning restore CS0661
#pragma warning restore CS0660

    struct InputPort : IEquatable<InputPort>
    {
        public int Id;
        public InputPort(int id) { Id = id; }
        public bool Equals(InputPort other) => Id == other.Id;
    }

    struct OutputPort : IEquatable<OutputPort>
    {
        public int Id;
        public OutputPort(int id) { Id = id; }
        public bool Equals(OutputPort other) => Id == other.Id;
    }

    partial struct TopologyTestDatabase : IDisposable
    {
        const uint k_DefaultTraversalFlags = 123;

        public struct NodeList : Topology.Database.ITopologyFromVertex, IDisposable
        {
            [NativeDisableParallelForRestriction]
            NativeList<TopologyIndex> m_List;
            [NativeDisableParallelForRestriction]
            NativeList<BurstConfig.ExecutionResult> m_ExecutionVehicle;

            public NodeList(Allocator allocator)
            {
                m_List = new NativeList<TopologyIndex>(0, allocator);
                m_ExecutionVehicle = new NativeList<BurstConfig.ExecutionResult>(1, allocator);
                m_ExecutionVehicle.Add(BurstConfig.ExecutionResult.Undefined);
            }

            public Node CreateNode()
            {
                m_List.Add(new TopologyIndex());
                return new Node(m_List.Length - 1);
            }

            public void DestroyAll(Topology.Database db)
            {
                for (int i = 0; i < m_List.Length; ++i)
                {
                    db.VertexDeleted(ref this, new Node(i));
                }

                m_List.Clear();
            }



            public TopologyIndex this[Node node]
            {
                get
                {
                    m_ExecutionVehicle[0] = BurstConfig.DetectExecutionEngine(); 
                    return m_List[node.Id];
                }
                set
                {
                    m_ExecutionVehicle[0] = BurstConfig.DetectExecutionEngine(); 
                    m_List[node.Id] = value;
                }
            }

            public void Dispose()
            {
                m_List.Dispose();
                m_ExecutionVehicle.Dispose();
            }

            public BurstConfig.ExecutionResult GetLastExecutionEngine() => m_ExecutionVehicle[0];
        }

        public NodeList Nodes;
        public Topology.Database Connections;

        public TopologyTestDatabase(Allocator allocator)
        {
            Nodes = new NodeList(Allocator.Persistent);
            Connections = new Topology.Database(0, Allocator.Persistent);
        }

        public void Dispose()
        {
            Nodes.Dispose();
            Connections.Dispose();
        }

        public Node CreateNode()
        {
            var node = Nodes.CreateNode();
            Connections.VertexCreated(ref Nodes, node);

            return node;
        }

        public void Connect(uint traversalFlags, Node source, OutputPort sourcePort, Node dest, InputPort destinationPort) =>
            Connections.Connect(ref Nodes, traversalFlags, source, sourcePort, dest, destinationPort);
        public void Connect(Node source, OutputPort sourcePort, Node dest, InputPort destinationPort) =>
            Connect(k_DefaultTraversalFlags, source, sourcePort, dest, destinationPort);
        public void Disconnect(Node source, OutputPort sourcePort, Node dest, InputPort destinationPort) =>
            Connections.Disconnect(ref Nodes, source, sourcePort, dest, destinationPort);
        public void DisconnectAll(Node source) =>
            Connections.DisconnectAll(ref Nodes, source);
        public void DisconnectAndRelease(Topology.Connection connection) =>
            Connections.DisconnectAndRelease(ref Nodes, connection);

        public void DestroyAllNodes() => Nodes.DestroyAll(Connections);

        public Topology.Connection FindConnection(Node source, OutputPort sourcePort, Node dest, InputPort destinationPort) =>
            Connections.FindConnection(ref Nodes, source, sourcePort, dest, destinationPort);
        public bool ConnectionExists(Node source, OutputPort sourcePort, Node dest, InputPort destinationPort) =>
            Connections.ConnectionExists(ref Nodes, source, sourcePort, dest, destinationPort);
    }

    public class TopologyDatabaseTests
    {
        static readonly InputPort k_InputPort = new InputPort(234);
        static readonly InputPort k_SecondaryInputPort = new InputPort(345);
        static readonly OutputPort k_OutputPort = new OutputPort(456);
        static readonly OutputPort k_SecondaryOutputPort = new OutputPort(567);

        [Test]
        public void DefaultConstructedDatabaseIsNotCreated()
        {
            Assert.IsFalse(new Topology.Database().IsCreated);
        }

        [Test]
        public void ConstructedDatabaseIsCreated()
        {
            var db = new Topology.Database(1, Allocator.Temp);
            Assert.IsTrue(db.IsCreated);
            Assert.DoesNotThrow(() => db.Dispose());
            Assert.IsFalse(db.IsCreated);
        }

        [Test]
        public void ConstructedDatabase_ContainsOneGroup()
        {
            using (var db = new Topology.Database(1, Allocator.Temp))
                Assert.AreEqual(db.ChangedGroups.Length, 1);
        }

        [Test]
        public void NewlyCreatedNodes_InitiallyBelongsTo_GenerationZero()
        {
            using (var topoDB = new TopologyTestDatabase(Allocator.Temp))
            {
                var node = topoDB.CreateNode();
                var index = topoDB.Nodes[node];

                Assert.AreEqual(index.GroupID, Topology.Database.OrphanGroupID);
            }
        }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        [Test]
        public void DefaultConstructedDatabaseThrows()
        {
            var database = new Topology.Database();
            Topology.Connection whatever;

            TopologyTestDatabase.NodeList list = new TopologyTestDatabase.NodeList(Allocator.Temp);

            try
            {
                var source = list.CreateNode();
                var dest = list.CreateNode();

                Assert.Throws<IndexOutOfRangeException>(() => whatever = database[0]);

                Assert.Throws<Exception>(() => database.Connect(ref list, 0, source, sourcePort: default, dest, destinationPort: default));
                Assert.Throws<NullReferenceException>(() => database.Disconnect(ref list, source, sourcePort: default, dest, destinationPort: default));
                Assert.Throws<ObjectDisposedException>(() => database.Dispose());

                Assert.Throws<ObjectDisposedException>(database.Dispose);
            }
            finally
            {
                list.Dispose();
            }
        }
#endif

        [Test]
        public void ConnectionsMade_CausesConnectionTable_ToBePopulatedCorrectly_AndSubsequentlyRemoved()
        {
            using (var topoDB = new TopologyTestDatabase(Allocator.Persistent))
            {
                var node1 = topoDB.CreateNode();
                var node2 = topoDB.CreateNode();

                topoDB.Connect(111, node1, k_OutputPort, node2, k_InputPort);

                Assert.AreEqual(1, topoDB.Connections.CountEstablishedConnections(),
                    "There isn't exactly one valid edge in a new set with one connection");

                var madeConnection = new Topology.Connection();
                Assert.IsFalse(madeConnection.Valid, "Default constructed connection is valid");

                int indexHandleCounter = 0, foundIndexHandle = 0;
                for(int i = 0; i < topoDB.Connections.TotalConnections; ++i)
                {
                    if (topoDB.Connections[i].Valid)
                    {
                        madeConnection = topoDB.Connections[i];
                        foundIndexHandle = indexHandleCounter;
                    }
                    indexHandleCounter++;
                }

                Assert.IsTrue(madeConnection.Valid, "Could not find the made connection");
                Assert.NotZero(foundIndexHandle, "Found connection cannot be the invalid slot");

                // check the connection is as it should be
                Assert.AreEqual(111, madeConnection.TraversalFlags);
                Assert.AreEqual(node1, madeConnection.Source);
                Assert.AreEqual(node2, madeConnection.Destination);
                Assert.AreEqual(k_OutputPort, madeConnection.SourceOutputPort);
                Assert.AreEqual(k_InputPort, madeConnection.DestinationInputPort);
                Assert.AreEqual(foundIndexHandle, madeConnection.HandleToSelf.Index);

                topoDB.Disconnect(node1, k_OutputPort, node2, k_InputPort);

                Assert.AreEqual(0, topoDB.Connections.CountEstablishedConnections(),
                    "There are valid connections in a new set with zero connections");
            }
        }

        [Test]
        public void CanQueryCreatedConnections()
        {
            using (var topoDB = new TopologyTestDatabase(Allocator.Persistent))
            {
                var node1 = topoDB.CreateNode();
                var node2 = topoDB.CreateNode();

                Assert.IsFalse(topoDB.ConnectionExists(node1, k_OutputPort, node2, k_InputPort));
                Assert.IsFalse(topoDB.FindConnection(node1, k_OutputPort, node2, k_InputPort).Valid);

                topoDB.Connect(111, node1, k_OutputPort, node2, k_InputPort);

                var foundConnection = topoDB.FindConnection(node1, k_OutputPort, node2, k_InputPort);
                Assert.IsTrue(topoDB.ConnectionExists(node1, k_OutputPort, node2, k_InputPort));
                Assert.IsTrue(foundConnection.Valid);
                Assert.AreEqual(node1, foundConnection.Source);
                Assert.AreEqual(k_OutputPort, foundConnection.SourceOutputPort);
                Assert.AreEqual(node2, foundConnection.Destination);
                Assert.AreEqual(k_InputPort, foundConnection.DestinationInputPort);
                Assert.AreEqual(111, foundConnection.TraversalFlags);
            }
        }

        [Test]
        public void CanCreate_MultiEdgeGraph()
        {
            using (var topoDB = new TopologyTestDatabase(Allocator.Persistent))
            {
                var node1 = topoDB.CreateNode();
                var node2 = topoDB.CreateNode();

                topoDB.Connect(node1, k_OutputPort, node2, k_InputPort);
                topoDB.Connect(node1, k_OutputPort, node2, k_InputPort);
                Assert.IsTrue(topoDB.ConnectionExists(node1, k_OutputPort, node2, k_InputPort));
                topoDB.Disconnect(node1, k_OutputPort, node2, k_InputPort);
                Assert.IsTrue(topoDB.ConnectionExists(node1, k_OutputPort, node2, k_InputPort));
                topoDB.Disconnect(node1, k_OutputPort, node2, k_InputPort);
                Assert.IsFalse(topoDB.ConnectionExists(node1, k_OutputPort, node2, k_InputPort));
            }
        }

        [Test]
        public void CanDisconnect()
        {
            using (var topoDB = new TopologyTestDatabase(Allocator.Persistent))
            {
                var node1 = topoDB.CreateNode();
                var node2 = topoDB.CreateNode();

                topoDB.Connect(node1, k_OutputPort, node2, k_InputPort);
                Assert.IsTrue(topoDB.ConnectionExists(node1, k_OutputPort, node2, k_InputPort));
                topoDB.Disconnect(node1, k_OutputPort, node2, k_InputPort);
                Assert.IsFalse(topoDB.ConnectionExists(node1, k_OutputPort, node2, k_InputPort));
            }
        }

        [Test]
        public void CannotDisconnectTwice()
        {
            using (var topoDB = new TopologyTestDatabase(Allocator.Persistent))
            {
                var node1 = topoDB.CreateNode();
                var node2 = topoDB.CreateNode();

                topoDB.Connect(node1, k_OutputPort, node2, k_InputPort);
                Assert.IsTrue(topoDB.ConnectionExists(node1, k_OutputPort, node2, k_InputPort));
                topoDB.Disconnect(node1, k_OutputPort, node2, k_InputPort);
                Assert.IsFalse(topoDB.ConnectionExists(node1, k_OutputPort, node2, k_InputPort));
                Assert.Throws<ArgumentException>(() => topoDB.Disconnect(node1, k_OutputPort, node2, k_InputPort));
            }
        }

        [Test]
        public void CanMakeTheSameConnectionTwice_UsingDifferentTraversalFlags()
        {
            using (var topoDB = new TopologyTestDatabase(Allocator.Persistent))
            {
                var node1 = topoDB.CreateNode();
                var node2 = topoDB.CreateNode();

                topoDB.Connect(111, node1, k_OutputPort, node2, k_InputPort);
                topoDB.Connect(222, node1, k_OutputPort, node2, k_InputPort);
            }
        }

        [Test]
        public void CanMakeConnections_BetweenConnectedNodes_OnDifferentPorts()
        {
            using (var topoDB = new TopologyTestDatabase(Allocator.Persistent))
            {
                var node1 = topoDB.CreateNode();
                var node2 = topoDB.CreateNode();

                topoDB.Connect(node1, k_OutputPort, node2, k_InputPort);
                Assert.IsFalse(topoDB.ConnectionExists(node1, k_OutputPort, node2, k_SecondaryInputPort));
                topoDB.Connect(node1, k_OutputPort, node2, k_SecondaryInputPort);
                Assert.IsFalse(topoDB.ConnectionExists(node1, k_SecondaryOutputPort, node2, k_InputPort));
                topoDB.Connect(node1, k_SecondaryOutputPort, node2, k_InputPort);
                Assert.IsFalse(topoDB.ConnectionExists(node1, k_SecondaryOutputPort, node2, k_SecondaryInputPort));
                topoDB.Connect(node1, k_SecondaryOutputPort, node2, k_SecondaryInputPort);

                Assert.IsTrue(topoDB.ConnectionExists(node1, k_OutputPort, node2, k_InputPort));
                Assert.IsTrue(topoDB.ConnectionExists(node1, k_OutputPort, node2, k_SecondaryInputPort));
                Assert.IsTrue(topoDB.ConnectionExists(node1, k_SecondaryOutputPort, node2, k_InputPort));
                Assert.IsTrue(topoDB.ConnectionExists(node1, k_SecondaryOutputPort, node2, k_SecondaryInputPort));
            }
        }

        [Test]
        public void CanDisconnect_DirectlyByConnection()
        {
            using (var topoDB = new TopologyTestDatabase(Allocator.Persistent))
            {
                var node1 = topoDB.CreateNode();
                var node2 = topoDB.CreateNode();

                topoDB.Connect(node1, k_OutputPort, node2, k_InputPort);
                var connection = topoDB.FindConnection(node1, k_OutputPort, node2, k_InputPort);
                Assert.IsTrue(connection.Valid);
                topoDB.DisconnectAndRelease(connection);
                Assert.IsFalse(topoDB.ConnectionExists(node1, k_OutputPort, node2, k_InputPort));
            }
        }

        [Test]
        public void DisconnectAll_DisconnectsOutputs()
        {
            using (var topoDB = new TopologyTestDatabase(Allocator.Persistent))
            {
                var node1 = topoDB.CreateNode();
                var node2 = topoDB.CreateNode();
                var node3 = topoDB.CreateNode();

                topoDB.Connect(node1, k_OutputPort, node2, k_InputPort);
                topoDB.Connect(node1, k_OutputPort, node3, k_InputPort);
                topoDB.Connect(node1, k_SecondaryOutputPort, node3, k_SecondaryInputPort);
                Assert.AreEqual(3, topoDB.Connections.CountEstablishedConnections());

                topoDB.DisconnectAll(node1);
                Assert.AreEqual(0, topoDB.Connections.CountEstablishedConnections());
            }
        }

        [Test]
        public void DisconnectAll_DisconnectsInputs()
        {
            using (var topoDB = new TopologyTestDatabase(Allocator.Persistent))
            {
                var node1 = topoDB.CreateNode();
                var node2 = topoDB.CreateNode();
                var node3 = topoDB.CreateNode();

                topoDB.Connect(node1, k_OutputPort, node3, k_InputPort);
                topoDB.Connect(node2, k_OutputPort, node3, k_InputPort);
                topoDB.Connect(node2, k_OutputPort, node3, k_SecondaryInputPort);
                Assert.AreEqual(3, topoDB.Connections.CountEstablishedConnections());

                topoDB.DisconnectAll(node3);
                Assert.AreEqual(0, topoDB.Connections.CountEstablishedConnections());
            }
        }

        [Test]
        public void CannotDisconnectNonExistentConnection()
        {
            using (var topoDB = new TopologyTestDatabase(Allocator.Persistent))
            {
                var node1 = topoDB.CreateNode();
                var node2 = topoDB.CreateNode();

                Assert.Throws<ArgumentException>(() => topoDB.Disconnect(node1, k_OutputPort, node2, k_InputPort));
            }
        }
    }
}
