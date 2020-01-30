using System;
namespace Unity.DataFlowGraph
{
    static partial class TopologyAPI<TVertex, TInputPort, TOutputPort>
        where TVertex : unmanaged, IEquatable<TVertex>
        where TInputPort : unmanaged, IEquatable<TInputPort>
        where TOutputPort : unmanaged, IEquatable<TOutputPort>
    {
        public struct InputVertexCacheWalker
        {
            TraversalCache m_Cache;

            int m_SlotIndex;

            int m_CurrentIndex;

            uint m_Mask;

            TInputPort m_Port;

            bool m_TraverseAllPorts;

            internal InputVertexCacheWalker(TraversalCache cache, int slotIndex, TraversalCache.Hierarchy hierarchy)
                : this(cache, slotIndex, default, hierarchy, true)
            {
            }

            internal InputVertexCacheWalker(TraversalCache cache, int slotIndex, TInputPort port, TraversalCache.Hierarchy hierarchy)
                : this(cache, slotIndex, port, hierarchy, false)
            {
            }

            InputVertexCacheWalker(TraversalCache cache, int slotIndex, TInputPort port, TraversalCache.Hierarchy hierarchy, bool traverseAllPorts)
            {
                m_Cache = cache;

                m_SlotIndex = slotIndex;
                m_Port = port;
                m_CurrentIndex = -1;
                m_TraverseAllPorts = traverseAllPorts;
                m_Mask = cache.GetMask(hierarchy);

                var slot = m_Cache.OrderedTraversal[slotIndex];
                var count = 0;
                var parentTableIndex = slot.ParentTableIndex;
                for (var i = 0; i < slot.ParentCount; i++)
                {
                    var cacheConn = m_Cache.ParentTable[parentTableIndex + i];
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
                    var slot = m_Cache.OrderedTraversal[m_SlotIndex];

                    var entryIndex = 0;
                    var index = 0;
                    var parentTableIndex = slot.ParentTableIndex;
                    for (var i = 0; i < slot.ParentCount; i++)
                    {
                        var cacheConn = m_Cache.ParentTable[parentTableIndex + i];
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

                    return new VertexCache(m_Cache, entryIndex);
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
            TraversalCache m_Cache;

            int m_SlotIndex;

            int m_CurrentIndex;

            uint m_Mask;

            TOutputPort m_Port;

            bool m_TraverseAllPorts;

            internal OutputVertexCacheWalker(TraversalCache cache, int slotIndex, TraversalCache.Hierarchy hierarchy)
                : this(cache, slotIndex, default, hierarchy, true)
            {
            }

            internal OutputVertexCacheWalker(TraversalCache cache, int slotIndex, TOutputPort port, TraversalCache.Hierarchy hierarchy)
                : this(cache, slotIndex, port, hierarchy, false)
            {
            }

            OutputVertexCacheWalker(TraversalCache cache, int slotIndex, TOutputPort port, TraversalCache.Hierarchy hierarchy, bool traverseAllPorts)
            {
                m_Cache = cache;

                m_SlotIndex = slotIndex;
                m_Port = port;
                m_CurrentIndex = -1;

                var slot = m_Cache.OrderedTraversal[slotIndex];
                m_TraverseAllPorts = traverseAllPorts;
                m_Mask = cache.GetMask(hierarchy);
                var count = 0;
                var childTableIndex = slot.ChildTableIndex;
                for (var i = 0; i < slot.ChildCount; i++)
                {
                    var cacheConn = m_Cache.ChildTable[childTableIndex + i];
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
                    var slot = m_Cache.OrderedTraversal[m_SlotIndex];

                    var entryIndex = 0;
                    var index = 0;
                    var childTableIndex = slot.ChildTableIndex;
                    for (var i = 0; i < slot.ChildCount; i++)
                    {
                        var cacheConn = m_Cache.ChildTable[childTableIndex + i];
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

                    return new VertexCache(m_Cache, entryIndex);
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
            TraversalCache m_Cache;

            int m_SlotIndex;

            int m_CurrentIndex;

            uint m_Mask;

            TInputPort m_Port;

            bool m_TraverseAllPorts;

            internal InputConnectionCacheWalker(TraversalCache cache, int slotIndex, TraversalCache.Hierarchy hierarchy)
               : this(cache, slotIndex, default, hierarchy, true)
            {
            }

            internal InputConnectionCacheWalker(TraversalCache cache, int slotIndex, TInputPort port, TraversalCache.Hierarchy hierarchy)
                : this(cache, slotIndex, port, hierarchy, false)
            {
            }

            InputConnectionCacheWalker(TraversalCache cache, int slotIndex, TInputPort port, TraversalCache.Hierarchy hierarchy, bool traverseAllPorts)
            {
                m_Cache = cache;

                m_SlotIndex = slotIndex;
                m_Port = port;
                m_CurrentIndex = -1;
                m_TraverseAllPorts = traverseAllPorts;
                m_Mask = cache.GetMask(hierarchy);

                var slot = m_Cache.OrderedTraversal[slotIndex];
                var count = 0;
                var parentTableIndex = slot.ParentTableIndex;
                for (var i = 0; i < slot.ParentCount; i++)
                {
                    var cacheConn = m_Cache.ParentTable[parentTableIndex + i];
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
                    var slot = m_Cache.OrderedTraversal[m_SlotIndex];

                    TraversalCache.Connection connection = new TraversalCache.Connection();
                    var index = 0;
                    var parentTableIndex = slot.ParentTableIndex;
                    for (var i = 0; i < slot.ParentCount; i++)
                    {
                        var cacheConn = m_Cache.ParentTable[parentTableIndex + i];
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

                    return new ConnectionCache(m_Cache, connection);
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
            TraversalCache m_Cache;

            int m_SlotIndex;

            int m_CurrentIndex;

            uint m_Mask;

            TOutputPort m_Port;

            bool m_TraverseAllPorts;

            internal OutputConnectionCacheWalker(TraversalCache cache, int slotIndex, TraversalCache.Hierarchy hierarchy)
                : this(cache, slotIndex, default, hierarchy, true)
            {
            }

            internal OutputConnectionCacheWalker(TraversalCache cache, int slotIndex, TOutputPort port, TraversalCache.Hierarchy hierarchy)
                : this(cache, slotIndex, port, hierarchy, false)
            {
            }

            OutputConnectionCacheWalker(TraversalCache cache, int slotIndex, TOutputPort port, TraversalCache.Hierarchy hierarchy, bool traverseAllPorts)
            {
                m_Cache = cache;

                m_SlotIndex = slotIndex;
                m_Port = port;
                m_CurrentIndex = -1;
                m_TraverseAllPorts = traverseAllPorts;
                m_Mask = cache.GetMask(hierarchy);

                var slot = m_Cache.OrderedTraversal[slotIndex];
                var count = 0;
                var childTableIndex = slot.ChildTableIndex;
                for (var i = 0; i < slot.ChildCount; i++)
                {
                    var cacheConn = m_Cache.ChildTable[childTableIndex + i];
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
                    var slot = m_Cache.OrderedTraversal[m_SlotIndex];

                    TraversalCache.Connection connection = new TraversalCache.Connection();
                    var index = 0;
                    var childTableIndex = slot.ChildTableIndex;
                    for (var i = 0; i < slot.ChildCount; i++)
                    {
                        var cacheConn = m_Cache.ChildTable[childTableIndex + i];
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

                    return new ConnectionCache(m_Cache, connection);
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
            public VertexCache Target => new VertexCache(m_Cache, m_Connection.TraversalIndex);

            /// <summary>
            /// The port number on the target that this connection ends at.
            /// </summary>
            public TInputPort InputPort => m_Connection.InputPort;

            /// <summary>
            /// The port number from the originally walked vertex that this connection started at.
            /// </summary>
            public TOutputPort OutputPort => m_Connection.OutputPort;

            internal ConnectionCache(TraversalCache cache, TraversalCache.Connection connection)
            {
                m_Cache = cache;
                m_Connection = connection;
            }

            TraversalCache m_Cache;
            TraversalCache.Connection m_Connection;
        }

        public struct VertexCache
        {
            public TVertex Vertex => m_Cache.OrderedTraversal[m_SlotIndex].Vertex;

            internal int CacheIndex => m_SlotIndex;

            TraversalCache m_Cache;
            int m_SlotIndex;

            internal VertexCache(TraversalCache cache, int slotIndex)
            {
                this.m_SlotIndex = slotIndex;
                m_Cache = cache;
            }

            public InputVertexCacheWalker GetParents(TraversalCache.Hierarchy hierarchy = TraversalCache.Hierarchy.Traversal)
                => new InputVertexCacheWalker(m_Cache, m_SlotIndex, hierarchy);

            public InputVertexCacheWalker GetParentsByPort(TInputPort port, TraversalCache.Hierarchy hierarchy = TraversalCache.Hierarchy.Traversal)
                => new InputVertexCacheWalker(m_Cache, m_SlotIndex, port, hierarchy);

            public OutputVertexCacheWalker GetChildren(TraversalCache.Hierarchy hierarchy = TraversalCache.Hierarchy.Traversal)
                => new OutputVertexCacheWalker(m_Cache, m_SlotIndex, hierarchy);

            public OutputVertexCacheWalker GetChildrenByPort(TOutputPort port, TraversalCache.Hierarchy hierarchy = TraversalCache.Hierarchy.Traversal)
                => new OutputVertexCacheWalker(m_Cache, m_SlotIndex, port, hierarchy);

            public InputConnectionCacheWalker GetParentConnections(TraversalCache.Hierarchy hierarchy = TraversalCache.Hierarchy.Traversal)
                => new InputConnectionCacheWalker(m_Cache, m_SlotIndex, hierarchy);

            public InputConnectionCacheWalker GetParentConnectionsByPort(TInputPort port, TraversalCache.Hierarchy hierarchy = TraversalCache.Hierarchy.Traversal)
                => new InputConnectionCacheWalker(m_Cache, m_SlotIndex, port, hierarchy);

            public OutputConnectionCacheWalker GetChildConnections(TraversalCache.Hierarchy hierarchy = TraversalCache.Hierarchy.Traversal)
                => new OutputConnectionCacheWalker(m_Cache, m_SlotIndex, hierarchy);

            public OutputConnectionCacheWalker GetChildConnectionsByPort(TOutputPort port, TraversalCache.Hierarchy hierarchy = TraversalCache.Hierarchy.Traversal)
                => new OutputConnectionCacheWalker(m_Cache, m_SlotIndex, port, hierarchy);
        }

        public struct CacheWalker
        {
            internal CacheWalker(TraversalCache cache)
            {
                m_Cache = cache;
                m_Island = new TraversalCache.Island
                {
                    Count = cache.OrderedTraversal.Length,
                    TraversalIndexOffset = 0
                };
                m_SlotIndex = 0;
            }

            internal CacheWalker(TraversalCache cache, TraversalCache.Island island)
            {
                m_Cache = cache;
                m_Island = island;
                m_SlotIndex = 0;
                Reset();
            }

            TraversalCache m_Cache;
            TraversalCache.Island m_Island;
            int m_SlotIndex;

            public VertexCache Current => new VertexCache(m_Cache, m_SlotIndex - 1);

            public bool MoveNext()
            {
                m_SlotIndex++;
                return m_SlotIndex - 1 < (m_Island.TraversalIndexOffset + m_Island.Count);
            }

            public void Reset()
            {
                m_SlotIndex = m_Island.TraversalIndexOffset;
            }

            public int Count
            {
                get { return m_Island.Count; }
            }

            public CacheWalker GetEnumerator()
            {
                return this;
            }
        }

        public struct RootCacheWalker
        {
            internal RootCacheWalker(TraversalCache cache)
            {
                m_Cache = cache;
                m_RemappedSlot = 0;
                m_Index = 0;
            }

            TraversalCache m_Cache;
            int m_RemappedSlot;
            int m_Index;

            public VertexCache Current => new VertexCache(m_Cache, m_RemappedSlot);

            public bool MoveNext()
            {
                m_Index++;
                if (m_Index - 1 < Count)
                {
                    m_RemappedSlot = m_Cache.Roots[m_Index - 1];
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
                get { return m_Cache.Roots.Length; }
            }

            public RootCacheWalker GetEnumerator()
            {
                return this;
            }
        }

        public struct LeafCacheWalker
        {
            internal LeafCacheWalker(TraversalCache cache)
            {
                m_Cache = cache;
                m_RemappedSlot = 0;
                m_Index = 0;
            }

            TraversalCache m_Cache;
            int m_RemappedSlot;
            int m_Index;

            public VertexCache Current => new VertexCache(m_Cache, m_RemappedSlot);

            public bool MoveNext()
            {
                m_Index++;
                if (m_Index - 1 < Count)
                {
                    m_RemappedSlot = m_Cache.Leaves[m_Index - 1];
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
                get { return m_Cache.Leaves.Length; }
            }

            public LeafCacheWalker GetEnumerator()
            {
                return this;
            }
        }
    }
}
