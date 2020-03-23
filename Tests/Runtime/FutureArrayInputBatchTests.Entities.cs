using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine.TestTools;
using Unity.DataFlowGraph;

#pragma warning disable 612,618


[assembly: RegisterGenericComponentType(typeof(NodeMemoryInput<Unity.DataFlowGraph.Tests.InputBatchTests.Entities.MemoryInputTag, Unity.DataFlowGraph.Tests.InputBatchTests.Entities.ECSBuffer>))]
[assembly: RegisterGenericComponentType(typeof(NodeMemoryInput<Unity.DataFlowGraph.Tests.InputBatchTests.Entities.MemoryInputTag, Unity.DataFlowGraph.Tests.InputBatchTests.Entities.ECSIntBuffer>))]
[assembly: RegisterGenericComponentType(typeof(NodeMemoryInput<Unity.DataFlowGraph.Tests.InputBatchTests.Entities.MemoryInputTag, Unity.DataFlowGraph.Tests.InputBatchTests.Entities.WeirdStruct>))]
[assembly: RegisterGenericComponentType(typeof(NodeMemoryInput<Unity.DataFlowGraph.Tests.InputBatchTests.Entities.MemoryInputTag, Unity.DataFlowGraph.Tests.InputBatchTests.Entities.IncompatibleBufferElement>))]

namespace Unity.DataFlowGraph.Tests
{
    public partial class InputBatchTests
    {
        public class Entities
        {
            public struct WeirdStruct : IBufferElementData
            {
                int m_A; float m_B; double m_C;
            }

            public struct MemoryInputTag : INodeMemoryInputTag { }

            public struct ECSBuffer : IBufferElementData
            {
                public float Value;
            }

            class InputSystem<TBuffer> : MemoryInputSystem<MemoryInputTag, TBuffer>
                where TBuffer : struct, IBufferElementData
            {
                public NodeSet Set { get => UnownedSet; set => UnownedSet = value; }

                public new JobHandle OnUpdate(JobHandle inputDeps)
                {
                    return base.OnUpdate(inputDeps);
                }
            }

            [Test]
            public void InstantiatingMemoryInputSystem_SpawnsAssociatedMainThreadChecker()
            {
                using (var world = new World("DataFlowGraph testing"))
                {
                    var memorySystem = world.CreateSystem<InputSystem<ECSBuffer>>();
                    Assert.IsNotNull(world.GetExistingSystem<InputSystem<ECSBuffer>.MainThreadChecker>());
                }
            }

            [Test]
            public void InstantiatingMemoryInputSystem_AndUpdating_WorksWithoutAssociatedNodeSet()
            {
                using (var world = new World("DataFlowGraph testing"))
                {
                    var memorySystem = world.CreateSystem<InputSystem<ECSBuffer>>();

                    memorySystem.Update();
                }
            }

            [Test]
            public void FreeRunningMemoryInputSystem_WithValidSet_CanUpdate([Values(0, 1, 2, 10, 20, 50)] int numUpdates)
            {
                using (var set = new NodeSet())
                using (var world = new World("DataFlowGraph testing"))
                {
                    var memorySystem = world.CreateSystem<InputSystem<ECSBuffer>>();
                    memorySystem.Set = set;

                    // Close to useless: ECS does not update systems without matching archetypes
                    for (int i = 0; i < numUpdates; ++i)
                        memorySystem.Update();
                }
            }

            [Test]
            public void FreeRunningMemoryInputSystem_WithValidSet_CanOnUpdate([Values(0, 1, 2, 10, 20, 50)] int numUpdates)
            {
                using (var set = new NodeSet())
                using (var world = new World("DataFlowGraph testing"))
                {
                    var memorySystem = world.CreateSystem<InputSystem<ECSBuffer>>();
                    memorySystem.Set = set;

                    var renderVersion = set.DataGraph.RenderVersion;

                    JobHandle deps = default;
                    for (int i = 0; i < numUpdates; ++i)
                    {
                        // Calling OnUpdate (instead of Update) forces the system to run
                        deps = memorySystem.OnUpdate(deps);
                        Assert.AreNotEqual(renderVersion, set.DataGraph.RenderVersion);
                        renderVersion = set.DataGraph.RenderVersion;
                    }

                    deps.Complete();
                }
            }

            [Test]
            public void CreateMemoryEntity_WithInvalidNodeTarget_WritesError()
            {
                using (var world = new World("DataFlowGraph testing"))
                using (var set = new NodeSet())
                {
                    var memorySystem = world.CreateSystem<InputSystem<ECSBuffer>>();
                    var entityManager = world.EntityManager;
                    var entity = entityManager.CreateEntity();

                    entityManager.AddBuffer<ECSBuffer>(entity);
                    entityManager.AddComponent(entity, ComponentType.ReadWrite<NodeMemoryInput<MemoryInputTag, ECSBuffer>>());

                    LogAssert.Expect(UnityEngine.LogType.Error, new Regex("Node is disposed or invalid"));

                    memorySystem.Set = set;
                    memorySystem.Update();
                }
            }

            public class InputECSBufferNode
                : NodeDefinition<InputECSBufferNode.Node, InputECSBufferNode.Data, InputECSBufferNode.KernelDefs, InputECSBufferNode.Kernel>
            {
                public struct Node : INodeData { }

                public struct Data : IKernelData { }

                public struct KernelDefs : IKernelPortDefinition
                {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                    public DataInput<InputECSBufferNode, Buffer<ECSBuffer>> ECSInput;
                    public PortArray<DataInput<InputECSBufferNode, Buffer<ECSBuffer>>> ECSInputArray;
                    public DataOutput<InputECSBufferNode, float> Sum;
#pragma warning restore 649
                }

                [BurstCompile(CompileSynchronously = true)]
                public struct Kernel : IGraphKernel<Data, KernelDefs>
                {
                    public void Execute(RenderContext ctx, Data data, ref KernelDefs ports)
                    {
                        float sum = 0;
                        var input = ctx.Resolve(ports.ECSInput);
                        for (int i = 0; i < input.Length; ++i)
                            sum += input[i].Value;

                        var portArray = ctx.Resolve(ports.ECSInputArray);
                        for (int i = 0; i < portArray.Length; ++i)
                        {
                            input = ctx.Resolve(portArray[i]);
                            for (int j = 0; j < input.Length; ++j)
                                sum += input[j].Value;
                        }
                        ctx.Resolve(ref ports.Sum) = sum;
                    }
                }
            }

            public enum APIType
            {
                StronglyTyped,
                WeaklyTyped
            }

            [Test]
            public void CreatingMemoryEntity_ResolvesDestination([Values] APIType apiType)
            {
#if UNITY_EDITOR
                // FIXME: Without calling this here, later, LowLevelNodeTraits complains that Buffer<ECSBuffer> is not blittable!?!?!
                new DataBufferTests().TestGenericType(50, new ECSBuffer());
#endif

                using (var world = new World("DataFlowGraph testing"))
                using (var set = new NodeSet())
                {
                    var node = set.Create<InputECSBufferNode>();
                    var memorySystem = world.CreateSystem<InputSystem<ECSBuffer>>();
                    var entityManager = world.EntityManager;
                    var entity = entityManager.CreateEntity();

                    entityManager.AddBuffer<ECSBuffer>(entity);

                    var memory = apiType == APIType.WeaklyTyped
                        ? new NodeMemoryInput<MemoryInputTag, ECSBuffer>(node, (InputPortID) InputECSBufferNode.KernelPorts.ECSInput)
                        : NodeMemoryInput<MemoryInputTag, ECSBuffer>.Create(node, InputECSBufferNode.KernelPorts.ECSInput);

                    entityManager.AddComponentData(entity, memory);

                    memorySystem.Set = set;
                    memorySystem.Update();

                    var checkedComponent = entityManager.GetComponentData<NodeMemoryInput<MemoryInputTag, ECSBuffer>>(entity);

                    Assert.AreEqual(memory.Node, checkedComponent.ResolvedDestination.Handle.ToPublicHandle());
                    Assert.AreEqual(memory.InternalPort, checkedComponent.ResolvedDestination.Port);

                    // Assert memory checked component is added to entity.
                    set.Destroy(node);
                }
            }

            [Test]
            public void ChangingMemoryEntity_ResolvesNewDestination()
            {
#if UNITY_EDITOR
                // FIXME: Without calling this here, later, LowLevelNodeTraits complains that Buffer<ECSBuffer> is not blittable!?!?!
                new DataBufferTests().TestGenericType(50, new ECSBuffer());
#endif

                using (var world = new World("DataFlowGraph testing"))
                using (var set = new NodeSet())
                {
                    var a = set.Create<InputECSBufferNode>();
                    var b = set.Create<InputECSBufferNode>();
                    var memorySystem = world.CreateSystem<InputSystem<ECSBuffer>>();
                    var entityManager = world.EntityManager;
                    var entity = entityManager.CreateEntity();

                    entityManager.AddBuffer<ECSBuffer>(entity);

                    var memoryA = new NodeMemoryInput<MemoryInputTag, ECSBuffer>(a, (InputPortID)InputECSBufferNode.KernelPorts.ECSInput);

                    entityManager.AddComponentData(entity, memoryA);

                    memorySystem.Set = set;
                    memorySystem.Update();

                    var checkedComponentA = entityManager.GetComponentData<NodeMemoryInput<MemoryInputTag, ECSBuffer>>(entity);

                    Assert.AreEqual(memoryA.Node, checkedComponentA.ResolvedDestination.Handle.ToPublicHandle());
                    Assert.AreEqual(memoryA.InternalPort, checkedComponentA.ResolvedDestination.Port);

                    var memoryB = new NodeMemoryInput<MemoryInputTag, ECSBuffer>(b, (InputPortID)InputECSBufferNode.KernelPorts.ECSInput);

                    entityManager.SetComponentData(entity, memoryB);

                    memorySystem.Update();

                    var checkedComponentB = entityManager.GetComponentData<NodeMemoryInput<MemoryInputTag, ECSBuffer>>(entity);

                    Assert.AreEqual(memoryB.Node, checkedComponentB.ResolvedDestination.Handle.ToPublicHandle());
                    Assert.AreEqual(memoryB.InternalPort, checkedComponentB.ResolvedDestination.Port);

                    // Assert memory checked component is added to entity.
                    set.Destroy(a, b);
                }
            }

            [Test]
            public void RemovingBufferTypes_UnresolvesDestination()
            {
#if UNITY_EDITOR
                // FIXME: Without calling this here, later, LowLevelNodeTraits complains that Buffer<ECSBuffer> is not blittable!?!?!
                new DataBufferTests().TestGenericType(50, new ECSBuffer());
#endif

                using (var world = new World("DataFlowGraph testing"))
                using (var set = new NodeSet())
                {
                    var node = set.Create<InputECSBufferNode>();
                    var memorySystem = world.CreateSystem<InputSystem<ECSBuffer>>();
                    var entityManager = world.EntityManager;
                    var entity = entityManager.CreateEntity();

                    entityManager.AddBuffer<ECSBuffer>(entity);

                    var memory = new NodeMemoryInput<MemoryInputTag, ECSBuffer>(node, (InputPortID)InputECSBufferNode.KernelPorts.ECSInput);

                    entityManager.AddComponentData(entity, memory);

                    memorySystem.Set = set;
                    memorySystem.Update();

                    var checkedComponent = entityManager.GetComponentData<NodeMemoryInput<MemoryInputTag, ECSBuffer>>(entity);
                    Assert.AreEqual(memory.Node, checkedComponent.ResolvedDestination.Handle.ToPublicHandle());
                    Assert.AreEqual(memory.InternalPort, checkedComponent.ResolvedDestination.Port);

                    entityManager.RemoveComponent<ECSBuffer>(entity);
                    memorySystem.Update();

                    checkedComponent = entityManager.GetComponentData<NodeMemoryInput<MemoryInputTag, ECSBuffer>>(entity);
                    Assert.AreEqual(new NodeHandle(), checkedComponent.ResolvedDestination.Handle.ToPublicHandle());
                    Assert.AreEqual(new InputPortArrayID(), checkedComponent.ResolvedDestination.Port);

                    entityManager.AddBuffer<ECSBuffer>(entity);
                    memorySystem.Update();
                    checkedComponent = entityManager.GetComponentData<NodeMemoryInput<MemoryInputTag, ECSBuffer>>(entity);
                    Assert.AreEqual(memory.Node, checkedComponent.ResolvedDestination.Handle.ToPublicHandle());
                    Assert.AreEqual(memory.InternalPort, checkedComponent.ResolvedDestination.Port);

                    // Assert memory checked component is added to entity.
                    set.Destroy(node);
                }
            }

            [Test]
            public void AssigningIncompatibleDataType_ProducesDeferredError()
            {
                using (var set = new NodeSet())
                using (var world = new World("DataFlowGraph testing"))
                {
                    var memorySystem = world.CreateSystem<InputSystem<WeirdStruct>>();
                    var a = set.Create<NodeWithAllTypesOfPorts>();

                    var entityManager = world.EntityManager;
                    var entity = entityManager.CreateEntity();

                    entityManager.AddBuffer<WeirdStruct>(entity);

                    var memory = new NodeMemoryInput<MemoryInputTag, WeirdStruct>(a, (InputPortID)NodeWithAllTypesOfPorts.KernelPorts.InputBuffer);

                    entityManager.AddComponentData(entity, memory);

                    LogAssert.Expect(UnityEngine.LogType.Error, new Regex("Cannot assign a buffer of type"));

                    memorySystem.Set = set;
                    memorySystem.Update();

                    set.Destroy(a);
                }
            }

            [Test]
            public void AssigningToScalarPort_ProducesDeferredError()
            {
                using (var set = new NodeSet())
                using (var world = new World("DataFlowGraph testing"))
                {
                    var memorySystem = world.CreateSystem<InputSystem<ECSIntBuffer>>();
                    var a = set.Create<NodeWithAllTypesOfPorts>();

                    var entityManager = world.EntityManager;
                    var entity = entityManager.CreateEntity();

                    entityManager.AddBuffer<ECSIntBuffer>(entity);

                    var memory = new NodeMemoryInput<MemoryInputTag, ECSIntBuffer>(a, (InputPortID)NodeWithAllTypesOfPorts.KernelPorts.InputScalar);

                    entityManager.AddComponentData(entity, memory);

                    LogAssert.Expect(UnityEngine.LogType.Error, new Regex("Cannot assign a buffer to a non-Buffer type port"));

                    memorySystem.Set = set;
                    memorySystem.Update();

                    set.Destroy(a);
                }
            }

            public class AggregateInputBufferNode
                : NodeDefinition<AggregateInputBufferNode.Node, AggregateInputBufferNode.Data, AggregateInputBufferNode.KernelDefs, AggregateInputBufferNode.Kernel>
            {
                public struct Node : INodeData { }

                public struct Data : IKernelData { }

                public struct Aggregate
                {
                    public Buffer<int> BufferOfInt;
                }

                public struct KernelDefs : IKernelPortDefinition
                {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                    public DataInput<AggregateInputBufferNode, Aggregate> AggregateIntBufferInput;
#pragma warning restore 649
                }

                [BurstCompile(CompileSynchronously = true)]
                public struct Kernel : IGraphKernel<Data, KernelDefs>
                {
                    public void Execute(RenderContext ctx, Data data, ref KernelDefs ports)
                    {
                    }
                }
            }

            [Test]
            public void AssigningToAggregatePort_ProducesDeferredError()
            {
                using (var set = new NodeSet())
                using (var world = new World("DataFlowGraph testing"))
                {
                    var memorySystem = world.CreateSystem<InputSystem<ECSIntBuffer>>();
                    var a = set.Create<AggregateInputBufferNode>();

                    var entityManager = world.EntityManager;
                    var entity = entityManager.CreateEntity();

                    entityManager.AddBuffer<ECSIntBuffer>(entity);

                    var memory = new NodeMemoryInput<MemoryInputTag, ECSIntBuffer>(a, (InputPortID)AggregateInputBufferNode.KernelPorts.AggregateIntBufferInput);

                    entityManager.AddComponentData(entity, memory);

                    LogAssert.Expect(UnityEngine.LogType.Error, new Regex("Cannot assign a buffer to a non-Buffer type port"));

                    memorySystem.Set = set;
                    memorySystem.Update();

                    set.Destroy(a);
                }
            }

            [Test]
            public void AssigningIncorrectPortArray_ProducesDeferredError()
            {
                using (var set = new NodeSet())
                using (var world = new World("DataFlowGraph testing"))
                {
                    var memorySystem = world.CreateSystem<InputSystem<ECSBuffer>>();
                    var node = set.Create<InputECSBufferNode>();

                    var entityManager = world.EntityManager;
                    var entity = entityManager.CreateEntity();

                    entityManager.AddBuffer<ECSBuffer>(entity);

                    var memory = new NodeMemoryInput<MemoryInputTag, ECSBuffer>(node, (InputPortID)InputECSBufferNode.KernelPorts.ECSInput, 1);

                    entityManager.AddComponentData(entity, memory);
                    memorySystem.Set = set;

                    LogAssert.Expect(UnityEngine.LogType.Error, new Regex("An array index can only be given"));
                    memorySystem.Update();

                    memory = new NodeMemoryInput<MemoryInputTag, ECSBuffer>(node, (InputPortID)InputECSBufferNode.KernelPorts.ECSInputArray);
                    entityManager.SetComponentData(entity, memory);

                    LogAssert.Expect(UnityEngine.LogType.Error, new Regex("An array index is required"));
                    memorySystem.Update();

                    memory = NodeMemoryInput<MemoryInputTag, ECSBuffer>.Create(node, InputECSBufferNode.KernelPorts.ECSInputArray, 0);
                    entityManager.SetComponentData(entity, memory);

                    LogAssert.Expect(UnityEngine.LogType.Error, new Regex("Port array index"));
                    memorySystem.Update();


                    set.SetPortArraySize(node, InputECSBufferNode.KernelPorts.ECSInputArray, 5);
                    memory = new NodeMemoryInput<MemoryInputTag, ECSBuffer>(node, (InputPortID)InputECSBufferNode.KernelPorts.ECSInputArray, 10);
                    entityManager.SetComponentData(entity, memory);

                    LogAssert.Expect(UnityEngine.LogType.Error, new Regex("Port array index"));
                    memorySystem.Update();

                    memory = NodeMemoryInput<MemoryInputTag, ECSBuffer>.Create(node, InputECSBufferNode.KernelPorts.ECSInputArray, 8);
                    entityManager.SetComponentData(entity, memory);

                    LogAssert.Expect(UnityEngine.LogType.Error, new Regex("Port array index"));
                    memorySystem.Update();

                    set.Destroy(node);
                }
            }

            [Test]
            public void CannotAssignTo_DestroyedNode()
            {
                using (var set = new NodeSet())
                using (var world = new World("DataFlowGraph testing"))
                {
                    var memorySystem = world.CreateSystem<InputSystem<ECSBuffer>>();

                    var entityManager = world.EntityManager;
                    var entity = entityManager.CreateEntity();

                    entityManager.AddBuffer<ECSBuffer>(entity);

                    NodeMemoryInput<MemoryInputTag, ECSBuffer> memory = default;

                    entityManager.AddComponentData(entity, memory);

                    LogAssert.Expect(UnityEngine.LogType.Error, "Node is disposed or invalid");

                    memorySystem.Set = set;
                    memorySystem.Update();
                }
            }

            struct NodeGVECS
            {
                public NodeHandle<InputECSBufferNode> Node;
                public GraphValue<float> GV;
                public Entity BufferEntity;
            }

            public enum PortTestType { OnSimplePort, OnPortArray };

            [Test]
            public void ZCanReadAndSum_InputBuffers_ProvidedFrom_ECS([Values] NodeSet.RenderExecutionModel model, [Values(1, 2, 5, 13, 46)] int sequenceLength, [Values] PortTestType portTestType)

            {
                const int k_Updates = 5;

#if UNITY_EDITOR
                // FIXME: Without calling this here, later, LowLevelNodeTraits complains that Buffer<ECSBuffer> is not blittable!?!?!
                new DataBufferTests().TestGenericType(50, new ECSBuffer());
#endif

                var sequence = Enumerable.Range(0, sequenceLength).Select(i => (float)i);

                float sequenceSum = sequence.Sum();

                using (var world = new World("DataFlowGraph testing"))
                using (var set = new NodeSet())
                using (var nodes = new NativeList<NodeGVECS>(sequenceLength, Allocator.Persistent))
                {
                    set.RendererModel = model;

                    var memorySystem = world.CreateSystem<InputSystem<ECSBuffer>>();
                    var entityManager = world.EntityManager;

                    // Set up all resources
                    for (int i = 0; i < sequenceLength; ++i)
                    {
                        var node = set.Create<InputECSBufferNode>();
                        var gv = set.CreateGraphValue(node, InputECSBufferNode.KernelPorts.Sum);

                        var entity = entityManager.CreateEntity();
                        nodes.Add(new NodeGVECS { Node = node, GV = gv, BufferEntity = entity });

                        NodeMemoryInput<MemoryInputTag, ECSBuffer> memoryInputTag;
                        if (portTestType == PortTestType.OnSimplePort)
                        {
                            memoryInputTag = new NodeMemoryInput<MemoryInputTag, ECSBuffer>(node, (InputPortID)InputECSBufferNode.KernelPorts.ECSInput);
                        }
                        else
                        {
                            set.SetPortArraySize(node, InputECSBufferNode.KernelPorts.ECSInputArray, 2);
                            memoryInputTag = new NodeMemoryInput<MemoryInputTag, ECSBuffer>(node, (InputPortID)InputECSBufferNode.KernelPorts.ECSInputArray, 1);
                        }

                        entityManager.AddBuffer<ECSBuffer>(entity);
                        entityManager.AddComponentData(entity, memoryInputTag);
                    }

                    var mainThreadNodes = new System.Collections.Generic.List<NodeGVECS>();
                    for (int i = 0; i < nodes.Length; ++i)
                        mainThreadNodes.Add(nodes[i]);

                    memorySystem.Set = set;

                    // Run N sequences of free running updates, then remove the tag, check nothing happens for N, rinse and repeat N times.
                    for (int k = 0; k < k_Updates; ++k)
                    {
                        mainThreadNodes.ForEach(
                            n =>
                            {
                                entityManager.AddBuffer<ECSBuffer>(n.BufferEntity);
                                entityManager.GetBuffer<ECSBuffer>(n.BufferEntity).Reinterpret<float>().CopyFrom(sequence.ToArray());
                            }
                        );

                        for (int n = 0; n < k_Updates; ++n)
                        {
                            memorySystem.Update();

                            for (int i = 0; i < sequenceLength; ++i)
                            {
                                var actualSum1 = set.GetValueBlocking(mainThreadNodes[i].GV);

                                Assert.AreEqual(sequenceSum, actualSum1);
                            }
                        }

                        // Remove one of required components. Now nothing should move.
                        mainThreadNodes.ForEach(n => entityManager.RemoveComponent<ECSBuffer>(n.BufferEntity));

                        for (int n = 0; n < k_Updates; ++n)
                        {
                            memorySystem.Update();

                            for (int i = 0; i < sequenceLength; ++i)
                            {
                                var actualSum1 = set.GetValueBlocking(mainThreadNodes[i].GV);

                                Assert.AreEqual(0f, actualSum1);
                            }
                        }
                    }

                    // Rebuild setup. Check that destroying target entities stops patching.
                    mainThreadNodes.ForEach(
                        n =>
                        {
                            entityManager.AddBuffer<ECSBuffer>(n.BufferEntity);
                            entityManager.GetBuffer<ECSBuffer>(n.BufferEntity).Reinterpret<float>().CopyFrom(sequence.ToArray());
                        }
                    );

                    for (int n = 0; n < k_Updates; ++n)
                    {
                        memorySystem.Update();

                        for (int i = 0; i < sequenceLength; ++i)
                        {
                            var actualSum1 = set.GetValueBlocking(mainThreadNodes[i].GV);

                            Assert.AreEqual(sequenceSum, actualSum1);
                        }
                    }

                    mainThreadNodes.ForEach(
                        n =>
                        {
                            entityManager.DestroyEntity(n.BufferEntity);
                        }
                    );

                    JobHandle deps = default;
                    for (int n = 0; n < k_Updates; ++n)
                    {
                        // (Note: we must now explicitly call OnUpdate because memorySystem's EntityQuery will be empty
                        // so it would otherwise not run)
                        deps = memorySystem.OnUpdate(deps);

                        for (int i = 0; i < sequenceLength; ++i)
                        {
                            var actualSum1 = set.GetValueBlocking(mainThreadNodes[i].GV);

                            Assert.AreEqual(0f, actualSum1);
                        }
                    }
                    deps.Complete();

                    // Cleanup

                    for (int i = 0; i < sequenceLength; ++i)
                    {
                        set.ReleaseGraphValue(mainThreadNodes[i].GV);
                        set.Destroy(mainThreadNodes[i].Node);
                    }
                }
            }

            [NativeAllowReinterpretation]
            public struct ECSIntBuffer : IBufferElementData
            {
                public int Value;
            }

            [Test]
            public void CanReinterpretAndAssignDifferentlyTypedBuffers_WithMatchingMemoryLayout()
            {
                using (var world = new World("DataFlowGraph testing"))
                using (var set = new NodeSet())
                {
                    var node = set.Create<NodeWithAllTypesOfPorts>();
                    var memorySystem = world.CreateSystem<InputSystem<ECSIntBuffer>>();
                    var entityManager = world.EntityManager;
                    var entity = entityManager.CreateEntity();

                    entityManager.AddBuffer<ECSIntBuffer>(entity);
                    entityManager.GetBuffer<ECSIntBuffer>(entity).Add(new ECSIntBuffer { Value = 3 });
                    entityManager.AddComponentData(
                        entity,
                        new NodeMemoryInput<MemoryInputTag, ECSBuffer>(node, (InputPortID)NodeWithAllTypesOfPorts.KernelPorts.InputBuffer));

                    memorySystem.Set = set;
                    memorySystem.Update();
                    set.Destroy(node);
                }
            }

            [NativeAllowReinterpretation]
            public struct IncompatibleBufferElement : IBufferElementData
            {
                public double Value;
            }

            [Test]
            public void IncompatibleMemoryLayout_CannotBeReinterpreted()
            {
                using (var world = new World("DataFlowGraph testing"))
                using (var set = new NodeSet())
                {
                    var node = set.Create<NodeWithAllTypesOfPorts>();
                    var memorySystem = world.CreateSystem<InputSystem<IncompatibleBufferElement>>();
                    var entityManager = world.EntityManager;
                    var entity = entityManager.CreateEntity();

                    entityManager.AddBuffer<IncompatibleBufferElement>(entity);
                    entityManager.GetBuffer<IncompatibleBufferElement>(entity).Add(new IncompatibleBufferElement { Value = 3 }); // Solely to avoid compiler warning
                    entityManager.AddComponentData(
                        entity,
                        new NodeMemoryInput<MemoryInputTag, IncompatibleBufferElement>(node, (InputPortID)NodeWithAllTypesOfPorts.KernelPorts.InputBuffer));

                    LogAssert.Expect(UnityEngine.LogType.Error, "Cannot assign one buffer type to another fundamentally different type");

                    memorySystem.Set = set;
                    memorySystem.Update();
                    set.Destroy(node);
                }
            }

            public class UberNodeWithDataForwarding
                : NodeDefinition<UberNodeWithDataForwarding.Data, UberNodeWithDataForwarding.KernelData, UberNodeWithDataForwarding.KernelDefs, UberNodeWithDataForwarding.Kernel>
            {
                public struct KernelDefs : IKernelPortDefinition
                {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                    public DataInput<UberNodeWithDataForwarding, Buffer<ECSBuffer>> ForwardedDataInput;
                    public DataOutput<UberNodeWithDataForwarding, float> ForwardedDataOutputSum;
#pragma warning restore 649  // Assigned through internal DataFlowGraph reflection

                }

                public struct Data : INodeData
                {
                    public NodeHandle<InputECSBufferNode> Child;
                }

                public struct KernelData : IKernelData { }

                [BurstCompile(CompileSynchronously = true)]
                public struct Kernel : IGraphKernel<KernelData, KernelDefs>
                {
                    public void Execute(RenderContext ctx, KernelData data, ref KernelDefs ports)
                    {
                    }
                }


                protected internal override void Init(InitContext ctx)
                {
                    ref var data = ref GetNodeData(ctx.Handle);
                    data.Child = Set.Create<InputECSBufferNode>();

                    ctx.ForwardInput(KernelPorts.ForwardedDataInput, data.Child, InputECSBufferNode.KernelPorts.ECSInput);
                    ctx.ForwardOutput(KernelPorts.ForwardedDataOutputSum, data.Child, InputECSBufferNode.KernelPorts.Sum);
                }

                protected internal override void Destroy(NodeHandle handle)
                {
                    Set.Destroy(GetNodeData(handle).Child);
                }
            }

            [Test]
            public void CanAssignBatch_ToPortForwardedDestination()
            {
                const int k_SequenceMax = 250;

#if UNITY_EDITOR
                // FIXME: Without calling this here, later, LowLevelNodeTraits complains that Buffer<ECSBuffer> is not blittable!?!?!
                new DataBufferTests().TestGenericType(50, new ECSBuffer());
#endif

                var sequence = Enumerable.Range(0, k_SequenceMax).Select(i => (float)i);

                using (var world = new World("DataFlowGraph testing"))
                using (var set = new NodeSet())
                {
                    var memorySystem = world.CreateSystem<InputSystem<ECSBuffer>>();
                    var entityManager = world.EntityManager;

                    var node = set.Create<UberNodeWithDataForwarding>();

                    var gv = set.CreateGraphValue(node, UberNodeWithDataForwarding.KernelPorts.ForwardedDataOutputSum);

                    var entity = entityManager.CreateEntity();

                    entityManager.AddBuffer<ECSBuffer>(entity);
                    entityManager.AddComponentData(
                        entity,
                        new NodeMemoryInput<MemoryInputTag, ECSBuffer>(node, (InputPortID)UberNodeWithDataForwarding.KernelPorts.ForwardedDataInput)
                    );

                    entityManager.GetBuffer<ECSBuffer>(entity).Reinterpret<float>().CopyFrom(sequence.ToArray());

                    memorySystem.Set = set;
                    memorySystem.Update();

                    var actualSum1 = set.GetValueBlocking(gv);

                    Assert.AreEqual(sequence.Sum(), actualSum1);

                    set.ReleaseGraphValue(gv);
                    set.Destroy(node);
                }
            }
        }
    }
}

#pragma warning restore 612,618
