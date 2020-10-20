using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.DataFlowGraph
{
    using Topology = TopologyAPI<ValidatedHandle, InputPortArrayID, OutputPortArrayID>;

    /// <summary>
    /// A context provided to a node's <see cref="NodeDefinition.OnMessage"/> implementation which is invoked when a
    /// node receives a message on one of their MessageInputs.
    /// </summary>
    public readonly partial struct MessageContext
    {
        readonly CommonContext m_Ctx;
        readonly InputPortArrayID m_Port;

        /// <summary>
        /// A handle to the node receiving a message.
        /// </summary>
        public NodeHandle Handle => m_Ctx.Handle;

        /// <summary>
        /// The <see cref="NodeSetAPI"/> associated with this context.
        /// </summary>
        public NodeSetAPI Set => m_Ctx.Set;

        /// <summary>
        /// The port ID of the <see cref="MessageInput{TDefinition, TMsg}"/> on which the message is being received.
        /// </summary>
        public InputPortID Port => m_Port.PortID;

        /// <summary>
        /// If the above port ID corresponds to a <see cref="PortArray{TInputPort}"/>, this is the array index on which the message
        /// is being received.
        /// </summary>
        public ushort ArrayIndex
        {
            get
            {
                if (!m_Port.IsArray)
                    throw new InvalidOperationException("Trying to access index array for a non array PortID.");

                return m_Port.ArrayIndex;
            }
        }

        /// <summary>
        /// Conversion operator for common API shared with other contexts.
        /// </summary>
        public static implicit operator CommonContext(in MessageContext ctx) => ctx.m_Ctx;

        /// <summary>
        /// Emit a message from yourself on a port. Everything connected to it will receive your message.
        /// </summary>
        public void EmitMessage<T, TNodeDefinition>(MessageOutput<TNodeDefinition, T> port, in T msg)
            where TNodeDefinition : NodeDefinition
        {
            Set.EmitMessage(InternalHandle, new OutputPortArrayID(port.Port), msg);
        }

        /// <summary>
        /// Emit a message from yourself on a port array. Everything connected to it will receive your message.
        /// </summary>
        /// <exception cref="IndexOutOfRangeException">Thrown if the index is out of range with respect to the port array.</exception>
        public void EmitMessage<T, TNodeDefinition>(PortArray<MessageOutput<TNodeDefinition, T>> port, int arrayIndex, in T msg)
            where TNodeDefinition : NodeDefinition
        {
            Set.EmitMessage(InternalHandle, new OutputPortArrayID(port.GetPortID(), arrayIndex), msg);
        }

        /// <summary>
        /// Updates the contents of <see cref="Buffer{T}"/>s appearing in this node's <see cref="IGraphKernel{TKernelData,TKernelPortDefinition}"/>.
        /// Pass an instance of the node's <see cref="IGraphKernel{TKernelData,TKernelPortDefinition}"/> as the <paramref name="requestedContents"/>
        /// parameter with <see cref="Buffer{T}"/> instances within it having been set using <see cref="UploadRequest"/>, or
        /// <see cref="Buffer{T}.SizeRequest(int)"/>.
        /// Any <see cref="Buffer{T}"/> instances within the given struct that have default values will be unaffected by the call.
        /// </summary>
        public void UpdateKernelBuffers<TGraphKernel>(in TGraphKernel kernel)
            where TGraphKernel : struct, IGraphKernel
        {
            Set.UpdateKernelBuffers(InternalHandle, kernel);
        }

        /// <summary>
        /// The return value should be used together with <see cref="UpdateKernelBuffers"/> to change the contents
        /// of a kernel buffer living on a <see cref="IGraphKernel{TKernelData, TKernelPortDefinition}"/>.
        /// </summary>
        /// <remarks>
        /// This will resize the affected buffer to the same size as <paramref name="inputMemory"/>.
        /// Failing to include the return value in a call to <see cref="UpdateKernelBuffers"/> is an error and will result in a memory leak.
        /// </remarks>
        public Buffer<T> UploadRequest<T>(NativeArray<T> inputMemory, BufferUploadMethod method = BufferUploadMethod.Copy)
            where T : struct
                => Set.UploadRequest(InternalHandle, inputMemory, method);

        /// <summary>
        /// Updates the associated <typeparamref name="TKernelData"/> asynchronously,
        /// to be available in a <see cref="IGraphKernel"/> in the next render.
        /// </summary>
        public void UpdateKernelData<TKernelData>(in TKernelData data)
            where TKernelData : struct, IKernelData
        {
            Set.UpdateKernelData(InternalHandle, data);
        }

        /// <summary>
        /// Registers <see cref="Handle"/> for regular updates every time <see cref="NodeSet.Update()"/> is called.
        /// This only takes effect after the next <see cref="NodeSet.Update()"/>.
        /// <seealso cref="IUpdate.Update(in UpdateContext)"/>
        /// <seealso cref="RemoveFromUpdate()"/>
        /// </summary>
        /// <remarks>
        /// A node will automatically be removed from the update list when it is destroyed.
        /// </remarks>
        /// <exception cref="InvalidNodeDefinitionException">
        /// Will be thrown if the <see cref="Handle"/> does not support updating.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown if <see cref="Handle"/> is already registered for updating.
        /// </exception>
        public void RegisterForUpdate() => Set.RegisterForUpdate(InternalHandle);

        /// <summary>
        /// Deregisters <see cref="Handle"/> from updating every time <see cref="NodeSet.Update()"/> is called.
        /// This only takes effect after the next <see cref="NodeSet.Update()"/>.
        /// <seealso cref="RegisterForUpdate()"/>
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown if <see cref="Handle"/> is not registered for updating.
        /// </exception>
        public void RemoveFromUpdate() => Set.RemoveFromUpdate(InternalHandle);

        internal ValidatedHandle InternalHandle => m_Ctx.InternalHandle;

        internal MessageContext(NodeSetAPI set, in InputPair dest)
        {
            m_Ctx = new CommonContext(set, dest.Handle);
            m_Port = dest.Port;
        }
    }

    /// <summary>
    /// Interface to be implemented on an <see cref="INodeData"/> struct for a <see cref="SimulationNodeDefinition{TSimulationPortDefinition}"/>
    /// or <see cref="SimulationKernelNodeDefinition{TSimulationPortDefinition,TKernelPortDefinition}"/> which includes an
    /// <see cref="ISimulationPortDefinition"/> that contains <see cref="MessageInput{TDefinition,TMsg}"/>
    /// fields. This interface is used to handle messages which arrive on those <see cref="MessageInput{TDefinition,TMsg}"/> ports.
    /// </summary>
    public interface IMsgHandler<TMsg>
    {
        void HandleMessage(in MessageContext ctx, in TMsg msg);
    }

    /// <summary>
    /// Alternate variant which can be used in place of <see cref="IMsgHandler{TMsg}"/> when an <see cref="INodeData"/> struct
    /// implementation would otherwise need to implement two incompatible <see cref="IMsgHandler{TMsg}"/> interfaces. A common
    /// use of this variant would be for a node which has an input of generic type and other input(s) of non generic type(s).
    /// In this scenario, the node could use <see cref="IMsgHandlerGeneric{TMsg}"/> to implement the handler for its generic
    /// input, and normal <see cref="IMsgHandler{TMsg}"/>s for other inputs.
    /// </summary>
    public interface IMsgHandlerGeneric<TMsg>
    {
        void HandleMessage(in MessageContext ctx, in TMsg msg);
    }

    public interface ITaskPortMsgHandler<TTask, TMessage> :
        ITaskPort<TTask>, IMsgHandler<TMessage>
        where TTask : IMsgHandler<TMessage>, ITaskPort<TTask>
    {
    }

    public partial class NodeSet
    {
        /// <summary>
        /// Send a message of a specific type to a message input port on a node.
        /// </summary>
        /// <param name="handle">The node to be messaged.</param>
        /// <param name="port">A <see cref="MessageInput{TDefinition,TMsg}"/> port on the given node.</param>
        /// <param name="msg">The content of the message to be delivered.</param>
        /// <typeparam name="TMsg">The type of message data. Must correspond to the type of the given <see cref="MessageInput{TDefinition,TMsg}"/>.</typeparam>
        /// <exception cref="InvalidOperationException">Thrown if the request is invalid.</exception>
        public void SendMessage<TMsg>(NodeHandle handle, InputPortID port, in TMsg msg)
        {
            SendMessage(handle, new InputPortArrayID(port), msg);
        }

        /// <summary>
        /// Overload of <see cref="SendMessage{TMsg}(Unity.DataFlowGraph.NodeHandle,Unity.DataFlowGraph.InputPortID,TMsg)"/>
        /// targeting a port array with an index parameter.
        /// </summary>
        /// <exception cref="IndexOutOfRangeException">Thrown if the index is out of range with respect to the port array.</exception>
        public void SendMessage<TMsg>(NodeHandle handle, InputPortID portArray, int index, in TMsg msg)
        {
            SendMessage(handle, new InputPortArrayID(portArray, index), msg);
        }

        /// <summary>
        /// See <see cref="SendMessage{TMsg}(Unity.DataFlowGraph.NodeHandle,Unity.DataFlowGraph.InputPortID,TMsg)"/>
        /// </summary>
        public void SendMessage<TMsg, TDefinition>(NodeHandle<TDefinition> handle, MessageInput<TDefinition, TMsg> port, in TMsg msg)
            where TDefinition : NodeDefinition
        {
            SendMessage(handle, new InputPortArrayID((InputPortID)port), msg);
        }

        /// <summary>
        /// Overload of <see cref="SendMessage{TMsg}(Unity.DataFlowGraph.NodeHandle,Unity.DataFlowGraph.InputPortID,TMsg)"/>
        /// targeting a port array with an index parameter.
        /// </summary>
        /// <exception cref="IndexOutOfRangeException">Thrown if the index is out of range with respect to the port array.</exception>
        public void SendMessage<TMsg, TDefinition>(NodeHandle<TDefinition> handle, PortArray<MessageInput<TDefinition, TMsg>> portArray, int index, in TMsg msg)
            where TDefinition : NodeDefinition
        {
            SendMessage(handle, new InputPortArrayID((InputPortID)portArray, index), msg);
        }

        public void SendMessage<TTask, TMsg, TDestination>(NodeInterfaceLink<TTask, TDestination> handle, in TMsg msg)
            where TTask : ITaskPort<TTask>
            where TDestination : NodeDefinition, TTask, new()
        {
            var f = GetDefinition(handle.TypedHandle);
            SendMessage(handle, f.GetPort(handle), msg);
        }

        public void SendMessage<TTask, TMsg>(NodeInterfaceLink<TTask> handle, in TMsg msg)
            where TTask : ITaskPort<TTask>
        {
            var f = GetDefinition(handle);
            if (f is TTask task)
            {
                SendMessage(handle, task.GetPort(handle), msg);
            }
            else
            {
                throw new InvalidOperationException($"Cannot send message to destination. Destination not of type {typeof(TTask).Name}");
            }
        }

        /// <summary>
        /// Sets the data on an unconnected data input port on a node.
        /// The data will persist on the input until a connection is made to that input, or, it replaced by another call to <see cref="SetData{TType}(Unity.DataFlowGraph.NodeHandle,Unity.DataFlowGraph.InputPortID,TType)"/>.
        /// </summary>
        /// <param name="handle">The node on which data is to be set.</param>
        /// <param name="port">A <see cref="DataInput{TDefinition,TType}"/> port on the given node.</param>
        /// <param name="data">The content of the data to be set.</param>
        /// <typeparam name="TType">The type of data to be set. Must correspond to the type of the given <see cref="DataInput{TDefinition,TType}"/>.</typeparam>
        /// <exception cref="InvalidOperationException">Thrown if the request is invalid.</exception>
        /// <remarks>
        /// Note that <see cref="Buffer{T}"/> data is unsupported at this time.
        /// </remarks>
        public void SetData<TType>(NodeHandle handle, InputPortID port, in TType data)
            where TType : struct
        {
            SetData(handle, new InputPortArrayID(port), data);
        }

        /// <summary>
        /// Overload of <see cref="SetData{TType}(Unity.DataFlowGraph.NodeHandle,Unity.DataFlowGraph.InputPortID,TType)"/>
        /// targeting a port array with an index parameter.
        /// </summary>
        /// <exception cref="IndexOutOfRangeException">Thrown if the index is out of range with respect to the port array.</exception>
        public void SetData<TType>(NodeHandle handle, InputPortID portArray, int index, in TType data)
            where TType : struct
        {
            SetData(handle, new InputPortArrayID(portArray, index), data);
        }

        /// <summary>
        /// See <see cref="SetData{TType}(Unity.DataFlowGraph.NodeHandle,Unity.DataFlowGraph.InputPortID,TType)"/>
        /// </summary>
        public void SetData<TType, TDefinition>(NodeHandle<TDefinition> handle, DataInput<TDefinition, TType> port, in TType data)
            where TDefinition : NodeDefinition
            where TType : struct
        {
            SetData(handle, new InputPortArrayID(port.Port), data);
        }

        /// <summary>
        /// Overload of <see cref="SetData{TType}(Unity.DataFlowGraph.NodeHandle,Unity.DataFlowGraph.InputPortID,TType)"/>
        /// targeting a port array with an index parameter.
        /// </summary>
        /// <exception cref="IndexOutOfRangeException">Thrown if the index is out of range with respect to the port array.</exception>
        public void SetData<TType, TDefinition>(NodeHandle<TDefinition> handle, PortArray<DataInput<TDefinition, TType>> portArray, int index, in TType data)
            where TDefinition : NodeDefinition
            where TType : struct
        {
            SetData(handle, new InputPortArrayID(portArray.GetPortID(), index), data);
        }

    }

    public partial class NodeSetAPI
    {
        unsafe internal void EmitMessage<TMsg>(ValidatedHandle handle, OutputPortArrayID port, in TMsg msg)
        {
            if (!Nodes.StillExists(handle))
                throw new InvalidOperationException("Cannot emit a message from a destroyed node");

            bool foundAnyValidConnections = false;

            for(var it = m_Topology[handle].OutputHeadConnection; it != Topology.Database.InvalidConnection; it = m_Database[it].NextOutputConnection)
            {
                ref readonly var connection = ref m_Database[it];

                if (connection.SourceOutputPort != port)
                    continue;

                foundAnyValidConnections = true;

                var dest = new InputPair(connection);
                if (connection.TraversalFlags == (uint)PortDescription.Category.Message)
                {
                    GetDefinitionInternal(connection.Destination).OnMessage(new MessageContext(this, dest), msg);
                }
                else
                {
#if DFG_ASSERTIONS
                    if (connection.TraversalFlags != PortDescription.MessageToDataConnectionCategory)
                        throw new AssertionException("Unexpected connection type");
#endif
#if DFG_ASSERTIONS
                    if (!UnsafeUtility.IsUnmanaged<TMsg>())
                        throw new AssertionException("Type was expected to be unmanaged");
#endif
                    m_Diff.SetData(dest, RenderGraph.AllocateAndCopyData(Utility.AddressOfEvenIfManaged(msg), new SimpleType(typeof(TMsg))));
                }
            }

            if (!foundAnyValidConnections)
            {
                for (var fP = Nodes[handle].ForwardedPortHead; fP != ForwardPortHandle.Invalid; fP = m_ForwardingTable[fP].NextIndex)
                {
                    ref var forward = ref m_ForwardingTable[fP];

                    if (forward.IsInput)
                        continue;

                    // Forwarded port list are monotonically increasing by port, so we can break out early
                    if (forward.GetOriginPortCounter() > port.PortID.Port)
                        break;

                    if (port.PortID == forward.GetOriginOutputPortID())
                        throw new ArgumentException("Cannot emit a message through a previously forwarded port");
                }

                CheckPortArrayBounds(new OutputPair(this, handle.ToPublicHandle(), port));
            }
        }

        internal void SendMessage<TMsg, TDefinition>(NodeHandle<TDefinition> handle, InputPortArrayID port, in TMsg msg)
            where TDefinition : NodeDefinition
        {
            var destination = new InputPair(this, handle, port);
            CheckPortArrayBounds(destination);
            GetDefinitionInternal(destination.Handle).OnMessage(new MessageContext(this, destination), msg);
        }

        internal unsafe void SendMessage<TMsg>(NodeHandle handle, InputPortArrayID port, in TMsg msg)
        {
            var destination = new InputPair(this, handle, port);

            var definition = GetDefinitionInternal(destination.Handle);
            var portDef = GetFormalPort(destination);

            if (portDef.IsPortArray != port.IsArray)
                throw new InvalidOperationException(portDef.IsPortArray
                    ? "An array index is required when sending a message to an array port."
                    : "An array index can only be given when sending a message to an array port.");

            if (portDef.Category != PortDescription.Category.Message)
                throw new InvalidOperationException($"Cannot send a message to a non-message typed port.");

            if (portDef.Type != typeof(TMsg))
                throw new InvalidOperationException(
                    $"Cannot send message of type ({typeof(TMsg)}) to a message port of type ({portDef.Type})");

            CheckPortArrayBounds(destination);

            definition.OnMessage(new MessageContext(this, destination), msg);
        }

        internal void SetData<TType, TDefinition>(NodeHandle<TDefinition> handle, InputPortArrayID port, in TType data)
            where TDefinition : NodeDefinition
            where TType : struct
        {
            var destination = new InputPair(this, handle, port);

            if (GetFormalPort(destination).HasBuffers)
                throw new InvalidOperationException($"Cannot set data on a data port which includes buffers");

            SetDataOnValidatedPort(destination, data);
        }

        internal unsafe void SetDataOnValidatedPort<TType>(in InputPair destination, in TType data)
            where TType : struct
        {
            CheckPortArrayBounds(destination);

            for (var it = m_Topology[destination.Handle].InputHeadConnection; it != InvalidConnection; it = m_Database[it].NextInputConnection)
            {
                if (m_Database[it].DestinationInputPort == destination.Port)
                    throw new InvalidOperationException("Cannot send data to an already connected Data input port");
            }

            // Allocate and copy data and give ownership to the graph diff which will ultimately transfer ownership to the KernelNode.
            m_Diff.SetData(destination, RenderGraph.AllocateAndCopyData(data));
        }

        internal void SetData<TType>(NodeHandle handle, InputPortArrayID port, in TType data)
            where TType : struct
        {
            var destination = new InputPair(this, handle, port);

            var portDef = GetFormalPort(destination);

            if (portDef.Category != PortDescription.Category.Data)
                throw new InvalidOperationException("Cannot set data on a non-data port");

            if (portDef.HasBuffers)
                throw new InvalidOperationException($"Cannot set data on a data port which includes buffers");

            if (portDef.Type != typeof(TType))
                throw new InvalidOperationException(
                    $"Cannot set data of type ({typeof(TType)}) on a data port of type ({portDef.Type})");

            if (portDef.IsPortArray != port.IsArray)
                throw new InvalidOperationException(portDef.IsPortArray
                    ? "An array index is required when setting data on an array port."
                    : "An array index can only be given when setting data on an array port.");

            SetDataOnValidatedPort(destination, data);
        }
    }

}
