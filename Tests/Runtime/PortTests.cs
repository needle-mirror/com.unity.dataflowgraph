using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Burst;
using Unity.Entities;
using UnityEngine.Scripting;

namespace Unity.DataFlowGraph.Tests
{
    public class PortTests
    {
        [Preserve] // avoid stripping, [Values()] is not enough
        struct Scalar : IComponentData
        {

        }

        [Preserve] // avoid stripping, [Values()] is not enough
        struct SystemScalar : ISystemStateComponentData
        {

        }

        [Preserve] // avoid stripping, [Values()] is not enough
        struct Buffer : IBufferElementData
        {

        }

        [Preserve] // avoid stripping, [Values()] is not enough
        struct SystemBuffer : ISystemStateBufferElementData
        {

        }

        [Test]
        public void PortStorageFlags_AreNotInLow16bits()
        {
            Assert.GreaterOrEqual(PortStorage.IsECSPortFlag, 1 << 16);
            Assert.GreaterOrEqual(1 << 28, PortStorage.IsECSPortFlag);
            Assert.GreaterOrEqual(PortStorage.IsDFGPortFlag, 1 << 16);
            Assert.GreaterOrEqual(1 << 29, PortStorage.IsDFGPortFlag);
        }

        [Test]
        public void DefaultConstructed_PortStorage_IsValid()
        {
            var defaultPortStorage = new PortStorage();
            Assert.IsFalse(defaultPortStorage.IsECSPort);
            Assert.IsFalse(defaultPortStorage.IsDFGPort);
#if DFG_ASSERTIONS
            PortStorage.EncodedDFGPort port;
            int componentType;
            Assert.Throws<AssertionException>(() => port = defaultPortStorage.DFGPort);
            Assert.Throws<AssertionException>(() => componentType = defaultPortStorage.ECSTypeIndex);
#endif
        }

        [Test]
        public void PortStorage_CanBeInitialized_FromUInt16_AndRetrieveValue_ThroughPortAccessor([Values((ushort)0u, (ushort)1u, (ushort)13u, (ushort)(PortStorage.MaxDFGPortNumber))] ushort ushortValue)
        {
            Assert.AreEqual(new PortStorage(ushortValue, PortStorage.Category.Message).DFGPort.CategoryCounter, ushortValue);
        }

        [Test]
        public void PortStorage_CanBeInitialized_FromECSTypes_AndMatchTypeIndex([Values(typeof(Scalar), typeof(SystemScalar), typeof(Buffer), typeof(SystemBuffer))] Type ecsType)
        {
            var component = new ComponentType(ecsType);
            Assert.AreEqual(new PortStorage(component).ECSTypeIndex, component.TypeIndex);
        }

        [Test]
        public void PortStorage_ReadComponentType_HasReadOnlyFlag([Values(typeof(Scalar), typeof(SystemScalar), typeof(Buffer), typeof(SystemBuffer))] Type ecsType)
        {
            var component = new ComponentType(ecsType);
            var storage = new PortStorage(component);
            var restoredReadOnlyComponent = storage.ReadOnlyComponentType;

            Assert.AreEqual(restoredReadOnlyComponent.AccessModeType, ComponentType.AccessMode.ReadOnly);
            Assert.AreEqual(restoredReadOnlyComponent.TypeIndex, component.TypeIndex);
        }

        [Test]
        public void PortStorage_ReadWriteComponentType_HasReadWriteFlag([Values(typeof(Scalar), typeof(SystemScalar), typeof(Buffer), typeof(SystemBuffer))] Type ecsType)
        {
            var component = new ComponentType(ecsType);
            var storage = new PortStorage(component);
            var restoredReadOnlyComponent = storage.ReadWriteComponentType;

            Assert.AreEqual(restoredReadOnlyComponent.AccessModeType, ComponentType.AccessMode.ReadWrite);
            Assert.AreEqual(restoredReadOnlyComponent.TypeIndex, component.TypeIndex);
        }

        [Test]
        public void PortStorageConstructors_CorrectlyTagUnion()
        {
            Assert.False(new PortStorage((ushort)0u, PortStorage.Category.Message).IsECSPort);
            Assert.False(new PortStorage(0, PortStorage.Category.Message).IsECSPort);
            Assert.True(new PortStorage(new ComponentType()).IsECSPort);
        }

        [Test]
        public void ConnectionCategories_DoNotClash()
        {
            var connectionCategories = new List<uint>();
            foreach (PortDescription.Category portCategory in Enum.GetValues(typeof(PortDescription.Category)))
            {
                foreach (PortDescription.CategoryShift shift in Enum.GetValues(typeof(PortDescription.CategoryShift)))
                {
                    if (shift == PortDescription.CategoryShift.Max)
                        continue;
                    var connectionCategory = (uint) portCategory << (int) shift;
                    connectionCategories.Add(connectionCategory);
                }
            }

            connectionCategories.Add(PortDescription.MessageToDataConnectionCategory);

            CollectionAssert.AllItemsAreUnique(connectionCategories);
            Assert.AreEqual(10, connectionCategories.Count);
        }

        public class KernelPortTestNode : KernelNodeDefinition<KernelPortTestNode.KernelDefs>
        {
            public struct KernelDefs : IKernelPortDefinition
            {
#pragma warning disable 649
                public DataInput<KernelPortTestNode, int> Input1, Input2;
                public PortArray<DataInput<KernelPortTestNode, int>> Input3;
                public DataOutput<KernelPortTestNode, int> Output1, Output2;
                public PortArray<DataOutput<KernelPortTestNode, int>> Output3;
#pragma warning restore 649

                public struct PortIDs
                {
                    public InputPortID Input1PortID, Input2PortID, Input3PortID;
                    public OutputPortID Output1PortID, Output2PortID, Output3PortID;
                }
                public DataOutput<KernelPortTestNode, PortIDs> PortIDsFromKernel;
            }

            struct EmptyKernelData : IKernelData { }

            [BurstCompile(CompileSynchronously = true)]
            struct Kernel : IGraphKernel<EmptyKernelData, KernelDefs>
            {
                public void Execute(RenderContext ctx, in EmptyKernelData data, ref KernelDefs ports)
                {
                    ref var portIDs = ref ctx.Resolve(ref ports.PortIDsFromKernel);
                    portIDs.Input1PortID = KernelPorts.Input1.Port;
                    portIDs.Input2PortID = KernelPorts.Input2.Port;
                    portIDs.Input3PortID = KernelPorts.Input3.GetPortID();
                    portIDs.Output1PortID = KernelPorts.Output1.Port;
                    portIDs.Output2PortID = KernelPorts.Output2.Port;
                    portIDs.Output3PortID = KernelPorts.Output3.GetPortID();
                }
            }
        }

        [Test]
        public void PortIDs_AreTheSame_InSimulation_AndInRendering()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<KernelPortTestNode>();
                var gv = set.CreateGraphValue(node, KernelPortTestNode.KernelPorts.PortIDsFromKernel);

                set.Update();
                var portIDs = set.GetValueBlocking(gv);

                // Verify that PortIDs in the NodeDefinition match those seen by the Kernel.
                Assert.AreEqual(KernelPortTestNode.KernelPorts.Input1.Port, portIDs.Input1PortID);
                Assert.AreEqual(KernelPortTestNode.KernelPorts.Input2.Port, portIDs.Input2PortID);
                Assert.AreEqual(KernelPortTestNode.KernelPorts.Input3.GetPortID(), portIDs.Input3PortID);
                Assert.AreEqual(KernelPortTestNode.KernelPorts.Output1.Port, portIDs.Output1PortID);
                Assert.AreEqual(KernelPortTestNode.KernelPorts.Output2.Port, portIDs.Output2PortID);
                Assert.AreEqual(KernelPortTestNode.KernelPorts.Output3.GetPortID(), portIDs.Output3PortID);

                // Verify that PortIDs are unique.
                Assert.AreNotEqual(KernelPortTestNode.KernelPorts.Input1.Port, portIDs.Input2PortID);
                Assert.AreNotEqual(KernelPortTestNode.KernelPorts.Input1.Port, portIDs.Input3PortID);
                Assert.AreNotEqual(KernelPortTestNode.KernelPorts.Input2.Port, portIDs.Input3PortID);
                Assert.AreNotEqual(KernelPortTestNode.KernelPorts.Output1.Port, portIDs.Output2PortID);
                Assert.AreNotEqual(KernelPortTestNode.KernelPorts.Output1.Port, portIDs.Output3PortID);
                Assert.AreNotEqual(KernelPortTestNode.KernelPorts.Output2.Port, portIDs.Output3PortID);

                set.ReleaseGraphValue(gv);
                set.Destroy(node);
            }
        }

        public class CloselyRelatedGenericsNode : SimulationNodeDefinition<CloselyRelatedGenericsNode.SimDefs>
        {
            public struct SimDefs : ISimulationPortDefinition
            {
                public MessageInput<CloselyRelatedGenericsNode, BlobAssetReference<int>> Graph;
                public MessageInput<CloselyRelatedGenericsNode, BlobAssetReference<float>> GraphInstance;
            }

            struct Data : INodeData, IMsgHandler<BlobAssetReference<int>>, IMsgHandler<BlobAssetReference<float>>
            {
                public void HandleMessage(MessageContext ctx, in BlobAssetReference<int> msg) { }

                public void HandleMessage(MessageContext ctx, in BlobAssetReference<float> msg) { }
            }
        }

        [Test]
        public void CanAuthorNode_WithIdenticalGenericMessagesTypes_ButDifferentNestedInstantiation()
        {
            using (var set = new NodeSet())
            {
                set.Destroy(set.Create<CloselyRelatedGenericsNode>());
            }
        }
    }
}
