using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.DataFlowGraph
{
    /// <summary>
    /// A unique initialization context provided to a node on instantiation that allows it to internally configure its specific instance.
    /// Allows forwarding port declarations to another node instance on a port of the same type.
    /// The effect is that any external connection made to those forwarded ports are converted into a direct connection between the 3rd party and the the node forwarded to.
    /// This is invisible to anyone external to the node, and handled transparently by the node set.
    /// This system allows a node to create sub graphs that appear as single node to everyone else.
    /// <seealso cref="NodeDefinition.Init(InitContext)"/>
    /// </summary>
    /// <remarks>
    /// Any port forwarding actions only take effect after <see cref="NodeDefinition.Init(InitContext)"/> has returned.
    /// </remarks>
    public struct InitContext
    {
        /// <summary>
        /// A handle uniquely identifying the currently initializing node.
        /// </summary>
        public NodeHandle Handle => m_Handle.ToPublicHandle();
        /// <summary>
        /// The <see cref="NodeSetAPI"/> associated with this context.
        /// </summary>
        public readonly NodeSetAPI Set;

        // Exceedingly hard to pass down a stack local, but that's all this is.
        internal readonly unsafe void* m_ForwardedConnectionsMemory;
        internal readonly ValidatedHandle m_Handle;
        internal readonly int TypeIndex;

        /// <summary>
        /// Sets up forwarding of the given input port to another input port on a different (sub) node.
        /// </summary>
        public void ForwardInput<TDefinition, TForwardedDefinition, TMsg>(MessageInput<TDefinition, TMsg> origin, NodeHandle<TForwardedDefinition> replacedNode, MessageInput<TForwardedDefinition, TMsg> replacement)
            where TDefinition : NodeDefinition, new()
            where TForwardedDefinition : NodeDefinition
        {
            CommonChecks<TDefinition>(replacedNode, (InputPortID)origin);
            GetForwardingBuffer().Add(ForwardedPort.Unchecked.Input(origin.Port, replacedNode, replacement.Port));
        }

        /// <summary>
        /// See <see cref="ForwardInput{TDefinition,TForwardedDefinition,TMsg}(MessageInput{TDefinition,TMsg}, NodeHandle{TForwardedDefinition}, MessageInput{TForwardedDefinition,TMsg})"/>.
        /// </summary>
        public void ForwardInput<TDefinition, TForwardedDefinition, TMsg>(PortArray<MessageInput<TDefinition, TMsg>> origin, NodeHandle<TForwardedDefinition> replacedNode, PortArray<MessageInput<TForwardedDefinition, TMsg>> replacement)
            where TDefinition : NodeDefinition, new()
            where TForwardedDefinition : NodeDefinition
        {
            CommonChecks<TDefinition>(replacedNode, (InputPortID)origin);
            GetForwardingBuffer().Add(ForwardedPort.Unchecked.Input(origin.GetPortID(), replacedNode, replacement.GetPortID()));
        }

        /// <summary>
        /// See <see cref="ForwardInput{TDefinition,TForwardedDefinition,TMsg}(MessageInput{TDefinition,TMsg}, NodeHandle{TForwardedDefinition}, MessageInput{TForwardedDefinition,TMsg})"/>.
        /// </summary>
        public void ForwardInput<TDefinition, TForwardedDefinition, TType>(DataInput<TDefinition, TType> origin, NodeHandle<TForwardedDefinition> replacedNode, DataInput<TForwardedDefinition, TType> replacement)
            where TDefinition : NodeDefinition
            where TForwardedDefinition : NodeDefinition
            where TType : struct
        {
            CommonChecks<TDefinition>(replacedNode, (InputPortID)origin);
            GetForwardingBuffer().Add(ForwardedPort.Unchecked.Input(origin.Port, replacedNode, replacement.Port));
        }

        /// <summary>
        /// See <see cref="ForwardInput{TDefinition,TForwardedDefinition,TMsg}(MessageInput{TDefinition,TMsg}, NodeHandle{TForwardedDefinition}, MessageInput{TForwardedDefinition,TMsg})"/>.
        /// </summary>
        public void ForwardInput<TDefinition, TForwardedDefinition, TType>(PortArray<DataInput<TDefinition, TType>> origin, NodeHandle<TForwardedDefinition> replacedNode, PortArray<DataInput<TForwardedDefinition, TType>> replacement)
            where TDefinition : NodeDefinition
            where TForwardedDefinition : NodeDefinition
            where TType : struct
        {
            CommonChecks<TDefinition>(replacedNode, (InputPortID)origin);
            GetForwardingBuffer().Add(ForwardedPort.Unchecked.Input(origin.GetPortID(), replacedNode, replacement.GetPortID()));
        }

        /// <summary>
        /// See <see cref="ForwardInput{TDefinition,TForwardedDefinition,TMsg}(MessageInput{TDefinition,TMsg}, NodeHandle{TForwardedDefinition}, MessageInput{TForwardedDefinition,TMsg})"/>.
        /// </summary>
        public void ForwardInput<TDefinition, TForwardedDefinition, TDSLDefinition, IDSL>(
            DSLInput<TDefinition, TDSLDefinition, IDSL> origin,
            NodeHandle<TForwardedDefinition> replacedNode,
            DSLInput<TForwardedDefinition, TDSLDefinition, IDSL> replacement
        )
            where TDefinition : NodeDefinition, IDSL
            where TForwardedDefinition : NodeDefinition, IDSL
            where TDSLDefinition : DSLHandler<IDSL>, new()
            where IDSL : class
        {
            CommonChecks<TDefinition>(replacedNode, (InputPortID)origin);
            GetForwardingBuffer().Add(ForwardedPort.Unchecked.Input(origin.Port, replacedNode, replacement.Port));
        }

        /// <summary>
        /// Sets up forwarding of the given output port to another output port on a different (sub) node.
        /// </summary>
        public void ForwardOutput<TDefinition, TForwardedDefinition, TMsg>(MessageOutput<TDefinition, TMsg> origin, NodeHandle<TForwardedDefinition> replacedNode, MessageOutput<TForwardedDefinition, TMsg> replacement)
            where TDefinition : NodeDefinition, new()
            where TForwardedDefinition : NodeDefinition
        {
            CommonChecks<TDefinition>(replacedNode, (OutputPortID)origin);
            GetForwardingBuffer().Add(ForwardedPort.Unchecked.Output(origin.Port, replacedNode, replacement.Port));
        }

        /// <summary>
        /// See <see cref="ForwardOutput{TDefinition,TForwardedDefinition,TMsg}(MessageOutput{TDefinition,TMsg}, NodeHandle{TForwardedDefinition}, MessageOutput{TForwardedDefinition,TMsg})"/>.
        /// </summary>
        public void ForwardOutput<TDefinition, TForwardedDefinition, TMsg>(PortArray<MessageOutput<TDefinition, TMsg>> origin, NodeHandle<TForwardedDefinition> replacedNode, PortArray<MessageOutput<TForwardedDefinition, TMsg>> replacement)
            where TDefinition : NodeDefinition, new()
            where TForwardedDefinition : NodeDefinition
        {
            CommonChecks<TDefinition>(replacedNode, (OutputPortID)origin);
            GetForwardingBuffer().Add(ForwardedPort.Unchecked.Output(origin.GetPortID(), replacedNode, replacement.GetPortID()));
        }

        /// <summary>
        /// See <see cref="ForwardOutput{TDefinition,TForwardedDefinition,TMsg}(MessageOutput{TDefinition,TMsg}, NodeHandle{TForwardedDefinition}, MessageOutput{TForwardedDefinition,TMsg})"/>.
        /// </summary>
        public void ForwardOutput<TDefinition, TForwardedDefinition, TType>(DataOutput<TDefinition, TType> origin, NodeHandle<TForwardedDefinition> replacedNode, DataOutput<TForwardedDefinition, TType> replacement)
            where TDefinition : NodeDefinition
            where TForwardedDefinition : NodeDefinition
            where TType : struct
        {
            CommonChecks<TDefinition>(replacedNode, (OutputPortID)origin);
            GetForwardingBuffer().Add(ForwardedPort.Unchecked.Output(origin.Port, replacedNode, replacement.Port));
        }

        /// <summary>
        /// See <see cref="ForwardOutput{TDefinition,TForwardedDefinition,TMsg}(MessageOutput{TDefinition,TMsg}, NodeHandle{TForwardedDefinition}, MessageOutput{TForwardedDefinition,TMsg})"/>.
        /// </summary>
        public void ForwardOutput<TDefinition, TForwardedDefinition, TDSLDefinition, IDSL>(
            DSLOutput<TDefinition, TDSLDefinition, IDSL> origin,
            NodeHandle<TForwardedDefinition> replacedNode,
            DSLOutput<TForwardedDefinition, TDSLDefinition, IDSL> replacement
        )
            where TDefinition : NodeDefinition, IDSL
            where TForwardedDefinition : NodeDefinition, IDSL
            where TDSLDefinition : DSLHandler<IDSL>, new()
            where IDSL : class
        {
            CommonChecks<TDefinition>(replacedNode, (OutputPortID)origin);
            GetForwardingBuffer().Add(ForwardedPort.Unchecked.Output(origin.Port, replacedNode, replacement.Port));
        }

        /// <summary>
        /// Emit a message from yourself on a port. Everything connected to it will receive your message.
        /// </summary>
        public void EmitMessage<T, TNodeDefinition>(MessageOutput<TNodeDefinition, T> port, in T msg)
            where TNodeDefinition : NodeDefinition
        {
            Set.EmitMessage(m_Handle, new OutputPortArrayID(port.Port), msg);
        }

        /// <summary>
        /// Emit a message from yourself on a port array. Everything connected to it will receive your message.
        /// </summary>
        public void EmitMessage<T, TNodeDefinition>(PortArray<MessageOutput<TNodeDefinition, T>> port, int arrayIndex, in T msg)
            where TNodeDefinition : NodeDefinition
        {
            Set.EmitMessage(m_Handle, new OutputPortArrayID(port.GetPortID(), arrayIndex), msg);
        }

        /// <summary>
        /// Set the size of a <see cref="Buffer{T}"/> appearing in this node's <see cref="IGraphKernel{TKernelData,TKernelPortDefinition}"/>.
        /// Pass an instance of the node's <see cref="IGraphKernel{TKernelData,TKernelPortDefinition}"/> as the <paramref name="requestedSize"/>
        /// parameter with <see cref="Buffer{T}"/> instances within it having been set using <see cref="Buffer{T}.SizeRequest(int)"/>.
        /// Any <see cref="Buffer{T}"/> instances within the given struct that have not been set using
        /// <see cref="Buffer{T}.SizeRequest(int)"/> will be unaffected by the call.
        /// </summary>
        public void SetKernelBufferSize<TGraphKernel>(in TGraphKernel requestedSize)
            where TGraphKernel : IGraphKernel
        {
            Set.SetKernelBufferSize(m_Handle, requestedSize);
        }

        /// <summary>
        /// Updates the associated <typeparamref name="TKernelData"/> asynchronously,
        /// to be available in a <see cref="IGraphKernel"/> in the next render.
        /// </summary>
        public void UpdateKernelData<TKernelData>(in TKernelData data)
            where TKernelData : struct, IKernelData
        {
            Set.UpdateKernelData(m_Handle, data);
        }

        /// <summary>
        /// Registers <see cref="Handle"/> for regular updates every time <see cref="NodeSet.Update"/> is called.
        /// This only takes effect after the next <see cref="NodeSet.Update"/>.
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
        public void RegisterForUpdate() => Set.RegisterForUpdate(m_Handle);

        /// <summary>
        /// Deregisters <see cref="Handle"/> from updating every time <see cref="NodeSet.Update"/> is called.
        /// This only takes effect after the next <see cref="NodeSet.Update"/>.
        /// <seealso cref="RegisterForUpdate()"/>
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown if <see cref="Handle"/> is not registered for updating.
        /// </exception>
        public void RemoveFromUpdate() => Set.RemoveFromUpdate(m_Handle);

        void CommonChecks<TDefinition>(NodeHandle replacedNode, InputPortID originPort)
            where TDefinition : NodeDefinition
        {
            CommonChecks<TDefinition>(replacedNode, true, originPort.Storage.DFGPortIndex);
        }

        void CommonChecks<TDefinition>(NodeHandle replacedNode, OutputPortID originPort)
            where TDefinition : NodeDefinition
        {
            CommonChecks<TDefinition>(replacedNode, false, originPort.Storage.DFGPortIndex);
        }

        void CommonChecks<TDefinition>(NodeHandle replacedNode, bool isInput, ushort originPort)
            where TDefinition : NodeDefinition
        {
            ref var buffer = ref GetForwardingBuffer();

            for (int i = buffer.Count - 1; i >= 0; --i)
            {
                if (buffer[i].IsInput != isInput)
                    continue;

                var lastForwardedPort = buffer[i].GetOriginPortCounter();

                if (originPort < lastForwardedPort)
                    throw new ArgumentException("Ports must be forwarded in order of declaration");

                if (originPort == lastForwardedPort)
                    throw new ArgumentException("Cannot forward port twice");

                break;
            }

            CommonChecks<TDefinition>(replacedNode);
        }

        void CommonChecks<TDefinition>(NodeHandle replacedNode)
            where TDefinition : NodeDefinition
        {
            if (TypeIndex != NodeDefinitionTypeIndex<TDefinition>.Index)
                throw new ArgumentException($"Unrelated type {typeof(TDefinition)} given for origin port");

            if (replacedNode == Handle)
                throw new ArgumentException("Cannot forward to self");
        }

        internal unsafe InitContext(ValidatedHandle handle, int typeIndex, NodeSetAPI set, ref BlitList<ForwardedPort.Unchecked> stackList)
        {
            m_Handle = handle;
            TypeIndex = typeIndex;
            m_ForwardedConnectionsMemory = UnsafeUtility.AddressOf(ref stackList);
            Set = set;
        }

        unsafe ref BlitList<ForwardedPort.Unchecked> GetForwardingBuffer()
        {
            ref BlitList<ForwardedPort.Unchecked> buffer = ref Utility.AsRef<BlitList<ForwardedPort.Unchecked>>(m_ForwardedConnectionsMemory);

            if (!buffer.IsCreated)
                buffer = new BlitList<ForwardedPort.Unchecked>(0, Allocator.Temp);

            return ref buffer;
        }
    }

    /// <summary>
    /// Interface for receiving constructor calls on <see cref="INodeData"/>,
    /// whenever a new node is created.
    ///
    /// This supersedes <see cref="NodeDefinition.Init(InitContext)"/>
    /// </summary>
    public interface IInit
    {
        /// <summary>
        /// Constructor function, called for each instantiation of this type.
        /// <seealso cref="NodeSetAPI.Create{TDefinition}"/>
        /// </summary>
        /// <remarks>
        /// It is undefined behaviour to throw an exception from this method.
        /// </remarks>
        /// <param name="ctx">
        /// Provides initialization context and do-once operations
        /// for this particular node.
        /// <seealso cref="Init(InitContext)"/>
        /// </param>
        void Init(InitContext ctx);
    }

}
