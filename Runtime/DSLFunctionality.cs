using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity.DataFlowGraph
{
    public interface IDSLHandler : IDisposable
    {
        void Connect(NodeSetAPI set, NodeHandle source, OutputPortID sourcePort, NodeHandle destination, InputPortID destinationPort);
        void Disconnect(NodeSetAPI set, NodeHandle source, OutputPortID sourcePort, NodeHandle destination, InputPortID destinationPort);
    }

    /// <summary>
    /// Connection handler for <see cref="DSLInput{TNodeDefinition,TDSLDefinition,IDSL}"/> and
    /// <see cref="DSLOutput{TNodeDefinition,TDSLDefinition,IDSL}"/> port types. The implementation is invoked whenever
    /// connections on DSL ports tied to this handler are made or broken.
    /// </summary>
    /// <typeparam name="TDSLInterface">
    /// The user defined interface which <see cref="NodeDefinition{TNodeData,TSimulationPortDefinition}"/>s must
    /// implement so that the <see cref="DSLHandler{TDSLInterface}"/> can interact with them.
    /// </typeparam>
    public abstract class DSLHandler<TDSLInterface> : IDSLHandler
        where TDSLInterface : class
    {
        protected struct ConnectionInfo
        {
            public NodeHandle Handle;
            public TDSLInterface Interface;
            public ushort DSLPortIndex;
        }

        public void Connect(NodeSetAPI set, NodeHandle source, OutputPortID sourcePort, NodeHandle destination, InputPortID destinationPort)
        {
            var srcNodeFunc = set.GetDefinition(source);
            var destNodeFunc = set.GetDefinition(destination);

            var srcNodeDSL = set.GetDefinition(source) as TDSLInterface;
            var destNodeDSL = set.GetDefinition(destination) as TDSLInterface;

            if (srcNodeDSL == null || destNodeDSL == null)
                throw new InvalidCastException();

            Connect(
                new ConnectionInfo {
                    Handle = source,
                    Interface = srcNodeDSL,
                    DSLPortIndex = GetDSLPortIndex(
                        sourcePort,
                        srcNodeFunc.GetPortDescription(source).Outputs.Cast<PortDescription.IPort<OutputPortID>>()
                    )
                },
                new ConnectionInfo {
                    Handle = destination,
                    Interface = destNodeDSL,
                    DSLPortIndex = GetDSLPortIndex(
                        destinationPort,
                        destNodeFunc.GetPortDescription(destination).Inputs.Cast<PortDescription.IPort<InputPortID>>()
                    )
                }
            );
        }

        public void Disconnect(NodeSetAPI set, NodeHandle source, OutputPortID sourcePort, NodeHandle destination, InputPortID destinationPort)
        {
            var srcNodeFunc = set.GetDefinition(source);
            var destNodeFunc = set.GetDefinition(destination);

            var srcNodeDSL = set.GetDefinition(source) as TDSLInterface;
            var destNodeDSL = set.GetDefinition(destination) as TDSLInterface;

            if (srcNodeDSL == null || destNodeDSL == null)
                throw new InvalidCastException();

            Disconnect(
                new ConnectionInfo
                {
                    Handle = source,
                    Interface = srcNodeDSL,
                    DSLPortIndex = GetDSLPortIndex(
                        sourcePort,
                        srcNodeFunc.GetPortDescription(source).Outputs.Cast<PortDescription.IPort<OutputPortID>>()
                    )
                },
                new ConnectionInfo
                {
                    Handle = destination,
                    Interface = destNodeDSL,
                    DSLPortIndex = GetDSLPortIndex(
                        destinationPort,
                        destNodeFunc.GetPortDescription(destination).Inputs.Cast<PortDescription.IPort<InputPortID>>()
                    )
                });
        }

        protected abstract void Connect(ConnectionInfo left, ConnectionInfo right);
        protected abstract void Disconnect(ConnectionInfo left, ConnectionInfo right);

        private ushort GetDSLPortIndex<TPortID>(TPortID port, IEnumerable<PortDescription.IPort<TPortID>> ports)
            where TPortID : IPortID
        {
            ushort index = 0;

            foreach (var p in ports)
            {
                if (p.Category == PortDescription.Category.DomainSpecific &&
                    p.Type == GetType())
                {
                    if (p.Equals(port))
                        break;
                    index++;
                }
            }
            return index;
        }

        public virtual void Dispose() { }
    }

    static class DSLTypeMap
    {
        static Dictionary<TypeHash, Func<IDSLHandler>> s_DSLFactory = new Dictionary<TypeHash, Func<IDSLHandler>>();

        static class PerTypeRegistry<TDSLHandler>
            where TDSLHandler : class, IDSLHandler, new()
        {
            static public readonly TypeHash Key = StaticHashNoRegister<TDSLHandler>();

            static PerTypeRegistry()
            {
                s_DSLFactory[Key] = () => new TDSLHandler();
            }
        }

        static public TypeHash RegisterDSL<TDSLHandler>()
            where TDSLHandler : class, IDSLHandler, new()
        {
            return PerTypeRegistry<TDSLHandler>.Key;
        }

        static public TypeHash StaticHashNoRegister<TDSLHandler>()
            where TDSLHandler : class, IDSLHandler, new()
        {
            return TypeHash.Create<TDSLHandler>();
        }

        static public IDSLHandler Instantiate(TypeHash hash)
        {
#if DFG_ASSERTIONS
            if(!s_DSLFactory.ContainsKey(hash))
                throw new AssertionException($"Unknown DSL for typehash {hash}");
#endif
            return s_DSLFactory[hash]();
        }
    }

    public partial class NodeSetAPI
    {
        Dictionary<TypeHash, IDSLHandler> m_ConnectionHandlerMap = new Dictionary<TypeHash, IDSLHandler>();

        internal IDSLHandler GetDSLHandler(TypeHash type)
        {
            if (!m_ConnectionHandlerMap.TryGetValue(type, out IDSLHandler handler))
            {
                handler = m_ConnectionHandlerMap[type] = DSLTypeMap.Instantiate(type);
            }

            return handler;
        }
    }
}


