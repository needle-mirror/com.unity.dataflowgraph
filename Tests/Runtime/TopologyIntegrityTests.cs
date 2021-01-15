using System.Linq;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;

namespace Unity.DataFlowGraph.Tests
{
    using Topology = TopologyAPI<ValidatedHandle, InputPortArrayID, OutputPortArrayID>;

    public class TopologyIntegrityTests
    {
        // TODO: Check linked list integrity of connection table

        public struct Node : INodeData
        {
            public int Contents;
        }


        public struct Data : IKernelData { }

        [Test]
        public void TopologyVersionIncreases_OnCreatingNodes()
        {
            using (var set = new NodeSet())
            {
                var version = set.TopologyVersion.Version;
                var node = set.Create<NodeWithAllTypesOfPorts>();
                Assert.Greater(set.TopologyVersion.Version, version);
                set.Destroy(node);
            }
        }

        [Test]
        public void TopologyVersionIncreases_OnDestroyingNodes()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<NodeWithAllTypesOfPorts>();
                var version = set.TopologyVersion.Version;
                set.Destroy(node);
                Assert.Greater(set.TopologyVersion.Version, version);
            }
        }

        [Test]
        public void TopologyVersionIncreases_OnCreatingConnections()
        {
            using (var set = new NodeSet())
            {
                NodeHandle<NodeWithAllTypesOfPorts>
                    a = set.Create<NodeWithAllTypesOfPorts>(),
                    b = set.Create<NodeWithAllTypesOfPorts>();

                var version = set.TopologyVersion.Version;
                set.Connect(a, NodeWithAllTypesOfPorts.SimulationPorts.MessageOut, b, NodeWithAllTypesOfPorts.SimulationPorts.MessageIn);

                Assert.Greater(set.TopologyVersion.Version, version);

                set.Destroy(a, b);
            }
        }

        [Test]
        public void TopologyVersionIncreases_OnDestroyingConnections()
        {
            using (var set = new NodeSet())
            {
                NodeHandle<NodeWithAllTypesOfPorts>
                    a = set.Create<NodeWithAllTypesOfPorts>(),
                    b = set.Create<NodeWithAllTypesOfPorts>();

                set.Connect(a, NodeWithAllTypesOfPorts.SimulationPorts.MessageOut, b, NodeWithAllTypesOfPorts.SimulationPorts.MessageIn);
                var version = set.TopologyVersion.Version;
                set.Disconnect(a, NodeWithAllTypesOfPorts.SimulationPorts.MessageOut, b, NodeWithAllTypesOfPorts.SimulationPorts.MessageIn);

                Assert.Greater(set.TopologyVersion.Version, version);

                set.Destroy(a, b);
            }
        }

        [Test]
        public void MessageConnectionsMade_CausesConnectionTable_ToBePopulatedCorrectly_AndSubsequentlyRemoved([Values] APIType meansOfConnection)
        {
            using (var set = new NodeSet())
            {
                NodeHandle<NodeWithAllTypesOfPorts>
                    a = set.Create<NodeWithAllTypesOfPorts>(),
                    b = set.Create<NodeWithAllTypesOfPorts>();

                NodeHandle untypedA = a, untypedB = b;

                Assert.AreEqual(0, set.GetTopologyDatabase_ForTesting().CountEstablishedConnections(), "There are valid connections in a new set with zero connections");

                if (meansOfConnection == APIType.StronglyTyped)
                    set.Connect(a, NodeWithAllTypesOfPorts.SimulationPorts.MessageOut, b, NodeWithAllTypesOfPorts.SimulationPorts.MessageIn);
                else
                    set.Connect(a, set.GetDefinition(a).GetPortDescription(a).Outputs[0], b, set.GetDefinition(b).GetPortDescription(b).Inputs[0]);

                Assert.AreEqual(1, set.GetTopologyDatabase_ForTesting().CountEstablishedConnections(), "There isn't exactly one valid edge in a new set with one connection");

                var madeConnection = new Topology.Connection();

                Assert.IsFalse(madeConnection.Valid, "Default constructed connection is valid");

                int indexHandleCounter = 0, foundIndexHandle = 0;
                for(int i = 0; i < set.GetTopologyDatabase_ForTesting().TotalConnections; ++i)
                {
                    if (set.GetTopologyDatabase_ForTesting()[i].Valid)
                    {
                        madeConnection = set.GetTopologyDatabase_ForTesting()[i];
                        foundIndexHandle = indexHandleCounter;
                    }
                    indexHandleCounter++;
                }

                Assert.IsTrue(madeConnection.Valid, "Could not find the made connection");
                Assert.NotZero(foundIndexHandle, "Found connection cannot be the invalid slot");

                // check the connection is as it should be
                Assert.AreEqual((uint)PortDescription.Category.Message, madeConnection.TraversalFlags);
                Assert.AreEqual(untypedB, madeConnection.Destination.ToPublicHandle());
                Assert.AreEqual(untypedA, madeConnection.Source.ToPublicHandle());
                Assert.AreEqual(NodeWithAllTypesOfPorts.SimulationPorts.MessageOut.Port, madeConnection.SourceOutputPort.PortID);
                Assert.AreEqual(NodeWithAllTypesOfPorts.SimulationPorts.MessageIn.Port, madeConnection.DestinationInputPort.PortID);
                Assert.AreEqual(foundIndexHandle, madeConnection.HandleToSelf.Index);

                if (meansOfConnection == APIType.StronglyTyped)
                    set.Disconnect(a, NodeWithAllTypesOfPorts.SimulationPorts.MessageOut, b, NodeWithAllTypesOfPorts.SimulationPorts.MessageIn);
                else
                    set.Disconnect(a, set.GetDefinition(a).GetPortDescription(a).Outputs[0], b, set.GetDefinition(b).GetPortDescription(b).Inputs[0]);

                Assert.AreEqual(0, set.GetTopologyDatabase_ForTesting().CountEstablishedConnections(), "There are valid connections in a new set with zero connections");

                set.Destroy(a, b);
            }
        }

        [Test]
        public void DSLConnectionsMade_CausesConnectionTable_ToBePopulatedCorrectly_AndSubsequentlyRemoved([Values] APIType meansOfConnection)
        {
            using (var set = new NodeSet())
            {
                NodeHandle<NodeWithAllTypesOfPorts>
                    a = set.Create<NodeWithAllTypesOfPorts>(),
                    b = set.Create<NodeWithAllTypesOfPorts>();

                NodeHandle untypedA = a, untypedB = b;

                Assert.AreEqual(0, set.GetTopologyDatabase_ForTesting().CountEstablishedConnections(), "There are valid connections in a new set with zero connections");

                if (meansOfConnection == APIType.StronglyTyped)
                    set.Connect(a, NodeWithAllTypesOfPorts.SimulationPorts.DSLOut, b, NodeWithAllTypesOfPorts.SimulationPorts.DSLIn);
                else
                    set.Connect(a, set.GetDefinition(a).GetPortDescription(a).Outputs[2], b, set.GetDefinition(b).GetPortDescription(b).Inputs[2]);

                Assert.AreEqual(1, set.GetTopologyDatabase_ForTesting().CountEstablishedConnections(), "There isn't exactly one valid edge in a new set with one connection");

                var madeConnection = new Topology.Connection();

                Assert.IsFalse(madeConnection.Valid, "Default constructed connection is valid");

                int indexHandleCounter = 0, foundIndexHandle = 0;
                for (int i = 0; i < set.GetTopologyDatabase_ForTesting().TotalConnections; ++i)
                {
                    if (set.GetTopologyDatabase_ForTesting()[i].Valid)
                    {
                        madeConnection = set.GetTopologyDatabase_ForTesting()[i];
                        foundIndexHandle = indexHandleCounter;
                    }
                    indexHandleCounter++;
                }

                Assert.IsTrue(madeConnection.Valid, "Could not find the made connection");
                Assert.NotZero(foundIndexHandle, "Found connection cannot be the invalid slot");

                // check the connection is as it should be
                Assert.AreEqual((uint)PortDescription.Category.DomainSpecific, madeConnection.TraversalFlags);
                Assert.AreEqual(untypedB, madeConnection.Destination.ToPublicHandle());
                Assert.AreEqual(untypedA, madeConnection.Source.ToPublicHandle());
                Assert.AreEqual(NodeWithAllTypesOfPorts.SimulationPorts.DSLOut.Port, madeConnection.SourceOutputPort.PortID);
                Assert.AreEqual(NodeWithAllTypesOfPorts.SimulationPorts.DSLIn.Port, madeConnection.DestinationInputPort.PortID);
                Assert.AreEqual(foundIndexHandle, madeConnection.HandleToSelf.Index);

                if (meansOfConnection == APIType.StronglyTyped)
                {
                    set.Disconnect(a, NodeWithAllTypesOfPorts.SimulationPorts.DSLOut, b, NodeWithAllTypesOfPorts.SimulationPorts.DSLIn);
                }
                else
                    set.Disconnect(a, set.GetDefinition(a).GetPortDescription(a).Outputs[2], b, set.GetDefinition(b).GetPortDescription(b).Inputs[2]);

                Assert.AreEqual(0, set.GetTopologyDatabase_ForTesting().CountEstablishedConnections(), "There are valid connections in a new set with zero connections");

                set.Destroy(a, b);
            }
        }

        [Test]
        public void DataConnectionsMade_CausesConnectionTable_ToBePopulatedCorrectly_AndSubsequentlyRemoved([Values] APIType meansOfConnection)
        {
            using (var set = new NodeSet())
            {
                NodeHandle<NodeWithAllTypesOfPorts>
                    a = set.Create<NodeWithAllTypesOfPorts>(),
                    b = set.Create<NodeWithAllTypesOfPorts>();

                NodeHandle untypedA = a, untypedB = b;

                Assert.AreEqual(0, set.GetTopologyDatabase_ForTesting().CountEstablishedConnections(), "There are valid connections in a new set with zero connections");

                if (meansOfConnection == APIType.StronglyTyped)
                    set.Connect(a, NodeWithAllTypesOfPorts.KernelPorts.OutputScalar, b, NodeWithAllTypesOfPorts.KernelPorts.InputScalar);
                else
                    set.Connect(a, set.GetDefinition(a).GetPortDescription(a).Outputs[5], b, set.GetDefinition(b).GetPortDescription(b).Inputs[5]);

                Assert.AreEqual(1, set.GetTopologyDatabase_ForTesting().CountEstablishedConnections(), "There isn't exactly one valid edge in a new set with one connection");

                var madeConnection = new Topology.Connection();

                Assert.IsFalse(madeConnection.Valid, "Default constructed connection is valid");

                int indexHandleCounter = 0, foundIndexHandle = 0;
                for (int i = 0; i < set.GetTopologyDatabase_ForTesting().TotalConnections; ++i)
                {
                    if (set.GetTopologyDatabase_ForTesting()[i].Valid)
                    {
                        madeConnection = set.GetTopologyDatabase_ForTesting()[i];
                        foundIndexHandle = indexHandleCounter;
                    }
                    indexHandleCounter++;
                }

                Assert.IsTrue(madeConnection.Valid, "Could not find the made connection");
                Assert.NotZero(foundIndexHandle, "Found connection cannot be the invalid slot");

                // check the connection is as it should be
                Assert.AreEqual((uint)PortDescription.Category.Data, madeConnection.TraversalFlags);
                Assert.AreEqual(untypedB, madeConnection.Destination.ToPublicHandle());
                Assert.AreEqual(untypedA, madeConnection.Source.ToPublicHandle());
                // Fails for the same reason as MixedPortDeclarations_AreConsecutivelyNumbered_AndRespectsDeclarationOrder
                Assert.AreEqual(NodeWithAllTypesOfPorts.KernelPorts.InputScalar.Port, madeConnection.DestinationInputPort.PortID);
                Assert.AreEqual(NodeWithAllTypesOfPorts.KernelPorts.OutputScalar.Port, madeConnection.SourceOutputPort.PortID);
                Assert.AreEqual(foundIndexHandle, madeConnection.HandleToSelf.Index);

                if (meansOfConnection == APIType.StronglyTyped)
                {
                    set.Disconnect(a, NodeWithAllTypesOfPorts.KernelPorts.OutputScalar, b, NodeWithAllTypesOfPorts.KernelPorts.InputScalar);
                }
                else
                    set.Disconnect(a, set.GetDefinition(a).GetPortDescription(a).Outputs[5], b, set.GetDefinition(b).GetPortDescription(b).Inputs[5]);

                Assert.AreEqual(0, set.GetTopologyDatabase_ForTesting().CountEstablishedConnections(), "There are valid connections in a new set with zero connections");

                set.Destroy(a, b);
            }
        }

        [Test]
        public unsafe void AnalyseLiveNodes_GathersEveryChangedNode_InAffectedGroups()
        {
            const int k_Nodes = 16;
            const int k_Groups = 4;

            using (var map = new FlatTopologyMap(16, Allocator.TempJob))
            using (var knodes = new BlitList<RenderGraph.KernelNode>(0, Allocator.TempJob))
            using (var db = new Topology.Database(16, Allocator.TempJob))
            using (var gatheredNodes = new NativeList<ValidatedHandle>(Allocator.TempJob))
            {
                map.EnsureSize(k_Nodes);

                var indirectVersions = db.ChangedGroups;
                indirectVersions.Resize(k_Groups, NativeArrayOptions.ClearMemory);

                for (int i = 0; i < k_Nodes; ++i)
                {
                    var handle = ValidatedHandle.Create_ForTesting(VersionedHandle.Create_ForTesting(i, 0, 0));
                    RenderGraph.KernelNode knode = default;
                    knode.Instance = new KernelLayout().VirtualReconstruct((void*)0xDEADBEEF);
                    knode.Handle = handle;
                    knodes.Add(knode);

                    // split into groups
                    map.GetRef(handle).GroupID = i % k_Groups;
                }

                for(int i = 0; i < k_Groups; ++i)
                {
                    gatheredNodes.Clear();

                    for(int k = 0; k < k_Groups; ++k)
                        indirectVersions[k] = false;

                    indirectVersions[i] = true;

                    RenderGraph.AnalyseLiveNodes analyseJob;
                    analyseJob.Filter = db;
                    analyseJob.KernelNodes = knodes;
                    analyseJob.Map = map;
                    analyseJob.Marker = RenderGraph.Markers.AnalyseLiveNodes;
                    analyseJob.ChangedNodes = gatheredNodes;

                    analyseJob.Schedule(default).Complete();

                    Assert.AreEqual(k_Groups, gatheredNodes.Length, $"{i}");

                    gatheredNodes.AsArray().ToArray().ToList().ForEach(
                        n => Assert.AreEqual(map[n].GroupID, i)
                    );
                }
            }
        }
    }
}
