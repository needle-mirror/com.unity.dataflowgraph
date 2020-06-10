using System;
namespace Unity.DataFlowGraph
{
    // TODO: Remove all .Count

    static partial class TopologyAPI<TVertex, TInputPort, TOutputPort>
        where TVertex : unmanaged, IEquatable<TVertex>
        where TInputPort : unmanaged, IEquatable<TInputPort>
        where TOutputPort : unmanaged, IEquatable<TOutputPort>
    {
        public struct InputVertexCacheWalker
        {
            TraversalCache.Group m_Group;

            int m_SlotIndex;

            int m_CurrentIndex;

            uint m_Mask;

            TInputPort m_Port;

            bool m_TraverseAllPorts;

            internal InputVertexCacheWalker(TraversalCache.Group group, int slotIndex, TraversalCache.Hierarchy hierarchy)
                : this(group, slotIndex, default, hierarchy, true)
            {
            }

            internal InputVertexCacheWalker(TraversalCache.Group group, int slotIndex, TInputPort port, TraversalCache.Hierarchy hierarchy)
                : this(group, slotIndex, port, hierarchy, false)
            {
            }

            InputVertexCacheWalker(TraversalCache.Group group, int slotIndex, TInputPort port, TraversalCache.Hierarchy hierarchy, bool traverseAllPorts)
            {
                m_Group = group;

                m_SlotIndex = slotIndex;
                m_Port = port;
                m_CurrentIndex = -1;
                m_TraverseAllPorts = traverseAllPorts;
                m_Mask = group.GetMask(hierarchy);

                var slot = m_Group.IndexTraversal(slotIndex);
                var count = 0;
                var parentTableIndex = slot.ParentTableIndex;
                for (var i = 0; i < slot.ParentCount; i++)
                {
                    var cacheConn = m_Group.IndexParent(parentTableIndex + i);
                    if ((m_TraverseAllPorts || cacheConn.InputPort.Equals(m_Port)) &&
                        (cacheConn.TraversalFlags & m_Mask) != 0)
                        count++;
                }

                Count = count;
            }

            public VertexCache Current
            {
                get
                {
                    var slot = m_Group.IndexTraversal(m_SlotIndex);

                    var entryIndex = 0;
                    var index = 0;
                    var parentTableIndex = slot.ParentTableIndex;
                    for (var i = 0; i < slot.ParentCount; i++)
                    {
                        var cacheConn = m_Group.IndexParent(parentTableIndex + i);
                        if ((m_TraverseAllPorts || cacheConn.InputPort.Equals(m_Port)) &&
                            (cacheConn.TraversalFlags & m_Mask) != 0)
                        {
                            if (index++ == m_CurrentIndex)
                            {
                                entryIndex = cacheConn.TraversalIndex;
                                break;
                            }
                        }
                    }

                    return new VertexCache(m_Group, entryIndex);
                }
            }

            public bool MoveNext()
            {
                m_CurrentIndex++;
                return m_CurrentIndex < Count;
            }

            public void Reset()
            {
                m_CurrentIndex = -1;
            }

            public int Count { get; private set; }

            public InputVertexCacheWalker GetEnumerator()
            {
                return this;
            }
        }

        public struct OutputVertexCacheWalker
        {
            TraversalCache.Group m_Group;

            int m_SlotIndex;

            int m_CurrentIndex;

            uint m_Mask;

            TOutputPort m_Port;

            bool m_TraverseAllPorts;

            internal OutputVertexCacheWalker(TraversalCache.Group group, int slotIndex, TraversalCache.Hierarchy hierarchy)
                : this(group, slotIndex, default, hierarchy, true)
            {
            }

            internal OutputVertexCacheWalker(TraversalCache.Group group, int slotIndex, TOutputPort port, TraversalCache.Hierarchy hierarchy)
                : this(group, slotIndex, port, hierarchy, false)
            {
            }

            OutputVertexCacheWalker(TraversalCache.Group group, int slotIndex, TOutputPort port, TraversalCache.Hierarchy hierarchy, bool traverseAllPorts)
            {
                m_Group = group;

                m_SlotIndex = slotIndex;
                m_Port = port;
                m_CurrentIndex = -1;

                var slot = m_Group.IndexTraversal(slotIndex);
                m_TraverseAllPorts = traverseAllPorts;
                m_Mask = group.GetMask(hierarchy);
                var count = 0;
                var childTableIndex = slot.ChildTableIndex;
                for (var i = 0; i < slot.ChildCount; i++)
                {
                    var cacheConn = m_Group.IndexChild(childTableIndex + i);
                    if ((m_TraverseAllPorts || cacheConn.OutputPort.Equals(m_Port)) &&
                        (cacheConn.TraversalFlags & m_Mask) != 0)
                        count++;
                }

                Count = count;
            }

            public VertexCache Current
            {
                get
                {
                    var slot = m_Group.IndexTraversal(m_SlotIndex);

                    var entryIndex = 0;
                    var index = 0;
                    var childTableIndex = slot.ChildTableIndex;
                    for (var i = 0; i < slot.ChildCount; i++)
                    {
                        var cacheConn = m_Group.IndexChild(childTableIndex + i);
                        if ((m_TraverseAllPorts || cacheConn.OutputPort.Equals(m_Port)) &&
                            (cacheConn.TraversalFlags & m_Mask) != 0)
                        {
                            if (index++ == m_CurrentIndex)
                            {
                                entryIndex = cacheConn.TraversalIndex;
                                break;
                            }
                        }
                    }

                    return new VertexCache(m_Group, entryIndex);
                }
            }

            public bool MoveNext()
            {
                m_CurrentIndex++;
                return m_CurrentIndex < Count;
            }

            public void Reset()
            {
                m_CurrentIndex = -1;
            }

            public int Count { get; private set; }

            public OutputVertexCacheWalker GetEnumerator()
            {
                return this;
            }
        }

        public struct InputConnectionCacheWalker
        {
            TraversalCache.Group m_Group;

            int m_SlotIndex;

            int m_CurrentIndex;

            uint m_Mask;

            TInputPort m_Port;

            bool m_TraverseAllPorts;

            internal InputConnectionCacheWalker(TraversalCache.Group group, int slotIndex, TraversalCache.Hierarchy hierarchy)
               : this(group, slotIndex, default, hierarchy, true)
            {
            }

            internal InputConnectionCacheWalker(TraversalCache.Group group, int slotIndex, TInputPort port, TraversalCache.Hierarchy hierarchy)
                : this(group, slotIndex, port, hierarchy, false)
            {
            }

            InputConnectionCacheWalker(TraversalCache.Group group, int slotIndex, TInputPort port, TraversalCache.Hierarchy hierarchy, bool traverseAllPorts)
            {
                m_Group = group;

                m_SlotIndex = slotIndex;
                m_Port = port;
                m_CurrentIndex = -1;
                m_TraverseAllPorts = traverseAllPorts;
                m_Mask = group.GetMask(hierarchy);

                var slot = m_Group.IndexTraversal(slotIndex);
                var count = 0;
                var parentTableIndex = slot.ParentTableIndex;
                for (var i = 0; i < slot.ParentCount; i++)
                {
                    var cacheConn = m_Group.IndexParent(parentTableIndex + i);
                    if ((m_TraverseAllPorts || cacheConn.InputPort.Equals(m_Port)) &&
                        (cacheConn.TraversalFlags & m_Mask) != 0)
                        count++;
                }

                Count = count;
            }

            public ConnectionCache Current
            {
                get
                {
                    var slot = m_Group.IndexTraversal(m_SlotIndex);

                    TraversalCache.Connection connection = new TraversalCache.Connection();
                    var index = 0;
                    var parentTableIndex = slot.ParentTableIndex;
                    for (var i = 0; i < slot.ParentCount; i++)
                    {
                        var cacheConn = m_Group.IndexParent(parentTableIndex + i);
                        if ((m_TraverseAllPorts || cacheConn.InputPort.Equals(m_Port)) &&
                            (cacheConn.TraversalFlags & m_Mask) != 0)
                        {
                            if (index++ == m_CurrentIndex)
                            {
                                connection = cacheConn;
                                break;
                            }
                        }
                    }

                    return new ConnectionCache(m_Group, connection);
                }
            }

            public bool MoveNext()
            {
                m_CurrentIndex++;
                return m_CurrentIndex < Count;
            }

            public void Reset()
            {
                m_CurrentIndex = -1;
            }

            public int Count { get; private set; }

            public InputConnectionCacheWalker GetEnumerator()
            {
                return this;
            }
        }

        public struct OutputConnectionCacheWalker
        {
            TraversalCache.Group m_Group;

            int m_SlotIndex;

            int m_CurrentIndex;

            uint m_Mask;

            TOutputPort m_Port;

            bool m_TraverseAllPorts;

            internal OutputConnectionCacheWalker(TraversalCache.Group group, int slotIndex, TraversalCache.Hierarchy hierarchy)
                : this(group, slotIndex, default, hierarchy, true)
            {
            }

            internal OutputConnectionCacheWalker(TraversalCache.Group group, int slotIndex, TOutputPort port, TraversalCache.Hierarchy hierarchy)
                : this(group, slotIndex, port, hierarchy, false)
            {
            }

            OutputConnectionCacheWalker(TraversalCache.Group group, int slotIndex, TOutputPort port, TraversalCache.Hierarchy hierarchy, bool traverseAllPorts)
            {
                m_Group = group;

                m_SlotIndex = slotIndex;
                m_Port = port;
                m_CurrentIndex = -1;
                m_TraverseAllPorts = traverseAllPorts;
                m_Mask = group.GetMask(hierarchy);

                var slot = m_Group.IndexTraversal(slotIndex);
                var count = 0;
                var childTableIndex = slot.ChildTableIndex;
                for (var i = 0; i < slot.ChildCount; i++)
                {
                    var cacheConn = m_Group.IndexChild(childTableIndex + i);
                    if ((m_TraverseAllPorts || cacheConn.OutputPort.Equals(m_Port)) &&
                        (cacheConn.TraversalFlags & m_Mask) != 0)
                        count++;
                }

                Count = count;
            }

            public ConnectionCache Current
            {
                get
                {
                    var slot = m_Group.IndexTraversal(m_SlotIndex);

                    TraversalCache.Connection connection = new TraversalCache.Connection();
                    var index = 0;
                    var childTableIndex = slot.ChildTableIndex;
                    for (var i = 0; i < slot.ChildCount; i++)
                    {
                        var cacheConn = m_Group.IndexChild(childTableIndex + i);
                        if ((m_TraverseAllPorts || cacheConn.OutputPort.Equals(m_Port)) &&
                            (cacheConn.TraversalFlags & m_Mask) != 0)
                        {
                            if (index++ == m_CurrentIndex)
                            {
                                connection = cacheConn;
                                break;
                            }
                        }
                    }

                    return new ConnectionCache(m_Group, connection);
                }
            }

            public bool MoveNext()
            {
                m_CurrentIndex++;
                return m_CurrentIndex < Count;
            }

            public void Reset()
            {
                m_CurrentIndex = -1;
            }

            public int Count { get; private set; }

            public OutputConnectionCacheWalker GetEnumerator()
            {
                return this;
            }
        }

        public struct ConnectionCache
        {
            public VertexCache Target => new VertexCache(m_Group, m_Connection.TraversalIndex);

            /// <summary>
            /// The port number on the target that this connection ends at.
            /// </summary>
            public TInputPort InputPort => m_Connection.InputPort;

            /// <summary>
            /// The port number from the originally walked vertex that this connection started at.
            /// </summary>
            public TOutputPort OutputPort => m_Connection.OutputPort;

            internal ConnectionCache(TraversalCache.Group group, TraversalCache.Connection connection)
            {
                m_Group = group;
                m_Connection = connection;
            }

            TraversalCache.Group m_Group;
            TraversalCache.Connection m_Connection;
        }

        public struct VertexCache
        {
            public TVertex Vertex => m_Group.IndexTraversal(m_SlotIndex).Vertex;

            internal int CacheIndex => m_SlotIndex;

            TraversalCache.Group m_Group;
            int m_SlotIndex;

            internal VertexCache(TraversalCache.Group cache, int slotIndex)
            {
                m_SlotIndex = slotIndex;
                m_Group = cache;
            }

            public InputVertexCacheWalker GetParents(TraversalCache.Hierarchy hierarchy = TraversalCache.Hierarchy.Traversal)
                => new InputVertexCacheWalker(m_Group, m_SlotIndex, hierarchy);

            public InputVertexCacheWalker GetParentsByPort(TInputPort port, TraversalCache.Hierarchy hierarchy = TraversalCache.Hierarchy.Traversal)
                => new InputVertexCacheWalker(m_Group, m_SlotIndex, port, hierarchy);

            public OutputVertexCacheWalker GetChildren(TraversalCache.Hierarchy hierarchy = TraversalCache.Hierarchy.Traversal)
                => new OutputVertexCacheWalker(m_Group, m_SlotIndex, hierarchy);

            public OutputVertexCacheWalker GetChildrenByPort(TOutputPort port, TraversalCache.Hierarchy hierarchy = TraversalCache.Hierarchy.Traversal)
                => new OutputVertexCacheWalker(m_Group, m_SlotIndex, port, hierarchy);

            public InputConnectionCacheWalker GetParentConnections(TraversalCache.Hierarchy hierarchy = TraversalCache.Hierarchy.Traversal)
                => new InputConnectionCacheWalker(m_Group, m_SlotIndex, hierarchy);

            public InputConnectionCacheWalker GetParentConnectionsByPort(TInputPort port, TraversalCache.Hierarchy hierarchy = TraversalCache.Hierarchy.Traversal)
                => new InputConnectionCacheWalker(m_Group, m_SlotIndex, port, hierarchy);

            public OutputConnectionCacheWalker GetChildConnections(TraversalCache.Hierarchy hierarchy = TraversalCache.Hierarchy.Traversal)
                => new OutputConnectionCacheWalker(m_Group, m_SlotIndex, hierarchy);

            public OutputConnectionCacheWalker GetChildConnectionsByPort(TOutputPort port, TraversalCache.Hierarchy hierarchy = TraversalCache.Hierarchy.Traversal)
                => new OutputConnectionCacheWalker(m_Group, m_SlotIndex, port, hierarchy);
        }

        public struct GroupWalker
        {

            internal GroupWalker(TraversalCache.Group group)
            {
                m_Group = group;
                m_SlotIndex = 0;
                Reset();
            }

            TraversalCache.Group m_Group;
            int m_SlotIndex;

            public VertexCache Current => new VertexCache(m_Group, m_SlotIndex - 1);

            public bool MoveNext()
            {
                m_SlotIndex++;
                return m_SlotIndex - 1 < m_Group.TraversalCount;
            }

            public void Reset()
            {
                m_SlotIndex = 0;
            }

            public int Count
            {
                get { return m_Group.TraversalCount; }
            }

            public GroupWalker GetEnumerator()
            {
                return this;
            }
        }

        public struct RootCacheWalker
        {
            internal RootCacheWalker(TraversalCache.Group cache)
            {
                m_Group = cache;
                m_RemappedSlot = 0;
                m_Index = 0;
            }

            TraversalCache.Group m_Group;
            int m_RemappedSlot;
            int m_Index;

            public VertexCache Current => new VertexCache(m_Group, m_RemappedSlot);

            public bool MoveNext()
            {
                m_Index++;
                if (m_Index - 1 < Count)
                {
                    m_RemappedSlot = m_Group.IndexRoot(m_Index - 1);
                    return true;
                }

                return false;
            }

            public void Reset()
            {
                m_Index = 0;
            }

            public int Count
            {
                get { return m_Group.RootCount; }
            }

            public RootCacheWalker GetEnumerator()
            {
                return this;
            }
        }

        public struct LeafCacheWalker
        {
            internal LeafCacheWalker(TraversalCache.Group group)
            {
                m_Group = group;
                m_RemappedSlot = 0;
                m_Index = 0;
            }

            TraversalCache.Group m_Group;
            int m_RemappedSlot;
            int m_Index;

            public VertexCache Current => new VertexCache(m_Group, m_RemappedSlot);

            public bool MoveNext()
            {
                m_Index++;
                if (m_Index - 1 < Count)
                {
                    m_RemappedSlot = m_Group.IndexLeaf(m_Index - 1);
                    return true;
                }

                return false;
            }

            public void Reset()
            {
                m_Index = 0;
            }

            public int Count
            {
                get { return m_Group.LeafCount; }
            }

            public LeafCacheWalker GetEnumerator()
            {
                return this;
            }
        }
    }
}
