using Unity.Burst;
using Unity.Mathematics;

namespace Unity.DataFlowGraph.Examples.RenderGraph
{
    
    public class AnimationMixer : SimulationKernelNodeDefinition<AnimationMixer.SimPorts, AnimationMixer.KernelDefs>
    {
        struct NodeData : INodeData, IMsgHandler<float>
        {
            public void HandleMessage(MessageContext ctx, in float msg)
                => ctx.UpdateKernelData(new KernelData { Blend = msg });
        }

        struct KernelData : IKernelData
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
        struct GraphKernel : IGraphKernel<KernelData, KernelDefs>
        {
            public void Execute(RenderContext ctx, in KernelData data, ref KernelDefs ports)
            {
                ctx.Resolve(ref ports.Output) = math.lerp(ctx.Resolve(ports.InputA), ctx.Resolve(ports.InputB), data.Blend);
            }
        }
    }
}
