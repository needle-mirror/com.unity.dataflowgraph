using System;

namespace Unity.DataFlowGraph
{
    [Obsolete("Renamed, use Unity.DataFlowGraph.PortDescription.Category instead.", true)]
    public enum Usage
    {
        Message,
        Data,
        DomainSpecific
    }

    [Obsolete("Change of namespace, use Unity.DataFlowGraph.NodeSet.RenderExecutionModel instead.", true)]
    public enum RenderExecutionModel
    {
        MaximallyParallel,
        SingleThreaded,
        Synchronous,
        Islands
    }

    public partial class NodeSet
    {
        [Obsolete("Renamed to GetDefinition.", true)]
        public NodeDefinition GetFunctionality(NodeHandle handle) => throw new NotImplementedException();
        [Obsolete("Renamed to GetDefinition.", true)]
        public NodeDefinition GetFunctionality<T>() => throw new NotImplementedException();
        [Obsolete("Renamed to GetDefinition.", true)]
        public TDefinition GetFunctionality<TDefinition>(NodeHandle<TDefinition> handle)
            where TDefinition : NodeDefinition, new()
            => throw new NotImplementedException();
    }
}
