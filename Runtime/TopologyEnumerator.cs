using System;
using System.Collections;
using System.Collections.Generic;

namespace Unity.DataFlowGraph
{
    public partial class NodeSet
    {
        internal struct NodeEnumerator<TPortID, TTopologyEnumerator> : IEnumerator<NodeHandle>, IEnumerable<NodeHandle>
            where TPortID : IPortID
            where TTopologyEnumerator : ITopologyEnumerator<TPortID>
        {
            internal NodeEnumerator(TTopologyEnumerator parent, TPortID port)
            {
                this.parent = parent;
                this.port = port;
                index = 0;
                Count = parent.Connections(port);
            }

            TTopologyEnumerator parent;
            TPortID port;
            int index;

            public int Count { get; private set; }

            public NodeHandle Current => parent[port, index - 1];

            public NodeHandle this[int userIndex] => parent[port, userIndex];

            object IEnumerator.Current => null;

            public void Dispose() { }

            public bool MoveNext()
            {
                index++;
                return (index - 1) < Count;
            }

            public void Reset()
            {
                index = 0;
            }

            public NodeEnumerator<TPortID, TTopologyEnumerator> GetEnumerator()
            {
                return this;
            }

            IEnumerator<NodeHandle> IEnumerable<NodeHandle>.GetEnumerator()
            {
                return GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }

        internal struct InputPortEnumerator : IEnumerator<NodeEnumerator<InputPortID, InputTopologyEnumerator>>, IEnumerable<NodeHandle>
        {
            internal InputPortEnumerator(InputTopologyEnumerator parent)
            {
                this.parent = parent;
                port = new InputPortID(0);
            }

            InputTopologyEnumerator parent;
            InputPortID port;

            public NodeEnumerator<InputPortID, InputTopologyEnumerator> Current => new NodeEnumerator<InputPortID, InputTopologyEnumerator>(parent, new InputPortID((ushort)(port.Port - 1)));
            public NodeEnumerator<InputPortID, InputTopologyEnumerator> this[InputPortID port] => new NodeEnumerator<InputPortID, InputTopologyEnumerator>(parent, port);

            object IEnumerator.Current => null;

            public void Dispose() { }

            public bool MoveNext()
            {
                port = new InputPortID((ushort)(port.Port + 1));
                return (port.Port - 1) < parent.Count;
            }

            public void Reset()
            {
                port = new InputPortID(0);
            }

            public IEnumerator<NodeHandle> GetEnumerator()
            {
                return new NodeEnumerator<InputPortID, InputTopologyEnumerator>(parent, new InputPortID((ushort)(port.Port - 1)));
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }

        internal struct OutputPortEnumerator : IEnumerator<NodeEnumerator<OutputPortID, OutputTopologyEnumerator>>, IEnumerable<NodeHandle>
        {
            internal OutputPortEnumerator(OutputTopologyEnumerator parent)
            {
                this.parent = parent;
                port = new OutputPortID { Port = 0 };
            }

            OutputTopologyEnumerator parent;
            OutputPortID port;

            public NodeEnumerator<OutputPortID, OutputTopologyEnumerator> Current => new NodeEnumerator<OutputPortID, OutputTopologyEnumerator>(parent, new OutputPortID { Port = (ushort)(port.Port - 1) });
            public NodeEnumerator<OutputPortID, OutputTopologyEnumerator> this[OutputPortID port] => new NodeEnumerator<OutputPortID, OutputTopologyEnumerator>(parent, port);

            object IEnumerator.Current => null;

            public void Dispose() { }

            public bool MoveNext()
            {
                port.Port++;
                return (port.Port - 1) < parent.Count;
            }

            public void Reset()
            {
                port.Port = 0;
            }

            public IEnumerator<NodeHandle> GetEnumerator()
            {
                return new NodeEnumerator<OutputPortID, OutputTopologyEnumerator>(parent, new OutputPortID { Port = (ushort)(port.Port - 1) });
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }

        internal interface ITopologyEnumerator<TPortID>
            where TPortID : IPortID
        {
            NodeHandle this[TPortID port, int index] { get; }
            int Count { get; }
            int Connections(TPortID port);
        }

        internal struct InputTopologyEnumerator : IReadOnlyCollection<NodeEnumerator<InputPortID, InputTopologyEnumerator>>, ITopologyEnumerator<InputPortID>
        {
            internal InputTopologyEnumerator(NodeSet manager, ref TopologyIndex index)
            {
                m_TopologyIndex = index;
                m_Set = manager;
            }

            NodeSet m_Set;
            TopologyIndex m_TopologyIndex;

            public NodeHandle this[InputPortID port, int index]
            {
                get
                {
                    if (port.Port >= m_TopologyIndex.InputPortCount)
                        throw new IndexOutOfRangeException("Port index is out of range of port count");

                    var currIndex = 0;
                    var it = m_TopologyIndex.InputHeadConnection;
                    while (true)
                    {
                        ref var connection = ref m_Set.m_Topology.Connections[it];

                        if (!connection.Valid)
                            break;

                        if (connection.DestinationInputPort.PortID == port)
                        {
                            if (currIndex == index)
                                return connection.SourceHandle;

                            currIndex++;
                        }

                        it = connection.NextInputConnection;
                    }

                    throw new IndexOutOfRangeException("Index of connection or port does not exist");
                }
            }

            public NodeEnumerator<InputPortID, InputTopologyEnumerator> this[InputPortID port]
            {
                get
                {
                    if (port.Port >= Count)
                        throw new IndexOutOfRangeException();

                    return new NodeEnumerator<InputPortID, InputTopologyEnumerator>(this, port);
                }
            }

            public int Count => (int)m_TopologyIndex.InputPortCount;

            public int Connections(InputPortID port)
            {
                var indexCount = 0;
                var it = m_TopologyIndex.InputHeadConnection;

                while (true)
                {
                    ref var connection = ref m_Set.m_Topology.Connections[it];

                    if (!connection.Valid)
                        break;

                    if (connection.DestinationInputPort.PortID == port)
                        indexCount++;

                    it = connection.NextInputConnection;
                }

                return indexCount;
            }

            public InputPortEnumerator GetEnumerator()
            {
                return new InputPortEnumerator(this);
            }

            IEnumerator<NodeEnumerator<InputPortID, InputTopologyEnumerator>> IEnumerable<NodeEnumerator<InputPortID, InputTopologyEnumerator>>.GetEnumerator()
            {
                return GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }

        internal struct OutputTopologyEnumerator : IReadOnlyCollection<NodeEnumerator<OutputPortID, OutputTopologyEnumerator>>, ITopologyEnumerator<OutputPortID>
        {
            internal OutputTopologyEnumerator(NodeSet manager, ref TopologyIndex index)
            {
                m_TopologyIndex = index;
                m_Set = manager;
            }

            NodeSet m_Set;
            TopologyIndex m_TopologyIndex;

            public NodeHandle this[OutputPortID port, int index]
            {
                get
                {
                    if (port.Port >= m_TopologyIndex.OutputPortCount)
                        throw new IndexOutOfRangeException("Port index is out of range of port count");

                    //Outputs
                    var currIndex = 0;
                    var it = m_TopologyIndex.OutputHeadConnection;

                    while (true)
                    {
                        ref var connection = ref m_Set.m_Topology.Connections[it];

                        if (!connection.Valid)
                            break;

                        if (connection.SourceOutputPort == port)
                        {
                            if (currIndex == index)
                                return connection.DestinationHandle;

                            currIndex++;
                        }

                        it = connection.NextOutputConnection;
                    }

                    throw new IndexOutOfRangeException("Index of connection or port does not exist");
                }
            }

            public NodeEnumerator<OutputPortID, OutputTopologyEnumerator> this[OutputPortID port]
            {
                get
                {
                    if (port.Port >= Count)
                        throw new IndexOutOfRangeException();

                    return new NodeEnumerator<OutputPortID, OutputTopologyEnumerator>(this, port);
                }
            }

            public int Count => (int)m_TopologyIndex.OutputPortCount;

            public int Connections(OutputPortID port)
            {
                var indexCount = 0;
                var it = m_TopologyIndex.OutputHeadConnection;
                while (true)
                {
                    ref var connection = ref m_Set.m_Topology.Connections[it];

                    if (!connection.Valid)
                        break;

                    if (connection.SourceOutputPort == port)
                        indexCount++;

                    it = connection.NextOutputConnection;
                }

                return indexCount;
            }

            public OutputPortEnumerator GetEnumerator()
            {
                return new OutputPortEnumerator(this);
            }

            IEnumerator<NodeEnumerator<OutputPortID, OutputTopologyEnumerator>> IEnumerable<NodeEnumerator<OutputPortID, OutputTopologyEnumerator>>.GetEnumerator()
            {
                return GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }

        internal InputTopologyEnumerator GetInputs(NodeHandle handle)
        {
            NodeVersionCheck(handle.VHandle);

            // TODO: Here we leak internal relocatable members out into the wild. Should probably either 
            // protect the walker through topology version or make it use handles instead.
            return new InputTopologyEnumerator(this, ref m_Topology.Indexes[handle.VHandle.Index]);
        }

        internal OutputTopologyEnumerator GetOutputs(NodeHandle handle)
        {
            NodeVersionCheck(handle.VHandle);

            // TODO: Here we leak internal relocatable members out into the wild. Should probably either 
            // protect the walker through topology version or make it use handles instead.
            return new OutputTopologyEnumerator(this, ref m_Topology.Indexes[handle.VHandle.Index]);
        }
    }

}
