using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Burst;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.DataFlowGraph.Tests
{
    public class RenderGraphTests
    {
        // TODO tests: 
        // * Check assigning local output ports in a kernel to a non-ref variable will still update the output value
        // * Check nodes in general are executed in topological order

        public class PotentiallyJobifiedNodeSet : NodeSet
        {
            public PotentiallyJobifiedNodeSet(RenderExecutionModel type)
                : base()
            {
                // TODO: This never actually worked, need to refactor TopologyCacheAPI to make it work again.
                /*CacheManager.ComputeJobified = */
                RendererModel = type;
            }
        }

        public struct Node : INodeData
        {
            public int Contents;
        }

        public struct Data : IKernelData
        {
            public int Contents;
        }

        class KernelNode : NodeDefinition<Node, Data, KernelNode.KernelDefs, KernelNode.Kernel>
        {
            public struct KernelDefs : IKernelPortDefinition
            {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                public DataInput<KernelNode, int> Input;
                public DataOutput<KernelNode, int> Output;
#pragma warning restore 649
            }

            [BurstCompile(CompileSynchronously = true)]
            public struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                public void Execute(RenderContext ctx, Data data, ref KernelDefs ports)
                {
                }
            }
        }

        class KernelAdderNode : NodeDefinition<Node, Data, KernelAdderNode.KernelDefs, KernelAdderNode.Kernel>
        {
            public struct KernelDefs : IKernelPortDefinition
            {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                public DataInput<KernelAdderNode, int> Input;
                public DataOutput<KernelAdderNode, int> Output;
#pragma warning restore 649
            }

            [BurstCompile(CompileSynchronously = true)]
            public struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                public void Execute(RenderContext ctx, Data data, ref KernelDefs ports)
                {
                    ctx.Resolve(ref ports.Output) = ctx.Resolve(ports.Input) + 1;
                }
            }
        }

        [
            TestCase(RenderExecutionModel.MaximallyParallel),
            TestCase(RenderExecutionModel.Synchronous),
            TestCase(RenderExecutionModel.Islands),
            TestCase(RenderExecutionModel.SingleThreaded)
        ]
        public void GraphCanUpdate_WithoutIssues(RenderExecutionModel meansOfComputation)
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

        [TestCase(2, RenderExecutionModel.Synchronous), TestCase(2, RenderExecutionModel.MaximallyParallel),
            TestCase(10, RenderExecutionModel.Synchronous), TestCase(10, RenderExecutionModel.MaximallyParallel),
            TestCase(30, RenderExecutionModel.Synchronous), TestCase(30, RenderExecutionModel.MaximallyParallel)]
        public void GraphAccumulatesData_OverLongChains(int nodeChainLength, RenderExecutionModel meansOfComputation)
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

        class PersistentKernelNode : NodeDefinition<Node, Data, PersistentKernelNode.KernelDefs, PersistentKernelNode.Kernel>
        {
            public struct KernelDefs : IKernelPortDefinition
            {
                public DataOutput<PersistentKernelNode, int> Output;
            }

            [BurstCompile(CompileSynchronously = true)]
            public struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                int m_State;

                public void Execute(RenderContext ctx, Data data, ref KernelDefs ports)
                {
                    ctx.Resolve(ref ports.Output) = m_State++;
                }
            }
        }

        [
            TestCase(RenderExecutionModel.MaximallyParallel),
            TestCase(RenderExecutionModel.Synchronous),
            TestCase(RenderExecutionModel.Islands),
            TestCase(RenderExecutionModel.SingleThreaded)
            ]
        public void KernelNodeMemberMemory_IsPersistent_OverMultipleGraphEvaluations(RenderExecutionModel meansOfComputation)
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

        struct DAGTest : IDisposable
        {
            public NodeHandle<ANode>[] Leaves;
            public GraphValue<int>[] Roots;

            List<NodeHandle> m_GC;

            NodeSet m_Set;

            public DAGTest(NodeSet set)
            {
                m_GC = new List<NodeHandle>();
                m_Set = set;
                Leaves = new NodeHandle<ANode>[5];
                Roots = new GraphValue<int>[5];

                // Part (1) of the graph.
                Leaves[0] = set.Create<ANode>();
                var b1 = set.Create<BNode>();
                set.Connect(Leaves[0], ANode.KernelPorts.Output, b1, BNode.KernelPorts.Input);

                Roots[0] = set.CreateGraphValue(b1, BNode.KernelPorts.Output);

                // Part (2) of the graph.
                Leaves[1] = set.Create<ANode>();
                var c2 = set.Create<CNode>();
                var b2 = set.Create<BNode>();

                set.Connect(Leaves[1], ANode.KernelPorts.Output, c2, CNode.KernelPorts.InputA);
                set.Connect(c2, CNode.KernelPorts.Output, b2, BNode.KernelPorts.Input);

                Roots[1] = set.CreateGraphValue(b2, BNode.KernelPorts.Output);

                // Part (4) of the graph.
                Leaves[3] = set.Create<ANode>();
                var c4 = set.Create<CNode>();
                var b4 = set.Create<BNode>();

                set.Connect(Leaves[3], ANode.KernelPorts.Output, c4, CNode.KernelPorts.InputA);
                set.Connect(c4, CNode.KernelPorts.Output, b4, BNode.KernelPorts.Input);

                Roots[3] = set.CreateGraphValue(b4, BNode.KernelPorts.Output);

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
                Roots[2] = set.CreateGraphValue(c3_2, CNode.KernelPorts.Output);

                // Part (5) of the graph.
                Leaves[4] = set.Create<ANode>();
                Roots[4] = set.CreateGraphValue(Leaves[4], ANode.KernelPorts.Output);

                GC(b1, c2, b2, c4, b4, b3_1, b3_2, c3_1, c3_2);
            }

            void GC(params NodeHandle[] handles)
            {
                m_GC.AddRange(handles);
            }

            public void SetLeafInputs(int value)
            {
                foreach (var leaf in Leaves)
                    m_Set.SendMessage(leaf, ANode.SimulationPorts.ValueInput, value);
            }

            public void Dispose()
            {
                var set = m_Set;
                m_GC.ForEach(a => set.Destroy(a));
                Leaves.ToList().ForEach(l => set.Destroy(l));
                Roots.ToList().ForEach(r => set.ReleaseGraphValue(r));
            }
        }

        class ANode : NodeDefinition<Node, ANode.SimPorts, Data, ANode.KernelDefs, ANode.Kernel>, IMsgHandler<int>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                public MessageInput<ANode, int> ValueInput;
#pragma warning restore 649
            }

            public struct KernelDefs : IKernelPortDefinition
            {
                public DataOutput<ANode, int> Output;
            }

            [BurstCompile(CompileSynchronously = true)]
            public struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                public void Execute(RenderContext ctx, Data data, ref KernelDefs ports) => ctx.Resolve(ref ports.Output) = data.Contents + 1;
            }

            public void HandleMessage(in MessageContext ctx, in int msg) => GetKernelData(ctx.Handle).Contents = msg;
        }

        class BNode : NodeDefinition<Node, Data, BNode.KernelDefs, BNode.Kernel>
        {
            public struct KernelDefs : IKernelPortDefinition
            {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                public DataInput<BNode, int> Input;
                public DataOutput<BNode, int> Output;
#pragma warning restore 649
            }

            [BurstCompile(CompileSynchronously = true)]
            public struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                public void Execute(RenderContext ctx, Data data, ref KernelDefs ports) => ctx.Resolve(ref ports.Output) = ctx.Resolve(ports.Input) * 3;
            }
        }

        class CNode : NodeDefinition<Node, Data, CNode.KernelDefs, CNode.Kernel>
        {
            public struct KernelDefs : IKernelPortDefinition
            {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                public DataInput<CNode, int> InputA;
                public DataInput<CNode, int> InputB;
                public DataOutput<CNode, int> Output;
#pragma warning restore 649
            }

            [BurstCompile(CompileSynchronously = true)]
            public struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                public void Execute(RenderContext ctx, Data data, ref KernelDefs ports) => ctx.Resolve(ref ports.Output) = ctx.Resolve(ports.InputA) + ctx.Resolve(ports.InputB);
            }
        }



        [
            TestCase(RenderExecutionModel.MaximallyParallel),
            TestCase(RenderExecutionModel.Synchronous),
            TestCase(RenderExecutionModel.Islands),
            TestCase(RenderExecutionModel.SingleThreaded)
            ]
        public void ComplexDAG_ProducesExpectedResults_InAllExecutionModels(RenderExecutionModel model)
        {
            const int k_NumGraphs = 10;
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

                for (int i = 0; i < k_NumGraphs; ++i)
                {
                    var graph = tests[i];
                    const int b = 3;
                    const int c = 2;

                    var a = (i + 1);
                    var abb = a * b * b;

                    Assert.AreEqual(
                        a * b,
                        set.GetValueBlocking(graph.Roots[0]),
                        $"Root[0] produced unexpected results in graph iteration {i}"
                    );

                    Assert.AreEqual(
                        (abb + a) * b,
                        set.GetValueBlocking(graph.Roots[1]),
                        $"Root[1] produced unexpected results in graph iteration {i}"
                    );

                    Assert.AreEqual(
                        (abb + a) * c * c,
                        set.GetValueBlocking(graph.Roots[2]),
                        $"Root[2] produced unexpected results in graph iteration {i}"
                    );

                    Assert.AreEqual(
                        (abb + a) * b,
                        set.GetValueBlocking(graph.Roots[3]),
                        $"Root[3] produced unexpected results in graph iteration {i}"
                    );

                    Assert.AreEqual(
                        a,
                        set.GetValueBlocking(graph.Roots[4]),
                        $"Root[4] produced unexpected results in graph iteration {i}"
                    );
                }

                tests.ForEach(t => t.Dispose());
            }
        }


        public class UserStructValueNode
            : NodeDefinition<Node, UserStructValueNode.SimPorts, UserStructValueNode.Data, UserStructValueNode.KernelDefs, UserStructValueNode.Kernel>
            , IMsgHandler<int>
        {
            public struct KernelDefs : IKernelPortDefinition { }

            public struct SimPorts : ISimulationPortDefinition
            {
                public MessageInput<UserStructValueNode, int> Input;
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

            [BurstCompile(CompileSynchronously = true)]
            public struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                SwappedData privateData;

                public void Execute(RenderContext ctx, Data data, ref KernelDefs ports)
                {
                    privateData.value = data.value + 1;
                }
            }

            public void HandleMessage(in MessageContext ctx, in int msg) => GetKernelData(ctx.Handle).value = msg;
        }

        [
            TestCase(RenderExecutionModel.MaximallyParallel),
            TestCase(RenderExecutionModel.Synchronous),
            TestCase(RenderExecutionModel.Islands),
            TestCase(RenderExecutionModel.SingleThreaded)
        ]
        // This test is designed to detect if let's say someone swaps around void* kernelData and void* kernel
        // inside DataFlowGraph
        public unsafe void AllUserStructsInRenderGraph_RetainExpectedValues_ThroughDifferentExecutionEngines(RenderExecutionModel model)
        {
            using (var set = new PotentiallyJobifiedNodeSet(model))
            {
                var node = set.Create<UserStructValueNode>();

                set.Update();

                var knodes = set.DataGraph.GetInternalData();
                set.DataGraph.SyncAnyRendering();

                Assert.AreEqual(1, knodes.Count);

                ref var knode = ref knodes[((NodeHandle)node).VHandle.Index];
                var kernelData = (UserStructValueNode.Data*)knode.KernelData;
                var kernel = (UserStructValueNode.SwappedData*)knode.Kernel;

                Assert.AreEqual(0, kernelData->value);
                Assert.AreEqual(1, kernel->value);

                for (int i = 0; i < 1300; i = i + 1)
                {
                    set.SendMessage(node, UserStructValueNode.SimulationPorts.Input, i);
                    set.Update();
                    set.DataGraph.SyncAnyRendering();
                    Assert.AreEqual(i, kernelData->value);
                    Assert.AreEqual(i + 1, kernel->value);
                }

                set.Destroy(node);
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

        class SlowNode : NodeDefinition<Node, Data, SlowNode.KernelDefs, SlowNode.Kernel>
        {
            public static volatile int s_RenderCount;

            public struct KernelDefs : IKernelPortDefinition
            {
#pragma warning disable 649  // never assigned

                public DataInput<SlowNode, int> InputA, InputB;
                public DataOutput<SlowNode, int> Output;

#pragma warning restore
            }

            public struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                public void Execute(RenderContext ctx, Data data, ref KernelDefs ports)
                {
                    System.Threading.Thread.Sleep(25);
                    System.Threading.Interlocked.Increment(ref s_RenderCount);
                }
            }
        }

        [Test]
        public void FencingOnRootFence_StopsAllOngoingRender_AndProducesExpectedRenderCount()
        {
#if ENABLE_IL2CPP
            Assert.Ignore("Skipping test since IL2CPP is broken for non-bursted Kernels");
#endif

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
