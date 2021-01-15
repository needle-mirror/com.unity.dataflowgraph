using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using static Unity.DataFlowGraph.Tests.ComponentNodeSetTests;
using static Unity.DataFlowGraph.Tests.ComponentNodeTests;

namespace Unity.DataFlowGraph.Tests
{
#pragma warning disable 649 // non-public unassigned default value

    class ComponentNodeBufferTests
    {
        class BufferNode : KernelNodeDefinition<BufferNode.KernelDefs>
        {
            public struct KernelDefs : IKernelPortDefinition
            {
                public DataInput<BufferNode, SimpleData> Filler_Input;
                public DataOutput<BufferNode, SimpleData> Filler_Output;

                public DataInput<BufferNode, float3> Offset;

                public DataInput<BufferNode, Buffer<SimpleBuffer>> Input;
                public DataOutput<BufferNode, Buffer<SimpleBuffer>> Output;
            }

            struct KernelData : IKernelData { }

            struct GraphKernel : IGraphKernel<KernelData, KernelDefs>
            {
                public void Execute(RenderContext ctx, in KernelData data, ref KernelDefs ports)
                {
                    var output = ctx.Resolve(ref ports.Output);
                    var input = ctx.Resolve(ports.Input);
                    var offset = ctx.Resolve(ports.Offset);

                    for (var i = 0; i < Math.Min(output.Length, input.Length); ++i)
                        output[i] = new SimpleBuffer { Values = input[i].Values + offset };
                }
            }
        }

        [Test]
        public void CanConnectBuffer_OfBufferElement_ToComponentNode_WithMatchingBuffer_ThroughWeakAPI(
            [Values(true, false)] bool forward,
            [Values] FixtureSystemType systemType)
        {
            using (var f = new Fixture<UpdateSystemDelegate>(systemType))
            {
                var entity = f.EM.CreateEntity(typeof(SimpleBuffer));

                var componentNode = f.Set.CreateComponentNode(entity);
                var node = f.Set.Create<BufferNode>();

                Assert.DoesNotThrow(
                    () =>
                    {
                        if(forward)
                        {
                            f.Set.Connect(
                                componentNode,
                                (OutputPortID)ComponentNode.Output<SimpleBuffer>(),
                                node,
                                (InputPortID)BufferNode.KernelPorts.Input
                            );
                        }
                        else
                        {
                            f.Set.Connect(
                                node,
                                (OutputPortID)BufferNode.KernelPorts.Output,
                                componentNode,
                                (InputPortID)ComponentNode.Input<SimpleBuffer>()
                            );
                        }
                    }
                );

                f.Set.Destroy(node, componentNode);
            }
        }

        [Test]
        public void CannotConnectScalarBufferElement_ToComponentNodeWithMatchingBuffer_ThroughWeakAPI(
            [Values] FixtureSystemType systemType)
        {
            Assert.Zero(NodeWithParametricPortType<SimpleBuffer>.IL2CPP_ClassInitializer);

            using (var f = new Fixture<UpdateSystemDelegate>(systemType))
            {
                var entity = f.EM.CreateEntity(typeof(SimpleBuffer));

                var entityNode = f.Set.CreateComponentNode(entity);
                var node = f.Set.Create<NodeWithParametricPortType<SimpleBuffer>>();

                Assert.Throws<InvalidOperationException>(
                    () =>
                    {
                        f.Set.Connect(
                            entityNode,
                            (OutputPortID)ComponentNode.Output<SimpleBuffer>(),
                            node,
                            (InputPortID)NodeWithParametricPortType<SimpleBuffer>.KernelPorts.Input
                        );
                    }
                );

                Assert.Throws<InvalidOperationException>(
                    () =>
                    {
                        f.Set.Connect(
                            node,
                            (OutputPortID)NodeWithParametricPortType<SimpleBuffer>.KernelPorts.Output,
                            entityNode,
                            (InputPortID)ComponentNode.Input<SimpleBuffer>()
                        );
                    }
                );

                f.Set.Destroy(node, entityNode);
            }
        }

        [Test]
        public void CanRead_ECSBuffer_InsideFromDFG(
            [Values] NodeSet.RenderExecutionModel model,
            [Values(1, 3, 13, 50)] int bufferSize,
            [Values] FixtureSystemType systemType)
        {
            const int k_Loops = 10;

            using (var f = new Fixture<UpdateSystemDelegate>(systemType))
            {
                f.Set.RendererModel = model;
                var entity = f.EM.CreateEntity(typeof(SimpleBuffer));
                var entityNode = f.Set.CreateComponentNode(entity);
                var dfgNode = f.Set.Create<BufferNode>();
                var gv = f.Set.CreateGraphValue(dfgNode, BufferNode.KernelPorts.Output);

                f.Set.Connect(entityNode, ComponentNode.Output<SimpleBuffer>(), dfgNode, BufferNode.KernelPorts.Input);

                var rng = new Mathematics.Random(0x7f);

                f.Set.SetBufferSize(dfgNode, BufferNode.KernelPorts.Output, Buffer<SimpleBuffer>.SizeRequest(bufferSize));

                for (int i = 0; i < k_Loops; ++i)
                {
                    var ecsBuffer = f.EM.GetBuffer<SimpleBuffer>(entity);
                    ecsBuffer.ResizeUninitialized(bufferSize);

                    for (int n = 0; n < bufferSize; ++n)
                    {
                        ecsBuffer[n] = new SimpleBuffer { Values = rng.NextFloat3() };
                    }

                    f.System.Update();

                    var resolver = f.Set.GetGraphValueResolver(out var dependency);
                    dependency.Complete();

                    ecsBuffer = f.EM.GetBuffer<SimpleBuffer>(entity);

                    var dfgBuffer = resolver.Resolve(gv);

                    Assert.AreEqual(ecsBuffer.Length, dfgBuffer.Length);

                    // TODO: can compare alias here
                    for (int n = 0; n < bufferSize; ++n)
                    {
                        Assert.AreEqual(ecsBuffer[n], dfgBuffer[n]);
                    }
                }

                f.Set.ReleaseGraphValue(gv);
                f.Set.Destroy(entityNode, dfgNode);
            }
        }

        public class CountInputSizeNode : KernelNodeDefinition<CountInputSizeNode.KernelDefs>
        {
            public struct KernelDefs : IKernelPortDefinition
            {
                public DataInput<CountInputSizeNode, Buffer<SimpleBuffer>> Input;
                public DataOutput<CountInputSizeNode, int> Indices;
            }

            struct KernelData : IKernelData { }

            struct GraphKernel : IGraphKernel<KernelData, KernelDefs>
            {
                public void Execute(RenderContext ctx, in KernelData data, ref KernelDefs ports)
                {
                    ctx.Resolve(ref ports.Indices) = ctx.Resolve(ports.Input).Length;
                }
            }
        }

        [Test]
        public void ECSInputConnection_WorksAsExpected_WhenMissingAndExisting_ThroughUpdates(
            [Values(1, 3, 13, 17)] int numEntities,
            [Values] FixtureSystemType systemType)
        {
            const int k_Loops = 10;

            var gc = new List<NodeHandle>(numEntities);
            var gvs = new List<(Entity, GraphValue<int>)>(numEntities);

            using (var f = new Fixture<UpdateSystemDelegate>(systemType))
            {
                for (int i = 0; i < numEntities; ++i)
                {
                    var entity = f.EM.CreateEntity();

                    var entityNode = f.Set.CreateComponentNode(entity);
                    var dfgNode = f.Set.Create<CountInputSizeNode>();

                    gc.Add(entityNode);
                    gc.Add(dfgNode);
                    gvs.Add((entity, f.Set.CreateGraphValue(dfgNode, CountInputSizeNode.KernelPorts.Indices)));

                    f.Set.Connect(entityNode, ComponentNode.Output<SimpleBuffer>(), dfgNode, CountInputSizeNode.KernelPorts.Input);

                }

                for (int loop = 0; loop < k_Loops; ++loop)
                {
                    f.System.Update();

                    for (int entity = 0; entity < numEntities; ++entity)
                    {
                        if (f.EM.HasComponent<SimpleBuffer>(gvs[entity].Item1))
                        {
                            Assert.AreEqual(loop - 1, f.Set.GetValueBlocking(gvs[entity].Item2));
                            f.EM.RemoveComponent<SimpleBuffer>(gvs[entity].Item1);
                        }
                        else
                        {
                            Assert.AreEqual(0, f.Set.GetValueBlocking(gvs[entity].Item2));

                            f
                                .EM
                                .AddBuffer<SimpleBuffer>(gvs[entity].Item1)
                                .ResizeUninitialized(loop);
                        }
                    }
                }

                // Additionally test when the entity is dead.
                for (int entity = 0; entity < numEntities; ++entity)
                {
                    f.EM.DestroyEntity(gvs[entity].Item1);
                }

                f.System.Update();

                for (int entity = 0; entity < numEntities; ++entity)
                {
                    Assert.AreEqual(0, f.Set.GetValueBlocking(gvs[entity].Item2));
                }

                gvs.ForEach(gve => f.Set.ReleaseGraphValue(gve.Item2));
                f.Set.Destroy(gc.ToArray());
            }
        }

        [Test]
        public void DFGBuffer_ToECSBuffer_WithMismatchedSize_OnlyBlitsSharedPortion(
            [Values(1, 3, 13, 50)] int bufferSize,
            [Values(true, false)] bool sourceIsBigger,
            [Values] FixtureSystemType systemType)
        {
            const int k_SizeDifference = 11;

            using (var f = new Fixture<UpdateSystemDelegate>(systemType))
            {
                var entitySource = f.EM.CreateEntity(typeof(SimpleBuffer), typeof(SimpleData));
                var entityDestination = f.EM.CreateEntity(typeof(SimpleBuffer));

                var entityNodeSource = f.Set.CreateComponentNode(entitySource);
                var entityNodeDest = f.Set.CreateComponentNode(entityDestination);

                var dfgNode = f.Set.Create<BufferNode>();
                var gv = f.Set.CreateGraphValue(dfgNode, BufferNode.KernelPorts.Output);

                f.Set.Connect(entityNodeSource, ComponentNode.Output<SimpleBuffer>(), dfgNode, BufferNode.KernelPorts.Input);
                f.Set.Connect(dfgNode, BufferNode.KernelPorts.Output, entityNodeDest, ComponentNode.Input<SimpleBuffer>());

                var rng = new Mathematics.Random(0x8f);

                var ecsSourceBuffer = f.EM.GetBuffer<SimpleBuffer>(entitySource);
                var ecsDestBuffer = f.EM.GetBuffer<SimpleBuffer>(entityDestination);

                var sourceSize = sourceIsBigger ? bufferSize * k_SizeDifference : bufferSize;
                var destSize = sourceIsBigger ? bufferSize : bufferSize * k_SizeDifference;

                // Make sources much larger than destination
                ecsSourceBuffer.ResizeUninitialized(sourceSize);
                ecsDestBuffer.ResizeUninitialized(destSize);
                f.Set.SetBufferSize(dfgNode, BufferNode.KernelPorts.Output, Buffer<SimpleBuffer>.SizeRequest(sourceSize));

                ecsSourceBuffer = f.EM.GetBuffer<SimpleBuffer>(entitySource);
                for (int n = 0; n < sourceSize; ++n)
                {
                    ecsSourceBuffer[n] = new SimpleBuffer { Values = rng.NextFloat3() };
                }

                f.System.Update();
                ecsDestBuffer = f.EM.GetBuffer<SimpleBuffer>(entityDestination);

                // TODO: can compare alias here
                for (int n = 0; n < bufferSize; ++n)
                {
                    Assert.AreEqual(ecsSourceBuffer[n], ecsDestBuffer[n]);
                }

                f.Set.ReleaseGraphValue(gv);
                f.Set.Destroy(entityNodeSource, entityNodeDest, dfgNode);
            }
        }

        [Test]
        public void CanWrite_ToECSBuffer_InsideFromDFG_FromOriginalECS_Source(
            [Values] NodeSet.RenderExecutionModel model,
            [Values(1, 3, 13, 50)] int bufferSize,
            [Values] APIType strongNess,
            [Values] FixtureSystemType systemType)
        {
            const int k_Loops = 10;

            using (var f = new Fixture<UpdateSystemDelegate>(systemType))
            {
                f.Set.RendererModel = model;
                var entitySource = f.EM.CreateEntity(typeof(SimpleBuffer));
                var entityDestination = f.EM.CreateEntity(typeof(SimpleBuffer));

                var entityNodeSource = f.Set.CreateComponentNode(entitySource);
                var entityNodeDest = f.Set.CreateComponentNode(entityDestination);

                var dfgNode = f.Set.Create<BufferNode>();
                var gv = f.Set.CreateGraphValue(dfgNode, BufferNode.KernelPorts.Output);

                if(strongNess == APIType.StronglyTyped)
                {
                    f.Set.Connect(entityNodeSource, ComponentNode.Output<SimpleBuffer>(), dfgNode, BufferNode.KernelPorts.Input);
                    f.Set.Connect(dfgNode, BufferNode.KernelPorts.Output, entityNodeDest, ComponentNode.Input<SimpleBuffer>());
                }
                else
                {
                    f.Set.Connect(
                        entityNodeSource,
                        (OutputPortID)ComponentNode.Output<SimpleBuffer>(),
                        dfgNode,
                        (InputPortID)BufferNode.KernelPorts.Input
                    );

                    f.Set.Connect(
                        dfgNode,
                        (OutputPortID)BufferNode.KernelPorts.Output,
                        entityNodeDest,
                        (InputPortID)ComponentNode.Input<SimpleBuffer>()
                    );
                }

                var rng = new Mathematics.Random(0x8f);

                var ecsSourceBuffer = f.EM.GetBuffer<SimpleBuffer>(entitySource);
                var ecsDestBuffer = f.EM.GetBuffer<SimpleBuffer>(entityDestination);

                // match all buffer sizes
                ecsSourceBuffer.ResizeUninitialized(bufferSize);
                ecsDestBuffer.ResizeUninitialized(bufferSize);
                f.Set.SetBufferSize(dfgNode, BufferNode.KernelPorts.Output, Buffer<SimpleBuffer>.SizeRequest(bufferSize));

                for (int i = 0; i < k_Loops; ++i)
                {
                    ecsSourceBuffer = f.EM.GetBuffer<SimpleBuffer>(entitySource);

                    for (int n = 0; n < bufferSize; ++n)
                    {
                        ecsSourceBuffer[n] = new SimpleBuffer { Values = rng.NextFloat3() };
                    }

                    f.System.Update();

                    // This should fence on all dependencies
                    ecsDestBuffer = f.EM.GetBuffer<SimpleBuffer>(entityDestination);

                    // TODO: can compare alias here
                    for (int n = 0; n < bufferSize; ++n)
                    {
                        Assert.AreEqual(ecsSourceBuffer[n], ecsDestBuffer[n]);
                    }
                }

                f.Set.ReleaseGraphValue(gv);
                f.Set.Destroy(entityNodeSource, entityNodeDest, dfgNode);
            }
        }

        [Test]
        public void CanConnect_ECSBuffer_ToECSBuffer_UsingOnlyComponentNodes_AndTransferData(
            [Values] NodeSet.RenderExecutionModel model,
            [Values(1, 3, 13, 50)] int bufferSize,
            [Values] APIType strongNess,
            [Values] FixtureSystemType systemType)
        {
            const int k_Loops = 10;

            using (var f = new Fixture<UpdateSystemDelegate>(systemType))
            {
                f.Set.RendererModel = model;
                var entitySource = f.EM.CreateEntity(typeof(SimpleBuffer));
                var entityDestination = f.EM.CreateEntity(typeof(SimpleBuffer));

                var entityNodeSource = f.Set.CreateComponentNode(entitySource);
                var entityNodeDest = f.Set.CreateComponentNode(entityDestination);

                if (strongNess == APIType.StronglyTyped)
                {
                    f.Set.Connect(
                        entityNodeSource,
                        ComponentNode.Output<SimpleBuffer>(),
                        entityNodeDest,
                        ComponentNode.Input<SimpleBuffer>()
                    );
                }
                else
                {
                    f.Set.Connect(
                        entityNodeSource,
                        (OutputPortID)ComponentNode.Output<SimpleBuffer>(),
                        entityNodeDest,
                        (InputPortID)ComponentNode.Input<SimpleBuffer>()
                    );
                }

                var rng = new Mathematics.Random(0x8f);

                var ecsSourceBuffer = f.EM.GetBuffer<SimpleBuffer>(entitySource);
                var ecsDestBuffer = f.EM.GetBuffer<SimpleBuffer>(entityDestination);

                // match all buffer sizes
                ecsSourceBuffer.ResizeUninitialized(bufferSize);
                ecsDestBuffer.ResizeUninitialized(bufferSize);

                for (int i = 0; i < k_Loops; ++i)
                {
                    ecsSourceBuffer = f.EM.GetBuffer<SimpleBuffer>(entitySource);

                    for (int n = 0; n < bufferSize; ++n)
                    {
                        ecsSourceBuffer[n] = new SimpleBuffer { Values = rng.NextFloat3() };
                    }

                    f.System.Update();

                    // This should fence on all dependencies
                    ecsDestBuffer = f.EM.GetBuffer<SimpleBuffer>(entityDestination);
                    //f.Set.DataGraph.SyncAnyRendering();
                    // TODO: can compare alias here
                    for (int n = 0; n < bufferSize; ++n)
                    {
                        Assert.AreEqual(ecsSourceBuffer[n], ecsDestBuffer[n]);
                    }
                }

                f.Set.Destroy(entityNodeSource, entityNodeDest);
            }
        }

        [Test]
        public void CanReadAndWrite_ToSameECSBuffer_FromInsideDFG(
            [Values] NodeSet.RenderExecutionModel model,
            [Values(1, 3, 13, 50)] int bufferSize,
            [Values] bool feedbackAfterProcessing,
            [Values] FixtureSystemType systemType)
        {
            const int k_Loops = 10;
            var k_Offset = new float3(1.0f, 1.5f, 2.0f);

            using (var f = new Fixture<UpdateSystemDelegate>(systemType))
            {
                f.Set.RendererModel = model;
                var entity = f.EM.CreateEntity(typeof(SimpleBuffer), typeof(SimpleData));

                var entityNode = f.Set.CreateComponentNode(entity);

                var dfgNode = f.Set.Create<BufferNode>();
                f.Set.SetData(dfgNode, BufferNode.KernelPorts.Offset, k_Offset);

                f.Set.Connect(
                    entityNode,
                    ComponentNode.Output<SimpleBuffer>(),
                    dfgNode,
                    BufferNode.KernelPorts.Input,
                    !feedbackAfterProcessing ? NodeSet.ConnectionType.Feedback : NodeSet.ConnectionType.Normal
                );

                f.Set.Connect(
                    dfgNode,
                    BufferNode.KernelPorts.Output,
                    entityNode,
                    ComponentNode.Input<SimpleBuffer>(),
                    feedbackAfterProcessing ? NodeSet.ConnectionType.Feedback : NodeSet.ConnectionType.Normal
                );

                var ecsBuffer = f.EM.GetBuffer<SimpleBuffer>(entity);

                // match all buffer sizes
                ecsBuffer.ResizeUninitialized(bufferSize);
                f.Set.SetBufferSize(dfgNode, BufferNode.KernelPorts.Output, Buffer<SimpleBuffer>.SizeRequest(bufferSize));

                var expected = new List<SimpleBuffer>();

                var rng = new Mathematics.Random(0x8f);
                for (int n = 0; n < bufferSize; ++n)
                {
                    ecsBuffer[n] = new SimpleBuffer { Values = feedbackAfterProcessing ? -k_Offset : rng.NextFloat3() };
                    expected.Add(ecsBuffer[n]);
                }

                for (int i = 0; i < k_Loops; ++i)
                {
                    f.System.Update();

                    // This should fence on all dependencies
                    ecsBuffer = f.EM.GetBuffer<SimpleBuffer>(entity);

                    for (int n = 0; n < bufferSize; ++n)
                        expected[n] = new SimpleBuffer { Values = expected[n].Values + k_Offset };

                    for (int n = 0; n < bufferSize; ++n)
                        Assert.AreEqual(expected[n], ecsBuffer[n]);
                }

                f.Set.Destroy(entityNode, dfgNode);
            }
        }
    }
#pragma warning restore 649 // non-public unassigned default value

}

