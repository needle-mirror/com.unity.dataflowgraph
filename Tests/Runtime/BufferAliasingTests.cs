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

        public struct Node : INodeData
        {
            public int Contents;
        }

        public unsafe struct Data : IKernelData
        {
            public long* AliasResult;
        }

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


        public unsafe class BufferNode : NodeDefinition<Node, Data, BufferNode.KernelDefs, BufferNode.Kernel>
        {
            public struct KernelDefs : IKernelPortDefinition
            {
                public DataInput<BufferNode, Buffer<BufferElement>> Input;
                public PortArray<DataInput<BufferNode, Aggregate>> InputArray;
                public DataOutput<BufferNode, Aggregate> InputSumAsAggr;
                public DataOutput<BufferNode, Buffer<BufferElement>> InputSumAsScalar;
                public DataOutput<BufferNode, Buffer<BufferElement>> PortArraySum;

                public long CheckIOAliasing(RenderContext c)
                {
                    if (InputSumAsAggr.m_Value.SubBuffer1.Ptr == PortArraySum.m_Value.Ptr)
                        return 1;

                    if (InputSumAsAggr.m_Value.SubBuffer2.Ptr == PortArraySum.m_Value.Ptr)
                        return 2;

                    if (InputSumAsAggr.m_Value.SubBuffer2.Ptr == InputSumAsAggr.m_Value.SubBuffer1.Ptr)
                        return 3;


                    if (InputSumAsScalar.m_Value.Ptr == InputSumAsAggr.m_Value.SubBuffer1.Ptr)
                        return 4;

                    if (InputSumAsScalar.m_Value.Ptr == InputSumAsAggr.m_Value.SubBuffer2.Ptr)
                        return 5;

                    if (InputSumAsScalar.m_Value.Ptr == PortArraySum.m_Value.Ptr)
                        return 6;

                    return
                        CheckOutputNoAlias(c.Resolve(ref InputSumAsAggr).SubBuffer1.ToNative(c), c) * 8 +
                        CheckOutputNoAlias(c.Resolve(ref InputSumAsAggr).SubBuffer2.ToNative(c), c) * 32 +
                        CheckOutputNoAlias(c.Resolve(ref PortArraySum), c) * 128 +
                        CheckOutputNoAlias(c.Resolve(ref InputSumAsScalar), c) * 512;
                }

                long CheckOutputNoAlias(NativeArray<BufferElement> output, RenderContext c)
                {
                    if (DoesOutputAliasInput(output, Input, c))
                        return 1;

                    var ports = c.Resolve(InputArray);

                    for(int i = 0; i < ports.Length; ++i)
                    {
                        if (DoesOutputAliasInput(output, ports[i], c))
                            return 1 + i;
                    }

                    return 0;
                }

                bool DoesOutputAliasInput(NativeArray<BufferElement> output, DataInput<BufferNode, Buffer<BufferElement>> input, RenderContext c)
                {
                    if (output.GetUnsafePtr() == input.Ptr)
                        return true;

                    return DoesOutputAliasInput(output, c.Resolve(input));
                }

                bool DoesOutputAliasInput(NativeArray<BufferElement> output, NativeArray<BufferElement> inputResolved)
                {
                    if (output.GetUnsafePtr() == inputResolved.GetUnsafeReadOnlyPtr())
                        return true;

                    // TODO: add range overlap

                    return false;
                }

                bool DoesOutputAliasInput(NativeArray<BufferElement> output, Aggregate input, RenderContext c)
                {
                    return DoesOutputAliasInput(output, input.SubBuffer1.ToNative(c)) || DoesOutputAliasInput(output, input.SubBuffer2.ToNative(c));
                }
            }

            [BurstCompile(CompileSynchronously = true)]
            public struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                public void Execute(RenderContext ctx, Data data, ref KernelDefs ports)
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

                    for(int p = 0; p < portArray.Length; ++p)
                    {
                        buffer = portArray[p].SubBuffer1.ToNative(ctx);
                        for (int i = 0; i < buffer.Length; ++i)
                            sum += buffer[i];

                        buffer = portArray[p].SubBuffer2.ToNative(ctx);
                        for (int i = 0; i < buffer.Length; ++i)
                            sum += buffer[i];
                    }

                    buffer = ctx.Resolve(ref ports.PortArraySum);
                    for (int i = 0; i < buffer.Length; ++i)
                        buffer[i] = sum;
                }
            }
        }

        public unsafe class SpliceNode : NodeDefinition<Node, Data, SpliceNode.KernelDefs, SpliceNode.Kernel>
        {
            public struct KernelDefs : IKernelPortDefinition
            {
                public DataInput<SpliceNode, Buffer<BufferElement>> Input;
                public DataInput<SpliceNode, Buffer<BufferElement>> Input2;
                public DataOutput<SpliceNode, Aggregate> AggrSum;
                public DataOutput<SpliceNode, Buffer<BufferElement>> ScalarSum;
            }

            [BurstCompile(CompileSynchronously = true)]
            public struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                public void Execute(RenderContext ctx, Data data, ref KernelDefs ports)
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

        public unsafe class StartPoint : NodeDefinition<Node, StartPoint.KernelData, StartPoint.KernelDefs, StartPoint.Kernel>
        {
            public struct KernelData : IKernelData
            {
                public long AggregateFill, ScalarFill;
            }

            public struct KernelDefs : IKernelPortDefinition
            {
                public DataOutput<StartPoint, Aggregate> AggregateOutput;
                public DataOutput<StartPoint, Buffer<BufferElement>> ScalarOutput;
            }

            [BurstCompile(CompileSynchronously = true)]
            public struct Kernel : IGraphKernel<KernelData, KernelDefs>
            {
                public void Execute(RenderContext ctx, KernelData data, ref KernelDefs ports)
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

        unsafe class AliasResults : IDisposable
        {
            BlitList<IntPtr> Pointers = new BlitList<IntPtr>(0);

            public long* CreateLong()
            {
                var res = UnsafeUtility.Malloc(sizeof(long), 8, Allocator.Persistent);

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

            ~AliasResults() {
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
            AliasResults m_AliasResults = new AliasResults();

            public Fixture(NodeSet.RenderExecutionModel model)
            {
                Set = new NodeSet();
                Set.RendererModel = model;
            }
            public Fixture(UpdateSystem s, NodeSet.RenderExecutionModel model)
            {
                Set = new NodeSet(s);
                s.Set = Set;
                Set.RendererModel = model;
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

                Set.GetKernelData<Data>(node).AliasResult = m_AliasResults.CreateLong();

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

                Set.GetKernelData<Data>(node).AliasResult = m_AliasResults.CreateLong();

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
                return *Set.GetKernelData<Data>(n).AliasResult;
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
        public void SingleChainOfNodes_ComputesCorrectly_AndExhibits_NoAliasing([Values] NodeSet.RenderExecutionModel model)
        {
            /*
             * o -> o -> o -> o -> o -> o -> o (...)
             */
            const int k_ChainLength = 8;
            const int k_Updates = 5;
            const int k_ScalarFill = 13;
            const int k_AggrFill = 7;

            using (var fix = new Fixture(model))
            {
                var start = fix.CreateStart(k_ScalarFill, k_AggrFill);

                var nodes = new List<NodeHandle<BufferNode>>();

                var first = fix.CreateNode();

                fix.Set.Connect(start, StartPoint.KernelPorts.AggregateOutput, first, BufferNode.KernelPorts.InputArray, 0);
                fix.Set.Connect(start, StartPoint.KernelPorts.ScalarOutput, first, BufferNode.KernelPorts.Input);

                nodes.Add(first);

                Assert.NotZero(k_ChainLength);

                for (int i = 1; i < k_ChainLength; ++i)
                {
                    var current = fix.CreateNode();

                    fix.Set.Connect(nodes[i - 1], BufferNode.KernelPorts.InputSumAsAggr, current, BufferNode.KernelPorts.InputArray, 0);
                    fix.Set.Connect(nodes[i - 1], BufferNode.KernelPorts.PortArraySum, current, BufferNode.KernelPorts.Input);

                    nodes.Add(current);
                }

                var gvAggrSum = fix.Set.CreateGraphValue(nodes.Last(), BufferNode.KernelPorts.InputSumAsAggr);
                var gvPortSum = fix.Set.CreateGraphValue(nodes.Last(), BufferNode.KernelPorts.PortArraySum);

                for(int n = 0; n < k_Updates; ++n)
                {

                    fix.Set.Update();

                    var c = fix.Set.GetGraphValueResolver(out var jobHandle);

                    jobHandle.Complete();

                    var sub1 = c.Resolve(gvAggrSum).SubBuffer1.ToNative(c).Reinterpret<long>().ToArray();
                    var sub2 = c.Resolve(gvAggrSum).SubBuffer2.ToNative(c).Reinterpret<long>().ToArray();
                    var portSum = c.Resolve(gvPortSum).Reinterpret<long>().ToArray();

                    CollectionAssert.AreEqual(
                        Enumerable.Repeat(6908733, Fixture.BufferSize),
                        sub1
                    );

                    CollectionAssert.AreEqual(
                        Enumerable.Repeat(6908733 * 2, Fixture.BufferSize),
                        sub2
                    );

                    CollectionAssert.AreEqual(
                        Enumerable.Repeat(3720087, Fixture.BufferSize),
                        portSum
                    );

                    for (int i = 1; i < k_ChainLength; ++i)
                    {
                        Assert.Zero(fix.GetAliasResult(nodes[i]));
                    }

                    // (trigger topology change)
                    fix.Set.Connect(start, StartPoint.KernelPorts.AggregateOutput, first, BufferNode.KernelPorts.InputArray, 2);
                    fix.Set.Disconnect(start, StartPoint.KernelPorts.AggregateOutput, first, BufferNode.KernelPorts.InputArray, 2);

                }

                fix.Set.Destroy(start);

                nodes.ForEach(n => fix.Set.Destroy(n));
                fix.Set.ReleaseGraphValue(gvAggrSum);
                fix.Set.ReleaseGraphValue(gvPortSum);
            }
        }

        [Test]
        public void SimpleDag_ComputesCorrectly_AndExhibits_NoAliasing([Values] NodeSet.RenderExecutionModel model)
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

            using (var fix = new Fixture(model))
            {
                var start = fix.CreateStart(k_ScalarFill, k_AggrFill);

                var nodes = new List<NodeHandle<BufferNode>>();

                var first = fix.CreateNode();
                NodeHandle<BufferNode> last = default;

                fix.Set.Connect(start, StartPoint.KernelPorts.AggregateOutput, first, BufferNode.KernelPorts.InputArray, 0);
                fix.Set.Connect(start, StartPoint.KernelPorts.ScalarOutput, first, BufferNode.KernelPorts.Input);

                nodes.Add(first);

                Assert.NotZero(k_ChainLength);

                for (int i = 1; i < k_ChainLength; ++i)
                {
                    last = fix.CreateNode();

                    fix.Set.Connect(nodes[i - 1], BufferNode.KernelPorts.InputSumAsAggr, last, BufferNode.KernelPorts.InputArray, 0);
                    fix.Set.Connect(nodes[i - 1], BufferNode.KernelPorts.PortArraySum, last, BufferNode.KernelPorts.Input);

                    nodes.Add(last);
                }

                // insert fork and join
                var fork = fix.CreateNode();

                fix.Set.Connect(nodes[nodes.Count - k_ChainLength], BufferNode.KernelPorts.InputSumAsAggr, fork, BufferNode.KernelPorts.InputArray, 0);
                fix.Set.Connect(nodes[nodes.Count - k_ChainLength], BufferNode.KernelPorts.PortArraySum, fork, BufferNode.KernelPorts.Input);

                nodes.Add(fork);

                var join = fix.CreateNode();

                // Anomaly: only use one output buffer, use two input port array contrary to rest
                fix.Set.Connect(fork, BufferNode.KernelPorts.InputSumAsAggr, join, BufferNode.KernelPorts.InputArray, 0);
                fix.Set.Connect(fork, BufferNode.KernelPorts.InputSumAsAggr, join, BufferNode.KernelPorts.InputArray, 1);

                fix.Set.Connect(join, BufferNode.KernelPorts.InputSumAsAggr, last, BufferNode.KernelPorts.InputArray, 1);
                fix.Set.Connect(join, BufferNode.KernelPorts.InputSumAsAggr, last, BufferNode.KernelPorts.InputArray, 2);

                nodes.Add(join);

                var gvAggrSum = fix.Set.CreateGraphValue(last, BufferNode.KernelPorts.InputSumAsAggr);
                var gvPortSum = fix.Set.CreateGraphValue(last, BufferNode.KernelPorts.PortArraySum);

                for(int n = 0; n < k_Updates; ++n)
                {
                    fix.Set.Update();

                    var c = fix.Set.GetGraphValueResolver(out var jobHandle);

                    jobHandle.Complete();

                    var sub1 = c.Resolve(gvAggrSum).SubBuffer1.ToNative(c).Reinterpret<long>().ToArray();
                    var sub2 = c.Resolve(gvAggrSum).SubBuffer2.ToNative(c).Reinterpret<long>().ToArray();
                    var portSum = c.Resolve(gvPortSum).Reinterpret<long>().ToArray();

                    /*Debug.Log(string.Join(",", sub1));
                    Debug.Log(string.Join(",", sub2));
                    Debug.Log(string.Join(",", portSum)); */

                    CollectionAssert.AreEqual(
                        Enumerable.Repeat(567, Fixture.BufferSize),
                        sub1
                    );

                    CollectionAssert.AreEqual(
                        Enumerable.Repeat(567 * 2, Fixture.BufferSize),
                        sub2
                    );

                    CollectionAssert.AreEqual(
                        Enumerable.Repeat(3159, Fixture.BufferSize),
                        portSum
                    );

                    for (int i = 1; i < k_ChainLength; ++i)
                    {
                        Assert.Zero(fix.GetAliasResult(nodes[i]));
                    }

                    // (trigger topology change)
                    fix.Set.Connect(start, StartPoint.KernelPorts.AggregateOutput, first, BufferNode.KernelPorts.InputArray, 2);
                    fix.Set.Disconnect(start, StartPoint.KernelPorts.AggregateOutput, first, BufferNode.KernelPorts.InputArray, 2);
                }


                fix.Set.Destroy(start);

                nodes.ForEach(n => fix.Set.Destroy(n));
                fix.Set.ReleaseGraphValue(gvAggrSum);
                fix.Set.ReleaseGraphValue(gvPortSum);
            }
        }

        [Test]
        public void StableFeedback_CyclicGraph_ComputesCorrectly_AndExhibits_NoAliasing([Values] NodeSet.RenderExecutionModel model)
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

            using (var fix = new Fixture(model))
            {
                var start = fix.CreateStart(k_ScalarFill, k_AggrFill);

                var nodes = new List<NodeHandle<BufferNode>>();

                var first = fix.CreateNode();
                NodeHandle<BufferNode> last = default;

                fix.Set.Connect(start, StartPoint.KernelPorts.AggregateOutput, first, BufferNode.KernelPorts.InputArray, 0);
                fix.Set.Connect(start, StartPoint.KernelPorts.ScalarOutput, first, BufferNode.KernelPorts.Input);

                nodes.Add(first);

                Assert.NotZero(k_ChainLength);

                for (int i = 1; i < k_ChainLength; ++i)
                {
                    last = fix.CreateNode();

                    fix.Set.Connect(nodes[i - 1], BufferNode.KernelPorts.InputSumAsAggr, last, BufferNode.KernelPorts.InputArray, 0);
                    fix.Set.Connect(nodes[i - 1], BufferNode.KernelPorts.PortArraySum, last, BufferNode.KernelPorts.Input);

                    nodes.Add(last);
                }

                // insert fork and join
                var fork = fix.CreateNode();

                fix.Set.Connect(nodes[nodes.Count - k_ChainLength], BufferNode.KernelPorts.InputSumAsAggr, fork, BufferNode.KernelPorts.InputArray, 0);
                fix.Set.Connect(nodes[nodes.Count - k_ChainLength], BufferNode.KernelPorts.PortArraySum, fork, BufferNode.KernelPorts.Input);

                var join = fix.CreateNode();

                // Anomaly: only use one output buffer, use two input port array contrary to rest
                fix.Set.Connect(fork, BufferNode.KernelPorts.InputSumAsAggr, join, BufferNode.KernelPorts.InputArray, 0);
                fix.Set.Connect(fork, BufferNode.KernelPorts.InputSumAsAggr, join, BufferNode.KernelPorts.InputArray, 1);

                fix.Set.Connect(join, BufferNode.KernelPorts.InputSumAsAggr, nodes[nodes.Count - 1], BufferNode.KernelPorts.InputArray, 1);
                fix.Set.Connect(join, BufferNode.KernelPorts.InputSumAsAggr, nodes[nodes.Count - 1], BufferNode.KernelPorts.InputArray, 2);

                // Make cyclic connection from join to middle of chain
                fix.Set.Connect(join, BufferNode.KernelPorts.InputSumAsAggr, nodes[nodes.Count - 3], BufferNode.KernelPorts.InputArray, 1, NodeSet.ConnectionType.Feedback);
                fix.Set.Connect(join, BufferNode.KernelPorts.InputSumAsAggr, nodes[nodes.Count - 3], BufferNode.KernelPorts.InputArray, 2, NodeSet.ConnectionType.Feedback);

                // Make cyclic connection from midpoints of chain
                fix.Set.Connect(nodes[nodes.Count - 2], BufferNode.KernelPorts.InputSumAsAggr, first, BufferNode.KernelPorts.InputArray, 1, NodeSet.ConnectionType.Feedback);
                fix.Set.Connect(nodes[nodes.Count - 2], BufferNode.KernelPorts.InputSumAsAggr, first, BufferNode.KernelPorts.InputArray, 2, NodeSet.ConnectionType.Feedback);

                nodes.Add(fork);
                nodes.Add(join);

                var gvAggrSum = fix.Set.CreateGraphValue(last, BufferNode.KernelPorts.InputSumAsAggr);
                var gvPortSum = fix.Set.CreateGraphValue(last, BufferNode.KernelPorts.PortArraySum);

                for (int n = 0; n < k_Updates; ++n)
                {
                    fix.Set.Update();

                    var c = fix.Set.GetGraphValueResolver(out var jobHandle);

                    jobHandle.Complete();

                    var sub1 = c.Resolve(gvAggrSum).SubBuffer1.ToNative(c).Reinterpret<long>().ToArray();
                    var sub2 = c.Resolve(gvAggrSum).SubBuffer2.ToNative(c).Reinterpret<long>().ToArray();
                    var portSum = c.Resolve(gvPortSum).Reinterpret<long>().ToArray();

                    // Second loop - feedback stabilizes
                    var gold = n == 0 ? 9477 : 836163;

                    CollectionAssert.AreEqual(
                        Enumerable.Repeat(gold, Fixture.BufferSize),
                        sub1
                    );

                    CollectionAssert.AreEqual(
                        Enumerable.Repeat(gold * 2, Fixture.BufferSize),
                        sub2
                    );

                    CollectionAssert.AreEqual(
                        Enumerable.Repeat(5103, Fixture.BufferSize),
                        portSum
                    );

                    for (int i = 1; i < k_ChainLength; ++i)
                    {
                        Assert.Zero(fix.GetAliasResult(nodes[i]));
                    }

                    // (trigger topology change)
                    fix.Set.Disconnect(start, StartPoint.KernelPorts.AggregateOutput, first, BufferNode.KernelPorts.InputArray, 0);
                    fix.Set.Connect(start, StartPoint.KernelPorts.AggregateOutput, first, BufferNode.KernelPorts.InputArray, 0);
                }


                fix.Set.Destroy(start);

                nodes.ForEach(n => fix.Set.Destroy(n));
                fix.Set.ReleaseGraphValue(gvAggrSum);
                fix.Set.ReleaseGraphValue(gvPortSum);
            }
        }

        [Test]
        public void AccumulatingUnstableFeedback_CyclicGraph_WithComponentNodes_ComputesCorrectly_AndExhibits_NoAliasing([Values] NodeSet.RenderExecutionModel model)
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

                using (var fix = new Fixture(system, model))
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
                    fix.Set.Connect(start, SpliceNode.KernelPorts.AggrSum, first, BufferNode.KernelPorts.InputArray, 0);
                    fix.Set.Connect(start, SpliceNode.KernelPorts.ScalarSum, first, BufferNode.KernelPorts.Input);

                    NodeHandle<BufferNode> last = default;

                    nodes.Add(first);

                    Assert.NotZero(k_ChainLength);

                    for (int i = 1; i < k_ChainLength; ++i)
                    {
                        last = fix.CreateNode();

                        fix.Set.Connect(nodes[i - 1], BufferNode.KernelPorts.InputSumAsAggr, last, BufferNode.KernelPorts.InputArray, 0);
                        fix.Set.Connect(nodes[i - 1], BufferNode.KernelPorts.PortArraySum, last, BufferNode.KernelPorts.Input);

                        nodes.Add(last);
                    }

                    // insert fork and join
                    var fork = fix.CreateNode();

                    fix.Set.Connect(nodes[0], BufferNode.KernelPorts.InputSumAsAggr, fork, BufferNode.KernelPorts.InputArray, 0);
                    fix.Set.Connect(nodes[0], BufferNode.KernelPorts.PortArraySum, fork, BufferNode.KernelPorts.Input);

                    var join = fix.CreateNode();

                    // Anomaly: only use one output buffer, use two input port array contrary to rest
                    fix.Set.Connect(fork, BufferNode.KernelPorts.InputSumAsAggr, join, BufferNode.KernelPorts.InputArray, 0);
                    fix.Set.Connect(fork, BufferNode.KernelPorts.InputSumAsAggr, join, BufferNode.KernelPorts.InputArray, 1);

                    fix.Set.Connect(join, BufferNode.KernelPorts.InputSumAsAggr, nodes[nodes.Count - 2], BufferNode.KernelPorts.InputArray, 1);
                    fix.Set.Connect(join, BufferNode.KernelPorts.InputSumAsAggr, nodes[nodes.Count - 2], BufferNode.KernelPorts.InputArray, 2);

                    // Make cyclic connection from midpoints of chain - TODO really cyclic?
                    fix.Set.Connect(nodes[nodes.Count - 2], BufferNode.KernelPorts.InputSumAsAggr, nodes[0], BufferNode.KernelPorts.InputArray, 1, NodeSet.ConnectionType.Feedback);
                    fix.Set.Connect(nodes[nodes.Count - 2], BufferNode.KernelPorts.InputSumAsAggr, nodes[0], BufferNode.KernelPorts.InputArray, 2, NodeSet.ConnectionType.Feedback);

                    nodes.Add(fork);
                    nodes.Add(join);

                    // from fork to E2, cyclic back to start
                    fix.Set.Connect(fork, BufferNode.KernelPorts.InputSumAsScalar, ce2, ComponentNode.Input<BufferElement>());
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
                        (10206, 567),
                        (15431472, 857304),
                        (23332385664, 1296243648),
                        (35278567123968, 1959920395776),
                        (53341193491439616, 2963399638413312)
                    };

                    for (int n = 0; n < k_Updates; ++n)
                    {
                        system.Update();

                        var sub1 = w.EntityManager.GetBuffer<BufferElement>(e1).AsNativeArray().Reinterpret<long>().ToArray();
                        var portSum = w.EntityManager.GetBuffer<BufferElement>(e2).AsNativeArray().Reinterpret<long>().ToArray();

                        //Debug.Log(string.Join(",", sub1));
                        //Debug.Log(string.Join(",", portSum));

                        CollectionAssert.AreEqual(
                            Enumerable.Repeat(gold[n].Item1, Fixture.BufferSize),
                            sub1
                        );
                        CollectionAssert.AreEqual(
                            Enumerable.Repeat(gold[n].Item2, Fixture.BufferSize),
                            portSum
                        );

                        for (int i = 1; i < k_ChainLength; ++i)
                        {
                            Assert.Zero(fix.GetAliasResult(nodes[i]));
                        }

                        // (trigger topology change)
                        fix.Set.Disconnect(fork, BufferNode.KernelPorts.InputSumAsAggr, join, BufferNode.KernelPorts.InputArray, 0);
                        fix.Set.Connect(fork, BufferNode.KernelPorts.InputSumAsAggr, join, BufferNode.KernelPorts.InputArray, 0);
                    }

                    fix.Set.Destroy(start, ce1, ce2);

                    nodes.ForEach(n => fix.Set.Destroy(n));
                }
            }

        }
    }
}
