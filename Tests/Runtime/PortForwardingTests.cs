using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Burst;
using UnityEngine;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.TestTools;
using static Unity.DataFlowGraph.Tests.ComponentNodeSetTests;
using Unity.Entities;

namespace Unity.DataFlowGraph.Tests
{
    public class PortForwardingTests
    {
        // Here we only need to test that mutation to the topology structures happen as we expect them to.
        // There is coverage for connections and flow in general.

        public class InOutTestNode : SimulationNodeDefinition<InOutTestNode.SimPorts>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
                public MessageInput<InOutTestNode, Message> Input;
                public MessageOutput<InOutTestNode, Message> Output;
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

        public class StaticUberNode : SimulationNodeDefinition<StaticUberNode.SimPorts>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
                public MessageInput<StaticUberNode, Message> ForwardedInput;
                public MessageOutput<StaticUberNode, Message> ForwardedOutput;
            }

            public struct Data : INodeData, IInit, IDestroy, IMsgHandler<Message>
            {
                public NodeHandle<InOutTestNode> ChildNode;

                public void HandleMessage(MessageContext ctx, in Message msg) => throw new NotImplementedException();

                public void Init(InitContext ctx)
                {
                    ChildNode = ctx.Set.Create<InOutTestNode>();
                    ctx.ForwardInput(SimulationPorts.ForwardedInput, ChildNode, InOutTestNode.SimulationPorts.Input);
                    ctx.ForwardOutput(SimulationPorts.ForwardedOutput, ChildNode, InOutTestNode.SimulationPorts.Output);
                }

                public void Destroy(DestroyContext ctx) => ctx.Set.Destroy(ChildNode);
            }
        }

        [Test]
        public void Connecting_ToAndFromUberNodeWorks_AndPopulatesTopologyDatabase()
        {
            using (var set = new NodeSet())
            {
                var msgNode = set.Create<InOutTestNode>();
                var uberNode = set.Create<StaticUberNode>();

                var initialEdgeCount = set.GetTopologyDatabase_ForTesting().CountEstablishedConnections();

                // other -> ubernode

                set.Connect(msgNode, InOutTestNode.SimulationPorts.Output, uberNode, StaticUberNode.SimulationPorts.ForwardedInput);
                Assert.AreEqual(initialEdgeCount + 1, set.GetTopologyDatabase_ForTesting().CountEstablishedConnections());

                set.Disconnect(msgNode, InOutTestNode.SimulationPorts.Output, uberNode, StaticUberNode.SimulationPorts.ForwardedInput);
                Assert.AreEqual(1, set.GetTopologyDatabase_ForTesting().FreeEdges);

                // ubernode -> other

                set.Connect(uberNode, StaticUberNode.SimulationPorts.ForwardedOutput, msgNode, InOutTestNode.SimulationPorts.Input);
                Assert.AreEqual(0, set.GetTopologyDatabase_ForTesting().FreeEdges);

                set.Disconnect(uberNode, StaticUberNode.SimulationPorts.ForwardedOutput, msgNode, InOutTestNode.SimulationPorts.Input);
                Assert.AreEqual(1, set.GetTopologyDatabase_ForTesting().FreeEdges);

                set.Destroy(msgNode, uberNode);
            }
        }

        [Test]
        public void ConnectingIO_ToUberNode_InternallyOnlyCreates_ForwardedConnection()
        {
            using (var set = new NodeSet())
            {
                var msgNode = set.Create<InOutTestNode>();
                var uberNode = set.Create<StaticUberNode>();

                set.Connect(msgNode, InOutTestNode.SimulationPorts.Output, uberNode, StaticUberNode.SimulationPorts.ForwardedInput);

                ref readonly var newEdge = ref set.GetTopologyDatabase_ForTesting()[set.GetTopologyDatabase_ForTesting().TotalConnections - 1];
                var edgeCopy = newEdge;

                Assert.IsTrue(newEdge.Valid);
                Assert.AreEqual(newEdge.Source.ToPublicHandle(), (NodeHandle)msgNode);
                set.SendTest(uberNode, (StaticUberNode.Data data) =>
                    Assert.AreEqual(edgeCopy.Destination.ToPublicHandle(), (NodeHandle)data.ChildNode));

                set.Disconnect(msgNode, InOutTestNode.SimulationPorts.Output, uberNode, StaticUberNode.SimulationPorts.ForwardedInput);

                Assert.IsFalse(newEdge.Valid);

                set.Destroy(msgNode, uberNode);
            }
        }

        [Test]
        public void ConnectingIO_FromUberNode_InternallyOnlyCreates_ForwardedConnection()
        {
            using (var set = new NodeSet())
            {
                var msgNode = set.Create<InOutTestNode>();
                var uberNode = set.Create<StaticUberNode>();

                set.Connect(uberNode, StaticUberNode.SimulationPorts.ForwardedOutput, msgNode, InOutTestNode.SimulationPorts.Input);

                ref readonly var newEdge = ref set.GetTopologyDatabase_ForTesting()[set.GetTopologyDatabase_ForTesting().TotalConnections - 1];
                var edgeCopy = newEdge;

                Assert.IsTrue(newEdge.Valid);
                set.SendTest(uberNode, (StaticUberNode.Data data) =>
                    Assert.AreEqual(edgeCopy.Source.ToPublicHandle(), (NodeHandle)data.ChildNode));
                Assert.AreEqual(newEdge.Destination.ToPublicHandle(), (NodeHandle)msgNode);

                set.Disconnect(uberNode, StaticUberNode.SimulationPorts.ForwardedOutput, msgNode, InOutTestNode.SimulationPorts.Input);

                Assert.IsFalse(newEdge.Valid);

                set.Destroy(msgNode, uberNode);
            }
        }

        [Test]
        public void CanConnectTwoForwardingUberNodes_Together()
        {
            using (var set = new NodeSet())
            {
                var uberNodeA = set.Create<StaticUberNode>();
                var uberNodeB = set.Create<StaticUberNode>();

                set.Connect(uberNodeA, StaticUberNode.SimulationPorts.ForwardedOutput, uberNodeB, StaticUberNode.SimulationPorts.ForwardedInput);

                ref readonly var newEdge = ref set.GetTopologyDatabase_ForTesting()[set.GetTopologyDatabase_ForTesting().TotalConnections - 1];
                var edgeCopy = newEdge;

                Assert.IsTrue(newEdge.Valid);
                set.SendTest(uberNodeA, (StaticUberNode.Data data) =>
                    Assert.AreEqual(edgeCopy.Source.ToPublicHandle(), (NodeHandle)data.ChildNode));
                set.SendTest(uberNodeB, (StaticUberNode.Data data) =>
                    Assert.AreEqual(edgeCopy.Destination.ToPublicHandle(), (NodeHandle)data.ChildNode));

                set.Disconnect(uberNodeA, StaticUberNode.SimulationPorts.ForwardedOutput, uberNodeB, StaticUberNode.SimulationPorts.ForwardedInput);

                Assert.IsFalse(newEdge.Valid);

                set.Destroy(uberNodeA, uberNodeB);
            }
        }


        public class RootNestedUberNode : SimulationNodeDefinition<RootNestedUberNode.SimPorts>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
                // Just to ensure port id's being tested are not all 0's
                public MessageInput<RootNestedUberNode, Message> _1, _2, _3, _4, _5;
                public MessageOutput<RootNestedUberNode, Message> __1, __2, __3, __4, __5, __6;

                public MessageInput<RootNestedUberNode, Message> ForwardedInput;
                public MessageOutput<RootNestedUberNode, Message> ForwardedOutput;
            }

            public struct Data : INodeData, IInit, IDestroy, IMsgHandler<Message>
            {
                public NodeHandle<NestedUberNodeMiddle> Child;

                public void HandleMessage(MessageContext ctx, in Message msg) => throw new NotImplementedException();

                public void Init(InitContext ctx)
                {
                    Child = ctx.Set.Create<NestedUberNodeMiddle>();

                    ctx.ForwardInput(SimulationPorts.ForwardedInput, Child, NestedUberNodeMiddle.SimulationPorts.ForwardedInput);
                    ctx.ForwardOutput(SimulationPorts.ForwardedOutput, Child, NestedUberNodeMiddle.SimulationPorts.ForwardedOutput);
                }

                public void Destroy(DestroyContext ctx)
                {
                    ctx.Set.Destroy(Child);
                }
            }
        }

        public class NestedUberNodeMiddle : SimulationNodeDefinition<NestedUberNodeMiddle.SimPorts>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
                // Just to ensure port id's being tested are not all 0's
                public MessageInput<NestedUberNodeMiddle, Message> _1, _2;
                public MessageOutput<NestedUberNodeMiddle, Message> __1, __2, __3, __4;

                public MessageInput<NestedUberNodeMiddle, Message> ForwardedInput;
                public MessageOutput<NestedUberNodeMiddle, Message> ForwardedOutput;
            }

            public struct Data : INodeData, IInit, IDestroy, IMsgHandler<Message>
            {
                public NodeHandle<InOutTestNode> Child;

                public void HandleMessage(MessageContext ctx, in Message msg) => throw new NotImplementedException();

                public void Init(InitContext ctx)
                {
                    Child = ctx.Set.Create<InOutTestNode>();

                    ctx.ForwardInput(SimulationPorts.ForwardedInput, Child, InOutTestNode.SimulationPorts.Input);
                    ctx.ForwardOutput(SimulationPorts.ForwardedOutput, Child, InOutTestNode.SimulationPorts.Output);
                }

                public void Destroy(DestroyContext ctx)
                {
                    ctx.Set.Destroy(Child);
                }
            }

            public ref Data ExposeData(NodeHandle handle) => ref Set.GetNodeData<Data>(handle);
        }

        [Test]
        public void CanConnectDoubleRecursive_UberNodes_Together()
        {
            using (var set = new NodeSet())
            {
                var uberNodeA = set.Create<RootNestedUberNode>();
                var uberNodeB = set.Create<RootNestedUberNode>();

                set.Connect(uberNodeA, RootNestedUberNode.SimulationPorts.ForwardedOutput, uberNodeB, RootNestedUberNode.SimulationPorts.ForwardedInput);

                ref readonly var newEdge = ref set.GetTopologyDatabase_ForTesting()[set.GetTopologyDatabase_ForTesting().TotalConnections - 1];
                var edgeCopy = newEdge;

                Assert.IsTrue(newEdge.Valid);

                set.SendTest<RootNestedUberNode.Data>(uberNodeA, ctx =>
                    ctx.SendTest(ctx.NodeData.Child, (NestedUberNodeMiddle.Data data) =>
                        Assert.AreEqual(edgeCopy.Source.ToPublicHandle(), (NodeHandle)data.Child)));

                set.SendTest<RootNestedUberNode.Data>(uberNodeB, ctx =>
                    ctx.SendTest(ctx.NodeData.Child, (NestedUberNodeMiddle.Data data) =>
                        Assert.AreEqual(edgeCopy.Destination.ToPublicHandle(), (NodeHandle)data.Child)));

                set.Disconnect(uberNodeA, RootNestedUberNode.SimulationPorts.ForwardedOutput, uberNodeB, RootNestedUberNode.SimulationPorts.ForwardedInput);

                Assert.IsFalse(newEdge.Valid);

                set.Destroy(uberNodeA, uberNodeB);
            }
        }

        [Test]
        public void PortForwardingToEmbeddedChild_CorrectlySetsUpForwardingTable()
        {
            using (var set = new NodeSet())
            {
                var nestedMiddle = set.Create<NestedUberNodeMiddle>();

                var forwardTable = set.GetForwardingTable();

                ref readonly var internalNestedMiddle = ref set.Nodes[nestedMiddle.VHandle];
                Assert.AreNotEqual(internalNestedMiddle.ForwardedPortHead, ForwardPortHandle.Invalid);

                // Test that middle level uber node's forward table also resolves to embedded child
                var middleFirst = forwardTable[internalNestedMiddle.ForwardedPortHead];
                Assert.AreNotEqual(middleFirst.NextIndex, ForwardPortHandle.Invalid);
                var middleSecond = forwardTable[middleFirst.NextIndex];

                var middleInput = middleFirst.IsInput ? middleFirst : middleSecond;
                var middleOutput = middleFirst.IsInput ? middleSecond : middleFirst;

                set.SendTest(nestedMiddle, (NestedUberNodeMiddle.Data data) =>
                    Assert.AreEqual(middleInput.Replacement.ToPublicHandle(), (NodeHandle)data.Child));
                set.SendTest(nestedMiddle, (NestedUberNodeMiddle.Data data) =>
                    Assert.AreEqual(middleOutput.Replacement.ToPublicHandle(), (NodeHandle)data.Child));

                Assert.AreEqual(middleInput.GetOriginInputPortID(), NestedUberNodeMiddle.SimulationPorts.ForwardedInput.Port);
                Assert.AreEqual(middleOutput.GetOriginOutputPortID(), NestedUberNodeMiddle.SimulationPorts.ForwardedOutput.Port);

                Assert.AreEqual(middleInput.GetReplacedInputPortID(), InOutTestNode.SimulationPorts.Input.Port);
                Assert.AreEqual(middleOutput.GetReplacedOutputPortID(), InOutTestNode.SimulationPorts.Output.Port);

                set.Destroy(nestedMiddle);
            }
        }

        [Test]
        public void ForwardDoubleRecursion_IsSolved_InForwardingTable()
        {
            using (var set = new NodeSet())
            {
                var rootUberNode = set.Create<RootNestedUberNode>();

                var forwardTable = set.GetForwardingTable();

                ref readonly var internalRootUberNode = ref set.Nodes[rootUberNode.VHandle];

                Assert.AreNotEqual(internalRootUberNode.ForwardedPortHead, ForwardPortHandle.Invalid);

                // Test that top level uber node's forward table resolves directly to the recursively nested child
                var rootFirst = forwardTable[internalRootUberNode.ForwardedPortHead];
                Assert.AreNotEqual(rootFirst.NextIndex, ForwardPortHandle.Invalid);
                var rootSecond = forwardTable[rootFirst.NextIndex];

                var rootInput = rootFirst.IsInput ? rootFirst : rootSecond;
                var rootOutput = rootFirst.IsInput ? rootSecond : rootFirst;

                set.SendTest<RootNestedUberNode.Data>(rootUberNode, ctx =>
                    ctx.SendTest(ctx.NodeData.Child, (NestedUberNodeMiddle.Data data) => {
                        Assert.AreEqual(rootInput.Replacement.ToPublicHandle(), (NodeHandle)data.Child);
                        Assert.AreEqual(rootOutput.Replacement.ToPublicHandle(), (NodeHandle)data.Child);
                    }));

                Assert.AreEqual(rootInput.GetOriginInputPortID(), RootNestedUberNode.SimulationPorts.ForwardedInput.Port);
                Assert.AreEqual(rootOutput.GetOriginOutputPortID(), RootNestedUberNode.SimulationPorts.ForwardedOutput.Port);

                Assert.AreEqual(rootInput.GetReplacedInputPortID(), InOutTestNode.SimulationPorts.Input.Port);
                Assert.AreEqual(rootOutput.GetReplacedOutputPortID(), InOutTestNode.SimulationPorts.Output.Port);

                set.Destroy(rootUberNode);
            }
        }

        public class AlienUberNode : SimulationNodeDefinition<AlienUberNode.SimPorts>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
                public MessageInput<AlienUberNode, Message> ForwardedInput;
                public MessageOutput<AlienUberNode, Message> ForwardedOutput;
            }

            struct Data : INodeData, IInit, IMsgHandler<Message>
            {
                public NodeHandle<InOutTestNode> Child;

                public void HandleMessage(MessageContext ctx, in Message msg) => throw new NotImplementedException();

                public void Init(InitContext ctx)
                {
                    Child = ctx.Set.Create<InOutTestNode>();

                    try
                    {
                        ctx.ForwardInput(InOutTestNode.SimulationPorts.Input, Child, InOutTestNode.SimulationPorts.Input);

                    }
                    catch (ArgumentException)
                    {
                        Debug.Log("All is good");
                    }
                    finally
                    {
                        ctx.Set.Destroy(Child);
                    }
                }
            }
        }

        [Test]
        public void ForwardingAlienPortFromSelf_ThrowsArgumentException()
        {
            using (var set = new NodeSet())
            {
                LogAssert.Expect(LogType.Log, "All is good");
                var node = set.Create<AlienUberNode>();
                set.Destroy(node);
            }
        }

        public class SelfForwardingUberNode : SimulationNodeDefinition<SelfForwardingUberNode.SimPorts>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
                public MessageInput<SelfForwardingUberNode, Message> ForwardedInput;
                public MessageOutput<SelfForwardingUberNode, Message> ForwardedOutput;
            }

            struct Data : INodeData, IInit, IMsgHandler<Message> {

                public void HandleMessage(MessageContext ctx, in Message msg) => throw new NotImplementedException();

                public void Init(InitContext ctx)
                {
                    try
                    {
                        ctx.ForwardInput(SimulationPorts.ForwardedInput, ctx.Set.CastHandle<SelfForwardingUberNode>(ctx.Handle), SimulationPorts.ForwardedInput);
                    }
                    catch (ArgumentException)
                    {
                        Debug.Log("All is good");
                    }
                }
            }
        }

        [Test]
        public void ForwardingToSelf_ThrowsArgumentException()
        {
            using (var set = new NodeSet())
            {
                LogAssert.Expect(LogType.Log, "All is good");
                var node = set.Create<SelfForwardingUberNode>();
                set.Destroy(node);
            }
        }

        public enum PortTypes
        {
            Message, DSL, Data, DataBuffer
        }

        [TestCase(PortTypes.Message), TestCase(PortTypes.DSL), TestCase(PortTypes.Data), TestCase(PortTypes.DataBuffer)]
        public void AllStrongForwardOverloads_ProducesExpectedForwardBuffer(PortTypes type)
        {
            BlitList<ForwardedPort.Unchecked> ports = default;

            try
            {
                var source = ValidatedHandle.Create_ForTesting(VersionedHandle.Create_ForTesting(1, 3, 99));
                var dest = new NodeHandle<NodeWithAllTypesOfPorts>(VersionedHandle.Create_ForTesting(2, 0, 3));

                InitContext ctx = new InitContext(source, NodeDefinitionTypeIndex<NodeWithAllTypesOfPorts>.Index, null, ref ports);

                InputPortID inputPort;
                OutputPortID outputPort;
                InputPortID inputArrayPort = default;
                OutputPortID outputArrayPort = default;

                switch (type)
                {
                    case PortTypes.Message:
                        inputPort = NodeWithAllTypesOfPorts.SimulationPorts.MessageIn.Port;
                        outputPort = NodeWithAllTypesOfPorts.SimulationPorts.MessageOut.Port;
                        inputArrayPort = NodeWithAllTypesOfPorts.SimulationPorts.MessageArrayIn.GetPortID();
                        outputArrayPort = NodeWithAllTypesOfPorts.SimulationPorts.MessageArrayOut.GetPortID();

                        ctx.ForwardInput(NodeWithAllTypesOfPorts.SimulationPorts.MessageIn, dest, NodeWithAllTypesOfPorts.SimulationPorts.MessageIn);
                        ctx.ForwardOutput(NodeWithAllTypesOfPorts.SimulationPorts.MessageOut, dest, NodeWithAllTypesOfPorts.SimulationPorts.MessageOut);
                        ctx.ForwardInput(NodeWithAllTypesOfPorts.SimulationPorts.MessageArrayIn, dest, NodeWithAllTypesOfPorts.SimulationPorts.MessageArrayIn);
                        ctx.ForwardOutput(NodeWithAllTypesOfPorts.SimulationPorts.MessageArrayOut, dest, NodeWithAllTypesOfPorts.SimulationPorts.MessageArrayOut);
                        break;
                    case PortTypes.DSL:
                        inputPort = NodeWithAllTypesOfPorts.SimulationPorts.DSLIn.Port;
                        outputPort = NodeWithAllTypesOfPorts.SimulationPorts.DSLOut.Port;

                        ctx.ForwardInput(NodeWithAllTypesOfPorts.SimulationPorts.DSLIn, dest, NodeWithAllTypesOfPorts.SimulationPorts.DSLIn);
                        ctx.ForwardOutput(NodeWithAllTypesOfPorts.SimulationPorts.DSLOut, dest, NodeWithAllTypesOfPorts.SimulationPorts.DSLOut);
                        break;
                    case PortTypes.Data:
                        inputPort = NodeWithAllTypesOfPorts.KernelPorts.InputScalar.Port;
                        outputPort = NodeWithAllTypesOfPorts.KernelPorts.OutputScalar.Port;
                        inputArrayPort = NodeWithAllTypesOfPorts.KernelPorts.InputArrayScalar.GetPortID();
                        outputArrayPort = NodeWithAllTypesOfPorts.KernelPorts.OutputArrayScalar.GetPortID();

                        ctx.ForwardInput(NodeWithAllTypesOfPorts.KernelPorts.InputScalar, dest, NodeWithAllTypesOfPorts.KernelPorts.InputScalar);
                        ctx.ForwardOutput(NodeWithAllTypesOfPorts.KernelPorts.OutputScalar, dest, NodeWithAllTypesOfPorts.KernelPorts.OutputScalar);
                        ctx.ForwardInput(NodeWithAllTypesOfPorts.KernelPorts.InputArrayScalar, dest, NodeWithAllTypesOfPorts.KernelPorts.InputArrayScalar);
                        ctx.ForwardOutput(NodeWithAllTypesOfPorts.KernelPorts.OutputArrayScalar, dest, NodeWithAllTypesOfPorts.KernelPorts.OutputArrayScalar);
                        break;
                    case PortTypes.DataBuffer:
                        inputPort = NodeWithAllTypesOfPorts.KernelPorts.InputBuffer.Port;
                        outputPort = NodeWithAllTypesOfPorts.KernelPorts.OutputBuffer.Port;
                        inputArrayPort = NodeWithAllTypesOfPorts.KernelPorts.InputArrayBuffer.GetPortID();
                        outputArrayPort = NodeWithAllTypesOfPorts.KernelPorts.OutputArrayBuffer.GetPortID();

                        ctx.ForwardInput(NodeWithAllTypesOfPorts.KernelPorts.InputBuffer, dest, NodeWithAllTypesOfPorts.KernelPorts.InputBuffer);
                        ctx.ForwardOutput(NodeWithAllTypesOfPorts.KernelPorts.OutputBuffer, dest, NodeWithAllTypesOfPorts.KernelPorts.OutputBuffer);
                        ctx.ForwardInput(NodeWithAllTypesOfPorts.KernelPorts.InputArrayBuffer, dest, NodeWithAllTypesOfPorts.KernelPorts.InputArrayBuffer);
                        ctx.ForwardOutput(NodeWithAllTypesOfPorts.KernelPorts.OutputArrayBuffer, dest, NodeWithAllTypesOfPorts.KernelPorts.OutputArrayBuffer);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(type), type, null);
                }

                Assert.IsTrue(ports.IsCreated);
                Assert.AreEqual(type == PortTypes.DSL ? 2 : 4, ports.Count);

                var input = ports[0];
                var output = ports[1];

                Assert.IsTrue(input.IsInput);
                Assert.AreEqual((NodeHandle)dest, input.Replacement);
                Assert.AreEqual(inputPort.Storage.DFGPort, input.OriginPortID);
                Assert.AreEqual(inputPort.Storage, input.ReplacedPortID);

                Assert.IsFalse(output.IsInput);
                Assert.AreEqual((NodeHandle)dest, output.Replacement);
                Assert.AreEqual(outputPort.Storage.DFGPort, output.OriginPortID);
                Assert.AreEqual(outputPort.Storage, output.ReplacedPortID);

                if (type != PortTypes.DSL)
                {
                    var inputArray = ports[2];
                    Assert.IsTrue(inputArray.IsInput);
                    Assert.AreEqual((NodeHandle)dest, inputArray.Replacement);
                    Assert.AreEqual(inputArrayPort.Storage.DFGPort, inputArray.OriginPortID);
                    Assert.AreEqual(inputArrayPort.Storage, inputArray.ReplacedPortID);

                    var outputArray = ports[3];
                    Assert.IsFalse(outputArray.IsInput);
                    Assert.AreEqual((NodeHandle)dest, outputArray.Replacement);
                    Assert.AreEqual(outputArrayPort.Storage.DFGPort, outputArray.OriginPortID);
                    Assert.AreEqual(outputArrayPort.Storage, outputArray.ReplacedPortID);
                }
            }
            finally
            {
                if (ports.IsCreated)
                    ports.Dispose();
            }
        }


        public class UberNodeThatSendsMessages_ThroughForwardingPorts
            : SimulationNodeDefinition<UberNodeThatSendsMessages_ThroughForwardingPorts.SimPorts>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
                public MessageInput<UberNodeThatSendsMessages_ThroughForwardingPorts, Message> TriggerSendMessageThroughForwardedPort;
                public MessageOutput<UberNodeThatSendsMessages_ThroughForwardingPorts, Message> ForwardedOutput;
            }

            struct Data : INodeData, IInit, IDestroy, IMsgHandler<Message>
            {
                public NodeHandle<InOutTestNode> Child;

                public void HandleMessage(MessageContext ctx, in Message msg)
                {
                    ctx.EmitMessage(SimulationPorts.ForwardedOutput, msg);
                }

                public void Init(InitContext ctx)
                {
                    Child = ctx.Set.Create<InOutTestNode>();
                    ctx.ForwardOutput(SimulationPorts.ForwardedOutput, Child, InOutTestNode.SimulationPorts.Output);
                }

                public void Destroy(DestroyContext ctx)
                {
                    ctx.Set.Destroy(Child);
                }
            }
        }

        [Test]
        public void EmittingMessages_ThroughPreviouslyForwardedPorts_ThrowsException()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<UberNodeThatSendsMessages_ThroughForwardingPorts>();
                var func = set.GetDefinition(node);

                Assert.Throws<ArgumentException>(
                    () => set.SendMessage(node, UberNodeThatSendsMessages_ThroughForwardingPorts.SimulationPorts.TriggerSendMessageThroughForwardedPort, new Message()));

                set.Destroy(node);
            }
        }

        [IsNotInstantiable]
        public class UberNodeThatForwardsToNonExistingNode
            : SimulationNodeDefinition<UberNodeThatForwardsToNonExistingNode.SimPorts>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
                public MessageOutput<UberNodeThatForwardsToNonExistingNode, Message> ForwardedOutput;
            }

            struct Data : INodeData, IInit
            {
                public void Init(InitContext ctx)
                {
                    ctx.ForwardOutput(SimulationPorts.ForwardedOutput, new NodeHandle<InOutTestNode>(), InOutTestNode.SimulationPorts.Output);
                }
            }
        }

        [Test]
        public void ForwardingToNonExistingNode_ThrowsArgumentException()
        {
            using (var set = new NodeSet())
            {
                LogAssert.Expect(LogType.Error, new Regex("Throwing exceptions from constructors"));
                Assert.Throws<ArgumentException>(() => set.Create<UberNodeThatForwardsToNonExistingNode>());
            }
        }

        [IsNotInstantiable]
        public class UberNodeThatPrematurely_KillsChildren
            : SimulationNodeDefinition<UberNodeThatPrematurely_KillsChildren.SimPorts>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
                public MessageInput<UberNodeThatPrematurely_KillsChildren, Message> ForwardedInput;
                public MessageInput<UberNodeThatPrematurely_KillsChildren, bool> KillChildrenTrigger;
                public MessageOutput<UberNodeThatPrematurely_KillsChildren, Message> ForwardedOutput;
            }

            struct Data : INodeData, IInit, IMsgHandler<Message>, IMsgHandler<bool>
            {
                public NodeHandle<InOutTestNode> Child;

                public void Init(InitContext ctx)
                {
                    Child = ctx.Set.Create<InOutTestNode>();
                    ctx.ForwardOutput(SimulationPorts.ForwardedOutput, Child, InOutTestNode.SimulationPorts.Output);
                    ctx.ForwardInput(SimulationPorts.ForwardedInput, Child, InOutTestNode.SimulationPorts.Input);
                }

                public void HandleMessage(MessageContext ctx, in bool _) => ctx.Set.Destroy(Child);

                public void HandleMessage(MessageContext ctx, in Message msg) => throw new NotImplementedException();
            }
        }

        [Test]
        public void CreatingConnection_BetweenUberNodeAndSomethingElse_WhereForwardedReplacement_IsDestroyed_ThrowsException()
        {
            using (var set = new NodeSet())
            {
                var uberA = set.Create<UberNodeThatPrematurely_KillsChildren>();
                var rhs = set.Create<InOutTestNode>();

                set.Connect(uberA, UberNodeThatPrematurely_KillsChildren.SimulationPorts.ForwardedOutput, rhs, InOutTestNode.SimulationPorts.Input);
                set.Connect(rhs, InOutTestNode.SimulationPorts.Output, uberA, UberNodeThatPrematurely_KillsChildren.SimulationPorts.ForwardedInput);

                set.Disconnect(uberA, UberNodeThatPrematurely_KillsChildren.SimulationPorts.ForwardedOutput, rhs, InOutTestNode.SimulationPorts.Input);
                set.Disconnect(rhs, InOutTestNode.SimulationPorts.Output, uberA, UberNodeThatPrematurely_KillsChildren.SimulationPorts.ForwardedInput);

                set.SendMessage(uberA, UberNodeThatPrematurely_KillsChildren.SimulationPorts.KillChildrenTrigger, true);

                Assert.Throws<InvalidOperationException>(() => set.Connect(uberA, UberNodeThatPrematurely_KillsChildren.SimulationPorts.ForwardedOutput, rhs, InOutTestNode.SimulationPorts.Input));
                Assert.Throws<InvalidOperationException>(() => set.Connect(rhs, InOutTestNode.SimulationPorts.Output, uberA, UberNodeThatPrematurely_KillsChildren.SimulationPorts.ForwardedInput));

                set.Destroy(uberA, rhs);
            }
        }

        public class UberNode_ThatForwardsPortTwice
            : SimulationNodeDefinition<UberNode_ThatForwardsPortTwice.SimPorts>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
                public MessageInput<UberNode_ThatForwardsPortTwice, Message> ForwardedInput;
            }

            struct Data : INodeData, IInit, IDestroy, IMsgHandler<Message>
            {
                public NodeHandle<InOutTestNode> Child;

                public void Init(InitContext ctx)
                {
                    Child = ctx.Set.Create<InOutTestNode>();
                    ctx.ForwardInput(SimulationPorts.ForwardedInput, Child, InOutTestNode.SimulationPorts.Input);

                    try
                    {
                        ctx.ForwardInput(SimulationPorts.ForwardedInput, Child, InOutTestNode.SimulationPorts.Input);
                    }
                    catch
                    {
                        Debug.Log("All is good");
                    }
                }

                public void Destroy(DestroyContext ctx) => ctx.Set.Destroy(Child);
                public void HandleMessage(MessageContext ctx, in Message msg) => throw new NotImplementedException();
            }
        }

        [Test]
        public void ForwardingPort_MoreThanOnce_ThrowsArgumentException()
        {
            using (var set = new NodeSet())
            {
                LogAssert.Expect(LogType.Log, "All is good");
                var node = set.Create<UberNode_ThatForwardsPortTwice>();
                set.Destroy(node);
            }
        }

        public class AllIsGoodException : Exception { }

        public class AllIsGoodMessageEndPoint : SimulationNodeDefinition<AllIsGoodMessageEndPoint.SimPorts>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
                public MessageInput<AllIsGoodMessageEndPoint, Message> Input;
            }

            struct Node : INodeData, IMsgHandler<Message>
            {
                public void HandleMessage(MessageContext ctx, in Message msg)
                {
                    if (ctx.Port == SimulationPorts.Input)
                        throw new AllIsGoodException();
                }
            }
        }

        [Test]
        public void SendingMessages_ToForwardedPorts_AreForwardedAsExpected()
        {
            using (var set = new NodeSet())
            {
                var nestedMiddle = set.Create<NestedUberNodeMiddle>();
                var child = set.GetDefinition(nestedMiddle).ExposeData(nestedMiddle).Child;
                var endPoint = set.Create<AllIsGoodMessageEndPoint>();

                set.Connect(child, InOutTestNode.SimulationPorts.Output, endPoint, AllIsGoodMessageEndPoint.SimulationPorts.Input);

                Assert.Throws<AllIsGoodException>(() => set.SendMessage(nestedMiddle, NestedUberNodeMiddle.SimulationPorts.ForwardedInput, new Message(10)));

                set.Destroy(nestedMiddle, endPoint);
            }
        }


        public class UberNodeWithDataForwarding
            : SimulationKernelNodeDefinition<UberNodeWithDataForwarding.SimPorts, UberNodeWithDataForwarding.KernelDefs>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
            }

            public struct KernelDefs : IKernelPortDefinition
            {
                public DataInput<UberNodeWithDataForwarding, int> ForwardedDataInput;
                public DataOutput<UberNodeWithDataForwarding, Buffer<int>> ForwardedDataOutputBuffer;
            }

            struct KernelData : IKernelData { }

            [BurstCompile(CompileSynchronously = true)]
            struct Kernel : IGraphKernel<KernelData, KernelDefs>
            {
                public void Execute(RenderContext ctx, in KernelData data, ref KernelDefs ports)
                {
                    throw new NotImplementedException();
                }
            }

            public struct Data : INodeData, IInit, IDestroy, IMsgHandler<Message>
            {
                public NodeHandle<NodeWithAllTypesOfPorts> Child;

                public void HandleMessage(MessageContext ctx, in Message msg) => throw new NotImplementedException();

                public void Init(InitContext ctx)
                {
                    Child = ctx.Set.Create<NodeWithAllTypesOfPorts>();

                    ctx.ForwardInput(KernelPorts.ForwardedDataInput, Child, NodeWithAllTypesOfPorts.KernelPorts.InputScalar);
                    ctx.ForwardOutput(KernelPorts.ForwardedDataOutputBuffer, Child, NodeWithAllTypesOfPorts.KernelPorts.OutputBuffer);
                }

                public void Destroy(DestroyContext ctx)
                {
                    ctx.Set.Destroy(Child);
                }
            }
        }

        [Test]
        public void SetData_ToForwardedPorts_AreForwardedAsExpected()
        {
            using (var set = new NodeSet())
            {
                var uber = set.Create<UberNodeWithDataForwarding>();

                set.SetData(uber, UberNodeWithDataForwarding.KernelPorts.ForwardedDataInput, 1);

                var diff = set.GetCurrentGraphDiff();

                Assert.AreEqual(diff.MessagesArrivingAtDataPorts.Count, 1);

                var message = diff.MessagesArrivingAtDataPorts[0];

                set.SendTest(uber, (UberNodeWithDataForwarding.Data data) =>
                    Assert.AreEqual(message.Destination.Handle.ToPublicHandle(), (NodeHandle)data.Child));
                Assert.AreEqual((InputPortID)NodeWithAllTypesOfPorts.KernelPorts.InputScalar, message.Destination.Port.PortID);

                set.Destroy(uber);
            }
        }

        [Test]
        public void SetBufferSize_OnForwardedPorts_AreForwardedAsExpected()
        {
            using (var set = new NodeSet())
            {
                var uber = set.Create<UberNodeWithDataForwarding>();

                set.SetBufferSize(uber, UberNodeWithDataForwarding.KernelPorts.ForwardedDataOutputBuffer, Buffer<int>.SizeRequest(10));

                var diff = set.GetCurrentGraphDiff();

                Assert.AreEqual(diff.ResizedDataBuffers.Count, 1);

                var resize = diff.ResizedDataBuffers[0];

                set.SendTest(uber, (UberNodeWithDataForwarding.Data data) =>
                    Assert.AreEqual((NodeHandle)data.Child, resize.Handle.ToPublicHandle()));
                Assert.AreEqual((OutputPortID)NodeWithAllTypesOfPorts.KernelPorts.OutputBuffer, resize.Port.PortID);

                set.Destroy(uber);
            }
        }

        [Test]
        public void DisconnectAndRetainValue_OnForwardedPorts_AreForwardedAsExpected()
        {
            using (var set = new NodeSet())
            {
                var uber = set.Create<UberNodeWithDataForwarding>();
                var rhs = set.Create<NodeWithAllTypesOfPorts>();

                set.Connect(rhs, NodeWithAllTypesOfPorts.KernelPorts.OutputScalar, uber, UberNodeWithDataForwarding.KernelPorts.ForwardedDataInput);

                set.DisconnectAndRetainValue(rhs, NodeWithAllTypesOfPorts.KernelPorts.OutputScalar, uber, UberNodeWithDataForwarding.KernelPorts.ForwardedDataInput);

                var diff = set.GetCurrentGraphDiff();

                Assert.AreEqual(diff.MessagesArrivingAtDataPorts.Count, 1);

                var message = diff.MessagesArrivingAtDataPorts[0];

                set.SendTest(uber, (UberNodeWithDataForwarding.Data data) =>
                    Assert.AreEqual(message.Destination.Handle.ToPublicHandle(), (NodeHandle)data.Child));
                Assert.AreEqual((InputPortID)NodeWithAllTypesOfPorts.KernelPorts.InputScalar, message.Destination.Port.PortID);

                set.Destroy(uber, rhs);
            }
        }

        [Test]
        public unsafe void GraphValues_AreForwarded_AsExpected()
        {
            using (var set = new NodeSet())
            {
                var uber = set.Create<UberNodeWithDataForwarding>();

                var gv = set.CreateGraphValue(uber, UberNodeWithDataForwarding.KernelPorts.ForwardedDataOutputBuffer);
                var values = set.GetOutputValues();

                var value = values[gv.Handle];

                set.SendTest(uber, (UberNodeWithDataForwarding.Data data) =>
                    Assert.AreEqual((NodeHandle)data.Child, value.Source.ToPublicHandle()));

                ref readonly var traits = ref set.GetNodeTraits(value.Source.ToPublicHandle());
                ref readonly var outputPort = ref traits.DataPorts.FindOutputDataPort(NodeWithAllTypesOfPorts.KernelPorts.OutputBuffer.Port);
                Assert.True(UnsafeUtilityExtensions.AddressOf(outputPort) == value.OutputDeclaration);

                set.ReleaseGraphValue(gv);
                set.Destroy(uber);
            }
        }

        [Test]
        public void CanForward_AfterDestruction()
        {
            using (var set = new NodeSet())
            {
                var child = set.Create<InOutTestNode>();
                var node = set.Create<DelegateMessageIONode>(
                    (InitContext ctx) =>
                    {
                        set.Destroy(ctx.Handle);
                        ctx.ForwardOutput(DelegateMessageIONode.SimulationPorts.Output, child, InOutTestNode.SimulationPorts.Output);
                    }
                );
                set.Destroy(child);
            }
        }

        public class UberNode_ThatForwards_NonMonotonically
            : SimulationNodeDefinition<UberNode_ThatForwards_NonMonotonically.SimPorts>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
                public MessageInput<UberNode_ThatForwards_NonMonotonically, Message> ForwardedInput;
                public MessageInput<UberNode_ThatForwards_NonMonotonically, Message> ForwardedInput2;
            }

            struct Data : INodeData, IInit, IDestroy, IMsgHandler<Message>
            {
                public NodeHandle<InOutTestNode> Child;

                public void Init(InitContext ctx)
                {
                    Child = ctx.Set.Create<InOutTestNode>();
                    ctx.ForwardInput(SimulationPorts.ForwardedInput2, Child, InOutTestNode.SimulationPorts.Input);

                    try
                    {
                        ctx.ForwardInput(SimulationPorts.ForwardedInput, Child, InOutTestNode.SimulationPorts.Input);
                    }
                    catch
                    {
                        Debug.Log("All is good");
                    }

                }

                public void Destroy(DestroyContext ctx) => ctx.Set.Destroy(Child);
                public void HandleMessage(MessageContext ctx, in Message msg) => throw new NotImplementedException();
            }
        }

        [Test]
        public void ForwardingPorts_NonMonotonically_ThrowsError_AtCallSite()
        {
            using (var set = new NodeSet())
            {
                LogAssert.Expect(LogType.Log, "All is good");
                var node = set.Create<UberNode_ThatForwards_NonMonotonically>();
                set.Destroy(node);
            }
        }

        [IsNotInstantiable]
        class ForwarderECSNode : KernelNodeDefinition<ForwarderECSNode.KernelDefs>
        {
            struct KernelData : IKernelData { }

            public struct KernelDefs : IKernelPortDefinition
            {
#pragma warning disable 649
                public DataInput<ForwarderECSNode, SimpleData> Input;
                public DataOutput<ForwarderECSNode, SimpleData> Output;
#pragma warning restore 649
            }

            struct GraphKernel : IGraphKernel<KernelData, KernelDefs>
            {
                public void Execute(RenderContext ctx, in KernelData data, ref KernelDefs ports) { }
            }

            internal struct Data : INodeData, IInit, IDestroy
            {
                public Entity Entity;
                public NodeHandle<ComponentNode> Child;

                public void Init(InitContext ctx)
                {
                    // API not available to normal users... In fact, it's hard to imagine how you would port forward to an entity.
                    Entity = ctx.Set.HostSystem.EntityManager.CreateEntity(typeof(SimpleData));
                    Child = ctx.Set.CreateComponentNode(Entity);

                    ctx.ForwardInput(KernelPorts.Input, Child, ComponentNode.Input<SimpleData>());
                    ctx.ForwardOutput(KernelPorts.Output, Child, ComponentNode.Output<SimpleData>());
                }

                public void Destroy(DestroyContext ctx)
                {
                    ctx.Set.Destroy(Child);
                }
            }
        }

        [Test]
        public void CanPortForward_InputsAndOutputs_OfEntityNodes_AndPipeDataCompletelyThrough([Values] FixtureSystemType systemType)
        {
            using (var f = new Fixture<UpdateSystemDelegate>(systemType))
            {
                var set = f.Set;

                var uber = f.Set.Create<ForwarderECSNode>();
                var end = f.Set.Create<PassthroughTest<SimpleData>>();
                var start = f.Set.Create<PassthroughTest<SimpleData>>();

                set.Connect(start, PassthroughTest<SimpleData>.KernelPorts.Output, uber, ForwarderECSNode.KernelPorts.Input);
                set.Connect(uber, ForwarderECSNode.KernelPorts.Output, end, PassthroughTest<SimpleData>.KernelPorts.Input);

                var gv = f.Set.CreateGraphValue(end, PassthroughTest<SimpleData>.KernelPorts.Output);

                set.SendTest(uber, (ForwarderECSNode.Data data) =>
                    Assert.That(f.EM.HasComponent<SimpleData>(data.Entity)));

                for(int i = 0; i < 10; ++i)
                {
                    set.SetData(start, PassthroughTest<SimpleData>.KernelPorts.Input, new SimpleData { Something = i, SomethingElse = i });

                    f.System.Update();

                    set.SendTest(uber, (ForwarderECSNode.Data data) =>
                    {
                        var ecsData = f.EM.GetComponentData<SimpleData>(data.Entity);
                        Assert.AreEqual(i, (int) ecsData.SomethingElse);
                        Assert.AreEqual(i, (int) ecsData.Something);
                    });

                    var dfgData = set.GetValueBlocking(gv);
                    Assert.AreEqual(i, (int)dfgData.SomethingElse);
                    Assert.AreEqual(i, (int)dfgData.Something);
                }

                set.Destroy(uber, start, end);
                set.ReleaseGraphValue(gv);
            }
        }
    }
}
