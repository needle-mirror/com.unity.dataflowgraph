using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Entities;
using Unity.Jobs;
using static Unity.DataFlowGraph.Tests.ComponentNodeSetTests;
using static Unity.DataFlowGraph.Tests.PortForwardingTests;

namespace Unity.DataFlowGraph.Tests
{
#pragma warning disable 649 // non-public unassigned default value

    class ComponentNodeTests
    {
        [Test]
        public void CannotCreateComponentNode_FromNonECSSet()
        {
            using (var world = new World("test"))
            using (var set = new NodeSet())
            {
                Assert.Throws<NullReferenceException>(() => set.CreateComponentNode(world.EntityManager.CreateEntity()));
            }
        }

        [Test]
        public void CannotCreateComponentNode_FromNonExistingEntity([Values] FixtureSystemType systemType)
        {
            using (var f = new Fixture<UpdateSystemDelegate>(systemType))
            {
                Assert.Throws<ArgumentException>(() => f.Set.CreateComponentNode(default));
            }
        }

        [Test]
        public void CanCreateAndDestroy_ComponentNode([Values] FixtureSystemType systemType)
        {
            using (var f = new Fixture<UpdateSystemDelegate>(systemType))
            {
                var entity = f.EM.CreateEntity();
                var node = f.Set.CreateComponentNode(entity);

                Assert.IsTrue(f.Set.Exists(node));

                f.Set.Destroy(node);
            }
        }

        [Test]
        public void WeaklyTypedComponentNodeHandle_CorrectlyImplements_Is([Values] FixtureSystemType systemType)
        {
            using (var f = new Fixture<UpdateSystemDelegate>(systemType))
            {
                var entity = f.EM.CreateEntity();
                var node = f.Set.CreateComponentNode(entity);

                Assert.IsTrue(f.Set.Is<ComponentNode>(node));

                f.Set.Destroy(node);
            }
        }

        [Test]
        public void CanGetDefinition_ForComponentNode([Values] FixtureSystemType systemType)
        {
            using (var f = new Fixture<UpdateSystemDelegate>(systemType))
            {
                var entity = f.EM.CreateEntity();
                var node = f.Set.CreateComponentNode(entity);

                // This cannot select the template overload, as ComponentNode is not : new
                // Hence the user can only ever get a base definition.
                var def = f.Set.GetDefinition(node);

                Assert.IsTrue(def is ComponentNode);
                Assert.IsTrue(def is InternalComponentNode);

                f.Set.Destroy(node);
            }
        }

        [Test]
        unsafe public void CreatedComponentNode_HasValidMembers_InKernelData([Values] FixtureSystemType systemType)
        {
            using (var f = new Fixture<UpdateSystemDelegate>(systemType))
            {
                var entity = f.EM.CreateEntity();
                var node = f.Set.CreateComponentNode(entity);

                ref readonly var kdata = ref f.Set.GetSimulationSide_KernelData(f.Set.Validate(node));

                Assert.AreEqual(entity, kdata.Entity);
                Assert.True(f.World.EntityManager.GetCheckedEntityDataAccess()->EntityComponentStore == kdata.EntityStore);

                f.Set.Destroy(node);
            }
        }

        [Test]
        unsafe public void CreatedComponentNode_HasValidIOBuffers_InRenderGraph([Values] FixtureSystemType systemType)
        {
            using (var f = new Fixture<UpdateSystemDelegate>(systemType))
            {
                var entity = f.EM.CreateEntity();
                var node = f.Set.CreateComponentNode(entity);

                f.System.Update();
                // Following data is computed in render dependent jobs.
                f.Set.DataGraph.SyncAnyRendering();

                ref readonly var kernelNode = ref f.Set.DataGraph.GetInternalData()[node.VHandle.Index];
                ref readonly var kernel = ref InternalComponentNode.GetGraphKernel(kernelNode.Instance.Kernel);

                Assert.IsTrue(kernel.Inputs.IsCreated);
                Assert.IsTrue(kernel.Outputs.IsCreated);
                Assert.IsTrue(kernel.JITPorts.IsCreated);

                Assert.Zero(kernel.Inputs.Count);
                Assert.Zero(kernel.Outputs.Count);
                Assert.Zero(kernel.JITPorts.Count);

                f.Set.Destroy(node);
            }
        }

        [Test]
        unsafe public void Dispose_CalledOnComponentNodeGraphKernel_DisposesIOBuffers()
        {
            var gk = new InternalComponentNode.GraphKernel();
            gk.Create();
            gk.Dispose();

            Assert.False(gk.Inputs.IsCreated);
            Assert.False(gk.Outputs.IsCreated);
            Assert.False(gk.JITPorts.IsCreated);
        }

        [Test]
        public void ComponentNode_DoesNotHaveAnyEntries_InPortDescription([Values] FixtureSystemType systemType)
        {
            using (var f = new Fixture<UpdateSystemDelegate>(systemType))
            {
                var entity = f.EM.CreateEntity();
                var node = f.Set.CreateComponentNode(entity);

                // This cannot select the template overload, as ComponentNode is not : new
                // Hence the user can only ever get a base definition.
                var def = f.Set.GetDefinition(node);
                var ports = def.GetPortDescription(node);

                Assert.Zero(ports.Inputs.Count);
                Assert.Zero(ports.Outputs.Count);

                f.Set.Destroy(node);
            }
        }

        [Test]
        public void CannotCreate_MoreThanOne_ComponentNode_FromSameEntity([Values] FixtureSystemType systemType)
        {
            using (var f = new Fixture<UpdateSystemDelegate>(systemType))
            {
                var entity = f.EM.CreateEntity();
                var node = f.Set.CreateComponentNode(entity);

                Assert.IsTrue(f.Set.Exists(node));

                for (int i = 0; i < 10; ++i)
                    Assert.Throws<InvalidOperationException>(() => f.Set.CreateComponentNode(entity));

                f.Set.Destroy(node);

                node = f.Set.CreateComponentNode(entity);
                Assert.IsTrue(f.Set.Exists(node));

                for (int i = 0; i < 10; ++i)
                    Assert.Throws<InvalidOperationException>(() => f.Set.CreateComponentNode(entity));

                f.Set.Destroy(node);
            }
        }


        [Test]
        public void CanCreateMultipleComponentNodes_EachFromDifferentEntity([Values] FixtureSystemType systemType)
        {
            const int k_Count = 100;

            var garbage = new List<NodeHandle>();

            using (var f = new Fixture<UpdateSystemDelegate>(systemType))
            {
                for(int i = 0; i < k_Count; ++i)
                {
                    var entity = f.EM.CreateEntity();
                    var node = f.Set.CreateComponentNode(entity);

                    Assert.IsTrue(f.Set.Exists(node));

                    garbage.Add(node);
                }

                garbage.ForEach(n => f.Set.Destroy(n));
            }
        }

        [Test]
        public void CanCreateMultipleComponentNodes_FromSameEntity_InDifferentSets([Values] FixtureSystemType systemType)
        {
            const int k_Count = 10;

            var garbage = new List<NodeHandle>();
            var sets = new List<NodeSet>();

            using (var f = new Fixture<UpdateSystemDelegate>(systemType))
            {
                var entity = f.EM.CreateEntity();

                for (int i = 0; i < k_Count; ++i)
                {
                    sets.Add(new NodeSet(f.System));

                    var node = sets[i].CreateComponentNode(entity);

                    Assert.IsTrue(sets[i].Exists(node));

                    garbage.Add(node);
                }

                for (int i = 0; i < k_Count; ++i)
                {
                    sets[i].Destroy(garbage[i]);
                    sets[i].Dispose();
                }
            }
        }


        [Test]
        public void CreatingComponentNode_AddsAndRemoves_Expected_NodeSetAttachment([Values] FixtureSystemType systemType)
        {
            using (var f = new Fixture<UpdateSystemDelegate>(systemType))
            {
                var entity = f.EM.CreateEntity();
                var node = f.Set.CreateComponentNode(entity);

                Assert.IsTrue(f.EM.HasComponent<NodeSetAttachment>(entity));

                var buf = f.EM.GetBuffer<NodeSetAttachment>(entity);

                Assert.AreEqual(1, buf.Length);
                var item0 = buf[0];

                Assert.AreEqual(node.VHandle, item0.Node.Versioned);
                Assert.AreEqual(f.Set.NodeSetID, item0.NodeSetID);

                f.Set.Destroy(node);

                Assert.False(f.EM.HasComponent<NodeSetAttachment>(entity));
            }
        }

        [Test]
        public void CanConnect_EntityWithoutComponentData_ToNodeWithComponentData([Values] FixtureSystemType systemType)
        {
            using (var f = new Fixture<UpdateSystemDelegate>(systemType))
            {
                var entity = f.EM.CreateEntity();
                var entityNode = f.Set.CreateComponentNode(entity);
                var dfgNode = f.Set.Create<SimpleNode_WithECSTypes>();

                Assert.DoesNotThrow(() => f.Set.Connect(entityNode, ComponentNode.Output<SimpleData>(), dfgNode, SimpleNode_WithECSTypes.KernelPorts.Input));

                f.Set.Destroy(entityNode, dfgNode);
            }
        }

        [Test]
        public void CanConnect_SourceComponentNode_AndNodeTogether([Values] APIType mode, [Values] FixtureSystemType systemType)
        {
            using (var f = new Fixture<UpdateSystemDelegate>(systemType))
            {
                var entity = f.EM.CreateEntity(typeof(SimpleData));
                var entityNode = f.Set.CreateComponentNode(entity);
                var dfgNode = f.Set.Create<SimpleNode_WithECSTypes>();

                Action disconnect = () =>
                {
                    if (mode == APIType.StronglyTyped)
                        f.Set.Disconnect(entityNode, ComponentNode.Output<SimpleData>(), dfgNode, SimpleNode_WithECSTypes.KernelPorts.Input);
                    else
                    {
                        f.Set.Disconnect(
                            entityNode,
                            ComponentNode.Output(ComponentType.ReadOnly<SimpleData>()),
                            dfgNode,
                            (InputPortID)SimpleNode_WithECSTypes.KernelPorts.Input
                        );
                    }
                };

                Action connect = () =>
                {
                    if (mode == APIType.StronglyTyped)
                        f.Set.Connect(entityNode, ComponentNode.Output<SimpleData>(), dfgNode, SimpleNode_WithECSTypes.KernelPorts.Input);
                    else
                    {
                        f.Set.Connect(
                            entityNode,
                            ComponentNode.Output(ComponentType.ReadOnly<SimpleData>()),
                            dfgNode,
                            (InputPortID)SimpleNode_WithECSTypes.KernelPorts.Input
                        );
                    }
                };

                // Connection doesn't exist
                Assert.Throws<ArgumentException>(() => disconnect());

                connect();

                // Connection already exists
                Assert.Throws<ArgumentException>(() => connect());

                disconnect();

                // Connection doesn't exist
                Assert.Throws<ArgumentException>(() => disconnect());

                f.Set.Destroy(entityNode, dfgNode);
            }
        }

        [Test]
        public void CanConnect_DestinationComponentNode_AndNodeTogether([Values] APIType mode, [Values] FixtureSystemType systemType)
        {
            using (var f = new Fixture<UpdateSystemDelegate>(systemType))
            {
                var entity = f.EM.CreateEntity(typeof(SimpleData));
                var entityNode = f.Set.CreateComponentNode(entity);
                var dfgNode = f.Set.Create<SimpleNode_WithECSTypes>();

                Action disconnect = () =>
                {
                    if (mode == APIType.StronglyTyped)
                        f.Set.Disconnect(dfgNode, SimpleNode_WithECSTypes.KernelPorts.Output, entityNode, ComponentNode.Input<SimpleData>());
                    else
                    {
                        f.Set.Disconnect(
                            dfgNode,
                            (OutputPortID)SimpleNode_WithECSTypes.KernelPorts.Output,
                            entityNode,
                            ComponentNode.Input(ComponentType.ReadWrite<SimpleData>())
                        );
                    }
                };

                Action connect = () =>
                {
                    if (mode == APIType.StronglyTyped)
                        f.Set.Connect(dfgNode, SimpleNode_WithECSTypes.KernelPorts.Output, entityNode, ComponentNode.Input<SimpleData>());
                    else
                    {
                        f.Set.Connect(
                            dfgNode,
                            (OutputPortID)SimpleNode_WithECSTypes.KernelPorts.Output,
                            entityNode,
                            ComponentNode.Input(ComponentType.ReadWrite<SimpleData>())
                        );
                    }
                };

                // Connection doesn't exist
                Assert.Throws<ArgumentException>(() => disconnect());

                connect();

                // Connection already exists
                Assert.Throws<ArgumentException>(() => connect());

                disconnect();

                // Connection doesn't exist
                Assert.Throws<ArgumentException>(() => disconnect());

                f.Set.Destroy(entityNode, dfgNode);
            }
        }

        class IntArgumentSystemDelegate : INodeSetSystemDelegate
        {
            public int DataArgument;
            EntityQuery m_Query;

            protected struct ProcessJob : IJobChunk
            {
                public int Arg;
                public ComponentTypeHandle<SimpleData> SimpleDataType;

                public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
                {
                    var buffers = chunk.GetNativeArray(SimpleDataType);

                    for (int c = 0; c < chunk.Count; c++)
                    {
                        SimpleData data;
                        data.Something = Arg;
                        data.SomethingElse = Arg;
                        buffers[c] = data;
                    }
                }
            }


            public void OnCreate(ComponentSystemBase system)
            {
                m_Query = system.GetEntityQuery(ComponentType.ReadOnly<SimpleData>());
            }

            public void OnDestroy(ComponentSystemBase system, NodeSet set) {}

            public void OnUpdate(ComponentSystemBase system, NodeSet set, JobHandle inputDeps, out JobHandle outputDeps)
            {
                outputDeps = set.Update(new ProcessJob { Arg = DataArgument, SimpleDataType = system.GetComponentTypeHandle<SimpleData>() }.Schedule(m_Query, inputDeps));
            }
        }

        [Test]
        public void CanRead_ComponentData_FromConnectedNode_FromGraphValue([Values] FixtureSystemType systemType)
        {
            const int k_Loops = 100;

            using (var f = new Fixture<IntArgumentSystemDelegate>(systemType))
            {
                var entity = f.EM.CreateEntity(typeof(SimpleData));
                var entityNode = f.Set.CreateComponentNode(entity);
                var dfgNode = f.Set.Create<SimpleNode_WithECSTypes>();

                f.Set.Connect(entityNode, ComponentNode.Output<SimpleData>(), dfgNode, SimpleNode_WithECSTypes.KernelPorts.Input);
                var gv = f.Set.CreateGraphValue(dfgNode, SimpleNode_WithECSTypes.KernelPorts.Output);

                var rng = new Mathematics.Random(0x7f);

                for (int i = 0; i < k_Loops; ++i)
                {
                    var value = rng.NextInt();
                    f.SystemDelegate.DataArgument = value;
                    f.System.Update();

                    Assert.AreEqual(value, (int)f.Set.GetValueBlocking(gv).Something);
                    Assert.AreEqual(value, (int)f.Set.GetValueBlocking(gv).SomethingElse);
                }

                f.Set.ReleaseGraphValue(gv);
                f.Set.Destroy(entityNode, dfgNode);
            }
        }

        [Test]
        public void CanWrite_ComponentData_FromConnectedNode_ToEntity([Values] FixtureSystemType systemType)
        {
            const int k_Loops = 100;

            using (var f = new Fixture<UpdateSystemDelegate>(systemType))
            {
                var entity = f.EM.CreateEntity(typeof(SimpleData));
                var entityNode = f.Set.CreateComponentNode(entity);
                var dfgNode = f.Set.Create<SimpleNode_WithECSTypes>();

                f.Set.Connect(dfgNode, SimpleNode_WithECSTypes.KernelPorts.Output, entityNode, ComponentNode.Input<SimpleData>());

                var rng = new Mathematics.Random(0x7f);

                for (int i = 0; i < k_Loops; ++i)
                {
                    var value = rng.NextInt();
                    var data = new SimpleData();
                    data.Something = value;
                    data.SomethingElse = value;

                    f.Set.SetData(dfgNode, SimpleNode_WithECSTypes.KernelPorts.Input, data);
                    f.System.Update();

                    Assert.AreEqual(value, (int)f.EM.GetComponentData<SimpleData>(entity).Something);
                    Assert.AreEqual(value, (int)f.EM.GetComponentData<SimpleData>(entity).SomethingElse);
                }

                f.Set.Destroy(entityNode, dfgNode);
            }
        }

        [Test]
        public unsafe void ConnectingEntityToEntity_CreatesECSBackReference_InInputToECS([Values] FixtureSystemType systemType)
        {
            using (var f = new Fixture<UpdateSystemDelegate>(systemType))
            {
                var sourceEntity = f.EM.CreateEntity(typeof(SimpleData));
                var destEntity = f.EM.CreateEntity(typeof(SimpleData));

                var sourceComponentNode = f.Set.CreateComponentNode(sourceEntity);
                var destComponentNode = f.Set.CreateComponentNode(destEntity);

                f.Set.Connect(sourceComponentNode, ComponentNode.Output<SimpleData>(), destComponentNode, ComponentNode.Input<SimpleData>());

                f.System.Update();
                // Following data is computed in render dependent jobs.
                f.Set.DataGraph.SyncAnyRendering();

                var pointers = f.Set.DataGraph.GetInternalData()[destComponentNode.VHandle.Index].Instance;
                ref readonly var graphKernel = ref InternalComponentNode.GetGraphKernel(pointers.Kernel);

                Assert.AreEqual(1, graphKernel.Inputs.Count);
                Assert.True(graphKernel.Inputs[0].IsECSSource);
                Assert.AreEqual(sourceEntity, graphKernel.Inputs[0].GetAsEntity_ForTesting());

                f.Set.Destroy(sourceComponentNode, destComponentNode);
            }
        }

        [Test]
        public unsafe void ConnectingEntityToEntity_DoesNotRecordOutputs([Values] FixtureSystemType systemType)
        {
            // Outputs from ECS are used to repatch DFG references.
            // In case of entity -> entity, we only the "input"
            // which models the copy back.

            using (var f = new Fixture<UpdateSystemDelegate>(systemType))
            {
                var sourceEntity = f.EM.CreateEntity(typeof(SimpleData));
                var destEntity = f.EM.CreateEntity(typeof(SimpleData));

                var sourceComponentNode = f.Set.CreateComponentNode(sourceEntity);
                var destComponentNode = f.Set.CreateComponentNode(destEntity);

                f.Set.Connect(sourceComponentNode, ComponentNode.Output<SimpleData>(), destComponentNode, ComponentNode.Input<SimpleData>());

                f.System.Update();
                // Following data is computed in render dependent jobs.
                f.Set.DataGraph.SyncAnyRendering();

                var pointers = f.Set.DataGraph.GetInternalData()[sourceComponentNode.VHandle.Index].Instance;
                var graphKernel = InternalComponentNode.GetGraphKernel(pointers.Kernel);

                Assert.Zero(graphKernel.Outputs.Count);

                f.Set.Destroy(sourceComponentNode, destComponentNode);
            }
        }

        [Test]
        public void CanConnectComponentData_FromComponentNode_ToComponentNode([Values] FixtureSystemType systemType)
        {
            const int k_Loops = 100;

            using (var f = new Fixture<UpdateSystemDelegate>(systemType))
            {
                var sourceEntity = f.EM.CreateEntity(typeof(SimpleData));
                var destEntity = f.EM.CreateEntity(typeof(SimpleData));

                var sourceComponentNode = f.Set.CreateComponentNode(sourceEntity);
                var destComponentNode = f.Set.CreateComponentNode(destEntity);

                f.Set.Connect(sourceComponentNode, ComponentNode.Output<SimpleData>(), destComponentNode, ComponentNode.Input<SimpleData>());

                var rng = new Mathematics.Random(0x7f);

                for (int i = 0; i < k_Loops; ++i)
                {
                    var value = rng.NextInt();
                    var data = new SimpleData();
                    data.Something = value;
                    data.SomethingElse = value;

                    f.EM.SetComponentData(sourceEntity, data);
                    f.System.Update();

                    Assert.AreEqual(value, (int)f.EM.GetComponentData<SimpleData>(destEntity).Something);
                    Assert.AreEqual(value, (int)f.EM.GetComponentData<SimpleData>(destEntity).SomethingElse);
                }

                f.Set.Destroy(sourceComponentNode, destComponentNode);
            }
        }

        [Test]
        public void CreatingIncompatible_IOPorts_Throws()
        {
            Assert.Throws<ArgumentException>(() => ComponentNode.Input(ComponentType.ReadOnly<SimpleData>()));
            Assert.Throws<ArgumentException>(() => ComponentNode.Output(ComponentType.ReadWrite<SimpleData>()));
        }

        [Test]
        public void CannotConnectMessage_ToComponentNode([Values(true, false)] bool forward, [Values] FixtureSystemType systemType)
        {
            using (var f = new Fixture<UpdateSystemDelegate>(systemType))
            {
                var e = f.EM.CreateEntity(typeof(SimpleData));
                var n = f.Set.CreateComponentNode(e);
                var o = f.Set.Create<InOutTestNode>();

                Assert.Throws<InvalidOperationException>(() =>
                    {
                        if (forward)
                            f.Set.Connect(o, (OutputPortID)InOutTestNode.SimulationPorts.Output, n, (InputPortID)ComponentNode.Input<SimpleData>());
                        else
                            f.Set.Connect(n, (OutputPortID)ComponentNode.Output<SimpleData>(), o, (InputPortID)InOutTestNode.SimulationPorts.Input);
                    }
                );

                f.Set.Destroy(n, o);
            }
        }

        [Test]
        public void CannotSendMessage_ToComponentNode([Values] FixtureSystemType systemType)
        {
            using (var f = new Fixture<UpdateSystemDelegate>(systemType))
            {
                var e = f.EM.CreateEntity(typeof(SimpleData));
                var n = f.Set.CreateComponentNode(e);

                Assert.Throws<NotImplementedException>(() =>
                    {
                        f.Set.SendMessage(n, (InputPortID)ComponentNode.Input<SimpleData>(), 10);
                    }
                );

                f.Set.Destroy(n);
            }
        }

        [Test]
        public void CannotSetBufferSize_OnComponentNode([Values] APIType mode, [Values] FixtureSystemType systemType)
        {
            using (var f = new Fixture<UpdateSystemDelegate>(systemType))
            {
                var e = f.EM.CreateEntity(typeof(SimpleData));
                var n = f.Set.CreateComponentNode(e);

                Assert.Throws<NotImplementedException>(() =>
                    {
                        if(mode == APIType.StronglyTyped)
                            f.Set.SetBufferSize(n, ComponentNode.Output<SimpleData>(), default);
                        else
                            f.Set.SetBufferSize(n, (OutputPortID)ComponentNode.Output<SimpleData>(), new SimpleData());
                    }
                );

                f.Set.Destroy(n);
            }
        }

        [Test]
        public void CannotSetPortArraySize_OnComponentNode([Values] FixtureSystemType systemType)
        {
            using (var f = new Fixture<UpdateSystemDelegate>(systemType))
            {
                var e = f.EM.CreateEntity(typeof(SimpleData));
                var n = f.Set.CreateComponentNode(e);

                Assert.Throws<NotImplementedException>(() =>
                    f.Set.SetPortArraySize(n, (InputPortID)ComponentNode.Input<SimpleData>(), 10));

                Assert.Throws<NotImplementedException>(() =>
                    f.Set.SetPortArraySize(n, (OutputPortID)ComponentNode.Output<SimpleData>(), 10));

                f.Set.Destroy(n);
            }
        }

        [Test]
        public void CannotCreateGraphValue_FromComponentNode([Values] APIType mode, [Values] FixtureSystemType systemType)
        {
            using (var f = new Fixture<UpdateSystemDelegate>(systemType))
            {
                var e = f.EM.CreateEntity(typeof(SimpleData));
                var n = f.Set.CreateComponentNode(e);

                Assert.Throws<NotImplementedException>(() =>
                    {
                        if(mode == APIType.StronglyTyped)
                            f.Set.CreateGraphValue(n, ComponentNode.Output<SimpleData>());
                        else
                            f.Set.CreateGraphValue<SimpleData>(n, (OutputPortID)ComponentNode.Output<SimpleData>());
                    }
                );

                f.Set.Destroy(n);
            }
        }

        [Test]
        public void CannotSetData_OnComponentNode([Values] APIType mode, [Values] FixtureSystemType systemType)
        {
            using (var f = new Fixture<UpdateSystemDelegate>(systemType))
            {
                var e = f.EM.CreateEntity(typeof(SimpleData));
                var n = f.Set.CreateComponentNode(e);

                Assert.Throws<NotImplementedException>(() =>
                    {
                        if (mode == APIType.StronglyTyped)
                            f.Set.SetData(n, ComponentNode.Input<SimpleData>(), new SimpleData());
                        else
                            f.Set.SetData(n, ComponentNode.Input<SimpleData>().Port, new SimpleData());
                    }
                );

                f.Set.Destroy(n);
            }
        }

        [Test]
        public void CanCreateNode_WithECSTypes_InNonECSSet()
        {
            // Exercises Connect() magic
            using (var set = new NodeSet())
            {
                var node = set.Create<SimpleNode_WithECSTypes>();
                var node2 = set.Create<SimpleNode_WithECSTypes>();

                set.Connect(node, SimpleNode_WithECSTypes.KernelPorts.Output, node2, SimpleNode_WithECSTypes.KernelPorts.Input);

                set.Destroy(node, node2);
            }
        }

        /* Doesn't work until next PR - and in general not until the weak system recognizes invalid port / type combos*/
        [Test, Explicit]
        public void ForceFeedingECSPortIDS_DoesNotBreakNormalConnectivity()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<SimpleNode_WithECSTypes>();
                var node2 = set.Create<SimpleNode_WithECSTypes>();

                set.Connect(
                    node,
                    ComponentNode.Output(ComponentType.ReadOnly<SimpleData>()),
                    node2,
                    ComponentNode.Input(ComponentType.ReadWrite<SimpleData>())
                );

                set.Destroy(node, node2);
            }
        }

    }
#pragma warning restore 649 // non-public unassigned default value

}

