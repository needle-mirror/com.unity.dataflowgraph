using System;
using Unity.Collections;

namespace Unity.DataFlowGraph
{
    using Topology = TopologyAPI<ValidatedHandle, InputPortArrayID, OutputPortArrayID>;

    struct GraphDiff : IDisposable
    {
        public struct Adjacency
        {
            public ValidatedHandle Destination;
            public InputPortArrayID DestinationInputPort;

            public ValidatedHandle Source;
            public OutputPortArrayID SourceOutputPort;
            public uint TraversalFlags;

            public static implicit operator Adjacency(in Topology.Connection c)
            {
                Adjacency ret;

                ret.Destination = c.Destination;
                ret.DestinationInputPort = c.DestinationInputPort;
                ret.Source = c.Source;
                ret.SourceOutputPort = c.SourceOutputPort;
                ret.TraversalFlags = c.TraversalFlags;

                return ret;
            }
        }

        public enum Command
        {
            Create, Destroy, ResizeBuffer, ResizeInputPortArray, ResizeOutputPortArray, MessageToData, GraphValueChanged,
            CreatedConnection, DeletedConnection
            //, DirtyKernel,
        }

        public bool IsCreated => CreatedNodes.IsCreated && DeletedNodes.IsCreated;

        public struct CommandTuple
        {
            public Command command;
            public int ContainerIndex;
        }

        public struct DeletedTuple
        {
            public ValidatedHandle Handle;
            public int Class;
        }

        public unsafe struct BufferResizedTuple
        {
            public BufferResizedTuple(ValidatedHandle handle, OutputPortArrayID port, PortBufferIndex bufferIndex, int newSize, SimpleType itemType)
            {
#if DFG_ASSERTIONS
                if (port == default)
                    throw new AssertionException("Port buffer resizing requires a valid port ID");
#endif
                Handle = handle;
                m_Port = port;
                m_BufferIndex = bufferIndex.Value;
                NewSize = newSize;
                m_ItemType = itemType;
                PotentialMemory = null;
            }
            public BufferResizedTuple(ValidatedHandle handle, KernelBufferIndex bufferIndex, int newSize, SimpleType itemType)
            {
                Handle = handle;
                m_Port = default;
                m_BufferIndex = bufferIndex.Value;
                NewSize = newSize;
                m_ItemType = itemType;
                PotentialMemory = null;
            }
            public BufferResizedTuple(ValidatedHandle handle, KernelBufferIndex bufferIndex, int newSize, void* memory = null)
            {
                Handle = handle;
                m_Port = default;
                m_BufferIndex = bufferIndex.Value;
                NewSize = newSize;
                m_ItemType = default;
                PotentialMemory = memory;
            }

            public bool IsKernelResize => m_Port == default;
            public ValidatedHandle Handle { get; }
            public OutputPortArrayID Port
            {
                get
                {
#if DFG_ASSERTIONS
                    if (IsKernelResize)
                        throw new AssertionException("Requesting port ID for a kernel buffer resize command");
#endif
                    return m_Port;
                }
            }

            /// <summary>
            /// Local index of which buffer within the port is being targeted.
            /// </summary>
            public PortBufferIndex PortBufferIndex
            {
                get
                {
#if DFG_ASSERTIONS
                    if (IsKernelResize)
                        throw new AssertionException("Requesting port buffer index for a kernel buffer resize command");
#endif
                    return new PortBufferIndex(m_BufferIndex);
                }
            }

            /// <summary>
            /// Local index of which buffer within the kernel is being targeted.
            /// </summary>
            public KernelBufferIndex KernelBufferIndex
            {
                get
                {
#if DFG_ASSERTIONS
                    if (!IsKernelResize)
                        throw new AssertionException("Requesting kernel buffer index for a port buffer resize command");
#endif
                    return new KernelBufferIndex(m_BufferIndex);
                }
            }

            public int NewSize { get; }
            public SimpleType ItemType
            {
                get
                {
#if DFG_ASSERTIONS
                    if (PotentialMemory != null)
                        throw new AssertionException("Memory should be adopted");
#endif
                    return m_ItemType;
                }
            }

            OutputPortArrayID m_Port;
            SimpleType m_ItemType;
            ushort m_BufferIndex;

            /// <summary>
            /// If this field isn't null, adopt this memory instead of using <see cref="ItemType"/> to allocate memory
            /// </summary>
            public void* PotentialMemory { get; }
        }

        public struct InputPortArrayResizedTuple
        {
            public InputPair Destination;
            public ushort NewSize;
        }

        public struct OutputPortArrayResizedTuple
        {
            public OutputPair Destination;
            public ushort NewSize;
        }

        unsafe public struct DataPortMessageTuple
        {
            public InputPair Destination;
            // optional message: null indicates that the port should retain its current value
            public void* msg;
        }

        public readonly struct GraphValueObservationTuple
        {
            public readonly bool IsCreation;
            public readonly ValidatedHandle SourceNode;

            public static GraphValueObservationTuple Created(in DataOutputValue value)
                => new GraphValueObservationTuple(value.Source, true);

            public static GraphValueObservationTuple Destroyed(in DataOutputValue value)
                => new GraphValueObservationTuple(value.Source, false);

            private GraphValueObservationTuple(ValidatedHandle node, bool isCreation)
            {
                IsCreation = isCreation;
                SourceNode = node;
            }
        }

        public BlitList<CommandTuple> Commands;
        public BlitList<ValidatedHandle> CreatedNodes;
        public BlitList<DeletedTuple> DeletedNodes;
        public BlitList<BufferResizedTuple> ResizedDataBuffers;
        public BlitList<InputPortArrayResizedTuple> ResizedInputPortArrays;
        public BlitList<OutputPortArrayResizedTuple> ResizedOutputPortArrays;
        public BlitList<DataPortMessageTuple> MessagesArrivingAtDataPorts;
        public BlitList<GraphValueObservationTuple> ChangedGraphValues;
        public BlitList<Adjacency> CreatedConnections;
        public BlitList<Adjacency> DeletedConnections;

        //public BlitList<> DirtyKernelDatas;


        public GraphDiff(Allocator allocator)
        {
            CreatedNodes = new BlitList<ValidatedHandle>(0, allocator);
            DeletedNodes = new BlitList<DeletedTuple>(0, allocator);
            Commands = new BlitList<CommandTuple>(0, allocator);
            ResizedDataBuffers = new BlitList<BufferResizedTuple>(0, allocator);
            ResizedInputPortArrays = new BlitList<InputPortArrayResizedTuple>(0, allocator);
            ResizedOutputPortArrays = new BlitList<OutputPortArrayResizedTuple>(0, allocator);
            MessagesArrivingAtDataPorts = new BlitList<DataPortMessageTuple>(0, allocator);
            ChangedGraphValues = new BlitList<GraphValueObservationTuple>(0, allocator);
            CreatedConnections = new BlitList<Adjacency>(0, allocator);
            DeletedConnections = new BlitList<Adjacency>(0, allocator);
        }

        public void NodeCreated(ValidatedHandle handle)
        {
            Commands.Add(new CommandTuple { command = Command.Create, ContainerIndex = CreatedNodes.Count });
            CreatedNodes.Add(handle);
        }

        public void NodeDeleted(ValidatedHandle handle, int definitionIndex)
        {
            Commands.Add(new CommandTuple { command = Command.Destroy, ContainerIndex = DeletedNodes.Count });
            DeletedNodes.Add(new DeletedTuple { Handle = handle, Class = definitionIndex });
        }

        public void NodeBufferResized(in OutputPair target, PortBufferIndex bufferIndex, int size, SimpleType itemType)
        {
            Commands.Add(new CommandTuple { command = Command.ResizeBuffer, ContainerIndex = ResizedDataBuffers.Count });
            ResizedDataBuffers.Add(new BufferResizedTuple(target.Handle, target.Port, bufferIndex, size, itemType));
        }

        public void KernelBufferResized(in ValidatedHandle target, KernelBufferIndex bufferIndex, int size, SimpleType itemType)
        {
            Commands.Add(new CommandTuple { command = Command.ResizeBuffer, ContainerIndex = ResizedDataBuffers.Count });
            ResizedDataBuffers.Add(new BufferResizedTuple(target, bufferIndex, size, itemType));
        }

        public unsafe void KernelBufferUpdated(in ValidatedHandle target, KernelBufferIndex bufferIndex, int size, void* memory)
        {
            Commands.Add(new CommandTuple { command = Command.ResizeBuffer, ContainerIndex = ResizedDataBuffers.Count });
            ResizedDataBuffers.Add(new BufferResizedTuple(target, bufferIndex, size, memory));
        }

        public void PortArrayResized(in InputPair dest, ushort size)
        {
            Commands.Add(new CommandTuple { command = Command.ResizeInputPortArray, ContainerIndex = ResizedInputPortArrays.Count });
            ResizedInputPortArrays.Add(new InputPortArrayResizedTuple { Destination = dest, NewSize = size });
        }

        public void PortArrayResized(in OutputPair dest, ushort size)
        {
            Commands.Add(new CommandTuple { command = Command.ResizeOutputPortArray, ContainerIndex = ResizedOutputPortArrays.Count });
            ResizedOutputPortArrays.Add(new OutputPortArrayResizedTuple { Destination = dest, NewSize = size });
        }

        public unsafe void SetData(in InputPair dest, void* msg)
        {
            Commands.Add(new CommandTuple { command = Command.MessageToData, ContainerIndex = MessagesArrivingAtDataPorts.Count });
            MessagesArrivingAtDataPorts.Add(new DataPortMessageTuple { Destination = dest, msg = msg });
        }

        public void RetainData(in InputPair dest)
        {
            Commands.Add(new CommandTuple { command = Command.MessageToData, ContainerIndex = MessagesArrivingAtDataPorts.Count });
            MessagesArrivingAtDataPorts.Add(new DataPortMessageTuple { Destination = dest, msg = null });
        }

        public void GraphValueCreated(in DataOutputValue value)
        {
            Commands.Add(new CommandTuple { command = Command.GraphValueChanged, ContainerIndex = ChangedGraphValues.Count });
            ChangedGraphValues.Add(GraphValueObservationTuple.Created(value));
        }

        public void GraphValueDestroyed(in DataOutputValue value)
        {
            Commands.Add(new CommandTuple { command = Command.GraphValueChanged, ContainerIndex = ChangedGraphValues.Count });
            ChangedGraphValues.Add(GraphValueObservationTuple.Destroyed(value));
        }

        public void Dispose()
        {
            CreatedNodes.Dispose();
            DeletedNodes.Dispose();
            Commands.Dispose();
            ResizedDataBuffers.Dispose();
            ResizedInputPortArrays.Dispose();
            ResizedOutputPortArrays.Dispose();
            MessagesArrivingAtDataPorts.Dispose();
            CreatedConnections.Dispose();
            ChangedGraphValues.Dispose();
            DeletedConnections.Dispose();
        }

        internal void DisconnectData(in Topology.Connection connection)
        {
#if DFG_ASSERTIONS
            if ((connection.TraversalFlags & PortDescription.k_MaskForAnyData) == 0)
                throw new AssertionException("Non-data disconnection transferred over graphdiff");
#endif

            Commands.Add(new CommandTuple { command = Command.DeletedConnection, ContainerIndex = DeletedConnections.Count });
            DeletedConnections.Add(connection);
        }

        internal void ConnectData(in Topology.Connection connection)
        {
#if DFG_ASSERTIONS
            if ((connection.TraversalFlags & PortDescription.k_MaskForAnyData) == 0)
                throw new AssertionException("Non-data connection transferred over graphdiff");
#endif

            Commands.Add(new CommandTuple { command = Command.CreatedConnection, ContainerIndex = CreatedConnections.Count });
            CreatedConnections.Add(connection);
        }
    }
}
