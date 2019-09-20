using System;

namespace Unity.DataFlowGraph
{
    class InvalidFunctionalitySlot : INodeDefinition
    {
        public NodeSet Set { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public NodeTraitsBase BaseTraits => throw new NotImplementedException();
        public void Destroy(NodeHandle handle) => throw new NotImplementedException();
        public void Dispose() => throw new NotImplementedException();
        public void GeneratePortDescriptions() => throw new NotImplementedException();
        public LLTraitsHandle CreateNodeTraits() => throw new NotImplementedException();
        public PortDescription GetPortDescription(NodeHandle handle) => throw new NotImplementedException();
        public void Init(InitContext ctx) => throw new NotImplementedException();
        public void OnMessage<T>(in MessageContext ctx, in T msg) => throw new NotImplementedException();
        public void OnUpdate(NodeHandle handle) => throw new NotImplementedException();
    }
}
