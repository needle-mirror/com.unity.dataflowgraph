using System;
using System.Diagnostics;

namespace Unity.DataFlowGraph
{

    public struct NodeAdapter
    {
        public NodeInterfaceLink<TInterface> To<TInterface>()
        {
            if (!(m_Set.GetFunctionality(m_Handle) is TInterface))
                throw new InvalidCastException($"Node could not be interpreted as {typeof(TInterface).Name}");

            return new NodeInterfaceLink<TInterface>() { m_Handle = m_Handle };
        }

        internal NodeHandle m_Handle;
        internal NodeSet m_Set;
    }

    public struct NodeAdapter<TDefinition>
        where TDefinition : INodeDefinition, new()
    {
        public NodeInterfaceLink<TInterface, TDefinition> To<TInterface>()
        {
            if (!(m_Set.GetFunctionality(m_Handle) is TInterface))
                throw new InvalidCastException($"Node {typeof(TDefinition).Name} could not be interpreted as {typeof(TInterface).Name}");

            return new NodeInterfaceLink<TInterface, TDefinition>() { m_Handle = m_Handle };
        }

        internal NodeHandle<TDefinition> m_Handle;
        internal NodeSet m_Set;
    }

    public partial class NodeSet
    {
        public NodeAdapter<TDefinition> Adapt<TDefinition>(NodeHandle<TDefinition> n)
            where TDefinition : INodeDefinition, new() => new NodeAdapter<TDefinition>() { m_Set = this, m_Handle = n };
        public NodeAdapter Adapt(NodeHandle n) => new NodeAdapter() { m_Set = this, m_Handle = n };
    }

    [DebuggerDisplay("{m_Handle, nq}")]
    public struct NodeInterfaceLink<TInterface>
    {
        internal NodeHandle m_Handle;

        public static implicit operator NodeHandle(NodeInterfaceLink<TInterface> handle)
        {
            return handle.m_Handle;
        }
    }

    [DebuggerDisplay("{m_Handle, nq}")]
    public struct NodeInterfaceLink<TInterface, TDefinition>
        where TDefinition : INodeDefinition
    {
        public static implicit operator NodeInterfaceLink<TInterface>(NodeInterfaceLink<TInterface, TDefinition> n)
        {
            return new NodeInterfaceLink<TInterface> { m_Handle = n.m_Handle };
        }

        public static implicit operator NodeInterfaceLink<TInterface, TDefinition>(NodeHandle<TDefinition> n)
        {
            return new NodeInterfaceLink<TInterface, TDefinition> { m_Handle = n };
        }

        public static implicit operator NodeHandle(NodeInterfaceLink<TInterface, TDefinition> handle)
        {
            return handle.m_Handle;
        }

        internal NodeHandle<TDefinition> m_Handle;
    }
}
