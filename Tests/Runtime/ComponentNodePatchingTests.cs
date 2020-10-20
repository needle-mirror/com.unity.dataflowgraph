using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using static Unity.DataFlowGraph.Tests.ComponentNodeSetTests;

namespace Unity.DataFlowGraph.Tests
{
    unsafe class ComponentNodePatchingTests
    {
        [InternalBufferCapacity(10)]
        struct Buffer : IBufferElementData { int Something; }

        struct Shared : ISharedComponentData, IEquatable<Shared>
        {
            public int Something;

            public Shared(int what) => Something = what;

            public override int GetHashCode() => Something;

            public bool Equals(Shared other)
            {
                return other.Something == Something;
            }
        }

        struct HookPatchJob : IJobChunk
        {
            public NativeQueue<Entity>.ParallelWriter NotifiedEntities;
            [ReadOnly] public BufferTypeHandle<NodeSetAttachment> NodeSetAttachmentType;
            [ReadOnly] public EntityTypeHandle EntityType;

            public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
            {
                NativeArray<Entity> entities = chunk.GetNativeArray(EntityType);
                foreach (var entity in entities)
                    NotifiedEntities.Enqueue(entity);
            }

        }

        [DisableAutoCreation, AlwaysUpdateSystem]
        public class RepatchSystemDelegate : INodeSetSystemDelegate
        {
            public NativeQueue<Entity> UpdatedEntities = new NativeQueue<Entity>(Allocator.Persistent);
            EntityQuery m_Query;

            public List<Entity> DequeueToList()
            {
                var list = new List<Entity>();

                while (UpdatedEntities.Count > 0)
                    list.Add(UpdatedEntities.Dequeue());

                return list;
            }

            public void OnCreate(ComponentSystemBase system)
            {
                m_Query = RenderGraph.CreateNodeSetAttachmentQuery(system);
            }

            public void OnDestroy(ComponentSystemBase system, NodeSet set)
            {
                UpdatedEntities.Dispose();
            }

            public void OnUpdate(ComponentSystemBase system, NodeSet set, JobHandle inputDeps, out JobHandle outputDeps)
            {
                var job = new HookPatchJob();
                job.NotifiedEntities = UpdatedEntities.AsParallelWriter();
                job.NodeSetAttachmentType = system.GetBufferTypeHandle<NodeSetAttachment>();
                job.EntityType = system.GetEntityTypeHandle();

                var deps = job.Schedule(m_Query, inputDeps);

                deps.Complete(); // Set does not expect another job running on NodeSetAttachment

                outputDeps = set.Update(deps);
            }
        }

        [Test]
        public void HookPatchJob_MatchesInternalRepatchJob()
        {
            Assert.True(typeof(IJobChunk).IsAssignableFrom(typeof(HookPatchJob)));
            Assert.True(typeof(IJobChunk).IsAssignableFrom(typeof(RepatchDFGInputsIfNeededJob)));

            var hookAttributes =
                typeof(HookPatchJob)
                .GetCustomAttributes(true)
                .Where(o => !(o is BurstCompileAttribute));

            var internalAttributes =
                typeof(RepatchDFGInputsIfNeededJob)
                .GetCustomAttributes(true)
                .Where(o => !(o is BurstCompileAttribute));

            CollectionAssert.AreEqual(hookAttributes, internalAttributes);
        }

        [Test]
        public void RepatchJobExecutes_OnCreatedEntities_ThatAreRelated([Values] FixtureSystemType systemType)
        {
            using (var f = new Fixture<RepatchSystemDelegate>(systemType))
            {
                var entity = f.EM.CreateEntity(typeof(NodeSetAttachment));
                f.System.Update();
                Assert.AreEqual(entity, f.SystemDelegate.UpdatedEntities.Dequeue());
            }
        }

        class PatchFixture : Fixture<RepatchSystemDelegate>, IDisposable
        {
            public Entity Original;
            public Entity Changed;

            public NodeHandle<ComponentNode> OriginalNode;
            public NodeHandle<ComponentNode> ChangedNode;
            public NodeHandle<SimpleNode_WithECSTypes_OnInputs> Receiver;

            public PatchFixture(FixtureSystemType systemType) : base(systemType)
            {
                Original = EM.CreateEntity();
                Changed = EM.CreateEntity(typeof(DataOne));
                OriginalNode = Set.CreateComponentNode(Original);
                ChangedNode = Set.CreateComponentNode(Changed);
                Receiver = Set.Create<SimpleNode_WithECSTypes_OnInputs>();

                Set.Connect(ChangedNode, ComponentNode.Output<DataOne>(), Receiver, SimpleNode_WithECSTypes_OnInputs.KernelPorts.Input);
            }

            public void Update()
            {
                SystemDelegate.UpdatedEntities.Clear();
                System.Update();
            }

            public new void Dispose()
            {
                if(ChangedNode != default)
                    Set.Destroy(ChangedNode);

                if (OriginalNode != default)
                    Set.Destroy(OriginalNode);

                Set.Destroy(Receiver);
                base.Dispose();
            }

            public void TestInvariants()
            {
                Assert.True(GetComponent<DataOne>(Changed) != null);
                var patch = GetInputStorage(Receiver, SimpleNode_WithECSTypes_OnInputs.KernelPorts.Input);
                Assert.True(patch->Pointer == GetComponent<DataOne>(Changed));
            }

            public unsafe DataInputStorage* GetInputStorage<T, TNode>(NodeHandle<TNode> handle, DataInput<TNode, T> id)
                where TNode : NodeDefinition
                where T : unmanaged
            {
                var graph = Set.DataGraph;
                graph.SyncAnyRendering();

                var knode = graph.GetInternalData()[handle.VHandle.Index];
                ref readonly var traits = ref knode.TraitsHandle.Resolve();

                return traits.DataPorts.FindInputDataPort(id.Port).GetStorageLocation(knode.Instance.Ports);
            }

            public unsafe T* GetComponent<T>(Entity e)
                where T : unmanaged
            {
                return (T*)World.EntityManager.GetCheckedEntityDataAccess()->EntityComponentStore->GetComponentDataWithTypeRO(e, ComponentType.ReadWrite<T>().TypeIndex);
            }
        }

        [Test]
        public void RepatchJobExecutes_WhenEntities_ChangeArchetype([Values] FixtureSystemType systemType)
        {
            using (var f = new PatchFixture(systemType))
            {
                f.Update();
                f.TestInvariants();
                // Clear it out, so we can detect repatching happened.
                *f.GetInputStorage(f.Receiver, SimpleNode_WithECSTypes_OnInputs.KernelPorts.Input) = new DataInputStorage(null);

                // Mutate archetype. Component pointer might have moved now.
                f.EM.AddBuffer<Buffer>(f.Changed);
                f.Update();
                f.TestInvariants();

                CollectionAssert.Contains(f.SystemDelegate.DequeueToList(), f.Changed);
            }
        }



        [Test]
        public void RepatchJobExecutes_WhenEntities_ChangeSharedComponentData([Values] FixtureSystemType systemType)
        {
            using (var f = new PatchFixture(systemType))
            {
                f.Update();
                f.TestInvariants();
                // Clear it out, so we can detect repatching happened.
                *f.GetInputStorage(f.Receiver, SimpleNode_WithECSTypes_OnInputs.KernelPorts.Input) = new DataInputStorage(null);

                // Mutate archetype filtering. Component pointer might have moved now.
                f.EM.AddSharedComponentData(f.Changed, new Shared(2));
                f.Update();
                f.TestInvariants();
                CollectionAssert.Contains(f.SystemDelegate.DequeueToList(), f.Changed);
            }
        }

        [Test]
        public void RepatchJobExecutes_WhenEntities_Die([Values] FixtureSystemType systemType)
        {
            using (var f = new PatchFixture(systemType))
            {
                f.Update();
                var oldMemoryPointer = f.GetInputStorage(f.Receiver, SimpleNode_WithECSTypes_OnInputs.KernelPorts.Input)->Pointer;

                f.EM.DestroyEntity(f.Changed);
                f.Update();

                // Memory mustn't point to a partially destroyed entity.
                Assert.False(oldMemoryPointer == f.GetInputStorage(f.Receiver, SimpleNode_WithECSTypes_OnInputs.KernelPorts.Input)->Pointer);
                // It's still contained in this list since NodeSetAttachment is a system state.
                CollectionAssert.Contains(f.SystemDelegate.DequeueToList(), f.Changed);

                f.Set.Destroy(f.ChangedNode);
                f.ChangedNode = default; // Don't double release it
                f.Update();

                // Memory mustn't point to a partially destroyed entity.
                Assert.False(oldMemoryPointer == f.GetInputStorage(f.Receiver, SimpleNode_WithECSTypes_OnInputs.KernelPorts.Input)->Pointer);
            }
        }

        [Test]
        public void RepatchJobExecutes_WhenChunk_IsReshuffled([Values] FixtureSystemType systemType)
        {
            // As entities are guaranteed to be linearly laid out, destroying
            // an entity behind another moves the other and invalidates pointers.
            // TODO: Establish confidence these entities are in the same chunk.
            using (var f = new PatchFixture(systemType))
            {
                f.System.Update();
                f.TestInvariants();

                f.EM.DestroyEntity(f.Original);
                f.Set.Destroy(f.OriginalNode);
                f.OriginalNode = default;

                f.Update();
                f.TestInvariants();
                CollectionAssert.Contains(f.SystemDelegate.DequeueToList(), f.Changed);
            }
        }

        [Test]
        public void RepatchJobExecutes_WhenBuffer_ChangesSize([Values] FixtureSystemType systemType)
        {
            // TODO: Rewrite this test to cover buffers.
            // Could be good to precisely cover when buffer switches from internal capacity to external capacity
            using (var f = new Fixture<RepatchSystemDelegate>(systemType))
            {
                var entity = f.EM.CreateEntity(typeof(NodeSetAttachment));

                f.System.Update();

                f.SystemDelegate.UpdatedEntities.Clear();
                f.EM.AddBuffer<Buffer>(entity);

                for (int i = 0; i < 100; ++i)
                {
                    f.EM.GetBuffer<Buffer>(entity).Add(default);
                    f.System.Update();
                    CollectionAssert.Contains(f.SystemDelegate.DequeueToList(), entity);
                }
            }
        }

        [Test]
        public unsafe void ECSInput_Union_OfPointerAndEntity_HaveExpectedLayout()
        {
            Assert.AreEqual(sizeof(void*), sizeof(Entity));
            Assert.AreEqual(16, sizeof(InternalComponentNode.InputToECS));
        }

        const int k_Pointer = 0x1339;
        const int k_Size = 13;
        const int k_Type = 12;

        [Test]
        public unsafe void ECSInput_ConstructedWithMemoryLocation_IsNotECSSource()
        {
            var input = new InternalComponentNode.InputToECS((void*)k_Pointer, k_Type, k_Size);

            Assert.AreEqual(k_Size, input.SizeOf);
            Assert.AreEqual(k_Type, input.ECSTypeIndex);

            Assert.False(input.IsECSSource);
            Assert.True(input.Resolve(null) == (void*)k_Pointer);
        }

        [Test]
        public unsafe void ECSInput_ConstructedWithMemoryLocation_IsECSSource()
        {
            var input = new InternalComponentNode.InputToECS(new Entity(), k_Type, k_Size);

            Assert.AreEqual(k_Size, input.SizeOf);
            Assert.AreEqual(k_Type, input.ECSTypeIndex);

            Assert.True(input.IsECSSource);
        }

        [Test]
        public unsafe void ConnectingDFGToEntity_Records_OutputConnection([Values] FixtureSystemType systemType)
        {
            using (var f = new PatchFixture(systemType))
            {
                var sourceEntity = f.EM.CreateEntity(typeof(SimpleData));
                var destEntity = f.EM.CreateEntity(typeof(SimpleData));

                var sourceEntityNode = f.Set.CreateComponentNode(sourceEntity);
                var dfgNode = f.Set.Create<SimpleNode_WithECSTypes>();

                f.Set.Connect(sourceEntityNode, ComponentNode.Output<SimpleData>(), dfgNode, SimpleNode_WithECSTypes.KernelPorts.Input);

                f.System.Update();
                // Following data is computed in render dependent jobs.
                f.Set.DataGraph.SyncAnyRendering();

                var pointers = f.Set.DataGraph.GetInternalData()[sourceEntityNode.VHandle.Index].Instance;
                ref readonly var graphKernel = ref InternalComponentNode.GetGraphKernel(pointers.Kernel);

                Assert.AreEqual(1, graphKernel.Outputs.Count);
                Assert.AreEqual(ComponentType.ReadWrite<SimpleData>().TypeIndex, graphKernel.Outputs[0].ComponentType);
                Assert.IsTrue(f.GetInputStorage(dfgNode, SimpleNode_WithECSTypes.KernelPorts.Input) == graphKernel.Outputs[0].DFGPatch);

                f.Set.Destroy(sourceEntityNode, dfgNode);
            }
        }

        [Test]
        public void EntityToEntity_TogglingComponentDataExistence_OrDestroyingSource_RetainsLastValue_InDestination(
            [Values] FixtureSystemType systemType)
        {
            const int k_Loops = 5;

            using (var f = new Fixture<UpdateSystemDelegate>(systemType))
            {
                var sourceEntity = f.EM.CreateEntity(typeof(SimpleData));
                var destEntity = f.EM.CreateEntity(typeof(SimpleData));

                var sourceEntityNode = f.Set.CreateComponentNode(sourceEntity);
                var destEntityNode = f.Set.CreateComponentNode(destEntity);

                f.Set.Connect(sourceEntityNode, ComponentNode.Output<SimpleData>(), destEntityNode, ComponentNode.Input<SimpleData>());

                var rng = new Mathematics.Random(0x7f);

                int value = 0;

                // Test removing and adding component type retains the value and works
                for (int i = 0; i < k_Loops; ++i)
                {
                    value = rng.NextInt();
                    var data = new SimpleData();
                    data.Something = value;
                    data.SomethingElse = value;

                    f.EM.SetComponentData(sourceEntity, data);
                    f.System.Update();

                    Assert.AreEqual(value, (int)f.EM.GetComponentData<SimpleData>(destEntity).Something);
                    Assert.AreEqual(value, (int)f.EM.GetComponentData<SimpleData>(destEntity).SomethingElse);

                    f.EM.RemoveComponent<SimpleData>(sourceEntity);

                    Assert.AreEqual(value, (int)f.EM.GetComponentData<SimpleData>(destEntity).Something);
                    Assert.AreEqual(value, (int)f.EM.GetComponentData<SimpleData>(destEntity).SomethingElse);

                    f.EM.AddComponent<SimpleData>(sourceEntity);
                }

                // Test removing the source entity, but keeping it as an entity node still retains the value
                f.EM.DestroyEntity(sourceEntity);
                for (int i = 0; i < k_Loops; ++i)
                {
                    f.System.Update();

                    Assert.AreEqual(value, (int)f.EM.GetComponentData<SimpleData>(destEntity).Something);
                    Assert.AreEqual(value, (int)f.EM.GetComponentData<SimpleData>(destEntity).SomethingElse);
                }

                // Test removing the dest entity, but keeping it as an entity node still works
                f.EM.DestroyEntity(destEntity);
                for (int i = 0; i < k_Loops; ++i)
                {
                    f.System.Update();
                }

                // Test removing the source as an entity node still keeps the orphan dest entity node
                // working
                f.Set.Destroy(sourceEntityNode);

                for (int i = 0; i < k_Loops; ++i)
                {
                    f.System.Update();
                }

                f.Set.Destroy(destEntityNode);
                for (int i = 0; i < k_Loops; ++i)
                {
                    f.System.Update();
                }
            }
        }

        [Test]
        public void ComponentNodeIOPatching_CorrectlyHandles_IncrementalTopologyUpdates(
            [Values] FixtureSystemType systemType)
        {
            const int k_Loops = 15;

            const int k_ChangeBoth = 0;
            const int k_ChangeInput = 1;
            const int k_ChangeOutput = 2;

            using (var f = new Fixture<UpdateSystemDelegate>(systemType))
            {
                // form two islands, and check input / output connectivity
                // remains correctly patched in both cases when either updates.
                var inputEntity = f.EM.CreateEntity(typeof(SimpleData));
                var outputEntity = f.EM.CreateEntity(typeof(SimpleData));

                var inputEntityNode = f.Set.CreateComponentNode(inputEntity);
                var outputEntityNode = f.Set.CreateComponentNode(outputEntity);

                var dfgInput = f.Set.Create<NodeWithParametricPortType<SimpleData>>();
                var dfgOutput = f.Set.Create<NodeWithParametricPortType<SimpleData>>();

                f.Set.Connect(outputEntityNode, ComponentNode.Output<SimpleData>(), dfgInput, NodeWithParametricPortType<SimpleData>.KernelPorts.Input);
                f.Set.Connect(dfgOutput, NodeWithParametricPortType<SimpleData>.KernelPorts.Output, inputEntityNode, ComponentNode.Input<SimpleData>());

                for (int i = 0; i < k_Loops; ++i)
                {
                    var mode = i % 3;
                    switch (mode)
                    {
                        case k_ChangeBoth:
                        {
                            f.Set.Disconnect(outputEntityNode, ComponentNode.Output<SimpleData>(), dfgInput, NodeWithParametricPortType<SimpleData>.KernelPorts.Input);
                            f.Set.Disconnect(dfgOutput, NodeWithParametricPortType<SimpleData>.KernelPorts.Output, inputEntityNode, ComponentNode.Input<SimpleData>());
                            f.Set.Connect(outputEntityNode, ComponentNode.Output<SimpleData>(), dfgInput, NodeWithParametricPortType<SimpleData>.KernelPorts.Input);
                            f.Set.Connect(dfgOutput, NodeWithParametricPortType<SimpleData>.KernelPorts.Output, inputEntityNode, ComponentNode.Input<SimpleData>());
                            break;
                        }
                        case k_ChangeOutput:
                        {
                            f.Set.Disconnect(outputEntityNode, ComponentNode.Output<SimpleData>(), dfgInput, NodeWithParametricPortType<SimpleData>.KernelPorts.Input);
                            f.Set.Connect(outputEntityNode, ComponentNode.Output<SimpleData>(), dfgInput, NodeWithParametricPortType<SimpleData>.KernelPorts.Input);
                            break;
                        }
                        case k_ChangeInput:
                        {
                            f.Set.Disconnect(dfgOutput, NodeWithParametricPortType<SimpleData>.KernelPorts.Output, inputEntityNode, ComponentNode.Input<SimpleData>());
                            f.Set.Connect(dfgOutput, NodeWithParametricPortType<SimpleData>.KernelPorts.Output, inputEntityNode, ComponentNode.Input<SimpleData>());
                            break;
                        }
                    }

                    f.System.Update();
                    var rg = f.Set.DataGraph;

                    rg.SyncAnyRendering();
                    var map = rg.GetMap_ForTesting();
                    var nodes = rg.GetInternalData();
                    var cache = rg.Cache;

                    var inputIndex = map[f.Set.Validate(inputEntityNode)];
                    var outputIndex = map[f.Set.Validate(outputEntityNode)];

                    // islands coalesced together?
                    Assume.That(inputIndex.GroupID != outputIndex.GroupID);

                    // check only the islands we want to change changed
                    if (mode == k_ChangeInput || mode == k_ChangeBoth)
                        CollectionAssert.Contains(cache.NewGroups.ToArray(), inputIndex.GroupID);

                    if (mode == k_ChangeOutput || mode == k_ChangeBoth)
                        CollectionAssert.Contains(cache.NewGroups.ToArray(), outputIndex.GroupID);

                    if (mode == k_ChangeInput)
                        CollectionAssert.DoesNotContain(cache.NewGroups.ToArray(), outputIndex.GroupID);

                    if (mode == k_ChangeOutput)
                        CollectionAssert.DoesNotContain(cache.NewGroups.ToArray(), inputIndex.GroupID);

                    var inputEntityData = InternalComponentNode.GetGraphKernel(nodes[inputEntityNode.VHandle.Index].Instance.Kernel);
                    var outputEntityData = InternalComponentNode.GetGraphKernel(nodes[outputEntityNode.VHandle.Index].Instance.Kernel);

                    ref var dfgInputPorts = ref UnsafeUtility.AsRef<NodeWithParametricPortType<SimpleData>.KernelDefs>(nodes[dfgInput.VHandle.Index].Instance.Ports);
                    ref var dfgOutputPorts = ref UnsafeUtility.AsRef<NodeWithParametricPortType<SimpleData>.KernelDefs>(nodes[dfgOutput.VHandle.Index].Instance.Ports);

                    // check patches point to the correct places
                    Assert.AreEqual(1, inputEntityData.Inputs.Count);
                    Assert.AreEqual(0, inputEntityData.Outputs.Count);

                    Assert.AreEqual(0, outputEntityData.Inputs.Count);
                    Assert.AreEqual(1, outputEntityData.Outputs.Count);

                    var inputToEcs = inputEntityData.Inputs[0].Resolve(f.EM.GetCheckedEntityDataAccess()->EntityComponentStore);
                    var output = outputEntityData.Outputs[0];

                    fixed (SimpleData* outputPortLocation = &dfgOutputPorts.Output.m_Value)
                        Assert.True(inputToEcs == outputPortLocation);

                    fixed (DataInputStorage* inputPortLocation = &dfgInputPorts.Input.Storage)
                        Assert.True(output.DFGPatch == inputPortLocation);
                }

                f.Set.Destroy(inputEntityNode, outputEntityNode, dfgInput, dfgOutput);
            }
        }
    }
}
