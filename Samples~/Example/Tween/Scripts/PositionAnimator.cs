﻿using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;

namespace Unity.DataFlowGraph.Examples.RenderGraph
{
    public class PositionAnimator : SimulationKernelNodeDefinition<PositionAnimator.SimPorts, PositionAnimator.KernelDefs>
    {
        public struct NodeData : INodeData, IUpdate, IMsgHandler<float>, IMsgHandler<float3>
        {
            public float Speed;
            KernelData m_Data;

            public void Update(in UpdateContext ctx)
            {
                m_Data.Time += Time.deltaTime * Speed;
                ctx.UpdateKernelData(m_Data);
            }

            public void HandleMessage(in MessageContext ctx, in float msg)
            {
                if (ctx.Port == SimulationPorts.Speed)
                    Speed = msg;
                else if (ctx.Port == SimulationPorts.Time)
                    m_Data.Time = msg;
            }

            public void HandleMessage(in MessageContext ctx, in float3 msg)
            {
                m_Data.Translation = msg;
                m_Data.Mask = math.normalize(msg);
            }
        }

        struct KernelData : IKernelData
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
        struct GraphKernel : IGraphKernel<KernelData, KernelDefs>
        {
            public void Execute(RenderContext ctx, KernelData data, ref KernelDefs ports)
            {
                math.sincos(data.Time, out float x, out float y);
                ctx.Resolve(ref ports.Output) = data.Mask * data.Translation * new float3(x, y, 0);
            }
        }


    }
}
