using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Burst;

namespace Unity.DataFlowGraph.Tests
{
    public class NodeSetIntegrityTests
    {
        // TODO: Tests of indexes and versioning of NodeHandle matches InternalNodeData
        // TODO: Test ALL API on NodeDefinition<A, B, C, D>
        // TODO: Test creation of nodes from invalid node definitions does not corrupt trait/definition tables.

        public enum NodeType
        {
            NonKernel,
            Kernel
        }

        class NonKernelNode : SimulationNodeDefinition<NonKernelNode.EmptyPorts>
        {
            public struct EmptyPorts : ISimulationPortDefinition { }
        }

        class KernelNode : KernelNodeDefinition<KernelNode.KernelDefs>
        {
            public struct KernelDefs : IKernelPortDefinition
            {
            }

            struct Data : IKernelData
            {
#pragma warning disable 649
                public int Contents;
#pragma warning restore 649
            }

            [BurstCompile(CompileSynchronously = true)]
            struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                public void Execute(RenderContext ctx, in Data data, ref KernelDefs ports)
                {

                }
            }
        }

        [TestCase(NodeType.NonKernel)]
        [TestCase(NodeType.Kernel)]
        public void NodeHasValidDefinition(NodeType type)
        {
            bool isKernel = type == NodeType.Kernel;
            using (var set = new NodeSet())
            {
                NodeHandle node = isKernel ? (NodeHandle)set.Create<KernelNode>() : (NodeHandle)set.Create<NonKernelNode>();
                ref var internalData = ref set.Nodes[node.VHandle];

                Assert.NotZero(internalData.TraitsIndex);
                Assert.IsTrue(isKernel ? set.GetDefinition(node) is KernelNode : set.GetDefinition(node) is NonKernelNode);

                set.Destroy(node);
            }
        }

        [TestCase(NodeType.NonKernel)]
        [TestCase(NodeType.Kernel)]
        public void NodeHasValidUserData(NodeType type)
        {
            bool isKernel = type == NodeType.Kernel;
            using (var set = new NodeSet())
            {
                NodeHandle node = isKernel ? (NodeHandle)set.Create<KernelNode>() : (NodeHandle)set.Create<NonKernelNode>();
                ref var internalData = ref set.Nodes[node.VHandle];

                unsafe
                {
                    Assert.IsTrue(internalData.UserData != null);
                }

                set.Destroy(node);
            }
        }


        [TestCase(NodeType.NonKernel)]
        [TestCase(NodeType.Kernel)]
        public void UnsafeMemoryForNode_IsCleanedUp(NodeType type)
        {
            bool isKernel = type == NodeType.Kernel;
            using (var set = new NodeSet())
            {
                NodeHandle node = isKernel ? (NodeHandle)set.Create<KernelNode>() : (NodeHandle)set.Create<NonKernelNode>();

                // TODO: Totally safe? Might be out of bounds if set decides to defragment
                ref readonly var internalData = ref set.Nodes[node.VHandle];

                set.Destroy(node);

                unsafe
                {
                    Assert.IsTrue(internalData.UserData == null);
                    Assert.IsTrue(internalData.KernelData == null);
                }
            }
        }

        [Test]
        public void NonKernelNode_HasNullKernelDataFields()
        {
            using (var set = new NodeSet())
            {
                NodeHandle node = set.Create<NonKernelNode>();
                var internalData = set.Nodes[node.VHandle];

                unsafe
                {
                    Assert.IsTrue(internalData.KernelData == null);
                    Assert.IsFalse(internalData.HasKernelData);
                }

                set.Destroy(node);
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
        public void DisposingNodeSet_ProperlyDeallocatesDataMembers()
        {
            NodeSet set = new NodeSet();

            set.Dispose();

            Assert.IsFalse(set.GetCurrentGraphDiff().IsCreated);
            Assert.IsFalse(set.GetLLTraits().IsCreated);
            Assert.IsFalse(set.GetOutputValues().IsCreated);
            Assert.IsFalse(set.GetTopologyMap_ForTesting().IsCreated);
            Assert.IsFalse(set.GetForwardingTable().IsCreated);
            Assert.IsFalse(set.GetArraySizesTable().IsCreated);
            Assert.IsFalse(set.GetActiveComponentTypes().IsCreated);
            Assert.IsFalse(set.GetUpdateIndices().IsCreated);
            Assert.IsFalse(set.GetUpdateQueue().IsCreated);
            Assert.IsFalse(set.GetPendingBuffers_ForTesting().IsCreated);

            // Add more as they come...
        }

        [Test]
        public void NonForwardingNode_HasInvalidForwardHead()
        {
            using (var set = new NodeSet())
            {
                NodeHandle node = set.Create<NonKernelNode>();
                var internalData = set.Nodes[node.VHandle];

                Assert.AreEqual(internalData.ForwardedPortHead, ForwardPortHandle.Invalid);

                set.Destroy(node);
            }
        }

        [Test]
        public void SettingPortArraySizes_ChangesPortArraySizeHead()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<NodeWithAllTypesOfPorts>();
                var internalData = set.Nodes[node.VHandle];

                Assert.AreEqual(internalData.PortArraySizesHead, ArraySizeEntryHandle.Invalid);

                set.SetPortArraySize(node, NodeWithAllTypesOfPorts.SimulationPorts.MessageArrayIn, 1);
                internalData = set.Nodes[node.VHandle];

                Assert.AreNotEqual(internalData.PortArraySizesHead, ArraySizeEntryHandle.Invalid);

                Assert.AreEqual(set.GetArraySizesTable()[internalData.PortArraySizesHead].Next, ArraySizeEntryHandle.Invalid);

                set.SetPortArraySize(node, NodeWithAllTypesOfPorts.SimulationPorts.MessageArrayIn, 0);
                internalData = set.Nodes[node.VHandle];

                Assert.AreEqual(internalData.PortArraySizesHead, ArraySizeEntryHandle.Invalid);

                set.SetPortArraySize(node, NodeWithAllTypesOfPorts.SimulationPorts.MessageArrayOut, 1);
                internalData = set.Nodes[node.VHandle];

                Assert.AreNotEqual(internalData.PortArraySizesHead, ArraySizeEntryHandle.Invalid);

                Assert.AreEqual(set.GetArraySizesTable()[internalData.PortArraySizesHead].Next, ArraySizeEntryHandle.Invalid);

                set.Destroy(node);
            }
        }

        [Test]
        public void NodeSetIDs_AreUnique_PerInstance()
        {
            const int k_Count = 20;

            var sets = new List<NodeSet>(k_Count);
            var ids = new HashSet<int>();

            try
            {
                for (int i = 0; i < k_Count; ++i)
                {
                    var set = new NodeSet();
                    sets.Add(set);

                    Assert.False(ids.Contains(set.NodeSetID));
                    ids.Add(set.NodeSetID);
                }
            }
            finally
            {
                sets.ForEach(s => s.Dispose());
            }

        }
    }
}
