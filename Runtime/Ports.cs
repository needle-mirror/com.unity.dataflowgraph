using System;
using Unity.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;
using System.Diagnostics;
using Unity.Entities;
#if ENABLE_IL2CPP
using UnityEngine.Scripting;
#endif

namespace Unity.DataFlowGraph
{

#if UNITY_64 || UNITY_EDITOR_64
    using PointerWord = System.UInt64;
#else
    using PointerWord = System.UInt32;
#endif

#pragma warning disable 660, 661  // We do not want Equals(object) nor GetHashCode() for all port primitives

    /// <summary>
    /// Base interface for weakly typed port IDs. Namely <see cref="InputPortID"/> and <see cref="OutputPortID"/>.
    /// </summary>
    interface IPortID { }

    [DebuggerDisplay("{ToString(), nq}")]
    readonly struct PortStorage
    {
        // TODO: Changed to TypeManager.ManagedComponentFlag once ECS 2.0 is available.
        internal const int IsECSPortFlag = TypeManager.SharedComponentTypeFlag;
        internal const int IsDFGPortFlag = IsECSPortFlag << 1;

        readonly int m_TypeOrPort;

        public PortStorage(ushort dfgPortIndex)
        {
            m_TypeOrPort = dfgPortIndex | IsDFGPortFlag;
        }

        public PortStorage(ComponentType ecsType)
        {
#if DFG_ASSERTIONS
            if((ecsType.TypeIndex & IsECSPortFlag) != 0)
                throw new AssertionException("Port storage being created with an incompatible ECS type (flag being reused)");
#endif
            m_TypeOrPort = ecsType.TypeIndex | IsECSPortFlag;
        }

        // unsigned modulo 32 bits -> 8 bits chops off IsDFGPortFlag.
        public ushort DFGPortIndex
        {
            get {
#if DFG_ASSERTIONS
                if(!IsDFGPort)
                    throw new AssertionException("Retrieving DFG port from a storage containing an ECS type");
#endif
                return (ushort)m_TypeOrPort;
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
            return IsECSPort ? $"ECS: {ReadOnlyComponentType}" : IsDFGPort ? $"DFG: {DFGPortIndex}" : "<INVALID>";
        }
    }

    /// <summary>
    /// Weakly typed identifier for a given input port of a node.
    /// </summary>
    [DebuggerDisplay("{ToString(), nq}")]
    public readonly struct InputPortID : IPortID
    {
        internal ushort Port => Storage.DFGPortIndex;
        internal ComponentType ECSType => Storage.ReadOnlyComponentType;

        internal readonly PortStorage Storage;

        public static bool operator ==(InputPortID left, InputPortID right)
        {
            return left.Storage == right.Storage;
        }

        public static bool operator !=(InputPortID left, InputPortID right)
        {
            return left.Storage != right.Storage;
        }

        internal InputPortID(PortStorage storage)
        {
            Storage = storage;
        }

        public override string ToString()
        {
            return $"Input {Storage}";
        }
    }

    [DebuggerDisplay("{ToString(), nq}")]
    readonly struct InputPortArrayID : IEquatable<InputPortArrayID>
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

        public bool Equals(InputPortArrayID other) => this == other;

        internal InputPortArrayID(InputPortID portId, int arrayIndex)
        {
            if ((uint)arrayIndex >= NonArraySentinel)
                throw new InvalidOperationException("Invalid array index.");
            PortID = portId;
            m_ArrayIndex = (ushort) arrayIndex;
        }

        internal InputPortArrayID(InputPortID portId)
        {
            PortID = portId;
            m_ArrayIndex = NonArraySentinel;
        }

        internal ushort ArrayIndex => m_ArrayIndex;

        internal bool IsArray => m_ArrayIndex != NonArraySentinel;

        public override string ToString()
        {
            if (IsArray)
                return $"Array[{ArrayIndex}]{PortID}";

            return PortID.ToString();
        }
    }

    /// <summary>
    /// Weakly typed identifier for a given output port of a node.
    /// </summary>
    [DebuggerDisplay("{ToString(), nq}")]
    public readonly struct OutputPortID : IPortID, IEquatable<OutputPortID>
    {
        internal ushort Port => Storage.DFGPortIndex;
        internal ComponentType ECSType => Storage.ReadWriteComponentType;

        internal readonly PortStorage Storage;

        public static bool operator ==(OutputPortID left, OutputPortID right)
        {
            return left.Storage == right.Storage;
        }

        public static bool operator !=(OutputPortID left, OutputPortID right)
        {
            return left.Storage != right.Storage;
        }

        public bool Equals(OutputPortID other) => this == other;

        public override string ToString()
        {
            return $"Output {Storage}";
        }

        internal OutputPortID(PortStorage storage)
        {
            Storage = storage;
        }
    }

    [DebuggerDisplay("{ToString(), nq}")]
    readonly struct OutputPortArrayID : IEquatable<OutputPortArrayID>
    {
        public const UInt16 NonArraySentinel = UInt16.MaxValue;
        public readonly OutputPortID PortID;
        readonly ushort m_ArrayIndex;

        public static bool operator ==(OutputPortArrayID left, OutputPortArrayID right)
        {
            return left.PortID == right.PortID && left.m_ArrayIndex == right.m_ArrayIndex;
        }

        public static bool operator !=(OutputPortArrayID left, OutputPortArrayID right)
        {
            return left.PortID != right.PortID || left.m_ArrayIndex != right.m_ArrayIndex;
        }

        public bool Equals(OutputPortArrayID other) => this == other;

        internal OutputPortArrayID(OutputPortID portId, int arrayIndex)
        {
            if ((uint)arrayIndex >= NonArraySentinel)
                throw new InvalidOperationException("Invalid array index.");
            PortID = portId;
            m_ArrayIndex = (ushort)arrayIndex;
        }

        internal OutputPortArrayID(OutputPortID portId)
        {
            PortID = portId;
            m_ArrayIndex = NonArraySentinel;
        }

        internal ushort ArrayIndex => m_ArrayIndex;

        internal bool IsArray => m_ArrayIndex != NonArraySentinel;

        public override string ToString()
        {
            if (IsArray)
                return $"Array[{ArrayIndex}]{PortID}";

            return PortID.ToString();
        }
    }

    /// <summary>
    /// Base interface all input port types that are allowed in <see cref="PortArray"/>s.
    /// </summary>
    public interface IInputPort : IIndexablePort {}

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
    public struct MessageInput<TDefinition, TMsg> : IInputPort
        where TDefinition : NodeDefinition
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

        internal static MessageInput<TDefinition, TMsg> Create(InputPortID port)
        {
            return new MessageInput<TDefinition, TMsg> { Port = port };
        }
    }

    /// <summary>
    /// Base interface all output port types that are allowed in <see cref="PortArray"/>s.
    /// </summary>
    public interface IOutputPort : IIndexablePort {}

    /// <summary>
    /// Declaration of a specific message output connection port for a given node type.
    ///
    /// These are used as fields within an <see cref="ISimulationPortDefinition"/> struct implementation
    /// (see <see cref="NodeDefinition{TNodeData,TSimulationPortDefinition}.SimulationPorts"/>).
    /// </summary>
    /// <typeparam name="TDefinition">
    /// The <see cref="NodeDefinition{TNodeData,TSimulationPortDefinition}"/> to which this port is associated.
    /// </typeparam>
    public struct MessageOutput<TDefinition, TMsg> : IOutputPort
        where TDefinition : NodeDefinition
    {
        internal OutputPortID Port;

        public static bool operator ==(OutputPortID left, MessageOutput<TDefinition, TMsg> right)
        {
            return left == right.Port;
        }

        public static bool operator !=(OutputPortID left, MessageOutput<TDefinition, TMsg> right)
        {
            return left != right.Port;
        }

        public static explicit operator OutputPortID(MessageOutput<TDefinition, TMsg> output)
        {
            return output.Port;
        }

        internal static MessageOutput<TDefinition, TMsg> Create(OutputPortID port)
        {
            return new MessageOutput<TDefinition, TMsg> { Port = port };
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
        where TNodeDefinition : NodeDefinition, IDSL
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

        internal static DSLInput<TNodeDefinition, TDSLDefinition, IDSL> Create(InputPortID port)
        {
            return new DSLInput<TNodeDefinition, TDSLDefinition, IDSL> { Port = port };
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
        where TNodeDefinition : NodeDefinition, IDSL
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

        internal static DSLOutput<TNodeDefinition, TDSLDefinition, IDSL> Create(OutputPortID port)
        {
            return new DSLOutput<TNodeDefinition, TDSLDefinition, IDSL> { Port = port };
        }
    }

    internal readonly unsafe struct DataInputStorage
    {
        public const int MinimumInputAlignment = (int)(k_OwnershipMask + 1);
        const PointerWord k_OwnershipMask = 0x3;
        const PointerWord k_PointerMask = ~k_OwnershipMask;

        public enum Ownership : PointerWord
        {
            None = 0,
            OwnedByPort = 1 << 0,
            ExternalMask = OwnedByPort
        }

        readonly public void* Pointer;

        readonly public InputPortID PortID;

        public DataInputStorage(void* pointer)
        {
            Pointer = pointer;
            PortID = default;
#if DFG_ASSERTIONS
            if (((PointerWord)Pointer & k_OwnershipMask) != 0)
                throw new AssertionException("Pointer to DataInput must have first three bits zero (alignment)");
#endif
        }

        public DataInputStorage(InputPortID portID)
        {
            Pointer = default;
            PortID = portID;
        }

        public DataInputStorage(void* pointer, Ownership ownership)
            : this(pointer)
        {
#if DFG_ASSERTIONS
            if (((PointerWord)ownership & k_PointerMask) != 0)
                throw new AssertionException("Ownership has out of range bits");
#endif
            Utility.As<InputPortID, Ownership>(ref PortID) = ownership;
        }

        public void FreeIfNeeded(Allocator originalAllocator)
        {
            if (OwnsMemory())
                UnsafeUtility.Free(Pointer, originalAllocator);
        }

        public Ownership GetMemoryOwnership()
        {
            return Utility.As<InputPortID, Ownership>(ref Utility.AsRef(PortID));
        }

        public bool OwnsMemory() => GetMemoryOwnership() == Ownership.OwnedByPort;

        public static bool PortOwnsMemory<TDefinition, TType>(in DataInput<TDefinition, TType> port)
            where TDefinition : NodeDefinition
            where TType : struct
        {
            return port.Storage.OwnsMemory();
        }

        public bool SomethingOwnsMemory()
            => GetMemoryOwnership() != Ownership.None;
    }

    public interface IDataInputPort : IInputPort {}

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
    public readonly unsafe struct DataInput<TDefinition, TType> : IDataInputPort
        where TDefinition : NodeDefinition
        where TType : struct
    {
        internal readonly DataInputStorage Storage;
        internal void* Ptr => Storage.Pointer;
        internal InputPortID Port => Storage.PortID;

        /// <summary>
        /// Converts the <paramref name="input"/> to an untyped <see cref="InputPortID"/>.
        /// </summary>
        /// <remarks>
        /// Has an undefined return value when invoked inside an
        /// <see cref="IGraphKernel{TKernelData, TKernelPortDefinition}.Execute(RenderContext, TKernelData, ref TKernelPortDefinition)"/>
        /// </remarks>
        public static explicit operator InputPortID(DataInput<TDefinition, TType> input)
        {
            return input.Port;
        }

        /// <summary>
        /// Creates a DataInput that must only be used as a pointer to another
        /// <see cref="DataOutput{TDefinition, TType}"/> or <see cref="InternalComponentNode.OutputFromECS"/>.
        /// <seealso cref="RenderContext.Resolve{TNodeDefinition, T}(in DataInput{TNodeDefinition, T})"/>
        /// </summary>
        internal DataInput(void* ptr) => Storage = new DataInputStorage(ptr);
        /// <summary>
        /// Creates a DataInput that must only be used as a <see cref="InputPortID"/>
        /// <seealso cref="Port"/>
        /// <seealso cref="DataInput(InputPortID)"/>
        /// </summary>
        internal DataInput(InputPortID port) => Storage = new DataInputStorage(port);

        /// <summary>
        /// Creates a DataInput that must only be used as a <see cref="InputPortID"/>
        /// <seealso cref="Port"/>
        /// <seealso cref="DataInput(InputPortID)"/>
        /// </summary>
        internal static DataInput<TDefinition, TType> Create(InputPortID port)
        {
            return new DataInput<TDefinition, TType>(port);
        }

        TType Value => Ptr != null ? Utility.AsRef<TType>(Ptr) : default;
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
        where TDefinition : NodeDefinition
        where TType : struct
    {
        internal TType m_Value;
        internal OutputPortID Port;

        public static explicit operator OutputPortID(DataOutput<TDefinition, TType> output)
        {
            return output.Port;
        }

        internal static DataOutput<TDefinition, TType> Create(OutputPortID port)
        {
            return new DataOutput<TDefinition, TType>{ m_Value = default, Port = port };
        }
    }

#pragma warning restore 660, 661

    /// <summary>
    /// A buffer description is an unmanaged and memory layout compatible (aliasable) with
    /// any <see cref="Buffer{T}"/>.
    /// </summary>
    readonly unsafe struct BufferDescription : IEquatable<BufferDescription>
    {
        internal readonly void* Ptr;
        internal readonly int Size;
        internal readonly ValidatedHandle OwnerNode;

        internal (int Size, bool IsValid) GetSizeRequest() => (-Size - 1, Size <= 0);

        internal BufferDescription(void* ptr, int size, in ValidatedHandle ownerNode)
        {
            Ptr = ptr;
            Size = size;
            OwnerNode = ownerNode;
        }

        public bool Equals(BufferDescription other)
        {
            return Ptr == other.Ptr && Size == other.Size && OwnerNode == other.OwnerNode;
        }

        public bool Equals<T>(in Buffer<T> other)
            where T : struct
            => Equals(other.Description);
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
        internal void* Ptr => Description.Ptr;
        internal int Size => Description.Size;
        internal ValidatedHandle OwnerNode => Description.OwnerNode;
        internal (int Size, bool IsValid) GetSizeRequest() => Description.GetSizeRequest();

        internal readonly BufferDescription Description;
        /// <summary>
        /// Gives access to the actual underlying data via a <see cref="NativeArray{T}"/>.
        /// </summary>
        public NativeArray<T> ToNative(in RenderContext ctx) => ctx.Resolve(this);
        /// <summary>
        /// Gives access to the actual underlying data via a <see cref="NativeArray{T}"/>.
        /// </summary>
        public NativeArray<T> ToNative(GraphValueResolver resolver) => resolver.Resolve(this);

        /// <summary>
        /// The return value should be used with <see cref="NodeSetAPI.SetBufferSize{TDefinition, TType}"/> to resize
        /// a buffer.
        /// </summary>
        public static Buffer<T> SizeRequest(int size)
        {
            if (size < 0)
                throw new ArgumentOutOfRangeException();
            return new Buffer<T>(null, -size - 1, default);
        }

        internal Buffer(void* ptr, int size, in ValidatedHandle ownerNode)
        {
            Description = new BufferDescription(ptr, size, ownerNode);
        }

        internal Buffer(BufferDescription d) => Description = d;
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
    /// Runtime type information about node ports as returned by <see cref="NodeDefinition.GetPortDescription"/>.
    /// </summary>
    public struct PortDescription
    {
        internal const uint k_MaskForAnyData = (int)Category.Data | ((int)Category.Data << (int)CategoryShift.FeedbackConnection) | ((int)Category.Data << (int)CategoryShift.BackConnection);

        /// <summary>
        /// Bit-shifting constants used to modulate <see cref="Category"/>
        /// </summary>
        internal enum CategoryShift
        {
            None,
            FeedbackConnection,
            BackConnection,
            Max
        }

        /// <summary>
        /// Describes the category of a port.
        /// </summary>
        // Note: leave room in bit mask for shifting according to above CategoryShift constants.
        public enum Category
        {
            Message = 1 << 0,
            Data = Message << CategoryShift.Max,
            DomainSpecific = Data << CategoryShift.Max
        }

        /// <remarks>
        /// Generally, the connection traversal mask is the category of its two endpoints, but in a Message->Data
        /// connection, the two endpoint categories do not match, thus, a special value is used.
        /// </remarks>
        internal const uint MessageToDataConnectionCategory = (uint)Category.DomainSpecific << (int)CategoryShift.Max;

        internal interface IPort<TPortID> : IEquatable<TPortID>
            where TPortID : IPortID
        {
            Category Category { get; }
            Type Type { get; }
            string Name { get; }
        }

        /// <summary>
        /// Describes an input port on a node. An <see cref="InputPort"/> can automatically decay to- and be used as a weakly typed <see cref="InputPortID"/>.
        /// </summary>
        public struct InputPort : IPort<InputPortID>, IEquatable<InputPort>
        {
            Category m_Category;
            Type m_Type;
            internal ushort m_Port;
            internal bool m_HasBuffers, m_IsPortArray, m_IsPublic;
            string m_Name;

            /// <summary>
            /// Describes the category of a port.
            /// </summary>
            public Category Category => m_Category;

            [Obsolete("Use Category instead.", true)]
            public Category PortUsage => m_Category;

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
            public bool IsPortArray => m_IsPortArray;

            internal bool IsPublic => m_IsPublic;

            internal static InputPort Data(Type type, ushort port, bool hasBuffers, bool isPortArray, bool isPublic, string name)
            {
                InputPort ret;
                ret.m_Category = Category.Data;
                ret.m_Type = type;
                ret.m_Port = port;
                ret.m_Name = name;
                ret.m_HasBuffers = hasBuffers;
                ret.m_IsPortArray = isPortArray;
                ret.m_IsPublic = isPublic;
                return ret;
            }

            internal static InputPort Message(Type type, ushort port, bool isPortArray, bool isPublic, string name)
            {
                InputPort ret;
                ret.m_Category = Category.Message;
                ret.m_Type = type;
                ret.m_Port = port;
                ret.m_Name = name;
                ret.m_HasBuffers = false;
                ret.m_IsPortArray = isPortArray;
                ret.m_IsPublic = isPublic;
                return ret;
            }

            internal static InputPort DSL(Type type, ushort port, bool isPublic, string name)
            {
                InputPort ret;
                ret.m_Category = Category.DomainSpecific;
                ret.m_Type = type;
                ret.m_Port = port;
                ret.m_Name = name;
                ret.m_HasBuffers = false;
                ret.m_IsPortArray = false;
                ret.m_IsPublic = isPublic;
                return ret;
            }

            /// <summary>
            /// Returns the weakly typed port identifier that corresponds to the input port being described.
            /// </summary>
            public static implicit operator InputPortID(InputPort port)
            {
                return new InputPortID(new PortStorage(port.m_Port));
            }

            public static bool operator ==(InputPort left, InputPort right)
            {
                return
                    left.m_Category == right.m_Category &&
                    left.m_Type == right.m_Type &&
                    left.m_Port == right.m_Port &&
                    left.m_IsPortArray == right.m_IsPortArray &&
                    left.m_IsPublic == right.m_IsPublic &&
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
                    (((int)m_Category) << 0) ^ // 3-bits
                    (((int)m_Port) << 3) ^ // 16-bits
                    ((m_IsPortArray ? 1 : 0) << 19) ^ // 1-bit
                    ((m_IsPublic ? 1 : 0) << 20) ^ // 1-bit
                    m_Type.GetHashCode() ^
                    m_Name.GetHashCode();
            }
        }

        /// <summary>
        /// Describes an output port on a node. An <see cref="OutputPort"> can automatically decay to- and be used as a weakly typed <see cref="OutputPortID">.
        /// </summary>
        public struct OutputPort : IPort<OutputPortID>, IEquatable<OutputPort>
        {
            Category m_Category;
            Type m_Type;
            internal ushort m_Port;
            internal List<(int Offset, SimpleType ItemType)> m_BufferInfos;
            internal bool m_IsPortArray, m_IsPublic;
            string m_Name;

            /// <summary>
            /// Describes the category of a port.
            /// </summary>
            public Category Category => m_Category;

            [Obsolete("Use Category instead.", true)]
            public Category PortUsage => m_Category;

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
            /// True if the port is a <see cref="PortArray{TInputPort}"/>.
            /// </summary>
            public bool IsPortArray => m_IsPortArray;

            internal bool IsPublic => m_IsPublic;

            /// <summary>
            /// List of offsets of all <see cref="Buffer{T}"/> instances within the port relative to the beginning of
            /// the <see cref="DataInput{TDefinition,TType}"/>
            /// </summary>
            internal List<(int Offset, SimpleType ItemType)> BufferInfos => m_BufferInfos;

            internal static OutputPort Data(Type type, ushort port, List<(int Offset, SimpleType ItemType)> bufferInfos, bool isPublic, string name)
            {
                OutputPort ret;
                ret.m_Category = Category.Data;
                ret.m_Type = type;
                ret.m_Port = port;
                ret.m_Name = name;
                ret.m_BufferInfos = bufferInfos;
                ret.m_IsPortArray = false;
                ret.m_IsPublic = isPublic;
                return ret;
            }

            internal static OutputPort Message(Type type, ushort port, bool isPortArray, bool isPublic, string name)
            {
                OutputPort ret;
                ret.m_Category = Category.Message;
                ret.m_Type = type;
                ret.m_Port = port;
                ret.m_Name = name;
                ret.m_BufferInfos = null;
                ret.m_IsPortArray = isPortArray;
                ret.m_IsPublic = isPublic;
                return ret;
            }

            internal static OutputPort DSL(Type type, ushort port, bool isPublic, string name)
            {
                OutputPort ret;
                ret.m_Category = Category.DomainSpecific;
                ret.m_Type = type;
                ret.m_Port = port;
                ret.m_Name = name;
                ret.m_BufferInfos = null;
                ret.m_IsPortArray = false;
                ret.m_IsPublic = isPublic;
                return ret;
            }

            /// <summary>
            /// Returns the weakly typed port identifier that corresponds to the output port being described.
            /// </summary>
            public static implicit operator OutputPortID(OutputPort port)
            {
                return new OutputPortID (new PortStorage(port.m_Port));
            }

            public static bool operator ==(OutputPort left, OutputPort right)
            {
                return
                    left.m_Category == right.m_Category &&
                    left.m_Type == right.m_Type &&
                    left.m_Port == right.m_Port &&
                    left.m_IsPortArray == right.m_IsPortArray &&
                    left.m_IsPublic == right.m_IsPublic &&
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
                    (((int)m_Category) << 0) ^ // 3-bits
                    (((int)m_Port.GetHashCode()) << 3) ^ // 16-bits
                    ((m_IsPortArray ? 1 : 0) << 19) ^ // 1-bit
                    ((m_IsPublic ? 1 : 0) << 20) ^ // 1-bit
                    m_Type.GetHashCode() ^
                    m_Name.GetHashCode();
            }

        }

        internal List<ComponentType> ComponentTypes;

        List<InputPort> m_Inputs;
        List<OutputPort> m_Outputs;

        public List<InputPort> Inputs { get => m_Inputs; internal set => m_Inputs = value; }
        public List<OutputPort> Outputs { get => m_Outputs; internal set => m_Outputs = value; }
    }

    public interface ITaskPort<in TTask>
    {
        InputPortID GetPort(NodeHandle handle);
    }
}
