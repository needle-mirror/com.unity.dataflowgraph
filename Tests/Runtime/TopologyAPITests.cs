using System;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.DataFlowGraph.Detail;
using Unity.Jobs;

namespace Unity.DataFlowGraph.Tests
{
    class TopologyAPITests
    {
        class InOutTestNode : SimulationNodeDefinition<InOutTestNode.SimPorts>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                public MessageInput<InOutTestNode, Message> Input;
                public MessageOutput<InOutTestNode, Message> Output;
#pragma warning restore 649
            }

            struct Node : INodeData, IMsgHandler<Message>
            {
                public int Contents;

                public void HandleMessage(MessageContext ctx, in Message msg)
                {
                    Assert.That(ctx.Port == SimulationPorts.Input);
                    Contents += msg.Contents;
                    ctx.EmitMessage(SimulationPorts.Output, new Message(Contents + 1));
                }
            }
        }

        class TwoInOutTestNode : SimulationNodeDefinition<TwoInOutTestNode.SimPorts>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                public MessageInput<TwoInOutTestNode, Message> Input1, Input2;
                public MessageOutput<TwoInOutTestNode, Message> Output1, Output2;
#pragma warning restore 649
            }

            struct Node : INodeData, IMsgHandler<Message>
            {
                public void HandleMessage(MessageContext ctx, in Message msg) { }
            }
        }

        [Test]
        public void CanConnectTwoNodes_AndKeepTopologyIntegrity()
        {
            using (var set = new NodeSet())
            {
                NodeHandle<InOutTestNode>
                    a = set.Create<InOutTestNode>(),
                    b = set.Create<InOutTestNode>();

                set.Connect(b.Tie(InOutTestNode.SimulationPorts.Output), a.Tie(InOutTestNode.SimulationPorts.Input));

                set.Destroy(a, b);
            }
        }

        [Test]
        public void CannotCreate_MultiEdgeGraph()
        {
            using (var set = new NodeSet())
            {
                NodeHandle<InOutTestNode>
                    a = set.Create<InOutTestNode>(),
                    b = set.Create<InOutTestNode>();

                set.Connect(a.Tie(InOutTestNode.SimulationPorts.Output), b.Tie(InOutTestNode.SimulationPorts.Input));
                Assert.Throws<ArgumentException>(() => set.Connect(a.Tie(InOutTestNode.SimulationPorts.Output), b.Tie(InOutTestNode.SimulationPorts.Input)));

                set.Destroy(a, b);
            }
        }

        [Test]
        public void CannotMakeTheSameConnectionTwice()
        {
            using (var set = new NodeSet())
            {
                NodeHandle<InOutTestNode>
                    a = set.Create<InOutTestNode>(),
                    b = set.Create<InOutTestNode>();

                set.Connect(a.Tie(InOutTestNode.SimulationPorts.Output), b.Tie(InOutTestNode.SimulationPorts.Input));
                Assert.Throws<ArgumentException>(() => set.Connect(a.Tie(InOutTestNode.SimulationPorts.Output), b.Tie(InOutTestNode.SimulationPorts.Input)));
                set.Disconnect(a.Tie(InOutTestNode.SimulationPorts.Output), b.Tie(InOutTestNode.SimulationPorts.Input));
                set.Connect(a.Tie(InOutTestNode.SimulationPorts.Output), b.Tie(InOutTestNode.SimulationPorts.Input));

                set.Destroy(a, b);
            }
        }

        [Test]
        public void CannotMakeTwoDataConnectionsToTheSamePort()
        {
            using (var set = new NodeSet())
            {
                NodeHandle<KernelAdderNode>
                    a = set.Create<KernelAdderNode>(),
                    b = set.Create<KernelAdderNode>(),
                    c = set.Create<KernelAdderNode>();

                set.Connect(a.Tie(KernelAdderNode.KernelPorts.Output), c.Tie(KernelAdderNode.KernelPorts.Input));
                Assert.Throws<ArgumentException>(() => set.Connect(b.Tie(KernelAdderNode.KernelPorts.Output), c.Tie(KernelAdderNode.KernelPorts.Input)));
                set.Disconnect(a.Tie(KernelAdderNode.KernelPorts.Output), c.Tie(KernelAdderNode.KernelPorts.Input));
                set.Connect(a.Tie(KernelAdderNode.KernelPorts.Output), c.Tie(KernelAdderNode.KernelPorts.Input));

                set.Destroy(a, b, c);
            }
        }

        [Test]
        public void ConnectThrows_OnDefaultConstructedHandles()
        {
            using (var set = new NodeSet())
            {
                NodeHandle<InOutTestNode> a = set.Create<InOutTestNode>(), b = set.Create<InOutTestNode>(), invalid = default;

                var e = Assert.Throws<ArgumentException>(() => set.Connect(a, (OutputPortID)InOutTestNode.SimulationPorts.Output, new NodeHandle(), (InputPortID)InOutTestNode.SimulationPorts.Input));
                StringAssert.Contains(Collections.VersionedList<InternalNodeData>.ValidationFail_InvalidMessage, e.Message);

                e = Assert.Throws<ArgumentException>(() => set.Connect(new NodeHandle(), (OutputPortID)InOutTestNode.SimulationPorts.Output, b, (InputPortID)InOutTestNode.SimulationPorts.Input));
                StringAssert.Contains(Collections.VersionedList<InternalNodeData>.ValidationFail_InvalidMessage, e.Message);

                e = Assert.Throws<ArgumentException>(() => set.Connect(a.Tie(InOutTestNode.SimulationPorts.Output), invalid.Tie(InOutTestNode.SimulationPorts.Input)));
                StringAssert.Contains(Collections.VersionedList<InternalNodeData>.ValidationFail_InvalidMessage, e.Message);

                e = Assert.Throws<ArgumentException>(() => set.Connect(invalid.Tie(InOutTestNode.SimulationPorts.Output), b.Tie(InOutTestNode.SimulationPorts.Input)));
                StringAssert.Contains(Collections.VersionedList<InternalNodeData>.ValidationFail_InvalidMessage, e.Message);

                set.Destroy(a, b);
            }
        }

        [Test]
        public void ConnectThrows_OnDefaultConstructedEndpoints()
        {
            using (var set = new NodeSet())
            {
                NodeHandle<InOutTestNode> a = set.Create<InOutTestNode>(), b = set.Create<InOutTestNode>();

                var e = Assert.Throws<ArgumentException>(() => set.Connect(a.Tie(InOutTestNode.SimulationPorts.Output), new MessageInputEndpoint<Message>()));
                StringAssert.Contains(Collections.VersionedList<InternalNodeData>.ValidationFail_InvalidMessage, e.Message);

                e = Assert.Throws<ArgumentException>(() => set.Connect(new MessageOutputEndpoint<Message>(), b.Tie(InOutTestNode.SimulationPorts.Input)));
                StringAssert.Contains(Collections.VersionedList<InternalNodeData>.ValidationFail_InvalidMessage, e.Message);

                set.Destroy(a, b);
            }
        }

        [Test]
        public void ConnectThrows_OnDefaultConstructedPortIDs()
        {
            using (var set = new NodeSet())
            {
                NodeHandle<InOutTestNode> a = set.Create<InOutTestNode>(), b = set.Create<InOutTestNode>();

                var e = Assert.Throws<ArgumentException>(() => set.Connect(a, new OutputPortID(), b, (InputPortID)InOutTestNode.SimulationPorts.Input));
                StringAssert.Contains("Invalid output port", e.Message);

                e = Assert.Throws<ArgumentException>(() => set.Connect(a, (OutputPortID)InOutTestNode.SimulationPorts.Output, b, new InputPortID()));
                StringAssert.Contains("Invalid input port", e.Message);

                e = Assert.Throws<ArgumentException>(() => set.Connect(a.Tie(new MessageOutput<InOutTestNode, Message>()), b.Tie(InOutTestNode.SimulationPorts.Input)));
                StringAssert.Contains("Invalid output port", e.Message);

                e = Assert.Throws<ArgumentException>(() => set.Connect(a.Tie(InOutTestNode.SimulationPorts.Output), b.Tie(new MessageInput<InOutTestNode, Message>())));
                StringAssert.Contains("Invalid input port", e.Message);

                set.Destroy(a, b);
            }
        }

        [Test]
        public void DisconnectThrows_OnDefaultConstructedHandles()
        {
            using (var set = new NodeSet())
            {
                NodeHandle<InOutTestNode> node = set.Create<InOutTestNode>(), invalid = default;

                var e = Assert.Throws<ArgumentException>(() => set.Disconnect(new NodeHandle(), (OutputPortID)InOutTestNode.SimulationPorts.Output, node, (InputPortID)InOutTestNode.SimulationPorts.Input));
                StringAssert.Contains(Collections.VersionedList<InternalNodeData>.ValidationFail_InvalidMessage, e.Message);

                e = Assert.Throws<ArgumentException>(() => set.Disconnect(node, (OutputPortID)InOutTestNode.SimulationPorts.Output, new NodeHandle(), (InputPortID)InOutTestNode.SimulationPorts.Input));
                StringAssert.Contains(Collections.VersionedList<InternalNodeData>.ValidationFail_InvalidMessage, e.Message);

                e = Assert.Throws<ArgumentException>(() => set.Disconnect(node.Tie(InOutTestNode.SimulationPorts.Output), invalid.Tie(InOutTestNode.SimulationPorts.Input)));
                StringAssert.Contains(Collections.VersionedList<InternalNodeData>.ValidationFail_InvalidMessage, e.Message);

                e = Assert.Throws<ArgumentException>(() => set.Disconnect(invalid.Tie(InOutTestNode.SimulationPorts.Output), node.Tie(InOutTestNode.SimulationPorts.Input)));
                StringAssert.Contains(Collections.VersionedList<InternalNodeData>.ValidationFail_InvalidMessage, e.Message);

                set.Destroy(node);
            }
        }

        [Test]
        public void DisconnectThrows_OnDefaultConstructedEndpoints()
        {
            using (var set = new NodeSet())
            {
                NodeHandle<InOutTestNode> node = set.Create<InOutTestNode>();

                var e = Assert.Throws<ArgumentException>(() => set.Disconnect(new MessageOutputEndpoint<Message>(), node.Tie(InOutTestNode.SimulationPorts.Input)));
                StringAssert.Contains(Collections.VersionedList<InternalNodeData>.ValidationFail_InvalidMessage, e.Message);

                e = Assert.Throws<ArgumentException>(() => set.Disconnect(node.Tie(InOutTestNode.SimulationPorts.Output), new MessageInputEndpoint<Message>()));
                StringAssert.Contains(Collections.VersionedList<InternalNodeData>.ValidationFail_InvalidMessage, e.Message);

                set.Destroy(node);
            }
        }

        [Test]
        public void DisonnectThrows_OnDefaultConstructedPortIDs()
        {
            using (var set = new NodeSet())
            {
                NodeHandle<InOutTestNode> a = set.Create<InOutTestNode>(), b = set.Create<InOutTestNode>();

                var e = Assert.Throws<ArgumentException>(() => set.Disconnect(a, new OutputPortID(), b, (InputPortID)InOutTestNode.SimulationPorts.Input));
                StringAssert.Contains("Invalid output port", e.Message);

                e = Assert.Throws<ArgumentException>(() => set.Disconnect(a, (OutputPortID)InOutTestNode.SimulationPorts.Output, b, new InputPortID()));
                StringAssert.Contains("Invalid input port", e.Message);

                e = Assert.Throws<ArgumentException>(() => set.Connect(a.Tie(new MessageOutput<InOutTestNode, Message>()), b.Tie(InOutTestNode.SimulationPorts.Input)));
                StringAssert.Contains("Invalid output port", e.Message);

                e = Assert.Throws<ArgumentException>(() => set.Connect(a.Tie(InOutTestNode.SimulationPorts.Output), b.Tie(new MessageInput<InOutTestNode, Message>())));
                StringAssert.Contains("Invalid input port", e.Message);

                set.Destroy(a, b);
            }
        }

        [Test]
        public void ConnectingOutOfPortIndicesRange_ThrowsException()
        {
            using (var set = new NodeSet())
            {
                NodeHandle<InOutTestNode>
                    a = set.Create<InOutTestNode>(),
                    b = set.Create<InOutTestNode>();

                set.Connect(a, InOutTestNode.SimulationPorts.Output, b, InOutTestNode.SimulationPorts.Input);

                var otherNodePortDef = set.GetStaticPortDescription<TwoInOutTestNode>();

                Assert.Throws<ArgumentOutOfRangeException>(() => set.Connect(a, otherNodePortDef.Outputs[1], b, otherNodePortDef.Inputs[0]));
                Assert.Throws<ArgumentOutOfRangeException>(() => set.Connect(a, otherNodePortDef.Outputs[0], b, otherNodePortDef.Inputs[1]));
                Assert.Throws<ArgumentOutOfRangeException>(() => set.Connect(a, otherNodePortDef.Outputs[1], b, otherNodePortDef.Inputs[1]));

                set.Destroy(a, b);
            }
        }

        void ConnectingOutOfRange_InputArrayPortIndices_ThrowsException<TNodeDefinition>(InputPortID inputs, OutputPortID output)
            where TNodeDefinition : NodeDefinition, new()
        {
            using (var set = new NodeSet())
            {
                NodeHandle<TNodeDefinition>
                    a = set.Create<TNodeDefinition>(),
                    b = set.Create<TNodeDefinition>();

                Assert.Throws<IndexOutOfRangeException>(() => set.Connect(a, output, b, inputs, 0));

                set.SetPortArraySize(b, inputs, 1);

                set.Connect(a, output, b, inputs, 0);

                Assert.Throws<IndexOutOfRangeException>(() => set.Connect(a, output, b, inputs, 1));

                set.Destroy(a, b);
            }
        }

        void ConnectingOutOfRange_OutputArrayPortIndices_ThrowsException<TNodeDefinition>(OutputPortID outputs, InputPortID input)
            where TNodeDefinition : NodeDefinition, new()
        {
            using (var set = new NodeSet())
            {
                NodeHandle<TNodeDefinition>
                    a = set.Create<TNodeDefinition>(),
                    b = set.Create<TNodeDefinition>();

                Assert.Throws<IndexOutOfRangeException>(() => set.Connect(a, outputs, 0, b, input));

                set.SetPortArraySize(a, outputs, 1);

                set.Connect(a, outputs, 0, b, input);

                Assert.Throws<IndexOutOfRangeException>(() => set.Connect(a, outputs, 1, b, input));

                set.Destroy(a, b);
            }
        }

        [Test]
        public void ConnectingOutOfArrayPortIndicesRange_ThrowsException()
        {
            ConnectingOutOfRange_InputArrayPortIndices_ThrowsException<NodeWithAllTypesOfPorts>(
                (InputPortID)NodeWithAllTypesOfPorts.SimulationPorts.MessageArrayIn, (OutputPortID)NodeWithAllTypesOfPorts.SimulationPorts.MessageOut);
            ConnectingOutOfRange_OutputArrayPortIndices_ThrowsException<NodeWithAllTypesOfPorts>(
                (OutputPortID)NodeWithAllTypesOfPorts.SimulationPorts.MessageArrayOut, (InputPortID)NodeWithAllTypesOfPorts.SimulationPorts.MessageIn);
            ConnectingOutOfRange_InputArrayPortIndices_ThrowsException<NodeWithAllTypesOfPorts>(
                (InputPortID)NodeWithAllTypesOfPorts.KernelPorts.InputArrayScalar, (OutputPortID)NodeWithAllTypesOfPorts.KernelPorts.OutputScalar);
            ConnectingOutOfRange_OutputArrayPortIndices_ThrowsException<NodeWithAllTypesOfPorts>(
                (OutputPortID)NodeWithAllTypesOfPorts.KernelPorts.OutputArrayScalar, (InputPortID)NodeWithAllTypesOfPorts.KernelPorts.InputScalar);
        }

        void DisconnectingOutOfRange_InputArrayPortIndices_ThrowsException<TNodeDefinition>(InputPortID inputs, OutputPortID output)
            where TNodeDefinition : NodeDefinition, new()
        {
            using (var set = new NodeSet())
            {
                NodeHandle<TNodeDefinition>
                    a = set.Create<TNodeDefinition>(),
                    b = set.Create<TNodeDefinition>();

                Assert.Throws<IndexOutOfRangeException>(() => set.Connect(a, output, b, inputs, 0));

                set.SetPortArraySize(b, inputs, 1);

                set.Connect(a, output, b, inputs, 0);

                Assert.Throws<IndexOutOfRangeException>(() => set.Disconnect(a, output, b, inputs, 1));

                set.Disconnect(a, output, b, inputs, 0);

                set.SetPortArraySize(b, inputs, 0);

                Assert.Throws<IndexOutOfRangeException>(() => set.Connect(a, output, b, inputs, 0));

                set.Destroy(a, b);
            }
        }

        void DisconnectingOutOfRange_OutputArrayPortIndices_ThrowsException<TNodeDefinition>(OutputPortID outputs, InputPortID input)
            where TNodeDefinition : NodeDefinition, new()
        {
            using (var set = new NodeSet())
            {
                NodeHandle<TNodeDefinition>
                    a = set.Create<TNodeDefinition>(),
                    b = set.Create<TNodeDefinition>();

                Assert.Throws<IndexOutOfRangeException>(() => set.Connect(a, outputs, 0, b, input));

                set.SetPortArraySize(a, outputs, 1);

                set.Connect(a, outputs, 0, b, input);

                Assert.Throws<IndexOutOfRangeException>(() => set.Disconnect(a, outputs, 1, b, input));

                set.Disconnect(a, outputs, 0, b, input);

                set.SetPortArraySize(a, outputs, 0);

                Assert.Throws<IndexOutOfRangeException>(() => set.Connect(a, outputs, 0, b, input));

                set.Destroy(a, b);
            }
        }

        [Test]
        public void DisconnectingOutOfArrayPortIndicesRange_ThrowsException()
        {
            DisconnectingOutOfRange_InputArrayPortIndices_ThrowsException<NodeWithAllTypesOfPorts>(
                (InputPortID)NodeWithAllTypesOfPorts.SimulationPorts.MessageArrayIn, (OutputPortID)NodeWithAllTypesOfPorts.SimulationPorts.MessageOut);
            DisconnectingOutOfRange_OutputArrayPortIndices_ThrowsException<NodeWithAllTypesOfPorts>(
                (OutputPortID)NodeWithAllTypesOfPorts.SimulationPorts.MessageArrayOut, (InputPortID)NodeWithAllTypesOfPorts.SimulationPorts.MessageIn);
            DisconnectingOutOfRange_InputArrayPortIndices_ThrowsException<NodeWithAllTypesOfPorts>(
                (InputPortID)NodeWithAllTypesOfPorts.KernelPorts.InputArrayScalar, (OutputPortID)NodeWithAllTypesOfPorts.KernelPorts.OutputScalar);
            DisconnectingOutOfRange_OutputArrayPortIndices_ThrowsException<NodeWithAllTypesOfPorts>(
                (OutputPortID)NodeWithAllTypesOfPorts.KernelPorts.OutputArrayScalar, (InputPortID)NodeWithAllTypesOfPorts.KernelPorts.InputScalar);
        }

        public void ReducingConnectedInputArrayPort_ThrowsException<TNodeDefinition>(InputPortID inputs, OutputPortID output)
            where TNodeDefinition : NodeDefinition, new()
        {
            using (var set = new NodeSet())
            {
                NodeHandle<TNodeDefinition>
                    a = set.Create<TNodeDefinition>(),
                    b = set.Create<TNodeDefinition>();

                set.SetPortArraySize(a, inputs, 1);
                set.SetPortArraySize(b, inputs, 1);

                set.Connect(a, output, b, inputs, 0);

                set.SetPortArraySize(a, inputs, 0);
                Assert.Throws<InvalidOperationException>(() => set.SetPortArraySize(b, inputs, 0));

                set.Disconnect(a, output, b, inputs, 0);

                set.SetPortArraySize(b, inputs, 0);

                set.Destroy(a, b);
            }
        }

        public void ReducingConnectedOutputArrayPort_ThrowsException<TNodeDefinition>(OutputPortID outputs, InputPortID input)
            where TNodeDefinition : NodeDefinition, new()
        {
            using (var set = new NodeSet())
            {
                NodeHandle<TNodeDefinition>
                    a = set.Create<TNodeDefinition>(),
                    b = set.Create<TNodeDefinition>();

                set.SetPortArraySize(a, outputs, 1);
                set.SetPortArraySize(b, outputs, 1);

                set.Connect(a, outputs, 0, b, input);

                Assert.Throws<InvalidOperationException>(() => set.SetPortArraySize(a, outputs, 0));
                set.SetPortArraySize(b, outputs, 0);

                set.Disconnect(a, outputs, 0, b, input);

                set.SetPortArraySize(a, outputs, 0);

                set.Destroy(a, b);
            }
        }

        [Test]
        public void ReducingConnectedArrayPort_ThrowsException()
        {
            ReducingConnectedInputArrayPort_ThrowsException<NodeWithAllTypesOfPorts>(
                (InputPortID)NodeWithAllTypesOfPorts.SimulationPorts.MessageArrayIn, (OutputPortID)NodeWithAllTypesOfPorts.SimulationPorts.MessageOut);
            ReducingConnectedOutputArrayPort_ThrowsException<NodeWithAllTypesOfPorts>(
                (OutputPortID)NodeWithAllTypesOfPorts.SimulationPorts.MessageArrayOut, (InputPortID)NodeWithAllTypesOfPorts.SimulationPorts.MessageIn);
            ReducingConnectedInputArrayPort_ThrowsException<NodeWithAllTypesOfPorts>(
                (InputPortID)NodeWithAllTypesOfPorts.KernelPorts.InputArrayScalar, (OutputPortID)NodeWithAllTypesOfPorts.KernelPorts.OutputScalar);
            ReducingConnectedOutputArrayPort_ThrowsException<NodeWithAllTypesOfPorts>(
                (OutputPortID)NodeWithAllTypesOfPorts.KernelPorts.OutputArrayScalar, (InputPortID)NodeWithAllTypesOfPorts.KernelPorts.InputScalar);
        }

#pragma warning disable 649

        unsafe struct BaseAPI
        {
            public const int ConstantForBranchOne = 13;
            public const int ConstantForBranchTwo = 57;

            [NativeDisableUnsafePtrRestriction]
            public long* BranchOne;

            [NativeDisableUnsafePtrRestriction]
            public long* BranchTwo;

            public void Connect<TLeft, TRight>(in TLeft lhs, in TRight rhs, NodeSetAPI.ConnectionType connectionType = NodeSetAPI.ConnectionType.Normal)
                where TLeft : struct, IConcrete, IOutputEndpoint, IConnectableWith<TRight>
                where TRight : struct, IConcrete, IInputEndpoint, IConnectableWith<TLeft>
            {
                if (!lhs.IsWeak)
                {
                    uint category = (uint)lhs.Category;

                    if (lhs.Category == PortDescription.Category.Message && rhs.Category == PortDescription.Category.Data)
                        category = PortDescription.MessageToDataConnectionCategory;

                    if ((lhs.Category != PortDescription.Category.Data || rhs.Category != PortDescription.Category.Data) && connectionType != NodeSetAPI.ConnectionType.Normal)
                        throw new InvalidOperationException($"Cannot create a feedback connection for non-Data Port");

                    *BranchOne = ConstantForBranchOne;
                }
                else
                {
                    *BranchTwo = ConstantForBranchTwo;
                }
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        unsafe struct AJobWithSideEffects_CallingEndPointLikeAPI_WithStrongEndPoints_ImmediatelyConstructed : IJob
        {
            public BaseAPI API;

            public void Execute()
            {
                API.Connect(new DataOutputEndpoint<float>(), new DataInputEndpoint<float>());
            }

        }

        [BurstCompile(CompileSynchronously = true)]
        unsafe struct AJobWithSideEffects_CallingEndPointLikeAPI_WithMessageToData_Strong_ImmediatelyConstructed : IJob
        {
            public BaseAPI API;

            public void Execute()
            {
                API.Connect(new MessageOutputEndpoint<float>(), new DataInputEndpoint<float>());
            }

        }

        [BurstCompile(CompileSynchronously = true)]
        unsafe struct AJobWithSideEffects_CallingEndPointLikeAPI_WithMessageToData_Strong_ImmediatelyConstructed_WithInvalidFeedback : IJob
        {
            public BaseAPI API;

            public void Execute()
            {
                API.Connect(new MessageOutputEndpoint<float>(), new DataInputEndpoint<float>(), NodeSetAPI.ConnectionType.Feedback);
            }

        }

        [BurstCompile(CompileSynchronously = true)]
        unsafe struct AJobWithSideEffects_CallingEndPointLikeAPI_WithStrongEndPoints_InvisiblyConstructed : IJob
        {
            public BaseAPI API;

            public DataOutputEndpoint<float> Output;
            public DataInputEndpoint<float> Input;

            public void Execute()
            {
                API.Connect(Output, Input);
            }
        }


        [BurstCompile(CompileSynchronously = true)]
        unsafe struct AJobWithSideEffects_CallingEndPointLikeAPI_WithWeakEndPoints_ImmediatelyConstructed : IJob
        {
            public BaseAPI API;

            public void Execute()
            {
                API.Connect(new OutputEndpoint(), new InputEndpoint());
            }

        }

        [BurstCompile(CompileSynchronously = true)]
        unsafe struct AJobWithSideEffects_CallingEndPointLikeAPI_WithWeakEndPoints_InvisiblyConstructed : IJob
        {
            public BaseAPI API;

            public OutputEndpoint Output;
            public InputEndpoint Input;

            public void Execute()
            {
                API.Connect(Output, Input);
            }
        }

#pragma warning restore 649



        [Test]
        public unsafe void CallingEndPointLikeAPI_ThroughBurst_Works()
        {
            // This, until we have the Burst API for verifying constant compile-time expressions,
            // really just exist to have an easy way to inspect and play around with codegen.

            long a = default, b = default;

            BaseAPI api;
            api.BranchOne = &a;
            api.BranchTwo = &b;

            new AJobWithSideEffects_CallingEndPointLikeAPI_WithStrongEndPoints_ImmediatelyConstructed
            {
                API = api
            }.Run();

            Assert.AreEqual(BaseAPI.ConstantForBranchOne, a);
            Assert.Zero(b);

            a = b = default;

            new AJobWithSideEffects_CallingEndPointLikeAPI_WithMessageToData_Strong_ImmediatelyConstructed
            {
                API = api
            }.Run();

            Assert.AreEqual(BaseAPI.ConstantForBranchOne, a);
            Assert.Zero(b);

            a = b = default;

            /* Throws exception, difficult to test
            new AJobWithSideEffects_CallingEndPointLikeAPI_WithMessageToData_Strong_ImmediatelyConstructed_WithInvalidFeedback
            {
                API = api
            }.Run();

            Assert.Zero(a);
            Assert.Zero(b);
            */

            new AJobWithSideEffects_CallingEndPointLikeAPI_WithStrongEndPoints_InvisiblyConstructed
            {
                API = api
            }.Run();

            Assert.AreEqual(BaseAPI.ConstantForBranchOne, a);
            Assert.Zero(b);

            a = b = default;

            new AJobWithSideEffects_CallingEndPointLikeAPI_WithWeakEndPoints_ImmediatelyConstructed
            {
                API = api
            }.Run();

            Assert.Zero(a);
            Assert.AreEqual(BaseAPI.ConstantForBranchTwo, b);

            a = b = default;

            new AJobWithSideEffects_CallingEndPointLikeAPI_WithWeakEndPoints_InvisiblyConstructed
            {
                API = api
            }.Run();

            Assert.Zero(a);
            Assert.AreEqual(BaseAPI.ConstantForBranchTwo, b);
        }


    }
}
