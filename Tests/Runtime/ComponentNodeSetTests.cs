using System;
using NUnit.Framework;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Unity.DataFlowGraph.Tests
{
#pragma warning disable 649 // non-public unassigned default value

    public class ComponentNodeSetTests
    {
        internal struct SimpleData : IComponentData
        {
            public double Something;
            public int SomethingElse;
        }

        internal struct SimpleBuffer : IBufferElementData
        {
            public float3 Values;

            public override string ToString()
            {
                return Values.ToString();
            }
        }

        internal struct DataTwo : IComponentData
        {
            public double Something;
            public int SomethingElse;
        }

        internal struct DataOne : IComponentData
        {
            public double Something;
            public int SomethingElse;
        }

        struct ZeroSizedComponentData : IComponentData
        {
        }

        public struct InstanceData : INodeData { }
        public struct KernelData : IKernelData { }

        internal class SimpleNode_WithECSTypes_OnInputs : NodeDefinition<InstanceData, KernelData, SimpleNode_WithECSTypes_OnInputs.KernelDefs, SimpleNode_WithECSTypes_OnInputs.GraphKernel>
        {
            
            public struct KernelDefs : IKernelPortDefinition
            {
                public DataInput<SimpleNode_WithECSTypes_OnInputs, DataOne> Input;
                public DataInput<SimpleNode_WithECSTypes_OnInputs, DataTwo> Input2;
                public DataInput<SimpleNode_WithECSTypes_OnInputs, Buffer<SimpleBuffer>> InputBuffer;

            }

            public struct GraphKernel : IGraphKernel<KernelData, KernelDefs>
            {
                public void Execute(RenderContext ctx, KernelData data, ref KernelDefs ports) { }
            }
        }

        internal class SimpleNode_WithECSTypes_InPortArray_OnInputs : NodeDefinition<InstanceData, KernelData, SimpleNode_WithECSTypes_InPortArray_OnInputs.KernelDefs, SimpleNode_WithECSTypes_InPortArray_OnInputs.GraphKernel>
        {
            public struct KernelDefs : IKernelPortDefinition
            {
                public PortArray<DataInput<SimpleNode_WithECSTypes_InPortArray_OnInputs, DataOne>> Input;
            }

            public struct GraphKernel : IGraphKernel<KernelData, KernelDefs>
            {
                public void Execute(RenderContext ctx, KernelData data, ref KernelDefs ports) { }
            }
        }

        internal class SimpleNode_WithECSTypes_OnOutputs : NodeDefinition<InstanceData, KernelData, SimpleNode_WithECSTypes_OnOutputs.KernelDefs, SimpleNode_WithECSTypes_OnOutputs.GraphKernel>
        {
            public struct KernelDefs : IKernelPortDefinition
            {
                public DataInput<SimpleNode_WithECSTypes_OnOutputs, DataOne> Output1;
                public DataInput<SimpleNode_WithECSTypes_OnOutputs, DataTwo> Output2;
                public DataInput<SimpleNode_WithECSTypes_OnOutputs, Buffer<SimpleBuffer>> OutputBuffer;

            }

            public struct GraphKernel : IGraphKernel<KernelData, KernelDefs>
            {
                public void Execute(RenderContext ctx, KernelData data, ref KernelDefs ports) { }
            }
        }

        internal class SimpleNode_WithECSTypes : NodeDefinition<SimpleNode_WithECSTypes.InstanceData, SimpleNode_WithECSTypes.KernelData, SimpleNode_WithECSTypes.KernelDefs, SimpleNode_WithECSTypes.GraphKernel>
        {
            public struct InstanceData : INodeData { }
            public struct KernelData : IKernelData { }

            public struct KernelDefs : IKernelPortDefinition
            {
                public DataInput<SimpleNode_WithECSTypes, SimpleData> Input;
                public DataOutput<SimpleNode_WithECSTypes, SimpleData> Output;
                public DataOutput<SimpleNode_WithECSTypes, Buffer<SimpleBuffer>> OutputBuffer;
            }

            public struct GraphKernel : IGraphKernel<KernelData, KernelDefs>
            {
                public void Execute(RenderContext ctx, KernelData data, ref KernelDefs ports)
                {
                    ctx.Resolve(ref ports.Output) = ctx.Resolve(ports.Input);
                }
            }
        }

        public abstract class HostJobSystem : JobComponentSystem
        {
            public NodeSet Set;
        }

        public class Fixture<TSystem> : IDisposable
            where TSystem : HostJobSystem
        {
            public World World;
            public EntityManager EM => World.EntityManager;
            public NodeSet Set;
            public TSystem System;

            public Fixture()
            {
                World = new World("ComponentNodeSetTests");
                System = World.GetOrCreateSystem<TSystem>();
                Set = System.Set = new NodeSet(System);
            }

            public void Dispose()
            {
                Set.Dispose();
                World.Dispose();
            }
        }

        [DisableAutoCreation, AlwaysUpdateSystem]
        public class UpdateSystem : HostJobSystem
        {
            protected override JobHandle OnUpdate(JobHandle inputDeps)
            {
                return Set.Update(inputDeps);
            }
        }

        [Test]
        public void NodeSetCreated_WithECSConstructor_HasCreatedComponentTypesArray()
        {
            using (var f = new Fixture<UpdateSystem>())
            {
                Assert.IsTrue(f.Set.GetActiveComponentTypes().IsCreated);
            }
        }

        [Test]
        public void ECSNodeSetConstructor_ThrowsException_OnInvalidArgument()
        {
            Assert.Throws<ArgumentNullException>(() => new NodeSet(null));
        }

        [Test]
        public void CanUpdateSimpleSystem()
        {
            using (var f = new Fixture<UpdateSystem>())
            {
                f.System.Update();
            }
        }

        [Test]
        public void UpdatingECSNodeSet_UsingNonECSUpdateFunction_ThrowsException()
        {
            using (var f = new Fixture<UpdateSystem>())
            {
                Assert.Throws<InvalidOperationException>(() => f.Set.Update());
            }
        }

        [Test]
        public void UpdatingNormalSet_UsingECSUpdateFunction_ThrowsException()
        {
            using (var set = new NodeSet())
            {
                Assert.Throws<InvalidOperationException>(() => set.Update(default));
            }
        }

        [DisableAutoCreation, AlwaysUpdateSystem]
        class System_WithSimpleProcessing : HostJobSystem
        {

            protected override void OnCreate()
            {
                for (int i = 0; i < 1000; ++i)
                    EntityManager.CreateEntity(typeof(SimpleData));
            }

            protected struct SimpleJob : IJobForEach<SimpleData>
            {
                public void Execute(ref SimpleData c0) { }
            }

            protected override JobHandle OnUpdate(JobHandle inputDeps)
            {
                return Set.Update(new SimpleJob().Schedule(this, inputDeps));
            }
        }

        [Test]
        public void CanUpdate_JobSchedulingSystem()
        {
            using (var f = new Fixture<System_WithSimpleProcessing>())
            {
                f.System.Update();
            }
        }

        [DisableAutoCreation, AlwaysUpdateSystem]
        class System_WithParallelScheduler : System_WithSimpleProcessing
        {
            protected override JobHandle OnUpdate(JobHandle inputDeps)
            {
                var job = new SimpleJob().Schedule(this, inputDeps);
                var dfg = Set.Update(inputDeps);

                return JobHandle.CombineDependencies(job, dfg);
            }
        }

        [Test]
        public void CanUpdate_ParallelJobSchedulingSystem()
        {
            using (var f = new Fixture<System_WithParallelScheduler>())
            {
                f.System.Update();
            }
        }

// Section for code testing atomic safety handle functionality, like race conditions. Only takes effect under following define.
#if ENABLE_UNITY_COLLECTIONS_CHECKS

        [DisableAutoCreation, AlwaysUpdateSystem]
        class System_WithRaceCondition : System_WithSimpleProcessing
        {
            public bool ExpectException = false;

            protected override JobHandle OnUpdate(JobHandle inputDeps)
            {
                JobHandle job = default;
                var dfg = Set.Update(inputDeps);

                if (ExpectException)
                    Assert.Throws<InvalidOperationException>(() => job = new SimpleJob().Schedule(this, inputDeps));
                else
                    job = new SimpleJob().Schedule(this, inputDeps);

                return JobHandle.CombineDependencies(job, dfg);
            }
        }

        [Test]
        public void SchedulingParallelJobs_UsingSameTypesAsDFG_ResultsInRaceConditionException()
        {
            using (var f = new Fixture<System_WithRaceCondition>())
            {
                f.System.Update();

                var node = f.Set.Create<SimpleNode_WithECSTypes>();
                f.System.ExpectException = true;
                f.System.Update();

                f.System.ExpectException = true;
                f.System.Update();

                // (Dependencies are only ever added).
                f.Set.Destroy(node);
                f.System.ExpectException = true;
                f.System.Update();
            }
        }

#endif

        [Test]
        public void CreatingNode_WithECSTypesOnInputs_CorrectlyUpdates_ActiveComponentTypes()
        {
            using (var f = new Fixture<UpdateSystem>())
            {
                Assert.Zero(f.Set.GetActiveComponentTypes().Count);

                var node = f.Set.Create<SimpleNode_WithECSTypes_OnInputs>();
                var componentTypes = f.Set.GetActiveComponentTypes();

                Assert.AreEqual(3, componentTypes.Count);

                // TODO: These should be read-only, but currently not supported.
                Assert.AreEqual(ComponentType.ReadWrite<DataOne>(), componentTypes[0].Type);
                Assert.AreEqual(ComponentType.ReadWrite<DataTwo>(), componentTypes[1].Type);
                Assert.AreEqual(ComponentType.ReadWrite<SimpleBuffer>(), componentTypes[2].Type);

                f.Set.Destroy(node);
            }
        }

        [Test]
        public void CreatingNode_WithECSTypes_InPortArray_OnInputs_CorrectlyUpdates_ActiveComponentTypes()
        {
            using (var f = new Fixture<UpdateSystem>())
            {
                Assert.Zero(f.Set.GetActiveComponentTypes().Count);

                var node = f.Set.Create<SimpleNode_WithECSTypes_InPortArray_OnInputs>();
                var componentTypes = f.Set.GetActiveComponentTypes();

                Assert.AreEqual(1, componentTypes.Count);

                // TODO: These should be read-only, but currently not supported.
                Assert.AreEqual(ComponentType.ReadWrite<DataOne>(), componentTypes[0].Type);

                f.Set.Destroy(node);
            }
        }

        [Test]
        public void CreatingNode_WithECSTypesOnOutputs_CorrectlyUpdates_ActiveComponentTypes()
        {
            using (var f = new Fixture<UpdateSystem>())
            {
                Assert.Zero(f.Set.GetActiveComponentTypes().Count);

                var node = f.Set.Create<SimpleNode_WithECSTypes_OnOutputs>();
                var componentTypes = f.Set.GetActiveComponentTypes();

                Assert.AreEqual(3, componentTypes.Count);

                Assert.AreEqual(ComponentType.ReadWrite<DataOne>(), componentTypes[0].Type);
                Assert.AreEqual(ComponentType.ReadWrite<DataTwo>(), componentTypes[1].Type);
                Assert.AreEqual(ComponentType.ReadWrite<SimpleBuffer>(), componentTypes[2].Type);

                f.Set.Destroy(node);
            }
        }

        [Test]
        public void CreatingDifferentNodes_WithDifferentECSTypes_CorrectlyUpdates_ActiveComponentTypes()
        {
            using (var f = new Fixture<UpdateSystem>())
            {
                Assert.Zero(f.Set.GetActiveComponentTypes().Count);

                var node = f.Set.Create<SimpleNode_WithECSTypes_OnOutputs>();

                Assert.AreEqual(3, f.Set.GetActiveComponentTypes().Count);
                var node2 = f.Set.Create<SimpleNode_WithECSTypes>();

                var componentTypes = f.Set.GetActiveComponentTypes();
                Assert.AreEqual(4, componentTypes.Count);

                Assert.AreEqual(ComponentType.ReadWrite<DataOne>(), componentTypes[0].Type);
                Assert.AreEqual(ComponentType.ReadWrite<DataTwo>(), componentTypes[1].Type);
                Assert.AreEqual(ComponentType.ReadWrite<SimpleBuffer>(), componentTypes[2].Type);
                Assert.AreEqual(ComponentType.ReadWrite<SimpleData>(), componentTypes[3].Type);

                f.Set.Destroy(node, node2);
            }
        }

        [Test]
        public void CannotCreateNode_ContainingZeroSizedComponents()
        {
            Assert.Zero(NodeWithParametricPortType<ZeroSizedComponentData>.IL2CPP_ClassInitializer);

            using (var f = new Fixture<UpdateSystem>())
            {
                Assert.Throws<InvalidNodeDefinitionException>(() => f.Set.Create<NodeWithParametricPortType<ZeroSizedComponentData>>());
            }
        }
    }

#pragma warning restore 649 // non-public unassigned default value
}

