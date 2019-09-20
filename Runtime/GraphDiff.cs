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
            public NodeHandle Handle;
            public int Class;
        }

        public struct BufferResizedTuple
        {
            public NodeHandle handle;
            public OutputPortID port;
            public int BufferOffset;
            public int NewSize;
            public SimpleType ItemType;
        }

        public struct PortArrayResizedTuple
        {
            public NodeHandle handle;
            public InputPortID port;
            public ushort NewSize;
        }

        unsafe public struct DataPortMessageTuple
        {
            public NodeHandle handle;
            public InputPortArrayID port;
            // optional message: null indicates that the port should retain its current value
            public void* msg;
        }

        public BlitList<CommandTuple> Commands;
        public BlitList<NodeHandle> CreatedNodes;
        public BlitList<DeletedTuple> DeletedNodes;
        public BlitList<BufferResizedTuple> ResizedDataBuffers;
        public BlitList<PortArrayResizedTuple> ResizedPortArrays;
        public BlitList<DataPortMessageTuple> MessagesArrivingAtDataPorts;

        //public BlitList<> DirtyKernelDatas;
        //public BlitList<> CreatedConnections;
        //public BlitList<> DeletedConnections;

        public GraphDiff(Allocator allocator)
        {
            CreatedNodes = new BlitList<NodeHandle>(0, allocator);
            DeletedNodes = new BlitList<DeletedTuple>(0, allocator);
            Commands = new BlitList<CommandTuple>(0, allocator);
            ResizedDataBuffers = new BlitList<BufferResizedTuple>(0, allocator);
            ResizedPortArrays = new BlitList<PortArrayResizedTuple>(0, allocator);
            MessagesArrivingAtDataPorts = new BlitList<DataPortMessageTuple>(0, allocator);
        }

        public void NodeCreated(NodeHandle handle)
        {
            Commands.Add(new CommandTuple { command = Command.Create, ContainerIndex = CreatedNodes.Count });
            CreatedNodes.Add(handle);
        }

        public void NodeDeleted(NodeHandle handle, int functionalityIndex)
        {
            Commands.Add(new CommandTuple { command = Command.Destroy, ContainerIndex = DeletedNodes.Count });
            DeletedNodes.Add(new DeletedTuple { Handle = handle, Class = functionalityIndex });
        }

        public void NodeBufferResized(NodeHandle handle, OutputPortID port, int bufferOffset, int size, SimpleType itemType)
        {
            Commands.Add(new CommandTuple { command = Command.ResizeBuffer, ContainerIndex = ResizedDataBuffers.Count });
            ResizedDataBuffers.Add(new BufferResizedTuple { handle = handle, port = port, BufferOffset = bufferOffset, NewSize = size, ItemType = itemType });
        }

        public void PortArrayResized(NodeHandle handle, InputPortID port, ushort size)
        {
            Commands.Add(new CommandTuple { command = Command.ResizePortArray, ContainerIndex = ResizedPortArrays.Count });
            ResizedPortArrays.Add(new PortArrayResizedTuple { handle = handle, port = port, NewSize = size });
        }

        public unsafe void SetData(NodeHandle handle, InputPortArrayID port, void* msg)
        {
            Commands.Add(new CommandTuple { command = Command.MessageToData, ContainerIndex = MessagesArrivingAtDataPorts.Count });
            MessagesArrivingAtDataPorts.Add(new DataPortMessageTuple { handle = handle, port = port, msg = msg });
        }

        public unsafe void RetainData(NodeHandle handle, InputPortArrayID port)
        {
            Commands.Add(new CommandTuple { command = Command.MessageToData, ContainerIndex = MessagesArrivingAtDataPorts.Count });
            MessagesArrivingAtDataPorts.Add(new DataPortMessageTuple { handle = handle, port = port, msg = null });
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
