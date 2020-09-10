using System;
using System.Diagnostics;

namespace Unity.DataFlowGraph
{

    public struct NodeAdapter
    {
        public NodeInterfaceLink<TInterface> To<TInterface>()
        {
            if (!(Set.GetDefinition(TypedHandle) is TInterface))
                throw new InvalidCastException($"Node could not be interpreted as {typeof(TInterface).Name}");

            return new NodeInterfaceLink<TInterface>() { TypedHandle = TypedHandle };
        }

        internal NodeHandle TypedHandle;
        internal NodeSetAPI Set;
    }

    public struct NodeAdapter<TDefinition>
        where TDefinition : NodeDefinition, new()
    {
        public NodeInterfaceLink<TInterface, TDefinition> To<TInterface>()
        {
            if (!(Set.GetDefinition(m_Handle) is TInterface))
                throw new InvalidCastException($"Node {typeof(TDefinition).Name} could not be interpreted as {typeof(TInterface).Name}");

            return new NodeInterfaceLink<TInterface, TDefinition>() { TypedHandle = m_Handle };
        }

        internal NodeHandle<TDefinition> m_Handle;
        internal NodeSetAPI Set;
    }

    public partial class NodeSetAPI
    {
        public NodeAdapter<TDefinition> Adapt<TDefinition>(NodeHandle<TDefinition> n)
            where TDefinition : NodeDefinition, new() => new NodeAdapter<TDefinition>() { Set = this, m_Handle = n };
        public NodeAdapter Adapt(NodeHandle n) => new NodeAdapter() { Set = this, TypedHandle = n };
    }

    [DebuggerDisplay("{TypedHandle, nq}")]
    public struct NodeInterfaceLink<TInterface>
    {
        internal NodeHandle TypedHandle;

        public static implicit operator NodeHandle(NodeInterfaceLink<TInterface> handle)
        {
            return handle.TypedHandle;
        }
    }

    [DebuggerDisplay("{TypedHandle, nq}")]
    public struct NodeInterfaceLink<TInterface, TDefinition>
        where TDefinition : NodeDefinition
    {
        public static implicit operator NodeInterfaceLink<TInterface>(NodeInterfaceLink<TInterface, TDefinition> n)
        {
            return new NodeInterfaceLink<TInterface> { TypedHandle = n.TypedHandle };
        }

        public static implicit operator NodeInterfaceLink<TInterface, TDefinition>(NodeHandle<TDefinition> n)
        {
            return new NodeInterfaceLink<TInterface, TDefinition> { TypedHandle = n };
        }

        public static implicit operator NodeHandle(NodeInterfaceLink<TInterface, TDefinition> handle)
        {
            return handle.TypedHandle;
        }

        internal NodeHandle<TDefinition> TypedHandle;
    }
}
