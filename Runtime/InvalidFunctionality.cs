using System;

namespace Unity.DataFlowGraph
{
    class InvalidDefinitionSlot : NodeDefinition
    {
        protected internal sealed override NodeTraitsBase BaseTraits => throw new NotImplementedException();
        protected internal sealed override void Destroy(NodeHandle handle) => throw new NotImplementedException();
        protected internal sealed override void Dispose() => throw new NotImplementedException();
        protected internal sealed override void Init(InitContext ctx) => throw new NotImplementedException();
        protected internal sealed override void OnUpdate(in UpdateContext ctx) => throw new NotImplementedException();
    }
}
