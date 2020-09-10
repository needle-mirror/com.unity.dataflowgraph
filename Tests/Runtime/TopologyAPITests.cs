using System;
using NUnit.Framework;

namespace Unity.DataFlowGraph.Tests
{
    class TopologyAPITests
    {
        public struct Node : INodeData
        {
            public int Contents;
        }

        class InOutTestNode : NodeDefinition<Node, InOutTestNode.SimPorts>, IMsgHandler<Message>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                public MessageInput<InOutTestNode, Message> Input;
                public MessageOutput<InOutTestNode, Message> Output;
#pragma warning restore 649
            }

            public void HandleMessage(in MessageContext ctx, in Message msg)
            {
                ref var data = ref GetNodeData(ctx.Handle);

                Assert.That(ctx.Port == SimulationPorts.Input);
                data.Contents += msg.Contents;
                ctx.EmitMessage(SimulationPorts.Output, new Message(data.Contents + 1));
            }
        }

        class TwoInOutTestNode : NodeDefinition<Node, TwoInOutTestNode.SimPorts>, IMsgHandler<Message>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                public MessageInput<TwoInOutTestNode, Message> Input1, Input2;
                public MessageOutput<TwoInOutTestNode, Message> Output1, Output2;
#pragma warning restore 649
            }

            public void HandleMessage(in MessageContext ctx, in Message msg) { }
        }

        [Test]
        public void CanConnectTwoNodes_AndKeepTopologyIntegrity()
        {
            using (var set = new NodeSet())
            {
                NodeHandle<InOutTestNode>
                    a = set.Create<InOutTestNode>(),
                    b = set.Create<InOutTestNode>();

                set.Connect(b, InOutTestNode.SimulationPorts.Output, a, InOutTestNode.SimulationPorts.Input);

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

                set.Connect(a, InOutTestNode.SimulationPorts.Output, b, InOutTestNode.SimulationPorts.Input);
                Assert.Throws<ArgumentException>(() => set.Connect(a, InOutTestNode.SimulationPorts.Output, b, InOutTestNode.SimulationPorts.Input));

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

                set.Connect(a, InOutTestNode.SimulationPorts.Output, b, InOutTestNode.SimulationPorts.Input);
                Assert.Throws<ArgumentException>(() => set.Connect(a, InOutTestNode.SimulationPorts.Output, b, InOutTestNode.SimulationPorts.Input));
                set.Disconnect(a, InOutTestNode.SimulationPorts.Output, b, InOutTestNode.SimulationPorts.Input);
                set.Connect(a, InOutTestNode.SimulationPorts.Output, b, InOutTestNode.SimulationPorts.Input);

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

                set.Connect(a, KernelAdderNode.KernelPorts.Output, c, KernelAdderNode.KernelPorts.Input);
                Assert.Throws<ArgumentException>(() => set.Connect(b, KernelAdderNode.KernelPorts.Output, c, KernelAdderNode.KernelPorts.Input));
                set.Disconnect(a, KernelAdderNode.KernelPorts.Output, c, KernelAdderNode.KernelPorts.Input);
                set.Connect(a, KernelAdderNode.KernelPorts.Output, c, KernelAdderNode.KernelPorts.Input);

                set.Destroy(a, b, c);
            }
        }

        [Test]
        public void ConnectThrows_OnDefaultConstructedHandles()
        {
            using (var set = new NodeSet())
            {
                NodeHandle a = set.Create<InOutTestNode>(), b = set.Create<InOutTestNode>();

                var e = Assert.Throws<ArgumentException>(() => set.Connect(a, (OutputPortID) InOutTestNode.SimulationPorts.Output, new NodeHandle(), (InputPortID)InOutTestNode.SimulationPorts.Input));
                StringAssert.Contains(Collections.VersionedList<InternalNodeData>.ValidationFail_InvalidMessage, e.Message);

                e = Assert.Throws<ArgumentException>(() => set.Connect(new NodeHandle(), (OutputPortID) InOutTestNode.SimulationPorts.Output, b, (InputPortID)InOutTestNode.SimulationPorts.Input));
                StringAssert.Contains(Collections.VersionedList<InternalNodeData>.ValidationFail_InvalidMessage, e.Message);

                set.Destroy(a, b);
            }
        }

        [Test]
        public void ConnectThrows_OnDefaultConstructedPortIDs()
        {
            using (var set = new NodeSet())
            {
                NodeHandle a = set.Create<InOutTestNode>(), b = set.Create<InOutTestNode>();

                var e = Assert.Throws<ArgumentException>(() => set.Connect(a, new OutputPortID(), b, (InputPortID)InOutTestNode.SimulationPorts.Input));
                StringAssert.Contains("Invalid output port", e.Message);

                e = Assert.Throws<ArgumentException>(() => set.Connect(a, (OutputPortID) InOutTestNode.SimulationPorts.Output, b, new InputPortID()));
                StringAssert.Contains("Invalid input port", e.Message);

                set.Destroy(a, b);
            }
        }

        [Test]
        public void DisconnectThrows_OnDefaultConstructedHandles()
        {
            using (var set = new NodeSet())
            {
                NodeHandle node = set.Create<InOutTestNode>();

                var e = Assert.Throws<ArgumentException>(() => set.Disconnect(new NodeHandle(), (OutputPortID) InOutTestNode.SimulationPorts.Output, node, (InputPortID)InOutTestNode.SimulationPorts.Input));
                StringAssert.Contains(Collections.VersionedList<InternalNodeData>.ValidationFail_InvalidMessage, e.Message);

                e = Assert.Throws<ArgumentException>(() => set.Disconnect(node, (OutputPortID) InOutTestNode.SimulationPorts.Output, new NodeHandle(), (InputPortID)InOutTestNode.SimulationPorts.Input));
                StringAssert.Contains(Collections.VersionedList<InternalNodeData>.ValidationFail_InvalidMessage, e.Message);

                set.Destroy(node);
            }
        }

        [Test]
        public void DisonnectThrows_OnDefaultConstructedPortIDs()
        {
            using (var set = new NodeSet())
            {
                NodeHandle a = set.Create<InOutTestNode>(), b = set.Create<InOutTestNode>();

                var e = Assert.Throws<ArgumentException>(() => set.Disconnect(a, new OutputPortID(), b, (InputPortID)InOutTestNode.SimulationPorts.Input));
                StringAssert.Contains("Invalid output port", e.Message);

                e = Assert.Throws<ArgumentException>(() => set.Disconnect(a, (OutputPortID) InOutTestNode.SimulationPorts.Output, b, new InputPortID()));
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
        }
    }
}
