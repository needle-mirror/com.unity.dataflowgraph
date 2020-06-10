using System;
using System.Runtime.CompilerServices;
using Unity.Collections;

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

        // Exceedingly hard to pass down a stack local, but that's all this is.
        internal readonly unsafe void* m_ForwardedConnectionsMemory;
        internal readonly ValidatedHandle m_Handle;
        readonly NodeSet m_Set;
        internal readonly int TypeIndex;

        /// <summary>
        /// Sets up forwarding of the given input port to another input port on a different (sub) node.
        /// </summary>
        public void ForwardInput<TDefinition, TForwardedDefinition, TMsg>(MessageInput<TDefinition, TMsg> origin, NodeHandle<TForwardedDefinition> replacedNode, MessageInput<TForwardedDefinition, TMsg> replacement)
            where TDefinition : NodeDefinition, IMsgHandler<TMsg>, new()
            where TForwardedDefinition : NodeDefinition, IMsgHandler<TMsg>
        {
            CommonChecks<TDefinition>(replacedNode, (InputPortID)origin);
            GetForwardingBuffer().Add(ForwardedPort.Unchecked.Input(origin.Port, replacedNode, replacement.Port));
        }

        /// <summary>
        /// See <see cref="ForwardInput{TDefinition,TForwardedDefinition,TMsg}(MessageInput{TDefinition,TMsg}, NodeHandle{TForwardedDefinition}, MessageInput{TForwardedDefinition,TMsg})"/>.
        /// </summary>
        public void ForwardInput<TDefinition, TForwardedDefinition, TMsg>(PortArray<MessageInput<TDefinition, TMsg>> origin, NodeHandle<TForwardedDefinition> replacedNode, PortArray<MessageInput<TForwardedDefinition, TMsg>> replacement)
            where TDefinition : NodeDefinition, IMsgHandler<TMsg>, new()
            where TForwardedDefinition : NodeDefinition, IMsgHandler<TMsg>
        {
            CommonChecks<TDefinition>(replacedNode, (InputPortID)origin);
            GetForwardingBuffer().Add(ForwardedPort.Unchecked.Input(origin.Port, replacedNode, replacement.Port));
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
            GetForwardingBuffer().Add(ForwardedPort.Unchecked.Input(origin.Port, replacedNode, replacement.Port));
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
        /// Set the size of a <see cref="Buffer{T}"/> appearing in this node's <see cref="IGraphKernel{TKernelData,TKernelPortDefinition}"/>.
        /// Pass an instance of the node's <see cref="IGraphKernel{TKernelData,TKernelPortDefinition}"/> as the <paramref name="requestedSize"/>
        /// parameter with <see cref="Buffer{T}"/> instances within it having been set using <see cref="Buffer{T}.SizeRequest(int)"/>. 
        /// Any <see cref="Buffer{T}"/> instances within the given struct that have not been set using 
        /// <see cref="Buffer{T}.SizeRequest(int)"/> will be unaffected by the call.
        /// </summary>
        public void SetKernelBufferSize<TGraphKernel>(in TGraphKernel requestedSize)
            where TGraphKernel : IGraphKernel
        {
            m_Set.SetKernelBufferSize(m_Handle, requestedSize);
        }

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

        internal unsafe InitContext(ValidatedHandle handle, int typeIndex, NodeSet set, ref BlitList<ForwardedPort.Unchecked> stackList)
        {
            m_Handle = handle;
            TypeIndex = typeIndex;
            m_ForwardedConnectionsMemory = Unsafe.AsPointer(ref stackList);
            m_Set = set;
        }

        unsafe ref BlitList<ForwardedPort.Unchecked> GetForwardingBuffer()
        {
            ref BlitList<ForwardedPort.Unchecked> buffer = ref Unsafe.AsRef<BlitList<ForwardedPort.Unchecked>>(m_ForwardedConnectionsMemory);

            if (!buffer.IsCreated)
                buffer = new BlitList<ForwardedPort.Unchecked>(0, Allocator.Temp);

            return ref buffer;
        }
    }

}
