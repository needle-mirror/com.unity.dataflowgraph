using System;

namespace Unity.DataFlowGraph.Tests
{
    static partial class TopologyTools<TVertex, TInputPort, TOutputPort>
        where TVertex : unmanaged, IEquatable<TVertex>
        where TInputPort : unmanaged, IEquatable<TInputPort>
        where TOutputPort : unmanaged, IEquatable<TOutputPort>
    {    /// <summary>
         /// Slow
         /// </summary>
        internal struct CacheWalker
        {

            internal CacheWalker(TopologyAPI<TVertex, TInputPort, TOutputPort>.TraversalCache cache)
            {
                m_Cache = cache;
                m_GroupIndex = 0;
                m_SlotIndex = 0;
            }

            TopologyAPI<TVertex, TInputPort, TOutputPort>.TraversalCache m_Cache;
            int m_SlotIndex;
            int m_GroupIndex;


            public TopologyAPI<TVertex, TInputPort, TOutputPort>.VertexCache Current
            {
                get
                {
#if DFG_ASSERTIONS
                    if (m_Cache.Groups[m_GroupIndex].TraversalCount < 1)
                        throw new AssertionException("CacheWalker enumerating empty cache, see MoveNext");
#endif
                    return new TopologyAPI<TVertex, TInputPort, TOutputPort>.VertexCache(m_Cache.Groups[m_GroupIndex], m_SlotIndex - 1);
                }
            }

            public bool MoveNext()
            {
                if (m_GroupIndex >= m_Cache.Groups.Length)
                    return false;

                m_SlotIndex++;

                while(true)
                {
                    if (m_GroupIndex >= m_Cache.Groups.Length)
                        return false;

                    if (m_SlotIndex > m_Cache.Groups[m_GroupIndex].TraversalCount)
                    {
                        m_GroupIndex++;
                        m_SlotIndex = 1;

                        continue;
                    }

                    return true;
                } 
            }

            public void Reset()
            {
                m_GroupIndex = 0;
                m_SlotIndex = 0;
            }

            public CacheWalker GetEnumerator()
            {
                return this;
            }
        }
    }
}
