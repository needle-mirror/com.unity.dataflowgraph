using System;
using NUnit.Framework;
using Unity.Collections;
using Unity.DataFlowGraph.Detail;

namespace Unity.DataFlowGraph.Tests
{
    using Topology = TopologyAPI<ValidatedHandle, InputPortArrayID, OutputPortArrayID>;

    static class NodeSetTestExtensions
    {
        public static Topology.Connection LookupConnection<TLhs, TRhs>(this NodeSet set, in TLhs lhs, in TRhs rhs)
            where TLhs : struct, IOutputEndpoint
            where TRhs : struct, IInputEndpoint
        {
            var left = new OutputPair(set, lhs.Endpoint);
            var right = new InputPair(set, rhs.Endpoint);

            var map = set.GetTopologyMap_ForTesting();
            return set.GetTopologyDatabase_ForTesting().FindConnection(ref map, left.Handle, left.Port, right.Handle, right.Port);
        }

        public static void AssertEndpoints_AndConnectionExistence<TLhs, TRhs>(this NodeSet set, bool shouldExist, in TLhs lhs, in TRhs rhs)
            where TLhs : struct, IOutputEndpoint
            where TRhs : struct, IInputEndpoint
        {
            var left = new OutputPair(set, lhs.Endpoint);
            var right = new InputPair(set, rhs.Endpoint);

            var map = set.GetTopologyMap_ForTesting();
            var connection = set.GetTopologyDatabase_ForTesting().FindConnection(ref map, left.Handle, left.Port, right.Handle, right.Port);

            Assert.AreEqual(shouldExist, connection.Valid);

            var leftDescript = set.GetVirtualPort(left);
            var rightDescript = set.GetVirtualPort(right);

            if (!lhs.IsWeak && !rhs.IsWeak)
            {
                Assert.AreEqual(leftDescript.Category, lhs.Category);
                Assert.AreEqual(TypeHash.CreateSlow(rightDescript.Type), lhs.Type);
                Assert.AreEqual(leftDescript.PortID, lhs.Endpoint.PortID.PortID);
                Assert.AreEqual(leftDescript.IsPortArray, lhs.Endpoint.PortID.IsArray);

                Assert.AreEqual(rightDescript.Category, rhs.Category);
                Assert.AreEqual(TypeHash.CreateSlow(rightDescript.Type), rhs.Type);
                Assert.AreEqual(rightDescript.PortID, rhs.Endpoint.PortID.PortID);
                Assert.AreEqual(rightDescript.IsPortArray, rhs.Endpoint.PortID.IsArray);
            }

            if (shouldExist)
            {
                uint flags = (uint)leftDescript.Category;
                if(leftDescript.Category == PortDescription.Category.Message && rightDescript.Category == PortDescription.Category.Data)
                    flags = PortDescription.MessageToDataConnectionCategory;

                Assert.AreEqual(flags, connection.TraversalFlags);

                Assert.AreEqual(connection.Source, left.Handle);
                Assert.AreEqual(connection.SourceOutputPort, left.Port);
                Assert.AreEqual(connection.Destination, right.Handle);
                Assert.AreEqual(connection.DestinationInputPort, right.Port);
            }
        }
    }

    public class EndpointTests
    {
        [Test]
        public void Endpoints_HaveExpectedCategory()
        {
            Assert.AreEqual(PortDescription.Category.Data, new DataInputEndpoint<int>().Category);
            Assert.AreEqual(PortDescription.Category.Data, new DataOutputEndpoint<int>().Category);
            Assert.AreEqual(PortDescription.Category.Message, new MessageInputEndpoint<int>().Category);
            Assert.AreEqual(PortDescription.Category.Message, new MessageOutputEndpoint<int>().Category);
            Assert.AreEqual(PortDescription.Category.DomainSpecific, new DSLInputEndpoint<DSL>().Category);
            Assert.AreEqual(PortDescription.Category.DomainSpecific, new DSLOutputEndpoint<DSL>().Category);

            PortDescription.Category category;
            Assert.Throws<InvalidOperationException>(() => category = new InputEndpoint().Category);
            Assert.Throws<InvalidOperationException>(() => category = new OutputEndpoint().Category);
        }

        [Test]
        public void Endpoints_HaveExpectedType()
        {
            Assert.AreEqual(TypeHash.Create<byte>(), new DataInputEndpoint<byte>().Type);
            Assert.AreEqual(TypeHash.Create<short>(), new DataOutputEndpoint<short>().Type);
            Assert.AreEqual(TypeHash.Create<int>(), new MessageInputEndpoint<int>().Type);
            Assert.AreEqual(TypeHash.Create<long>(), new MessageOutputEndpoint<long>().Type);
            Assert.AreEqual(DSLTypeMap.StaticHashNoRegister<DSL>(), new DSLInputEndpoint<DSL>().Type);
            Assert.AreEqual(DSLTypeMap.StaticHashNoRegister<DSL>(), new DSLOutputEndpoint<DSL>().Type);

            TypeHash typeHash;
            Assert.Throws<InvalidOperationException>(() => typeHash = new InputEndpoint().Type);
            Assert.Throws<InvalidOperationException>(() => typeHash = new OutputEndpoint().Type);
        }

        [Test]
        public void Endpoints_AreTaggedWeakStrong()
        {
            Assert.IsFalse(new DataInputEndpoint<int>().IsWeak);
            Assert.IsFalse(new DataOutputEndpoint<int>().IsWeak);
            Assert.IsFalse(new MessageInputEndpoint<int>().IsWeak);
            Assert.IsFalse(new MessageOutputEndpoint<int>().IsWeak);
            Assert.IsFalse(new DSLInputEndpoint<DSL>().IsWeak);
            Assert.IsFalse(new DSLOutputEndpoint<DSL>().IsWeak);

            Assert.IsTrue(new InputEndpoint().IsWeak);
            Assert.IsTrue(new OutputEndpoint().IsWeak);
        }

        [Test]
        public void WeakEndpoints_CanBeConstructed_FromStrong()
        {
            var inputPair = new UnresolvedInputPair(new NodeHandle(VersionedHandle.Create_ForTesting(1,2,3)), new InputPortArrayID());
            var outputPair = new UnresolvedOutputPair(new NodeHandle(VersionedHandle.Create_ForTesting(3,2,1)), new OutputPortArrayID());

            Assert.AreEqual(inputPair, ((InputEndpoint)new DataInputEndpoint<int>(inputPair)).Endpoint);
            Assert.AreEqual(outputPair, ((OutputEndpoint)new DataOutputEndpoint<int>(outputPair)).Endpoint);
            Assert.AreEqual(inputPair, ((InputEndpoint)new MessageInputEndpoint<int>(inputPair)).Endpoint);
            Assert.AreEqual(outputPair, ((OutputEndpoint)new MessageOutputEndpoint<int>(outputPair)).Endpoint);
            Assert.AreEqual(inputPair, ((InputEndpoint)new DSLInputEndpoint<DSL>(inputPair)).Endpoint);
            Assert.AreEqual(outputPair, ((OutputEndpoint)new DSLOutputEndpoint<DSL>(outputPair)).Endpoint);
        }

        [Test]
        public void Endpoints_CanBeCreated_FromNodeHandle_TyingAPI()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<NodeWithAllTypesOfPorts>();

                Assert.AreEqual(
                    new UnresolvedInputPair(node, new InputPortArrayID((InputPortID)NodeWithAllTypesOfPorts.KernelPorts.InputScalar)),
                    node.Tie(NodeWithAllTypesOfPorts.KernelPorts.InputScalar).Endpoint);

                Assert.AreEqual(
                    new UnresolvedOutputPair(node, new OutputPortArrayID((OutputPortID)NodeWithAllTypesOfPorts.KernelPorts.OutputScalar)),
                    node.Tie(NodeWithAllTypesOfPorts.KernelPorts.OutputScalar).Endpoint);

                Assert.AreEqual(
                    new UnresolvedInputPair(node, new InputPortArrayID((InputPortID)NodeWithAllTypesOfPorts.SimulationPorts.MessageIn)),
                    node.Tie(NodeWithAllTypesOfPorts.SimulationPorts.MessageIn).Endpoint);

                Assert.AreEqual(
                    new UnresolvedOutputPair(node, new OutputPortArrayID((OutputPortID)NodeWithAllTypesOfPorts.SimulationPorts.MessageOut)),
                    node.Tie(NodeWithAllTypesOfPorts.SimulationPorts.MessageOut).Endpoint);

                Assert.AreEqual(
                    new UnresolvedInputPair(node, new InputPortArrayID((InputPortID)NodeWithAllTypesOfPorts.SimulationPorts.DSLIn)),
                    node.Tie(NodeWithAllTypesOfPorts.SimulationPorts.DSLIn).Endpoint);

                Assert.AreEqual(
                    new UnresolvedOutputPair(node, new OutputPortArrayID((OutputPortID)NodeWithAllTypesOfPorts.SimulationPorts.DSLOut)),
                    node.Tie(NodeWithAllTypesOfPorts.SimulationPorts.DSLOut).Endpoint);

                set.Destroy(node);
            }
        }

        public enum ConnectScenario
        {
            Data,
            DataWithDisconnectAndRetainValue,
            Message,
            MessageToData,
            DSL
        }

        static void TestEndpoints<TLhs, TRhs>(NodeSet set, in TLhs lhs, in TRhs rhs, bool disconnectAndRetain = false)
            where TLhs : struct, IOutputEndpoint, IConnectableWith<TRhs>, IConcrete
            where TRhs : struct, IInputEndpoint, IConnectableWith<TLhs>, IConcrete
        {
            set.Connect(lhs, rhs);
            set.AssertEndpoints_AndConnectionExistence(shouldExist: true, lhs, rhs);

            if(disconnectAndRetain)
            {
                set.DisconnectAndRetainValue(lhs, rhs);
                Assert.AreEqual(new InputPair(set, rhs.Endpoint), set.GetCurrentGraphDiff().MessagesArrivingAtDataPorts[0].Destination);
            }
            else
            {
                set.Disconnect(lhs, rhs);
            }
        }

        [Test]
        public void CanConnectAndDisconnect_TypedEndpoints([Values] APIType type, [Values] ConnectScenario scenario)
        {
            using (var set = new NodeSet())
            {
                NodeHandle<NodeWithAllTypesOfPorts>
                    a = set.Create<NodeWithAllTypesOfPorts>(),
                    b = set.Create<NodeWithAllTypesOfPorts>();

                switch (scenario)
                {
                    case ConnectScenario.Data:
                    {
                        var lhs = a.Tie(NodeWithAllTypesOfPorts.KernelPorts.OutputScalar);
                        var rhs = b.Tie(NodeWithAllTypesOfPorts.KernelPorts.InputScalar);
                        if (type == APIType.StronglyTyped)
                            TestEndpoints(set, lhs, rhs);
                        else
                            TestEndpoints(set, (OutputEndpoint)lhs, (InputEndpoint)rhs);
                        break;
                    }

                    case ConnectScenario.DataWithDisconnectAndRetainValue:
                    {
                        var lhs = a.Tie(NodeWithAllTypesOfPorts.KernelPorts.OutputScalar);
                        var rhs = b.Tie(NodeWithAllTypesOfPorts.KernelPorts.InputScalar);
                        if (type == APIType.StronglyTyped)
                            TestEndpoints(set, lhs, rhs, disconnectAndRetain: true);
                        else
                            TestEndpoints(set, (OutputEndpoint)lhs, (InputEndpoint)rhs, disconnectAndRetain: true);
                        break;
                    }

                    case ConnectScenario.Message:
                    {
                        var lhs = a.Tie(NodeWithAllTypesOfPorts.SimulationPorts.MessageOut);
                        var rhs = b.Tie(NodeWithAllTypesOfPorts.SimulationPorts.MessageIn);
                        if (type == APIType.StronglyTyped)
                            TestEndpoints(set, lhs, rhs);
                        else
                            TestEndpoints(set, (OutputEndpoint)lhs, (InputEndpoint)rhs);
                        break;
                    }

                    case ConnectScenario.MessageToData:
                    {
                        var lhs = a.Tie(NodeWithAllTypesOfPorts.SimulationPorts.MessageOut);
                        var rhs = b.Tie(NodeWithAllTypesOfPorts.KernelPorts.InputScalar);
                        if (type == APIType.StronglyTyped)
                            TestEndpoints(set, lhs, rhs);
                        else
                            TestEndpoints(set, (OutputEndpoint)lhs, (InputEndpoint)rhs);
                        break;
                    }

                    case ConnectScenario.DSL:
                    {
                        var lhs = a.Tie(NodeWithAllTypesOfPorts.SimulationPorts.DSLOut);
                        var rhs = b.Tie(NodeWithAllTypesOfPorts.SimulationPorts.DSLIn);
                        if (type == APIType.StronglyTyped)
                            TestEndpoints(set, lhs, rhs);
                        else
                            TestEndpoints(set, (OutputEndpoint)lhs, (InputEndpoint)rhs);
                        break;
                    }
                }

                set.Destroy(a, b);
            }
        }


    }
}
