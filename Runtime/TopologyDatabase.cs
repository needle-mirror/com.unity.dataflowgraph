using System;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.DataFlowGraph
{
    struct ConnectionHandle
    {
        public int Index;

        public static implicit operator ConnectionHandle(int arg)
        {
            return new ConnectionHandle {Index = arg};
        }

        public static implicit operator int(ConnectionHandle handle)
        {
            return handle.Index;
        }
    }

    struct TopologyIndex
    {
        public ConnectionHandle InputHeadConnection;
        public ConnectionHandle OutputHeadConnection;

        // TODO: Remove; see usage in TopologyCacheAPI
        // The reason this exists here is that the topology cache manager
        // always needs O(N) scratch space, where N != absolute amount of nodes.
        // So if you compute topology for 5 nodes, it may still need 10000 scratch space
        // (however will only touch 5 of those indices).
        public int VisitCacheIndex;
    }

    static partial class TopologyAPI<TVertex, TInputPort, TOutputPort>
        where TVertex : unmanaged, IEquatable<TVertex>
        where TInputPort : unmanaged, IEquatable<TInputPort>
        where TOutputPort : unmanaged, IEquatable<TOutputPort>
    {
        public struct Connection
        {
            public ConnectionHandle HandleToSelf;

            public TVertex Destination;
            public TInputPort DestinationInputPort;

            public TVertex Source;
            public TOutputPort SourceOutputPort;

            public ConnectionHandle NextInputConnection;
            public ConnectionHandle NextOutputConnection;

            public uint TraversalFlags;
            public bool Valid;
        }

        public partial struct Database
        {
            /// <summary>
            /// Interface that can resolve a <see cref="TopologyIndex"/> from an <typeparamref name="TVertex"/>.
            /// This is needed for most API on the <see cref="Database"/>.
            /// </summary>
            public interface ITopologyFromVertex
            {
                // TODO: Would be lovely if we could ref-return here, but this is incompatible with ComponentDataFromEntity
                TopologyIndex this[TVertex vertex] { get; set; }
            }

            /// <summary>
            /// Any valid <see cref="ConnectionHandle"/> will compare unequal against this constant.
            /// </summary>
            public const int InvalidConnection = 0;

            /// <summary>
            /// Test whether this <see cref="Database"/> is created, and not disposed.
            /// </summary>
            public bool IsCreated => m_Conns.IsCreated;

            /// <summary>
            /// The amount of free connections internally available in the database.
            /// </summary>
            public int FreeEdges => m_FreeConnections.Count;

            /// <summary>
            /// Returns the total amount of indexable connections, whether valid or not.
            /// </summary>
            public int TotalConnections => m_Conns.Length;

            /// <summary>
            /// Look up a <see cref="Connection"/> from a <see cref="ConnectionHandle"/>.
            /// </summary>
            /// <returns>
            /// A reference to an immutable <see cref="Connection"/>.
            /// </returns>
            /// <exception cref="IndexOutOfRangeException">If the <paramref name="handle"/> is not indexable or valid.</exception>
            public unsafe ref /*readonly - Burst crashes if enabled*/ Connection this[ConnectionHandle handle]
            {
                get
                {
                    if (m_Conns.Length > (uint) handle.Index)
                        return ref Unsafe.AsRef<Connection>(m_Conns.Ptr + m_SizeOf * handle.Index);

                    throw new IndexOutOfRangeException("ConnectionHandle was out of range");
                }
            }

            /// <summary>
            /// Count the number of valid <see cref="Connection"/>s in the <see cref="Database"/>.
            /// </summary>
            /// <remarks>O(N)</remarks>
            public int CountEstablishedConnections()
            {
                int ret = 0;
                for (int i = 0; i < m_Conns.Length; ++i)
                    if (IndexConnectionInternal(i).Valid)
                        ret++;

                return ret;
            }

            unsafe struct UnsafeConnectionList : IDisposable
            {
                public bool IsCreated => Ptr != null;

                [NativeDisableUnsafePtrRestriction]
                public byte* Ptr;
                public int Length;
                public int Capacity;
                public Allocator Allocator;

                public void Dispose()
                {
                    var list = ToUnsafeList();
                    list.Dispose();
                    this = list;
                }

                public void Add(in Connection v)
                {
                    var list = ToUnsafeList();
                    list.Add(v);
                    this = list;
                }

                UnsafeList ToUnsafeList()
                {
                    return new UnsafeList {Ptr = Ptr, Length = Length, Capacity = Capacity, Allocator = Allocator};
                }

                public static implicit operator UnsafeConnectionList(UnsafeList target)
                {
                    return new UnsafeConnectionList
                    {
                        Ptr = (byte*) target.Ptr,
                        Length = target.Length,
                        Capacity = target.Capacity,
                        Allocator = target.Allocator
                    };
                }
            }

            unsafe ref Connection IndexConnectionInternal(int index)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (m_Conns.Ptr == null)
                    throw new NullReferenceException();
#endif
                return ref Unsafe.AsRef<Connection>(m_Conns.Ptr + m_SizeOf * index);
            }

            UnsafeConnectionList m_Conns;

            int m_SizeOf;

            // TODO: Use FreeList once it supports constructed generics.
            BlitList<int> m_FreeConnections;


            public Database(int capacity, Allocator allocator)
            {
                m_SizeOf = UnsafeUtility.SizeOf<Connection>();

                m_Conns = new UnsafeList(m_SizeOf, UnsafeUtility.AlignOf<Connection>(), capacity, allocator, NativeArrayOptions.ClearMemory);
                m_Conns.Add(new Connection());
                m_FreeConnections = new BlitList<int>(0, allocator);
                m_FreeConnections.Reserve(capacity);
            }


            public void Dispose()
            {
                m_Conns.Dispose();
                m_FreeConnections.Dispose();
            }

            /// <summary>
            /// Connect two vertices together.
            /// </summary>
            /// <exception cref="ArgumentException">Thrown if the connection already exists.</exception>
            public void Connect<TTopologyFromVertex>(
                ref TTopologyFromVertex topologyResolver,
                uint traversalFlags,
                TVertex source,
                TOutputPort sourcePort,
                TVertex dest,
                TInputPort destinationPort
            )
                where TTopologyFromVertex : ITopologyFromVertex
            {
                var sourceTopology = topologyResolver[source];
                var destTopology = topologyResolver[dest];

                // TODO: Check recursion at some point (cycle detection in graph)..

                var connHandle = AcquireConnection();

                ref var newConnection = ref IndexConnectionInternal(connHandle);

                newConnection.TraversalFlags = traversalFlags;
                newConnection.Destination = dest;
                newConnection.DestinationInputPort = destinationPort;
                newConnection.Source = source;
                newConnection.SourceOutputPort = sourcePort;
                newConnection.NextInputConnection = destTopology.InputHeadConnection;
                newConnection.NextOutputConnection = sourceTopology.OutputHeadConnection;

                destTopology.InputHeadConnection = connHandle;
                sourceTopology.OutputHeadConnection = connHandle;

                topologyResolver[source] = sourceTopology;
                topologyResolver[dest] = destTopology;
            }

            /// <summary>
            /// Disconnect the connection, and release it.
            /// <seealso cref="FindConnection{TTopologyFromVertex}(ref TTopologyFromVertex, TVertex, TOutputPort, TVertex, TInputPort)"/>
            /// <seealso cref="DisconnectAndRelease{TTopologyFromVertex}(ref TTopologyFromVertex, in Connection)"/>
            /// </summary>
            /// <exception cref="ArgumentException">Thrown if the exception does not exist</exception>
            public void Disconnect<TTopologyFromVertex>(
                ref TTopologyFromVertex topologyResolver,
                TVertex source,
                TOutputPort sourcePort,
                TVertex dest,
                TInputPort destinationPort
            )
                where TTopologyFromVertex : ITopologyFromVertex
            {
                var connectionHandle = FindConnection(topologyResolver[dest], source, sourcePort, dest, destinationPort);
                ref var connection = ref IndexConnectionInternal(connectionHandle);

                if (!connection.Valid)
                    throw new ArgumentException("Connection does not exist!");

                DisconnectAndRelease(ref topologyResolver, connection);
            }

            /// <summary>
            /// Disconnect and release all connections on the <see cref="TopologyIndex"/> resolved by the <paramref name="topologyResolver"/>.
            /// </summary>
            /// <returns>
            /// Returns the amount of disconnections done.
            /// </returns>
            /// <exception cref="ArgumentException">Thrown if there's an invalid linked connection. 
            /// This can happen when using <see cref="DisconnectAndRelease{TTopologyFromVertex}(ref TTopologyFromVertex, in Connection)"/>
            /// </exception>
            /// <exception cref="InvalidOperationException">Thrown if the <see cref="Database"/> has been corrupted. 
            /// This can happen when using <see cref="DisconnectAndRelease{TTopologyFromVertex}(ref TTopologyFromVertex, in Connection)"/>
            /// </exception>
            public int DisconnectAll<TTopologyFromVertex>(ref TTopologyFromVertex topologyResolver, TVertex vertex)
                where TTopologyFromVertex : ITopologyFromVertex
            {
                int disconnections = 0;
                var index = topologyResolver[vertex];

                for (var it = index.InputHeadConnection; it != InvalidConnection; it = IndexConnectionInternal(it).NextInputConnection)
                {
                    DisconnectAndRelease(ref topologyResolver, IndexConnectionInternal(it));
                    disconnections++;
                }

                for (var it = index.OutputHeadConnection; it != InvalidConnection; it = IndexConnectionInternal(it).NextOutputConnection)
                {
                    DisconnectAndRelease(ref topologyResolver, IndexConnectionInternal(it));
                    disconnections++;
                }

                return disconnections;
            }

            /// <summary>
            /// Tests whether a connection exists.
            /// <seealso cref="FindConnection{TTopologyFromVertex}(ref TTopologyFromVertex, TVertex, TOutputPort, TVertex, TInputPort)"/>
            /// </summary>
            public bool ConnectionExists<TTopologyFromVertex>(
                ref TTopologyFromVertex topologyResolver,
                TVertex source,
                TOutputPort sourcePort,
                TVertex dest,
                TInputPort destPort
            )
                where TTopologyFromVertex : ITopologyFromVertex
            {
                return FindConnection(ref topologyResolver, source, sourcePort, dest, destPort).Valid;
            }


            /// <summary>
            /// Look up a connection, and return it.
            /// </summary>
            /// <remarks>If the connection is not found, the returned connection's <see cref="Connection.Valid"/> will be false.</remarks>
            public ref readonly Connection FindConnection<TTopologyFromVertex>(
                ref TTopologyFromVertex topologyResolver,
                TVertex source,
                TOutputPort sourcePort,
                TVertex dest,
                TInputPort destPort
            )
                where TTopologyFromVertex : ITopologyFromVertex
            {
                var potentialConnection = FindConnection(topologyResolver[dest], source, sourcePort, dest, destPort);
                return ref IndexConnectionInternal(potentialConnection);
            }

            /// <summary>
            /// Disconnect and release an already resolved connection.
            /// <seealso cref="Disconnect{TTopologyFromVertex}(ref TTopologyFromVertex, TVertex, TOutputPort, TVertex, TInputPort)"/>
            /// </summary>
            /// <remarks>
            /// This does not check whether the connection is valid, so you can potentially corrupt the database.
            /// Use <see cref="Disconnect{TTopologyFromVertex}(ref TTopologyFromVertex, TVertex, TOutputPort, TVertex, TInputPort)"/> 
            /// for a safe but slower alternative.
            /// </remarks>
            /// <exception cref="InvalidOperationException">
            /// Thrown if the <see cref="Database"/> has been corrupted. 
            /// See Remarks section.
            /// </exception>
            public void DisconnectAndRelease<TTopologyFromVertex>(
                ref TTopologyFromVertex topologyResolver,
                in Connection connection
            )
                where TTopologyFromVertex : ITopologyFromVertex
            {
                // Disconnect database
                bool listWasPatched = false;
                bool hadToWalkLists = false;

                var destTopology = topologyResolver[connection.Destination];

                // If we are removing the head of the destination, just change
                // the topology head to the next link
                if (connection.HandleToSelf == destTopology.InputHeadConnection)
                {
                    destTopology.InputHeadConnection = connection.NextInputConnection;
                    topologyResolver[connection.Destination] = destTopology;
                }
                else
                {
                    // otherwise, we need to walk the list to find the preceding
                    // connection and patch up the next link
                    hadToWalkLists = true;
                    // TODO: Doubly linked list would make this O(1)

                    for (var it = destTopology.InputHeadConnection; it != InvalidConnection; it = IndexConnectionInternal(it).NextInputConnection)
                    {
                        ref var prevConnection = ref IndexConnectionInternal(it);

                        if (prevConnection.NextInputConnection == connection.HandleToSelf)
                        {
                            prevConnection.NextInputConnection = connection.NextInputConnection;
                            listWasPatched = true;
                            break;
                        }
                    }

                    if (!listWasPatched)
                        throw new InvalidOperationException("Internal list structure invalid");
                }

                var sourceTopology = topologyResolver[connection.Source];

                // Similarly for source, patch up the head or the list
                if (connection.HandleToSelf == sourceTopology.OutputHeadConnection)
                {
                    sourceTopology.OutputHeadConnection = connection.NextOutputConnection;
                    topologyResolver[connection.Source] = sourceTopology;
                }
                else
                {
                    hadToWalkLists = true;

                    for (var it = sourceTopology.OutputHeadConnection; it != InvalidConnection; it = IndexConnectionInternal(it).NextOutputConnection)
                    {
                        ref var prevConnection = ref IndexConnectionInternal(it);

                        if (prevConnection.NextOutputConnection == connection.HandleToSelf)
                        {
                            prevConnection.NextOutputConnection = connection.NextOutputConnection;
                            listWasPatched = true;
                            break;
                        }
                    }
                }

                if (hadToWalkLists && !listWasPatched)
                    throw new InvalidOperationException(
                        "Internal list structure invalid; connection handle not found in any heads");

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
                    m_Conns.Add(new Connection());
                    index = m_Conns.Length - 1;
                }

                ref var c = ref IndexConnectionInternal(index);

                c.Valid = true;
                c.HandleToSelf = index;

                return index;
            }

            ConnectionHandle FindConnection(
                in TopologyIndex index,
                TVertex source,
                TOutputPort sourceOutputPort,
                TVertex dest,
                TInputPort destInputPort
            )
            {
                for (var it = index.InputHeadConnection; it != InvalidConnection; it = IndexConnectionInternal(it).NextInputConnection)
                {
                    ref var connection = ref IndexConnectionInternal(it);

                    if (connection.Destination.Equals(dest) &&
                        connection.DestinationInputPort.Equals(destInputPort) &&
                        connection.Source.Equals(source) &&
                        connection.SourceOutputPort.Equals(sourceOutputPort))
                    {
                        return it;
                    }
                }

                return InvalidConnection;
            }

            void ReleaseConnection(ConnectionHandle connHandle)
            {
                ref var c = ref IndexConnectionInternal(connHandle);
                c.Valid = false;
                m_FreeConnections.Add(connHandle);
            }
        }
    }
}
