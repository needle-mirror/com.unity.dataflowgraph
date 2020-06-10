using System;

namespace Unity.DataFlowGraph
{
    class InvalidDefinitionSlot : NodeDefinition
    {
        struct DummyPorts : ISimulationPortDefinition { }
        protected internal sealed override void Destroy(DestroyContext ctx) => throw new NotImplementedException();
        protected internal sealed override void Dispose() => throw new NotImplementedException();
        protected internal sealed override void Init(InitContext ctx) => throw new NotImplementedException();
        protected internal sealed override void OnUpdate(in UpdateContext ctx) => throw new NotImplementedException();
    }
}
