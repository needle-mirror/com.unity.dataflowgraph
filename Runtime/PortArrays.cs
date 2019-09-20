using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.DataFlowGraph
{
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
        internal const UInt16 MaxSize = InputPortArrayID.NonArraySentinel;

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

        internal static unsafe void Resize<TDefinition, TType>(ref PortArray<DataInput<TDefinition, TType>> portArray, ushort newSize, void* blankPage, Allocator allocator)
            where TDefinition : INodeDefinition
            where TType : struct
        {
            if (newSize == MaxSize)
                throw new ArgumentException("Requested array size is too large");

            if (newSize == portArray.Size)
                return;

            // Release any owned memory if downsizing.
            for (int i = newSize; i < portArray.Size; ++i)
            {
                var inputPortPatch = (void**)((byte*)portArray.Ptr + i * Unsafe.SizeOf<DataInput<TDefinition, TType>>());
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

        internal static unsafe void Free<TDefinition, TType>(ref PortArray<DataInput<TDefinition, TType>> portArray, Allocator allocator)
            where TDefinition : INodeDefinition
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
        /// <exception cref="InvalidOperationException">Thrown if the given port is not a <see cref="PortArray"/>, or, if downsizing the array would invalidate existing connections</exception>
        public void SetPortArraySize(NodeHandle handle, InputPortID portArray, ushort size)
        {
            var destPortDef = GetFunctionality(handle).GetPortDescription(handle).Inputs[portArray.Port];

            if (!destPortDef.IsPortArray)
                throw new InvalidOperationException("Cannot set port array size on a port that's not an array.");

            ResolvePort_AndSetArraySize_OnValidatedPort(ref handle, ref portArray, size);

            if (destPortDef.PortUsage == Usage.Data)
                m_Diff.PortArrayResized(handle, portArray, size);
        }

        /// <summary>
        /// Set the size of an array of data ports.
        /// </summary>
        /// <param name="handle">Node on which to set the size of the array of ports</param>
        /// <param name="portArray">Data port array to be modified</param>
        /// <param name="size">Desired array size</param>
        /// <exception cref="InvalidOperationException">Thrown if downsizing the array would invalidate existing connections</exception>
        public void SetPortArraySize<TDefinition, TType>(
            NodeHandle<TDefinition> handle,
            PortArray<DataInput<TDefinition, TType>> portArray,
            ushort size
        )
            where TDefinition : INodeDefinition
            where TType : struct
        {
            NodeVersionCheck(handle.VHandle);

            var resolvedHandle = (NodeHandle)handle;
            var resolvedPort = (InputPortID)portArray;
            ResolvePort_AndSetArraySize_OnValidatedPort(ref resolvedHandle, ref resolvedPort, size);

            m_Diff.PortArrayResized(resolvedHandle, resolvedPort, size);
        }

        /// <summary>
        /// Set the size of an array of message ports.
        /// </summary>
        /// <param name="handle">Node on which to set the size of the array of ports</param>
        /// <param name="portArray">Message port array to be modified</param>
        /// <param name="size">Desired array size</param>
        /// <exception cref="InvalidOperationException">Thrown if downsizing the array would invalidate existing connections</exception>
        public void SetPortArraySize<TDefinition, TMsg>(
            NodeHandle<TDefinition> handle,
            PortArray<MessageInput<TDefinition, TMsg>> portArray,
            ushort size
        )
            where TDefinition : INodeDefinition, IMsgHandler<TMsg>
        {
            NodeVersionCheck(handle.VHandle);

            var resolvedHandle = (NodeHandle)handle;
            var resolvedPort = (InputPortID)portArray;
            ResolvePort_AndSetArraySize_OnValidatedPort(ref resolvedHandle, ref resolvedPort, size);
        }

        /// <summary>
        /// Inputs must be resolved
        /// </summary>
        bool PortArrayDownsizeWouldCauseDisconnection(NodeHandle handle, InputPortID port, ushort newSize)
        {
            for (var it = m_Topology.Indexes[handle.VHandle.Index].InputHeadConnection; it != TopologyDatabase.InvalidConnection; it = m_Topology.Connections[it].NextOutputConnection)
            {
                ref var connection = ref m_Topology.Connections[it];

                if (connection.DestinationInputPort.PortID == port &&
                    connection.DestinationInputPort.ArrayIndex >= newSize)
                    return true;
            }

            return false;
        }

        void ResolvePort_AndSetArraySize_OnValidatedPort(ref NodeHandle handle, ref InputPortID port, ushort value)
        {
            InputPortArrayID resolvedPort = new InputPortArrayID(port);
            ResolvePublicDestination(ref handle, ref resolvedPort);
            port = resolvedPort.PortID;

            ref ArraySizeEntryHandle arraySizeHead = ref m_Nodes[handle.VHandle.Index].PortArraySizesHead;

            for (ArraySizeEntryHandle i = arraySizeHead, prev = ArraySizeEntryHandle.Invalid; i != ArraySizeEntryHandle.Invalid; prev = i, i = m_ArraySizes[i].Next)
            {
                if (m_ArraySizes[i].Port != port)
                    continue;

                if (m_ArraySizes[i].Value > value && PortArrayDownsizeWouldCauseDisconnection(handle, port, value))
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
            m_ArraySizes[newEntry].Port = port;
            arraySizeHead = newEntry;
        }

        void CleanupPortArraySizes(ref InternalNodeData node)
        {
            for (var i = node.PortArraySizesHead; i != ArraySizeEntryHandle.Invalid; i = m_ArraySizes[i].Next)
                m_ArraySizes.Release(i);
            node.PortArraySizesHead = ArraySizeEntryHandle.Invalid;
        }

        /// <summary>
        /// Inputs are assumed to be resolved.
        /// </summary>
        internal ushort GetPortArraySize_Unchecked(NodeHandle handle, InputPortID portArray)
        {
            for (var i = m_Nodes[handle.VHandle.Index].PortArraySizesHead; i != ArraySizeEntryHandle.Invalid; i = m_ArraySizes[i].Next)
            {
                if (m_ArraySizes[i].Port == portArray)
                {
                    return m_ArraySizes[i].Value;
                }
            }

            return 0;
        }

        internal FreeList<ArraySizeEntry> GetArraySizesTable() => m_ArraySizes;
    }
}
