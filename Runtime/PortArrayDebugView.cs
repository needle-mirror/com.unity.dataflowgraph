using Unity.Collections.LowLevel.Unsafe;

namespace Unity.DataFlowGraph
{
    internal sealed class PortArrayDebugView<TPort>
        where TPort : struct, IIndexablePort
    {
        PortArray<TPort> m_PortArray;

        public PortArrayDebugView(PortArray<TPort> array)
        {
            m_PortArray = array;
        }

        public unsafe TPort[] Items
        {
            get
            {
                TPort[] ret = new TPort[m_PortArray.Size];

                if (m_PortArray.Ptr != null)
                {
                    if (typeof(TPort).GetGenericTypeDefinition() == typeof(DataInput<,>))
                    {
                        var untypedPortArray = *(UntypedDataInputPortArray*)UnsafeUtility.AddressOf(ref m_PortArray);
                        for (ushort i = 0; i < m_PortArray.Size; ++i)
                            ret[i] = UnsafeUtility.AsRef<TPort>(untypedPortArray.NthInputStorage(i));
                    }
                    else
                    {
#if DFG_ASSERTIONS
                        if (typeof(TPort).GetGenericTypeDefinition() != typeof(DataOutput<,>))
                            throw new AssertionException("Unexpected non-null PortArray type.");
#endif
                        var elementType = new SimpleType(typeof(TPort).GetGenericArguments()[1]).GetMinAlignedTyped(DataInputStorage.MinimumInputAlignment);
                        var untypedPortArray = *(UntypedDataOutputPortArray*)UnsafeUtility.AddressOf(ref m_PortArray);
                        for (ushort i = 0; i < m_PortArray.Size; ++i)
                            UnsafeUtility.MemCpy(UnsafeUtility.AddressOf(ref ret[i]), untypedPortArray.Get(elementType, i), elementType.Size);
                    }
                }

                return ret;
            }
        }
    }

    public readonly partial struct RenderContext
    {
        internal sealed class ResolvedInputPortArrayDebugView<TDefinition, TType>
            where TType : struct
            where TDefinition : NodeDefinition
        {
            ResolvedInputPortArray<TDefinition, TType> m_Array;

            public ResolvedInputPortArrayDebugView(ResolvedInputPortArray<TDefinition, TType> array)
            {
                m_Array = array;
            }

            public TType[] Items
            {
                get
                {
                    var ret = new TType[m_Array.Length];
                    for (int i = 0; i < m_Array.Length; ++i)
                        ret[i] = m_Array[i];
                    return ret;
                }
            }
        }
    }
}
