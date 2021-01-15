using System.Collections.Generic;
using NUnit.Framework;
using Unity.Burst;

namespace Unity.DataFlowGraph.Tests
{
    public class GraphDiffTests
    {
        // TODO: Tests of indexes and versioning of NodeHandle matches InternalNodeData
        // TODO: Test ALL API on NodeDefinition<A, B, C, D>

        public enum NodeType
        {
            NonKernel,
            Kernel
        }

        class NonKernelNode : SimulationNodeDefinition<NonKernelNode.EmptyPorts>
        {
            public struct EmptyPorts : ISimulationPortDefinition { }
        }

        public class KernelNode : KernelNodeDefinition<KernelNode.KernelDefs>
        {
            public struct KernelDefs : IKernelPortDefinition { }

            struct Data : IKernelData { }

            [BurstCompile(CompileSynchronously = true)]
            struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                public void Execute(RenderContext ctx, in Data data, ref KernelDefs ports) { }
            }
        }

        [Test]
        public void KernelNode_HasValidKernelDataFields()
        {
            using (var set = new NodeSet())
            {
                NodeHandle node = set.Create<KernelNode>();
                var internalData = set.Nodes[node.VHandle];

                unsafe
                {
                    Assert.IsTrue(internalData.KernelData != null);
                    Assert.IsTrue(internalData.HasKernelData);
                }

                set.Destroy(node);
            }
        }

        [Test]
        public void GraphDiff_OnNodeSet_AlwaysExists()
        {
            using (var set = new NodeSet())
            {
                Assert.IsTrue(set.GetCurrentGraphDiff().IsCreated);
                set.Update();
                Assert.IsTrue(set.GetCurrentGraphDiff().IsCreated);
            }
        }

        [Test]
        public void CreatingAndDestroyingNodes_UpdatesGraphDiff_OverUpdates([Values] NodeType type)
        {
            bool isKernel = type == NodeType.Kernel;

            using (var set = new NodeSet())
            {
                for (int numNodesToCreate = 0; numNodesToCreate < 5; ++numNodesToCreate)
                {
                    var list = new List<NodeHandle>();

                    Assert.Zero(set.GetCurrentGraphDiff().CreatedNodes.Count);
                    Assert.Zero(set.GetCurrentGraphDiff().DeletedNodes.Count);

                    for (int i = 0; i < numNodesToCreate; ++i)
                        list.Add(isKernel ? (NodeHandle)set.Create<KernelNode>() : (NodeHandle)set.Create<NonKernelNode>());

                    Assert.AreEqual(numNodesToCreate, set.GetCurrentGraphDiff().CreatedNodes.Count);
                    Assert.Zero(set.GetCurrentGraphDiff().DeletedNodes.Count);

                    for (int i = 0; i < numNodesToCreate; ++i)
                        Assert.AreEqual(list[i], set.GetCurrentGraphDiff().CreatedNodes[i].ToPublicHandle());

                    for (int i = 0; i < numNodesToCreate; ++i)
                        set.Destroy(list[i]);

                    Assert.AreEqual(numNodesToCreate, set.GetCurrentGraphDiff().CreatedNodes.Count);
                    Assert.AreEqual(numNodesToCreate, set.GetCurrentGraphDiff().DeletedNodes.Count);

                    for (int i = 0; i < numNodesToCreate; ++i)
                    {
                        Assert.AreEqual(list[i], set.GetCurrentGraphDiff().CreatedNodes[i].ToPublicHandle());
                        Assert.AreEqual(list[i], set.GetCurrentGraphDiff().DeletedNodes[i].Handle.ToPublicHandle());
                        // TODO: Assert definition index of deleted nodes
                    }

                    // TODO: Assert command queue integrity

                    set.Update();

                    Assert.Zero(set.GetCurrentGraphDiff().CreatedNodes.Count);
                    Assert.Zero(set.GetCurrentGraphDiff().DeletedNodes.Count);
                }

            }
        }

        [Test]
        public void CreatingGraphValue_AddsEntryInGraphDiff([Values] bool strong)
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<NodeWithAllTypesOfPorts>();
                GraphValue<int> gv;

                if (strong)
                    gv = set.CreateGraphValue(node, NodeWithAllTypesOfPorts.KernelPorts.OutputScalar);
                else
                    gv = set.CreateGraphValue<int>(node, (OutputPortID)NodeWithAllTypesOfPorts.KernelPorts.OutputScalar);

                Assert.AreEqual(1, set.GetCurrentGraphDiff().ChangedGraphValues.Count);

                var change = set.GetCurrentGraphDiff().ChangedGraphValues[0];

                Assert.True(change.IsCreation);

                set.ReleaseGraphValue(gv);
                set.Destroy(node);
            }
        }


        [Test]
        public void DestroyingGraphValue_AddsEntryInGraphDiff_WithValidHandles_ButIsNotValid([Values] bool strong)
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<NodeWithAllTypesOfPorts>();
                GraphValue<int> gv;

                if (strong)
                    gv = set.CreateGraphValue(node, NodeWithAllTypesOfPorts.KernelPorts.OutputScalar);
                else
                    gv = set.CreateGraphValue<int>(node, (OutputPortID)NodeWithAllTypesOfPorts.KernelPorts.OutputScalar);

                set.Update();

                set.ReleaseGraphValue(gv);

                Assert.AreEqual(1, set.GetCurrentGraphDiff().ChangedGraphValues.Count);
                var item = set.GetCurrentGraphDiff().ChangedGraphValues[0];

                Assert.AreEqual(node.VHandle, item.SourceNode.Versioned);
                Assert.False(item.IsCreation);
                
                set.Destroy(node);
            }
        }
    }
}
