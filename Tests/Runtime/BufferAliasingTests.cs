using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;

namespace Unity.DataFlowGraph.Tests
{
    public class BufferAliasingTests
    {
        /*
         * Make sure Resolve().GetUnsafePtr() == m_Value.Ptr
         *
         *
         */

        public struct BufferElement : IBufferElementData
        {
            public long Contents;

            public static implicit operator BufferElement(long c)
                => new BufferElement { Contents = c };

            public static implicit operator long(BufferElement b)
                => b.Contents;
        }


        public struct Aggregate
        {
            public Buffer<BufferElement> SubBuffer1;
            public Buffer<BufferElement> SubBuffer2;
        }

        public unsafe class BufferNode : KernelNodeDefinition<BufferNode.KernelDefs>
        {
            public struct KernelDefs : IKernelPortDefinition
            {
                public DataInput<BufferNode, Buffer<BufferElement>> Input;
                public PortArray<DataInput<BufferNode, Aggregate>> InputArray;

                public DataOutput<BufferNode, Aggregate> InputSumAsAggr;
                public DataOutput<BufferNode, Buffer<BufferElement>> InputSumAsScalar;
                public DataOutput<BufferNode, Buffer<BufferElement>> PortArraySum;
                public PortArray<DataOutput<BufferNode, Aggregate>> PortArrayElementSumsAsAggr;
                public PortArray<DataOutput<BufferNode, Buffer<BufferElement>>> PortArrayElementSumsAsScalar;

                public long CheckIOAliasing(RenderContext c)
                {
                    var resolvedInputSumAsAggr = c.Resolve(ref InputSumAsAggr);
                    var resolvedInputSumAsScalar = c.Resolve(ref InputSumAsScalar);
                    var resolvedPortArraySum = c.Resolve(ref PortArraySum);

                    var outputPointers = new NativeList<IntPtr>(Allocator.Temp);
                    outputPointers.Add(new IntPtr(InputSumAsAggr.m_Value.SubBuffer1.Ptr));
                    outputPointers.Add(new IntPtr(InputSumAsAggr.m_Value.SubBuffer2.Ptr));
                    outputPointers.Add(new IntPtr(InputSumAsScalar.m_Value.Ptr));
                    outputPointers.Add(new IntPtr(PortArraySum.m_Value.Ptr));

                    if (InputSumAsAggr.m_Value.SubBuffer1.Ptr != resolvedInputSumAsAggr.SubBuffer1.ToNative(c).GetUnsafePtr())
                        return 1;
                    if (InputSumAsAggr.m_Value.SubBuffer2.Ptr != resolvedInputSumAsAggr.SubBuffer2.ToNative(c).GetUnsafePtr())
                        return 2;
                    if (InputSumAsScalar.m_Value.Ptr != resolvedInputSumAsScalar.GetUnsafePtr())
                        return 3;
                    if (PortArraySum.m_Value.Ptr != resolvedPortArraySum.GetUnsafePtr())
                        return 4;

                    var resolvedPortArrayElementSumsAsScalar = c.Resolve(ref PortArrayElementSumsAsScalar);
                    for (int i = 0; i < resolvedPortArrayElementSumsAsScalar.Length; ++i)
                    {
                        outputPointers.Add(new IntPtr(resolvedPortArrayElementSumsAsScalar[i].ToNative(c).GetUnsafePtr()));
                    }

                    var resolvedPortArrayElementSumAsAggr = c.Resolve(ref PortArrayElementSumsAsAggr);
                    for (int i = 0; i < resolvedPortArrayElementSumAsAggr.Length; ++i)
                    {
                        outputPointers.Add(new IntPtr(resolvedPortArrayElementSumAsAggr[i].SubBuffer1.ToNative(c).GetUnsafePtr()));
                        outputPointers.Add(new IntPtr(resolvedPortArrayElementSumAsAggr[i].SubBuffer2.ToNative(c).GetUnsafePtr()));
                    }

                    // No non-null output pointers should alias one another.
                    for (int i = 0; i < outputPointers.Length; ++i)
                        for (int j = i + 1; j < outputPointers.Length && outputPointers[i] != new IntPtr(null); ++j)
                            if (outputPointers[i] == outputPointers[j])
                                return 1000000 + i * 1000 + j;

                    var resolvedInput = c.Resolve(Input);
                    var resolvedInputArray = c.Resolve(InputArray);

                    var inputPointers = new NativeList<IntPtr>(Allocator.Temp);
                    inputPointers.Add(new IntPtr(Input.Ptr));
                    inputPointers.Add(new IntPtr(InputArray.Ptr));
                    inputPointers.Add(new IntPtr(resolvedInput.GetUnsafeReadOnlyPtr()));
                    for (int i = 0; i < resolvedInputArray.Length; ++i)
                    {
                        var port = resolvedInputArray[i];
                        inputPointers.Add(new IntPtr(port.SubBuffer1.Ptr));
                        inputPointers.Add(new IntPtr(port.SubBuffer2.Ptr));
                        if (port.SubBuffer1.Ptr != port.SubBuffer1.ToNative(c).GetUnsafeReadOnlyPtr())
                            return 2000000 + i * 1000 + 1;
                        if (port.SubBuffer2.Ptr != port.SubBuffer2.ToNative(c).GetUnsafeReadOnlyPtr())
                            return 2000000 + i * 1000 + 2;
                    }

                    // No inputs should alias non-null outputs.
                    for (int i = 0; i < outputPointers.Length; ++i)
                        for (int j = 0; j < inputPointers.Length && outputPointers[i] != new IntPtr(null); ++j)
                            if (outputPointers[i] == inputPointers[j])
                                return 3000000 + i * 1000 + j;

                    return 0;
                }
            }

            public struct Data : IKernelData
            {
                public long* AliasResult;
            }

            [BurstCompile(CompileSynchronously = true)]
            struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                public void Execute(RenderContext ctx, in Data data, ref KernelDefs ports)
                {
                    *data.AliasResult = ports.CheckIOAliasing(ctx);

                    long sum = 0;

                    var buffer = ctx.Resolve(ports.Input);
                    for (int i = 0; i < buffer.Length; ++i)
                        sum += buffer[i];

                    var aggr = ctx.Resolve(ref ports.InputSumAsAggr);

                    buffer = aggr.SubBuffer1.ToNative(ctx);

                    for (int i = 0; i < buffer.Length; ++i)
                        buffer[i] = sum;

                    buffer = aggr.SubBuffer2.ToNative(ctx);

                    for (int i = 0; i < buffer.Length; ++i)
                        buffer[i] = sum * 2;

                    buffer = ctx.Resolve(ref ports.InputSumAsScalar);
                    for (int i = 0; i < buffer.Length; ++i)
                        buffer[i] = sum;

                    sum = 0;

                    var portArray = ctx.Resolve(ports.InputArray);

                    var elemBuffer = ctx.Resolve(ref ports.PortArrayElementSumsAsScalar);
                    for (int p = 0; p < elemBuffer.Length; ++p)
                    {
                        buffer = elemBuffer[p].ToNative(ctx);
                        for (int i = 0; i < buffer.Length; ++i)
                            buffer[i] = 0;
                    }

                    var elemAggr = ctx.Resolve(ref ports.PortArrayElementSumsAsAggr);
                    for (int p = 0; p < elemAggr.Length; ++p)
                    {
                        aggr = elemAggr[p];
                        buffer = aggr.SubBuffer1.ToNative(ctx);
                        for (int i = 0; i < buffer.Length; ++i)
                            buffer[i] = 0;
                        buffer = aggr.SubBuffer2.ToNative(ctx);
                        for (int i = 0; i < buffer.Length; ++i)
                            buffer[i] = 0;
                    }

                    for(int p = 0; p < portArray.Length; ++p)
                    {
                        long elemSum = 0;

                        buffer = portArray[p].SubBuffer1.ToNative(ctx);
                        for (int i = 0; i < buffer.Length; ++i)
                            elemSum += buffer[i];

                        buffer = portArray[p].SubBuffer2.ToNative(ctx);
                        for (int i = 0; i < buffer.Length; ++i)
                            elemSum += buffer[i];

                        sum += elemSum;

                        if (p < elemBuffer.Length)
                        {
                            buffer = elemBuffer[p].ToNative(ctx);
                            for (int i = 0; i < buffer.Length; ++i)
                                buffer[i] += elemSum;
                        }

                        if (p < elemAggr.Length)
                        {
                            aggr = elemAggr[p];
                            buffer = aggr.SubBuffer1.ToNative(ctx);
                            for (int i = 0; i < buffer.Length; ++i)
                                buffer[i] += elemSum;
                            buffer = aggr.SubBuffer2.ToNative(ctx);
                            for (int i = 0; i < buffer.Length; ++i)
                                buffer[i] += elemSum * 2;
                        }
                    }

                    buffer = ctx.Resolve(ref ports.PortArraySum);
                    for (int i = 0; i < buffer.Length; ++i)
                        buffer[i] = sum;
                }
            }
        }

        public unsafe class SpliceNode : KernelNodeDefinition<SpliceNode.KernelDefs>
        {
            public struct KernelDefs : IKernelPortDefinition
            {
                public DataInput<SpliceNode, Buffer<BufferElement>> Input;
                public DataInput<SpliceNode, Buffer<BufferElement>> Input2;
                public DataOutput<SpliceNode, Aggregate> AggrSum;
                public DataOutput<SpliceNode, Buffer<BufferElement>> ScalarSum;
            }

            public struct Data : IKernelData
            {
                public long* AliasResult;
            }

            [BurstCompile(CompileSynchronously = true)]
            struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                public void Execute(RenderContext ctx, in Data data, ref KernelDefs ports)
                {
                    long sum = 0;

                    var buffer = ctx.Resolve(ports.Input);
                    for (int i = 0; i < buffer.Length; ++i)
                        sum += buffer[i];

                    var aggr = ctx.Resolve(ref ports.AggrSum);

                    buffer = aggr.SubBuffer1.ToNative(ctx);

                    for (int i = 0; i < buffer.Length; ++i)
                        buffer[i] = sum;

                    buffer = aggr.SubBuffer2.ToNative(ctx);

                    for (int i = 0; i < buffer.Length; ++i)
                        buffer[i] = sum * 2;

                    sum = 0;

                    buffer = ctx.Resolve(ports.Input2);
                    for (int i = 0; i < buffer.Length; ++i)
                        sum += buffer[i];

                    buffer = ctx.Resolve(ref ports.ScalarSum);
                    for (int i = 0; i < buffer.Length; ++i)
                        buffer[i] = sum;
                }
            }
        }

        public class StartPoint : KernelNodeDefinition<StartPoint.KernelDefs>
        {
            public struct KernelDefs : IKernelPortDefinition
            {
                public DataOutput<StartPoint, Aggregate> AggregateOutput;
                public DataOutput<StartPoint, Buffer<BufferElement>> ScalarOutput;
            }

            public struct KernelData : IKernelData
            {
                public long AggregateFill, ScalarFill;
            }

            [BurstCompile(CompileSynchronously = true)]
            struct Kernel : IGraphKernel<KernelData, KernelDefs>
            {
                public void Execute(RenderContext ctx, in KernelData data, ref KernelDefs ports)
                {
                    var aggr = ctx.Resolve(ref ports.AggregateOutput);

                    var buffer = aggr.SubBuffer1.ToNative(ctx);

                    for (int i = 0; i < buffer.Length; ++i)
                        buffer[i] = data.AggregateFill;

                    buffer = aggr.SubBuffer2.ToNative(ctx);

                    for (int i = 0; i < buffer.Length; ++i)
                        buffer[i] = data.AggregateFill * 2;

                    buffer = ctx.Resolve(ref ports.ScalarOutput);
                    for (int i = 0; i < buffer.Length; ++i)
                        buffer[i] = data.ScalarFill;
                }
            }
        }

        internal unsafe class PointerPool : IDisposable
        {
            BlitList<IntPtr> Pointers = new BlitList<IntPtr>(0);

            public long* CreateLong()
            {
                var res = Utility.CAlloc<long>(Allocator.Persistent);

                Pointers.Add((IntPtr)res);

                return (long*)res;
            }

            #region IDisposable Support
            private bool disposedValue = false; // To detect redundant calls

            protected virtual void Dispose(bool disposing)
            {
                if (!disposedValue)
                {
                    if (disposing)
                    {
                        // TODO: dispose managed state (managed objects).
                    }

                    for (int i = 0; i < Pointers.Count; ++i)
                        UnsafeUtility.Free(Pointers[i].ToPointer(), Allocator.Persistent);

                    Pointers.Dispose();

                    disposedValue = true;
                }
            }

            ~PointerPool() {
              Dispose(false);
            }

            // This code added to correctly implement the disposable pattern.
            public void Dispose()
            {
                // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
                Dispose(true);
                // TODO: uncomment the following line if the finalizer is overridden above.
                // GC.SuppressFinalize(this);
            }
            #endregion

        }

        [DisableAutoCreation, AlwaysUpdateSystem]
        public class UpdateSystem : JobComponentSystem
        {
            public NodeSet Set;

            protected override JobHandle OnUpdate(JobHandle inputDeps)
            {
                return Set.Update(inputDeps);
            }
        }

        unsafe class Fixture : IDisposable
        {
            public const int BufferSize = 3;

            public NodeSet Set;
            PointerPool m_AliasResults = new PointerPool();

            public Fixture(NodeSet.RenderExecutionModel model, NodeSet.RenderOptimizations opt = default)
            {
                Set = new NodeSet();
                Set.RendererModel = model;
                Set.RendererOptimizations = opt;
            }

            public Fixture(UpdateSystem s, NodeSet.RenderExecutionModel model, NodeSet.RenderOptimizations opt = default)
            {
                Set = new NodeSet(s);
                s.Set = Set;
                Set.RendererModel = model;
                Set.RendererOptimizations = opt;
            }

            public void Dispose()
            {
                Set.Dispose();
                m_AliasResults.Dispose();
            }

            public NodeHandle<BufferNode> CreateNode()
            {
                var node = Set.Create<BufferNode>();
                Aggregate ag;

                ag.SubBuffer1 = Buffer<BufferElement>.SizeRequest(BufferSize);
                ag.SubBuffer2 = Buffer<BufferElement>.SizeRequest(BufferSize);

                Set.SetBufferSize(node, BufferNode.KernelPorts.InputSumAsAggr, ag);
                Set.SetBufferSize(node, BufferNode.KernelPorts.PortArraySum, Buffer<BufferElement>.SizeRequest(BufferSize));
                Set.SetBufferSize(node, BufferNode.KernelPorts.InputSumAsScalar, Buffer<BufferElement>.SizeRequest(BufferSize));

                Set.SetPortArraySize(node, BufferNode.KernelPorts.InputArray, BufferSize);

                Set.SetPortArraySize(node, BufferNode.KernelPorts.PortArrayElementSumsAsScalar, BufferSize);
                Set.SetPortArraySize(node, BufferNode.KernelPorts.PortArrayElementSumsAsAggr, BufferSize);

                for (int i = 0; i < BufferSize; ++i)
                {
                    Set.SetBufferSize(node, BufferNode.KernelPorts.PortArrayElementSumsAsAggr, i, ag);
                    Set.SetBufferSize(node, BufferNode.KernelPorts.PortArrayElementSumsAsScalar, i, Buffer<BufferElement>.SizeRequest(BufferSize));
                }

                Set.GetKernelData<BufferNode.Data>(node).AliasResult = m_AliasResults.CreateLong();

                return node;
            }

            public NodeHandle<SpliceNode> CreateSpliceNode()
            {
                var node = Set.Create<SpliceNode>();
                Aggregate ag;

                ag.SubBuffer1 = Buffer<BufferElement>.SizeRequest(BufferSize);
                ag.SubBuffer2 = Buffer<BufferElement>.SizeRequest(BufferSize);

                Set.SetBufferSize(node, SpliceNode.KernelPorts.AggrSum, ag);
                Set.SetBufferSize(node, SpliceNode.KernelPorts.ScalarSum, Buffer<BufferElement>.SizeRequest(BufferSize));

                Set.GetKernelData<SpliceNode.Data>(node).AliasResult = m_AliasResults.CreateLong();

                return node;
            }

            public NodeHandle<StartPoint> CreateStart(int aggregateValue, int scalarValue)
            {
                var node = Set.Create<StartPoint>();
                Aggregate ag;

                ag.SubBuffer1 = Buffer<BufferElement>.SizeRequest(BufferSize);
                ag.SubBuffer2 = Buffer<BufferElement>.SizeRequest(BufferSize);

                Set.SetBufferSize(node, StartPoint.KernelPorts.AggregateOutput, ag);
                Set.SetBufferSize(node, StartPoint.KernelPorts.ScalarOutput, Buffer<BufferElement>.SizeRequest(BufferSize));

                Set.GetKernelData<StartPoint.KernelData>(node).AggregateFill = aggregateValue;
                Set.GetKernelData<StartPoint.KernelData>(node).ScalarFill = scalarValue;

                return node;
            }

            public long GetAliasResult(NodeHandle<BufferNode> n)
            {
                Set.DataGraph.SyncAnyRendering();
                var kernelData = Set.GetKernelData<BufferNode.Data>(n);
                return *kernelData.AliasResult;
            }

        }


        [Test]
        public void CanCreate_BufferAliasNode_AndRun_WithoutAliasing([Values] NodeSet.RenderExecutionModel model)
        {
            using (var fix = new Fixture(model))
            {
                var node = fix.CreateNode();
                fix.Set.Update();

                Assert.Zero(fix.GetAliasResult(node));

                fix.Set.Destroy(node);
            }
        }

        [Test]
        public void SingleChainOfNodes_ComputesCorrectly_AndExhibits_NoAliasing(
            [Values] NodeSet.RenderExecutionModel model,
            [Values] NodeSet.RenderOptimizations optimizations)
        {
            /*
             * o -> o -> o -> o -> o -> o -> o (...)
             */
            const int k_ChainLength = 8;
            const int k_Updates = 5;
            const int k_ScalarFill = 13;
            const int k_AggrFill = 7;

            using (var fix = new Fixture(model, optimizations))
            {
                var start = fix.CreateStart(k_ScalarFill, k_AggrFill);

                var nodes = new List<NodeHandle<BufferNode>>();

                var first = fix.CreateNode();

                fix.Set.Connect(start, StartPoint.KernelPorts.AggregateOutput, first, BufferNode.KernelPorts.InputArray, 1);
                fix.Set.Connect(start, StartPoint.KernelPorts.ScalarOutput, first, BufferNode.KernelPorts.Input);

                nodes.Add(first);

                Assert.NotZero(k_ChainLength);

                for (int i = 1; i < k_ChainLength; ++i)
                {
                    var current = fix.CreateNode();

                    fix.Set.Connect(nodes[i - 1], BufferNode.KernelPorts.InputSumAsAggr, current, BufferNode.KernelPorts.InputArray, 1);
                    fix.Set.Connect(nodes[i - 1], BufferNode.KernelPorts.PortArrayElementSumsAsAggr, 1, current, BufferNode.KernelPorts.InputArray, 2);
                    fix.Set.Connect(nodes[i - 1], BufferNode.KernelPorts.PortArraySum, current, BufferNode.KernelPorts.Input);

                    nodes.Add(current);
                }

                var gvAggrSum = fix.Set.CreateGraphValue(nodes.Last(), BufferNode.KernelPorts.InputSumAsAggr);
                var gvPortSum = fix.Set.CreateGraphValue(nodes.Last(), BufferNode.KernelPorts.PortArraySum);
                var gvPortElemSum = fix.Set.CreateGraphValueArray(nodes.Last(), BufferNode.KernelPorts.PortArrayElementSumsAsScalar);
                var gvPortElemAggrSum = fix.Set.CreateGraphValueArray(nodes.Last(), BufferNode.KernelPorts.PortArrayElementSumsAsAggr);

                for(int n = 0; n < k_Updates; ++n)
                {

                    fix.Set.Update();

                    var c = fix.Set.GetGraphValueResolver(out var jobHandle);

                    jobHandle.Complete();

                    var sub1 = c.Resolve(gvAggrSum).SubBuffer1.ToNative(c).Reinterpret<long>().ToArray();
                    var sub2 = c.Resolve(gvAggrSum).SubBuffer2.ToNative(c).Reinterpret<long>().ToArray();
                    var portSum = c.Resolve(gvPortSum).Reinterpret<long>().ToArray();
                    var portElemSub1 = c.Resolve(gvPortElemAggrSum)[1].SubBuffer1.ToNative(c).Reinterpret<long>().ToArray();
                    var portElemSub2 = c.Resolve(gvPortElemAggrSum)[1].SubBuffer2.ToNative(c).Reinterpret<long>().ToArray();
                    var portElemSum = c.Resolve(gvPortElemSum)[1].ToNative(c).Reinterpret<long>().ToArray();

                    for (int j = 0; j < Fixture.BufferSize; ++j)
                    {
                        Assert.AreEqual(80247591, sub1[j]);
                        Assert.AreEqual(80247591 * 2, sub2[j]);
                        Assert.AreEqual(182284263, portSum[j]);
                        Assert.AreEqual(77058945, portElemSub1[j]);
                        Assert.AreEqual(77058945 * 2, portElemSub2[j]);
                        Assert.AreEqual(77058945, portElemSum[j]);
                    }

                    for (int i = 1; i < k_ChainLength; ++i)
                    {
                        Assert.Zero(fix.GetAliasResult(nodes[i]));
                    }

                    // (trigger topology change)
                    fix.Set.Disconnect(start, StartPoint.KernelPorts.ScalarOutput, first, BufferNode.KernelPorts.Input);
                    fix.Set.Connect(start, StartPoint.KernelPorts.ScalarOutput, first, BufferNode.KernelPorts.Input);
                }

                fix.Set.Destroy(start);

                nodes.ForEach(n => fix.Set.Destroy(n));
                fix.Set.ReleaseGraphValue(gvAggrSum);
                fix.Set.ReleaseGraphValue(gvPortSum);
                fix.Set.ReleaseGraphValueArray(gvPortElemSum);
                fix.Set.ReleaseGraphValueArray(gvPortElemAggrSum);
            }
        }

        [Test]
        public void SimpleDag_ComputesCorrectly_AndExhibits_NoAliasing(
            [Values] NodeSet.RenderExecutionModel model,
            [Values] NodeSet.RenderOptimizations optimizations)
        {
            /*
             * o -> o -> o -> o
             *       \       /
             *         o -> o
             */
            const int k_ScalarFill = 13;
            const int k_AggrFill = 7;
            const int k_ChainLength = 3;
            const int k_Updates = 5;

            using (var fix = new Fixture(model, optimizations))
            {
                var start = fix.CreateStart(k_ScalarFill, k_AggrFill);

                var nodes = new List<NodeHandle<BufferNode>>();

                var first = fix.CreateNode();
                NodeHandle<BufferNode> last = default;

                fix.Set.Connect(start, StartPoint.KernelPorts.AggregateOutput, first, BufferNode.KernelPorts.InputArray, 1);
                fix.Set.Connect(start, StartPoint.KernelPorts.ScalarOutput, first, BufferNode.KernelPorts.Input);

                nodes.Add(first);

                Assert.NotZero(k_ChainLength);

                for (int i = 1; i < k_ChainLength; ++i)
                {
                    last = fix.CreateNode();

                    fix.Set.Connect(nodes[i - 1], BufferNode.KernelPorts.InputSumAsAggr, last, BufferNode.KernelPorts.InputArray, 1);
                    fix.Set.Connect(nodes[i - 1], BufferNode.KernelPorts.PortArrayElementSumsAsAggr, 1, last, BufferNode.KernelPorts.InputArray, 2);
                    fix.Set.Connect(nodes[i - 1], BufferNode.KernelPorts.PortArraySum, last, BufferNode.KernelPorts.Input);

                    nodes.Add(last);
                }

                // insert fork and join
                var fork = fix.CreateNode();

                fix.Set.Connect(nodes[nodes.Count - k_ChainLength], BufferNode.KernelPorts.InputSumAsAggr, fork, BufferNode.KernelPorts.InputArray, 1);
                fix.Set.Connect(nodes[nodes.Count - k_ChainLength], BufferNode.KernelPorts.PortArrayElementSumsAsAggr, 1, fork, BufferNode.KernelPorts.InputArray, 2);
                fix.Set.Connect(nodes[nodes.Count - k_ChainLength], BufferNode.KernelPorts.PortArraySum, fork, BufferNode.KernelPorts.Input);

                nodes.Add(fork);

                var join = fix.CreateNode();

                // Anomaly: only use one output buffer, use two input port array contrary to rest
                fix.Set.Connect(fork, BufferNode.KernelPorts.InputSumAsAggr, join, BufferNode.KernelPorts.InputArray, 1);
                fix.Set.Connect(fork, BufferNode.KernelPorts.PortArrayElementSumsAsAggr, 1, join, BufferNode.KernelPorts.InputArray, 2);
                fix.Set.Connect(fork, BufferNode.KernelPorts.PortArrayElementSumsAsScalar, 2, join, BufferNode.KernelPorts.Input);

                fix.Set.Connect(join, BufferNode.KernelPorts.PortArrayElementSumsAsAggr, 1, last, BufferNode.KernelPorts.InputArray, 0);

                nodes.Add(join);

                var gvAggrSum = fix.Set.CreateGraphValue(last, BufferNode.KernelPorts.InputSumAsAggr);
                var gvPortSum = fix.Set.CreateGraphValue(last, BufferNode.KernelPorts.PortArraySum);
                var gvPortElemSum = fix.Set.CreateGraphValueArray(last, BufferNode.KernelPorts.PortArrayElementSumsAsScalar);
                var gvPortElemAggrSum = fix.Set.CreateGraphValueArray(last, BufferNode.KernelPorts.PortArrayElementSumsAsAggr);

                for(int n = 0; n < k_Updates; ++n)
                {
                    fix.Set.Update();

                    var c = fix.Set.GetGraphValueResolver(out var jobHandle);

                    jobHandle.Complete();

                    var sub1 = c.Resolve(gvAggrSum).SubBuffer1.ToNative(c).Reinterpret<long>().ToArray();
                    var sub2 = c.Resolve(gvAggrSum).SubBuffer2.ToNative(c).Reinterpret<long>().ToArray();
                    var portSum = c.Resolve(gvPortSum).Reinterpret<long>().ToArray();
                    var portElemSub1 = c.Resolve(gvPortElemAggrSum)[1].SubBuffer1.ToNative(c).Reinterpret<long>().ToArray();
                    var portElemSub2 = c.Resolve(gvPortElemAggrSum)[1].SubBuffer2.ToNative(c).Reinterpret<long>().ToArray();
                    var portElemSum = c.Resolve(gvPortElemSum)[1].ToNative(c).Reinterpret<long>().ToArray();

                    for (int j = 0; j < Fixture.BufferSize; ++j)
                    {
                        Assert.AreEqual(3726, sub1[j]);
                        Assert.AreEqual(3726 * 2, sub2[j]);
                        Assert.AreEqual(33291, portSum[j]);
                        Assert.AreEqual(3159, portElemSub1[j]);
                        Assert.AreEqual(3159 * 2, portElemSub2[j]);
                        Assert.AreEqual(3159, portElemSum[j]);
                    }

                    for (int i = 1; i < k_ChainLength; ++i)
                    {
                        Assert.Zero(fix.GetAliasResult(nodes[i]));
                    }

                    // (trigger topology change)
                    fix.Set.Disconnect(start, StartPoint.KernelPorts.ScalarOutput, first, BufferNode.KernelPorts.Input);
                    fix.Set.Connect(start, StartPoint.KernelPorts.ScalarOutput, first, BufferNode.KernelPorts.Input);
                }

                fix.Set.Destroy(start);

                nodes.ForEach(n => fix.Set.Destroy(n));
                fix.Set.ReleaseGraphValue(gvAggrSum);
                fix.Set.ReleaseGraphValue(gvPortSum);
                fix.Set.ReleaseGraphValueArray(gvPortElemSum);
                fix.Set.ReleaseGraphValueArray(gvPortElemAggrSum);
            }
        }

        [Test]
        public void StableFeedback_CyclicGraph_ComputesCorrectly_AndExhibits_NoAliasing(
            [Values] NodeSet.RenderExecutionModel model,
            [Values] NodeSet.RenderOptimizations optimizations)
        {
            /*         <----
             *       /       \
             * o -> o -> o -> o -> o
             *       \    \  /
             *         o -> o
             */
            const int k_ScalarFill = 13;
            const int k_AggrFill = 7;
            const int k_ChainLength = 4;
            const int k_Updates = 5;

            using (var fix = new Fixture(model, optimizations))
            {
                var start = fix.CreateStart(k_ScalarFill, k_AggrFill);

                var nodes = new List<NodeHandle<BufferNode>>();

                var first = fix.CreateNode();
                NodeHandle<BufferNode> last = default;

                fix.Set.Connect(start, StartPoint.KernelPorts.AggregateOutput, first, BufferNode.KernelPorts.InputArray, 1);
                fix.Set.Connect(start, StartPoint.KernelPorts.ScalarOutput, first, BufferNode.KernelPorts.Input);

                nodes.Add(first);

                Assert.NotZero(k_ChainLength);

                for (int i = 1; i < k_ChainLength; ++i)
                {
                    last = fix.CreateNode();

                    fix.Set.Connect(nodes[i - 1], BufferNode.KernelPorts.InputSumAsAggr, last, BufferNode.KernelPorts.InputArray, 1);
                    fix.Set.Connect(nodes[i - 1], BufferNode.KernelPorts.PortArrayElementSumsAsAggr, 1, last, BufferNode.KernelPorts.InputArray, 2);
                    fix.Set.Connect(nodes[i - 1], BufferNode.KernelPorts.PortArraySum, last, BufferNode.KernelPorts.Input);

                    nodes.Add(last);
                }

                // insert fork and join
                var fork = fix.CreateNode();

                fix.Set.Connect(nodes[nodes.Count - k_ChainLength], BufferNode.KernelPorts.InputSumAsAggr, fork, BufferNode.KernelPorts.InputArray, 1);
                fix.Set.Connect(nodes[nodes.Count - k_ChainLength], BufferNode.KernelPorts.PortArrayElementSumsAsAggr, 1, fork, BufferNode.KernelPorts.InputArray, 2);
                fix.Set.Connect(nodes[nodes.Count - k_ChainLength], BufferNode.KernelPorts.PortArraySum, fork, BufferNode.KernelPorts.Input);

                var join = fix.CreateNode();

                // Anomaly: only use one output buffer, use two input port array contrary to rest
                fix.Set.Connect(fork, BufferNode.KernelPorts.InputSumAsAggr, join, BufferNode.KernelPorts.InputArray, 1);
                fix.Set.Connect(fork, BufferNode.KernelPorts.PortArrayElementSumsAsAggr, 1, join, BufferNode.KernelPorts.InputArray, 2);
                fix.Set.Connect(fork, BufferNode.KernelPorts.PortArrayElementSumsAsScalar, 2, join, BufferNode.KernelPorts.Input);

                fix.Set.Connect(join, BufferNode.KernelPorts.PortArrayElementSumsAsAggr, 1, nodes[nodes.Count - 1], BufferNode.KernelPorts.InputArray, 0);

                // Make cyclic connection from join to middle of chain
                fix.Set.Connect(join, BufferNode.KernelPorts.InputSumAsAggr, nodes[nodes.Count - 3], BufferNode.KernelPorts.InputArray, 0, NodeSet.ConnectionType.Feedback);

                // Make cyclic connection from midpoints of chain
                fix.Set.Connect(nodes[nodes.Count - 2], BufferNode.KernelPorts.PortArrayElementSumsAsAggr, 2, first, BufferNode.KernelPorts.InputArray, 0, NodeSet.ConnectionType.Feedback);

                nodes.Add(fork);
                nodes.Add(join);

                var gvAggrSum = fix.Set.CreateGraphValue(last, BufferNode.KernelPorts.InputSumAsAggr);
                var gvPortSum = fix.Set.CreateGraphValue(last, BufferNode.KernelPorts.PortArraySum);
                var gvPortElemSum = fix.Set.CreateGraphValueArray(last, BufferNode.KernelPorts.PortArrayElementSumsAsScalar);
                var gvPortElemAggrSum = fix.Set.CreateGraphValueArray(last, BufferNode.KernelPorts.PortArrayElementSumsAsAggr);

                for (int n = 0; n < k_Updates; ++n)
                {
                    fix.Set.Update();

                    var c = fix.Set.GetGraphValueResolver(out var jobHandle);

                    jobHandle.Complete();

                    var sub1 = c.Resolve(gvAggrSum).SubBuffer1.ToNative(c).Reinterpret<long>().ToArray();
                    var sub2 = c.Resolve(gvAggrSum).SubBuffer2.ToNative(c).Reinterpret<long>().ToArray();
                    var portSum = c.Resolve(gvPortSum).Reinterpret<long>().ToArray();
                    var portElemSub1 = c.Resolve(gvPortElemAggrSum)[1].SubBuffer1.ToNative(c).Reinterpret<long>().ToArray();
                    var portElemSub2 = c.Resolve(gvPortElemAggrSum)[1].SubBuffer2.ToNative(c).Reinterpret<long>().ToArray();
                    var portElemSum = c.Resolve(gvPortElemSum)[1].ToNative(c).Reinterpret<long>().ToArray();

                    // Second loop - feedback stabilizes
                    var goldSub1 = n == 0 ? 14580 : 1254609;
                    var goldPortSum = n == 0 ? 90396 : 8298207;
                    var goldPortElemSub = n == 0 ? 33534 : 801171;

                    for (int j = 0; j < Fixture.BufferSize; ++j)
                    {
                        Assert.AreEqual(goldSub1, sub1[j]);
                        Assert.AreEqual(goldSub1 * 2, sub2[j]);
                        Assert.AreEqual(goldPortSum, portSum[j]);
                        Assert.AreEqual(goldPortElemSub, portElemSub1[j]);
                        Assert.AreEqual(goldPortElemSub * 2, portElemSub2[j]);
                        Assert.AreEqual(goldPortElemSub, portElemSum[j]);
                    }

                    for (int i = 1; i < k_ChainLength; ++i)
                    {
                        Assert.Zero(fix.GetAliasResult(nodes[i]));
                    }

                    // (trigger topology change)
                    fix.Set.Disconnect(start, StartPoint.KernelPorts.ScalarOutput, first, BufferNode.KernelPorts.Input);
                    fix.Set.Connect(start, StartPoint.KernelPorts.ScalarOutput, first, BufferNode.KernelPorts.Input);
                }

                fix.Set.Destroy(start);

                nodes.ForEach(n => fix.Set.Destroy(n));
                fix.Set.ReleaseGraphValue(gvAggrSum);
                fix.Set.ReleaseGraphValue(gvPortSum);
                fix.Set.ReleaseGraphValueArray(gvPortElemSum);
                fix.Set.ReleaseGraphValueArray(gvPortElemAggrSum);
            }
        }

        [Test]
        public void AccumulatingUnstableFeedback_CyclicGraph_WithComponentNodes_ComputesCorrectly_AndExhibits_NoAliasing(
            [Values] NodeSet.RenderExecutionModel model,
            [Values] NodeSet.RenderOptimizations optimizations)
        {
            /*     ------------------
             *   /     <----          \
             *  /    /       \         \
             * o -> o -> o -> o -> o -> E1
             *  \    \       /
             *   \     o -> o -> E2
             *     < ----------- /
             *
             */
            const int k_ChainLength = 3;
            const int k_Updates = 5;

            using (var w = new World("unstable feedback"))
            {
                var system = w.GetOrCreateSystem<UpdateSystem>();

                using (var fix = new Fixture(system, model, optimizations))
                {
                    var e1 = w.EntityManager.CreateEntity();
                    var e2 = w.EntityManager.CreateEntity();

                    w.EntityManager.AddBuffer<BufferElement>(e1);
                    w.EntityManager.AddBuffer<BufferElement>(e2);

                    var ce1 = fix.Set.CreateComponentNode(e1);
                    var ce2 = fix.Set.CreateComponentNode(e2);

                    var nodes = new List<NodeHandle<BufferNode>>();

                    var start = fix.CreateSpliceNode();
                    var first = fix.CreateNode();

                    // Connect splice to start of chain
                    fix.Set.Connect(start, SpliceNode.KernelPorts.AggrSum, first, BufferNode.KernelPorts.InputArray, 1);
                    fix.Set.Connect(start, SpliceNode.KernelPorts.ScalarSum, first, BufferNode.KernelPorts.Input);

                    NodeHandle<BufferNode> last = default;

                    nodes.Add(first);

                    Assert.NotZero(k_ChainLength);

                    for (int i = 1; i < k_ChainLength; ++i)
                    {
                        last = fix.CreateNode();

                        fix.Set.Connect(nodes[i - 1], BufferNode.KernelPorts.InputSumAsAggr, last, BufferNode.KernelPorts.InputArray, 1);
                        fix.Set.Connect(nodes[i - 1], BufferNode.KernelPorts.PortArrayElementSumsAsAggr, 1, last, BufferNode.KernelPorts.InputArray, 2);
                        fix.Set.Connect(nodes[i - 1], BufferNode.KernelPorts.PortArraySum, last, BufferNode.KernelPorts.Input);

                        nodes.Add(last);
                    }

                    // insert fork and join
                    var fork = fix.CreateNode();

                    fix.Set.Connect(nodes[0], BufferNode.KernelPorts.InputSumAsAggr, fork, BufferNode.KernelPorts.InputArray, 1);
                    fix.Set.Connect(nodes[0], BufferNode.KernelPorts.PortArrayElementSumsAsAggr, 1, fork, BufferNode.KernelPorts.InputArray, 2);
                    fix.Set.Connect(nodes[0], BufferNode.KernelPorts.PortArraySum, fork, BufferNode.KernelPorts.Input);

                    var join = fix.CreateNode();

                    // Anomaly: only use one output buffer, use two input port array contrary to rest
                    fix.Set.Connect(fork, BufferNode.KernelPorts.InputSumAsAggr, join, BufferNode.KernelPorts.InputArray, 1);
                    fix.Set.Connect(fork, BufferNode.KernelPorts.PortArrayElementSumsAsAggr, 1, join, BufferNode.KernelPorts.InputArray, 2);
                    fix.Set.Connect(fork, BufferNode.KernelPorts.PortArrayElementSumsAsScalar, 2, join, BufferNode.KernelPorts.Input);

                    fix.Set.Connect(join, BufferNode.KernelPorts.PortArrayElementSumsAsAggr, 1, nodes[nodes.Count - 2], BufferNode.KernelPorts.InputArray, 0);

                    // Make cyclic connection from midpoints of chain
                    fix.Set.Connect(nodes[nodes.Count - 2], BufferNode.KernelPorts.InputSumAsAggr, nodes[0], BufferNode.KernelPorts.InputArray, 0, NodeSet.ConnectionType.Feedback);
                    fix.Set.Connect(nodes[nodes.Count - 2], BufferNode.KernelPorts.PortArrayElementSumsAsAggr, 1, nodes[0], BufferNode.KernelPorts.InputArray, 2, NodeSet.ConnectionType.Feedback);

                    nodes.Add(fork);
                    nodes.Add(join);

                    // from fork to E2, cyclic back to start
                    fix.Set.Connect(fork, BufferNode.KernelPorts.PortArrayElementSumsAsScalar, 1, ce2, ComponentNode.Input<BufferElement>());
                    fix.Set.Connect(ce2, ComponentNode.Output<BufferElement>(), start, SpliceNode.KernelPorts.Input2, NodeSet.ConnectionType.Feedback);

                    // from chain end to e1, cyclic back to start
                    fix.Set.Connect(nodes[nodes.Count - 1], BufferNode.KernelPorts.PortArraySum, ce1, ComponentNode.Input<BufferElement>());
                    fix.Set.Connect(ce1, ComponentNode.Output<BufferElement>(), start, SpliceNode.KernelPorts.Input, NodeSet.ConnectionType.Feedback);

                    // Seed feedback
                    var sub1Buffer = w.EntityManager.GetBuffer<BufferElement>(e1);
                    var portSumBuffer = w.EntityManager.GetBuffer<BufferElement>(e2);

                    sub1Buffer.ResizeUninitialized(Fixture.BufferSize);
                    portSumBuffer.ResizeUninitialized(Fixture.BufferSize);

                    for(int i = 0; i < Fixture.BufferSize; ++i)
                    {
                        // small primes...
                        sub1Buffer[i] = 7;
                        portSumBuffer[i] = 3;
                    }

                    var gold = new[]
                    {
                        (7290, 243),
                        (5688387, 19683),
                        (4314769479, 1594323),
                        (3263127987591, 129140163),
                        (2467018901797623, 10460353203)
                    };

                    for (int n = 0; n < k_Updates; ++n)
                    {
                        system.Update();

                        var sub1 = w.EntityManager.GetBuffer<BufferElement>(e1).AsNativeArray().Reinterpret<long>().ToArray();
                        var portSum = w.EntityManager.GetBuffer<BufferElement>(e2).AsNativeArray().Reinterpret<long>().ToArray();

                        for (int j = 0; j < Fixture.BufferSize; ++j)
                        {
                            Assert.AreEqual(gold[n].Item1, sub1[j]);
                            Assert.AreEqual(gold[n].Item2, portSum[j]);
                        }

                        for (int i = 1; i < k_ChainLength; ++i)
                        {
                            Assert.Zero(fix.GetAliasResult(nodes[i]), $"update {n}");
                        }

                        // (trigger topology change)
                        fix.Set.Disconnect(start, SpliceNode.KernelPorts.ScalarSum, first, BufferNode.KernelPorts.Input);
                        fix.Set.Connect(start, SpliceNode.KernelPorts.ScalarSum, first, BufferNode.KernelPorts.Input);
                    }

                    fix.Set.Destroy(start, ce1, ce2);

                    nodes.ForEach(n => fix.Set.Destroy(n));
                }
            }
        }
    }
}
