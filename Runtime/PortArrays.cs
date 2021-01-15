using System;
using System.Diagnostics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.DataFlowGraph
{
    /// <summary>
    /// Base interface for all port types that are allowed in <see cref="PortArray"/>s.
    /// </summary>
    public interface IIndexablePort { }

#pragma warning disable 660, 661  // We do not want Equals(object) nor GetHashCode()
    readonly struct InputOutputPortID
    {
        public readonly PortStorage PortStorage;
        public readonly bool IsInput;

        public InputOutputPortID(InputPortID port)
        {
            IsInput = true;
            PortStorage = port.Storage;
        }

        public InputOutputPortID(OutputPortID port)
        {
            IsInput = false;
            PortStorage = port.Storage;
        }

        public static bool operator ==(InputOutputPortID left, InputOutputPortID right)
        {
            return left.IsInput == right.IsInput && left.PortStorage == right.PortStorage;
        }

        public static bool operator !=(InputOutputPortID left, InputOutputPortID right)
        {
            return left.IsInput != right.IsInput || left.PortStorage != right.PortStorage;
        }

        public InputPortID InputPort
        {
            get
            {
#if DFG_ASSERTIONS
                if (!IsInput)
                    throw new AssertionException("Port array does not represent an input");
#endif

                return new InputPortID(PortStorage);
            }
        }

        public OutputPortID OutputPort
        {
            get
            {
#if DFG_ASSERTIONS
                if (IsInput)
                    throw new AssertionException("Port array does not represent an output");
#endif

                return new OutputPortID(PortStorage);
            }
        }
    }
#pragma warning restore 660, 661

    readonly unsafe struct PortArrayStorage
    {
        // move InputOutputPortID in here, private.
        public const UInt16 MaxSize = InputPortArrayID.NonArraySentinel;

        public readonly void* Ptr;
        public readonly ushort Size;
        public readonly InputOutputPortID PortID;

        public PortArrayStorage(void* newPtr, ushort newSize, InputOutputPortID portID)
        {
            Ptr = newPtr;
            Size = newSize;
            PortID = portID;
        }
    }

    /// <summary>
    /// Declaration of an array of ports (used within an <see cref="ISimulationPortDefinition"/> or <see cref="IKernelPortDefinition"/>).
    /// Used when a node requires an array of ports with a size that can be changed dynamically.
    /// </summary>
    /// <typeparam name="TPort">Input or output port declaration (eg. <see cref="MessageInput{TDefinition, TMsg}"/>,
    /// <see cref="MessageOutput{TDefinition, TMsg}"/>, <see cref="DataInput{TDefinition, TType}"/>
#pragma warning disable 660, 661  // We do not want Equals(object) nor GetHashCode()
    [DebuggerTypeProxy(typeof(PortArrayDebugView<>))]
    public readonly struct PortArray<TPort>
        where TPort : struct, IIndexablePort
    {
        public const UInt16 MaxSize = PortArrayStorage.MaxSize;

        readonly PortArrayStorage m_Storage;

        internal ushort Size => m_Storage.Size;
        internal unsafe void* Ptr => m_Storage.Ptr;

        public static bool operator ==(InputPortID left, PortArray<TPort> right)
        {
            return right.m_Storage.PortID.IsInput && left == right.m_Storage.PortID.InputPort;
        }

        public static bool operator !=(InputPortID left, PortArray<TPort> right)
        {
            return !(left == right);
        }

        public static bool operator ==(PortArray<TPort> left, InputPortID right)
        {
            return right == left;
        }

        public static bool operator !=(PortArray<TPort> left, InputPortID right)
        {
            return !(left == right);
        }

        public static bool operator ==(OutputPortID left, PortArray<TPort> right)
        {
            return !right.m_Storage.PortID.IsInput && left == right.m_Storage.PortID.OutputPort;
        }

        public static bool operator !=(OutputPortID left, PortArray<TPort> right)
        {
            return !(left == right);
        }

        public static bool operator ==(PortArray<TPort> left, OutputPortID right)
        {
            return right == left;
        }

        public static bool operator !=(PortArray<TPort> left, OutputPortID right)
        {
            return !(left == right);
        }

        public static explicit operator InputPortID(PortArray<TPort> input)
        {
            if (!input.m_Storage.PortID.IsInput)
                throw new InvalidOperationException("Port array does not represent an input");

            return input.m_Storage.PortID.InputPort;
        }

        public static explicit operator OutputPortID(PortArray<TPort> output)
        {
            if (output.m_Storage.PortID.IsInput)
                throw new InvalidOperationException("Port array does not represent an output");

            return output.m_Storage.PortID.OutputPort;
        }

        internal static PortArray<TPort> Create(InputPortID port) => new PortArray<TPort>(new InputOutputPortID(port));
        internal static PortArray<TPort> Create(OutputPortID port) => new PortArray<TPort>(new InputOutputPortID(port));

        internal static InputPortID InputPort<TInputPort>(in PortArray<TInputPort> portArray)
            where TInputPort : struct, IInputPort
                => portArray.m_Storage.PortID.InputPort;

        internal static OutputPortID OutputPort<TOutputPort>(in PortArray<TOutputPort> portArray)
            where TOutputPort : struct, IOutputPort
                => portArray.m_Storage.PortID.OutputPort;

        unsafe PortArray(InputOutputPortID port)
        {
            m_Storage = new PortArrayStorage(null, 0, port);
        }
    }
#pragma warning restore 660, 661

    /// <summary>
    /// Extension methods added to <see cref="PortArray{TPort}"/> which are specific to
    /// PortArrays of input port types
    /// </summary>
    public static class InputPortArrayExt
    {
        /// <summary>
        /// Returns the <see cref="InputPortID"/> corresponding to this <see cref="PortArray{TPort}"/> similar to
        /// <see cref="PortArray{TPort}"/>'s explicit cast operator but without incurring a runtime check that the
        /// port in fact represents an input type.
        /// </summary>
        public static InputPortID GetPortID<TInputPort>(this in PortArray<TInputPort> portArray)
            where TInputPort : struct, IInputPort
                => PortArray<TInputPort>.InputPort(portArray);

        internal static unsafe ref TDataInput GetRef<TDataInput>(this in PortArray<TDataInput> portArray, ushort i)
            where TDataInput : struct, IDataInputPort
                => ref UnsafeUtility.AsRef<TDataInput>((byte*)portArray.Ptr + i * UnsafeUtility.SizeOf<TDataInput>());

        internal static unsafe ref UntypedDataInputPortArray AsUntyped<TDataInput>(this ref PortArray<TDataInput> portArray)
            where TDataInput : struct, IDataInputPort
                => ref *(UntypedDataInputPortArray*)UnsafeUtility.AddressOf(ref portArray);
    }

    /// <summary>
    /// Extension methods added to <see cref="PortArray{TPort}"/> which are specific to
    /// PortArrays of output port types
    /// </summary>
    public static class OutputPortArrayExt
    {
        internal struct PortArrayDetails<TType>
            where TType : struct
        {
            public static readonly SimpleType ElementType =
                SimpleType.Create<TType>().GetMinAlignedTyped(DataInputStorage.MinimumInputAlignment);
        }

        /// <summary>
        /// Returns the <see cref="OutputPortID"/> corresponding to this <see cref="PortArray{TPort}"/> similar to
        /// <see cref="PortArray{TPort}"/>'s explicit cast operator but without incurring a runtime check that the
        /// port in fact represents an output type.
        /// </summary>
        public static OutputPortID GetPortID<TOutputPort>(this in PortArray<TOutputPort> portArray)
            where TOutputPort : struct, IOutputPort
                => PortArray<TOutputPort>.OutputPort(portArray);

        internal unsafe static ref TType GetRef<TDefinition, TType>(this in PortArray<DataOutput<TDefinition, TType>> portArray, ushort i)
            where TDefinition : NodeDefinition
            where TType : struct
                => ref UnsafeUtility.AsRef<TType>((byte*)portArray.Ptr + i * portArray.GetElementType().Size);

        internal static SimpleType GetElementType<TDefinition, TType>(this in PortArray<DataOutput<TDefinition, TType>> _portArray)
            where TDefinition : NodeDefinition
            where TType : struct
                => PortArrayDetails<TType>.ElementType;

        internal static unsafe ref UntypedDataOutputPortArray AsUntyped<TDataOutput>(this ref PortArray<TDataOutput> portArray)
            where TDataOutput : struct, IDataOutputPort
                => ref *(UntypedDataOutputPortArray*)UnsafeUtility.AddressOf(ref portArray);
    }

    /// <summary>
    /// A view on a concrete instance of a <see cref="PortArray{TPort}"/> of <see cref="DataInput{TDefinition,TType}"/>
    /// but without knowing the concrete TDefinition and TType types. Offers a reduced API on the PortArray corresponding
    /// to the operations that are safe to perform without the aforementioned missing information.
    /// </summary>
    unsafe ref struct UntypedDataInputPortArray
    {
        PortArrayStorage m_Storage;

        public void Resize(ushort newSize, void* blankPage, Allocator allocator)
        {
#if DFG_ASSERTIONS
            if (newSize == PortArrayStorage.MaxSize)
                throw new AssertionException("Requested array size is too large");
#endif
            if (newSize == m_Storage.Size)
                return;

            // Release any owned memory if downsizing.
            for (ushort i = newSize; i < m_Storage.Size; ++i)
                NthInputStorage(i)->FreeIfNeeded(allocator);

            // Perform realloc.
            void* newPtr = Utility.ReAlloc(
                m_Storage.Ptr,
                m_Storage.Size * UnsafeUtility.SizeOf<DataInputStorage>(),
                SimpleType.Create<DataInputStorage>(newSize),
                allocator
            );

            var preservedSize = Math.Min(newSize, m_Storage.Size);
            m_Storage = new PortArrayStorage(newPtr, newSize, m_Storage.PortID);

            // Point newly added DataInputs to the blank page here so that
            // we don't need to ComputeValueChunkAndPatchPorts on PortArray resize
            for (ushort i = preservedSize; i < newSize; ++i)
                *NthInputStorage(i) = new DataInputStorage(blankPage);
        }

        public void Free(Allocator allocator) => Resize(0, null, allocator);
        public DataInputStorage* NthInputStorage(ushort i) => (DataInputStorage*)m_Storage.Ptr + i;
        public ushort Size => m_Storage.Size;
    }

    /// <summary>
    /// A view on a concrete instance of a <see cref="PortArray{TPort}"/> of <see cref="DataOutput{TDefinition,TType}"/>
    /// but without knowing the concrete TDefinition and TType types. Offers a reduced API on the PortArray corresponding
    /// to the operations that are safe to perform without the aforementioned missing information.
    /// </summary>
    unsafe ref struct UntypedDataOutputPortArray
    {
        PortArrayStorage m_Storage;

        public void Resize(RenderKernelFunction.BasePort* ports, in DataPortDeclarations dataPorts, in DataPortDeclarations.OutputDeclaration portDecl, in ValidatedHandle ownerNode, ushort newSize, Allocator allocator)
        {
            var previousSize = Size;

            if (portDecl.BufferCount > 0)
            {
                // Release any data buffer memory that is about to be lost due to downsizing.
                for (ushort j = newSize; j < previousSize; ++j)
                {
                    foreach (var bufferIndex in portDecl.BufferIndices)
                    {
                        dataPorts.GetAggregateOutputBuffer(ports, portDecl, bufferIndex, j).FreeIfNeeded(allocator);
                    }
                }
            }

            ResizeMem(newSize, portDecl.Type, allocator);

            if (portDecl.BufferCount > 0)
            {
                // Assign owner IDs to new data output buffers
                for (ushort j = previousSize; j < Size; ++j)
                {
                    foreach (var bufferIndex in portDecl.BufferIndices)
                    {
                        dataPorts.GetAggregateOutputBuffer(ports, portDecl, bufferIndex, j) =
                            new BufferDescription(null, 0, ownerNode);
                    }
                }
            }
        }

        public void Free(RenderKernelFunction.BasePort* ports, in DataPortDeclarations dataPorts, in DataPortDeclarations.OutputDeclaration portDecl, Allocator allocator) =>
            Resize(ports, dataPorts, portDecl, default, 0, allocator);

        public ushort Size => m_Storage.Size;

        public void* Get(SimpleType elementSize, ushort i) => (byte*)m_Storage.Ptr + i * elementSize.Size;

        void ResizeMem(ushort newSize, SimpleType elementSize, Allocator allocator)
        {
#if DFG_ASSERTIONS
            if (newSize == PortArrayStorage.MaxSize)
                throw new AssertionException("Requested array size is too large");

            if (elementSize.Align % DataInputStorage.MinimumInputAlignment != 0)
                throw new AssertionException("Unexpected alignment in PortArray element type");
#endif

            if (newSize == m_Storage.Size)
                return;

            // Perform realloc.
            var newPtr = Utility.ReAlloc(
                m_Storage.Ptr,
                m_Storage.Size *
                elementSize.Size,
                new SimpleType(elementSize.Size * newSize, elementSize.Align),
                allocator
            );

            m_Storage = new PortArrayStorage(newPtr, newSize, m_Storage.PortID);
        }
    }

    struct ArraySizeEntryHandle
    {
        public static ArraySizeEntryHandle Invalid => new ArraySizeEntryHandle { Index = 0 };
        public int Index;

        public static implicit operator ArraySizeEntryHandle(int arg)
        {
            return new ArraySizeEntryHandle { Index = arg };
        }

        public static implicit operator int(ArraySizeEntryHandle handle)
        {
            return handle.Index;
        }
    }

    struct ArraySizeEntry
    {
        public ushort Value;
        public InputOutputPortID Port;
        public ArraySizeEntryHandle Next;
    }

    public partial class NodeSetAPI
    {
        FreeList<ArraySizeEntry> m_ArraySizes = new FreeList<ArraySizeEntry>(Allocator.Persistent);

        /// <summary>
        /// Set the size of an array of input ports.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the given port is not a <see cref="PortArray"/>, if downsizing the array would invalidate existing
        /// connections, or if the given size exceeds <see cref="PortArray.MaxSize"/>
        /// </exception>
        public void SetPortArraySize(NodeHandle handle, InputPortID portArray, int size)
        {
            var destination = new InputPair(this, handle, new InputPortArrayID(portArray));
            var destPortDef = GetFormalPort(destination);

            if (!destPortDef.IsPortArray)
                throw new InvalidOperationException("Cannot set port array size on a port that is not an array.");

            if (destPortDef.Category == PortDescription.Category.Data)
                SetArraySize_OnValidatedDataPort(destination, size);
            else
                SetArraySize_OnValidatedPort(destination, size);
        }

        /// <summary>
        /// Set the size of an array of output ports.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the given port is not a <see cref="PortArray"/>, if downsizing the array would invalidate existing
        /// connections, or if the given size exceeds <see cref="PortArray.MaxSize"/>
        /// </exception>
        public void SetPortArraySize(NodeHandle handle, OutputPortID portArray, int size)
        {
            var destination = new OutputPair(this, handle, new OutputPortArrayID(portArray));
            var destPortDef = GetFormalPort(destination);

            if (!destPortDef.IsPortArray)
                throw new InvalidOperationException("Cannot set port array size on a port that is not an array.");

            if (destPortDef.Category == PortDescription.Category.Data)
                SetArraySize_OnValidatedDataPort(destination, size);
            else
                SetArraySize_OnValidatedPort(destination, size);
        }

        /// <summary>
        /// Set the size of an array of input data ports.
        /// </summary>
        /// <param name="handle">Node on which to set the size of the array of ports</param>
        /// <param name="portArray">Data port array to be modified</param>
        /// <param name="size">Desired array size</param>
        /// <exception cref="InvalidOperationException">
        /// If downsizing the array would invalidate existing connections, or if the given size exceeds <see cref="PortArray.MaxSize"/>
        /// </exception>
        public void SetPortArraySize<TDefinition, TType>(
            NodeHandle<TDefinition> handle,
            PortArray<DataInput<TDefinition, TType>> portArray,
            int size
        )
            where TDefinition : NodeDefinition
            where TType : struct
        {
            var destination = new InputPair(this, handle, new InputPortArrayID(portArray.GetPortID()));

            SetArraySize_OnValidatedDataPort(destination, size);
        }

        /// <summary>
        /// Set the size of an array of output data ports.
        /// </summary>
        /// <param name="handle">Node on which to set the size of the array of ports</param>
        /// <param name="portArray">Data port array to be modified</param>
        /// <param name="size">Desired array size</param>
        /// <exception cref="InvalidOperationException">
        /// If downsizing the array would invalidate existing connections, or if the given size exceeds <see cref="PortArray.MaxSize"/>
        /// </exception>
        public void SetPortArraySize<TDefinition, TType>(
            NodeHandle<TDefinition> handle,
            PortArray<DataOutput<TDefinition, TType>> portArray,
            int size
        )
            where TDefinition : NodeDefinition
            where TType : struct
        {
            var destination = new OutputPair(this, handle, new OutputPortArrayID(portArray.GetPortID()));

            SetArraySize_OnValidatedDataPort(destination, size);
        }

        /// <summary>
        /// Set the size of an array of input message ports.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// If downsizing the array would invalidate existing connections, or if the given size exceeds <see cref="PortArray.MaxSize"/>
        /// </exception>
        public void SetPortArraySize<TDefinition, TMsg>(
            NodeHandle<TDefinition> handle,
            PortArray<MessageInput<TDefinition, TMsg>> portArray,
            int size
        )
            where TDefinition : NodeDefinition
        {
            SetArraySize_OnValidatedPort(new InputPair(this, handle, new InputPortArrayID(portArray.GetPortID())), size);
        }

        /// <summary>
        /// Set the size of an array of output message ports.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// If downsizing the array would invalidate existing connections, or if the given size exceeds <see cref="PortArray.MaxSize"/>
        /// </exception>
        public void SetPortArraySize<TDefinition, TMsg>(
            NodeHandle<TDefinition> handle,
            PortArray<MessageOutput<TDefinition, TMsg>> portArray,
            int size
        )
            where TDefinition : NodeDefinition
        {
            SetArraySize_OnValidatedPort(new OutputPair(this, handle, new OutputPortArrayID(portArray.GetPortID())), size);
        }

        /// <summary>
        /// Inputs / outputs must be resolved
        /// </summary>
        bool PortArrayDownsizeWouldCauseDisconnection(ValidatedHandle handle, InputOutputPortID portID, ushort newSize)
        {
            if (portID.IsInput)
            {
                for (var it = m_Topology[handle].InputHeadConnection; it != FlatTopologyMap.InvalidConnection; it = m_Database[it].NextOutputConnection)
                {
                    ref readonly var connection = ref m_Database[it];

                    if (connection.DestinationInputPort.PortID == portID.InputPort &&
                        connection.DestinationInputPort.ArrayIndex >= newSize)
                        return true;
                }
            }
            else
            {
                for (var it = m_Topology[handle].OutputHeadConnection; it != FlatTopologyMap.InvalidConnection; it = m_Database[it].NextInputConnection)
                {
                    ref readonly var connection = ref m_Database[it];

                    if (connection.SourceOutputPort.PortID == portID.OutputPort &&
                        connection.SourceOutputPort.ArrayIndex >= newSize)
                        return true;
                }
            }

            return false;
        }

        void SetArraySize_OnValidatedPort(in InputPair portArray, int value)
            => SetArraySize_OnValidatedPort(portArray.Handle, new InputOutputPortID(portArray.Port.PortID), value);

        void SetArraySize_OnValidatedPort(in OutputPair portArray, int value)
            => SetArraySize_OnValidatedPort(portArray.Handle, new InputOutputPortID(portArray.Port.PortID), value);

        void SetArraySize_OnValidatedDataPort(in InputPair portArray, int value)
        {
            SetArraySize_OnValidatedPort(portArray.Handle, new InputOutputPortID(portArray.Port.PortID), value);
            m_Diff.PortArrayResized(portArray, (ushort)value);
        }

        void SetArraySize_OnValidatedDataPort(in OutputPair portArray, int value)
        {
            SetArraySize_OnValidatedPort(portArray.Handle, new InputOutputPortID(portArray.Port.PortID), value);
            m_Diff.PortArrayResized(portArray, (ushort)value);
        }

        void SetArraySize_OnValidatedPort(ValidatedHandle handle, InputOutputPortID portID, int value)
        {
            if ((uint)value >= PortArrayStorage.MaxSize)
                throw new ArgumentException("Requested array size is too large");

            ushort ushortValue = (ushort)value;

            ref ArraySizeEntryHandle arraySizeHead = ref Nodes[handle].PortArraySizesHead;

            for (ArraySizeEntryHandle i = arraySizeHead, prev = ArraySizeEntryHandle.Invalid; i != ArraySizeEntryHandle.Invalid; prev = i, i = m_ArraySizes[i].Next)
            {
                if (m_ArraySizes[i].Port != portID)
                    continue;

                if (m_ArraySizes[i].Value > value && PortArrayDownsizeWouldCauseDisconnection(handle, portID, ushortValue))
                    throw new InvalidOperationException("Port array downsize would affect active connections");

                if (value > 0)
                {
                    m_ArraySizes[i].Value = ushortValue;
                }
                else
                {
                    if (prev != ArraySizeEntryHandle.Invalid)
                        m_ArraySizes[prev].Next = m_ArraySizes[i].Next;
                    else
                        arraySizeHead = m_ArraySizes[i].Next;
                    m_ArraySizes.Release(i);
                }
            }

            if (value != 0)
            {
                // Optimization opportunity: Rather than naively add new entries to the end of this singly-linked list, we
                // could insert them in increasing Port index order subsequently making it faster to search for a particular
                // entry in subsequent operations.
                int newEntry = m_ArraySizes.Allocate();
                m_ArraySizes[newEntry].Next = arraySizeHead;
                m_ArraySizes[newEntry].Value = ushortValue;
                m_ArraySizes[newEntry].Port = portID;
                arraySizeHead = newEntry;
            }
        }

        void CleanupPortArraySizes(ref InternalNodeData node)
        {
            for (var i = node.PortArraySizesHead; i != ArraySizeEntryHandle.Invalid; i = m_ArraySizes[i].Next)
                m_ArraySizes.Release(i);
            node.PortArraySizesHead = ArraySizeEntryHandle.Invalid;
        }

        internal void CheckPortArrayBounds(in InputPair portArray)
        {
            if (!portArray.Port.IsArray)
                return;
            CheckPortArrayBounds(portArray.Handle, new InputOutputPortID(portArray.Port.PortID), portArray.Port.ArrayIndex);
        }

        internal void CheckPortArrayBounds(in OutputPair portArray)
        {
            if (!portArray.Port.IsArray)
                return;
            CheckPortArrayBounds(portArray.Handle, new InputOutputPortID(portArray.Port.PortID), portArray.Port.ArrayIndex);
        }

        void CheckPortArrayBounds(ValidatedHandle handle, InputOutputPortID portID, ushort arrayIndex)
        {
            for (var i = Nodes[handle].PortArraySizesHead; i != ArraySizeEntryHandle.Invalid; i = m_ArraySizes[i].Next)
            {
                if (m_ArraySizes[i].Port != portID)
                    continue;

                if (arrayIndex >= m_ArraySizes[i].Value)
                    throw new IndexOutOfRangeException($"Port array index {arrayIndex} was out of bounds, array only has {m_ArraySizes[i].Value} indices");

                return;
            }

            throw new IndexOutOfRangeException($"Port array index {arrayIndex} was out of bounds, array only has 0 indices");
        }

        internal FreeList<ArraySizeEntry> GetArraySizesTable() => m_ArraySizes;
    }
}
