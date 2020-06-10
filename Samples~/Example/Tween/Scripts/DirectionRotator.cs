using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;

namespace Unity.DataFlowGraph.Examples.RenderGraph
{
    public class DirectionRotator 
        : NodeDefinition<DirectionRotator.NodeData, DirectionRotator.SimPorts, DirectionRotator.KernelData, DirectionRotator.KernelDefs, DirectionRotator.GraphKernel>
        , IMsgHandler<float>
        , IMsgHandler<Transform>

    {
        [Managed]
        public struct NodeData : INodeData
        {
            public Transform OutputTransform;
            public GraphValue<float3> Output;
        }

        public struct KernelData : IKernelData
        {
            public float Magnitude;
        }

        public struct KernelDefs : IKernelPortDefinition
        {
            public DataInput<DirectionRotator, float3> Input;
            public DataOutput<DirectionRotator, float3> Output;
        }

        public struct SimPorts : ISimulationPortDefinition
        {
            public MessageInput<DirectionRotator, float> Magnitude;
            public MessageInput<DirectionRotator, Transform> TransformTarget;
        }

        [BurstCompile]
        public struct GraphKernel : IGraphKernel<KernelData, KernelDefs>
        {
            public void Execute(RenderContext ctx, KernelData data, ref KernelDefs ports)
            {
                var rotation = quaternion.AxisAngle(new float3(0, 1, 0), data.Magnitude);
                ctx.Resolve(ref ports.Output) = math.mul(rotation, ctx.Resolve(ports.Input));
            }
        }

        protected override void Init(InitContext ctx)
        {
            GetNodeData(ctx.Handle).Output = Set.CreateGraphValue(Set.CastHandle<DirectionRotator>(ctx.Handle), KernelPorts.Output);
        }

        protected override void Destroy(DestroyContext ctx)
        {
            Set.ReleaseGraphValue(GetNodeData(ctx.Handle).Output);
        }

        protected override void OnUpdate(in UpdateContext ctx)
        {
            ref var data = ref GetNodeData(ctx.Handle);
            data.OutputTransform.position = Set.GetValueBlocking(data.Output);
        }
        
        public void HandleMessage(in MessageContext ctx, in float msg)
        {
            if(ctx.Port == SimulationPorts.Magnitude)
            {
                GetKernelData(ctx.Handle).Magnitude = msg;
            }
        }

        public void HandleMessage(in MessageContext ctx, in Transform msg)
        {
            if (ctx.Port == SimulationPorts.TransformTarget)
                GetNodeData(ctx.Handle).OutputTransform = msg;
        }
    }
}
