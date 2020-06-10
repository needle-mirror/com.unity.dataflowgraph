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
            public void Execute(RenderContext ctx, KernelData data, ref KernelDefs ports) { }
        }
    }

    class SimulationKernelNode : SimulationKernelNodeDefinition<SimulationKernelNode.SimPorts, SimulationKernelNode.KernelDefs>
    {
        public struct KernelDefs : IKernelPortDefinition { }
        public struct SimPorts : ISimulationPortDefinition { }

        public struct KernelData : IKernelData { }

        public struct GraphKernel : IGraphKernel<KernelData, KernelDefs>
        {
            public void Execute(RenderContext ctx, KernelData data, ref KernelDefs ports) { }
        }
    }

    class Scaffold_1_Node : NodeDefinition<Scaffold_2_Node.SimPorts>
    {
        public struct SimPorts : ISimulationPortDefinition { }
    }

    class Scaffold_2_Node : NodeDefinition<Scaffold_2_Node.NodeData, Scaffold_2_Node.SimPorts>
    {
        public struct SimPorts : ISimulationPortDefinition { }
        public struct NodeData : INodeData { }
    }

    class Scaffold_3_Node : NodeDefinition<Scaffold_3_Node.KernelData, Scaffold_3_Node.KernelDefs, Scaffold_3_Node.GraphKernel>
    {
        public struct KernelDefs : IKernelPortDefinition { }

        public struct KernelData : IKernelData { }

        public struct GraphKernel : IGraphKernel<KernelData, KernelDefs>
        {
            public void Execute(RenderContext ctx, KernelData data, ref KernelDefs ports) { }
        }
    }

    class Scaffold_4_Node : NodeDefinition<Scaffold_4_Node.NodeData, Scaffold_4_Node.KernelData, Scaffold_4_Node.KernelDefs, Scaffold_4_Node.GraphKernel>
    {
        public struct NodeData : INodeData { }

        public struct KernelDefs : IKernelPortDefinition { }

        public struct KernelData : IKernelData { }

        public struct GraphKernel : IGraphKernel<KernelData, KernelDefs>
        {
            public void Execute(RenderContext ctx, KernelData data, ref KernelDefs ports) { }
        }
    }

    class Scaffold_5_Node : NodeDefinition<Scaffold_5_Node.NodeData, Scaffold_5_Node.SimPorts, Scaffold_5_Node.KernelData, Scaffold_5_Node.KernelDefs, Scaffold_5_Node.GraphKernel>
    {
        public struct NodeData : INodeData { }
        public struct SimPorts : ISimulationPortDefinition { }

        public struct KernelDefs : IKernelPortDefinition { }

        public struct KernelData : IKernelData { }

        public struct GraphKernel : IGraphKernel<KernelData, KernelDefs>
        {
            public void Execute(RenderContext ctx, KernelData data, ref KernelDefs ports) { }
        }
    }
        
}
