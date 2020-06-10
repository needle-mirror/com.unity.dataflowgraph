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

        public struct Data : IKernelData
        {
            public int Contents;
        }

        class NonKernelNode : NodeDefinition<EmptyPorts> {}

        class KernelNode : NodeDefinition<Data, KernelNode.KernelDefs, KernelNode.Kernel>
        {
            public struct KernelDefs : IKernelPortDefinition
            {
            }

            [BurstCompile(CompileSynchronously = true)]
            public struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                public void Execute(RenderContext ctx, Data data, ref KernelDefs ports)
                {

                }
            }
        }



        [Test]
        public void KernelNode_HasValidKernelDataFields()
        {
            using (var set = new NodeSet())
            {
                NodeHandle node = set.Create<KernelNode>();
                var internalData = set.GetNodeChecked(node);

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

        [TestCase(NodeType.NonKernel)]
        [TestCase(NodeType.Kernel)]
        public void CreatingAndDestroyingNodes_UpdatesGraphDiff_OverUpdates(NodeType type)
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

    }
}
