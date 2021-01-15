using Unity.Burst;
using UnityEngine;
using Unity.Mathematics;
using System.Collections.Generic;
using System;

namespace Unity.DataFlowGraph.PolynomialCubeRotatorExample
{
    /// <summary>
    /// This is a runnable example of analytical N-degree polynomial system for generating interesting animation patterns.
    /// It showcases how to use buffers as a data protocol between nodes, as well.
    /// </summary>
    public class PolynomialCubeRotator : MonoBehaviour
    {
        public struct Polynomial
        {
            public Buffer<float> Coefficients;
        }

        public class AdderNode : KernelNodeDefinition<AdderNode.KernelDefs>
        {
            public struct KernelDefs : IKernelPortDefinition
            {
                public DataInput<AdderNode, Polynomial> A;
                public DataInput<AdderNode, Polynomial> B;
                
                public DataOutput<AdderNode, Polynomial> Output;
            }

            struct KernelData : IKernelData { }

            [BurstCompile]
            struct GraphKernel : IGraphKernel<KernelData, KernelDefs>
            {
                public void Execute(RenderContext ctx, in KernelData data, ref KernelDefs ports)
                {
                    var a = ctx.Resolve(ports.A);
                    var b = ctx.Resolve(ports.B);
                    var output = ctx.Resolve(ref ports.Output);

                    var resultPolym = output.Coefficients.ToNative(ctx);
                    var aPolym = a.Coefficients.ToNative(ctx);
                    var bPolym = b.Coefficients.ToNative(ctx);

                    for(int i = 0; i < resultPolym.Length; ++i)
                    {
                        resultPolym[i] = aPolym[i] + bPolym[i];
                    }

                }
            }
        }
        
        public class EvaluatorNode : KernelNodeDefinition<EvaluatorNode.KernelDefs>
        {
            public struct KernelDefs : IKernelPortDefinition
            {
                public DataInput<EvaluatorNode, Polynomial> Input;
                public DataInput<EvaluatorNode, float> X;

                public DataOutput<EvaluatorNode, float> Y;
            }

            struct KernelData : IKernelData { }

            [BurstCompile]
            struct GraphKernel : IGraphKernel<KernelData, KernelDefs>
            {
                public void Execute(RenderContext ctx, in KernelData data, ref KernelDefs ports)
                {
                    /*
                     * Sum[a0 + a1*x^1 .. + aN*x^N]
                     */

                    var input = ctx.Resolve(ports.Input);
                    var x = ctx.Resolve(ports.X);
                    ref var y = ref ctx.Resolve(ref ports.Y);
                    
                    var coeffs = input.Coefficients.ToNative(ctx);

                    y = 0;

                    for (int i = 0; i < coeffs.Length; ++i)
                    {
                        y += coeffs[i] * math.pow(x, i);
                    }
                }
            }
        }

        public class ProceduralGeneratorNode : KernelNodeDefinition<ProceduralGeneratorNode.KernelDefs>
        {
            public struct KernelDefs : IKernelPortDefinition
            {
                public DataOutput<ProceduralGeneratorNode, Polynomial> Polym;
            }

            struct KernelData : IKernelData
            {
                public Mathematics.Random RNG;
            }

            struct NodeData : INodeData, IInit
            {
                public void Init(InitContext ctx)
                {
                    ctx.UpdateKernelData(
                        new KernelData {
                            RNG = new Mathematics.Random((uint)UnityEngine.Random.Range(1, 127))
                        }
                    );
                }
            }

            [BurstCompile]
            struct GraphKernel : IGraphKernel<KernelData, KernelDefs>
            {
                public void Execute(RenderContext ctx, in KernelData data, ref KernelDefs ports)
                {
                    /*
                     * Expand[Rng[-2^x, 2^x], N]
                     */

                    ref var polym = ref ctx.Resolve(ref ports.Polym);
                    var coeffs = polym.Coefficients.ToNative(ctx);
                    int term = 1;

                    for (int i = 0; i < coeffs.Length; ++i)
                    {
                        term <<= 1;
                        coeffs[i] = data.RNG.NextFloat(-term, term);
                    }
                }
            }
        }
        

        public class PhaseNode : SimulationKernelNodeDefinition<PhaseNode.SimPorts, PhaseNode.KernelDefs>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
                public MessageInput<PhaseNode, float> Time, Scale;
            }

            struct InstanceData : INodeData, IMsgHandler<float>, IUpdate, IInit
            {
                float m_Time, m_TimeScale;

                public void Init(InitContext ctx)
                {
                    ctx.RegisterForUpdate();
                }

                public void HandleMessage(MessageContext ctx, in float msg)
                {
                    if (ctx.Port == SimulationPorts.Time)
                        m_Time = msg;
                    else if (ctx.Port == SimulationPorts.Scale)
                        m_TimeScale = msg;
                }

                public void Update(UpdateContext ctx)
                {
                    m_Time += Time.deltaTime * m_TimeScale;
                    ctx.UpdateKernelData ( new KernelData { Time = m_Time });
                }
            }

            struct KernelData : IKernelData
            {
                public float Time;
            }

            public struct KernelDefs : IKernelPortDefinition
            {
                public DataOutput<PhaseNode, float> X;
            }

            [BurstCompile]
            struct GraphKernel : IGraphKernel<KernelData, KernelDefs>
            {
                public void Execute(RenderContext ctx, in KernelData data, ref KernelDefs ports)
                    => ctx.Resolve(ref ports.X) = math.sin(data.Time);
            }


        }

        struct LocalGraph : IDisposable
        {
            List<NodeHandle> m_Nodes;
            NodeSet m_Set;
            GraphValue<float> m_Result;

            public LocalGraph(NodeSet parent, NodeHandle<PhaseNode> phase, int degree)
            {
                m_Nodes = new List<NodeHandle>();
                m_Set = parent;

                var polym1 = m_Set.Create<ProceduralGeneratorNode>();
                var polym2 = m_Set.Create<ProceduralGeneratorNode>();

                var mixer = m_Set.Create<AdderNode>();
                var eval = m_Set.Create<EvaluatorNode>();

                m_Set.Connect(polym1, ProceduralGeneratorNode.KernelPorts.Polym, mixer, AdderNode.KernelPorts.A);
                m_Set.Connect(polym2, ProceduralGeneratorNode.KernelPorts.Polym, mixer, AdderNode.KernelPorts.B);

                m_Set.Connect(mixer, AdderNode.KernelPorts.Output, eval, EvaluatorNode.KernelPorts.Input);
                m_Set.Connect(phase, PhaseNode.KernelPorts.X, eval, EvaluatorNode.KernelPorts.X);

                var bufferSize = new Polynomial
                {
                    Coefficients = Buffer<float>.SizeRequest(degree)
                };

                m_Set.SetBufferSize(polym1, ProceduralGeneratorNode.KernelPorts.Polym, bufferSize);
                m_Set.SetBufferSize(polym2, ProceduralGeneratorNode.KernelPorts.Polym, bufferSize);
                m_Set.SetBufferSize(mixer, AdderNode.KernelPorts.Output, bufferSize);

                m_Result = m_Set.CreateGraphValue(eval, EvaluatorNode.KernelPorts.Y);

                m_Nodes.AddRange(new NodeHandle[] { polym1, polym2, mixer, eval });
            }

            public void Dispose()
            {
                var set = m_Set;
                m_Nodes.ForEach(n => set.Destroy(n));
                m_Nodes.Clear();
                m_Set.ReleaseGraphValue(m_Result);
            }

            public float GetResult()
            {
                return m_Set.GetValueBlocking(m_Result);
            }
        }

        public int NumCubes = 11;

        NodeSet m_Set;
        NodeHandle<PhaseNode> m_Phase;

        List<(GameObject GameObject, LocalGraph Graph)> m_Cubes = new List<(GameObject, LocalGraph)>();
        LocalGraph m_GlobalRotation;
        LocalGraph m_Colour;

        float m_Speed = 0.2f;

        void OnEnable()
        {
            m_Set = new NodeSet();
            m_Phase = m_Set.Create<PhaseNode>();
            (m_GlobalRotation, m_Colour) = (new LocalGraph(m_Set, m_Phase, 9), new LocalGraph(m_Set, m_Phase, 5));

            for(int i = 0; i < NumCubes; ++i)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.parent = transform;
                var x = i - (NumCubes - 1) * 0.5f;
                cube.transform.localPosition = new Vector3(x, 0);
                m_Cubes.Add((cube, new LocalGraph(m_Set, m_Phase, 12)));
            }

            m_Set.SendMessage(m_Phase, PhaseNode.SimulationPorts.Scale, m_Speed);
        }

        void Update()
        {
            // Note we call .GetResult() before scheduling the next update. This means we don't
            // have to immediately wait on the results, instead we read the results from the previous
            // frame. This introduces a delay, but is much easier on the scheduler and CPU.
            // Alternatively, implement this directly inside an ECS system for optimal execution
            // together with a GraphValueResolver.
            var colour = Color.HSVToRGB(math.abs(math.sin(m_Colour.GetResult())), 0.5f, 0.5f);
            var globalRotation = m_Colour.GetResult();

            m_Cubes.ForEach(c =>
            {
                var angle = c.Graph.GetResult();
                c.GameObject.transform.rotation = Quaternion.Euler(0, Mathf.PI * angle, 0);
                c.GameObject.GetComponent<MeshRenderer>().material.color = colour;
            });

            transform.rotation = Quaternion.Euler(0, Mathf.PI * globalRotation, 0);

            m_Set.Update();
        }

        void OnGUI()
        {
            if(GUI.Button(new Rect(5, 5, 100, 20), "Reset"))
            {
                m_Set.SendMessage(m_Phase, PhaseNode.SimulationPorts.Time, 0);
            }

            var newSpeed = GUI.HorizontalSlider(new Rect(5, 25, 100, 20), m_Speed, 0.001f, 1f);

            if(newSpeed != m_Speed)
            {
                m_Speed = newSpeed;
                m_Set.SendMessage(m_Phase, PhaseNode.SimulationPorts.Scale, m_Speed);
            }
            
        }

        void OnDisable()
        {
            m_Cubes.ForEach(c =>
            {
                Destroy(c.GameObject);
                c.Graph.Dispose();
            });

            m_Colour.Dispose();
            m_GlobalRotation.Dispose();
            m_Set.Destroy(m_Phase);
            m_Set.Dispose();
        }

    }
}
