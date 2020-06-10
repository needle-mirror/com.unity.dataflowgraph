using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity.DataFlowGraph
{
    public interface IDSLHandler : IDisposable
    {
        void Connect(NodeSet set, NodeHandle source, OutputPortID sourcePort, NodeHandle destination, InputPortID destinationPort);
        void Disconnect(NodeSet set, NodeHandle source, OutputPortID sourcePort, NodeHandle destination, InputPortID destinationPort);
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

        public void Connect(NodeSet set, NodeHandle source, OutputPortID sourcePort, NodeHandle destination, InputPortID destinationPort)
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

        public void Disconnect(NodeSet set, NodeHandle source, OutputPortID sourcePort, NodeHandle destination, InputPortID destinationPort)
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

    public partial class NodeSet
    {
        // TODO: Change all this to use type index lookup instead of associative lookup

        Dictionary<Type, IDSLHandler> m_ConnectionHandlerMap = new Dictionary<Type, IDSLHandler>();

        internal IDSLHandler GetDSLHandler(Type type)
        {
            if (!m_ConnectionHandlerMap.TryGetValue(type, out IDSLHandler handler))
            {
                // FIXME: IWBN to get rid of this variant of GetDSLHandler taking a runtime type and exclusively use
                // the generic method version which has a constraint requiring interface IDSLHandler
                if (!typeof(IDSLHandler).IsAssignableFrom(type))
                    throw new ArgumentException($"Cannot get DSL handler for non IDSLHandler type ({type})");

                handler = m_ConnectionHandlerMap[type] = (IDSLHandler)Activator.CreateInstance(type);
            }

            return handler;
        }

        public TDSLHandler GetDSLHandler<TDSLHandler>()
            where TDSLHandler : class, IDSLHandler
        {
            return (TDSLHandler)GetDSLHandler(typeof(TDSLHandler));
        }
    }
}


