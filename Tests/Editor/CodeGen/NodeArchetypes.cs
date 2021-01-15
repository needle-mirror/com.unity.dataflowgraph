namespace Unity.DataFlowGraph.CodeGen.Tests.Archetypes
{
    class NakedNode : NodeDefinition
    {
        public struct SimPorts : ISimulationPortDefinition { }
    }

    class SimulationNode : SimulationNodeDefinition<SimulationNode.SimPorts>
    {
        public struct SimPorts : ISimulationPortDefinition { }
    }

    class KernelNode : KernelNodeDefinition<KernelNode.KernelDefs>
    {
        public struct KernelDefs : IKernelPortDefinition { }

        public struct KernelData : IKernelData { }

        public struct GraphKernel : IGraphKernel<KernelData, KernelDefs>
        {
            public void Execute(RenderContext ctx, in KernelData data, ref KernelDefs ports) { }
        }
    }

    class SimulationKernelNode : SimulationKernelNodeDefinition<SimulationKernelNode.SimPorts, SimulationKernelNode.KernelDefs>
    {
        public struct KernelDefs : IKernelPortDefinition { }
        public struct SimPorts : ISimulationPortDefinition { }

        public struct KernelData : IKernelData { }

        public struct GraphKernel : IGraphKernel<KernelData, KernelDefs>
        {
            public void Execute(RenderContext ctx, in KernelData data, ref KernelDefs ports) { }
        }
    }
}
