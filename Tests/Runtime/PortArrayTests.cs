using System;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using static Unity.DataFlowGraph.Tests.ComponentNodeSetTests;

namespace Unity.DataFlowGraph.Tests
{
    using UntypedPortArray = PortArray<DataInput<InvalidDefinitionSlot, byte>>;

    public class PortArrayTests
    {
        public class ArrayIONode : NodeDefinition<ArrayIONode.EmptyData, ArrayIONode.SimPorts, EmptyKernelData, ArrayIONode.KernelDefs, ArrayIONode.Kernel>, IMsgHandler<int>
        {
            public struct EmptyData : INodeData {}

            public struct SimPorts : ISimulationPortDefinition
            {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                public PortArray<MessageInput<ArrayIONode, int>> Inputs;
#pragma warning restore 649
            }

            public struct Aggregate
            {
                public long Long;
                public Buffer<long> BufferOfLongs;
            }

            public struct KernelDefs : IKernelPortDefinition
            {
#pragma warning disable 649  // never assigned
                public PortArray<DataInput<ArrayIONode, int>> InputInt;
                public PortArray<DataInput<ArrayIONode, Buffer<int>>> InputBufferOfInts;
                public PortArray<DataInput<ArrayIONode, Aggregate>> InputAggregate;

                public DataOutput<ArrayIONode, int> SumInt;
                public DataOutput<ArrayIONode, Buffer<int>> OutputBufferOfInts;
                public DataOutput<ArrayIONode, Aggregate> OutputAggregate;
#pragma warning restore 649
            }

            [BurstCompile(CompileSynchronously = true)]
            public struct Kernel : IGraphKernel<EmptyKernelData, KernelDefs>
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

            public static (int, int) s_LastReceivedMsg;
            public void HandleMessage(in MessageContext ctx, in int msg)
            {
                s_LastReceivedMsg = (ctx.ArrayIndex, msg);
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
        public void DefaultConstructed_InputPortArrayID_IsAnArrayPort()
        {
            InputPortArrayID id = default;
            ushort arrayIndex;

            Assert.True(id.IsArray);
            Assert.DoesNotThrow(() => arrayIndex = id.ArrayIndex);
        }

        [Test]
        public void ArrayConstructorFor_InputPortArrayID_WithSentinel_Throws()
        {
            Assert.Throws<InvalidOperationException>(() => new InputPortArrayID(portId: default, InputPortArrayID.NonArraySentinel));
        }

        static UInt16[] s_ArrayConstructorParameters = new ushort[] {
            (ushort)0u,
            (ushort)1u,
            (ushort)4u,
            (ushort)13u,
            ushort.MaxValue - 1
        };

        [Test]
        public void ArrayConstructorFor_InputPortArrayID_IsAnArrayPort([ValueSource("s_ArrayConstructorParameters")] ushort arrayIndex)
        {
            InputPortArrayID id = new InputPortArrayID(portId: default, arrayIndex);

            Assert.True(id.IsArray);
            Assert.DoesNotThrow(() => arrayIndex = id.ArrayIndex);
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
            (ushort)(UntypedPortArray.MaxSize >> 1)
        };

        [Test]
        public unsafe void CanResize_DefaultConstructed_PortArray([ValueSource("s_ResizeParameters")] UInt16 size)
        {
            using (var sd = new RenderGraph.SharedData(16))
            {
                var array = new UntypedPortArray();

                UntypedPortArray.Resize(ref array, size, sd.BlankPage, Allocator.Temp);
                UntypedPortArray.Free(ref array, Allocator.Temp);
            }
        }

        [Test]
        public unsafe void CannotSizeAPortArray_ToMaxSize()
        {
#if DFG_ASSERTIONS
            using (var sd = new RenderGraph.SharedData(16))
            {
                var array = new UntypedPortArray();

                Assert.Throws<AssertionException>(
                    () => UntypedPortArray.Resize(ref array, UntypedPortArray.MaxSize, sd.BlankPage, Allocator.Temp)
                );

                UntypedPortArray.Free(ref array, Allocator.Temp);
            }
#endif

            using (var set = new NodeSet())
            {
                var node = set.Create<ArrayIONode>();

                Assert.Throws<ArgumentException>(
                    () => set.SetPortArraySize(node, ArrayIONode.KernelPorts.InputInt, UntypedPortArray.MaxSize)
                );

                set.Destroy(node);
            }
        }

        [Test]
        public unsafe void CanContinuouslyResize_TheSamePortArray()
        {
            void* blank = (void*)0x15;
            const int k_Times = 100;
            var rng = new Mathematics.Random(seed: 0xFF);
            var array = new UntypedPortArray();

            for (int i = 0; i < k_Times; ++i)
            {
                var size = (UInt16)rng.NextUInt(0, UntypedPortArray.MaxSize);

                UntypedPortArray.Resize(ref array, size, blank, Allocator.Temp);

                Assert.AreEqual(size, array.Size);
                Assert.IsTrue(array.Ptr != null);

                for (ushort n = 0; n < size; ++n)
                    Assert.IsTrue(blank == array[n].Ptr);
            }

            UntypedPortArray.Free(ref array, Allocator.Temp);
        }

        [Test]
        public unsafe void ResizingPortArray_CorrectlyUpdatesBlankValue()
        {
            const int k_Times = 100;
            var rng = new Mathematics.Random(seed: 0xFF);
            var array = new UntypedPortArray();

            for (int i = 0; i < k_Times; ++i)
            {
                var oldSize = array.Size;
                var size = (UInt16)rng.NextUInt(0, UntypedPortArray.MaxSize);

                UntypedPortArray.Resize(ref array, size, (void*)i, Allocator.Temp);

                for (ushort n = oldSize; n < size; ++n)
                    Assert.IsTrue((void*)i == array[n].Ptr);

            }

            UntypedPortArray.Free(ref array, Allocator.Temp);
        }

        [Test]
        public unsafe void CanResizePortArray_ThroughAlias()
        {
            var original = new PortArray<DataInput<InvalidDefinitionSlot, double>>();
            void* blank = (void*)0x13;

            PortArray<DataInput<InvalidDefinitionSlot, double>>.Resize(ref original, 27, blank, Allocator.Temp);

            Assert.AreEqual(27, original.Size);

            ref var alias = ref Unsafe.AsRef<UntypedPortArray>(Unsafe.AsPointer(ref original));

            UntypedPortArray.Resize(ref alias, 53, blank, Allocator.Temp);

            Assert.AreEqual(53, original.Size);

            for (ushort n = 0; n < original.Size; ++n)
                Assert.IsTrue(blank == original[n].Ptr);

            UntypedPortArray.Free(ref original, Allocator.Temp);
        }

        [Test]
        public unsafe void ResizingPortArray_ToSameSize_DoesNotReallocate()
        {
            var array = new PortArray<DataInput<InvalidDefinitionSlot, double>>();
            void* blank = (void*)0x13;

            PortArray<DataInput<InvalidDefinitionSlot, double>>.Resize(ref array, 27, blank, Allocator.Temp);
            var oldPtr = array.Ptr;
            PortArray<DataInput<InvalidDefinitionSlot, double>>.Resize(ref array, 27, blank, Allocator.Temp);
            Assert.IsTrue(oldPtr == array.Ptr);

            UntypedPortArray.Free(ref array, Allocator.Temp);
        }

        public class UberNodeWithPortArrayForwarding
            : NodeDefinition<UberNodeWithPortArrayForwarding.Data, UberNodeWithPortArrayForwarding.SimPorts, UberNodeWithPortArrayForwarding.KernelData, UberNodeWithPortArrayForwarding.KernelDefs, UberNodeWithPortArrayForwarding.Kernel>, IMsgHandler<int>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                public PortArray<MessageInput<UberNodeWithPortArrayForwarding, int>> ForwardedMsgInputs;
                public MessageOutput<UberNodeWithPortArrayForwarding, int> ForwardedMsgOutput;
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

                set.SetPortArraySize(uber, UberNodeWithPortArrayForwarding.SimulationPorts.ForwardedMsgInputs, 5);

                ArrayIONode.s_LastReceivedMsg = (0, 0);
                set.SendMessage(uber, UberNodeWithPortArrayForwarding.SimulationPorts.ForwardedMsgInputs, 2, 4);
                Assert.AreEqual((2, 4), ArrayIONode.s_LastReceivedMsg);

                set.Destroy(uber);
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
    }
}
