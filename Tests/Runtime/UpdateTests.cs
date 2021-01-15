using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.DataFlowGraph.Tests
{
    public class UpdateTests
    {

        public class NodeWithUpdateHandler : SimulationNodeDefinition<NodeWithUpdateHandler.MyPorts>
        {
            public struct MyPorts : ISimulationPortDefinition
            {
                public MessageInput<NodeWithUpdateHandler, bool> TriggerContinuousUpdate;
            }


            internal struct NodeData : INodeData, IUpdate, IMsgHandler<bool>
            {
                public int Calls;

                public void HandleMessage(MessageContext ctx, in bool msg)
                {
                    if (msg)
                        ctx.RegisterForUpdate();
                    else
                        ctx.RemoveFromUpdate();
                }

                public void Update(UpdateContext ctx)
                {
                    Calls++;
                }
            }
        }

        [Test]
        public void CanCall_CodeGenerated_UpdateHandler_AndOnlyGetsCalled_WhenRegistered_ForUpdate()
        {
            const int k_UpdateCalls = 10;

            using (var set = new NodeSet())
            {
                var node = set.Create<NodeWithUpdateHandler>();
                set.SendMessage(node, NodeWithUpdateHandler.SimulationPorts.TriggerContinuousUpdate, true);
                set.Update();

                for (int i = 0; i < k_UpdateCalls; ++i)
                {
                    set.SendTest<NodeWithUpdateHandler.NodeData>(node, ctx =>
                        Assert.AreEqual(i, ctx.NodeData.Calls));
                    set.Update();
                }

                set.SendMessage(node, NodeWithUpdateHandler.SimulationPorts.TriggerContinuousUpdate, false);
                // triggers one extra call due to the delayed effects of above statement
                // this is why there's a + 1 in following asserts.
                set.Update();

                for (int i = 0; i < k_UpdateCalls; ++i)
                {
                    set.SendTest(node, (NodeWithUpdateHandler.NodeData data) =>
                        Assert.AreEqual(k_UpdateCalls + 1,data.Calls));
                    set.Update();
                }

                set.SendMessage(node, NodeWithUpdateHandler.SimulationPorts.TriggerContinuousUpdate, true);
                set.Update();

                for (int i = 0; i < k_UpdateCalls; ++i)
                {
                    set.SendTest(node, (NodeWithUpdateHandler.NodeData data) =>
                        Assert.AreEqual(k_UpdateCalls + i + 1, data.Calls));
                    set.Update();
                }

                set.Destroy(node);
            }
        }


        [Test]
        public void CannotRegister_ForUpdateTwice_OrUnregister_Twice_InInit()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<DelegateMessageIONode>(
                    (InitContext ctx) =>
                    {
                        ctx.RegisterForUpdate();
                        Assert.Throws<InvalidOperationException>(() => ctx.RegisterForUpdate());
                        ctx.RemoveFromUpdate();
                        Assert.Throws<InvalidOperationException>(() => ctx.RemoveFromUpdate());
                        ctx.RegisterForUpdate();
                        Assert.Throws<InvalidOperationException>(() => ctx.RegisterForUpdate());
                    }
                );

                set.Destroy(node);
            }
        }

        [Test]
        public void CanRegisterInInit_CausingNotYetApplied_UpdateStatus_AndImmediateSelfTerminate()
        {
            using (var set = new NodeSet())
            {
                set.Create<DelegateMessageIONode>(
                    (InitContext ctx) =>
                    {
                        ctx.RegisterForUpdate();
                        ctx.Set.Destroy(ctx.Handle);
                    }
                );
            }
        }

        class Node_ThatWantsToRegisterForUpdate_ButDoesntImplementIUpdate : SimulationNodeDefinition<Node_ThatWantsToRegisterForUpdate_ButDoesntImplementIUpdate.EmptyPorts>
        {
            public struct EmptyPorts : ISimulationPortDefinition { }

            struct Node : INodeData, IInit
            {
                public void Init(InitContext ctx)
                {
                    Assert.Throws<InvalidNodeDefinitionException>(() => ctx.RegisterForUpdate());
                }
            }
        }

        [Test]
        public void CannotRegister_ForUpdate_IfNodeDoesNotImplement_IUpdate()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<Node_ThatWantsToRegisterForUpdate_ButDoesntImplementIUpdate>();
                set.Destroy(node);
            }
        }

        [Test]
        public void CannotRegister_ForUpdateTwice_OrUnregister_Twice_InUpdate()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<DelegateMessageIONode>(
                    (UpdateContext ctx) =>
                    {
                        var ctxCopy = ctx;
                        Assert.Throws<InvalidOperationException>(() => ctxCopy.RegisterForUpdate());
                        // covers removal of "properly" registrered update
                        ctx.RemoveFromUpdate();
                        Assert.Throws<InvalidOperationException>(() => ctxCopy.RemoveFromUpdate());
                        ctx.RegisterForUpdate();
                        Assert.Throws<InvalidOperationException>(() => ctxCopy.RegisterForUpdate());
                        // covers removal of "partial" registrered update
                        ctx.RemoveFromUpdate();
                        Assert.Throws<InvalidOperationException>(() => ctxCopy.RemoveFromUpdate());
                    }
                );

                // new-style update registration only happens after the next update
                set.Update();
                set.Update();
                set.Destroy(node);
            }
        }

        [Test]
        public void NodeThatRegistersForUpdate_HasExpectedUpdateState_ThroughLifeCycle()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<DelegateMessageIONode>((UpdateContext ctx) => { });

                ref var nodeData = ref set.GetInternalData()[node.VHandle];

                Assert.AreEqual((int)NodeSet.UpdateState.NotYetApplied, nodeData.UpdateIndex);

                set.Update();
                Assert.GreaterOrEqual((int)NodeSet.UpdateState.ValidUpdateOffset, nodeData.UpdateIndex);

                set.Destroy(node);
                Assert.AreEqual((int)NodeSet.UpdateState.InvalidUpdateIndex, nodeData.UpdateIndex);
            }
        }

        [Test]
        public void NonUpdatingNode_HasInvalidUpdateIndex()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<EmptyNode>();
                ref var nodeData = ref set.GetInternalData()[node.VHandle];
                Assert.AreEqual((int)NodeSet.UpdateState.InvalidUpdateIndex, nodeData.UpdateIndex);
                set.Destroy(node);
            }
        }


        [Test]
        public void RemovingFromUpdate_StopsUpdating()
        {
            using (var set = new NodeSet())
            {
                bool wasEvenCalled = false;
                var node = set.Create<DelegateMessageIONode<int>, int>(
                    (UpdateContext ctx, ref int counter) =>
                    {
                        ctx.RemoveFromUpdate();
                        Assert.Zero(counter);
                        counter++;
                        wasEvenCalled = true;
                    }
                );
                // new-style update registration only happens after the next update
                set.Update();

                for (int i = 0; i < 10; ++i)
                {
                    set.Update();
                    Assert.True(wasEvenCalled);
                }

                set.Destroy(node);
            }
        }


        [Test]
        public void StressTest_RegisteringAndRemovingFromUpdate_ThroughMessages_YieldExpectedNumber_OfUpdateCalls([Values(0xFFEE, 0x1E, 0xDD)] int seed)
        {
            const int k_Nodes = 30;
            const int k_Updates = 20 * 10;

            using (var set = new NodeSet())
            {
                var map = new Dictionary<NodeHandle, (bool Updating, int Counter, int Expected)>();
                var lookup = new List<NodeHandle<DelegateMessageIONode>>();

                for(int i = 0; i < k_Nodes; ++i)
                {
                    var node = set.Create<DelegateMessageIONode>(
                        (MessageContext ctx, in Message m) =>
                        {
                            if(m.Contents > 0)
                                ctx.RegisterForUpdate();
                            else
                                ctx.RemoveFromUpdate();
                        },
                        (UpdateContext ctx) =>
                        {
                            var current = map[ctx.Handle];
                            current.Counter++;
                            map[ctx.Handle] = current;
                        }
                    );

                    map.Add(node, (true, 0, 0));
                    lookup.Add(node);
                }

                set.Update();

                var rng = new Mathematics.Random((uint)seed);

                for (int i = 0; i < k_Updates; ++i)
                {
                    // update expected count for nodes (changes only take effect next loop)
                    for (int n = 0; n < k_Nodes; ++n)
                    {
                        var current = map[lookup[n]];

                        if (!current.Updating)
                            continue;

                        current.Expected++;

                        map[lookup[n]] = current;
                    }

                    // change 10 random nodes to potentially flip between updating
                    for (int u = 0; u < k_Updates / 10; ++u)
                    {
                        var index = (int)rng.NextUInt(k_Nodes);

                        var shouldUpdate = rng.NextBool();

                        var handle = lookup[index];

                        var current = map[handle];

                        if (current.Updating == shouldUpdate)
                            continue;

                        set.SendMessage(handle, DelegateMessageIONode.SimulationPorts.Input, new Message(shouldUpdate ? 1 : 0));
                        current.Updating = shouldUpdate;
                        map[handle] = current;
                    }

                    set.Update();

                    // verify counts
                    for (int n = 0; n < k_Nodes; ++n)
                    {
                        var current = map[lookup[n]];

                        Assert.AreEqual(current.Expected, current.Counter);
                    }
                }

                for (int i = 0; i < k_Nodes; ++i)
                {
                    set.Destroy(lookup[i]);
                }
            }
        }
    }
}
