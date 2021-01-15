using System;

namespace Unity.DataFlowGraph
{
    class InvalidDefinitionSlot : NodeDefinition
    {
        struct DummyPorts : ISimulationPortDefinition { }
        protected internal sealed override void Dispose() => throw new NotImplementedException();

        struct NodeData : INodeData, IInit, IDestroy, IUpdate
        {
            public void Init(InitContext ctx) => throw new NotImplementedException();
            public void Destroy(DestroyContext ctx) => throw new NotImplementedException();
            public void Update(UpdateContext ctx) => throw new NotImplementedException();
        }
    }
}
