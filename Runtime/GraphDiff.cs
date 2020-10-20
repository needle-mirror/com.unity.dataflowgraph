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
            Create, Destroy, ResizeBuffer, ResizePortArray, MessageToData, GraphValueCreated,
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
            public ValidatedHandle Handle;
            public OutputPortArrayID Port;
            /// <summary>
            /// Local offset from start of the port.
            /// </summary>
            public int LocalBufferOffset;
            public int NewSize;
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
                set => m_ItemType = value;
            }

            SimpleType m_ItemType;

            /// <summary>
            /// If this field isn't null, adopt this memory instead of using <see cref="ItemType"/> to allocate memory
            /// </summary>
            public void* PotentialMemory;
        }

        public struct PortArrayResizedTuple
        {
            public InputPair Destination;
            public ushort NewSize;
        }

        unsafe public struct DataPortMessageTuple
        {
            public InputPair Destination;
            // optional message: null indicates that the port should retain its current value
            public void* msg;
        }

        public BlitList<CommandTuple> Commands;
        public BlitList<ValidatedHandle> CreatedNodes;
        public BlitList<DeletedTuple> DeletedNodes;
        public BlitList<BufferResizedTuple> ResizedDataBuffers;
        public BlitList<PortArrayResizedTuple> ResizedPortArrays;
        public BlitList<DataPortMessageTuple> MessagesArrivingAtDataPorts;
        public BlitList<ValidatedHandle> CreatedGraphValues;
        public BlitList<Adjacency> CreatedConnections;
        public BlitList<Adjacency> DeletedConnections;

        //public BlitList<> DirtyKernelDatas;


        public GraphDiff(Allocator allocator)
        {
            CreatedNodes = new BlitList<ValidatedHandle>(0, allocator);
            DeletedNodes = new BlitList<DeletedTuple>(0, allocator);
            Commands = new BlitList<CommandTuple>(0, allocator);
            ResizedDataBuffers = new BlitList<BufferResizedTuple>(0, allocator);
            ResizedPortArrays = new BlitList<PortArrayResizedTuple>(0, allocator);
            MessagesArrivingAtDataPorts = new BlitList<DataPortMessageTuple>(0, allocator);
            CreatedGraphValues = new BlitList<ValidatedHandle>(0, allocator);
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

        public void NodeBufferResized(in OutputPair target, int bufferOffset, int size, SimpleType itemType)
        {
            Commands.Add(new CommandTuple { command = Command.ResizeBuffer, ContainerIndex = ResizedDataBuffers.Count });
            ResizedDataBuffers.Add(new BufferResizedTuple { Handle = target.Handle, Port = target.Port, LocalBufferOffset = bufferOffset, NewSize = size, ItemType = itemType });
        }

        public void KernelBufferResized(in ValidatedHandle target, int bufferOffset, int size, SimpleType itemType)
        {
            Commands.Add(new CommandTuple { command = Command.ResizeBuffer, ContainerIndex = ResizedDataBuffers.Count });
            ResizedDataBuffers.Add(new BufferResizedTuple { Handle = target, Port = default, LocalBufferOffset = bufferOffset, NewSize = size, ItemType = itemType });
        }

        public unsafe void KernelBufferUpdated(in ValidatedHandle target, int bufferOffset, int size, void* memory)
        {
            Commands.Add(new CommandTuple { command = Command.ResizeBuffer, ContainerIndex = ResizedDataBuffers.Count });
            ResizedDataBuffers.Add(new BufferResizedTuple { Handle = target, Port = default, LocalBufferOffset = bufferOffset, NewSize = size, PotentialMemory = memory });
        }

        public void PortArrayResized(in InputPair dest, ushort size)
        {
            Commands.Add(new CommandTuple { command = Command.ResizePortArray, ContainerIndex = ResizedPortArrays.Count });
            ResizedPortArrays.Add(new PortArrayResizedTuple { Destination = dest, NewSize = size });
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

        public void GraphValueCreated(ValidatedHandle handle)
        {
            Commands.Add(new CommandTuple { command = Command.GraphValueCreated, ContainerIndex = CreatedGraphValues.Count });
            CreatedGraphValues.Add(handle);
        }

        public void Dispose()
        {
            CreatedNodes.Dispose();
            DeletedNodes.Dispose();
            Commands.Dispose();
            ResizedDataBuffers.Dispose();
            ResizedPortArrays.Dispose();
            MessagesArrivingAtDataPorts.Dispose();
            CreatedConnections.Dispose();
            CreatedGraphValues.Dispose();
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
