using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Burst;
using Unity.Entities;
using static Unity.DataFlowGraph.Tests.BufferAliasingTests;

namespace Unity.DataFlowGraph.Tests
{
    using CNode = NodeHandle<CullingTests.CounterNode>;

    public class CullingTests
    {
        public struct Item : IComponentData { int _; }

        public enum CullingScenario
        {
            AlwaysOff,
            AlwaysOn,
            InitiallyOff_ButFlipping,
            InitiallyOn_ButFlipping,
        }

        public enum ObservationScenario
        {
            GraphValue,
            ComponentNode,
            SideEffectNode
        }

        public unsafe class CounterNode : KernelNodeDefinition<CounterNode.KernelDefs>
        {
            public struct KernelDefs : IKernelPortDefinition
            {
                public DataInput<CounterNode, Item> Input;
                public DataInput<CounterNode, Item> OtherInput;
                public DataOutput<CounterNode, Item> Output;
            }

            public struct Data : IKernelData
            {
                public long* ExecutionCounter;
            }

            [BurstCompile(CompileSynchronously = true)]
            struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                public void Execute(RenderContext ctx, in Data data, ref KernelDefs ports)
                {
                    (*data.ExecutionCounter)++;
                }
            }
        }

        public unsafe class SideEffectNode : KernelNodeDefinition<SideEffectNode.KernelDefs>
        {
            public struct KernelDefs : IKernelPortDefinition
            {
                public DataInput<SideEffectNode, Item> Input;
            }

            public struct Data : IKernelData
            {
                public long* ExecutionCounter;
            }

            [BurstCompile(CompileSynchronously = true), CausesSideEffects]
            struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                public void Execute(RenderContext ctx, in Data data, ref KernelDefs ports)
                {
                    (*data.ExecutionCounter)++;
                }
            }
        }

        unsafe class Fixture : IDisposable
        {
            public NodeSet Set;
            List<NodeHandle> m_GC = new List<NodeHandle>();
            Dictionary<CNode, Stack<NodeHandle>> m_ObservationMap = new Dictionary<CNode, Stack<NodeHandle>>();
            Dictionary<CNode, Stack<GraphValue<Item>>> m_GVMap = new Dictionary<CNode, Stack<GraphValue<Item>>>();

            PointerPool m_AliasResults = new PointerPool();
            World m_World;
            UpdateSystem m_System;
            ObservationScenario m_ObservationScenario;

            public Fixture(NodeSet.RenderExecutionModel model = NodeSet.RenderExecutionModel.MaximallyParallel)
            {
                Set = new NodeSet();
                Set.RendererModel = model;
            }

            public Fixture(NodeSet.RenderExecutionModel model, ObservationScenario scenario, NodeSet.RenderOptimizations opt = default)
            {
                m_World = new World("Culling tests");
                m_System = m_World.GetOrCreateSystem<UpdateSystem>();
                m_System.Set = Set = new NodeSet(m_System);
                Set.RendererModel = model;
                Set.RendererOptimizations = opt;
                m_ObservationScenario = scenario;
            }

            public void Dispose()
            {
                foreach(var s in m_GVMap.Values)
                    s.ToList().ForEach(o => Set.ReleaseGraphValue(o));

                foreach (var s in m_ObservationMap.Values)
                    s.ToList().ForEach(o => Set.Destroy(o));

                m_GC.ForEach(c => Set.Destroy(c));
                Set.Dispose();
                m_AliasResults.Dispose();
                m_World?.Dispose();
            }

            public void Update()
            {
                if (m_System != null)
                    m_System.Update();
                else
                    Set.Update();
            }

            public CNode Observe(CNode c)
            {
                NodeHandle handleObserver = default;

                switch (m_ObservationScenario)
                {
                    case ObservationScenario.GraphValue:
                    {
                        var gv = Set.CreateGraphValue(c, CounterNode.KernelPorts.Output);
                        if(!m_GVMap.ContainsKey(c))
                            m_GVMap[c] = new Stack<GraphValue<Item>>();
                        m_GVMap[c].Push(gv);
                    }
                    break;
                    case ObservationScenario.ComponentNode:
                    {
                        var entity = m_World.EntityManager.CreateEntity(typeof(Item));
                        var component = Set.CreateComponentNode(entity);
                        handleObserver = component;
                        Set.Connect(c, CounterNode.KernelPorts.Output, component, ComponentNode.Input<Item>());
                        break;
                    }
                    case ObservationScenario.SideEffectNode:
                    {
                        var sideEffect = CreateSideEffect();
                        handleObserver = sideEffect;
                        Set.Connect(c, CounterNode.KernelPorts.Output, sideEffect, SideEffectNode.KernelPorts.Input);
                    }
                    break;
                }

                if(handleObserver != default)
                {
                    if (!m_ObservationMap.ContainsKey(c))
                        m_ObservationMap[c] = new Stack<NodeHandle>();

                    m_ObservationMap[c].Push(handleObserver);
                }

                return c;
            }

            public void Unobserve(CNode node)
            {
                switch (m_ObservationScenario)
                {
                    case ObservationScenario.GraphValue:
                        Set.ReleaseGraphValue(m_GVMap[node].Pop());
                        break;
                    case ObservationScenario.ComponentNode:
                    case ObservationScenario.SideEffectNode:
                    {
                        Set.Destroy(m_ObservationMap[node].Pop());
                        break;
                    }
                }
                
            }

            public void AssertExecution(bool shouldRun, CNode node, int e)
            {
                var flags = GetExecutionFlags(node);
                Assert.AreEqual(shouldRun, (flags & RenderGraph.KernelNode.Flags.Enabled) > 0);
                Assert.AreEqual(shouldRun, (flags & RenderGraph.KernelNode.Flags.WillRun) > 0);

                var actualExecution = GetExecutionCounter(node);

                if (e != actualExecution)
                    Assert.AreEqual(e, actualExecution);
            }

            public CNode Connect(CNode a, CNode b) => Connect(a, b, CounterNode.KernelPorts.Input);

            public CNode Connect(CNode a, CNode b, DataInput<CounterNode, Item> input)
            {
                Set.Connect(a, CounterNode.KernelPorts.Output, b, input);
                return b;
            }

            public NodeHandle<CounterNode> CreateNode()
            {
                var node = Set.Create<CounterNode>();
                Set.GetKernelData<CounterNode.Data>(node).ExecutionCounter = m_AliasResults.CreateLong();
                m_GC.Add(node);
                return node;
            }

            NodeHandle<SideEffectNode> CreateSideEffect()
            {
                var node = Set.Create<SideEffectNode>();
                Set.GetKernelData<SideEffectNode.Data>(node).ExecutionCounter = m_AliasResults.CreateLong();
                return node;
            }

            public (CNode a, CNode b, CNode c) MakeTree()
            {
                var a = Connect(CreateNode(), CreateNode());
                var c = Connect(CreateNode(), CreateNode());
                var b = Connect(a, CreateNode());
                Connect(c, b, CounterNode.KernelPorts.OtherInput);

                return (a, b, c);
            }

            public IEnumerable<CNode> GetParents(CNode root)
            {
                foreach (var conn in Set.GetInputs(Set.Validate(root)))
                    yield return Set.CastHandle<CounterNode>(conn.Source.ToPublicHandle());
            }

            public IEnumerable<CNode> FlattenAllParents(CNode root)
            {
                foreach (var parent in GetParents(root))
                {
                    foreach (var p2 in FlattenAllParents(parent))
                        yield return p2;

                    yield return parent;
                }
            }

            public long GetExecutionCounter(CNode n)
            {
                Set.DataGraph.SyncAnyRendering();
                var kernelData = Set.GetKernelData<CounterNode.Data>(n);
                return *kernelData.ExecutionCounter;
            }

            public RenderGraph.KernelNode.Flags GetExecutionFlags(CNode n)
            {
                Set.DataGraph.SyncAnyRendering();
                return Set.DataGraph.GetInternalData()[n.VHandle.Index].RunState;
            }
        }

        [Test]
        public void OptimizationFlags_DoNotIncludeCulling_ByDefault()
        {
            Assert.Zero((int)(NodeSet.RenderOptimizations.Default & NodeSet.RenderOptimizations.ObservabilityCulling));
            using (var set = new NodeSet())
            {
                Assert.AreEqual(NodeSet.RenderOptimizations.Default, set.RendererOptimizations);
            }
        }

        [Test]
        public void OptimizationFlags_OnNodeSetCreation_AreDefault()
        {
            using (var set = new NodeSet())
            {
                Assert.AreEqual(NodeSet.RenderOptimizations.Default, set.RendererOptimizations);
            }
        }
        
        [Test]
        public void ChangingOptimizationFlags_SignalsTopologyChange()
        {
            using (var set = new NodeSet())
            {
                var topologyVersion = set.TopologyVersion;
                set.RendererOptimizations |= NodeSet.RenderOptimizations.ObservabilityCulling;

                Assert.Greater(set.TopologyVersion.Version, topologyVersion.Version);
            }
        }

        [Test]
        public void RenderingGraph_KnowsOptimizationFlagsChanged_WhenChangingOptimizationFlags()
        {
            using (var set = new NodeSet())
            {
                var topologyVersion = set.TopologyVersion;

                Assert.False(set.DataGraph.OptimizationsChangedThisUpdate);

                // The render graph sets the flag to true during the first update,
                // so that all optimizations running on "dirty" flags run once at least
                set.Update();
                Assert.True(set.DataGraph.OptimizationsChangedThisUpdate);

                set.Update();
                Assert.False(set.DataGraph.OptimizationsChangedThisUpdate);

                set.RendererOptimizations |= NodeSet.RenderOptimizations.ObservabilityCulling;
                set.Update();
                Assert.True(set.DataGraph.OptimizationsChangedThisUpdate);

                set.Update();
                Assert.False(set.DataGraph.OptimizationsChangedThisUpdate);

                // shouldn't change, it's the same
                set.RendererOptimizations |= NodeSet.RenderOptimizations.ObservabilityCulling;
                set.Update();
                Assert.False(set.DataGraph.OptimizationsChangedThisUpdate);

                set.RendererOptimizations &= ~NodeSet.RenderOptimizations.ObservabilityCulling;
                set.Update();
                Assert.True(set.DataGraph.OptimizationsChangedThisUpdate);

                set.Update();
                Assert.False(set.DataGraph.OptimizationsChangedThisUpdate);
            }
        }

        [Test]
        public void NewlyCreatedNodes_AreNominallyEnabled_WithoutCulling()
        {
            const int k_Loops = 10;
            using (var f = new Fixture(NodeSet.RenderExecutionModel.MaximallyParallel))
            {
                for(int i = 0; i < k_Loops; ++i)
                {
                    var node = f.CreateNode();

                    f.Update();

                    var flags = f.GetExecutionFlags(node);
                    Assert.True((flags & RenderGraph.KernelNode.Flags.Enabled) > 0);
                }
            }
        }

        Func<bool> GetFlipper(CullingScenario scenario)
        {
            bool state = false; // (Initially off)
            switch (scenario)
            {
                case CullingScenario.AlwaysOff:
                    return () => false;
                case CullingScenario.AlwaysOn:
                    return () => true;
                case CullingScenario.InitiallyOn_ButFlipping:
                    state = true;
                    break;
            }

            return () =>
            {
                var copy = state;
                state = !state;
                return copy;
            };
        }

        void RunFlipLoop(
            Fixture f,
            Func<bool> getCull,
            Action<bool, (int Loop, int Cull)> validateIteration)
        {
            const int k_Flips = 10;
            bool shouldCull = getCull();

            for (int i = 0, e = 0; i < k_Flips; ++i)
            {
                if (shouldCull)
                    f.Set.RendererOptimizations |= NodeSet.RenderOptimizations.ObservabilityCulling;
                else
                    f.Set.RendererOptimizations &= ~NodeSet.RenderOptimizations.ObservabilityCulling;

                if (!shouldCull)
                    e++;

                f.Update();

                validateIteration(shouldCull, (i + 1, e));

                shouldCull = getCull();
            }
        }
        
        [Test]
        public void NonObservedNode_Executes_DependingOnCullingStatus([Values] NodeSet.RenderExecutionModel model, [Values]CullingScenario scenario)
        {
            using (var f = new Fixture(model))
            {
                var node = f.CreateNode();

                void AssertState(bool isCulling, (int Loop, int Cull) count)
                    => f.AssertExecution(shouldRun: !isCulling, node, count.Cull);
                
                RunFlipLoop(f, GetFlipper(scenario), AssertState);
            }
        }

        [Test]
        public void ComplexDag_WithIslands_ConditionallyGetsCulled_AndExecutes_DependingOnCullingStatus(
            [Values] NodeSet.RenderExecutionModel model,
            [Values] CullingScenario cullingScenario,
            [Values] ObservationScenario observations)
        {
            /*
             * "o" denotes a vertex, "x" denotes an observed vertex
             *
             * 1. lone node, never executes since not observed
             * o
             *
             * 2. simple graph but never observed
             * o -> o
             *
             * 3. tree that is observed from a root, everything should run
             * o -> o
             *       \
             *        o -> x
             *       /
             * o -> o
             *
             * 4. DAG that is observed from one root, the other root shouldn't compute
             *
             * o -> o
             *       \
             *        o -> x
             *       / \
             * o -> o   o
             *
             * 5. A DAG that is only observed from one branch, everything else shouldn't run
             * 
             * o -> o -> x
             *       \
             *        o 
             *       /
             * o -> o
             * 
             */


            using (var f = new Fixture(model, observations))
            {
                // ------------------------------------------------------------------------------------

                var one = f.CreateNode();

                // ------------------------------------------------------------------------------------

                var two = f.Connect(f.CreateNode(), f.CreateNode());

                // ------------------------------------------------------------------------------------

                var three = f.Connect(f.MakeTree().b, f.CreateNode());
                f.Observe(three);

                // ------------------------------------------------------------------------------------

                var fourTree = f.MakeTree();
                var observedFour = f.Observe(f.Connect(fourTree.b, f.CreateNode()));
                var nonObservedFour = f.Connect(fourTree.b, f.CreateNode());

                // ------------------------------------------------------------------------------------

                var fiveTree = f.MakeTree();
                var five = f.Connect(fiveTree.a, f.CreateNode());
                f.Observe(five);

                void AssertState(bool isCulling, (int Loop, int Cull) count)
                {
                    // ------------------------------------------------------------------------------------

                    f.AssertExecution(shouldRun: !isCulling, one, count.Cull);

                    // ------------------------------------------------------------------------------------

                    f.AssertExecution(shouldRun: !isCulling, two, count.Cull);

                    foreach (var parent in f.GetParents(two))
                    {
                        f.AssertExecution(shouldRun: !isCulling, parent, count.Cull);
                    }

                    // ------------------------------------------------------------------------------------

                    foreach (var node in f.FlattenAllParents(three).Concat(new[] { three }))
                    {
                        f.AssertExecution(shouldRun: true, node, count.Loop);
                    }

                    // ------------------------------------------------------------------------------------

                    foreach (var node in f.FlattenAllParents(observedFour).Concat(new[] { observedFour }))
                    {
                        f.AssertExecution(shouldRun: true, node, count.Loop);
                    }

                    f.AssertExecution(shouldRun: !isCulling, nonObservedFour, count.Cull);

                    // ------------------------------------------------------------------------------------

                    var observed = f.FlattenAllParents(five).Concat(new[] { five });
                    var notObserved = f.GetParents(fiveTree.c).Concat(new[] { fiveTree.c, fiveTree.b });

                    foreach (var node in observed)
                    {
                        f.AssertExecution(shouldRun: true, node, count.Loop);
                    }

                    foreach (var node in notObserved)
                    {
                        f.AssertExecution(shouldRun: !isCulling, node, count.Cull);
                    }
                }

                RunFlipLoop(f, GetFlipper(cullingScenario), AssertState);
            }
        }

        [Test]
        public void GraphWithObservationUpstream_DoesNotRunDownstreamNodes(
            [Values] NodeSet.RenderExecutionModel model,
            [Values] ObservationScenario scenario)
        {
            /*
             * "o" denotes a vertex, "x" denotes an observed vertex
             *
             * 1. just a single downstream node
             * 
             *  x -> o
             *
             * 2. observation in the middle, downstream not expected to run
             * 
             *  o -> x -> o
             *
             * 3. observation in the middle, downstream branches not expected to run
             *
             *          o
             *         /
             *   o -> x 
             *         \
             *          o
             * 
             */

            const int k_LoopCount = 10;


            using (var f = new Fixture(model, scenario, NodeSet.RenderOptimizations.ObservabilityCulling))
            {
                // ------------------------------------------------------------------------------------

                var one = f.Connect(f.Observe(f.CreateNode()), f.CreateNode());
                var two = f.Connect(f.Connect(f.CreateNode(), f.Observe(f.CreateNode())), f.CreateNode());
                var threeStem = f.Connect(f.CreateNode(), f.Observe(f.CreateNode()));
                var threeA = f.Connect(threeStem, f.CreateNode());
                var threeB = f.Connect(threeStem, f.CreateNode());

                var notObservedNodes = new[] { one, two, threeA, threeB };

                // ------------------------------------------------------------------------------------

                for (int i = 0; i < k_LoopCount; ++i)
                {
                    f.Update();

                    foreach(var notObserved in notObservedNodes)
                    {
                        f.AssertExecution(shouldRun: false, notObserved, 0);

                        foreach (var parent in f.FlattenAllParents(notObserved))
                        {
                            f.AssertExecution(shouldRun: true, parent, i + 1);
                        }
                    }
                }
            }
        }

        [Test]
        public void IncrementalTopologyChanges_CorrectlyRecomputesAffectedCullingIslands([Values] NodeSet.RenderExecutionModel model)
        {
            /*
             * "o" denotes a vertex, "x" denotes an observed vertex
             *
             * 1. conditionally connect the lower branch on one of many islands, test that it starts to run
             * 
             *  o -> x
             *      /
             *  o -
             * 
             */

            const int k_Islands = 50;

            List<(CNode a, CNode b)> pairs = new List<(CNode a, CNode b)>();

            using (var f = new Fixture(model, ObservationScenario.GraphValue, NodeSet.RenderOptimizations.ObservabilityCulling))
            {
                // ------------------------------------------------------------------------------------

                for (int i = 0; i < k_Islands; ++i)
                {
                    var b = f.Connect(f.CreateNode(), f.Observe(f.CreateNode()));
                    var a = f.CreateNode();
                    pairs.Add((a, b));
                }

                for (int i = 0; i < k_Islands; ++i)
                {
                    f.Update();

                    for(int n = 0; n < i; ++n)
                        f.AssertExecution(true, pairs[n].a, i - n);

                    for (int n = i; n < k_Islands; ++n)
                        f.AssertExecution(false, pairs[n].a, 0);

                    f.Connect(pairs[i].a, pairs[i].b, CounterNode.KernelPorts.OtherInput);
                }
            }
        }

        [Test]
        public void ObservationSources_AreEffectively_ReferenceCounted(
            [Values] NodeSet.RenderExecutionModel model,
            [Values] ObservationScenario scenario)
        {
            /*
             * "o" denotes a vertex, "x" denotes an observed vertex
             *
             * 1. switch between observing x on and off, refcounted
             * 
             *  o -> x -> o
             * 
             */

            const int k_LoopCount = 10;


            using (var f = new Fixture(model, scenario, NodeSet.RenderOptimizations.ObservabilityCulling))
            {
                // ------------------------------------------------------------------------------------

                var middle = f.CreateNode();
                var last = f.Connect(f.Connect(f.CreateNode(), middle), f.CreateNode());

                // ------------------------------------------------------------------------------------

                var rng = new Mathematics.Random(0x55);

                for (int i = 0 , e = 0; i < k_LoopCount; ++i)
                { 
                    var refs = rng.NextInt(1, 10);

                    Enumerable.Range(0, refs).ToList().ForEach(r => f.Observe(middle));

                    for(; refs > 0; --refs)
                    {
                        f.Update();

                        f.AssertExecution(shouldRun: true, middle, ++e);
                        f.AssertExecution(shouldRun: false, last, 0);

                        // slowly remove reference counts through a mix of additions and decrements
                        f.Unobserve(middle);
                        f.Observe(middle);
                        f.Unobserve(middle);
                    }

                    // Ensure that it now has settled.
                    f.Update();
                    f.AssertExecution(shouldRun: false, middle, e);
                    f.AssertExecution(shouldRun: false, last, 0);
                }
            }
        }
    }
}
