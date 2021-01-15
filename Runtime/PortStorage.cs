using System.Diagnostics;
using Unity.Entities;
using Unity.Mathematics;
#if ENABLE_IL2CPP
using UnityEngine.Scripting;
#endif

namespace Unity.DataFlowGraph
{
#pragma warning disable 660, 661  // We do not want Equals(object) nor GetHashCode() for all port primitives

    [DebuggerDisplay("{ToString(), nq}")]
    readonly struct PortStorage
    {
        public enum Category
        {
            Undefined,
            Message = 1 << 15,
            DSL     = 1 << 14,
            Data    = DSL | Message
        }

        public readonly struct EncodedDFGPort
        {
            readonly ushort m_Bits;

            public Category Category => (Category)(m_Bits & CategoryMask);
            public ushort CategoryCounter => (ushort)(m_Bits & DFGPortCounterMask);

            internal EncodedDFGPort(ushort bits) => m_Bits = bits;

            public override string ToString()
            {
                return $"{CategoryCounter} ({Category})";
            }

            public override int GetHashCode() => m_Bits;

            public static bool operator ==(EncodedDFGPort a, EncodedDFGPort b) => a.m_Bits == b.m_Bits;
            public static bool operator !=(EncodedDFGPort a, EncodedDFGPort b) => a.m_Bits == b.m_Bits;
        }

        internal const int IsECSPortFlag = TypeManager.ManagedComponentTypeFlag;
        internal const int IsDFGPortFlag = IsECSPortFlag << 1;
        public const int MaxDFGPortNumber = DFGPortCounterMask;

        const ushort CategoryMask = (int)Category.Message | (int)Category.DSL | (int)Category.Data;
        const ushort DFGPortCounterMask = unchecked((ushort)~CategoryMask);

        readonly int m_TypeOrPort;

        public PortStorage(ushort dfgPortIndex, Category portClass)
        {
            ushort classBits = (ushort)portClass;
#if DFG_ASSERTIONS
            if ((dfgPortIndex & CategoryMask) != 0)
                throw new AssertionException($"Port number invades class category bits or is over {MaxDFGPortNumber}");
#endif

            m_TypeOrPort = dfgPortIndex | IsDFGPortFlag | classBits;
        }

        public PortStorage(EncodedDFGPort port) : this(port.CategoryCounter, port.Category) { }

        public PortStorage(ComponentType ecsType)
        {
#if DFG_ASSERTIONS
            if((ecsType.TypeIndex & IsECSPortFlag) != 0)
                throw new AssertionException("Port storage being created with an incompatible ECS type (flag being reused)");
#endif
            m_TypeOrPort = ecsType.TypeIndex | IsECSPortFlag;
        }

        public EncodedDFGPort DFGPort
        {
            get {
#if DFG_ASSERTIONS
                if(!IsDFGPort)
                    throw new AssertionException("Retrieving DFG port from a storage containing an ECS type");
#endif
                // unsigned modulo 32 bits -> 8 bits chops off IsDFGPortFlag.
                return new EncodedDFGPort((ushort)m_TypeOrPort);
            }
        }

        public int ECSTypeIndex
        {
            get {
#if DFG_ASSERTIONS
                if(!IsECSPort)
                    throw new AssertionException("Retrieving ECS type from a storage containing an DFG type");
#endif
                return m_TypeOrPort & (~IsECSPortFlag);
            }
        }

        public ComponentType ReadOnlyComponentType =>
            new ComponentType { TypeIndex = ECSTypeIndex, AccessModeType = ComponentType.AccessMode.ReadOnly };

        public ComponentType ReadWriteComponentType =>
            new ComponentType { TypeIndex = ECSTypeIndex, AccessModeType = ComponentType.AccessMode.ReadWrite };

        public bool IsECSPort => (m_TypeOrPort & IsECSPortFlag) != 0;
        public bool IsDFGPort => (m_TypeOrPort & IsDFGPortFlag) != 0 && !IsECSPort;

        public static bool operator ==(PortStorage left, PortStorage right)
        {
            return left.m_TypeOrPort == right.m_TypeOrPort;
        }

        public static bool operator !=(PortStorage left, PortStorage right)
        {
            return left.m_TypeOrPort != right.m_TypeOrPort;
        }

        public override string ToString()
        {
            return IsECSPort ? $"ECS: {ReadOnlyComponentType}" : IsDFGPort ? $"DFG: {DFGPort}" : "<INVALID>";
        }
    }
#pragma warning restore 660, 661  // We do not want Equals(object) nor GetHashCode() for all port primitives

}
