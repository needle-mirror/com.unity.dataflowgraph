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

        public struct Node : INodeData { }

        public struct Data : IKernelData { }

        class EmptyNode : NodeDefinition<EmptyPorts> { }

        public class NodeWithOneMessageIO : NodeDefinition<Node, NodeWithOneMessageIO.SimPorts>, IMsgHandler<int>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                public MessageInput<NodeWithOneMessageIO, int> Input;
                public MessageOutput<NodeWithOneMessageIO, int> Output;
#pragma warning restore 649
            }

            public void HandleMessage(in MessageContext ctx, in int msg) { }
        }

        public class NodeWithMessageArrayInput : NodeDefinition<Node, NodeWithMessageArrayInput.SimPorts>, IMsgHandler<int>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                public PortArray<MessageInput<NodeWithMessageArrayInput, int>> Input;
#pragma warning restore 649
            }

            public void HandleMessage(in MessageContext ctx, in int msg) { }
        }

        class NodeWithManyInputs
            : NodeDefinition<Node, NodeWithManyInputs.SimPorts, Data, NodeWithManyInputs.KernelDefs, NodeWithManyInputs.Kernel>
            , TestDSL
            , IMsgHandler<int>
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

            [BurstCompile(CompileSynchronously = true)]
            public struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                public void Execute(RenderContext ctx, Data data, ref KernelDefs ports) { }
            }
            public void HandleMessage(in MessageContext ctx, in int msg) { }
        }

        class NodeWithManyOutputs : NodeDefinition<Node, NodeWithManyOutputs.SimPorts, Data, NodeWithManyOutputs.KernelDefs, NodeWithManyOutputs.Kernel>, TestDSL
        {
            public struct SimPorts : ISimulationPortDefinition
            {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                public MessageOutput<NodeWithManyOutputs, int> I0, I1, I2;
                public DSLOutput<NodeWithManyOutputs, DSL, TestDSL> D3, D4, D5;
#pragma warning restore 649
            }

            public struct KernelDefs : IKernelPortDefinition
            {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                public DataOutput<NodeWithManyOutputs, int> K6, K7, K8;
#pragma warning restore 649
            }

            [BurstCompile(CompileSynchronously = true)]
            public struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                public void Execute(RenderContext ctx, Data data, ref KernelDefs ports) { }
            }
        }

        class NodeWithMixedInputs
            : NodeDefinition<Node, NodeWithMixedInputs.SimPorts>
            , TestDSL
            , IMsgHandler<int>
        {
            public void HandleMessage(in MessageContext ctx, in int msg) { }

            public struct SimPorts : ISimulationPortDefinition
            {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                public MessageInput<NodeWithMixedInputs, int> I0;
                public DSLInput<NodeWithMixedInputs, DSL, TestDSL> D0;
                public MessageInput<NodeWithMixedInputs, int> I1;
                public DSLInput<NodeWithMixedInputs, DSL, TestDSL> D1;
#pragma warning restore 649
            }


        }

        class NodeWithOneDSLIO : NodeDefinition<Node, NodeWithOneDSLIO.SimPorts>, TestDSL
        {
            public struct SimPorts : ISimulationPortDefinition
            {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                public DSLInput<NodeWithOneDSLIO, DSL, TestDSL> Input;
                public DSLOutput<NodeWithOneDSLIO, DSL, TestDSL> Output;
#pragma warning restore 649
            }
        }

        public class NodeWithNonStaticPorts_OutsideOfPortDefinition : NodeDefinition<EmptyPorts>, IMsgHandler<int>
        {
            public MessageInput<NodeWithNonStaticPorts_OutsideOfPortDefinition, int> Input;
            public MessageOutput<NodeWithNonStaticPorts_OutsideOfPortDefinition, int> Output;

            public void HandleMessage(in MessageContext ctx, in int msg) { }
        }

        public class NodeWithStaticPorts_OutsideOfPortDefinition : NodeDefinition<EmptyPorts>, IMsgHandler<int>
        {
            public static MessageInput<NodeWithStaticPorts_OutsideOfPortDefinition, int> Input;
            public static MessageOutput<NodeWithStaticPorts_OutsideOfPortDefinition, int> Output;

            public void HandleMessage(in MessageContext ctx, in int msg) { }
        }

        public class NodeWithOneDataIO : NodeDefinition<Node, Data, NodeWithOneDataIO.KernelDefs, NodeWithOneDataIO.Kernel>
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

            [BurstCompile(CompileSynchronously = true)]
            public struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                public void Execute(RenderContext ctx, Data data, ref KernelDefs ports) { }
            }
        }

        class NodeWithDataArrayInput : NodeDefinition<Node, Data, NodeWithDataArrayInput.KernelDefs, NodeWithDataArrayInput.Kernel>
        {
            public struct KernelDefs : IKernelPortDefinition
            {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                public PortArray<DataInput<NodeWithDataArrayInput, int>> Input;
#pragma warning restore 649
            }

            [BurstCompile(CompileSynchronously = true)]
            public struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                public void Execute(RenderContext ctx, Data data, ref KernelDefs ports) { }
            }
        }

        [InvalidTestNodeDefinition]
        class NodeWithMessageAndDSLPortsInIKernelPortDefinition
            : NodeDefinition<Node, Data, NodeWithMessageAndDSLPortsInIKernelPortDefinition.KernelDefs, NodeWithMessageAndDSLPortsInIKernelPortDefinition.Kernel>
                , TestDSL
        {
            public struct KernelDefs : IKernelPortDefinition
            {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                public DSLInput<NodeWithMessageAndDSLPortsInIKernelPortDefinition, DSL, TestDSL> Input;
                public MessageOutput<NodeWithOneMessageIO, int> Output;
#pragma warning restore 649
            }

            [BurstCompile(CompileSynchronously = true)]
            public struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                public void Execute(RenderContext ctx, Data data, ref KernelDefs ports) { }
            }
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
        public void NodeWithMessageArrayInput_IsCorrectlyInsertedInto_PortDescription()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<NodeWithMessageArrayInput>();
                var func = set.GetDefinition(node);

                Assert.AreEqual(1, func.GetPortDescription(node).Inputs.Count);
                Assert.AreEqual(0, func.GetPortDescription(node).Outputs.Count);

                Assert.IsTrue(func.GetPortDescription(node).Inputs.All(p => p.Category == PortDescription.Category.Message));
                Assert.IsTrue(func.GetPortDescription(node).Outputs.All(p => p.Category == PortDescription.Category.Message));

                Assert.AreEqual(func.GetPortDescription(node).Inputs.Count, 1);
                Assert.IsTrue(func.GetPortDescription(node).Inputs[0].IsPortArray);

                set.Destroy(node);
            }
        }

        [Test]
        public void NodeWithDataArrayInput_IsCorrectlyInsertedInto_PortDescription()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<NodeWithDataArrayInput>();
                var func = set.GetDefinition(node);

                Assert.AreEqual(1, func.GetPortDescription(node).Inputs.Count);
                Assert.AreEqual(0, func.GetPortDescription(node).Outputs.Count);

                Assert.IsTrue(func.GetPortDescription(node).Inputs.All(p => p.Category == PortDescription.Category.Data));
                Assert.IsTrue(func.GetPortDescription(node).Outputs.All(p => p.Category == PortDescription.Category.Data));

                Assert.AreEqual(func.GetPortDescription(node).Inputs.Count, 1);
                Assert.IsTrue(func.GetPortDescription(node).Inputs[0].IsPortArray);

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

        public class ComplexKernelAggregateNode : NodeDefinition<Node, Data, ComplexKernelAggregateNode.KernelDefs, ComplexKernelAggregateNode.Kernel>
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

            [BurstCompile(CompileSynchronously = true)]
            public struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                public void Execute(RenderContext ctx, Data data, ref KernelDefs ports) { }
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

        [InvalidTestNodeDefinition]
        public class NodeWithDataPortsInSimulationPortDefinition
            : NodeDefinition<NodeWithDataPortsInSimulationPortDefinition.Node, NodeWithDataPortsInSimulationPortDefinition.SimPorts>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
                public DataInput<NodeWithDataPortsInSimulationPortDefinition, float> Input;
                public DataOutput<NodeWithDataPortsInSimulationPortDefinition, float> Output;
            }

            public struct Node : INodeData
            {
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
                Assert.AreEqual(9, outputNodePorts.Outputs.Count);

                Assert.AreEqual(NodeWithManyInputs.SimulationPorts.I0.Port, (InputPortID)inputNodePorts.Inputs[0]);
                Assert.AreEqual(NodeWithManyInputs.SimulationPorts.I1.Port, (InputPortID)inputNodePorts.Inputs[1]);
                Assert.AreEqual(NodeWithManyInputs.SimulationPorts.I2.Port, (InputPortID)inputNodePorts.Inputs[2]);
                Assert.AreEqual(NodeWithManyInputs.SimulationPorts.IArray3.Port, (InputPortID)inputNodePorts.Inputs[3]);
                Assert.AreEqual(NodeWithManyInputs.SimulationPorts.D4.Port, (InputPortID)inputNodePorts.Inputs[4]);
                Assert.AreEqual(NodeWithManyInputs.SimulationPorts.D5.Port, (InputPortID)inputNodePorts.Inputs[5]);
                Assert.AreEqual(NodeWithManyInputs.SimulationPorts.D6.Port, (InputPortID)inputNodePorts.Inputs[6]);
                Assert.AreEqual(NodeWithManyInputs.KernelPorts.K7.Port, (InputPortID)inputNodePorts.Inputs[7]);
                Assert.AreEqual(NodeWithManyInputs.KernelPorts.K8.Port, (InputPortID)inputNodePorts.Inputs[8]);
                Assert.AreEqual(NodeWithManyInputs.KernelPorts.K9.Port, (InputPortID)inputNodePorts.Inputs[9]);
                Assert.AreEqual(NodeWithManyInputs.KernelPorts.KArray10.Port, (InputPortID)inputNodePorts.Inputs[10]);

                Assert.AreEqual(NodeWithManyOutputs.SimulationPorts.I0.Port, (OutputPortID)outputNodePorts.Outputs[0]);
                Assert.AreEqual(NodeWithManyOutputs.SimulationPorts.I1.Port, (OutputPortID)outputNodePorts.Outputs[1]);
                Assert.AreEqual(NodeWithManyOutputs.SimulationPorts.I2.Port, (OutputPortID)outputNodePorts.Outputs[2]);
                Assert.AreEqual(NodeWithManyOutputs.SimulationPorts.D3.Port, (OutputPortID)outputNodePorts.Outputs[3]);
                Assert.AreEqual(NodeWithManyOutputs.SimulationPorts.D4.Port, (OutputPortID)outputNodePorts.Outputs[4]);
                Assert.AreEqual(NodeWithManyOutputs.SimulationPorts.D5.Port, (OutputPortID)outputNodePorts.Outputs[5]);
                Assert.AreEqual(NodeWithManyOutputs.KernelPorts.K6.Port, (OutputPortID)outputNodePorts.Outputs[6]);
                Assert.AreEqual(NodeWithManyOutputs.KernelPorts.K7.Port, (OutputPortID)outputNodePorts.Outputs[7]);
                Assert.AreEqual(NodeWithManyOutputs.KernelPorts.K8.Port, (OutputPortID)outputNodePorts.Outputs[8]);

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

                set.Destroy(inputNode);
            }
        }

        [Test]
        public void SimulationPorts_AreNotAllNullAssignedPortIDs()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<NodeWithMixedInputs>();
                var ports = NodeWithMixedInputs.SimulationPorts;
                int portIDCount = ports.I0.Port.Port + ports.D0.Port.Port + ports.I1.Port.Port + ports.D1.Port.Port;

                Assert.GreaterOrEqual(portIDCount, 6); // minimum required sum for four unique values

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

                int portIDCount = 0;

                foreach (var inputPort in inputNodePorts.Inputs)
                    portIDCount += inputPort.m_Port;

                Assert.GreaterOrEqual(portIDCount, 6); // minimum required sum for four unique values

                set.Destroy(node);
            }
        }

        [Test]
        public void ExpectPortIDs_AreAssignedIndices_BasedOnPortDeclarationOrder([ValueSource(typeof(TestUtilities), nameof(TestUtilities.FindValidTestNodes))] Type nodeType)
        {
            // This assumes that PortDescriptions respect port declaration order (<see cref="PortDeclarations_RespectsDeclarationOrder"/>)
            using (var set = new NodeSet())
            {
                var ports = set.GetStaticPortDescriptionFromType(nodeType);

                ushort portNumber = 0;
                foreach (var input in ports.Inputs)
                    Assert.AreEqual(portNumber++, ((InputPortID)input).Port);

                portNumber = 0;
                foreach (var output in ports.Outputs)
                    Assert.AreEqual(portNumber++, ((OutputPortID)output).Port);
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
                Assert.AreEqual(4, func.GetPortDescription(node).Outputs.Count);

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
            : NodeDefinition<Node, NodeWithParametricPortTypeIncludingDSLs<T>.SimPorts, Data, NodeWithParametricPortTypeIncludingDSLs<T>.KernelDefs, NodeWithParametricPortTypeIncludingDSLs<T>.Kernel>
            , TestDSL
            , IMsgHandler<T>
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

            [BurstCompile(CompileSynchronously = true)]
            public struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                public void Execute(RenderContext ctx, Data data, ref KernelDefs ports) { }
            }

            public void HandleMessage(in MessageContext ctx, in T msg) { }
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

                Assert.AreNotEqual(NodeWithAllTypesOfPorts.KernelPorts.InputScalar, new DataInput<NodeWithAllTypesOfPorts, int>());
                Assert.AreNotEqual(NodeWithAllTypesOfPorts.KernelPorts.InputArrayScalar, new PortArray<DataInput<NodeWithAllTypesOfPorts, int>>());
                Assert.AreNotEqual(NodeWithAllTypesOfPorts.KernelPorts.OutputScalar, new DataOutput<NodeWithAllTypesOfPorts, int>());

                Assert.AreNotEqual(NodeWithAllTypesOfPorts.KernelPorts.InputBuffer, new DataInput<NodeWithAllTypesOfPorts, Buffer<int>>());
                Assert.AreNotEqual(NodeWithAllTypesOfPorts.KernelPorts.InputArrayBuffer, new PortArray<DataInput<NodeWithAllTypesOfPorts, Buffer<int>>>());
                Assert.AreNotEqual(NodeWithAllTypesOfPorts.KernelPorts.OutputBuffer, new DataOutput<NodeWithAllTypesOfPorts, Buffer<int>>());

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

                Assert.AreEqual(NodeWithAllTypesOfPorts.SimulationPorts.MessageArrayIn.Port, (InputPortID)NodeWithAllTypesOfPorts.SimulationPorts.MessageArrayIn);
                Assert.AreEqual((InputPortID)desc.Inputs[1], (InputPortID)NodeWithAllTypesOfPorts.SimulationPorts.MessageArrayIn);

                Assert.AreEqual(NodeWithAllTypesOfPorts.SimulationPorts.DSLIn.Port, (InputPortID)NodeWithAllTypesOfPorts.SimulationPorts.DSLIn);
                Assert.AreEqual((InputPortID)desc.Inputs[2], (InputPortID)NodeWithAllTypesOfPorts.SimulationPorts.DSLIn);

                Assert.AreEqual(NodeWithAllTypesOfPorts.KernelPorts.InputBuffer.Port, (InputPortID)NodeWithAllTypesOfPorts.KernelPorts.InputBuffer);
                Assert.AreEqual((InputPortID)desc.Inputs[3], (InputPortID)NodeWithAllTypesOfPorts.KernelPorts.InputBuffer);

                Assert.AreEqual(NodeWithAllTypesOfPorts.KernelPorts.InputArrayBuffer.Port, (InputPortID)NodeWithAllTypesOfPorts.KernelPorts.InputArrayBuffer);
                Assert.AreEqual((InputPortID)desc.Inputs[4], (InputPortID)NodeWithAllTypesOfPorts.KernelPorts.InputArrayBuffer);

                Assert.AreEqual(NodeWithAllTypesOfPorts.KernelPorts.InputScalar.Port, (InputPortID)NodeWithAllTypesOfPorts.KernelPorts.InputScalar);
                Assert.AreEqual((InputPortID)desc.Inputs[5], (InputPortID)NodeWithAllTypesOfPorts.KernelPorts.InputScalar);

                Assert.AreEqual(NodeWithAllTypesOfPorts.KernelPorts.InputArrayScalar.Port, (InputPortID)NodeWithAllTypesOfPorts.KernelPorts.InputArrayScalar);
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

                Assert.AreEqual(NodeWithAllTypesOfPorts.SimulationPorts.DSLOut.Port, (OutputPortID)NodeWithAllTypesOfPorts.SimulationPorts.DSLOut);
                Assert.AreEqual((OutputPortID)desc.Outputs[1], (OutputPortID)NodeWithAllTypesOfPorts.SimulationPorts.DSLOut);

                Assert.AreEqual(NodeWithAllTypesOfPorts.KernelPorts.OutputBuffer.Port, (OutputPortID)NodeWithAllTypesOfPorts.KernelPorts.OutputBuffer);
                Assert.AreEqual((OutputPortID)desc.Outputs[2], (OutputPortID)NodeWithAllTypesOfPorts.KernelPorts.OutputBuffer);

                Assert.AreEqual(NodeWithAllTypesOfPorts.KernelPorts.OutputScalar.Port, (OutputPortID)NodeWithAllTypesOfPorts.KernelPorts.OutputScalar);
                Assert.AreEqual((OutputPortID)desc.Outputs[3], (OutputPortID)NodeWithAllTypesOfPorts.KernelPorts.OutputScalar);

                set.Destroy(node);
            }
        }
    }
}
