using System.Linq;
using NUnit.Framework;

namespace Unity.DataFlowGraph.Tests
{
    public class NodeDebuggerTests
    {
        NodeHandleDebugView.FullDebugInfo GetDebugInfo<TNode>(NodeHandle<TNode> handle)
            where TNode : NodeDefinition
        {
            NodeHandleDebugView<TNode> debugger = default;
            Assert.DoesNotThrow(() => debugger = new NodeHandleDebugView<TNode>(handle));
            Assert.IsInstanceOf(typeof(NodeHandleDebugView.FullDebugInfo), debugger.DebugInfo);
            return (NodeHandleDebugView.FullDebugInfo)debugger.DebugInfo;
        }

        [Test]
        public void CanInstantiateNodeDebugger_ForNodeWithManyPorts()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<NodeWithAllTypesOfPorts>();
                GetDebugInfo(node);
                set.Destroy(node);
            }
        }

        [Test]
        public void CanMakeNodeDebugger_FromComponentNode([Values] ComponentNodeSetTests.FixtureSystemType systemType)
        {
            using (var f = new ComponentNodeSetTests.Fixture<ComponentNodeSetTests.UpdateSystemDelegate>(systemType))
            {
                var entity = f.EM.CreateEntity();
                var componentNode = f.Set.CreateComponentNode(entity);

                GetDebugInfo(componentNode);

                f.Set.Destroy(componentNode);
            }
        }

        [Test]
        public void NodeDebugger_SeesPorts_OnComponentNodes_ThatHaveThoseConnections([Values] ComponentNodeSetTests.FixtureSystemType systemType)
        {
            using (var f = new ComponentNodeSetTests.Fixture<ComponentNodeSetTests.UpdateSystemDelegate>(systemType))
            {
                var entity = f.EM.CreateEntity(typeof(ComponentNodeSetTests.DataOne));
                var componentNode = f.Set.CreateComponentNode(entity);
                var otherNode = f.Set.Create<ComponentNodeSetTests.SimpleNode_WithECSTypes_OnInputs>();

                var ecsStrongPort = ComponentNode.Output<ComponentNodeSetTests.DataOne>();
                var weakPort = (OutputPortID) ecsStrongPort;
                f.Set.Connect(
                    componentNode,
                    ecsStrongPort,
                    otherNode,
                    ComponentNodeSetTests.SimpleNode_WithECSTypes_OnInputs.KernelPorts.Input
                );

                var debug = GetDebugInfo(componentNode);
                var ecsPortDescription = debug.OutputPorts.Array.Single(p => p.Description == weakPort);

                Assert.AreEqual(
                    f.Set.GetVirtualPort(new OutputPair(f.Set, componentNode, new OutputPortArrayID(weakPort))),
                    ecsPortDescription.Description
                );

                f.Set.Destroy(otherNode, componentNode);
            }
        }

        [Test]
        public void NodeDebugger_SeesForwardedPorts()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<PortForwardingTests.UberNodeWithDataForwarding>();
                var debug = GetDebugInfo(node);

                Assert.AreEqual(1, debug.ForwardedInputs.Array.Length);
                Assert.AreEqual(1, debug.ForwardedOutputs.Array.Length);

                Assert.AreEqual(
                    (InputPortID)PortForwardingTests.UberNodeWithDataForwarding.KernelPorts.ForwardedDataInput,
                    debug.ForwardedInputs.Array[0].OriginPort
                );

                Assert.AreEqual(
                    (OutputPortID)PortForwardingTests.UberNodeWithDataForwarding.KernelPorts.ForwardedDataOutputBuffer,
                    debug.ForwardedOutputs.Array[0].OriginPort
                );

                set.Destroy(node);
            }
        }

        [Test]
        public void NodeDebugger_SeesPrivatePorts()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<PortAPITests.NodeWithSomeNonPublicPorts>();
                var debug = GetDebugInfo(node);
                var def = set.GetDefinition<PortAPITests.NodeWithSomeNonPublicPorts>();
                Assert.AreEqual(def.AutoPorts.Inputs.Count, debug.InputPorts.Array.Length);
                Assert.AreEqual(def.AutoPorts.Outputs.Count, debug.OutputPorts.Array.Length);

                set.Destroy(node);
            }
        }
    }
}
