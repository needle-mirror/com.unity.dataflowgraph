using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;

namespace Unity.DataFlowGraph.Examples.RenderGraph
{
    public class DirectionRotator : SimulationKernelNodeDefinition<DirectionRotator.SimPorts, DirectionRotator.KernelDefs>
    {
        [Managed]
        struct NodeData : INodeData, IMsgHandler<float>, IMsgHandler<Transform>, IInit, IUpdate, IDestroy
        {
            Transform m_OutputTransform;
            GraphValue<float3> m_Output;

            public void Init(InitContext ctx)
            {
                m_Output = ctx.Set.CreateGraphValue(ctx.Set.CastHandle<DirectionRotator>(ctx.Handle), KernelPorts.Output);
                ctx.RegisterForUpdate();
            }

            public void Destroy(DestroyContext ctx) => ctx.Set.ReleaseGraphValue(m_Output);
            public void Update(UpdateContext ctx) => m_OutputTransform.position = ctx.Set.GetValueBlocking(m_Output);
            public void HandleMessage(MessageContext ctx, in float msg) => ctx.UpdateKernelData(new KernelData { Magnitude = msg });
            public void HandleMessage(MessageContext ctx, in Transform msg) => m_OutputTransform = msg;
        }

        struct KernelData : IKernelData
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
        struct GraphKernel : IGraphKernel<KernelData, KernelDefs>
        {
            public void Execute(RenderContext ctx, in KernelData data, ref KernelDefs ports)
            {
                var rotation = quaternion.AxisAngle(new float3(0, 1, 0), data.Magnitude);
                ctx.Resolve(ref ports.Output) = math.mul(rotation, ctx.Resolve(ports.Input));
            }
        }
    }
}
