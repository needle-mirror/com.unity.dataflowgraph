using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
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

        struct HookPatchJob
#pragma warning disable 618  // warning CS0618: 'IJobForEach' is obsolete: 'Please use Entities.ForEach or IJobChunk to schedule jobs that work on Entities. (RemovedAfter 2020-06-20)
            : IJobForEachWithEntity_EB<NodeSetAttachment>
#pragma warning restore 618
        {
            public NativeQueue<Entity> NotifiedEntities;

            public void Execute(Entity entity, int index, [ReadOnly] DynamicBuffer<NodeSetAttachment> b0)
            {
                NotifiedEntities.Enqueue(entity);
            }
        }

        [DisableAutoCreation, AlwaysUpdateSystem]
        public class RepatchSystemDelegate : INodeSetSystemDelegate
        {
            public NativeQueue<Entity> UpdatedEntities = new NativeQueue<Entity>(Allocator.Persistent);
            public List<Entity> DequeueToList()
            {
                var list = new List<Entity>();

                while (UpdatedEntities.Count > 0)
                    list.Add(UpdatedEntities.Dequeue());

                return list;
            }

            public void OnCreate(ComponentSystemBase system) {}

            public void OnDestroy(ComponentSystemBase system, NodeSet set)
            {
                UpdatedEntities.Dispose();
            }

            public void OnUpdate(ComponentSystemBase system, NodeSet set, JobHandle inputDeps, out JobHandle outputDeps)
            {
                var job = new HookPatchJob();
                job.NotifiedEntities = UpdatedEntities;
                var deps = job.ScheduleSingle(system, inputDeps);

                deps.Complete(); // Set does not expect another job running on NodeSetAttachment

                outputDeps = set.Update(deps);
            }
        }

        [Test]
        public void HookPatchJob_MatchesInternalRepatchJob()
        {
#pragma warning disable 618  // warning CS0618: 'IJobForEach' is obsolete: 'Please use Entities.ForEach or IJobChunk to schedule jobs that work on Entities. (RemovedAfter 2020-06-20)
            Assert.True(typeof(IJobForEachWithEntity_EB<NodeSetAttachment>).IsAssignableFrom(typeof(HookPatchJob)));
            Assert.True(typeof(IJobForEachWithEntity_EB<NodeSetAttachment>).IsAssignableFrom(typeof(RepatchDFGInputsIfNeededJob)));
#pragma warning restore 618

            var hookAttributes = 
                typeof(HookPatchJob)
                .GetCustomAttributes(true)
                .Where(o => !(o is BurstCompileAttribute));

            var internalAttributes = 
                typeof(RepatchDFGInputsIfNeededJob)
                .GetCustomAttributes(true)
                .Where(o => !(o is BurstCompileAttribute));

            CollectionAssert.AreEqual(hookAttributes, internalAttributes);

            var hookParams = typeof(HookPatchJob).GetMethod("Execute").GetParameters();
            var internalParams = typeof(RepatchDFGInputsIfNeededJob).GetMethod("Execute").GetParameters();

            // Defaults compare by name... !
            CollectionAssert.AreEqual(
                hookParams.Select(p => p.ParameterType), 
                internalParams.Select(p => p.ParameterType)
            );

            for(int i = 0; i < hookParams.Length; ++i)
            {
                CollectionAssert.AreEqual(
                    hookParams[i].GetCustomAttributes(true), 
                    internalParams[i].GetCustomAttributes(true)
                );
            }
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
                var patch = GetPortPatch(Receiver, SimpleNode_WithECSTypes_OnInputs.KernelPorts.Input);
                Assert.True(*patch == GetComponent<DataOne>(Changed));
            }

            public unsafe T** GetPortPatch<T, TNode>(NodeHandle<TNode> handle, DataInput<TNode, T> id)
                where TNode : NodeDefinition
                where T : unmanaged
            {
                var graph = Set.DataGraph;
                graph.SyncAnyRendering();
                
                var knode = graph.GetInternalData()[handle.VHandle.Index];
                ref readonly var traits = ref knode.TraitsHandle.Resolve();

                return (T**)traits.DataPorts.FindInputDataPort(id.Port).GetPointerToPatch(knode.Instance.Ports);
            }

            public unsafe T* GetComponent<T>(Entity e)
                where T : unmanaged
            {
                return (T*)World.EntityManager.EntityComponentStore->GetComponentDataWithTypeRO(e, ComponentType.ReadWrite<T>().TypeIndex);
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
                *f.GetPortPatch(f.Receiver, SimpleNode_WithECSTypes_OnInputs.KernelPorts.Input) = null;

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
                *f.GetPortPatch(f.Receiver, SimpleNode_WithECSTypes_OnInputs.KernelPorts.Input) = null;

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
                var oldMemoryPointer = *f.GetPortPatch(f.Receiver, SimpleNode_WithECSTypes_OnInputs.KernelPorts.Input);

                f.EM.DestroyEntity(f.Changed);
                f.Update();

                // Memory mustn't point to a partially destroyed entity.
                Assert.False(oldMemoryPointer == *f.GetPortPatch(f.Receiver, SimpleNode_WithECSTypes_OnInputs.KernelPorts.Input));
                // It's still contained in this list since NodeSetAttachment is a system state.
                CollectionAssert.Contains(f.SystemDelegate.DequeueToList(), f.Changed);

                f.Set.Destroy(f.ChangedNode);
                f.ChangedNode = default; // Don't double release it
                f.Update();

                // Memory mustn't point to a partially destroyed entity.
                Assert.False(oldMemoryPointer == *f.GetPortPatch(f.Receiver, SimpleNode_WithECSTypes_OnInputs.KernelPorts.Input));
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
                Assert.IsTrue(f.GetPortPatch(dfgNode, SimpleNode_WithECSTypes.KernelPorts.Input) == graphKernel.Outputs[0].DFGPatch);

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
    }
}