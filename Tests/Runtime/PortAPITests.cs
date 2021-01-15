using System;
using System.Linq;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
#if !UNITY_EDITOR
using Unity.Jobs.LowLevel.Unsafe;
#endif

namespace Unity.DataFlowGraph.Tests
{
    public class PortAPITests
    {
        // TODO:
        // * Check that ports are numbered upwards
        // * Fail for direct declarations of data ports without pattern

        public class NodeWithOneMessageIO : SimulationNodeDefinition<NodeWithOneMessageIO.SimPorts>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                public MessageInput<NodeWithOneMessageIO, int> Input;
                public MessageOutput<NodeWithOneMessageIO, int> Output;
#pragma warning restore 649
            }

            struct Node : INodeData, IMsgHandler<int>
            {
                public void HandleMessage(MessageContext ctx, in int msg) { }
            }
        }

        public class NodeWithMessageArrays : SimulationNodeDefinition<NodeWithMessageArrays.SimPorts>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                public PortArray<MessageInput<NodeWithMessageArrays, int>> Input;
                public PortArray<MessageOutput<NodeWithMessageArrays, int>> Output;
#pragma warning restore 649
            }

            struct Node : INodeData, IMsgHandler<int>
            {
                public void HandleMessage(MessageContext ctx, in int msg) { }
            }
        }

        class NodeWithManyInputs
            : SimulationKernelNodeDefinition<NodeWithManyInputs.SimPorts, NodeWithManyInputs.KernelDefs>
            , TestDSL
        {
            public struct SimPorts : ISimulationPortDefinition
            {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                public MessageInput<NodeWithManyInputs, int> I0, I1, I2;
                public PortArray<MessageInput<NodeWithManyInputs, int>> IArray3;
                public DSLInput<NodeWithManyInputs, DSL, TestDSL> D4, D5, D6;
#pragma warning restore 649
            }

            public struct KernelDefs : IKernelPortDefinition
            {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                public DataInput<NodeWithManyInputs, int> K7, K8, K9;
                public PortArray<DataInput<NodeWithManyInputs, int>> KArray10;
#pragma warning restore 649
            }

            struct Data : IKernelData { }

            [BurstCompile(CompileSynchronously = true)]
            struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                public void Execute(RenderContext ctx, in Data data, ref KernelDefs ports) { }
            }

            struct Node : INodeData, IMsgHandler<int>
            {
                public void HandleMessage(MessageContext ctx, in int msg) { }
            }
        }

        class NodeWithManyOutputs : SimulationKernelNodeDefinition<NodeWithManyOutputs.SimPorts, NodeWithManyOutputs.KernelDefs>, TestDSL
        {
            public struct SimPorts : ISimulationPortDefinition
            {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                public MessageOutput<NodeWithManyOutputs, int> M0, M1, M2;
                public PortArray<MessageOutput<NodeWithManyOutputs, int>> MArray3;
                public DSLOutput<NodeWithManyOutputs, DSL, TestDSL> D4, D5, D6;
#pragma warning restore 649
            }

            public struct KernelDefs : IKernelPortDefinition
            {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                public DataOutput<NodeWithManyOutputs, int> K7, K8, K9;
                public PortArray<DataOutput<NodeWithManyOutputs, int>> KArray10;
#pragma warning restore 649
            }

            struct Data : IKernelData { }

            [BurstCompile(CompileSynchronously = true)]
            struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                public void Execute(RenderContext ctx, in Data data, ref KernelDefs ports) { }
            }
        }

        class NodeWithMixedInputs
            : SimulationNodeDefinition<NodeWithMixedInputs.SimPorts>
            , TestDSL
        {
            public struct SimPorts : ISimulationPortDefinition
            {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                public MessageInput<NodeWithMixedInputs, int> I0;
                public DSLInput<NodeWithMixedInputs, DSL, TestDSL> D0;
                public MessageInput<NodeWithMixedInputs, int> I1;
                public DSLInput<NodeWithMixedInputs, DSL, TestDSL> D1;
#pragma warning restore 649
            }

            struct Node : INodeData, IMsgHandler<int>
            {
                public void HandleMessage(MessageContext ctx, in int msg) { }
            }
        }

        class NodeWithOneDSLIO : SimulationNodeDefinition<NodeWithOneDSLIO.SimPorts>, TestDSL
        {
            public struct SimPorts : ISimulationPortDefinition
            {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                public DSLInput<NodeWithOneDSLIO, DSL, TestDSL> Input;
                public DSLOutput<NodeWithOneDSLIO, DSL, TestDSL> Output;
#pragma warning restore 649
            }
        }

        public class NodeWithNonStaticPorts_OutsideOfPortDefinition
            : SimulationNodeDefinition<NodeWithNonStaticPorts_OutsideOfPortDefinition.EmptyPorts>
        {
            public struct EmptyPorts : ISimulationPortDefinition { }

            public MessageInput<NodeWithNonStaticPorts_OutsideOfPortDefinition, int> Input;
            public MessageOutput<NodeWithNonStaticPorts_OutsideOfPortDefinition, int> Output;

            struct Node : INodeData, IMsgHandler<int>
            {
                public void HandleMessage(MessageContext ctx, in int msg) { }
            }
        }

        public class NodeWithStaticPorts_OutsideOfPortDefinition
            : SimulationNodeDefinition<NodeWithStaticPorts_OutsideOfPortDefinition.EmptyPorts>
        {
            public struct EmptyPorts : ISimulationPortDefinition { }

            public static MessageInput<NodeWithStaticPorts_OutsideOfPortDefinition, int> Input;
            public static MessageOutput<NodeWithStaticPorts_OutsideOfPortDefinition, int> Output;

            struct Node : INodeData, IMsgHandler<int>
            {
                public void HandleMessage(MessageContext ctx, in int msg) { }
            }
        }

        public class NodeWithOneDataIO : KernelNodeDefinition<NodeWithOneDataIO.KernelDefs>
        {
            public struct KernelDefs : IKernelPortDefinition
            {
                public struct Aggregate
                {
                    public Buffer<int> SubBuffer1;
                    public Buffer<int> SubBuffer2;
                }
                public DataInput<NodeWithOneDataIO, int> Input;
                public DataOutput<NodeWithOneDataIO, int> Output;
                public DataOutput<NodeWithOneDataIO, Buffer<int>> BufferOutput;
                public DataOutput<NodeWithOneDataIO, Aggregate> AggregateBufferOutput;

            }

            struct Data : IKernelData { }

            [BurstCompile(CompileSynchronously = true)]
            struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                public void Execute(RenderContext ctx, in Data data, ref KernelDefs ports) { }
            }
        }

        class NodeWithDataArrays : KernelNodeDefinition<NodeWithDataArrays.KernelDefs>
        {
            public struct KernelDefs : IKernelPortDefinition
            {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                public PortArray<DataInput<NodeWithDataArrays, int>> Input;
                public PortArray<DataOutput<NodeWithDataArrays, int>> Output;
#pragma warning restore 649
            }

            struct Data : IKernelData { }

            [BurstCompile(CompileSynchronously = true)]
            struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                public void Execute(RenderContext ctx, in Data data, ref KernelDefs ports) { }
            }
        }

        [IsNotInstantiable]
        class NodeWithMessageAndDSLPortsInIKernelPortDefinition
            : KernelNodeDefinition<NodeWithMessageAndDSLPortsInIKernelPortDefinition.KernelDefs>
                , TestDSL
        {
            public struct KernelDefs : IKernelPortDefinition
            {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                public DSLInput<NodeWithMessageAndDSLPortsInIKernelPortDefinition, DSL, TestDSL> Input;
                public MessageOutput<NodeWithOneMessageIO, int> Output;
#pragma warning restore 649
            }

            struct Data : IKernelData { }

            [BurstCompile(CompileSynchronously = true)]
            struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                public void Execute(RenderContext ctx, in Data data, ref KernelDefs ports) { }
            }
        }

        Dictionary<PortDescription.Category, ushort> CreateEmptyPortCounter()
        {
            var dict = new Dictionary<PortDescription.Category, ushort>();
            dict.Add(PortDescription.Category.Data, 0); dict.Add(PortDescription.Category.DomainSpecific, 0); dict.Add(PortDescription.Category.Message, 0);
            return dict;
        }

        [Test]
        public void NodeWithoutAnyDeclarations_DoesNotHaveNullPortDescriptions()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<EmptyNode>();
                var func = set.GetDefinition(node);

                Assert.IsNotNull(func.GetPortDescription(node).Inputs);
                Assert.IsNotNull(func.GetPortDescription(node).Outputs);

                set.Destroy(node);
            }
        }

        [Test]
        public void NodeWithoutAnyDeclarations_DoesNotHaveAnyPortDefinitions()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<EmptyNode>();
                var func = set.GetDefinition(node);

                Assert.Zero(func.GetPortDescription(node).Inputs.Count);
                Assert.Zero(func.GetPortDescription(node).Outputs.Count);

                set.Destroy(node);
            }
        }

        [Test]
        public void QueryingPortDescription_WithDefaultNode_ThrowsException()
        {
            using (var set = new NodeSet())
            {
                Assert.Throws<ArgumentException>(
                    () => set.GetDefinition<EmptyNode>().GetPortDescription(new NodeHandle()));

                var node = set.Create<EmptyNode>();
                Assert.Throws<ArgumentException>(
                    () => set.GetDefinition(node).GetPortDescription(new NodeHandle()));
                set.Destroy(node);
            }
        }

        [Test]
        public void QueryingPortDescription_WithWrongNodeType_ThrowsException()
        {
            using (var set = new NodeSet())
            {
                var emptyNode = set.Create<EmptyNode>();

                Assert.Throws<ArgumentException>(
                    () => set.GetDefinition<NodeWithOneMessageIO>().GetPortDescription(emptyNode));

                var msgNode = set.Create<NodeWithOneMessageIO>();
                Assert.Throws<ArgumentException>(
                    () => set.GetDefinition(msgNode).GetPortDescription(emptyNode));

                set.Destroy(emptyNode, msgNode);
            }
        }

        [Test]
        public void NodeWithMessageIO_IsCorrectlyInsertedInto_PortDescription()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<NodeWithOneMessageIO>();
                var func = set.GetDefinition(node);

                Assert.AreEqual(1, func.GetPortDescription(node).Inputs.Count);
                Assert.AreEqual(1, func.GetPortDescription(node).Outputs.Count);

                Assert.IsTrue(func.GetPortDescription(node).Inputs.All(p => p.Category == PortDescription.Category.Message));
                Assert.IsTrue(func.GetPortDescription(node).Outputs.All(p => p.Category == PortDescription.Category.Message));

                set.Destroy(node);
            }
        }

        [Test]
        public void NodeWithDSLIO_IsCorrectlyInsertedInto_PortDescription()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<NodeWithOneDSLIO>();
                var func = set.GetDefinition(node);

                Assert.AreEqual(1, func.GetPortDescription(node).Inputs.Count);
                Assert.AreEqual(1, func.GetPortDescription(node).Outputs.Count);

                Assert.IsTrue(func.GetPortDescription(node).Inputs.All(p => p.Category == PortDescription.Category.DomainSpecific));
                Assert.IsTrue(func.GetPortDescription(node).Outputs.All(p => p.Category == PortDescription.Category.DomainSpecific));

                set.Destroy(node);
            }
        }

        [Test]
        public void NodeWithDataIO_IsCorrectlyInsertedInto_PortDescription()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<NodeWithOneDataIO>();
                var func = set.GetDefinition(node);

                Assert.AreEqual(1, func.GetPortDescription(node).Inputs.Count);
                Assert.AreEqual(3, func.GetPortDescription(node).Outputs.Count);

                Assert.IsTrue(func.GetPortDescription(node).Inputs.All(p => p.Category == PortDescription.Category.Data));
                Assert.IsTrue(func.GetPortDescription(node).Outputs.All(p => p.Category == PortDescription.Category.Data));

                set.Destroy(node);
            }
        }

        [Test]
        public void NodeWithMessageArrays_IsCorrectlyInsertedInto_PortDescription()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<NodeWithMessageArrays>();
                var func = set.GetDefinition(node);

                Assert.AreEqual(1, func.GetPortDescription(node).Inputs.Count);
                Assert.AreEqual(1, func.GetPortDescription(node).Outputs.Count);

                Assert.IsTrue(func.GetPortDescription(node).Inputs.All(p => p.Category == PortDescription.Category.Message));
                Assert.IsTrue(func.GetPortDescription(node).Outputs.All(p => p.Category == PortDescription.Category.Message));

                Assert.IsTrue(func.GetPortDescription(node).Inputs[0].IsPortArray);
                Assert.IsTrue(func.GetPortDescription(node).Outputs[0].IsPortArray);

                set.Destroy(node);
            }
        }

        [Test]
        public void NodeWithDataArrays_IsCorrectlyInsertedInto_PortDescription()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<NodeWithDataArrays>();
                var func = set.GetDefinition(node);

                Assert.AreEqual(1, func.GetPortDescription(node).Inputs.Count);
                Assert.AreEqual(1, func.GetPortDescription(node).Outputs.Count);

                Assert.IsTrue(func.GetPortDescription(node).Inputs.All(p => p.Category == PortDescription.Category.Data));
                Assert.IsTrue(func.GetPortDescription(node).Outputs.All(p => p.Category == PortDescription.Category.Data));

                Assert.IsTrue(func.GetPortDescription(node).Inputs[0].IsPortArray);
                Assert.IsTrue(func.GetPortDescription(node).Outputs[0].IsPortArray);

                set.Destroy(node);
            }
        }

        [Test]
        public void NodeWithDataBufferOutput_IsCorrectlyInsertedInto_PortDescription()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<NodeWithOneDataIO>();
                var func = set.GetDefinition(node);

                var outputs = func.GetPortDescription(node).Outputs;

                Assert.AreEqual(3, outputs.Count);

                Assert.AreEqual(0, outputs[0].BufferInfos.Count);

                Assert.AreEqual(1, outputs[1].BufferInfos.Count);
                Assert.AreEqual(0, outputs[1].BufferInfos[0].Offset);
                Assert.AreEqual(sizeof(int), outputs[1].BufferInfos[0].ItemType.Size);

                var nodeAggregateType = typeof(NodeWithOneDataIO.KernelDefs.Aggregate);
                var subBuffer1Offset = UnsafeUtility.GetFieldOffset(nodeAggregateType.GetField("SubBuffer1"));
                var subBuffer2Offset = UnsafeUtility.GetFieldOffset(nodeAggregateType.GetField("SubBuffer2"));
                Assert.AreEqual(2, outputs[2].BufferInfos.Count);
                Assert.AreEqual(Math.Min(subBuffer1Offset, subBuffer2Offset), outputs[2].BufferInfos[0].Offset);
                Assert.AreEqual(sizeof(int), outputs[2].BufferInfos[0].ItemType.Size);
                Assert.AreEqual(Math.Max(subBuffer1Offset, subBuffer2Offset), outputs[2].BufferInfos[1].Offset);
                Assert.AreEqual(sizeof(int), outputs[2].BufferInfos[1].ItemType.Size);

                set.Destroy(node);
            }
        }

        public class ComplexKernelAggregateNode : KernelNodeDefinition<ComplexKernelAggregateNode.KernelDefs>
        {
            public struct ComplexAggregate
            {
                public float FloatScalar;
                public Buffer<double> Doubles;
                public short ShortScalar;
                public Buffer<float4> Vectors;
                public byte ByteScalar;
                public Buffer<byte> Bytes;
                public Buffer<float4x4> Matrices;
            }

            public struct KernelDefs : IKernelPortDefinition
            {
                public DataInput<ComplexKernelAggregateNode, uint> RandomSeed;
                public DataInput<ComplexKernelAggregateNode, ComplexAggregate> Input;
                public DataOutput<ComplexKernelAggregateNode, uint> __padding;
                public DataOutput<ComplexKernelAggregateNode, ComplexAggregate> Output;
            }

            struct Data : IKernelData { }

            [BurstCompile(CompileSynchronously = true)]
            struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                public void Execute(RenderContext ctx, in Data data, ref KernelDefs ports) { }
            }
        }

        [Test]
        public void NodeWithComplexDataBufferAggregate_IsCorrectlyInsertedInto_PortDescription()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<ComplexKernelAggregateNode>();
                var func = set.GetDefinition(node);

                var inputs = func.GetPortDescription(node).Inputs;
                Assert.AreEqual(2, inputs.Count);

                Assert.IsFalse(inputs[0].HasBuffers);
                Assert.IsTrue(inputs[1].HasBuffers);

                var outputs = func.GetPortDescription(node).Outputs;

                Assert.AreEqual(2, outputs.Count);

                Assert.AreEqual(0, outputs[0].BufferInfos.Count);

                var nodeAggregateType = typeof(ComplexKernelAggregateNode.ComplexAggregate);
                var expectedOffsetsAndSizes = new List<(int Offset, int Size)>()
                {
                    (UnsafeUtility.GetFieldOffset(nodeAggregateType.GetField("Doubles")), UnsafeUtility.SizeOf<double>()),
                    (UnsafeUtility.GetFieldOffset(nodeAggregateType.GetField("Vectors")), UnsafeUtility.SizeOf<float4>()),
                    (UnsafeUtility.GetFieldOffset(nodeAggregateType.GetField("Bytes")), UnsafeUtility.SizeOf<byte>()),
                    (UnsafeUtility.GetFieldOffset(nodeAggregateType.GetField("Matrices")), UnsafeUtility.SizeOf<float4x4>())
                };
                Assert.AreEqual(expectedOffsetsAndSizes.Count, outputs[1].BufferInfos.Count);
                for (int i = 0; i < expectedOffsetsAndSizes.Count; ++i)
                {
                    for (int j = 0; j < outputs[1].BufferInfos.Count; ++j)
                    {
                        if (expectedOffsetsAndSizes[i].Offset == outputs[1].BufferInfos[j].Offset &&
                            expectedOffsetsAndSizes[i].Size == outputs[1].BufferInfos[j].ItemType.Size)
                        {
                            outputs[1].BufferInfos.RemoveAt(j);
                            break;
                        }
                    }
                }
                Assert.Zero(outputs[1].BufferInfos.Count);

                set.Destroy(node);
            }
        }

        [Test]
        public void NodeWithMessageAndDataPorts_InKernelPortDefinition_ThrowsError()
        {
            using (var set = new NodeSet())
            {
                Assert.Throws<InvalidNodeDefinitionException>(() => set.Create<NodeWithMessageAndDSLPortsInIKernelPortDefinition>());
            }
        }

        [IsNotInstantiable]
        public class NodeWithDataPortsInSimulationPortDefinition
            : SimulationNodeDefinition<NodeWithDataPortsInSimulationPortDefinition.SimPorts>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
                public DataInput<NodeWithDataPortsInSimulationPortDefinition, float> Input;
                public DataOutput<NodeWithDataPortsInSimulationPortDefinition, float> Output;
            }
        }

        [Test]
        public void NodeWithDataPorts_InSimulationPortDefinition_ThrowsError()
        {
            using (var set = new NodeSet())
            {
                Assert.Throws<InvalidNodeDefinitionException>(() => set.Create<NodeWithDataPortsInSimulationPortDefinition>());
            }
        }

        [Test]
        public void NodeWithNonStaticPortDeclarations_OutsideOfPortDefinition_AreNotPickedUp()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<NodeWithNonStaticPorts_OutsideOfPortDefinition>();
                var func = set.GetDefinition(node);

                Assert.AreEqual(0, func.GetPortDescription(node).Inputs.Count);
                Assert.AreEqual(0, func.GetPortDescription(node).Outputs.Count);

                set.Destroy(node);
            }
        }

        [Test]
        public void NodeWithStaticPortDeclarations_OutsideOfPortDefinition_AreNotPickedUp()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<NodeWithStaticPorts_OutsideOfPortDefinition>();
                var func = set.GetDefinition(node);

                Assert.AreEqual(0, func.GetPortDescription(node).Inputs.Count);
                Assert.AreEqual(0, func.GetPortDescription(node).Outputs.Count);

                set.Destroy(node);
            }
        }

        [Test]
        public void PortDeclarations_RespectsDeclarationOrder()
        {
            using (var set = new NodeSet())
            {
                var inputNode = set.Create<NodeWithManyInputs>();
                var outputNode = set.Create<NodeWithManyOutputs>();

                var inputFunc = set.GetDefinition(inputNode);
                var outputFunc = set.GetDefinition(outputNode);

                var inputNodePorts = inputFunc.GetPortDescription(inputNode);
                var outputNodePorts = outputFunc.GetPortDescription(outputNode);

                Assert.AreEqual(11, inputNodePorts.Inputs.Count);
                Assert.AreEqual(0, inputNodePorts.Outputs.Count);

                Assert.AreEqual(0, outputNodePorts.Inputs.Count);
                Assert.AreEqual(11, outputNodePorts.Outputs.Count);

                Assert.AreEqual(NodeWithManyInputs.SimulationPorts.I0.Port, (InputPortID)inputNodePorts.Inputs[0]);
                Assert.AreEqual(NodeWithManyInputs.SimulationPorts.I1.Port, (InputPortID)inputNodePorts.Inputs[1]);
                Assert.AreEqual(NodeWithManyInputs.SimulationPorts.I2.Port, (InputPortID)inputNodePorts.Inputs[2]);
                Assert.AreEqual(NodeWithManyInputs.SimulationPorts.IArray3.GetPortID(), (InputPortID)inputNodePorts.Inputs[3]);
                Assert.AreEqual(NodeWithManyInputs.SimulationPorts.D4.Port, (InputPortID)inputNodePorts.Inputs[4]);
                Assert.AreEqual(NodeWithManyInputs.SimulationPorts.D5.Port, (InputPortID)inputNodePorts.Inputs[5]);
                Assert.AreEqual(NodeWithManyInputs.SimulationPorts.D6.Port, (InputPortID)inputNodePorts.Inputs[6]);
                Assert.AreEqual(NodeWithManyInputs.KernelPorts.K7.Port, (InputPortID)inputNodePorts.Inputs[7]);
                Assert.AreEqual(NodeWithManyInputs.KernelPorts.K8.Port, (InputPortID)inputNodePorts.Inputs[8]);
                Assert.AreEqual(NodeWithManyInputs.KernelPorts.K9.Port, (InputPortID)inputNodePorts.Inputs[9]);
                Assert.AreEqual(NodeWithManyInputs.KernelPorts.KArray10.GetPortID(), (InputPortID)inputNodePorts.Inputs[10]);

                Assert.AreEqual(NodeWithManyOutputs.SimulationPorts.M0.Port, (OutputPortID)outputNodePorts.Outputs[0]);
                Assert.AreEqual(NodeWithManyOutputs.SimulationPorts.M1.Port, (OutputPortID)outputNodePorts.Outputs[1]);
                Assert.AreEqual(NodeWithManyOutputs.SimulationPorts.M2.Port, (OutputPortID)outputNodePorts.Outputs[2]);
                Assert.AreEqual(NodeWithManyOutputs.SimulationPorts.MArray3.GetPortID(), (OutputPortID)outputNodePorts.Outputs[3]);
                Assert.AreEqual(NodeWithManyOutputs.SimulationPorts.D4.Port, (OutputPortID)outputNodePorts.Outputs[4]);
                Assert.AreEqual(NodeWithManyOutputs.SimulationPorts.D5.Port, (OutputPortID)outputNodePorts.Outputs[5]);
                Assert.AreEqual(NodeWithManyOutputs.SimulationPorts.D6.Port, (OutputPortID)outputNodePorts.Outputs[6]);
                Assert.AreEqual(NodeWithManyOutputs.KernelPorts.K7.Port, (OutputPortID)outputNodePorts.Outputs[7]);
                Assert.AreEqual(NodeWithManyOutputs.KernelPorts.K8.Port, (OutputPortID)outputNodePorts.Outputs[8]);
                Assert.AreEqual(NodeWithManyOutputs.KernelPorts.K9.Port, (OutputPortID)outputNodePorts.Outputs[9]);
                Assert.AreEqual(NodeWithManyOutputs.KernelPorts.KArray10.GetPortID(), (OutputPortID)outputNodePorts.Outputs[10]);

                set.Destroy(inputNode, outputNode);
            }
        }

        [Test]
        public void PortDeclarations_WithPortArrays_AreProperlyIdentified()
        {
            using (var set = new NodeSet())
            {
                var inputNode = set.Create<NodeWithManyInputs>();
                var inputFunc = set.GetDefinition(inputNode);
                var inputNodePorts = inputFunc.GetPortDescription(inputNode);

                Assert.AreEqual(11, inputNodePorts.Inputs.Count);
                Assert.AreEqual(0, inputNodePorts.Outputs.Count);

                Assert.IsFalse(inputNodePorts.Inputs[0].IsPortArray);
                Assert.IsFalse(inputNodePorts.Inputs[1].IsPortArray);
                Assert.IsFalse(inputNodePorts.Inputs[2].IsPortArray);
                Assert.IsTrue(inputNodePorts.Inputs[3].IsPortArray);
                Assert.IsFalse(inputNodePorts.Inputs[4].IsPortArray);
                Assert.IsFalse(inputNodePorts.Inputs[5].IsPortArray);
                Assert.IsFalse(inputNodePorts.Inputs[6].IsPortArray);
                Assert.IsFalse(inputNodePorts.Inputs[7].IsPortArray);
                Assert.IsFalse(inputNodePorts.Inputs[8].IsPortArray);
                Assert.IsFalse(inputNodePorts.Inputs[9].IsPortArray);
                Assert.IsTrue(inputNodePorts.Inputs[10].IsPortArray);

                var outputNode = set.Create<NodeWithManyOutputs>();
                var outputFunc = set.GetDefinition(outputNode);
                var outputNodePorts = outputFunc.GetPortDescription(outputNode);

                Assert.AreEqual(0, outputNodePorts.Inputs.Count);
                Assert.AreEqual(11, outputNodePorts.Outputs.Count);

                Assert.IsFalse(outputNodePorts.Outputs[0].IsPortArray);
                Assert.IsFalse(outputNodePorts.Outputs[1].IsPortArray);
                Assert.IsFalse(outputNodePorts.Outputs[2].IsPortArray);
                Assert.IsTrue(outputNodePorts.Outputs[3].IsPortArray);
                Assert.IsFalse(outputNodePorts.Outputs[4].IsPortArray);
                Assert.IsFalse(outputNodePorts.Outputs[5].IsPortArray);
                Assert.IsFalse(outputNodePorts.Outputs[6].IsPortArray);
                Assert.IsFalse(outputNodePorts.Outputs[7].IsPortArray);
                Assert.IsFalse(outputNodePorts.Outputs[8].IsPortArray);
                Assert.IsFalse(outputNodePorts.Outputs[9].IsPortArray);
                Assert.IsTrue(outputNodePorts.Outputs[10].IsPortArray);

                set.Destroy(inputNode, outputNode);
            }
        }

        public class NodeWithSomeNonPublicPorts
            : SimulationKernelNodeDefinition<NodeWithSomeNonPublicPorts.SimPorts, NodeWithSomeNonPublicPorts.KernelDefs>
            , TestDSL
        {
            public struct SimPorts : ISimulationPortDefinition
            {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                public MessageInput<NodeWithSomeNonPublicPorts, int> IM0;
                MessageInput<NodeWithSomeNonPublicPorts, int> IM1;
                public PortArray<MessageInput<NodeWithSomeNonPublicPorts, int>> IMArray2;
                internal PortArray<MessageInput<NodeWithSomeNonPublicPorts, int>> IMArray3;
                public DSLInput<NodeWithSomeNonPublicPorts, DSL, TestDSL> IDSL4;
                DSLInput<NodeWithSomeNonPublicPorts, DSL, TestDSL> IDSL5;
                public MessageOutput<NodeWithSomeNonPublicPorts, int> OM0;
                internal MessageOutput<NodeWithSomeNonPublicPorts, int> OM1;
                public PortArray<MessageOutput<NodeWithSomeNonPublicPorts, int>> OMArray2;
                PortArray<MessageOutput<NodeWithSomeNonPublicPorts, int>> OMArray3;
                public DSLOutput<NodeWithSomeNonPublicPorts, DSL, TestDSL> ODSL4;
                internal DSLOutput<NodeWithSomeNonPublicPorts, DSL, TestDSL> ODSL5;
#pragma warning restore 649
            }

            public struct KernelDefs : IKernelPortDefinition
            {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                public DataInput<NodeWithSomeNonPublicPorts, int> IK6;
                DataInput<NodeWithSomeNonPublicPorts, int> IK7;
                public PortArray<DataInput<NodeWithSomeNonPublicPorts, int>> IKArray8;
                internal PortArray<DataInput<NodeWithSomeNonPublicPorts, int>> IKArray9;
                public DataOutput<NodeWithSomeNonPublicPorts, int> OK6;
                DataOutput<NodeWithSomeNonPublicPorts, int> OK7;
#pragma warning restore 649
            }

            struct Data : IKernelData { }

            [BurstCompile(CompileSynchronously = true)]
            struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                public void Execute(RenderContext ctx, in Data data, ref KernelDefs ports) { }
            }

            struct Node : INodeData, IMsgHandler<int>
            {
                public void HandleMessage(MessageContext ctx, in int msg) { }
            }
        }

        [Test]
        public void PublicPortDescriptions_DoNotInclude_NonPublicPorts()
        {
            using (var set = new NodeSet())
            {
                var publicPorts = set.GetStaticPortDescription<NodeWithSomeNonPublicPorts>();
                var allPorts = set.GetDefinition<NodeWithSomeNonPublicPorts>().AutoPorts;

                Assert.AreEqual(5, publicPorts.Inputs.Count);
                Assert.AreEqual(10, allPorts.Inputs.Count);
                for (int i = 0; i < publicPorts.Inputs.Count; ++i)
                {
                    Assert.AreEqual(publicPorts.Inputs[i], allPorts.Inputs[i * 2]);
                    Assert.True(publicPorts.Inputs[i].IsPublic);
                    Assert.True(allPorts.Inputs[i * 2].IsPublic);
                    Assert.False(allPorts.Inputs[i * 2 + 1].IsPublic);
                    Assert.AreNotEqual((InputPortID)publicPorts.Inputs[i], (InputPortID)allPorts.Inputs[i * 2 + 1]);
                }

                Assert.AreEqual(4, publicPorts.Outputs.Count);
                Assert.AreEqual(8, allPorts.Outputs.Count);
                for (int i = 0; i < publicPorts.Outputs.Count; ++i)
                {
                    Assert.AreEqual(publicPorts.Outputs[i], allPorts.Outputs[i*2]);
                    Assert.True(publicPorts.Outputs[i].IsPublic);
                    Assert.True(allPorts.Outputs[i * 2].IsPublic);
                    Assert.False(allPorts.Outputs[i * 2 + 1].IsPublic);
                    Assert.AreNotEqual((OutputPortID)publicPorts.Outputs[i], (OutputPortID)allPorts.Outputs[i * 2 + 1]);
                }
            }
        }

        [Test]
        public void SimulationPorts_AreNotAllNullAssignedPortIDs()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<NodeWithMixedInputs>();
                var ports = NodeWithMixedInputs.SimulationPorts;
                Assert.NotZero(ports.I0.Port.Port.CategoryCounter + ports.I1.Port.Port.CategoryCounter);
                Assert.NotZero(ports.D0.Port.Port.CategoryCounter + ports.D1.Port.Port.CategoryCounter);

                set.Destroy(node);
            }
        }

        [Test]
        public void ExplicitlyCasting_WrongPortType_FromPortArray_Throws()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<NodeWithMessageArrays>();

                OutputPortID outputPortId;
                Assert.Throws<InvalidOperationException>(() => outputPortId = (OutputPortID)NodeWithMessageArrays.SimulationPorts.Input);
                InputPortID inputPortId;
                Assert.Throws<InvalidOperationException>(() => inputPortId = (InputPortID)NodeWithMessageArrays.SimulationPorts.Output);

                set.Destroy(node);
            }
        }

        [Test]
        public void SimulationPortDescription_AreNotAllNullAssignedPortIDs()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<NodeWithMixedInputs>();
                var func = set.GetDefinition(node);
                var inputNodePorts = func.GetPortDescription(node);

                var dict = CreateEmptyPortCounter();
                var foundCategories = new HashSet<PortDescription.Category>();

                foreach (var inputPort in inputNodePorts.Inputs)
                {
                    foundCategories.Add(inputPort.Category);
                    dict[inputPort.Category] += inputPort.PortID.Port.CategoryCounter;
                }

                foreach (var cat in foundCategories)
                    Assert.NotZero(dict[cat]);

                set.Destroy(node);
            }
        }

        [Test]
        public void ExpectPortIDs_AreAssignedIndices_BasedOnPortDeclarationOrder_UsingNodeTyped_StaticPortDescription([ValueSource(typeof(TestUtilities), nameof(TestUtilities.FindInstantiableTestNodes))] Type nodeType)
        {
            // This assumes that PortDescriptions respect port declaration order (<see cref="PortDeclarations_RespectsDeclarationOrder"/>)
            using (var set = new NodeSet())
            {
                var def = set.GetDefinitionFromType(nodeType);
                var ports = set.GetStaticPortDescriptionFromType(nodeType);

                if (def.AutoPorts.Inputs.All(p => p.IsPublic))
                {
                    var dict = CreateEmptyPortCounter();

                    foreach (var input in ports.Inputs)
                        Assert.AreEqual(dict[input.Category]++, ((InputPortID)input).Port.CategoryCounter);
                }


                if (def.AutoPorts.Outputs.All(p => p.IsPublic))
                {
                    var dict = CreateEmptyPortCounter();

                    foreach (var output in ports.Outputs)
                        Assert.AreEqual(dict[output.Category]++, ((OutputPortID)output).Port.CategoryCounter);
                }

            }
        }

        [Test]
        public void ExpectPortIDs_AreAssignedIndices_BasedOnPortDeclarationOrder_UsingDefinitionFromHandle_StaticOrDynamic([ValueSource(typeof(TestUtilities), nameof(TestUtilities.FindInstantiableTestNodes))] Type nodeType)
        {
            // This assumes that PortDescriptions respect port declaration order (<see cref="PortDeclarations_RespectsDeclarationOrder"/>)
            using (var set = new NodeSet())
            {
                var handle = set.CreateNodeFromType(nodeType);
                var def = set.GetDefinition(handle);
                var staticPorts = set.GetStaticPortDescription(handle);
                var weakPorts = set.GetPortDescription(handle);

                if (def.AutoPorts.Inputs.All(p => p.IsPublic))
                {
                    var dict = CreateEmptyPortCounter();

                    Assert.AreEqual(staticPorts.Inputs.Count, weakPorts.Inputs.Count);

                    for (int i = 0; i < staticPorts.Inputs.Count; ++i)
                    {
                        var input = staticPorts.Inputs[i];
                        Assert.AreEqual(input, weakPorts.Inputs[i]);
                        Assert.AreEqual(dict[input.Category], ((InputPortID)input).Port.CategoryCounter);

                        dict[input.Category] += 1;
                    }
                }

                if (def.AutoPorts.Outputs.All(p => p.IsPublic))
                {
                    var dict = CreateEmptyPortCounter();

                    Assert.AreEqual(staticPorts.Outputs.Count, weakPorts.Outputs.Count);

                    for (int i = 0; i < staticPorts.Outputs.Count; ++i)
                    {
                        var output = staticPorts.Outputs[i];
                        Assert.AreEqual(output, weakPorts.Outputs[i]);
                        Assert.AreEqual(dict[output.Category], ((OutputPortID)output).Port.CategoryCounter);

                        dict[output.Category] += 1;
                    }
                }

                set.Destroy(handle);
            }
        }

        [Test]
        public void MixedPortDeclarations_RespectsDeclarationOrder()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<NodeWithMixedInputs>();

                var func = set.GetDefinition(node);

                var inputNodePorts = func.GetPortDescription(node);

                Assert.AreEqual(4, inputNodePorts.Inputs.Count);

                Assert.AreEqual(NodeWithMixedInputs.SimulationPorts.I0.Port, (InputPortID)inputNodePorts.Inputs[0]);
                Assert.AreEqual(NodeWithMixedInputs.SimulationPorts.D0.Port, (InputPortID)inputNodePorts.Inputs[1]);
                Assert.AreEqual(NodeWithMixedInputs.SimulationPorts.I1.Port, (InputPortID)inputNodePorts.Inputs[2]);
                Assert.AreEqual(NodeWithMixedInputs.SimulationPorts.D1.Port, (InputPortID)inputNodePorts.Inputs[3]);

                set.Destroy(node);
            }
        }

        [Test]
        public void CanHave_MessageDSLAndData_AsInputsAndOuputs_InOneNode()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<NodeWithAllTypesOfPorts>();
                var func = set.GetDefinition(node);

                Assert.AreEqual(7, func.GetPortDescription(node).Inputs.Count);
                Assert.AreEqual(7, func.GetPortDescription(node).Outputs.Count);

                set.Destroy(node);
            }
        }

        [Test]
        public void PortAPIs_CorrectlyStoreTypeInformation_InPortDeclaration()
        {
            using (var set = new NodeSet())
            {
                var typeSet = new[] { typeof(int), typeof(float), typeof(double) };

                var intNode = set.Create<NodeWithParametricPortType<int>>();
                var floatNode = set.Create<NodeWithParametricPortType<float>>();
                var doubleNode = set.Create<NodeWithParametricPortType<double>>();

                int typeCounter = 0;

                foreach (var node in new NodeHandle[] { intNode, floatNode, doubleNode })
                {
                    var func = set.GetDefinition(node);

                    foreach (var inputPort in func.GetPortDescription(node).Inputs)
                    {
                        Assert.AreEqual(typeSet[typeCounter], inputPort.Type);
                    }

                    foreach (var outputPort in func.GetPortDescription(node).Outputs)
                    {
                        Assert.AreEqual(typeSet[typeCounter], outputPort.Type);
                    }

                    set.Destroy(node);
                    typeCounter++;
                }
            }
        }

        class NodeWithParametricPortTypeIncludingDSLs<T>
            : SimulationKernelNodeDefinition<NodeWithParametricPortTypeIncludingDSLs<T>.SimPorts, NodeWithParametricPortTypeIncludingDSLs<T>.KernelDefs>
            , TestDSL
                where T : struct
        {
            public struct SimPorts : ISimulationPortDefinition
            {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                public MessageInput<NodeWithParametricPortTypeIncludingDSLs<T>, T> MessageIn;
                public MessageOutput<NodeWithParametricPortTypeIncludingDSLs<T>, T> MessageOut;

                public DSLInput<NodeWithParametricPortTypeIncludingDSLs<T>, DSL, TestDSL> DSLIn;
                public DSLOutput<NodeWithParametricPortTypeIncludingDSLs<T>, DSL, TestDSL> DSLOut;
#pragma warning restore 649
            }


            public struct KernelDefs : IKernelPortDefinition
            {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                public DataInput<NodeWithParametricPortTypeIncludingDSLs<T>, T> Input;
                public DataOutput<NodeWithParametricPortTypeIncludingDSLs<T>, T> Output;
#pragma warning restore 649
            }

            struct Data : IKernelData { }

            [BurstCompile(CompileSynchronously = true)]
            struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                public void Execute(RenderContext ctx, in Data data, ref KernelDefs ports) { }
            }

            struct Node : INodeData, IMsgHandler<T>
            {
                public void HandleMessage(MessageContext ctx, in T msg) { }
            }
        }

        [Test]
        public void PortsAreGenerated_EvenForParametricNodeTypes()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<NodeWithParametricPortTypeIncludingDSLs<int>>();

                var func = set.GetDefinition(node);

                Assert.AreEqual(3, func.GetPortDescription(node).Inputs.Count);
                Assert.AreEqual(3, func.GetPortDescription(node).Outputs.Count);

                set.Destroy(node);
            }
        }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        [Test]
        public void CanOnlyHave_BlittableTypes_AsDataPorts()
        {
            using (var set = new NodeSet())
            {
                // Bool is special-cased now to be allowed. See #199
                Assert.DoesNotThrow(() => set.Destroy(set.Create<NodeWithParametricPortType<bool>>()));
                Assert.Throws<InvalidNodeDefinitionException>(() => set.Create<NodeWithParametricPortType<NativeArray<int>>>());
            }
        }
#endif

        [Test]
        public void DefaultConstructedPort_DoesNotEqualDeclaredPort()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<NodeWithAllTypesOfPorts>();

                Assert.AreNotEqual(NodeWithAllTypesOfPorts.SimulationPorts.DSLIn, new DSLInput<NodeWithAllTypesOfPorts, DSL, TestDSL>());
                Assert.AreNotEqual(NodeWithAllTypesOfPorts.SimulationPorts.DSLOut, new DSLOutput<NodeWithAllTypesOfPorts, DSL, TestDSL>());

                Assert.AreNotEqual(NodeWithAllTypesOfPorts.SimulationPorts.MessageIn, new MessageInput<NodeWithAllTypesOfPorts, int>());
                Assert.AreNotEqual(NodeWithAllTypesOfPorts.SimulationPorts.MessageArrayIn, new PortArray<MessageInput<NodeWithAllTypesOfPorts, int>>());
                Assert.AreNotEqual(NodeWithAllTypesOfPorts.SimulationPorts.MessageOut, new MessageOutput<NodeWithAllTypesOfPorts, int>());
                Assert.AreNotEqual(NodeWithAllTypesOfPorts.SimulationPorts.MessageArrayOut, new PortArray<MessageOutput<NodeWithAllTypesOfPorts, int>>());

                Assert.AreNotEqual(NodeWithAllTypesOfPorts.KernelPorts.InputScalar, new DataInput<NodeWithAllTypesOfPorts, int>());
                Assert.AreNotEqual(NodeWithAllTypesOfPorts.KernelPorts.InputArrayScalar, new PortArray<DataInput<NodeWithAllTypesOfPorts, int>>());
                Assert.AreNotEqual(NodeWithAllTypesOfPorts.KernelPorts.OutputScalar, new DataOutput<NodeWithAllTypesOfPorts, int>());
                Assert.AreNotEqual(NodeWithAllTypesOfPorts.KernelPorts.OutputArrayScalar, new PortArray<DataOutput<NodeWithAllTypesOfPorts, int>>());

                Assert.AreNotEqual(NodeWithAllTypesOfPorts.KernelPorts.InputBuffer, new DataInput<NodeWithAllTypesOfPorts, Buffer<int>>());
                Assert.AreNotEqual(NodeWithAllTypesOfPorts.KernelPorts.InputArrayBuffer, new PortArray<DataInput<NodeWithAllTypesOfPorts, Buffer<int>>>());
                Assert.AreNotEqual(NodeWithAllTypesOfPorts.KernelPorts.OutputBuffer, new DataOutput<NodeWithAllTypesOfPorts, Buffer<int>>());
                Assert.AreNotEqual(NodeWithAllTypesOfPorts.KernelPorts.OutputArrayBuffer, new PortArray<DataOutput<NodeWithAllTypesOfPorts, Buffer<int>>>());

                set.Destroy(node);
            }
        }

        [Test]
        public void ExplicitInputPortIDConversionOperator_IsConsistentWithStorage_AndPortDescriptionTable()
        {
            using (var set = new NodeSet())
            {
                var klass = set.GetDefinition<NodeWithAllTypesOfPorts>();
                var node = set.Create<NodeWithAllTypesOfPorts>();
                var desc = klass.GetPortDescription(node);

                Assert.AreEqual(NodeWithAllTypesOfPorts.SimulationPorts.MessageIn.Port, (InputPortID)NodeWithAllTypesOfPorts.SimulationPorts.MessageIn);
                Assert.AreEqual((InputPortID)desc.Inputs[0], (InputPortID)NodeWithAllTypesOfPorts.SimulationPorts.MessageIn);

                Assert.AreEqual(NodeWithAllTypesOfPorts.SimulationPorts.MessageArrayIn.GetPortID(), (InputPortID)NodeWithAllTypesOfPorts.SimulationPorts.MessageArrayIn);
                Assert.AreEqual((InputPortID)desc.Inputs[1], (InputPortID)NodeWithAllTypesOfPorts.SimulationPorts.MessageArrayIn);

                Assert.AreEqual(NodeWithAllTypesOfPorts.SimulationPorts.DSLIn.Port, (InputPortID)NodeWithAllTypesOfPorts.SimulationPorts.DSLIn);
                Assert.AreEqual((InputPortID)desc.Inputs[2], (InputPortID)NodeWithAllTypesOfPorts.SimulationPorts.DSLIn);

                Assert.AreEqual(NodeWithAllTypesOfPorts.KernelPorts.InputBuffer.Port, (InputPortID)NodeWithAllTypesOfPorts.KernelPorts.InputBuffer);
                Assert.AreEqual((InputPortID)desc.Inputs[3], (InputPortID)NodeWithAllTypesOfPorts.KernelPorts.InputBuffer);

                Assert.AreEqual(NodeWithAllTypesOfPorts.KernelPorts.InputArrayBuffer.GetPortID(), (InputPortID)NodeWithAllTypesOfPorts.KernelPorts.InputArrayBuffer);
                Assert.AreEqual((InputPortID)desc.Inputs[4], (InputPortID)NodeWithAllTypesOfPorts.KernelPorts.InputArrayBuffer);

                Assert.AreEqual(NodeWithAllTypesOfPorts.KernelPorts.InputScalar.Port, (InputPortID)NodeWithAllTypesOfPorts.KernelPorts.InputScalar);
                Assert.AreEqual((InputPortID)desc.Inputs[5], (InputPortID)NodeWithAllTypesOfPorts.KernelPorts.InputScalar);

                Assert.AreEqual(NodeWithAllTypesOfPorts.KernelPorts.InputArrayScalar.GetPortID(), (InputPortID)NodeWithAllTypesOfPorts.KernelPorts.InputArrayScalar);
                Assert.AreEqual((InputPortID)desc.Inputs[6], (InputPortID)NodeWithAllTypesOfPorts.KernelPorts.InputArrayScalar);

                set.Destroy(node);
            }
        }

        [Test]
        public void ExplicitOutputPortIDConversionOperator_IsConsistentWithStorage_AndPortDescriptionTable()
        {
            using (var set = new NodeSet())
            {
                var klass = set.GetDefinition<NodeWithAllTypesOfPorts>();
                var node = set.Create<NodeWithAllTypesOfPorts>();
                var desc = klass.GetPortDescription(node);

                Assert.AreEqual(NodeWithAllTypesOfPorts.SimulationPorts.MessageOut.Port, (OutputPortID)NodeWithAllTypesOfPorts.SimulationPorts.MessageOut);
                Assert.AreEqual((OutputPortID)desc.Outputs[0], (OutputPortID)NodeWithAllTypesOfPorts.SimulationPorts.MessageOut);

                Assert.AreEqual(NodeWithAllTypesOfPorts.SimulationPorts.MessageArrayOut.GetPortID(), (OutputPortID)NodeWithAllTypesOfPorts.SimulationPorts.MessageArrayOut);
                Assert.AreEqual((OutputPortID)desc.Outputs[1], (OutputPortID)NodeWithAllTypesOfPorts.SimulationPorts.MessageArrayOut);

                Assert.AreEqual(NodeWithAllTypesOfPorts.SimulationPorts.DSLOut.Port, (OutputPortID)NodeWithAllTypesOfPorts.SimulationPorts.DSLOut);
                Assert.AreEqual((OutputPortID)desc.Outputs[2], (OutputPortID)NodeWithAllTypesOfPorts.SimulationPorts.DSLOut);

                Assert.AreEqual(NodeWithAllTypesOfPorts.KernelPorts.OutputBuffer.Port, (OutputPortID)NodeWithAllTypesOfPorts.KernelPorts.OutputBuffer);
                Assert.AreEqual((OutputPortID)desc.Outputs[3], (OutputPortID)NodeWithAllTypesOfPorts.KernelPorts.OutputBuffer);

                Assert.AreEqual(NodeWithAllTypesOfPorts.KernelPorts.OutputArrayScalar.GetPortID(), (OutputPortID)NodeWithAllTypesOfPorts.KernelPorts.OutputArrayScalar);
                Assert.AreEqual((OutputPortID)desc.Outputs[6], (OutputPortID)NodeWithAllTypesOfPorts.KernelPorts.OutputArrayScalar);

                Assert.AreEqual(NodeWithAllTypesOfPorts.KernelPorts.OutputScalar.Port, (OutputPortID)NodeWithAllTypesOfPorts.KernelPorts.OutputScalar);
                Assert.AreEqual((OutputPortID)desc.Outputs[5], (OutputPortID)NodeWithAllTypesOfPorts.KernelPorts.OutputScalar);

                Assert.AreEqual(NodeWithAllTypesOfPorts.KernelPorts.OutputArrayBuffer.GetPortID(), (OutputPortID)NodeWithAllTypesOfPorts.KernelPorts.OutputArrayBuffer);
                Assert.AreEqual((OutputPortID)desc.Outputs[4], (OutputPortID)NodeWithAllTypesOfPorts.KernelPorts.OutputArrayBuffer);

                set.Destroy(node);
            }
        }

        public class UberWithPartialNonPublicComms
            : SimulationKernelNodeDefinition<UberWithPartialNonPublicComms.SimPorts, UberWithPartialNonPublicComms.KernelDefs>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
                public MessageInput<UberWithPartialNonPublicComms, int> PublicInput;
                public MessageOutput<UberWithPartialNonPublicComms, int> PublicOutput;
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                internal MessageInput<UberWithPartialNonPublicComms, int> PrivateInput;
                internal MessageOutput<UberWithPartialNonPublicComms, int> PrivateOutput;
#pragma warning restore 649
            }

            public struct KernelDefs : IKernelPortDefinition
            {
                public DataInput<UberWithPartialNonPublicComms, int> PublicInput;
                public DataOutput<UberWithPartialNonPublicComms, int> PublicOutput;
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                internal DataInput<UberWithPartialNonPublicComms, int> PrivateInput;
                internal DataOutput<UberWithPartialNonPublicComms, int> PrivateOutput;
#pragma warning restore 649
            }

            struct Node : INodeData, IInit, IDestroy, IMsgHandler<int>
            {
                NodeHandle Child;

                public void Init(InitContext ctx)
                {
                    var self = ctx.Set.CastHandle<UberWithPartialNonPublicComms>(ctx.Handle);
                    var child = ctx.Set.Create<PassthroughTest<int>>();
                    ctx.Set.Connect(self, SimulationPorts.PrivateOutput, child, PassthroughTest<int>.SimulationPorts.Input);
                    ctx.Set.Connect(child, PassthroughTest<int>.SimulationPorts.Output, self, SimulationPorts.PrivateInput);
                    ctx.Set.Connect(self, KernelPorts.PrivateOutput, child, PassthroughTest<int>.KernelPorts.Input);
                    ctx.Set.Connect(child, PassthroughTest<int>.KernelPorts.Output, self, KernelPorts.PrivateInput, NodeSet.ConnectionType.Feedback);
                    Child = child;
                }

                public void Destroy(DestroyContext ctx)
                {
                    ctx.Set.Destroy(Child);
                }

                public void HandleMessage(MessageContext ctx, in int msg)
                {
                    if (ctx.Port == SimulationPorts.PublicInput)
                        ctx.EmitMessage(SimulationPorts.PrivateOutput, msg);
                    else if (ctx.Port == SimulationPorts.PrivateInput)
                        ctx.EmitMessage(SimulationPorts.PublicOutput, msg);
                }
            }

            struct Data : IKernelData {}

            [BurstCompile(CompileSynchronously = true)]
            struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                public void Execute(RenderContext ctx, in Data data, ref KernelDefs ports)
                {
                    ctx.Resolve(ref ports.PrivateOutput) = ctx.Resolve(ports.PublicInput);
                    ctx.Resolve(ref ports.PublicOutput) = ctx.Resolve(ports.PrivateInput);
                }
            }
        }

        [Test]
        public void UberCanCommunicate_WithChild_ThroughPartiallyNonPublicPorts()
        {
            const int k_Loops = 10;
            using (var set = new NodeSet())
            {
                var uber = set.Create<UberWithPartialNonPublicComms>();
                var result = set.Create<PassthroughTest<int>>();

                set.Connect(uber, UberWithPartialNonPublicComms.SimulationPorts.PublicOutput, result, PassthroughTest<int>.SimulationPorts.Input);
                var gv = set.CreateGraphValue(uber, UberWithPartialNonPublicComms.KernelPorts.PublicOutput);

                for (int i = 0; i < k_Loops; ++i)
                {
                    set.SendMessage(uber, UberWithPartialNonPublicComms.SimulationPorts.PublicInput, i);
                    set.SetData(uber, UberWithPartialNonPublicComms.KernelPorts.PublicInput, -i);
                    set.Update();
                    set.SendTest(result, (PassthroughTest<int>.NodeData data) => Assert.AreEqual(i, data.LastReceivedMsg));
                    var value = set.GetValueBlocking(gv);
                    Assert.AreEqual(-i, i == 0 ? 0 : value - 1);
                }

                set.ReleaseGraphValue(gv);
                set.Destroy(uber, result);
            }
        }

        public class UberWithNonPublicDataComms
            : SimulationKernelNodeDefinition<UberWithNonPublicDataComms.SimPorts, UberWithNonPublicDataComms.KernelDefs>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
                public MessageInput<UberWithNonPublicDataComms, int> PublicInput;
                public MessageOutput<UberWithNonPublicDataComms, int> PublicOutput;
            }

            public struct KernelDefs : IKernelPortDefinition
            {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                internal DataInput<UberWithNonPublicDataComms, int> PrivateInput;
                internal DataOutput<UberWithNonPublicDataComms, int> PrivateOutput;
#pragma warning restore 649
            }

            struct Node : INodeData, IInit, IDestroy, IUpdate, IMsgHandler<int>
            {
                NodeHandle Child;
                public GraphValue<int> Value;

                public void Init(InitContext ctx)
                {
                    ctx.RegisterForUpdate();

                    var self = ctx.Set.CastHandle<UberWithNonPublicDataComms>(ctx.Handle);
                    var child = ctx.Set.Create<PassthroughTest<int>>();
                    ctx.Set.Connect(self, KernelPorts.PrivateOutput, child, PassthroughTest<int>.KernelPorts.Input);
                    ctx.Set.Connect(child, PassthroughTest<int>.KernelPorts.Output, self, KernelPorts.PrivateInput, NodeSet.ConnectionType.Feedback);
                    Value = ctx.Set.CreateGraphValue(child, PassthroughTest<int>.KernelPorts.Output);
                    Child = child;
                }

                public void Destroy(DestroyContext ctx)
                {
                    ctx.Set.ReleaseGraphValue(Value);
                    ctx.Set.Destroy(Child);
                }

                public void HandleMessage(MessageContext ctx, in int msg)
                {
                    ctx.UpdateKernelData(new Data {Content = msg});
                }

                public void Update(UpdateContext ctx)
                {
                    ctx.EmitMessage(SimulationPorts.PublicOutput, ctx.Set.GetValueBlocking(Value));
                }
            }

            struct Data : IKernelData
            {
                public int Content;
            }

            [BurstCompile(CompileSynchronously = true)]
            struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                public void Execute(RenderContext ctx, in Data data, ref KernelDefs ports)
                {
                    ctx.Resolve(ref ports.PrivateOutput) = ctx.Resolve(ports.PrivateInput) + data.Content;
                }
            }
        }

        [Test]
        public void UberCanCommunicate_WithChild_ThroughNonPublicDataPorts()
        {
            const int k_Loops = 10;
            using (var set = new NodeSet())
            {
                var uber = set.Create<UberWithNonPublicDataComms>();
                var result = set.Create<PassthroughTest<int>>();

                set.Connect(uber, UberWithNonPublicDataComms.SimulationPorts.PublicOutput, result, PassthroughTest<int>.SimulationPorts.Input);

                int lastValue = 1;
                for (int i = 0; i < k_Loops; ++i)
                {
                    set.SendMessage(uber, UberWithNonPublicDataComms.SimulationPorts.PublicInput, i);
                    set.Update();
                    var expectedValue = i + lastValue - 1;
                    set.SendTest(result, (PassthroughTest<int>.NodeData data) =>
                        Assert.AreEqual(expectedValue, data.LastReceivedMsg));
                    lastValue = expectedValue;
                }

                set.Destroy(uber, result);
            }
        }

        public class UberWithNonPublicMsgComms
            : SimulationKernelNodeDefinition<UberWithNonPublicMsgComms.SimPorts, UberWithNonPublicMsgComms.KernelDefs>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                internal MessageInput<UberWithNonPublicMsgComms, int> PrivateInput;
                internal MessageOutput<UberWithNonPublicMsgComms, int> PrivateOutput;
#pragma warning restore 649
            }

            public struct KernelDefs : IKernelPortDefinition
            {
                public DataInput<UberWithNonPublicMsgComms, int> PublicInput;
                public DataOutput<UberWithNonPublicMsgComms, int> PublicOutput;
            }

            struct Node : INodeData, IInit, IDestroy, IUpdate, IMsgHandler<int>
            {
                public NodeHandle Child;
                public GraphValue<int> Value;

                public void HandleMessage(MessageContext ctx, in int msg)
                {
                    ctx.UpdateKernelData(new Data {Content = msg});
                }

                public void Update(UpdateContext ctx)
                {
                    ctx.EmitMessage(SimulationPorts.PrivateOutput, ctx.Set.GetValueBlocking(Value));
                }

                public void Init(InitContext ctx)
                {
                    ctx.RegisterForUpdate();
                    var self = ctx.Set.CastHandle<UberWithNonPublicMsgComms>(ctx.Handle);
                    var child = ctx.Set.Create<PassthroughTest<int>>();
                    ctx.Set.Connect(self, SimulationPorts.PrivateOutput, child, PassthroughTest<int>.SimulationPorts.Input);
                    ctx.Set.Connect(child, PassthroughTest<int>.SimulationPorts.Output, self, SimulationPorts.PrivateInput);
                    Value = ctx.Set.CreateGraphValue(self, KernelPorts.PublicOutput);
                    Child = child;
                }

                public void Destroy(DestroyContext ctx)
                {
                    ctx.Set.ReleaseGraphValue(Value);
                    ctx.Set.Destroy(Child);
                }
            }

            struct Data : IKernelData
            {
                public int Content;
            }

            [BurstCompile(CompileSynchronously = true)]
            struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                public void Execute(RenderContext ctx, in Data data, ref KernelDefs ports)
                {
                    ctx.Resolve(ref ports.PublicOutput) = ctx.Resolve(ports.PublicInput) + data.Content;
                }
            }
        }

        [Test]
        public void UberCanCommunicate_WithChild_ThroughNonPublicMsgPorts()
        {
            const int k_Loops = 10;
            using (var set = new NodeSet())
            {
                var uber = set.Create<UberWithNonPublicMsgComms>();

                var gv = set.CreateGraphValue(uber, UberWithNonPublicMsgComms.KernelPorts.PublicOutput);

                int lastValue = 0;
                for (int i = 0; i < k_Loops; ++i)
                {
                    set.SetData(uber, UberWithNonPublicMsgComms.KernelPorts.PublicInput, i);
                    set.Update();
                    var value = set.GetValueBlocking(gv);
                    Assert.AreEqual(i + lastValue, value);
                    lastValue = value;
                }

                set.ReleaseGraphValue(gv);
                set.Destroy(uber);
            }
        }

        public class Node_ThatSetsDefaultValue_OnInputPort
            : SimulationKernelNodeDefinition<Node_ThatSetsDefaultValue_OnInputPort.SimPorts, Node_ThatSetsDefaultValue_OnInputPort.KernelDefs>
        {
            public const int kValue = 33;

            public struct SimPorts : ISimulationPortDefinition {}

            public struct KernelDefs : IKernelPortDefinition
            {
                public DataInput<Node_ThatSetsDefaultValue_OnInputPort, int> Input;
                public DataOutput<Node_ThatSetsDefaultValue_OnInputPort, int> Output;
            }

            struct Node : INodeData, IInit
            {
                public void Init(InitContext ctx)
                    => ctx.SetInitialPortValue(KernelPorts.Input, kValue);
            }

            struct Data : IKernelData { }

            [BurstCompile(CompileSynchronously = true)]
            struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                public void Execute(RenderContext ctx, in Data data, ref KernelDefs ports)
                {
                    ctx.Resolve(ref ports.Output) = ctx.Resolve(ports.Input);
                }
            }
        }

        [Test]
        public void CanUseSetInitialPortValue_OnInitContext()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<Node_ThatSetsDefaultValue_OnInputPort>();
                var gv = set.CreateGraphValue(node, Node_ThatSetsDefaultValue_OnInputPort.KernelPorts.Output);

                set.Update();

                Assert.AreEqual(Node_ThatSetsDefaultValue_OnInputPort.kValue, set.GetValueBlocking(gv));

                set.ReleaseGraphValue(gv);
                set.Destroy(node);
            }
        }
    }
}
