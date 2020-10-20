using System;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using static Unity.DataFlowGraph.Tests.ComponentNodeSetTests;

namespace Unity.DataFlowGraph.Tests
{
    using TestBytePortArray = PortArray<DataInput<InvalidDefinitionSlot, byte>>;
    using TestDoublePortArray = PortArray<DataInput<InvalidDefinitionSlot, double>>;

    public class PortArrayTests
    {
        public class ArrayIONode : SimulationKernelNodeDefinition<ArrayIONode.SimPorts, ArrayIONode.KernelDefs>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
                public PortArray<MessageInput<ArrayIONode, int>> Inputs;
                public PortArray<MessageOutput<ArrayIONode, int>> Outputs;
            }

            public struct Aggregate
            {
                public long Long;
                public Buffer<long> BufferOfLongs;
            }

            public struct KernelDefs : IKernelPortDefinition
            {
                public PortArray<DataInput<ArrayIONode, int>> InputInt;
                public PortArray<DataInput<ArrayIONode, Buffer<int>>> InputBufferOfInts;
                public PortArray<DataInput<ArrayIONode, Aggregate>> InputAggregate;

                public DataOutput<ArrayIONode, int> SumInt;
                public DataOutput<ArrayIONode, Buffer<int>> OutputBufferOfInts;
                public DataOutput<ArrayIONode, Aggregate> OutputAggregate;
            }

            struct EmptyKernelData : IKernelData { }

            [BurstCompile(CompileSynchronously = true)]
            struct Kernel : IGraphKernel<EmptyKernelData, KernelDefs>
            {
                public void Execute(RenderContext ctx, EmptyKernelData data, ref KernelDefs ports)
                {
                    ref var outInt = ref ctx.Resolve(ref ports.SumInt);
                    outInt = 0;
                    var inInts = ctx.Resolve(ports.InputInt);
                    for (int i = 0; i < inInts.Length; ++i)
                        outInt += inInts[i];

                    var outBufferOfInts = ctx.Resolve(ref ports.OutputBufferOfInts);
                    for (int i = 0; i < outBufferOfInts.Length; ++i)
                        outBufferOfInts[i] = i + outInt;
                    var inBufferOfInts = ctx.Resolve(ports.InputBufferOfInts);
                    for (int i = 0; i < inBufferOfInts.Length; ++i)
                    {
                        var inNativeArray = ctx.Resolve(inBufferOfInts[i]);
                        for (int j = 0; j < Math.Min(inNativeArray.Length, outBufferOfInts.Length); ++j)
                            outBufferOfInts[j] += inNativeArray[j];
                    }

                    ref var outAggregate = ref ctx.Resolve(ref ports.OutputAggregate);
                    outAggregate.Long = outInt;
                    var outNativeArray = ctx.Resolve(outAggregate.BufferOfLongs);
                    for (int i = 0; i < outNativeArray.Length; ++i)
                        outNativeArray[i] = i + outInt;
                    var inAggregates = ctx.Resolve(ports.InputAggregate);
                    for (int i = 0; i < inAggregates.Length; ++i)
                    {
                        outAggregate.Long += inAggregates[i].Long;
                        var inNativeArray = ctx.Resolve(inAggregates[i].BufferOfLongs);
                        for (int j = 0; j < Math.Min(inNativeArray.Length, outNativeArray.Length); ++j)
                            outNativeArray[j] += inNativeArray[j];
                    }
                }
            }

            public struct NodeData : INodeData, IMsgHandler<int>
            {
                public (int, int) LastReceivedMsg;

                public void HandleMessage(in MessageContext ctx, in int msg)
                {
                    LastReceivedMsg = (ctx.ArrayIndex, msg);
                    ctx.EmitMessage(SimulationPorts.Outputs, ctx.ArrayIndex, msg);
                }
            }
        }

        [Test]
        public void CanSetData_OnDataPortArray()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<ArrayIONode>();

                set.SetPortArraySize(node, ArrayIONode.KernelPorts.InputInt, 5);

                set.SetData(node, ArrayIONode.KernelPorts.InputInt, 2, 99);
                set.SetData(node, ArrayIONode.KernelPorts.InputInt, 4, 200);

                var result = set.CreateGraphValue(node, ArrayIONode.KernelPorts.SumInt);

                set.Update();
                Assert.AreEqual(299, set.GetValueBlocking(result));

                set.ReleaseGraphValue(result);
                set.Destroy(node);
            }
        }

        [Test]
        public void ResizingDataPortArray_InvalidatesSetData()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<ArrayIONode>();

                set.SetPortArraySize(node, ArrayIONode.KernelPorts.InputInt, 5);

                set.SetData(node, ArrayIONode.KernelPorts.InputInt, 2, 99);
                set.SetData(node, ArrayIONode.KernelPorts.InputInt, 4, 200);

                set.SetPortArraySize(node, ArrayIONode.KernelPorts.InputInt, 3);
                set.SetPortArraySize(node, ArrayIONode.KernelPorts.InputInt, 5);

                var result = set.CreateGraphValue(node, ArrayIONode.KernelPorts.SumInt);

                set.Update();
                Assert.AreEqual(99, set.GetValueBlocking(result));

                set.ReleaseGraphValue(result);
                set.Destroy(node);
            }
        }

        [Test]
        public void ScalarDataPortArrays_WorkProperly()
        {
            using (var set = new NodeSet())
            {
                var src1 = set.Create<ArrayIONode>();
                var src2 = set.Create<ArrayIONode>();
                var dest = set.Create<ArrayIONode>();

                set.SetPortArraySize(src1, ArrayIONode.KernelPorts.InputInt, 1);
                set.SetPortArraySize(src2, ArrayIONode.KernelPorts.InputInt, 1);
                set.SetPortArraySize(dest, ArrayIONode.KernelPorts.InputInt, 40);

                set.Connect(src1, ArrayIONode.KernelPorts.SumInt, dest, ArrayIONode.KernelPorts.InputInt, 10);
                set.Connect(src2, ArrayIONode.KernelPorts.SumInt, dest, ArrayIONode.KernelPorts.InputInt, 11);
                set.Connect(src1, ArrayIONode.KernelPorts.SumInt, dest, ArrayIONode.KernelPorts.InputInt, 30);
                set.Connect(src2, ArrayIONode.KernelPorts.SumInt, dest, ArrayIONode.KernelPorts.InputInt, 31);

                set.SetData(src1, ArrayIONode.KernelPorts.InputInt, 0, 11);
                set.SetData(src2, ArrayIONode.KernelPorts.InputInt, 0, 22);
                set.SetData(dest, ArrayIONode.KernelPorts.InputInt, 20, 33);
                set.SetData(dest, ArrayIONode.KernelPorts.InputInt, 21, 44);

                var result = set.CreateGraphValue(dest, ArrayIONode.KernelPorts.SumInt);

                set.Update();

                Assert.AreEqual(2 * 11 + 2 * 22 + 33 + 44, set.GetValueBlocking(result));

                set.ReleaseGraphValue(result);
                set.Destroy(src1, src2, dest);
            }
        }

        [Test]
        public void BufferDataPortArrays_WorkProperly()
        {
            using (var set = new NodeSet())
            {
                var src1 = set.Create<ArrayIONode>();
                var src2 = set.Create<ArrayIONode>();
                var dest = set.Create<ArrayIONode>();

                set.SetBufferSize(src1, ArrayIONode.KernelPorts.OutputBufferOfInts, Buffer<int>.SizeRequest(10));
                set.SetBufferSize(src2, ArrayIONode.KernelPorts.OutputBufferOfInts, Buffer<int>.SizeRequest(20));
                set.SetPortArraySize(dest, ArrayIONode.KernelPorts.InputBufferOfInts, 30);
                set.SetBufferSize(dest, ArrayIONode.KernelPorts.OutputBufferOfInts, Buffer<int>.SizeRequest(15));

                set.Connect(src1, ArrayIONode.KernelPorts.OutputBufferOfInts, dest, ArrayIONode.KernelPorts.InputBufferOfInts, 10);
                set.Connect(src2, ArrayIONode.KernelPorts.OutputBufferOfInts, dest, ArrayIONode.KernelPorts.InputBufferOfInts, 11);
                set.Connect(src1, ArrayIONode.KernelPorts.OutputBufferOfInts, dest, ArrayIONode.KernelPorts.InputBufferOfInts, 20);
                set.Connect(src2, ArrayIONode.KernelPorts.OutputBufferOfInts, dest, ArrayIONode.KernelPorts.InputBufferOfInts, 21);

                set.SetPortArraySize(src1, ArrayIONode.KernelPorts.InputInt, 1);
                set.SetPortArraySize(src2, ArrayIONode.KernelPorts.InputInt, 1);
                set.SetPortArraySize(dest, ArrayIONode.KernelPorts.InputInt, 1);
                set.SetData(src1, ArrayIONode.KernelPorts.InputInt, 0, 11);
                set.SetData(src2, ArrayIONode.KernelPorts.InputInt, 0, 22);
                set.SetData(dest, ArrayIONode.KernelPorts.InputInt, 0, 33);

                var result = set.CreateGraphValue(dest, ArrayIONode.KernelPorts.OutputBufferOfInts);

                set.Update();

                var resolver = set.GetGraphValueResolver(out var valueResolverDeps);
                valueResolverDeps.Complete();
                var readback = resolver.Resolve(result);

                Assert.AreEqual(15, readback.Length);
                for (int i = 0; i < readback.Length; ++i)
                    Assert.AreEqual((i + 33) + 2 * (i < 10 ? (i + 11) : 0) + 2 * (i + 22), readback[i]);

                set.ReleaseGraphValue(result);
                set.Destroy(src1, src2, dest);
            }
        }

        [Test]
        public void AggregateDataPortArrays_WorkProperly()
        {
            using (var set = new NodeSet())
            {
                var src1 = set.Create<ArrayIONode>();
                var src2 = set.Create<ArrayIONode>();
                var dest = set.Create<ArrayIONode>();

                set.SetBufferSize(src1, ArrayIONode.KernelPorts.OutputAggregate, new ArrayIONode.Aggregate { BufferOfLongs = Buffer<long>.SizeRequest(10) });
                set.SetBufferSize(src2, ArrayIONode.KernelPorts.OutputAggregate, new ArrayIONode.Aggregate { BufferOfLongs = Buffer<long>.SizeRequest(20) });
                set.SetPortArraySize(dest, ArrayIONode.KernelPorts.InputAggregate, 30);
                set.SetBufferSize(dest, ArrayIONode.KernelPorts.OutputAggregate, new ArrayIONode.Aggregate { BufferOfLongs = Buffer<long>.SizeRequest(15) });

                set.Connect(src1, ArrayIONode.KernelPorts.OutputAggregate, dest, ArrayIONode.KernelPorts.InputAggregate, 10);
                set.Connect(src2, ArrayIONode.KernelPorts.OutputAggregate, dest, ArrayIONode.KernelPorts.InputAggregate, 11);
                set.Connect(src1, ArrayIONode.KernelPorts.OutputAggregate, dest, ArrayIONode.KernelPorts.InputAggregate, 20);
                set.Connect(src2, ArrayIONode.KernelPorts.OutputAggregate, dest, ArrayIONode.KernelPorts.InputAggregate, 21);

                set.SetPortArraySize(src1, ArrayIONode.KernelPorts.InputInt, 1);
                set.SetPortArraySize(src2, ArrayIONode.KernelPorts.InputInt, 1);
                set.SetPortArraySize(dest, ArrayIONode.KernelPorts.InputInt, 1);
                set.SetData(src1, ArrayIONode.KernelPorts.InputInt, 0, 11);
                set.SetData(src2, ArrayIONode.KernelPorts.InputInt, 0, 22);
                set.SetData(dest, ArrayIONode.KernelPorts.InputInt, 0, 33);

                var result = set.CreateGraphValue(dest, ArrayIONode.KernelPorts.OutputAggregate);

                set.Update();

                Assert.AreEqual(2 * 11 + 2 * 22 + 33, set.GetValueBlocking(result).Long);

                var resolver = set.GetGraphValueResolver(out var valueResolverDeps);
                valueResolverDeps.Complete();
                var readback = resolver.Resolve(result).BufferOfLongs.ToNative(resolver);

                Assert.AreEqual(15, readback.Length);
                for (int i = 0; i < readback.Length; ++i)
                    Assert.AreEqual((i + 33) + 2 * (i < 10 ? (i + 11) : 0) + 2 * (i + 22), readback[i]);

                set.ReleaseGraphValue(result);
                set.Destroy(src1, src2, dest);
            }
        }

        [Test]
        public void DefaultConstructed_PortArrayIDs_AreArrayPorts()
        {
            InputPortArrayID inputPortArrayId = default;
            ushort arrayIndex;

            Assert.True(inputPortArrayId.IsArray);
            Assert.DoesNotThrow(() => arrayIndex = inputPortArrayId.ArrayIndex);

            OutputPortArrayID outputPortArrayId = default;

            Assert.True(outputPortArrayId.IsArray);
            Assert.DoesNotThrow(() => arrayIndex = outputPortArrayId.ArrayIndex);
        }

        [Test]
        public void ArrayConstructorFor_PortArrayIDs_WithSentinel_Throw()
        {
            Assert.Throws<InvalidOperationException>(() => new InputPortArrayID(portId: default, InputPortArrayID.NonArraySentinel));
            Assert.Throws<InvalidOperationException>(() => new OutputPortArrayID(portId: default, OutputPortArrayID.NonArraySentinel));
        }

        static UInt16[] s_ArrayConstructorParameters = new ushort[] {
            (ushort)0u,
            (ushort)1u,
            (ushort)4u,
            (ushort)13u,
            ushort.MaxValue - 1
        };

        [Test]
        public void ArrayConstructorFor_PortArrayIDs_AreArrayPorts([ValueSource("s_ArrayConstructorParameters")] ushort arrayIndex)
        {
            InputPortArrayID inputPortArrayId = new InputPortArrayID(portId: default, arrayIndex);

            Assert.True(inputPortArrayId.IsArray);
            Assert.DoesNotThrow(() => arrayIndex = inputPortArrayId.ArrayIndex);

            OutputPortArrayID outputPortArrayId = new OutputPortArrayID(portId: default, arrayIndex);

            Assert.True(outputPortArrayId.IsArray);
            Assert.DoesNotThrow(() => arrayIndex = outputPortArrayId.ArrayIndex);
        }

        [Test]
        public void AccessingArrayIndex_OnMessageContext_ThrowsForNonArrayTarget()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<PassthroughTest<int>>();

                ushort arrayIndex;
                InputPortArrayID id = new InputPortArrayID((InputPortID)PassthroughTest<int>.SimulationPorts.Input);
                MessageContext context = new MessageContext(set, new InputPair(set, node, id));

                Assert.Throws<InvalidOperationException>(() => arrayIndex = context.ArrayIndex);

                set.Destroy(node);
            }
        }

        static UInt16[] s_ResizeParameters = new ushort[] {
            (ushort)0u,
            (ushort)1u,
            (ushort)4u,
            (ushort)13u,
            TestBytePortArray.MaxSize >> 1
        };

        [Test]
        public unsafe void CanResize_DefaultConstructed_DataPortArray([ValueSource("s_ResizeParameters")] UInt16 size)
        {
            using (var sd = new RenderGraph.SharedData(SimpleType.MaxAlignment))
            {
                var array = new TestBytePortArray();

                array.Resize(size, sd.BlankPage, Allocator.Temp);
                array.Free(Allocator.Temp);
            }
        }

#if DFG_ASSERTIONS
        [Test]
        public unsafe void CannotSizeADataPortArray_ToMaxSize()
        {
            using (var sd = new RenderGraph.SharedData(SimpleType.MaxAlignment))
            {
                var array = new TestBytePortArray();

                Assert.Throws<AssertionException>(
                    () => array.Resize(TestBytePortArray.MaxSize, sd.BlankPage, Allocator.Temp)
                );

                array.Free(Allocator.Temp);
            }
        }
#endif

        static int[] s_InvalidResizeParameters = new int[] {
            -TestBytePortArray.MaxSize - 2,
            -TestBytePortArray.MaxSize - 1,
            -TestBytePortArray.MaxSize,
            -TestBytePortArray.MaxSize + 1,
            -2,
            -1,
            TestBytePortArray.MaxSize,
            TestBytePortArray.MaxSize + 1,
            TestBytePortArray.MaxSize << 1
        };

        [Test]
        public void CannotSizeAPortArray_ToInvalidSize([ValueSource("s_InvalidResizeParameters")] int size)
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<ArrayIONode>();

                Assert.Throws<ArgumentException>(() => set.SetPortArraySize(node, ArrayIONode.SimulationPorts.Inputs, size));
                Assert.Throws<ArgumentException>(() => set.SetPortArraySize(node, ArrayIONode.SimulationPorts.Outputs, size));
                Assert.Throws<ArgumentException>(() => set.SetPortArraySize(node, ArrayIONode.KernelPorts.InputInt, size));

                set.Destroy(node);
            }
        }

        [Test]
        public unsafe void CanContinuouslyResize_TheSameDataPortArray()
        {
            void* blank = (void*)0x10;
            const int k_Times = 100;
            var rng = new Mathematics.Random(seed: 0xFF);
            var array = new TestBytePortArray();

            for (int i = 0; i < k_Times; ++i)
            {
                var size = (UInt16)rng.NextUInt(0, TestBytePortArray.MaxSize);

                array.Resize(size, blank, Allocator.Temp);

                Assert.AreEqual(size, array.Size);
                Assert.IsTrue(array.Ptr != null);

                for (ushort n = 0; n < size; ++n)
                    Assert.IsTrue(blank == array.GetRef(n).Ptr);
            }

            array.Free(Allocator.Temp);
        }

        [Test]
        public unsafe void ResizingPortArray_CorrectlyUpdatesBlankValue()
        {
            const int k_Times = 100;
            var rng = new Mathematics.Random(seed: 0xFF);
            var array = new TestBytePortArray();

            for (int i = 0; i < k_Times; ++i)
            {
                var oldSize = array.Size;
                var size = (UInt16)rng.NextUInt(0, TestBytePortArray.MaxSize);

                array.Resize(size, (void*)(i * 8), Allocator.Temp);

                for (ushort n = oldSize; n < size; ++n)
                    Assert.IsTrue((void*)(i * 8) == array.GetRef(n).Ptr);

            }

            array.Free(Allocator.Temp);
        }

        [Test]
        public unsafe void CanResizeDataInputPortArray_ThroughUntypedAlias()
        {
            var original = new TestDoublePortArray();
            void* blank = (void*)0x10;

            original.Resize(27, blank, Allocator.Temp);

            Assert.AreEqual(27, original.Size);

            ref var alias = ref original.AsUntyped();

            alias.Resize(53, blank, Allocator.Temp);

            Assert.AreEqual(53, original.Size);

            for (ushort n = 0; n < original.Size; ++n)
                Assert.IsTrue(blank == original.GetRef(n).Ptr);

            original.Free(Allocator.Temp);
        }

        [Test]
        public unsafe void ResizingPortArray_ToSameSize_DoesNotReallocate()
        {
            var array = new TestDoublePortArray();
            void* blank = (void*)0x10;

            array.Resize(27, blank, Allocator.Temp);
            var oldPtr = array.Ptr;
            array.Resize(27, blank, Allocator.Temp);
            Assert.IsTrue(oldPtr == array.Ptr);

            array.Free(Allocator.Temp);
        }

        public class UberNodeWithPortArrayForwarding
            : NodeDefinition<UberNodeWithPortArrayForwarding.Data, UberNodeWithPortArrayForwarding.SimPorts, UberNodeWithPortArrayForwarding.KernelData, UberNodeWithPortArrayForwarding.KernelDefs, UberNodeWithPortArrayForwarding.Kernel>, IMsgHandler<int>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                public PortArray<MessageInput<UberNodeWithPortArrayForwarding, int>> ForwardedMsgInputs;
                public PortArray<MessageOutput<UberNodeWithPortArrayForwarding, int>> ForwardedMsgOutputs;
#pragma warning restore 649
            }

            public struct KernelDefs : IKernelPortDefinition
            {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                public PortArray<DataInput<UberNodeWithPortArrayForwarding, int>> ForwardedDataInput;
                public DataOutput<UberNodeWithPortArrayForwarding, int> ForwardedDataOutputSum;
#pragma warning restore 649  // Assigned through internal DataFlowGraph reflection

            }

            public struct Data : INodeData
            {
                public NodeHandle<ArrayIONode> Child;
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
                data.Child = Set.Create<ArrayIONode>();

                ctx.ForwardInput(SimulationPorts.ForwardedMsgInputs, data.Child, ArrayIONode.SimulationPorts.Inputs);
                ctx.ForwardInput(KernelPorts.ForwardedDataInput, data.Child, ArrayIONode.KernelPorts.InputInt);
                ctx.ForwardOutput(SimulationPorts.ForwardedMsgOutputs, data.Child, ArrayIONode.SimulationPorts.Outputs);
                ctx.ForwardOutput(KernelPorts.ForwardedDataOutputSum, data.Child, ArrayIONode.KernelPorts.SumInt);
            }

            protected internal override void Destroy(DestroyContext ctx)
            {
                Set.Destroy(GetNodeData(ctx.Handle).Child);
            }

            public void HandleMessage(in MessageContext ctx, in int msg)
            {
                throw new NotImplementedException();
            }
        }

        [Test]
        public void MessagePortArrays_CanBeForwarded()
        {
            using (var set = new NodeSet())
            {
                var uber = set.Create<UberNodeWithPortArrayForwarding>();
                var result = set.Create<PassthroughTest<int>>();

                set.SetPortArraySize(uber, UberNodeWithPortArrayForwarding.SimulationPorts.ForwardedMsgInputs, 5);
                set.SetPortArraySize(uber, UberNodeWithPortArrayForwarding.SimulationPorts.ForwardedMsgOutputs, 5);
                set.Connect(uber, UberNodeWithPortArrayForwarding.SimulationPorts.ForwardedMsgOutputs, 2, result, PassthroughTest<int>.SimulationPorts.Input);

                set.SendMessage(uber, UberNodeWithPortArrayForwarding.SimulationPorts.ForwardedMsgInputs, 2, 4);
                set.SendTest<UberNodeWithPortArrayForwarding.Data>(uber, ctx =>
                    ctx.SendTest(ctx.NodeData.Child, (ArrayIONode.NodeData data) =>
                        Assert.AreEqual((2, 4), data.LastReceivedMsg)));
                set.SendTest(result, (PassthroughTest<int>.NodeData data) =>
                    Assert.AreEqual(4, data.LastReceivedMsg));

                set.Destroy(uber, result);
            }
        }

        [Test]
        public void DataPortArrays_CanBeForwarded()
        {
            using (var set = new NodeSet())
            {
                var uber = set.Create<UberNodeWithPortArrayForwarding>();

                set.SetPortArraySize(uber, UberNodeWithPortArrayForwarding.KernelPorts.ForwardedDataInput, 5);

                set.SetData(uber, UberNodeWithPortArrayForwarding.KernelPorts.ForwardedDataInput, 2, 99);
                set.SetData(uber, UberNodeWithPortArrayForwarding.KernelPorts.ForwardedDataInput, 4, 33);

                var result = set.CreateGraphValue(uber, UberNodeWithPortArrayForwarding.KernelPorts.ForwardedDataOutputSum);

                set.Update();
                Assert.AreEqual(99 + 33, set.GetValueBlocking(result));

                set.ReleaseGraphValue(result);

                set.Destroy(uber);
            }
        }

        [Test]
        public void CanConnectEntityNode_ToPortArray([Values] FixtureSystemType systemType)
        {
            using (var f = new Fixture<UpdateSystemDelegate>(systemType))
            {
                var entity = f.EM.CreateEntity(typeof(ECSInt));
                var entityNode = f.Set.CreateComponentNode(entity);
                var sumNode = f.Set.Create<KernelSumNode>();
                var gv = f.Set.CreateGraphValue(sumNode, KernelSumNode.KernelPorts.Output);

                for(int i = 1; i < 10; ++i)
                {
                    f.Set.SetPortArraySize(sumNode, KernelSumNode.KernelPorts.Inputs, (ushort)i);
                    f.EM.SetComponentData(entity, (ECSInt)i);
                    f.Set.Connect(entityNode, ComponentNode.Output<ECSInt>(), sumNode, KernelSumNode.KernelPorts.Inputs, i - 1);

                    f.System.Update();

                    Assert.AreEqual(i * i, f.Set.GetValueBlocking(gv).Value);
                }

                f.Set.Destroy(entityNode, sumNode);
                f.Set.ReleaseGraphValue(gv);
            }
        }

        public class PortArrayDebugNode : SimulationKernelNodeDefinition<PortArrayDebugNode.SimPorts, PortArrayDebugNode.KernelDefs>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
                public PortArray<MessageInput<PortArrayDebugNode, int>> Inputs;
                public PortArray<MessageOutput<PortArrayDebugNode, int>> Outputs;
            }

            public struct KernelDefs : IKernelPortDefinition
            {
                public PortArray<DataInput<PortArrayDebugNode, int>> Inputs;
                public DataOutput<PortArrayDebugNode, bool> AllGood;
            }

            struct EmptyKernelData : IKernelData { }

            struct Kernel : IGraphKernel<EmptyKernelData, KernelDefs>
            {
                public unsafe void Execute(RenderContext ctx, EmptyKernelData data, ref KernelDefs ports)
                {
                    ctx.Resolve(ref ports.AllGood) = false;

                    // Check ResolvedPortArrayDebugView.
                    var inputs = ctx.Resolve(ports.Inputs);
                    var dbgResolvedView = new RenderContext.ResolvedPortArrayDebugView<PortArrayDebugNode, int>(inputs);
                    if (inputs.Length == dbgResolvedView.Items.Length)
                    {
                        for (var i = 0; i < inputs.Length; ++i)
                            if (inputs[i] != dbgResolvedView.Items[i])
                                return;
                    }

                    // Check PortArrayDebugView on input.
                    var dbgInputView = new PortArrayDebugView<DataInput<PortArrayDebugNode, int>>(ports.Inputs);
                    if (inputs.Length == dbgInputView.Items.Length)
                    {
                        for (var i = 0; i < inputs.Length; ++i)
                            if (inputs[i] != UnsafeUtility.AsRef<int>(dbgInputView.Items[i].Ptr))
                                return;
                    }

                    ctx.Resolve(ref ports.AllGood) = true;
                }
            }

            struct Node : INodeData, IMsgHandler<int>
            {
                public void HandleMessage(in MessageContext ctx, in int msg) {}
            }
        }

        [Test]
        public void PortArrayDebugView_IsAlwaysEmpty_InSimulation()
        {
            Assert.Zero(new PortArrayDebugView<MessageInput<PortArrayDebugNode, int>>(PortArrayDebugNode.SimulationPorts.Inputs).Items.Length);
            Assert.Zero(new PortArrayDebugView<MessageOutput<PortArrayDebugNode, int>>(PortArrayDebugNode.SimulationPorts.Outputs).Items.Length);
            Assert.Zero(new PortArrayDebugView<DataInput<PortArrayDebugNode, int>>(PortArrayDebugNode.KernelPorts.Inputs).Items.Length);
        }

        [Test]
        public void PortArrayDebugView_AccuratelyMirrors_ResolvedPortArray()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<PortArrayDebugNode>();

                var gv = set.CreateGraphValue(node, PortArrayDebugNode.KernelPorts.AllGood);

                set.SetPortArraySize(node, PortArrayDebugNode.KernelPorts.Inputs, 17);
                for (int i=0; i<17; ++i)
                    set.SetData(node, PortArrayDebugNode.KernelPorts.Inputs, i, (i+1) * 10);

                set.Update();
                Assert.IsTrue(set.GetValueBlocking(gv));

                set.ReleaseGraphValue(gv);
                set.Destroy(node);
            }
        }
    }
}
