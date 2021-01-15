using System;
using System.ComponentModel;
using Unity.DataFlowGraph.Detail;

namespace Unity.DataFlowGraph
{
    namespace Detail
    {
        [EditorBrowsable(EditorBrowsableState.Never)]
        public readonly struct UnresolvedOutputPair
        {
            internal readonly NodeHandle Handle;
            internal readonly OutputPortArrayID PortID;

            internal UnresolvedOutputPair(NodeHandle handle, OutputPortArrayID id)
            {
                Handle = handle;
                PortID = id;
            }
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public readonly struct UnresolvedInputPair
        {
            internal readonly NodeHandle Handle;
            internal readonly InputPortArrayID PortID;

            internal UnresolvedInputPair(NodeHandle handle, InputPortArrayID id)
            {
                Handle = handle;
                PortID = id;
            }
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public interface IOutputEndpoint : IEndpoint
        {
            UnresolvedOutputPair Endpoint { get; }
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public interface IInputEndpoint : IEndpoint
        {
            UnresolvedInputPair Endpoint { get; }
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public interface IConnectableWith<TType> { }

        // These exist internally so Conceivable endpoints in the future *won't* implement IConcrete versions
        // This allows conceivable / concrete to actively match each other for GCB, but NOT for normal connect.
        [EditorBrowsable(EditorBrowsableState.Never)]
        public interface IConcrete { }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public interface IPotentialDataEndpoint { }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public interface IInternalEndpoint
        {
            bool IsWeak { get; }
            PortDescription.Category Category { get; }
            TypeHash Type { get; }
        }
    }

    /// <summary>
    /// An endpoint represents a directed port (like <see cref="DataInput{TDefinition, TType}"/> and a node handle, like
    /// <see cref="NodeHandle{TDefinition}"/>.
    ///
    /// Together they form a generalized "connectable" point that can be used in various unary and binary topology
    /// APIs.
    ///
    /// You can create them for instance by tying a known handle to a port, using <see cref="NodeHandle{TDefinition}.Tie"/>.
    /// </summary>
    /// <remarks>
    /// There's no stable API on the endpoint itself, only APIs external to endpoints are stable.
    /// Endpoints are not designed to be user implementable. Using an endpoint defined outside of this assembly is undefined
    /// behaviour.
    /// </remarks>
    public interface IEndpoint : IInternalEndpoint { }

    /// <summary>
    /// A directed, weak (runtime checked) input endpoint.
    /// See <see cref="IEndPoint"/> for more information.
    /// You can use this in various topology APIs, like <see cref="NodeSetAPI.Connect{TLeft, TRight}(in TLeft, in TRight)"/>.
    ///
    /// <seealso cref="OutputEndpoint"/>
    /// </summary>
    public readonly struct InputEndpoint : IConcrete, IInputEndpoint, IPotentialDataEndpoint, IConnectableWith<OutputEndpoint>
    {
        public bool IsWeak => true;
        public UnresolvedInputPair Endpoint { get; }
        public PortDescription.Category Category => throw new InvalidOperationException("Weak endpoints have undefined category");
        public TypeHash Type => throw new InvalidOperationException("Weak endpoints have undefined type");
        internal InputEndpoint(in UnresolvedInputPair pair) => Endpoint = pair;
    }

    /// <summary>
    /// A directed, weak (runtime checked) output endpoint.
    /// See <see cref="IEndPoint"/> for more information.
    /// You can use this in various topology APIs, like <see cref="NodeSetAPI.Connect{TLeft, TRight}(in TLeft, in TRight)"/>.
    ///
    /// <seealso cref="InputEndpoint"/>
    /// </summary>
    public readonly struct OutputEndpoint : IConcrete, IOutputEndpoint, IPotentialDataEndpoint, IConnectableWith<InputEndpoint>
    {
        public bool IsWeak => true;
        public UnresolvedOutputPair Endpoint { get; }
        public PortDescription.Category Category => throw new InvalidOperationException("Weak endpoints have undefined category");
        public TypeHash Type => throw new InvalidOperationException("Weak endpoints have undefined type");
        internal OutputEndpoint(in UnresolvedOutputPair pair) => Endpoint = pair;
    }

    /// <summary>
    /// A directed, strongly-typed data input endpoint.
    /// See <see cref="IEndPoint"/> for more information.
    /// You can use this in various topology APIs, like <see cref="NodeSetAPI.Connect{TLeft, TRight}(in TLeft, in TRight)"/>.
    ///
    /// <seealso cref="DataOutputEndpoint{TType}"/>
    /// <seealso cref="NodeHandle{TDefinition}.Tie{TType}(DataInput{TDefinition, TType})"/>
    /// </summary>
    public readonly struct DataInputEndpoint<TType> : IConcrete, IInputEndpoint, IPotentialDataEndpoint, IConnectableWith<DataOutputEndpoint<TType>>, IConnectableWith<MessageOutputEndpoint<TType>>
    {
        public bool IsWeak => false;
        public UnresolvedInputPair Endpoint { get; }
        public PortDescription.Category Category => PortDescription.Category.Data;
        public TypeHash Type => TypeHash.Create<TType>();
        public static implicit operator InputEndpoint(in DataInputEndpoint<TType> operand) => new InputEndpoint(operand.Endpoint);
        internal DataInputEndpoint(in UnresolvedInputPair pair) => Endpoint = pair;
    }

    /// <summary>
    /// A directed, strongly-typed data output endpoint.
    /// See <see cref="IEndPoint"/> for more information.
    /// You can use this in various topology APIs, like <see cref="NodeSetAPI.Connect{TLeft, TRight}(in TLeft, in TRight)"/>.
    ///
    /// <seealso cref="DataInputEndpoint{TType}"/>
    /// <seealso cref="NodeHandle{TDefinition}.Tie{TType}(DataOutput{TDefinition, TType})"/>
    /// </summary>
    public readonly struct DataOutputEndpoint<TType> : IConcrete, IOutputEndpoint, IPotentialDataEndpoint, IConnectableWith<DataInputEndpoint<TType>>
    {
        public bool IsWeak => false;
        public UnresolvedOutputPair Endpoint { get; }
        public PortDescription.Category Category => PortDescription.Category.Data;
        public TypeHash Type => TypeHash.Create<TType>();
        public static implicit operator OutputEndpoint(in DataOutputEndpoint<TType> operand) => new OutputEndpoint(operand.Endpoint);
        internal DataOutputEndpoint(in UnresolvedOutputPair pair) => Endpoint = pair;
    }

    /// <summary>
    /// A directed, strongly-typed message input endpoint.
    /// See <see cref="IEndPoint"/> for more information.
    /// You can use this in various topology APIs, like <see cref="NodeSetAPI.Connect{TLeft, TRight}(in TLeft, in TRight)"/>.
    ///
    /// <seealso cref="MessageOutputEndpoint{TType}"/>
    /// <seealso cref="NodeHandle{TDefinition}.Tie{TType}(MessageInput{TDefinition, TType})"/>
    /// </summary>
    public readonly struct MessageInputEndpoint<TType> : IConcrete, IInputEndpoint, IConnectableWith<MessageOutputEndpoint<TType>>
    {
        public bool IsWeak => false;
        public UnresolvedInputPair Endpoint { get; }
        public PortDescription.Category Category => PortDescription.Category.Message;
        public TypeHash Type => TypeHash.Create<TType>();
        public static implicit operator InputEndpoint(in MessageInputEndpoint<TType> operand) => new InputEndpoint(operand.Endpoint);
        internal MessageInputEndpoint(in UnresolvedInputPair pair) => Endpoint = pair;
    }

    /// <summary>
    /// A directed, strongly-typed message output endpoint.
    /// See <see cref="IEndPoint"/> for more information.
    /// You can use this in various topology APIs, like <see cref="NodeSetAPI.Connect{TLeft, TRight}(in TLeft, in TRight)"/>.
    ///
    /// <seealso cref="MessageInputEndpoint{TType}"/>
    /// <seealso cref="NodeHandle{TDefinition}.Tie{TType}(MessageOutput{TDefinition, TType})"/>
    /// </summary>
    public readonly struct MessageOutputEndpoint<TType> : IConcrete, IOutputEndpoint, IConnectableWith<MessageInputEndpoint<TType>>, IConnectableWith<DataInputEndpoint<TType>>
    {
        public bool IsWeak => false;
        public UnresolvedOutputPair Endpoint { get; }
        public PortDescription.Category Category => PortDescription.Category.Message;
        public TypeHash Type => TypeHash.Create<TType>();
        public static implicit operator OutputEndpoint(in MessageOutputEndpoint<TType> operand) => new OutputEndpoint(operand.Endpoint);
        internal MessageOutputEndpoint(in UnresolvedOutputPair pair) => Endpoint = pair;
    }

    /// <summary>
    /// A directed, strongly-typed domain specific input endpoint.
    /// See <see cref="IEndPoint"/> for more information.
    /// You can use this in various topology APIs, like <see cref="NodeSetAPI.Connect{TLeft, TRight}(in TLeft, in TRight)"/>.
    ///
    /// <seealso cref="DSLOutputEndpoint{TDSLHandler}"/>
    /// <seealso cref="NodeHandle{TDefinition}.Tie{TCompleteDefinition, TDSLDefinition, IDSL}(DSLInput{TCompleteDefinition, TDSLDefinition, IDSL})"/>
    /// </summary>
    public readonly struct DSLInputEndpoint<TDSLHandler> : IConcrete, IInputEndpoint, IConnectableWith<DSLOutputEndpoint<TDSLHandler>>
        where TDSLHandler : class, IDSLHandler, new()
    {
        public bool IsWeak => false;
        public UnresolvedInputPair Endpoint { get; }
        public PortDescription.Category Category => PortDescription.Category.DomainSpecific;
        public TypeHash Type => DSLTypeMap.StaticHashNoRegister<TDSLHandler>();
        public static implicit operator InputEndpoint(in DSLInputEndpoint<TDSLHandler> operand) => new InputEndpoint(operand.Endpoint);
        internal DSLInputEndpoint(in UnresolvedInputPair pair) => Endpoint = pair;
    }

    /// <summary>
    /// A directed, strongly-typed domain specific output endpoint.
    /// See <see cref="IEndPoint"/> for more information.
    /// You can use this in various topology APIs, like <see cref="NodeSetAPI.Connect{TLeft, TRight}(in TLeft, in TRight)"/>.
    ///
    /// <seealso cref="DSLOutputEndpoint{TDSLHandler}"/>
    /// <seealso cref="NodeHandle{TDefinition}.Tie{TCompleteDefinition, TDSLDefinition, IDSL}(DSLOutput{TCompleteDefinition, TDSLDefinition, IDSL})"/>
    /// </summary>
    public readonly struct DSLOutputEndpoint<TDSLHandler> : IConcrete, IOutputEndpoint, IConnectableWith<DSLInputEndpoint<TDSLHandler>>
        where TDSLHandler : class, IDSLHandler, new()
    {
        public bool IsWeak => false;
        public UnresolvedOutputPair Endpoint { get; }
        public PortDescription.Category Category => PortDescription.Category.DomainSpecific;
        public TypeHash Type => DSLTypeMap.StaticHashNoRegister<TDSLHandler>();
        public static implicit operator OutputEndpoint(in DSLOutputEndpoint<TDSLHandler> operand) => new OutputEndpoint(operand.Endpoint);
        internal DSLOutputEndpoint(in UnresolvedOutputPair pair) => Endpoint = pair;
    }

    public partial struct NodeHandle<TDefinition> : IEquatable<NodeHandle<TDefinition>>
        where TDefinition : NodeDefinition
    {
        /// <summary>
        /// Combine this node handle with <paramref name="input"/> to form a type validated endpoint.
        /// See <see cref="IEndpoint"/> for more information.
        /// </summary>
        public DataInputEndpoint<TType> Tie<TType>(DataInput<TDefinition, TType> input)
            where TType : struct
                => new DataInputEndpoint<TType>(new UnresolvedInputPair(m_UntypedHandle, new InputPortArrayID(input.Port)));

        /// <summary>
        /// Combine this node handle with <paramref name="output"/> to form a type validated endpoint.
        /// See <see cref="IEndpoint"/> for more information.
        /// </summary>
        public DataOutputEndpoint<TType> Tie<TType>(DataOutput<TDefinition, TType> output)
            where TType : struct
                => new DataOutputEndpoint<TType>(new UnresolvedOutputPair(m_UntypedHandle, new OutputPortArrayID(output.Port)));

        /// <summary>
        /// Combine this node handle with <paramref name="input"/> to form a type validated endpoint.
        /// See <see cref="IEndpoint"/> for more information.
        /// </summary>
        public MessageInputEndpoint<TType> Tie<TType>(MessageInput<TDefinition, TType> input)
            where TType : struct
                => new MessageInputEndpoint<TType>(new UnresolvedInputPair(m_UntypedHandle, new InputPortArrayID(input.Port)));

        /// <summary>
        /// Combine this node handle with <paramref name="output"/> to form a type validated endpoint.
        /// See <see cref="IEndpoint"/> for more information.
        /// </summary>
        public MessageOutputEndpoint<TType> Tie<TType>(MessageOutput<TDefinition, TType> output)
            where TType : struct
                => new MessageOutputEndpoint<TType>(new UnresolvedOutputPair(m_UntypedHandle, new OutputPortArrayID(output.Port)));

        /// <summary>
        /// Combine this node handle with <paramref name="input"/> to form a type validated endpoint.
        /// See <see cref="IEndpoint"/> for more information.
        /// </summary>
        public DSLInputEndpoint<TDSLDefinition> Tie<TCompleteDefinition, TDSLDefinition, IDSL>(DSLInput<TCompleteDefinition, TDSLDefinition, IDSL> input)
            where TCompleteDefinition : TDefinition, IDSL
            where TDSLDefinition : DSLHandler<IDSL>, new()
            where IDSL : class
                => new DSLInputEndpoint<TDSLDefinition>(new UnresolvedInputPair(m_UntypedHandle, new InputPortArrayID(input.Port)));

        /// <summary>
        /// Combine this node handle with <paramref name="output"/> to form a type validated endpoint.
        /// See <see cref="IEndpoint"/> for more information.
        /// </summary>
        public DSLOutputEndpoint<TDSLDefinition> Tie<TCompleteDefinition, TDSLDefinition, IDSL>(DSLOutput<TCompleteDefinition, TDSLDefinition, IDSL> output)
            where TCompleteDefinition : TDefinition, IDSL
            where TDSLDefinition : DSLHandler<IDSL>, new()
            where IDSL : class
                => new DSLOutputEndpoint<TDSLDefinition>(new UnresolvedOutputPair(m_UntypedHandle, new OutputPortArrayID(output.Port)));
    }
}
