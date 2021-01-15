using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Burst;
using Unity.Jobs;

namespace Unity.DataFlowGraph.Tests
{
    public class RenderGraphTests
    {
        // TODO tests:
        // * Check assigning local output ports in a kernel to a non-ref variable will still update the output value

        public class PotentiallyJobifiedNodeSet : NodeSet
        {
            public PotentiallyJobifiedNodeSet(RenderExecutionModel type)
                : base()
            {
                RendererModel = type;
            }
        }

        public class KernelNode : KernelNodeDefinition<KernelNode.KernelDefs>
        {
            public struct KernelDefs : IKernelPortDefinition
            {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                public DataInput<KernelNode, int> Input;
                public DataOutput<KernelNode, int> Output;
#pragma warning restore 649
            }

            struct Data : IKernelData { }

            [BurstCompile(CompileSynchronously = true)]
            struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                public void Execute(RenderContext ctx, in Data data, ref KernelDefs ports) { }
            }
        }

        [Test]
        public void GraphCanUpdate_WithoutIssues([Values] NodeSet.RenderExecutionModel meansOfComputation)
        {
            using (var set = new PotentiallyJobifiedNodeSet(meansOfComputation))
            {
                NodeHandle<KernelNode>
                    a = set.Create<KernelNode>(),
                    b = set.Create<KernelNode>();

                set.Connect(a, KernelNode.KernelPorts.Output, b, KernelNode.KernelPorts.Input);
                set.Update();
                set.DataGraph.SyncAnyRendering();

                set.Destroy(a, b);
            }
        }

        [Test]
        public void GraphAccumulatesData_OverLongChains(
            [Values(2, 10, 30)] int nodeChainLength,
            [Values(NodeSet.RenderExecutionModel.Synchronous, NodeSet.RenderExecutionModel.MaximallyParallel)] NodeSet.RenderExecutionModel meansOfComputation)
        {
            using (var set = new PotentiallyJobifiedNodeSet(meansOfComputation))
            {
                var nodes = new List<NodeHandle<KernelAdderNode>>(nodeChainLength);
                var graphValues = new List<GraphValue<int>>(nodeChainLength);

                for (int i = 0; i < nodeChainLength; ++i)
                {
                    var node = set.Create<KernelAdderNode>();
                    nodes.Add(node);
                    graphValues.Add(set.CreateGraphValue(node, KernelAdderNode.KernelPorts.Output));
                }

                for (int i = 0; i < nodeChainLength - 1; ++i)
                {
                    set.Connect(nodes[i], KernelAdderNode.KernelPorts.Output, nodes[i + 1], KernelAdderNode.KernelPorts.Input);
                }

                set.Update();

                for (int i = 0; i < nodeChainLength; ++i)
                {
                    Assert.AreEqual(i + 1, set.GetValueBlocking(graphValues[i]));
                }

                for (int i = 0; i < nodeChainLength; ++i)
                {
                    set.ReleaseGraphValue(graphValues[i]);
                    set.Destroy(nodes[i]);
                }
            }
        }

        public class PersistentKernelNode : KernelNodeDefinition<PersistentKernelNode.KernelDefs>
        {
            public struct KernelDefs : IKernelPortDefinition
            {
                public DataOutput<PersistentKernelNode, int> Output;
            }

            struct Data : IKernelData { }

            [BurstCompile(CompileSynchronously = true)]
            struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                int m_State;

                public void Execute(RenderContext ctx, in Data data, ref KernelDefs ports)
                {
                    ctx.Resolve(ref ports.Output) = m_State++;
                }
            }
        }

        [Test]
        public void KernelNodeMemberMemory_IsPersistent_OverMultipleGraphEvaluations([Values] NodeSet.RenderExecutionModel meansOfComputation)
        {
            using (var set = new PotentiallyJobifiedNodeSet(meansOfComputation))
            {
                var node = set.Create<PersistentKernelNode>();
                var value = set.CreateGraphValue(node, PersistentKernelNode.KernelPorts.Output);

                for (int i = 0; i < 100; ++i)
                {
                    set.Update();

                    Assert.AreEqual(i, set.GetValueBlocking(value));
                }

                set.Destroy(node);
                set.ReleaseGraphValue(value);
            }
        }


        /*  DAG test diagram.
         *  Flow from left to right.
         *
         *  A ---------------- B (1)
         *  A -------- C ----- B (2)
         *           /   \
         *  A - B - B      C = C (3)
         *           \   /
         *  A -------- C ----- B (4)
         *  A                    (5)
         *
         *  Contains nodes not connected anywhere.
         *  Contains multiple children (tree), and multiple parents (DAG).
         *  Contains multiple connected components.
         *  Contains diamond.
         *  Contains more than one connection between the same nodes.
         *  Contains opportunities for batching, and executing paths in series.
         *  Contains multiple connections from the same output.
         */

        public struct DAGTest : IDisposable
        {
            public NodeHandle<ANode>[] Leaves;
            public NodeHandle[] Roots;
            public GraphValue<int>[] RootGVs;

            List<NodeHandle> m_GC;

            NodeSet m_Set;

            public DAGTest(NodeSet set)
            {
                m_GC = new List<NodeHandle>();
                m_Set = set;
                Leaves = new NodeHandle<ANode>[5];
                Roots = new NodeHandle[5];
                RootGVs = new GraphValue<int>[5];

                // Part (1) of the graph.
                Leaves[0] = set.Create<ANode>();
                var b1 = set.Create<BNode>();
                set.Connect(Leaves[0], ANode.KernelPorts.Output, b1, BNode.KernelPorts.Input);

                Roots[0] = b1;
                RootGVs[0] = set.CreateGraphValue(b1, BNode.KernelPorts.Output);

                // Part (2) of the graph.
                Leaves[1] = set.Create<ANode>();
                var c2 = set.Create<CNode>();
                var b2 = set.Create<BNode>();

                set.Connect(Leaves[1], ANode.KernelPorts.Output, c2, CNode.KernelPorts.InputA);
                set.Connect(c2, CNode.KernelPorts.Output, b2, BNode.KernelPorts.Input);

                Roots[1] = b2;
                RootGVs[1] = set.CreateGraphValue(b2, BNode.KernelPorts.Output);

                // Part (4) of the graph.
                Leaves[3] = set.Create<ANode>();
                var c4 = set.Create<CNode>();
                var b4 = set.Create<BNode>();

                set.Connect(Leaves[3], ANode.KernelPorts.Output, c4, CNode.KernelPorts.InputA);
                set.Connect(c4, CNode.KernelPorts.Output, b4, BNode.KernelPorts.Input);

                Roots[3] = b4;
                RootGVs[3] = set.CreateGraphValue(b4, BNode.KernelPorts.Output);

                // Part (3) of the graph.
                Leaves[2] = set.Create<ANode>();
                var b3_1 = set.Create<BNode>();
                var b3_2 = set.Create<BNode>();
                var c3_1 = set.Create<CNode>();
                var c3_2 = set.Create<CNode>();

                set.Connect(Leaves[2], ANode.KernelPorts.Output, b3_1, BNode.KernelPorts.Input);
                set.Connect(b3_1, BNode.KernelPorts.Output, b3_2, BNode.KernelPorts.Input);
                set.Connect(b3_2, BNode.KernelPorts.Output, c2, CNode.KernelPorts.InputB);
                set.Connect(b3_2, BNode.KernelPorts.Output, c4, CNode.KernelPorts.InputB);

                set.Connect(c2, CNode.KernelPorts.Output, c3_1, CNode.KernelPorts.InputA);
                set.Connect(c4, CNode.KernelPorts.Output, c3_1, CNode.KernelPorts.InputB);

                set.Connect(c3_1, CNode.KernelPorts.Output, c3_2, CNode.KernelPorts.InputA);
                set.Connect(c3_1, CNode.KernelPorts.Output, c3_2, CNode.KernelPorts.InputB);
                Roots[2] = c3_2;
                RootGVs[2] = set.CreateGraphValue(c3_2, CNode.KernelPorts.Output);

                // Part (5) of the graph.
                Leaves[4] = set.Create<ANode>();
                Roots[4] = Leaves[4];
                RootGVs[4] = set.CreateGraphValue(Leaves[4], ANode.KernelPorts.Output);

                GC(b1, c2, b2, c4, b4, b3_1, b3_2, c3_1, c3_2);
            }

            void GC(params NodeHandle[] handles)
            {
                m_GC.AddRange(handles);
            }

            public void SetLeafInputs(int value)
            {
                foreach (var leaf in Leaves)
                    m_Set.SetData(leaf, ANode.KernelPorts.ValueInput, value);
            }

            public void Dispose()
            {
                var set = m_Set;
                m_GC.ForEach(a => set.Destroy(a));
                Leaves.ToList().ForEach(l => set.Destroy(l));
                RootGVs.ToList().ForEach(r => set.ReleaseGraphValue(r));
            }
        }

        public interface IComputeNode
        {
            OutputPortID OutputPort { get; }
        }

        public class ANode : KernelNodeDefinition<ANode.KernelDefs>, IComputeNode
        {
            public struct KernelDefs : IKernelPortDefinition
            {
                public DataInput<ANode, int> ValueInput;
                public DataOutput<ANode, int> Output;
            }

            struct Data : IKernelData { }

            [BurstCompile(CompileSynchronously = true)]
            struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                public void Execute(RenderContext ctx, in Data data, ref KernelDefs ports) =>
                    ctx.Resolve(ref ports.Output) = ctx.Resolve(ports.ValueInput) + 1;
            }

            public OutputPortID OutputPort => (OutputPortID)KernelPorts.Output;
        }

        public class BNode : KernelNodeDefinition<BNode.KernelDefs>, IComputeNode
        {
            public struct KernelDefs : IKernelPortDefinition
            {
                public DataInput<BNode, int> Input;
                public DataOutput<BNode, int> Output;
            }

            struct Data : IKernelData { }

            [BurstCompile(CompileSynchronously = true)]
            struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                public void Execute(RenderContext ctx, in Data data, ref KernelDefs ports) => ctx.Resolve(ref ports.Output) = ctx.Resolve(ports.Input) * 3;
            }

            public OutputPortID OutputPort => (OutputPortID)KernelPorts.Output;
        }

        public class CNode : KernelNodeDefinition<CNode.KernelDefs>, IComputeNode
        {
            public struct KernelDefs : IKernelPortDefinition
            {
                public DataInput<CNode, int> InputA;
                public DataInput<CNode, int> InputB;
                public DataOutput<CNode, int> Output;
            }

            struct Data : IKernelData { }

            [BurstCompile(CompileSynchronously = true)]
            struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                public void Execute(RenderContext ctx, in Data data, ref KernelDefs ports) => ctx.Resolve(ref ports.Output) = ctx.Resolve(ports.InputA) + ctx.Resolve(ports.InputB);
            }

            public OutputPortID OutputPort => (OutputPortID)KernelPorts.Output;
        }

        [Test]
        public void ComplexDAG_ProducesExpectedResults_InAllExecutionModels([Values] NodeSet.RenderExecutionModel model)
        {
            const int k_NumGraphs = 10;

            for(int k = 0; k < 10; ++k)
            using (var set = new NodeSet())
            {
                set.RendererModel = model;

                var tests = new List<DAGTest>(k_NumGraphs);

                for (int i = 0; i < k_NumGraphs; ++i)
                {
                    tests.Add(new DAGTest(set));
                }

                for (int i = 0; i < k_NumGraphs; ++i)
                {
                    tests[i].SetLeafInputs(i);
                }

                set.Update();

                /*  A ---------------- B (1)
                 *  A -------- C ----- B (2)
                 *           /   \
                 *  A - B - B      C = C (3)
                 *           \   /
                 *  A -------- C ----- B (4)
                 *  A                    (5)
                 *
                 *  A = in + 1
                 *  B = in * 3
                 *  C = in1 + in2
                 *
                 */

                void CheckExpectedValueAtRoot(int expected, DAGTest graph, int root, int i)
                {
                    ref var value = ref set.GetOutputValues()[graph.RootGVs[root].Handle];
                    if (model == NodeSet.RenderExecutionModel.Synchronous)
                        Assert.AreEqual(set.DataGraph.ComputeDependency(value), new JobHandle());
                    else
                        Assert.AreNotEqual(set.DataGraph.ComputeDependency(value), new JobHandle());

                    var kernelNodes = set.DataGraph.GetInternalData();
                    Assert.True(RenderGraph.StillExists(ref kernelNodes, value.Source));

                    var output = set.GetValueBlocking(graph.RootGVs[root]);

                    if(expected != output)
                    {
                        System.Threading.Thread.Sleep(1000);

                        var laterOutput = set.GetValueBlocking(graph.RootGVs[0]);

                        Assert.AreEqual(
                            output,
                            laterOutput,
                            $"Root[{root}] produced a race condition in graph iteration {i}"
                        );

                        Assert.AreEqual(
                            expected,
                            output,
                            $"Root[0] produced unexpected results in graph iteration {i}"
                        );
                    }
                }

                for (int i = 0; i < k_NumGraphs; ++i)
                {
                    var graph = tests[i];
                    const int b = 3;
                    const int c = 2;

                    var a = (i + 1);
                    var abb = a * b * b;

                    CheckExpectedValueAtRoot(
                        a * b,
                        graph,
                        0,
                        i
                    );

                    CheckExpectedValueAtRoot(
                        (abb + a) * b,
                        graph,
                        1,
                        i
                    );

                    CheckExpectedValueAtRoot(
                        (abb + a) * c * c,
                        graph,
                        2,
                        i
                    );

                    CheckExpectedValueAtRoot(
                        (abb + a) * b,
                        graph,
                        3,
                        i
                    );

                    CheckExpectedValueAtRoot(
                        a,
                        graph,
                        4,
                        i
                    );
                }

                tests.ForEach(t => t.Dispose());
            }
        }

        public class SideEffectUserStructValueNode
            : SimulationKernelNodeDefinition<SideEffectUserStructValueNode.SimPorts, SideEffectUserStructValueNode.KernelDefs>
        {
            public struct KernelDefs : IKernelPortDefinition { }

            public struct SimPorts : ISimulationPortDefinition
            {
                public MessageInput<SideEffectUserStructValueNode, int> Input;
            }

            public struct Data : IKernelData
            {
                public int value;
#pragma warning disable 649  // never assigned
                public int __fake;
#pragma warning restore 649
            }

            public struct SwappedData
            {
#pragma warning disable 649  // never assigned
                public int __fake;
#pragma warning restore 649
                public int value;
            }

            [BurstCompile(CompileSynchronously = true), CausesSideEffects]
            struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                SwappedData privateData;

                public void Execute(RenderContext ctx, in Data data, ref KernelDefs ports)
                {
                    privateData.value = data.value + 1;
                }
            }

            struct Node : INodeData, IMsgHandler<int>
            {
                public void HandleMessage(MessageContext ctx, in int msg) => ctx.UpdateKernelData(new Data{ value = msg });
            }
        }

        [Test]
        // This test is designed to detect if let's say someone swaps around void* kernelData and void* kernel
        // inside DataFlowGraph
        public unsafe void AllUserStructsInRenderGraph_RetainExpectedValues_ThroughDifferentExecutionEngines([Values] NodeSet.RenderExecutionModel model)
        {
            using (var set = new PotentiallyJobifiedNodeSet(model))
            {
                var node = set.Create<SideEffectUserStructValueNode>();

                set.Update();

                var knodes = set.DataGraph.GetInternalData();
                set.DataGraph.SyncAnyRendering();

                Assert.GreaterOrEqual(knodes.Count, 1);

                ref var knode = ref knodes[node.VHandle.Index];
                var kernelData = (SideEffectUserStructValueNode.Data*)knode.Instance.Data;
                var kernel = (SideEffectUserStructValueNode.SwappedData*)knode.Instance.Kernel;

                Assert.AreEqual(0, kernelData->value);
                Assert.AreEqual(1, kernel->value);

                for (int i = 0; i < 1300; i = i + 1)
                {
                    set.SendMessage(node, SideEffectUserStructValueNode.SimulationPorts.Input, i);
                    set.Update();
                    set.DataGraph.SyncAnyRendering();
                    Assert.AreEqual(i, kernelData->value);
                    Assert.AreEqual(i + 1, kernel->value);
                }

                set.Destroy(node);
            }
        }

        [Test]
        public unsafe void ImpureStructs_AreMarkedToBeRunning([Values] NodeSet.RenderExecutionModel model)
        {
            using (var set = new PotentiallyJobifiedNodeSet(model))
            {
                var node = set.Create<SideEffectUserStructValueNode>();

                set.Update();

                var knodes = set.DataGraph.GetInternalData();
                set.DataGraph.SyncAnyRendering();

                Assert.GreaterOrEqual(knodes.Count, 1);
                Assert.True((knodes[node.VHandle.Index].RunState & RenderGraph.KernelNode.Flags.WillRun) != 0);

                set.Destroy(node);
            }
        }

        [Test]
        public void ChangingRendererModel_IncreasesTopologyVersion()
        {
            using (var set = new NodeSet())
            {
                var version = set.TopologyVersion.Version;
                set.RendererModel = set.RendererModel == NodeSet.RenderExecutionModel.Islands ? NodeSet.RenderExecutionModel.MaximallyParallel : NodeSet.RenderExecutionModel.Islands;
                Assert.Greater(set.TopologyVersion.Version, version);
            }
        }


        [Test]
        public void UpdatingNodeSet_IncreasesRenderVersion()
        {
            const int k_NumRuns = 10;

            using (var set = new NodeSet())
            {
                for (int i = 0; i < k_NumRuns; ++i)
                {
                    var renderVersion = set.DataGraph.RenderVersion;

                    set.Update();

                    Assert.GreaterOrEqual(set.DataGraph.RenderVersion, renderVersion);
                }
            }
        }

        public class SlowNode : KernelNodeDefinition<SlowNode.KernelDefs>
        {
            public static volatile int s_RenderCount;

            public static void Reset() => s_RenderCount = 0;

            public struct KernelDefs : IKernelPortDefinition
            {
#pragma warning disable 649  // never assigned

                public DataInput<SlowNode, int> InputA, InputB;
                public DataOutput<SlowNode, int> Output;

#pragma warning restore
            }

            struct Data : IKernelData { }

            struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                public void Execute(RenderContext ctx, in Data data, ref KernelDefs ports)
                {
                    System.Threading.Thread.Sleep(25);
                    System.Threading.Interlocked.Increment(ref s_RenderCount);
                }
            }
        }

        [Test]
        public void FencingOnRootFence_StopsAllOngoingRender_AndProducesExpectedRenderCount()
        {
            SlowNode.Reset();
            const int k_NumRoots = 5;

            using (var set = new NodeSet())
            {
                List<NodeHandle<SlowNode>>
                    leaves = new List<NodeHandle<SlowNode>>(k_NumRoots * 2),
                    roots = new List<NodeHandle<SlowNode>>(k_NumRoots);

                for (int r = 0; r < k_NumRoots; ++r)
                {
                    var root = set.Create<SlowNode>();

                    roots.Add(root);

                    var leafA = set.Create<SlowNode>();
                    var leafB = set.Create<SlowNode>();

                    set.Connect(leafA, SlowNode.KernelPorts.Output, root, SlowNode.KernelPorts.InputA);
                    set.Connect(leafB, SlowNode.KernelPorts.Output, root, SlowNode.KernelPorts.InputB);

                    leaves.Add(leafA);
                    leaves.Add(leafB);
                }

                set.Update();

                set.DataGraph.RootFence.Complete();

                Assert.AreEqual(2 * k_NumRoots + k_NumRoots, SlowNode.s_RenderCount);

                roots.ForEach(r => set.Destroy(r));
                leaves.ForEach(l => set.Destroy(l));
            }
        }

    }

}
