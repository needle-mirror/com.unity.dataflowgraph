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

        public interface INodeSetSystemDelegate
        {
            void OnCreate(ComponentSystemBase system);
            void OnDestroy(ComponentSystemBase system, NodeSet set);
            void OnUpdate(ComponentSystemBase system, NodeSet set, JobHandle inputDeps, out JobHandle outputDeps);
        }

        [AlwaysUpdateSystem]
        public class HostSystemBase<TNodeSetSystemDelegate> : SystemBase
            where TNodeSetSystemDelegate : INodeSetSystemDelegate, new()
        {
            public NodeSet Set;
            public TNodeSetSystemDelegate SystemDelegate = new TNodeSetSystemDelegate();

            protected override void OnCreate()
            {
                SystemDelegate.OnCreate(this);
            }

            protected override void OnDestroy()
            {
                SystemDelegate.OnDestroy(this, Set);
            }

            protected override void OnUpdate()
            {
                SystemDelegate.OnUpdate(this, Set, Dependency, out var outputDeps);
                Dependency = outputDeps;
            }
        }

        [AlwaysUpdateSystem]
        public class HostJobComponentSystem<TNodeSetSystemDelegate> : JobComponentSystem
            where TNodeSetSystemDelegate : INodeSetSystemDelegate, new()
        {
            public NodeSet Set;
            public TNodeSetSystemDelegate SystemDelegate = new TNodeSetSystemDelegate();

            protected override void OnCreate()
            {
                SystemDelegate.OnCreate(this);
            }

            protected override void OnDestroy()
            {
                SystemDelegate.OnDestroy(this, Set);
            }

            protected override JobHandle OnUpdate(JobHandle inputDeps)
            {
                SystemDelegate.OnUpdate(this, Set, inputDeps, out var outputDeps);
                return outputDeps;
            }
        }

        [AlwaysUpdateSystem]
        public class HostComponentSystem<TNodeSetSystemDelegate> : ComponentSystem
            where TNodeSetSystemDelegate : INodeSetSystemDelegate, new()
        {
            public NodeSet Set;
            public TNodeSetSystemDelegate SystemDelegate = new TNodeSetSystemDelegate();

            protected override void OnCreate()
            {
                SystemDelegate.OnCreate(this);
            }

            protected override void OnDestroy()
            {
                SystemDelegate.OnDestroy(this, Set);
            }

            protected override void OnUpdate()
            {
                SystemDelegate.OnUpdate(this, Set, default, out var outputDeps);
                outputDeps.Complete();
            }
        }

        public enum FixtureSystemType { ComponentSystem, JobComponentSystem, SystemBase }

        public class Fixture<TNodeSetSystemDelegate> : IDisposable
            where TNodeSetSystemDelegate : INodeSetSystemDelegate, new()
        {
            public World World;
            public EntityManager EM => World.EntityManager;
            public NodeSet Set;
            public ComponentSystemBase System;
            public TNodeSetSystemDelegate SystemDelegate;

            public Fixture(FixtureSystemType systemType)
            {
                World = new World("ComponentNodeSetTests");
                switch (systemType)
                {
                    case FixtureSystemType.ComponentSystem:
                        var componentSys = World.GetOrCreateSystem<HostComponentSystem<TNodeSetSystemDelegate>>();
                        componentSys.Set = Set = new NodeSet(componentSys);
                        System = componentSys;
                        SystemDelegate = componentSys.SystemDelegate;
                        break;

                    case FixtureSystemType.JobComponentSystem:
                        var jobComponentSys = World.GetOrCreateSystem<HostJobComponentSystem<TNodeSetSystemDelegate>>();
                        jobComponentSys.Set = Set = new NodeSet(jobComponentSys);
                        System = jobComponentSys;
                        SystemDelegate = jobComponentSys.SystemDelegate;
                        break;

                    case FixtureSystemType.SystemBase:
                        var sysBase = World.GetOrCreateSystem<HostSystemBase<TNodeSetSystemDelegate>>();
                        sysBase.Set = Set = new NodeSet(sysBase);
                        System = sysBase;
                        SystemDelegate = sysBase.SystemDelegate;
                        break;
                }
            }

            public void Dispose()
            {
                Set.Dispose();
                World.Dispose();
            }
        }

        public class UpdateSystemDelegate : INodeSetSystemDelegate
        {
            public void OnCreate(ComponentSystemBase system) {}
            public void OnDestroy(ComponentSystemBase system, NodeSet set) {}
            public void OnUpdate(ComponentSystemBase system, NodeSet set, JobHandle inputDeps, out JobHandle outputDeps)
            {
                outputDeps = set.Update(inputDeps);
            }
        }

        [Test]
        public void NodeSetCreated_WithECSConstructor_HasCreatedComponentTypesArray([Values] FixtureSystemType systemType)
        {
            using (var f = new Fixture<UpdateSystemDelegate>(systemType))
            {
                Assert.IsTrue(f.Set.GetActiveComponentTypes().IsCreated);
            }
        }

        [Test]
        public void ECSNodeSetConstructor_ThrowsException_OnInvalidArgument([Values] FixtureSystemType systemType)
        {
            Assert.Throws<ArgumentNullException>(() => new NodeSet(null));
        }

        [Test]
        public void CanUpdateSimpleSystem([Values] FixtureSystemType systemType)
        {
            using (var f = new Fixture<UpdateSystemDelegate>(systemType))
            {
                f.System.Update();
            }
        }

        [Test]
        public void UpdatingECSNodeSet_UsingNonECSUpdateFunction_ThrowsException([Values] FixtureSystemType systemType)
        {
            using (var f = new Fixture<UpdateSystemDelegate>(systemType))
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

        class SimpleProcessingSystemDelegate : INodeSetSystemDelegate
        {
            public void OnCreate(ComponentSystemBase system)
            {
                for (int i = 0; i < 1000; ++i)
                    system.EntityManager.CreateEntity(typeof(SimpleData));
            }

            public void OnDestroy(ComponentSystemBase system, NodeSet set) {}

            protected struct SimpleJob
#pragma warning disable 618  // warning CS0618: 'IJobForEach' is obsolete: 'Please use Entities.ForEach or IJobChunk to schedule jobs that work on Entities. (RemovedAfter 2020-06-20)
                : IJobForEach<SimpleData>
#pragma warning restore 618
            {
                public void Execute(ref SimpleData c0) { }
            }

            public virtual void OnUpdate(ComponentSystemBase system, NodeSet set, JobHandle inputDeps, out JobHandle outputDeps)
            {
                outputDeps = set.Update(new SimpleJob().Schedule(system, inputDeps));
            }
        }

        [Test]
        public void CanUpdate_JobSchedulingSystem([Values] FixtureSystemType systemType)
        {
            using (var f = new Fixture<SimpleProcessingSystemDelegate>(systemType))
            {
                f.System.Update();
            }
        }

        class ParallelSchedulerSystemDelegate : SimpleProcessingSystemDelegate
        {
            public override void OnUpdate(ComponentSystemBase system, NodeSet set, JobHandle inputDeps, out JobHandle outputDeps)
            {
                var job = new SimpleJob().Schedule(system, inputDeps);
                var dfg = set.Update(inputDeps);

                outputDeps = JobHandle.CombineDependencies(job, dfg);
            }
        }

        [Test]
        public void CanUpdate_ParallelJobSchedulingSystem([Values] FixtureSystemType systemType)
        {
            using (var f = new Fixture<ParallelSchedulerSystemDelegate>(systemType))
            {
                f.System.Update();
            }
        }

// Section for code testing atomic safety handle functionality, like race conditions. Only takes effect under following define.
#if ENABLE_UNITY_COLLECTIONS_CHECKS

        class RaceConditionSystemDelegate : SimpleProcessingSystemDelegate
        {
            public bool ExpectException = false;

            public override void OnUpdate(ComponentSystemBase system, NodeSet set, JobHandle inputDeps, out JobHandle outputDeps)
            {
                JobHandle job = default;
                var dfg = set.Update(inputDeps);

                if (ExpectException)
                    Assert.Throws<InvalidOperationException>(() => job = new SimpleJob().Schedule(system, inputDeps));
                else
                    job = new SimpleJob().Schedule(system, inputDeps);

                outputDeps = JobHandle.CombineDependencies(job, dfg);
            }
        }

        [Test]
        public void SchedulingParallelJobs_UsingSameTypesAsDFG_ResultsInRaceConditionException([Values] FixtureSystemType systemType)
        {
            using (var f = new Fixture<RaceConditionSystemDelegate>(systemType))
            {
                f.System.Update();

                var node = f.Set.Create<SimpleNode_WithECSTypes>();
                f.SystemDelegate.ExpectException = true;
                f.System.Update();

                f.SystemDelegate.ExpectException = true;
                f.System.Update();

                // (Dependencies are only ever added).
                f.Set.Destroy(node);
                f.SystemDelegate.ExpectException = true;
                f.System.Update();
            }
        }

#endif

        [Test]
        public void CreatingNode_WithECSTypesOnInputs_CorrectlyUpdates_ActiveComponentTypes([Values] FixtureSystemType systemType)
        {
            using (var f = new Fixture<UpdateSystemDelegate>(systemType))
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
        public void CreatingNode_WithECSTypes_InPortArray_OnInputs_CorrectlyUpdates_ActiveComponentTypes([Values] FixtureSystemType systemType)
        {
            using (var f = new Fixture<UpdateSystemDelegate>(systemType))
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
        public void CreatingNode_WithECSTypesOnOutputs_CorrectlyUpdates_ActiveComponentTypes([Values] FixtureSystemType systemType)
        {
            using (var f = new Fixture<UpdateSystemDelegate>(systemType))
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
        public void CreatingDifferentNodes_WithDifferentECSTypes_CorrectlyUpdates_ActiveComponentTypes([Values] FixtureSystemType systemType)
        {
            using (var f = new Fixture<UpdateSystemDelegate>(systemType))
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
        public void CannotCreateNode_ContainingZeroSizedComponents([Values] FixtureSystemType systemType)
        {
            Assert.Zero(NodeWithParametricPortType<ZeroSizedComponentData>.IL2CPP_ClassInitializer);

            using (var f = new Fixture<UpdateSystemDelegate>(systemType))
            {
                Assert.Throws<InvalidNodeDefinitionException>(() => f.Set.Create<NodeWithParametricPortType<ZeroSizedComponentData>>());
            }
        }
    }

#pragma warning restore 649 // non-public unassigned default value
}

