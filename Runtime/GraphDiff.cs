using System;
using Unity.Collections;

namespace Unity.DataFlowGraph
{
    // TODO: This is not the most efficient structure of doing this.
    // Also for reference, this is doing the same job as EntityCommandBuffer or DSPGraph.CommandBuffer
    // TODO: Implement compression of the diff to avoid duplicating commands / creating nodes that are destroyed later in the same diff
    struct GraphDiff : IDisposable
    {
        public enum Command
        {
            Create, Destroy, ResizeBuffer, ResizePortArray, MessageToData
            //, DirtyKernel, CreatedConnection, DeletedConnection
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

        public struct BufferResizedTuple
        {
            public OutputPair Source;
            /// <summary>
            /// Local offset from start of the port.
            /// </summary>
            public int LocalBufferOffset;
            public int NewSize;
            public SimpleType ItemType;
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

        //public BlitList<> DirtyKernelDatas;
        //public BlitList<> CreatedConnections;
        //public BlitList<> DeletedConnections;

        public GraphDiff(Allocator allocator)
        {
            CreatedNodes = new BlitList<ValidatedHandle>(0, allocator);
            DeletedNodes = new BlitList<DeletedTuple>(0, allocator);
            Commands = new BlitList<CommandTuple>(0, allocator);
            ResizedDataBuffers = new BlitList<BufferResizedTuple>(0, allocator);
            ResizedPortArrays = new BlitList<PortArrayResizedTuple>(0, allocator);
            MessagesArrivingAtDataPorts = new BlitList<DataPortMessageTuple>(0, allocator);
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

        public void NodeBufferResized(in OutputPair source, int bufferOffset, int size, SimpleType itemType)
        {
            Commands.Add(new CommandTuple { command = Command.ResizeBuffer, ContainerIndex = ResizedDataBuffers.Count });
            ResizedDataBuffers.Add(new BufferResizedTuple { Source = source, LocalBufferOffset = bufferOffset, NewSize = size, ItemType = itemType });
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

        public unsafe void RetainData(in InputPair dest)
        {
            Commands.Add(new CommandTuple { command = Command.MessageToData, ContainerIndex = MessagesArrivingAtDataPorts.Count });
            MessagesArrivingAtDataPorts.Add(new DataPortMessageTuple { Destination = dest, msg = null });
        }

        public void Dispose()
        {
            CreatedNodes.Dispose();
            DeletedNodes.Dispose();
            Commands.Dispose();
            ResizedDataBuffers.Dispose();
            ResizedPortArrays.Dispose();
            MessagesArrivingAtDataPorts.Dispose();
        }
    }
}
