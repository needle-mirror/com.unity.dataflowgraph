using System;
using System.ComponentModel;
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
    public readonly struct InitContext : Detail.IContext<InitContext>
    {
        readonly CommonContext m_Ctx;
        // Exceedingly hard to pass down a stack local, but that's all this is.
        readonly unsafe void* m_ForwardedConnectionsMemory;
        readonly int TypeIndex;

        /// <summary>
        /// A handle uniquely identifying the currently initializing node.
        /// </summary>
        public NodeHandle Handle => m_Ctx.Handle;

        /// <summary>
        /// The <see cref="NodeSetAPI"/> associated with this context.
        /// </summary>
        public NodeSetAPI Set => m_Ctx.Set;

        /// <summary>
        /// Conversion operator for common API shared with other contexts.
        /// </summary>
        public static implicit operator CommonContext(in InitContext ctx) => ctx.m_Ctx;

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
        public void ForwardOutput<TDefinition, TForwardedDefinition, TType>(PortArray<DataOutput<TDefinition, TType>> origin, NodeHandle<TForwardedDefinition> replacedNode, PortArray<DataOutput<TForwardedDefinition, TType>> replacement)
            where TDefinition : NodeDefinition
            where TForwardedDefinition : NodeDefinition
            where TType : struct
        {
            CommonChecks<TDefinition>(replacedNode, (OutputPortID)origin);
            GetForwardingBuffer().Add(ForwardedPort.Unchecked.Output(origin.GetPortID(), replacedNode, replacement.GetPortID()));
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
        /// Sets an initial <paramref name="value"/> on <paramref name="port"/>.
        /// </summary>
        /// <remarks>
        /// This function cannot resiliently be used for making default values on input ports,
        /// as subsequent disconnections will reset the value to a default representation of <typeparamref name="TType"/>.
        /// </remarks>
        public void SetInitialPortValue<TNodeDefinition, TType>(DataInput<TNodeDefinition, TType> port, in TType value)
            where TNodeDefinition : NodeDefinition
            where TType : struct
        {
            if (TypeIndex != NodeDefinitionTypeIndex<TNodeDefinition>.Index)
                throw new ArgumentException($"Unrelated type {typeof(TNodeDefinition)} given for origin port");

            Set.SetDataOnValidatedPort(new InputPair(Set, Handle, new InputPortArrayID(port.Port)), value);
        }

        internal unsafe InitContext(ValidatedHandle handle, int typeIndex, NodeSetAPI set, ref BlitList<ForwardedPort.Unchecked> stackList)
        {
            m_Ctx = new CommonContext(set, handle);
            TypeIndex = typeIndex;
            m_ForwardedConnectionsMemory = UnsafeUtility.AddressOf(ref stackList);
        }

        void CommonChecks<TDefinition>(NodeHandle replacedNode, InputPortID originPort)
            where TDefinition : NodeDefinition
        {
            CommonChecks<TDefinition>(replacedNode, true, originPort.Storage.DFGPort);
        }

        void CommonChecks<TDefinition>(NodeHandle replacedNode, OutputPortID originPort)
            where TDefinition : NodeDefinition
        {
            CommonChecks<TDefinition>(replacedNode, false, originPort.Storage.DFGPort);
        }

        void CommonChecks<TDefinition>(NodeHandle replacedNode, bool isInput, PortStorage.EncodedDFGPort originPort)
            where TDefinition : NodeDefinition
        {
            ref var buffer = ref GetForwardingBuffer();

            for (int i = buffer.Count - 1; i >= 0; --i)
            {
                if (buffer[i].IsInput != isInput)
                    continue;

                if (buffer[i].OriginPortID.Category != originPort.Category)
                    continue;

                if (originPort.CategoryCounter < buffer[i].OriginPortID.CategoryCounter)
                    throw new ArgumentException("Ports must be forwarded in order of declaration");

                if (originPort.CategoryCounter == buffer[i].OriginPortID.CategoryCounter)
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

        unsafe ref BlitList<ForwardedPort.Unchecked> GetForwardingBuffer()
        {
            ref BlitList<ForwardedPort.Unchecked> buffer = ref UnsafeUtility.AsRef<BlitList<ForwardedPort.Unchecked>>(m_ForwardedConnectionsMemory);

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
