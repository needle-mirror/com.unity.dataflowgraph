namespace Unity.DataFlowGraph
{
    struct InputNodeCacheWalker
    {
        TraversalCache m_Cache;

        int m_SlotIndex;

        InputPortArrayID m_Port;

        int m_CurrentIndex;

        internal InputNodeCacheWalker(TraversalCache cache, int slotIndex, InputPortArrayID port)
        {
            m_Cache = cache;

            m_SlotIndex = slotIndex;
            m_Port = port;
            m_CurrentIndex = -1;

            var slot = m_Cache.OrderedTraversal[slotIndex];
            if (port.PortID != InputPortID.Invalid)
            {
                var count = 0;
                var parentTableIndex = slot.ParentTableIndex;
                for (var i = 0; i < slot.ParentCount; i++)
                {
                    if (m_Cache.ParentTable[parentTableIndex + i].InputPort == port)
                        count++;
                }

                Count = count;
            }
            else
                Count = slot.ParentCount;
        }

        public NodeCache Current
        {
            get
            {
                var slot = m_Cache.OrderedTraversal[m_SlotIndex];

                var entryIndex = 0;
                if (m_Port.PortID == InputPortID.Invalid)
                {
                    entryIndex = m_Cache.ParentTable[slot.ParentTableIndex + m_CurrentIndex].TraversalIndex;
                }
                else
                {
                    var index = 0;
                    var parentTableIndex = slot.ParentTableIndex;
                    for (var i = 0; i < slot.ParentCount; i++)
                    {
                        var cacheConn = m_Cache.ParentTable[parentTableIndex + i];
                        if (cacheConn.InputPort == m_Port)
                        {
                            if (index++ == m_CurrentIndex)
                            {
                                entryIndex = cacheConn.TraversalIndex;
                                break;
                            }
                        }
                    }
                }

                return new NodeCache(m_Cache, entryIndex);
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

        public InputNodeCacheWalker GetEnumerator()
        {
            return this;
        }
    }

    struct OutputNodeCacheWalker
    {
        TraversalCache m_Cache;

        int m_SlotIndex;

        OutputPortID m_Port;

        int m_CurrentIndex;

        internal OutputNodeCacheWalker(TraversalCache cache, int slotIndex, OutputPortID port)
        {
            m_Cache = cache;

            m_SlotIndex = slotIndex;
            m_Port = port;
            m_CurrentIndex = -1;

            var slot = m_Cache.OrderedTraversal[slotIndex];
            if (port != OutputPortID.Invalid)
            {
                var count = 0;
                var childTableIndex = slot.ChildTableIndex;
                for (var i = 0; i < slot.ChildCount; i++)
                {
                    if (m_Cache.ChildTable[childTableIndex + i].OutputPort == port)
                        count++;
                }

                Count = count;
            }
            else
                Count = slot.ChildCount;
        }

        public NodeCache Current
        {
            get
            {
                var slot = m_Cache.OrderedTraversal[m_SlotIndex];

                var entryIndex = 0;
                if (m_Port == OutputPortID.Invalid)
                {
                    entryIndex = m_Cache.ChildTable[slot.ChildTableIndex + m_CurrentIndex].TraversalIndex;
                }
                else
                {
                    var index = 0;
                    var childTableIndex = slot.ChildTableIndex;
                    for (var i = 0; i < slot.ChildCount; i++)
                    {
                        var cacheConn = m_Cache.ChildTable[childTableIndex + i];
                        if (cacheConn.OutputPort == m_Port)
                        {
                            if (index++ == m_CurrentIndex)
                            {
                                entryIndex = cacheConn.TraversalIndex;
                                break;
                            }
                        }
                    }
                }

                return new NodeCache(m_Cache, entryIndex);
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

        public OutputNodeCacheWalker GetEnumerator()
        {
            return this;
        }
    }

    struct InputConnectionCacheWalker
    {
        TraversalCache m_Cache;

        int m_SlotIndex;

        InputPortArrayID m_Port;

        int m_CurrentIndex;

        internal InputConnectionCacheWalker(TraversalCache cache, int slotIndex, InputPortArrayID port)
        {
            m_Cache = cache;

            m_SlotIndex = slotIndex;
            m_Port = port;
            m_CurrentIndex = -1;

            var slot = m_Cache.OrderedTraversal[slotIndex];
            if (port.PortID != InputPortID.Invalid)
            {
                var count = 0;
                var parentTableIndex = slot.ParentTableIndex;
                for (var i = 0; i < slot.ParentCount; i++)
                {
                    if (m_Cache.ParentTable[parentTableIndex + i].InputPort == port)
                        count++;
                }

                Count = count;
            }
            else
                Count = slot.ParentCount;
        }

        public ConnectionCache Current
        {
            get
            {
                var slot = m_Cache.OrderedTraversal[m_SlotIndex];

                TraversalCache.Connection connection = new TraversalCache.Connection();
                if (m_Port.PortID == InputPortID.Invalid)
                {
                    connection = m_Cache.ParentTable[slot.ParentTableIndex + m_CurrentIndex];
                }
                else
                {
                    var index = 0;
                    var parentTableIndex = slot.ParentTableIndex;
                    for (var i = 0; i < slot.ParentCount; i++)
                    {
                        var cacheConn = m_Cache.ParentTable[parentTableIndex + i];
                        if (cacheConn.InputPort == m_Port)
                        {
                            if (index++ == m_CurrentIndex)
                            {
                                connection = cacheConn;
                                break;
                            }
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

    struct OutputConnectionCacheWalker
    {
        TraversalCache m_Cache;

        int m_SlotIndex;

        OutputPortID m_Port;

        int m_CurrentIndex;

        internal OutputConnectionCacheWalker(TraversalCache cache, int slotIndex, OutputPortID port)
        {
            m_Cache = cache;

            m_SlotIndex = slotIndex;
            m_Port = port;
            m_CurrentIndex = -1;

            var slot = m_Cache.OrderedTraversal[slotIndex];
            if (port != OutputPortID.Invalid)
            {
                var count = 0;
                var childTableIndex = slot.ChildTableIndex;
                for (var i = 0; i < slot.ChildCount; i++)
                {
                    if (m_Cache.ChildTable[childTableIndex + i].OutputPort == port)
                        count++;
                }

                Count = count;
            }
            else
                Count = slot.ChildCount;
        }

        public ConnectionCache Current
        {
            get
            {
                var slot = m_Cache.OrderedTraversal[m_SlotIndex];

                TraversalCache.Connection connection = new TraversalCache.Connection();
                if (m_Port == OutputPortID.Invalid)
                {
                    connection = m_Cache.ChildTable[slot.ChildTableIndex + m_CurrentIndex];
                }
                else
                {
                    var index = 0;
                    var childTableIndex = slot.ChildTableIndex;
                    for (var i = 0; i < slot.ChildCount; i++)
                    {
                        var cacheConn = m_Cache.ChildTable[childTableIndex + i];
                        if (cacheConn.OutputPort == m_Port)
                        {
                            if (index++ == m_CurrentIndex)
                            {
                                connection = cacheConn;
                                break;
                            }
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

    struct ConnectionCache
    {
        public NodeCache TargetNode => new NodeCache(m_Cache, m_Connection.TraversalIndex);

        /// <summary>
        /// The port number on the target node that this connection ends at.
        /// </summary>
        public InputPortID InputPort => m_Connection.InputPort.PortID;

        /// <summary>
        /// The port number from the originally walked node that this connection started at.
        /// </summary>
        public OutputPortID OutputPort => m_Connection.OutputPort;

        internal ConnectionCache(TraversalCache cache, TraversalCache.Connection connection)
        {
            m_Cache = cache;
            m_Connection = connection;
        }

        TraversalCache m_Cache;
        TraversalCache.Connection m_Connection;
    }

    struct NodeCache
    {
        public NodeHandle Handle => m_Cache.OrderedTraversal[m_SlotIndex].Handle;

        internal int CacheIndex => m_SlotIndex;

        internal int UnorderedIndex => m_Cache.OrderedTraversal[m_SlotIndex].UnorderedIndex;

        int m_SlotIndex;
        TraversalCache m_Cache;

        internal NodeCache(TraversalCache cache, int slotIndex)
        {
            this.m_SlotIndex = slotIndex;
            m_Cache = cache;
        }

        public InputNodeCacheWalker GetParents() => new InputNodeCacheWalker(m_Cache, m_SlotIndex, new InputPortArrayID(InputPortID.Invalid));

        public InputNodeCacheWalker GetParentsByPort(InputPortID port) => new InputNodeCacheWalker(m_Cache, m_SlotIndex, new InputPortArrayID(port));

        public OutputNodeCacheWalker GetChildren() => new OutputNodeCacheWalker(m_Cache, m_SlotIndex, OutputPortID.Invalid);

        public OutputNodeCacheWalker GetChildrenByPort(OutputPortID port) => new OutputNodeCacheWalker(m_Cache, m_SlotIndex, port);

        public InputConnectionCacheWalker GetParentConnections() => new InputConnectionCacheWalker(m_Cache, m_SlotIndex, new InputPortArrayID(InputPortID.Invalid));

        public InputConnectionCacheWalker GetParentConnectionsByPort(InputPortID port) => new InputConnectionCacheWalker(m_Cache, m_SlotIndex, new InputPortArrayID(port));
        public InputConnectionCacheWalker GetParentConnectionsByPort(InputPortID port, ushort index) => new InputConnectionCacheWalker(m_Cache, m_SlotIndex, new InputPortArrayID(port, index));

        public OutputConnectionCacheWalker GetChildConnections() => new OutputConnectionCacheWalker(m_Cache, m_SlotIndex, OutputPortID.Invalid);

        public OutputConnectionCacheWalker GetChildConnectionsByPort(OutputPortID port) => new OutputConnectionCacheWalker(m_Cache, m_SlotIndex, port);
    }

    struct TopologyCacheWalker
    {
        internal TopologyCacheWalker(TraversalCache cache)
        {
            m_Cache = cache;
            m_Island = new TraversalCache.Island
            {
                Count = cache.OrderedTraversal.Length,
                TraversalIndexOffset = 0
            };
            m_SlotIndex = 0;
        }

        internal TopologyCacheWalker(TraversalCache cache, TraversalCache.Island island)
        {
            m_Cache = cache;
            m_Island = island;
            m_SlotIndex = 0;
            Reset();
        }

        TraversalCache m_Cache;
        TraversalCache.Island m_Island;
        int m_SlotIndex;

        public NodeCache Current => new NodeCache(m_Cache, m_SlotIndex - 1);

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

        public TopologyCacheWalker GetEnumerator()
        {
            return this;
        }
    }

    struct RootCacheWalker
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

        public NodeCache Current => new NodeCache(m_Cache, m_RemappedSlot);

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

    struct LeafCacheWalker
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

        public NodeCache Current => new NodeCache(m_Cache, m_RemappedSlot);

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
