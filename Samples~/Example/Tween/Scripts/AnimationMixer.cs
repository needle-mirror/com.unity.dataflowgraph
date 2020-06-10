using Unity.Burst;
using Unity.Mathematics;

namespace Unity.DataFlowGraph.Examples.RenderGraph
{
    
    public class AnimationMixer 
        : NodeDefinition<AnimationMixer.NodeData, AnimationMixer.SimPorts, AnimationMixer.KernelData, AnimationMixer.KernelDefs, AnimationMixer.GraphKernel>
        , IMsgHandler<float>
    {
        public struct NodeData : INodeData { }

        public struct KernelData : IKernelData
        {
            public float Blend;
        }

        public struct KernelDefs : IKernelPortDefinition
        {
            public DataInput<AnimationMixer, float3> InputA, InputB;
            public DataOutput<AnimationMixer, float3> Output;
        }

        public struct SimPorts : ISimulationPortDefinition
        {
            public MessageInput<AnimationMixer, float> Blend;
        }

        [BurstCompile]
        public struct GraphKernel : IGraphKernel<KernelData, KernelDefs>
        {
            public void Execute(RenderContext ctx, KernelData data, ref KernelDefs ports)
            {
                ctx.Resolve(ref ports.Output) = math.lerp(ctx.Resolve(ports.InputA), ctx.Resolve(ports.InputB), data.Blend);
            }
        }

        public void HandleMessage(in MessageContext ctx, in float msg)
        {
            if (ctx.Port == SimulationPorts.Blend)
                GetKernelData(ctx.Handle).Blend = msg;
        }
    }
}
