using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.Scripting;
using static Unity.DataFlowGraph.Tests.ComponentNodeSetTests;

namespace Unity.DataFlowGraph.Tests
{
    using TestInputBytePortArray = PortArray<DataInput<InvalidDefinitionSlot, byte>>;
    using TestInputDoublePortArray = PortArray<DataInput<InvalidDefinitionSlot, double>>;
    using TestOutputBytePortArray = PortArray<DataOutput<InvalidDefinitionSlot, byte>>;
    using TestOutputDoublePortArray = PortArray<DataOutput<InvalidDefinitionSlot, double>>;

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

                public PortArray<DataOutput<ArrayIONode, int>> OutputInt;
                public DataOutput<ArrayIONode, int> SumInt;
                public DataOutput<ArrayIONode, Buffer<int>> OutputBufferOfInts;
                public DataOutput<ArrayIONode, Aggregate> OutputAggregate;
            }

            struct EmptyKernelData : IKernelData { }

            [BurstCompile(CompileSynchronously = true)]
            struct Kernel : IGraphKernel<EmptyKernelData, KernelDefs>
            {
                public void Execute(RenderContext ctx, in EmptyKernelData data, ref KernelDefs ports)
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

                public void HandleMessage(MessageContext ctx, in int msg)
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
            TestInputBytePortArray.MaxSize >> 1
        };

        unsafe void ResizeNonBufferOutputArray(ref UntypedDataOutputPortArray array, ushort newSize, SimpleType elementType, Allocator allocator)
        {
            // Note: output array resizing requires intimate knowledge of Buffer<T> layout within the array element type.
            // Since we know we are dealing with a type that does not contain any buffers, provide the minimal information
            // required to achieve the resize using a fake DataPortDeclarations.OutputDeclaration.
            var portArrayDecl =
                new DataPortDeclarations.OutputDeclaration(elementType, default, 0, (0, 0), true);
            array.Resize(null, default, portArrayDecl, default, newSize, allocator);
        }

        unsafe void FreeNonBufferOutputArray(ref UntypedDataOutputPortArray array, Allocator allocator)
        {
            // Note: output array deallocation requires intimate knowledge of Buffer<T> layout within the array element type.
            // Since we know we are dealing with a type that does not contain any buffers, provide the minimal information
            // required to achieve the deallocation using a fake DataPortDeclarations.OutputDeclaration.
            var portArrayDecl =
                new DataPortDeclarations.OutputDeclaration(default, default, 0, (0, 0), true);
            array.Free(null, default, portArrayDecl, allocator);
        }

        [Test]
        public unsafe void CanResize_DefaultConstructed_DataPortArrays([ValueSource("s_ResizeParameters")] UInt16 size)
        {
            using (var sd = new RenderGraph.SharedData(SimpleType.MaxAlignment))
            {
                var inputArray = new TestInputBytePortArray();
                var untypedInput = inputArray.AsUntyped();

                untypedInput.Resize(size, sd.BlankPage, Allocator.Temp);
                untypedInput.Free(Allocator.Temp);

                var outputArray = new TestOutputBytePortArray();
                var untypedOutput = outputArray.AsUntyped();

                ResizeNonBufferOutputArray(ref untypedOutput, size, outputArray.GetElementType(), Allocator.Temp);
                FreeNonBufferOutputArray(ref untypedOutput, Allocator.Temp);
            }
        }

        static int[] s_InvalidResizeParameters = new int[] {
            -TestInputBytePortArray.MaxSize - 2,
            -TestInputBytePortArray.MaxSize - 1,
            -TestInputBytePortArray.MaxSize,
            -TestInputBytePortArray.MaxSize + 1,
            -2,
            -1,
            TestInputBytePortArray.MaxSize,
            TestInputBytePortArray.MaxSize + 1,
            TestInputBytePortArray.MaxSize << 1
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
                Assert.Throws<ArgumentException>(() => set.SetPortArraySize(node, ArrayIONode.KernelPorts.OutputInt, size));

                set.Destroy(node);
            }
        }

        [Test]
        public unsafe void CanContinuouslyResize_TheSameDataInputPortArray()
        {
            void* blank = (void*)0x10;
            const int k_Times = 100;
            var rng = new Mathematics.Random(seed: 0xFF);
            var array = new TestInputBytePortArray();
            ref var untyped = ref array.AsUntyped();

            for (int i = 0; i < k_Times; ++i)
            {
                var size = (UInt16)rng.NextUInt(0, TestInputBytePortArray.MaxSize);

                untyped.Resize(size, blank, Allocator.Temp);

                Assert.AreEqual(size, array.Size);
                Assert.IsTrue(array.Ptr != null);

                for (ushort n = 0; n < size; ++n)
                    Assert.IsTrue(blank == array.GetRef(n).Ptr);
            }

            untyped.Free(Allocator.Temp);
        }

        [Test]
        public unsafe void CanContinuouslyResize_TheSameDataOutputPortArray()
        {
            const int k_Times = 100;
            var rng = new Mathematics.Random(seed: 0xFF);
            var array = new TestOutputBytePortArray();
            ref var untyped = ref array.AsUntyped();

            for (int i = 0; i < k_Times; ++i)
            {
                var size = (UInt16)rng.NextUInt(0, TestInputBytePortArray.MaxSize);

                ResizeNonBufferOutputArray(ref untyped, size, array.GetElementType(), Allocator.Temp);

                Assert.AreEqual(size, array.Size);
                Assert.IsTrue(array.Ptr != null);
            }

            FreeNonBufferOutputArray(ref untyped, Allocator.Temp);
        }

        [Test]
        public unsafe void ResizingPortArray_CorrectlyUpdatesBlankValue()
        {
            const int k_Times = 100;
            var rng = new Mathematics.Random(seed: 0xFF);
            var array = new TestInputBytePortArray();
            ref var untyped = ref array.AsUntyped();

            for (int i = 0; i < k_Times; ++i)
            {
                var oldSize = array.Size;
                var size = (UInt16)rng.NextUInt(0, TestInputBytePortArray.MaxSize);

                untyped.Resize(size, (void*)(i * 8), Allocator.Temp);

                for (ushort n = oldSize; n < size; ++n)
                    Assert.IsTrue((void*)(i * 8) == array.GetRef(n).Ptr);

            }

            untyped.Free(Allocator.Temp);
        }

        [Test]
        public unsafe void ResizingPortArrays_ToSameSize_DoesNotReallocate()
        {
            var inputArray = new TestInputDoublePortArray();
            ref var inputUntyped = ref inputArray.AsUntyped();
            void* blank = (void*)0x10;

            inputUntyped.Resize(27, blank, Allocator.Temp);
            var oldPtr = inputArray.Ptr;
            inputUntyped.Resize(27, blank, Allocator.Temp);
            Assert.IsTrue(oldPtr == inputArray.Ptr);

            inputUntyped.Free(Allocator.Temp);

            var outputArray = new TestOutputDoublePortArray();
            ref var outputUntyped = ref outputArray.AsUntyped();

            var portArrayDecl =
                new DataPortDeclarations.OutputDeclaration(outputArray.GetElementType(), outputArray.GetElementType(), 0, (0, 0), true);

            outputUntyped.Resize(null,default, portArrayDecl, default, 27, Allocator.Temp);
            oldPtr = outputArray.Ptr;
            outputUntyped.Resize(null,default, portArrayDecl, default, 27, Allocator.Temp);
            Assert.IsTrue(oldPtr == outputArray.Ptr);

            outputUntyped.Free(null, default, portArrayDecl, Allocator.Temp);
        }

        public class UberNodeWithPortArrayForwarding
            : SimulationKernelNodeDefinition<UberNodeWithPortArrayForwarding.SimPorts, UberNodeWithPortArrayForwarding.KernelDefs>
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

            struct KernelData : IKernelData { }

            [BurstCompile(CompileSynchronously = true)]
            struct Kernel : IGraphKernel<KernelData, KernelDefs>
            {
                public void Execute(RenderContext ctx, in KernelData data, ref KernelDefs ports)
                {
                }
            }

            public struct Data : INodeData, IInit, IDestroy, IMsgHandler<int>
            {
                public NodeHandle<ArrayIONode> Child;

                public void Init(InitContext ctx)
                {
                    Child = ctx.Set.Create<ArrayIONode>();

                    ctx.ForwardInput(SimulationPorts.ForwardedMsgInputs, Child, ArrayIONode.SimulationPorts.Inputs);
                    ctx.ForwardInput(KernelPorts.ForwardedDataInput, Child, ArrayIONode.KernelPorts.InputInt);
                    ctx.ForwardOutput(SimulationPorts.ForwardedMsgOutputs, Child, ArrayIONode.SimulationPorts.Outputs);
                    ctx.ForwardOutput(KernelPorts.ForwardedDataOutputSum, Child, ArrayIONode.KernelPorts.SumInt);
                }

                public void Destroy(DestroyContext ctx)
                {
                    ctx.Set.Destroy(Child);
                }

                public void HandleMessage(MessageContext ctx, in int msg)
                {
                    throw new NotImplementedException();
                }
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

        [Test]
        public void CanConnectPortArray_ToEntityNode([Values] FixtureSystemType systemType)
        {
            using (var f = new Fixture<UpdateSystemDelegate>(systemType))
            {
                var entity = f.EM.CreateEntity(typeof(ECSInt));
                var entityNode = f.Set.CreateComponentNode(entity);
                var outputNode = f.Set.Create<KernelArrayOutputNode>();

                for(int i = 1; i < 10; ++i)
                {
                    f.Set.SetPortArraySize(outputNode, KernelArrayOutputNode.KernelPorts.Outputs, (ushort)i);

                    if (i % 2 == 1) // only change connectivity every other update to ensure patching works independently of topology changes.
                        f.Set.Connect(outputNode, KernelArrayOutputNode.KernelPorts.Outputs, i - 1, entityNode, ComponentNode.Input<ECSInt>());

                    f.System.Update();

                    Assert.AreEqual((i-1) / 2 * 2 + i, f.EM.GetComponentData<ECSInt>(entity).Value);

                    if (i % 2 == 0)
                        f.Set.Disconnect(outputNode, KernelArrayOutputNode.KernelPorts.Outputs, i - 2, entityNode, ComponentNode.Input<ECSInt>());
                }

                f.Set.Destroy(entityNode, outputNode);
            }
        }

        public interface ITestable
        {
            void Test();
        }

        public class DataOutputPortArrays_HaveExpectedAlignment_Node<T>
            : KernelNodeDefinition<DataOutputPortArrays_HaveExpectedAlignment_Node<T>.Ports>
            , ITestable
                where T : struct
        {
            const int k_PortArraySize = 2;

            public struct Ports : IKernelPortDefinition
            {
                public PortArray<DataOutput<DataOutputPortArrays_HaveExpectedAlignment_Node<T>, T>> Array;
                public DataOutput<DataOutputPortArrays_HaveExpectedAlignment_Node<T>, long> FirstItemPtr, SecondItemPtr;
            }

            struct EmptyKernelData : IKernelData {}

            [BurstCompile(CompileSynchronously = true)]
            struct Kernel : IGraphKernel<EmptyKernelData, Ports>
            {
                public unsafe void Execute(RenderContext ctx, in EmptyKernelData data, ref Ports ports)
                {
                    var resolved = ctx.Resolve(ref ports.Array);
                    ctx.Resolve(ref ports.FirstItemPtr) = (long)resolved.GetUnsafePtr();
                    ctx.Resolve(ref ports.SecondItemPtr) = (long)resolved.GetUnsafePtr() + resolved.Stride;
                }
            }

            public override string ToString() => typeof(T).Name;

            public void Test()
            {
                using (var set = new NodeSet())
                {
                    var node = set.Create<DataOutputPortArrays_HaveExpectedAlignment_Node<T>>();
                    set.SetPortArraySize(node, KernelPorts.Array, k_PortArraySize);
                    var gvs = new[]
                    {
                        set.CreateGraphValue(node, KernelPorts.FirstItemPtr),
                        set.CreateGraphValue(node, KernelPorts.SecondItemPtr)
                    };

                    set.Update();
                    var ptrs = new[] {set.GetValueBlocking(gvs[0]), set.GetValueBlocking(gvs[1])};

                    var simpleType = SimpleType.Create<bool>();
                    var expectedType = KernelPorts.Array.GetElementType();
                    for (int i = 0; i < k_PortArraySize; ++i)
                    {
                        Assert.Zero(ptrs[i] % DataInputStorage.MinimumInputAlignment);
                        Assert.Zero(ptrs[i] % simpleType.Align);
                        Assert.Zero(ptrs[i] % expectedType.Align);
                    }

                    Assert.AreEqual(expectedType.Size, ptrs[1] - ptrs[0]);

                    set.ReleaseGraphValue(gvs[0]);
                    set.ReleaseGraphValue(gvs[1]);
                    set.Destroy(node);
                }
            }
        }

        [Preserve]
        public static IEnumerable<ITestable> DataOutputPortArrays_HaveExpectedAlignment_TestCases()
        {
            yield return new DataOutputPortArrays_HaveExpectedAlignment_Node<bool>();
            yield return new DataOutputPortArrays_HaveExpectedAlignment_Node<byte>();
            yield return new DataOutputPortArrays_HaveExpectedAlignment_Node<short>();
            yield return new DataOutputPortArrays_HaveExpectedAlignment_Node<int>();
            yield return new DataOutputPortArrays_HaveExpectedAlignment_Node<float>();
            yield return new DataOutputPortArrays_HaveExpectedAlignment_Node<double>();
            yield return new DataOutputPortArrays_HaveExpectedAlignment_Node<long>();
            yield return new DataOutputPortArrays_HaveExpectedAlignment_Node<LowLevelNodeTraitsTests.ThreeByteStruct>();
            yield return new DataOutputPortArrays_HaveExpectedAlignment_Node<LowLevelNodeTraitsTests.ShortAndByteStruct>();
            yield return new DataOutputPortArrays_HaveExpectedAlignment_Node<LowLevelNodeTraitsTests.PointerStruct>();
            yield return new DataOutputPortArrays_HaveExpectedAlignment_Node<LowLevelNodeTraitsTests.TwoPointersStruct>();
            yield return new DataOutputPortArrays_HaveExpectedAlignment_Node<LowLevelNodeTraitsTests.ThreePointersStruct>();
            yield return new DataOutputPortArrays_HaveExpectedAlignment_Node<LowLevelNodeTraitsTests.ThreePointersAndAByteStruct>();
            yield return new DataOutputPortArrays_HaveExpectedAlignment_Node<LowLevelNodeTraitsTests.ThreePointersAndAShortStruct>();
            yield return new DataOutputPortArrays_HaveExpectedAlignment_Node<LowLevelNodeTraitsTests.BigStructWithPointers>();
            yield return new DataOutputPortArrays_HaveExpectedAlignment_Node<LowLevelNodeTraitsTests.EmptyStruct>();
        }

        [TestCaseSource(nameof(DataOutputPortArrays_HaveExpectedAlignment_TestCases))]
        public void DataOutputPortArrays_HaveExpectedAlignment(ITestable test)
            => test.Test();


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
                public PortArray<DataOutput<PortArrayDebugNode, int>> Outputs;
                public DataOutput<PortArrayDebugNode, bool> AllGood;
            }

            struct EmptyKernelData : IKernelData { }

            struct Kernel : IGraphKernel<EmptyKernelData, KernelDefs>
            {
                public unsafe void Execute(RenderContext ctx, in EmptyKernelData data, ref KernelDefs ports)
                {
                    ctx.Resolve(ref ports.AllGood) = false;

                    // Check ResolvedInputPortArrayDebugView.
                    var inputs = ctx.Resolve(ports.Inputs);
                    var dbgResolvedInputView = new RenderContext.ResolvedInputPortArrayDebugView<PortArrayDebugNode, int>(inputs);
                    if (inputs.Length == dbgResolvedInputView.Items.Length)
                    {
                        for (var i = 0; i < inputs.Length; ++i)
                            if (inputs[i] != dbgResolvedInputView.Items[i])
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
                public void HandleMessage(MessageContext ctx, in int msg) {}
            }
        }

        [Test]
        public void PortArrayDebugView_IsAlwaysEmpty_InSimulation()
        {
            Assert.Zero(new PortArrayDebugView<MessageInput<PortArrayDebugNode, int>>(PortArrayDebugNode.SimulationPorts.Inputs).Items.Length);
            Assert.Zero(new PortArrayDebugView<MessageOutput<PortArrayDebugNode, int>>(PortArrayDebugNode.SimulationPorts.Outputs).Items.Length);
            Assert.Zero(new PortArrayDebugView<DataInput<PortArrayDebugNode, int>>(PortArrayDebugNode.KernelPorts.Inputs).Items.Length);
            Assert.Zero(new PortArrayDebugView<DataOutput<PortArrayDebugNode, int>>(PortArrayDebugNode.KernelPorts.Outputs).Items.Length);
        }

        [Test]
        public void PortArrayDebugView_AccuratelyMirrors_ResolvedPortArray()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<PortArrayDebugNode>();

                var gv = set.CreateGraphValue(node, PortArrayDebugNode.KernelPorts.AllGood);

                set.SetPortArraySize(node, PortArrayDebugNode.KernelPorts.Inputs, 17);
                set.SetPortArraySize(node, PortArrayDebugNode.KernelPorts.Outputs, 21);
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
