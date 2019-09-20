using System;
using Unity.Collections;

namespace Unity.DataFlowGraph
{
    struct ConnectionHandle
    {
        public int Index;

        public static implicit operator ConnectionHandle(int arg)
        {
            return new ConnectionHandle { Index = arg };
        }

        public static implicit operator int(ConnectionHandle handle)
        {
            return handle.Index;
        }
    }

    struct NodeIndexHandle
    {
        public int Index;

        public static implicit operator NodeIndexHandle(int arg)
        {
            return new NodeIndexHandle { Index = arg };
        }

        public static implicit operator int(NodeIndexHandle handle)
        {
            return handle.Index;
        }
    }


    struct TopologyIndex
    {
        public ConnectionHandle InputHeadConnection;
        public ConnectionHandle OutputHeadConnection;

        public ushort InputPortCount;
        public ushort OutputPortCount;

        // TODO: Remove; see usage in TopologyCacheAPI
        // The reason this exists here is that the topology cache manager
        // always needs O(N) scratch space, where N != absolute amount of nodes.
        // So if you compute topology for 5 nodes, it may still need 10000 scratch space
        // (however will only touch 5 of those indices).
        public int VisitCacheIndex;
    }

    // TODO: Figure out a good way to sync these values with Ports.Usage
    // They are different in that this is a mask, and the other is strictly
    // single values
    [Flags]
    enum TraversalFlags
    {
        None = 0 << 0,
        Message = 1 << 0,
        DataFlow = 1 << 1,
        DSL = 1 << 2,
        All = -1
    }

    struct Connection
    {
        public ConnectionHandle HandleToSelf;

        public NodeHandle DestinationHandle;
        public InputPortArrayID DestinationInputPort;

        public NodeHandle SourceHandle;
        public OutputPortID SourceOutputPort;

        public ConnectionHandle NextInputConnection;
        public ConnectionHandle NextOutputConnection;

        public bool Valid;
        public TraversalFlags ConnectionType;
    }

    struct TopologyDatabase : IDisposable
    {
        // TODO: Use this for all iterator patterns (next planned PR).
        public const int InvalidConnection = 0;

        public BlitList<TopologyIndex> Indexes;
        public BlitList<Connection> Connections;

        public int IndexCount => Indexes.Count;
        public bool IsCreated => Indexes.IsCreated;

        internal BlitList<int> m_FreeConnections;

        public TopologyDatabase(int capacity, Allocator allocator)
        {
            Indexes = new BlitList<TopologyIndex>(0, allocator);
            Connections = new BlitList<Connection>(1, allocator);
            m_FreeConnections = new BlitList<int>(0, allocator);

            Indexes.Reserve(capacity);
            Connections.Reserve(capacity);
            m_FreeConnections.Reserve(capacity);
        }

        public void Dispose()
        {
            Indexes.Dispose();
            Connections.Dispose();
            m_FreeConnections.Dispose();
        }

        /// <summary>
        /// Ensures the topology can handle NodeHandle with indexes of this size
        /// </summary>
        /// <see cref="IndexCount"/>
        public void EnsureSize(int count)
        {
            Indexes.EnsureSize(count);
        }

        public void Connect(TraversalFlags connectionClass, NodeHandle source, OutputPortID sourcePort, NodeHandle dest, InputPortArrayID destinationPort)
        {
            ref var sourceTopology = ref Indexes[source.VHandle.Index];
            ref var destTopology = ref Indexes[dest.VHandle.Index];

            var potentialConnection = FindConnection(ref destTopology, source, sourcePort, dest, destinationPort);

            if (Connections[potentialConnection].Valid)
                throw new ArgumentException("Connections already exists!");

            // TODO: Check recursion at some point (cycle detection in graph)..

            var connHandle = AcquireConnection();

            ref var newConnection = ref Connections[connHandle];

            newConnection.ConnectionType = connectionClass;
            newConnection.DestinationHandle = new NodeHandle(dest.VHandle);
            newConnection.DestinationInputPort = destinationPort;
            newConnection.SourceHandle = new NodeHandle(source.VHandle);
            newConnection.SourceOutputPort = sourcePort;
            newConnection.NextInputConnection = destTopology.InputHeadConnection;
            newConnection.NextOutputConnection = sourceTopology.OutputHeadConnection;

            destTopology.InputHeadConnection = connHandle;
            sourceTopology.OutputHeadConnection = connHandle;
        }

        public void Disconnect(NodeHandle sourceHandle, OutputPortID sourcePort, NodeHandle destHandle, InputPortArrayID destinationPort)
        {
            var connectionHandle = FindConnection(ref Indexes[destHandle.VHandle.Index], sourceHandle, sourcePort, destHandle, destinationPort);
            ref var connection = ref Connections[connectionHandle];

            DisconnectAndRelease(ref connection);
        }

        public int DisconnectAll(NodeHandle handle)
        {
            int disconnections = 0;
            ref var index = ref Indexes[handle.VHandle.Index];

            var it = index.InputHeadConnection;
            while (true)
            {
                ref var connection = ref Connections[it];

                if (!connection.Valid)
                    break;

                it = connection.NextInputConnection;

                DisconnectAndRelease(ref connection);
                disconnections++;
            }

            it = index.OutputHeadConnection;
            while (true)
            {
                ref var connection = ref Connections[it];

                if (!connection.Valid)
                    break;

                it = connection.NextOutputConnection;

                DisconnectAndRelease(ref connection);
                disconnections++;
            }

            return disconnections;
        }

        public bool ConnectionExists(NodeHandle source, OutputPortID sourcePort, NodeHandle dest, InputPortArrayID destPort)
        {
            return FindConnection(source, sourcePort, dest, destPort).Valid;
        }

        public ref Connection FindConnection(NodeHandle source, OutputPortID sourcePort, NodeHandle dest, InputPortArrayID destPort)
        {
            ref var destTopology = ref Indexes[dest.VHandle.Index];
            var potentialConnection = FindConnection(ref destTopology, source, sourcePort, dest, destPort);

            return ref Connections[potentialConnection];
        }

        public void DisconnectAndRelease(ref Connection connection)
        {
            // Disconnect database
            bool listWasPatched = false;
            bool hadToWalkLists = false;

            ref var destTopology = ref Indexes[connection.DestinationHandle.VHandle.Index];

            // If we are removing the head of the destination, just change
            // the topology head to the next link
            if (connection.HandleToSelf == destTopology.InputHeadConnection)
            {
                destTopology.InputHeadConnection = connection.NextInputConnection;
            }
            else
            {
                // otherwise, we need to walk the list to find the preceding
                // connection and patch up the next link
                hadToWalkLists = true;
                // TODO: Doubly linked list would make this O(1)

                var it = destTopology.InputHeadConnection;

                while (true)
                {
                    ref var prevConnection = ref Connections[it];

                    if (!prevConnection.Valid)
                        break;

                    if (prevConnection.NextInputConnection == connection.HandleToSelf)
                    {
                        prevConnection.NextInputConnection = connection.NextInputConnection;
                        listWasPatched = true;
                        break;
                    }

                    it = prevConnection.NextInputConnection;
                }

                if (!listWasPatched)
                    throw new InvalidOperationException("Internal list structure invalid");
            }

            ref var sourceTopology = ref Indexes[connection.SourceHandle.VHandle.Index];

            // Similarly for source, patch up the head or the list
            if (connection.HandleToSelf == sourceTopology.OutputHeadConnection)
            {
                sourceTopology.OutputHeadConnection = connection.NextOutputConnection;
            }
            else
            {
                hadToWalkLists = true;

                var it = sourceTopology.OutputHeadConnection;

                while (true)
                {
                    ref var prevConnection = ref Connections[it];

                    if (!prevConnection.Valid)
                        break;

                    if (prevConnection.NextOutputConnection == connection.HandleToSelf)
                    {
                        prevConnection.NextOutputConnection = connection.NextOutputConnection;
                        listWasPatched = true;
                        break;
                    }

                    it = prevConnection.NextOutputConnection;
                }

            }

            if (hadToWalkLists && !listWasPatched)
                throw new InvalidOperationException("Internal list structure invalid; connection handle not found in any heads");

            // everything good
            ReleaseConnection(connection.HandleToSelf);
        }

        ConnectionHandle AcquireConnection()
        {
            ConnectionHandle index;

            if (m_FreeConnections.Count > 0)
            {
                index = m_FreeConnections[m_FreeConnections.Count - 1];
                m_FreeConnections.PopBack();
            }
            else
            {
                Connections.Add(new Connection());
                index = Connections.Count - 1;
            }

            Connections[index].Valid = true;
            Connections[index].HandleToSelf = index;

            return index;
        }

        ConnectionHandle FindConnection(ref TopologyIndex index, NodeHandle source, OutputPortID sourceOutputPort, NodeHandle dest, InputPortArrayID destInputPort)
        {
            ConnectionHandle it = index.InputHeadConnection;

            while (true)
            {
                ref var connection = ref Connections[it];

                if (!connection.Valid)
                    break;

                if (connection.DestinationHandle == dest &&
                    connection.DestinationInputPort == destInputPort &&
                    connection.SourceHandle == source &&
                    connection.SourceOutputPort == sourceOutputPort)
                {
                    return it;
                }

                it = connection.NextInputConnection;
            }

            return InvalidConnection;
        }

        void ReleaseConnection(ConnectionHandle connHandle)
        {
            ref var c = ref Connections[connHandle];
            c.Valid = false;
            m_FreeConnections.Add(connHandle);
        }
    }

}
