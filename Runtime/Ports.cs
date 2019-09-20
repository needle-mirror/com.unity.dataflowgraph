using System;
using Unity.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;
using System.Diagnostics;
using UnityEngine.Scripting;

namespace Unity.DataFlowGraph
{
    /// <summary>
    /// Base interface for weakly typed port IDs. Namely <see cref="InputPortID"/> and <see cref="OutputPortID"/>.
    /// </summary>
    interface IPortID { }

    /// <summary>
    /// Weakly typed identifier for a given input port of a node.
    /// </summary>
#pragma warning disable 660, 661  // We do not want Equals(object) nor GetHashCode()
    public readonly struct InputPortID : IPortID
    {
        internal readonly ushort Port;

        public static bool operator ==(InputPortID left, InputPortID right)
        {
            return left.Port == right.Port;
        }

        public static bool operator !=(InputPortID left, InputPortID right)
        {
            return left.Port != right.Port;
        }

        internal InputPortID(ushort port)
        {
            Port = port;
        }

        internal static readonly InputPortID Invalid = new InputPortID(UInt16.MaxValue);
    }
#pragma warning restore 660, 661

#pragma warning disable 660, 661  // We do not want Equals(object) nor GetHashCode()
    readonly struct InputPortArrayID
    {
        public const UInt16 NonArraySentinel = UInt16.MaxValue;
        public readonly InputPortID PortID;
        readonly ushort m_ArrayIndex;

        public static bool operator ==(InputPortArrayID left, InputPortArrayID right)
        {
            return left.PortID == right.PortID && left.m_ArrayIndex == right.m_ArrayIndex;
        }

        public static bool operator !=(InputPortArrayID left, InputPortArrayID right)
        {
            return left.PortID != right.PortID || left.m_ArrayIndex != right.m_ArrayIndex;
        }

        internal InputPortArrayID(InputPortID portId, ushort arrayIndex)
        {
            if (arrayIndex == NonArraySentinel)
                throw new InvalidOperationException("Invalid array index.");
            PortID = portId;
            m_ArrayIndex = arrayIndex;
        }

        internal InputPortArrayID(InputPortID portId)
        {
            PortID = portId;
            m_ArrayIndex = NonArraySentinel;
        }

        internal ushort ArrayIndex => m_ArrayIndex;

        internal bool IsArray => m_ArrayIndex != NonArraySentinel;
    }
#pragma warning restore 660, 661

    /// <summary>
    /// Weakly typed identifier for a given output port of a node.
    /// </summary>
#pragma warning disable 660, 661  // We do not want Equals(object) nor GetHashCode()
    public struct OutputPortID : IPortID
    {
        internal ushort Port;

        public static bool operator ==(OutputPortID left, OutputPortID right)
        {
            return left.Port == right.Port;
        }

        public static bool operator !=(OutputPortID left, OutputPortID right)
        {
            return left.Port != right.Port;
        }

        internal static readonly OutputPortID Invalid = new OutputPortID {Port = UInt16.MaxValue};
    }
#pragma warning restore 660, 661

    /// <summary>
    /// Declaration of a specific message input connection port for a given node type.
    ///
    /// These are used as fields within an <see cref="ISimulationPortDefinition"/> struct implementation
    /// (see <see cref="NodeDefinition{TNodeData,TSimulationPortDefinition}.SimulationPorts"/>).
    /// </summary>
    /// <remarks>
    /// <see cref="NodeDefinition{TNodeData,TSimulationPortDefinition}"/>s which include this type of port must implement
    /// <see cref="IMsgHandler{TMsg}"/> of the corresponding type in order to handle incoming messages.
    /// </remarks>
    /// <typeparam name="TDefinition">
    /// The <see cref="NodeDefinition{TNodeData,TSimulationPortDefinition}"/> to which this port is associated.
    /// </typeparam>
#pragma warning disable 660, 661  // We do not want Equals(object) nor GetHashCode()
    public struct MessageInput<TDefinition, TMsg> : IIndexableInputPort
        where TDefinition : INodeDefinition, IMsgHandler<TMsg>
    {
        internal InputPortID Port;

        public static bool operator ==(InputPortID left, MessageInput<TDefinition, TMsg> right)
        {
            return left == right.Port;
        }

        public static bool operator !=(InputPortID left, MessageInput<TDefinition, TMsg> right)
        {
            return left != right.Port;
        }

        public static explicit operator InputPortID(MessageInput<TDefinition, TMsg> input)
        {
            return input.Port;
        }
    }
#pragma warning restore 660, 661

    /// <summary>
    /// Declaration of a specific message output connection port for a given node type.
    ///
    /// These are used as fields within an <see cref="ISimulationPortDefinition"/> struct implementation
    /// (see <see cref="NodeDefinition{TNodeData,TSimulationPortDefinition}.SimulationPorts"/>).
    /// </summary>
    /// <typeparam name="TDefinition">
    /// The <see cref="NodeDefinition{TNodeData,TSimulationPortDefinition}"/> to which this port is associated.
    /// </typeparam>
    public struct MessageOutput<TDefinition, TMsg>
        where TDefinition : INodeDefinition
    {
        internal OutputPortID Port;

        public static explicit operator OutputPortID(MessageOutput<TDefinition, TMsg> output)
        {
            return output.Port;
        }
    }

    /// <summary>
    /// Declaration of a specific DSL input connection port for a given node type.
    ///
    /// These are used as fields within an <see cref="ISimulationPortDefinition"/> struct implementation
    /// (see <see cref="NodeDefinition{TNodeData,TSimulationPortDefinition}.SimulationPorts"/>).
    /// </summary>
    /// <remarks>
    /// <see cref="NodeDefinition{TNodeData,TSimulationPortDefinition}"/>s which include this type of port must implement
    /// the provided <typeparamref name="IDSL"/> interface.
    /// </remarks>
    /// <typeparam name="TNodeDefinition">
    /// The <see cref="NodeDefinition{TNodeData,TSimulationPortDefinition}"/> to which this port is associated.
    /// </typeparam>
    public struct DSLInput<TNodeDefinition, TDSLDefinition, IDSL>
        where TNodeDefinition : INodeDefinition, IDSL
        where TDSLDefinition : DSLHandler<IDSL>, new()
        where IDSL : class
    {
        internal InputPortID Port;

#if ENABLE_IL2CPP
        [Preserve]
        IDSLHandler DontStripConstructor()
        {
            return new TDSLDefinition();
        }
#endif

        public static explicit operator InputPortID(DSLInput<TNodeDefinition, TDSLDefinition, IDSL> input)
        {
            return input.Port;
        }
    }

    /// <summary>
    /// Declaration of a specific DSL output connection port for a given node type.
    ///
    /// These are used as fields within an <see cref="ISimulationPortDefinition"/> struct implementation
    /// (see <see cref="NodeDefinition{TNodeData,TSimulationPortDefinition}.SimulationPorts"/>).
    /// </summary>
    /// <remarks>
    /// <see cref="NodeDefinition{TNodeData,TSimulationPortDefinition}"/>s which include this type of port must implement
    /// the provided IDSL interface.
    /// </remarks>
    /// <typeparam name="TNodeDefinition">
    /// The <see cref="NodeDefinition{TNodeData,TSimulationPortDefinition}"/> to which this port is associated.
    /// </typeparam>
    public struct DSLOutput<TNodeDefinition, TDSLDefinition, IDSL>
        where TNodeDefinition : INodeDefinition, IDSL
        where TDSLDefinition : DSLHandler<IDSL>, new()
        where IDSL : class
    {
        internal OutputPortID Port;

#if ENABLE_IL2CPP
        [Preserve]
        IDSLHandler DontStripConstructor()
        {
            return new TDSLDefinition();
        }
#endif

        public static explicit operator OutputPortID(DSLOutput<TNodeDefinition, TDSLDefinition, IDSL> output)
        {
            return output.Port;
        }
    }

    internal static unsafe class DataInputUtility
    {
        public enum Ownership : ushort
        {
            None = 0,
            OwnedByPort = 1 << 0,
            OwnedByBatch = 1 << 1,
            ExternalMask = OwnedByPort | OwnedByBatch
        }

        internal static ref Ownership GetMemoryOwnership(void** inputPortPatch)
        {
            // Note: hijacking the area of memory for the InputPortID of the DataInput which is
            // unused on the RenderGraph side.
            return ref *(Ownership*)(inputPortPatch + 1);
        }

        public static bool PortOwnsMemory(void** inputPortPatch)
            => GetMemoryOwnership(inputPortPatch) == Ownership.OwnedByPort;

        public static bool SomethingOwnsMemory(void** inputPortPatch)
            => (GetMemoryOwnership(inputPortPatch) & Ownership.ExternalMask) != 0;
    }

    /// <summary>
    /// Declaration of a specific data input connection port for a given node type.
    ///
    /// These are used as fields within an <see cref="IKernelPortDefinition"/> struct implementation
    /// (see <see cref="NodeDefinition{TNodeData,TSimulationportDefinition,TKernelData,TKernelPortDefinition,TKernel}.KernelPorts"/>).
    ///
    /// Connections and data appearing on these types of ports is only available in the node's implementation of <see cref="IGraphKernel{TKernelData,TKernelPortDefinition}.Execute"/>
    /// and accessible via the given <see cref="RenderContext"/> instance.
    /// </summary>
    /// <typeparam name="TDefinition">
    /// The <see cref="NodeDefinition{TNodeData,TSimulationportDefinition,TKernelData,TKernelPortDefinition,TKernel}"/> to which this port is associated.
    /// </typeparam>
    [DebuggerDisplay("{Value}")]
    public readonly unsafe struct DataInput<TDefinition, TType> : IIndexableInputPort
        where TDefinition : INodeDefinition
        where TType : struct
    {
        internal readonly void* Ptr;
        internal readonly InputPortID Port;

        public static explicit operator InputPortID(DataInput<TDefinition, TType> input)
        {
            return input.Port;
        }

        internal DataInput(void* ptr, InputPortID port)
        {
            Ptr = ptr;
            Port = port;
        }

        TType Value => Ptr != null ? Unsafe.AsRef<TType>(Ptr) : default;
    }

    /// <summary>
    /// Declaration of a specific data output connection port for a given node type.
    ///
    /// These are used as fields within an <see cref="IKernelPortDefinition"/> struct implementation
    /// (see <see cref="NodeDefinition{TNodeData,TSimulationportDefinition,TKernelData,TKernelPortDefinition,TKernel}.KernelPorts"/>).
    ///
    /// Data from these ports can only be produced in the node's implementation of <see cref="IGraphKernel{TKernelData,TKernelPortDefinition}.Execute"/>
    /// by filling out the instance accessible via the given <see cref="RenderContext"/>.
    /// </summary>
    /// <typeparam name="TDefinition">
    /// The <see cref="NodeDefinition{TNodeData,TSimulationportDefinition,TKernelData,TKernelPortDefinition,TKernel}"/> to which this port is associated.
    /// </typeparam>
    [DebuggerDisplay("{m_Value}")]
    public struct DataOutput<TDefinition, TType>
        where TDefinition : INodeDefinition
        where TType : struct
    {
        internal TType m_Value;
        internal OutputPortID Port;

        public static explicit operator OutputPortID(DataOutput<TDefinition, TType> output)
        {
            return output.Port;
        }
    }

    /// <summary>
    /// An array data type to be used inside of a <see cref="DataInput{TDefinition,TType}"/> or <see cref="DataOutput{TDefinition,TType}"/>.
    /// </summary>
    [DebuggerTypeProxy(typeof(BufferDebugView<>))]
    [DebuggerDisplay("Size = {Size}")]
    [StructLayout(LayoutKind.Sequential)]
    public readonly unsafe struct Buffer<T>
        where T : struct
    {
        internal readonly void* Ptr;
        internal readonly int Size;

        internal readonly NodeHandle OwnerNode;

        /// <summary>
        /// Gives access to the actual underlying data via a <see cref="NativeArray{T}"/>.
        /// </summary>
        public NativeArray<T> ToNative(in RenderContext ctx) => ctx.Resolve(this);
        /// <summary>
        /// Gives access to the actual underlying data via a <see cref="NativeArray{T}"/>.
        /// </summary>
        public NativeArray<T> ToNative(GraphValueResolver resolver) => resolver.Resolve(this);

        /// <summary>
        /// The return value should be used with <see cref="NodeSet.SetBufferSize{TDefinition, TType}"/> to resize
        /// a buffer.
        /// </summary>
        public static Buffer<T> SizeRequest(int size)
        {
            if (size < 0)
                throw new ArgumentOutOfRangeException();
            return new Buffer<T>(null, -size - 1, default);
        }

        internal Buffer(void* ptr, int size, in NodeHandle ownerNode)
        {
            Ptr = ptr;
            Size = size;
            OwnerNode = ownerNode;
        }

        internal (int Size, bool IsValid) GetSizeRequest() => (-Size - 1, Size <= 0);
    }

    internal sealed class BufferDebugView<T> where T : struct
    {
        private Buffer<T> m_Buffer;

        public BufferDebugView(Buffer<T> array)
        {
            m_Buffer = array;
        }

        unsafe public T[] Items
        {
            get
            {
                T[] ret = new T[m_Buffer.Size];

                for (int i = 0; i < m_Buffer.Size; ++i)
                {
                    ret[i] = UnsafeUtility.ReadArrayElement<T>(m_Buffer.Ptr, i);
                }

                return ret;
            }
        }
    }

    /// <summary>
    /// Describes the category of a port.
    /// </summary>
    public enum Usage
    {
        Message = 1 << 0,
        Data = 1 << 1,
        DomainSpecific = 1 << 2
    }

    /// <summary>
    /// Runtime type information about node ports as returned by <see cref="NodeFunctionality.GetPortDescription"/>.
    /// </summary>
    public struct PortDescription
    {
        internal interface IPort<TPortID> : IEquatable<TPortID>
            where TPortID : IPortID
        {
            Usage PortUsage { get; }
            Type Type { get; }
            string Name { get; }
        }

        /// <summary>
        /// Describes an input port on a node. An <see cref="InputPort"> can automatically decay to- and be used as a weakly typed <see cref="InputPortID">.
        /// </summary>
        public struct InputPort : IPort<InputPortID>, IEquatable<InputPort>
        {
            Usage m_PortUsage;
            Type m_Type;
            internal ushort m_Port;
            internal bool m_HasBuffers, m_IsPortArray;
            string m_Name;

            /// <summary>
            /// Describes the category of a port.
            /// </summary>
            public Usage PortUsage => m_PortUsage;

            /// <summary>
            /// Describes the data type of a port.
            /// </summary>
            public Type Type => m_Type;

            /// <summary>
            /// Returns the name of the port suitable for use in UI.
            /// </summary>
            public string Name => m_Name;

            /// <summary>
            /// True if the port's <see cref="Type"/> is a <see cref="Buffer{T}"/> or a struct which contains a
            /// <see cref="Buffer{T}"/> somewhere in its field hierarchy.
            /// </summary>
            internal bool HasBuffers => m_HasBuffers;

            /// <summary>
            /// True if the port is a <see cref="PortArray{TInputPort}"/>.
            /// </summary>
            internal bool IsPortArray => m_IsPortArray;

            internal static InputPort Data(Type type, ushort port, bool hasBuffers, bool isPortArray, string name)
            {
                InputPort ret;
                ret.m_PortUsage = Usage.Data;
                ret.m_Type = type;
                ret.m_Port = port;
                ret.m_Name = name;
                ret.m_HasBuffers = hasBuffers;
                ret.m_IsPortArray = isPortArray;
                return ret;
            }

            internal static InputPort Message(Type type, ushort port, bool isPortArray, string name)
            {
                InputPort ret;
                ret.m_PortUsage = Usage.Message;
                ret.m_Type = type;
                ret.m_Port = port;
                ret.m_Name = name;
                ret.m_HasBuffers = false;
                ret.m_IsPortArray = isPortArray;
                return ret;
            }

            internal static InputPort DSL(Type type, ushort port, string name)
            {
                InputPort ret;
                ret.m_PortUsage = Usage.DomainSpecific;
                ret.m_Type = type;
                ret.m_Port = port;
                ret.m_Name = name;
                ret.m_HasBuffers = false;
                ret.m_IsPortArray = false;
                return ret;
            }

            /// <summary>
            /// Returns the weakly typed port identifier that corresponds to the input port being described.
            /// </summary>
            public static implicit operator InputPortID(InputPort port)
            {
                return new InputPortID(port.m_Port);
            }

            public static bool operator ==(InputPort left, InputPort right)
            {
                return
                    left.m_PortUsage == right.m_PortUsage &&
                    left.m_Type == right.m_Type &&
                    left.m_Port == right.m_Port &&
                    left.m_IsPortArray == right.m_IsPortArray &&
                    left.m_Name == right.m_Name;
            }

            public static bool operator !=(InputPort left, InputPort right)
            {
                return !(left == right);
            }

            public static bool operator ==(InputPortID left, InputPort right)
            {
                return left.Port == right.m_Port;
            }

            public static bool operator !=(InputPortID left, InputPort right)
            {
                return left.Port != right.m_Port;
            }

            public bool Equals(InputPort port)
            {
                return this == port;
            }

            public bool Equals(InputPortID portID)
            {
                return this == portID;
            }

            public override bool Equals(object obj)
            {
                return
                    (obj is InputPort port && this == port) ||
                    (obj is InputPortID portID && this == portID);
            }

            public override int GetHashCode()
            {
                return
                    (((int)m_PortUsage) << 0) ^ // 3-bits
                    (((int)m_Port) << 3) ^ // 16-bits
                    ((m_IsPortArray ? 1 : 0) << 19) ^ // 1-bit
                    m_Type.GetHashCode() ^
                    m_Name.GetHashCode();
            }
        }

        /// <summary>
        /// Describes an output port on a node. An <see cref="OutputPort"> can automatically decay to- and be used as a weakly typed <see cref="OutputPortID">.
        /// </summary>
        public struct OutputPort : IPort<OutputPortID>, IEquatable<OutputPort>
        {
            Usage m_PortUsage;
            Type m_Type;
            internal ushort m_Port;
            internal List<(int Offset, SimpleType ItemType)> m_BufferInfos;
            string m_Name;

            /// <summary>
            /// Describes the category of a port.
            /// </summary>
            public Usage PortUsage => m_PortUsage;

            /// <summary>
            /// Describes the data type of a port.
            /// </summary>
            public Type Type => m_Type;

            /// <summary>
            /// Returns the name of the port suitable for use in UI.
            /// </summary>
            public string Name => m_Name;

            /// <summary>
            /// True if the port's <see cref="Type"/> is a <see cref="Buffer{T}"/> or a struct which contains a
            /// <see cref="Buffer{T}"/> somewhere in its field hierarchy.
            /// </summary>
            internal bool HasBuffers => m_BufferInfos?.Count > 0;

            /// <summary>
            /// List of offsets of all <see cref="Buffer{T}"/> instances within the port relative to the beginning of
            /// the <see cref="DataInput{TDefinition,TType}"/>
            /// </summary>
            internal List<(int Offset, SimpleType ItemType)> BufferInfos => m_BufferInfos;

            internal static OutputPort Data(Type type, ushort port, List<(int Offset, SimpleType ItemType)> bufferInfos, string name)
            {
                OutputPort ret;
                ret.m_PortUsage = Usage.Data;
                ret.m_Type = type;
                ret.m_Port = port;
                ret.m_Name = name;
                ret.m_BufferInfos = bufferInfos;
                return ret;
            }

            internal static OutputPort Message(Type type, ushort port, string name)
            {
                OutputPort ret;
                ret.m_PortUsage = Usage.Message;
                ret.m_Type = type;
                ret.m_Port = port;
                ret.m_Name = name;
                ret.m_BufferInfos = null;
                return ret;
            }

            internal static OutputPort DSL(Type type, ushort port, string name)
            {
                OutputPort ret;
                ret.m_PortUsage = Usage.DomainSpecific;
                ret.m_Type = type;
                ret.m_Port = port;
                ret.m_Name = name;
                ret.m_BufferInfos = null;
                return ret;
            }

            /// <summary>
            /// Returns the weakly typed port identifier that corresponds to the output port being described.
            /// </summary>
            public static implicit operator OutputPortID(OutputPort port)
            {
                return new OutputPortID { Port = port.m_Port };
            }

            public static bool operator ==(OutputPort left, OutputPort right)
            {
                return
                    left.m_PortUsage == right.m_PortUsage &&
                    left.m_Type == right.m_Type &&
                    left.m_Port == right.m_Port &&
                    left.m_Name == right.m_Name;
            }

            public static bool operator !=(OutputPort left, OutputPort right)
            {
                return !(left == right);
            }

            public static bool operator ==(OutputPortID left, OutputPort right)
            {
                return left.Port == right.m_Port;
            }

            public static bool operator !=(OutputPortID left, OutputPort right)
            {
                return left.Port != right.m_Port;
            }

            public bool Equals(OutputPort port)
            {
                return this == port;
            }

            public bool Equals(OutputPortID portID)
            {
                return this == portID;
            }

            public override bool Equals(object obj)
            {
                return
                    (obj is OutputPort port && this == port) ||
                    (obj is OutputPortID portID && this == portID);
            }

            public override int GetHashCode()
            {
                return
                    (((int)m_PortUsage) << 0) ^ // 3-bits
                    (((int)m_Port.GetHashCode()) << 3) ^ // 16-bits
                    m_Type.GetHashCode() ^
                    m_Name.GetHashCode();
            }

        }
        List<InputPort> inputs;
        List<OutputPort> outputs;

        public List<InputPort> Inputs { get => inputs; set => inputs = value; }
        public List<OutputPort> Outputs { get => outputs; set => outputs = value; }
    }

    public interface ITaskPort<in TTask>
    {
        InputPortID GetPort(NodeHandle handle);
    }
}
