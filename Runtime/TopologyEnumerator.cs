using System;

namespace Unity.DataFlowGraph
{
    static partial class TopologyAPI<TVertex, TInputPort, TOutputPort>
        where TVertex : unmanaged, IEquatable<TVertex>
        where TInputPort : unmanaged, IEquatable<TInputPort>
        where TOutputPort : unmanaged, IEquatable<TOutputPort>
    {
        public partial struct Database
        {
            internal interface IMoveable
            {
                bool MoveNext();
            }

            internal struct InputTopologyEnumerable /* : IEnumerable<ConnectionWalker> */
            {
                public struct ConnectionEnumerator : IMoveable
                {
                    public struct NodeEnumeratorByPort : IMoveable
                    {
                        public NodeEnumeratorByPort GetEnumerator() => this;
                        public TVertex Current => m_Walker.Current.Source;
                        public ref readonly Connection Connection => ref m_Walker.Current;


                        ConnectionEnumerator m_Walker;
                        TInputPort m_Filter;

                        internal NodeEnumeratorByPort(ConnectionEnumerator parent, TInputPort filter)
                        {
                            m_Walker = parent;
                            m_Filter = filter;
                        }

                        public bool MoveNext()
                        {
                            while (m_Walker.MoveNext())
                            {
                                if (m_Walker.Current.DestinationInputPort.Equals(m_Filter))
                                    return true;
                            }

                            return false;
                        }
                    }

                    public ref readonly Connection Current => ref m_Database.IndexConnectionInternal(m_It);
                    public ConnectionEnumerator GetEnumerator() => this;

                    Database m_Database;
                    ConnectionHandle m_It;
                    bool m_PastOne;

                    public ConnectionEnumerator(in Database database, ConnectionHandle startingConnection)
                    {
                        m_Database = database;
                        m_It = startingConnection;
                        m_PastOne = false;
                    }

                    public bool MoveNext()
                    {
                        if (!m_PastOne)
                        {
                            m_PastOne = true;
                            return m_It != InvalidConnection;
                        }

                        m_It = Current.NextInputConnection;
                        return m_It != InvalidConnection;
                    }
                }

                internal InputTopologyEnumerable(Database database, in TopologyIndex index)
                {
                    m_TopologyIndex = index;
                    m_Database = database;
                }

                Database m_Database;
                TopologyIndex m_TopologyIndex;

                /// <summary>
                /// Returns a filtered vertex/connection enumerator connected to this vertex'
                /// input port.
                /// </summary>
                public ConnectionEnumerator.NodeEnumeratorByPort this[TInputPort port]
                    => new ConnectionEnumerator.NodeEnumeratorByPort(GetEnumerator(), port);

                public ConnectionEnumerator GetEnumerator()
                    => new ConnectionEnumerator(m_Database, m_TopologyIndex.InputHeadConnection);
            }

            internal struct OutputTopologyEnumerable /* : IEnumerable<ConnectionWalker> */
            {
                internal struct ConnectionEnumerator : IMoveable
                {
                    internal struct NodeEnumeratorByPort : IMoveable
                    {
                        public NodeEnumeratorByPort GetEnumerator() => this;
                        public TVertex Current => m_Walker.Current.Destination;
                        public ref readonly Connection Connection => ref m_Walker.Current;

                        ConnectionEnumerator m_Walker;
                        TOutputPort m_Filter;

                        internal NodeEnumeratorByPort(ConnectionEnumerator parent, TOutputPort filter)
                        {
                            m_Walker = parent;
                            m_Filter = filter;
                        }

                        public bool MoveNext()
                        {
                            while (m_Walker.MoveNext())
                            {
                                if (m_Walker.Current.SourceOutputPort.Equals(m_Filter))
                                    return true;
                            }

                            return false;
                        }
                    }

                    public ref readonly Connection Current => ref m_Database.IndexConnectionInternal(m_It);
                    public ConnectionEnumerator GetEnumerator() => this;

                    // TODO: Would be nice to only copy the connection list, not entire database
                    Database m_Database;
                    ConnectionHandle m_It;
                    bool m_PastOne;

                    public ConnectionEnumerator(in Database database, ConnectionHandle startingConnection)
                    {
                        m_Database = database;
                        m_It = startingConnection;
                        m_PastOne = false;
                    }

                    public bool MoveNext()
                    {
                        if (!m_PastOne)
                        {
                            m_PastOne = true;
                            return m_It != InvalidConnection;
                        }

                        m_It = Current.NextOutputConnection;

                        return m_It != InvalidConnection;
                    }
                }

                internal OutputTopologyEnumerable(Database database, in TopologyIndex index)
                {
                    m_TopologyIndex = index;
                    m_Database = database;
                }

                Database m_Database;
                TopologyIndex m_TopologyIndex;

                /// <summary>
                /// Returns a filtered vertex/connection enumerator connected to this vertex'
                /// output port.
                /// </summary>
                public ConnectionEnumerator.NodeEnumeratorByPort this[TOutputPort port]
                    => new ConnectionEnumerator.NodeEnumeratorByPort(GetEnumerator(), port);

                public ConnectionEnumerator GetEnumerator()
                    => new ConnectionEnumerator(m_Database, m_TopologyIndex.OutputHeadConnection);
            }

            internal InputTopologyEnumerable GetInputs(in TopologyIndex topologyIndex)
            {
                return new InputTopologyEnumerable(this, topologyIndex);
            }

            internal OutputTopologyEnumerable GetOutputs(in TopologyIndex topologyIndex)
            {
                return new OutputTopologyEnumerable(this, topologyIndex);
            }
        }
    }

}
