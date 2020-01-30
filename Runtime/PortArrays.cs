using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.DataFlowGraph
{
    using UntypedPortArray = PortArray<DataInput<InvalidDefinitionSlot, byte>>;

    public interface IIndexableInputPort { }

    /// <summary>
    /// Declaration of an array of ports (used within an <see cref="ISimulationPortDefinition"/> or <see cref="IKernelPortDefinition"/>).
    /// Used when a node requires an array of input ports with a size that can be changed dynamically.
    /// </summary>
    /// <typeparam name="TInputPort">Input port declaration (eg. <see cref="MessageInput{TDefinition, TMsg}"/> 
    /// or <see cref="DataInput{TDefinition, TType}"/></typeparam>
#pragma warning disable 660, 661  // We do not want Equals(object) nor GetHashCode()
    [DebuggerTypeProxy(typeof(PortArrayDebugView<>))]
    public readonly unsafe struct PortArray<TInputPort>
        where TInputPort : IIndexableInputPort
    {
        public const UInt16 MaxSize = InputPortArrayID.NonArraySentinel;

        internal readonly void* Ptr;
        internal readonly ushort Size;
        internal readonly InputPortID Port;

        public static bool operator ==(InputPortID left, PortArray<TInputPort> right)
        {
            return left == right.Port;
        }

        public static bool operator !=(InputPortID left, PortArray<TInputPort> right)
        {
            return !(left == right);
        }

        public static bool operator ==(PortArray<TInputPort> left, InputPortID right)
        {
            return right == left;
        }

        public static bool operator !=(PortArray<TInputPort> left, InputPortID right)
        {
            return !(left == right);
        }

        public static explicit operator InputPortID(PortArray<TInputPort> input)
        {
            return input.Port;
        }

        PortArray(void* ptr, ushort size, InputPortID port)
        {
            Ptr = ptr;
            Size = size;
            Port = port;
        }

        internal ref TInputPort this[ushort i]
            => ref Unsafe.AsRef<TInputPort>((byte*)Ptr + i * Unsafe.SizeOf<TInputPort>());

        internal void** NthInputPortPointer(ushort i)
            => (void**)((byte*)Ptr + i * Unsafe.SizeOf<TInputPort>());

        internal static void Resize<TDefinition, TType>(ref PortArray<DataInput<TDefinition, TType>> portArray, ushort newSize, void* blankPage, Allocator allocator)
            where TDefinition : NodeDefinition
            where TType : struct
        {
#if DFG_ASSERTIONS
            if (newSize == MaxSize)
                throw new AssertionException("Requested array size is too large");
#endif

            if (newSize == portArray.Size)
                return;

            // Release any owned memory if downsizing.
            for (ushort i = newSize; i < portArray.Size; ++i)
            {
                var inputPortPatch = portArray.NthInputPortPointer(i);
                ref var ownership = ref DataInputUtility.GetMemoryOwnership(inputPortPatch);
                if (ownership == DataInputUtility.Ownership.OwnedByPort)
                    UnsafeUtility.Free(*inputPortPatch, allocator);
            }

            // Perform realloc.
            void* newPtr = null;
            if (newSize > 0)
                newPtr = UnsafeUtility.Malloc(newSize * Unsafe.SizeOf<DataInput<TDefinition, TType>>(), 16, allocator);

            // Preserve old content if appropriate.
            var preserveSize = Math.Min(newSize, portArray.Size);
            if (preserveSize > 0)
                UnsafeUtility.MemCpy(newPtr, portArray.Ptr, preserveSize * Unsafe.SizeOf<DataInput<TDefinition, TType>>());

            if (portArray.Ptr != null)
                UnsafeUtility.Free(portArray.Ptr, allocator);

            portArray = new PortArray<DataInput<TDefinition, TType>>(newPtr, newSize, portArray.Port);

            // Point newly added DataInputs to the blank page here so that we don't need to ComputeValueChunkAndPatchPorts on PortArray resize
            for (ushort i = preserveSize; i < newSize; ++i)
                portArray[i] = new DataInput<TDefinition, TType>(blankPage, default);
        }

        internal static void Free<TDefinition, TType>(ref PortArray<DataInput<TDefinition, TType>> portArray, Allocator allocator)
            where TDefinition : NodeDefinition
            where TType : struct
        {
            Resize(ref portArray, 0, null, allocator);
        }
    }
#pragma warning restore 660, 661

    internal sealed class PortArrayDebugView<TInputPort>
        where TInputPort : IIndexableInputPort
    {
        private PortArray<TInputPort> m_PortArray;

        public PortArrayDebugView(PortArray<TInputPort> array)
        {
            m_PortArray = array;
        }

        public TInputPort[] Items
        {
            get
            {
                TInputPort[] ret = new TInputPort[m_PortArray.Size];

                for (ushort i = 0; i < m_PortArray.Size; ++i)
                    ret[i] = m_PortArray[i];

                return ret;
            }
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
        public InputPortID Port;
        public ArraySizeEntryHandle Next;
    }

    public partial class NodeSet
    {
        FreeList<ArraySizeEntry> m_ArraySizes = new FreeList<ArraySizeEntry>(Allocator.Persistent);

        /// <summary>
        /// Set the size of an array of ports.
        /// </summary>
        /// <param name="handle">Node on which to set the size of the array of ports</param>
        /// <param name="portArray">Port array to be modified</param>
        /// <param name="size">Desired array size</param>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the given port is not a <see cref="PortArray"/>, if downsizing the array would invalidate existing
        /// connections, or if the given size exceeds <see cref="PortArray.MaxSize"/>
        /// </exception>
        public void SetPortArraySize(NodeHandle handle, InputPortID portArray, int size)
        {
            var destination = new InputPair(this, handle, new InputPortArrayID(portArray));
            var destPortDef = GetFormalPort(destination);

            if (!destPortDef.IsPortArray)
                throw new InvalidOperationException("Cannot set port array size on a port that's not an array.");

            ushort sizeUshort = (ushort)size;
            SetArraySize_OnValidatedPort(destination, sizeUshort);

            if (destPortDef.Category == PortDescription.Category.Data)
                m_Diff.PortArrayResized(destination, sizeUshort);
        }

        /// <summary>
        /// Set the size of an array of data ports.
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
            var destination = new InputPair(this, handle, new InputPortArrayID(portArray.Port));

            ushort sizeUshort = (ushort)size;
            SetArraySize_OnValidatedPort(destination, sizeUshort);
            m_Diff.PortArrayResized(destination, sizeUshort);
        }

        /// <summary>
        /// Set the size of an array of message ports.
        /// </summary>
        /// <param name="handle">Node on which to set the size of the array of ports</param>
        /// <param name="portArray">Message port array to be modified</param>
        /// <param name="size">Desired array size</param>
        /// <exception cref="InvalidOperationException">
        /// If downsizing the array would invalidate existing connections, or if the given size exceeds <see cref="PortArray.MaxSize"/>
        /// </exception>
        public void SetPortArraySize<TDefinition, TMsg>(
            NodeHandle<TDefinition> handle,
            PortArray<MessageInput<TDefinition, TMsg>> portArray,
            int size
        )
            where TDefinition : NodeDefinition, IMsgHandler<TMsg>
        {
            SetArraySize_OnValidatedPort(new InputPair(this, handle, new InputPortArrayID(portArray.Port)), (ushort)size);
        }

        /// <summary>
        /// Inputs must be resolved
        /// </summary>
        bool PortArrayDownsizeWouldCauseDisconnection(in InputPair portArray, ushort newSize)
        {
            for (var it = m_Topology[portArray.Handle].InputHeadConnection; it != FlatTopologyMap.InvalidConnection; it = m_Database[it].NextOutputConnection)
            {
                ref readonly var connection = ref m_Database[it];

                if (connection.DestinationInputPort.PortID == portArray.Port.PortID &&
                    connection.DestinationInputPort.ArrayIndex >= newSize)
                    return true;
            }

            return false;
        }

        void SetArraySize_OnValidatedPort(in InputPair portArray, ushort value)
        {
            if (value >= UntypedPortArray.MaxSize)
                throw new ArgumentException("Requested array size is too large");

            ref ArraySizeEntryHandle arraySizeHead = ref GetNode(portArray.Handle).PortArraySizesHead;

            for (ArraySizeEntryHandle i = arraySizeHead, prev = ArraySizeEntryHandle.Invalid; i != ArraySizeEntryHandle.Invalid; prev = i, i = m_ArraySizes[i].Next)
            {
                if (m_ArraySizes[i].Port != portArray.Port.PortID)
                    continue;

                if (m_ArraySizes[i].Value > value && PortArrayDownsizeWouldCauseDisconnection(portArray, value))
                    throw new InvalidOperationException("Port array resize would affect active connections");

                if (value > 0)
                {
                    m_ArraySizes[i].Value = value;
                }
                else
                {
                    if (prev != ArraySizeEntryHandle.Invalid)
                        m_ArraySizes[prev].Next = m_ArraySizes[i].Next;
                    else
                        arraySizeHead = m_ArraySizes[i].Next;
                    m_ArraySizes.Release(i);
                }
                return;
            }

            if (value == 0)
                return;

            // Optimization opportunity: Rather than naively add new entries to the end of this singly-linked list, we
            // could insert them in increasing Port index order subsequently making it faster to search for a particular
            // entry in subsequent operations.
            int newEntry = m_ArraySizes.Allocate();
            m_ArraySizes[newEntry].Next = arraySizeHead;
            m_ArraySizes[newEntry].Value = value;
            m_ArraySizes[newEntry].Port = portArray.Port.PortID;
            arraySizeHead = newEntry;
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
            
            for (var i = GetNode(portArray.Handle).PortArraySizesHead; i != ArraySizeEntryHandle.Invalid; i = m_ArraySizes[i].Next)
            {
                if (m_ArraySizes[i].Port != portArray.Port.PortID)
                    continue;

                if (portArray.Port.ArrayIndex >= m_ArraySizes[i].Value)
                    throw new IndexOutOfRangeException($"Port array index {portArray.Port.ArrayIndex} was out of bounds, array only has {m_ArraySizes[i].Value} indices");

                return;
            }

            throw new IndexOutOfRangeException($"Port array index {portArray.Port.ArrayIndex} was out of bounds, array only has 0 indices");
        }

        internal bool ReportPortArrayBounds(in InputPair portArray)
        {
            if (!portArray.Port.IsArray)
                return true;

            for (var i = GetNode(portArray.Handle).PortArraySizesHead; i != ArraySizeEntryHandle.Invalid; i = m_ArraySizes[i].Next)
            {
                if (m_ArraySizes[i].Port != portArray.Port.PortID)
                    continue;

                if (portArray.Port.ArrayIndex >= m_ArraySizes[i].Value)
                {
                    UnityEngine.Debug.LogError($"Port array index {portArray.Port.ArrayIndex} was out of bounds, array only has {m_ArraySizes[i].Value} ports");
                    return false;
                }

                return true;
            }

            UnityEngine.Debug.LogError($"Port array index {portArray.Port.ArrayIndex} was out of bounds, array only has 0 ports");

            return false;
        }

        internal FreeList<ArraySizeEntry> GetArraySizesTable() => m_ArraySizes;
    }
}
