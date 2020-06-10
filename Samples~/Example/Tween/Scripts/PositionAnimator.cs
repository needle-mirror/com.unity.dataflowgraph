using Unity.Burst;
using Unity.Mathematics;

namespace Unity.DataFlowGraph.Examples.RenderGraph
{
    public class PositionAnimator 
        : NodeDefinition<PositionAnimator.NodeData, PositionAnimator.SimPorts, PositionAnimator.KernelData, PositionAnimator.KernelDefs, PositionAnimator.GraphKernel>
        , IMsgHandler<float>
        , IMsgHandler<float3>
    {
        public struct NodeData : INodeData
        {
            public float Speed;
        }

        public struct KernelData : IKernelData
        {
            public float Time;
            public float3 Mask, Translation;
        }

        public struct KernelDefs : IKernelPortDefinition
        {
            public DataOutput<PositionAnimator, float3> Output;
        }

        public struct SimPorts : ISimulationPortDefinition
        {
            public MessageInput<PositionAnimator, float> Time;
            public MessageInput<PositionAnimator, float> Speed;
            public MessageInput<PositionAnimator, float3> Movement;
        }

        [BurstCompile]
        public struct GraphKernel : IGraphKernel<KernelData, KernelDefs>
        {
            public void Execute(RenderContext ctx, KernelData data, ref KernelDefs ports)
            {
                math.sincos(data.Time, out float x, out float y);
                ctx.Resolve(ref ports.Output) = data.Mask * data.Translation * new float3(x, y, 0);
            }
        }

        protected override void OnUpdate(in UpdateContext ctx)
        {
            GetKernelData(ctx.Handle).Time += UnityEngine.Time.deltaTime * GetNodeData(ctx.Handle).Speed;
        }

        public void HandleMessage(in MessageContext ctx, in float msg)
        {
            if (ctx.Port == SimulationPorts.Speed)
            {
                GetNodeData(ctx.Handle).Speed = msg;
            }
            else if (ctx.Port == SimulationPorts.Time)
            {
                GetKernelData(ctx.Handle).Time = msg;
            }
        }

        public void HandleMessage(in MessageContext ctx, in float3 msg)
        {
            if (ctx.Port == SimulationPorts.Movement)
            {
                ref var kernelData = ref GetKernelData(ctx.Handle);
                kernelData.Translation = msg;
                kernelData.Mask = math.normalize(msg);
            }
        }
    }
}
