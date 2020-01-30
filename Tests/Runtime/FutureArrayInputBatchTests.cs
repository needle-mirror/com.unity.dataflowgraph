using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine.TestTools;

namespace Unity.DataFlowGraph.Tests
{
    public partial class InputBatchTests
    {
        public struct Data : IKernelData
        {
            public int Contents;
        }


        [Test]
        public void CanCreateFutureBatch()
        {
            var batch = new FutureArrayInputBatch(10, Allocator.Persistent);
            batch.Dispose();
        }

        [Test]
        public void CanSubmitItemsToFutureBatch()
        {
            using (var array = new NativeArray<int>(10, Allocator.Temp))
            using (var batch = new FutureArrayInputBatch(10, Allocator.Persistent))
            {
                batch.SetTransientBuffer(new InputPair(), array);
            }
        }

        [Test]
        public void CanSubmitBatch_ToNodeSet_Deferred()
        {
            using (var array = new NativeArray<int>(10, Allocator.Temp))
            using (var batch = new FutureArrayInputBatch(10, Allocator.Persistent))
            using (var set = new NodeSet())
            {
                batch.SetTransientBuffer(new InputPair(), array);
                var batchHandle = set.SubmitDeferredInputBatch(new JobHandle(), batch);
            }
        }

        public class InputBufferNode : NodeDefinition<PortAPITests.Node, PortAPITests.Data, InputBufferNode.KernelDefs, InputBufferNode.Kernel>
        {
            public struct KernelDefs : IKernelPortDefinition
            {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                public DataInput<InputBufferNode, Buffer<int>> InputBuffer;
                public DataOutput<InputBufferNode, long> OutSum;
                public DataInput<InputBufferNode, Buffer<int>> InputBuffer2;
                public DataOutput<InputBufferNode, long> OutSum2;
                public PortArray<DataInput<InputBufferNode, Buffer<int>>> ArrayOfInputBuffer;
                public DataOutput<InputBufferNode, long> OutArraySum;
#pragma warning restore 649  // Assigned through internal DataFlowGraph reflection

            }

            [BurstCompile(CompileSynchronously = true)]
            public struct Kernel : IGraphKernel<PortAPITests.Data, KernelDefs>
            {
                public void Execute(RenderContext context, PortAPITests.Data data, ref KernelDefs ports)
                {
                    var na = context.Resolve(ports.InputBuffer);
                    ref var sum = ref context.Resolve(ref ports.OutSum);

                    sum = 0;

                    for (int i = 0; i < na.Length; ++i)
                    {
                        sum += na[i];
                    }

                    var na2 = context.Resolve(ports.InputBuffer2);
                    ref var sum2 = ref context.Resolve(ref ports.OutSum2);

                    sum2 = 0;

                    for (int i = 0; i < na2.Length; ++i)
                    {
                        sum2 += na2[i];
                    }

                    ref var sum3 = ref context.Resolve(ref ports.OutArraySum);
                    sum3 = 0;
                    var portArray = context.Resolve(ports.ArrayOfInputBuffer);
                    for (int i = 0; i < portArray.Length; ++i)
                    {
                        var na3 = context.Resolve(portArray[i]);
                        for (int j = 0; j < na3.Length; ++j)
                        {
                            sum3 += na3[j];
                        }
                    }
                }
            }

        }

        [Test]
        public void CanReadAndSum_SingleInputBuffer_ProvidedFrom_InputBatch()
        {
            const int k_SequenceLength = 10;

            using (var array = new NativeArray<int>(k_SequenceLength, Allocator.Temp))
            using (var batch = new FutureArrayInputBatch(10, Allocator.Persistent))
            using (var set = new NodeSet())
            {
                var node = set.Create<InputBufferNode>();
                var sequence = Enumerable.Range(0, k_SequenceLength);

                array.CopyFrom(sequence.ToArray());

                batch.SetTransientBuffer(new InputPair(set, node, new InputPortArrayID(InputBufferNode.KernelPorts.InputBuffer.Port)), array);
                var batchHandle = set.SubmitDeferredInputBatch(new JobHandle(), batch);

                var gv = set.CreateGraphValue(node, InputBufferNode.KernelPorts.OutSum);

                set.Update();

                Assert.AreEqual(sequence.Sum(), set.GetValueBlocking(gv));

                set.Update();

                // Inputs disappear after one update
                Assert.AreEqual(0, set.GetValueBlocking(gv));

                set.ReleaseGraphValue(gv);
                set.Destroy(node);
            }
        }

        [Test]
        public void CanReadAndSum_ArrayOfInputBuffer_ProvidedFrom_InputBatch()
        {
            const int k_SequenceLength = 10;

            using (var array = new NativeArray<int>(k_SequenceLength, Allocator.Temp))
            using (var doubleArray = new NativeArray<int>(k_SequenceLength * 2, Allocator.Temp))
            using (var batch = new FutureArrayInputBatch(10, Allocator.Persistent))
            using (var set = new NodeSet())
            {
                var node = set.Create<InputBufferNode>();
                set.SetPortArraySize(node, InputBufferNode.KernelPorts.ArrayOfInputBuffer, 3);
                var sequence = Enumerable.Range(0, k_SequenceLength);
                var doubleSequence = Enumerable.Range(0, 2 * k_SequenceLength);

                array.CopyFrom(sequence.ToArray());
                doubleArray.CopyFrom(doubleSequence.ToArray());

                batch.SetTransientBuffer(new InputPair(set, node, new InputPortArrayID(InputBufferNode.KernelPorts.ArrayOfInputBuffer.Port, 0)), array);
                batch.SetTransientBuffer(new InputPair(set, node, new InputPortArrayID(InputBufferNode.KernelPorts.ArrayOfInputBuffer.Port, 2)), doubleArray);
                var batchHandle = set.SubmitDeferredInputBatch(new JobHandle(), batch);

                var gv = set.CreateGraphValue(node, InputBufferNode.KernelPorts.OutArraySum);

                set.Update();

                Assert.AreEqual(sequence.Sum() + doubleSequence.Sum(), set.GetValueBlocking(gv));

                set.Update();

                // Inputs disappear after one update
                Assert.AreEqual(0, set.GetValueBlocking(gv));

                set.ReleaseGraphValue(gv);
                set.Destroy(node);
            }
        }

        struct NodeGV
        {
            public NodeHandle<InputBufferNode> Node;

            public InputPair
                InputBuffer,
                InputBuffer2,
                ArrayOfInputBuffer_0,
                ArrayOfInputBuffer_2;

            public GraphValue<long> GV, GV2, GV3;

            public NodeGV(NodeSet set, NodeHandle<InputBufferNode> node)
            {
                GV = set.CreateGraphValue(node, InputBufferNode.KernelPorts.OutSum);
                GV2 = set.CreateGraphValue(node, InputBufferNode.KernelPorts.OutSum2);
                GV3 = set.CreateGraphValue(node, InputBufferNode.KernelPorts.OutArraySum);

                InputBuffer = new InputPair(set, node, new InputPortArrayID(InputBufferNode.KernelPorts.InputBuffer.Port));
                InputBuffer2 = new InputPair(set, node, new InputPortArrayID(InputBufferNode.KernelPorts.InputBuffer2.Port));
                ArrayOfInputBuffer_0 = new InputPair(set, node, new InputPortArrayID(InputBufferNode.KernelPorts.ArrayOfInputBuffer.Port, 0));
                ArrayOfInputBuffer_2 = new InputPair(set, node, new InputPortArrayID(InputBufferNode.KernelPorts.ArrayOfInputBuffer.Port, 2));

                Node = node;
            }
        }

        struct DeferredJobProducer : IJob
        {
            public NativeList<NodeGV> NodeList;
            public NativeArray<int> Array, DoubleArray;
            public FutureArrayInputBatch Batch;

            public void Execute()
            {
                for (int i = 0; i < NodeList.Length; ++i)
                {
                    Batch.SetTransientBuffer(NodeList[i].InputBuffer, Array);
                    Batch.SetTransientBuffer(NodeList[i].InputBuffer2, DoubleArray);
                    Batch.SetTransientBuffer(NodeList[i].ArrayOfInputBuffer_0, Array);
                    Batch.SetTransientBuffer(NodeList[i].ArrayOfInputBuffer_2, DoubleArray);
                }
            }
        }

        [Test]
        public void ZCanReadAndSum_InputBuffers_ProvidedFrom_JobifiedInputBatch([Values(1, 2, 5, 13, 46)] int sequenceLength)
        {
            var sequence = Enumerable.Range(0, sequenceLength);
            var doubleSequence = Enumerable.Range(0, sequenceLength * 2);

            long sequenceSum = sequence.Sum();
            long doubleSequenceSum = doubleSequence.Sum();

            using (var array = new NativeArray<int>(sequenceLength, Allocator.Persistent))
            using (var doubleArray = new NativeArray<int>(sequenceLength * 2, Allocator.Persistent))
            using (var set = new NodeSet())
            using (var nodes = new NativeList<NodeGV>(sequenceLength, Allocator.Persistent))
            {
                for (int i = 0; i < sequenceLength; ++i)
                {
                    var node = set.Create<InputBufferNode>();
                    set.SetPortArraySize(node, InputBufferNode.KernelPorts.ArrayOfInputBuffer, 3);

                    nodes.Add(new NodeGV (set, node));
                }

                array.CopyFrom(sequence.ToArray());
                doubleArray.CopyFrom(doubleSequence.ToArray());

                var mainThreadNodes = new NodeGV[nodes.Length];
                for (int i = 0; i < nodes.Length; ++i)
                    mainThreadNodes[i] = nodes[i];

                using (var batch = new FutureArrayInputBatch(sequenceLength, Allocator.Persistent))
                {
                    DeferredJobProducer producer;

                    producer.Array = array;
                    producer.DoubleArray = doubleArray;
                    producer.Batch = batch;
                    producer.NodeList = nodes;

                    var batchHandle = set.SubmitDeferredInputBatch(producer.Schedule(), batch);

                    set.Update();

                    for (int i = 0; i < sequenceLength; ++i)
                    {
                        var actualSum1 = set.GetValueBlocking(mainThreadNodes[i].GV);
                        var actualSum2 = set.GetValueBlocking(mainThreadNodes[i].GV2);
                        var actualSum3 = set.GetValueBlocking(mainThreadNodes[i].GV3);

                        Assert.AreEqual(sequenceSum, actualSum1);
                        Assert.AreEqual(doubleSequenceSum, actualSum2);
                        Assert.AreEqual(sequenceSum + doubleSequenceSum, actualSum3);
                    }

                    set.Update();

                    // Inputs disappear after one update
                    for (int i = 0; i < sequenceLength; ++i)
                    {
                        Assert.AreEqual(0L, set.GetValueBlocking(mainThreadNodes[i].GV));
                        Assert.AreEqual(0L, set.GetValueBlocking(mainThreadNodes[i].GV2));
                        Assert.AreEqual(0L, set.GetValueBlocking(mainThreadNodes[i].GV3));
                    }

                    set.GetBatchDependencies(batchHandle).Complete();
                }

                for (int i = 0; i < sequenceLength; ++i)
                {
                    set.ReleaseGraphValue(mainThreadNodes[i].GV);
                    set.ReleaseGraphValue(mainThreadNodes[i].GV2);
                    set.ReleaseGraphValue(mainThreadNodes[i].GV3);

                    set.Destroy(mainThreadNodes[i].Node);
                }
            }
        }

        [Test]
        public void CannotDoubleAssign_ToASinglePort()
        {
            if (JobsUtility.JobCompilerEnabled)
                Assert.Ignore("Skipping test since Burst doesn't support exceptions");

            using (var array = new NativeArray<int>(10, Allocator.Temp))
            using (var batch = new FutureArrayInputBatch(10, Allocator.Persistent))
            using (var set = new NodeSet())
            {
                var a = set.Create<NodeWithAllTypesOfPorts>();

                batch.SetTransientBuffer(new InputPair(set, a, new InputPortArrayID(NodeWithAllTypesOfPorts.KernelPorts.InputBuffer.Port)), array);
                batch.SetTransientBuffer(new InputPair(set, a, new InputPortArrayID(NodeWithAllTypesOfPorts.KernelPorts.InputBuffer.Port)), array);

                var batchHandle = set.SubmitDeferredInputBatch(new JobHandle(), batch);

                LogAssert.Expect(UnityEngine.LogType.Exception, new Regex("Cannot assign a buffer to a port that has already been assigned"));

                set.Update();

                set.Destroy(a);
            }
        }

        [Test]
        public void CannotAssign_ToAlreadyConnectedPort()
        {
            if (JobsUtility.JobCompilerEnabled)
                Assert.Ignore("Skipping test since Burst doesn't support exceptions");

            using (var array = new NativeArray<int>(10, Allocator.Temp))
            using (var batch = new FutureArrayInputBatch(10, Allocator.Persistent))
            using (var set = new NodeSet())
            {
                NodeHandle<NodeWithAllTypesOfPorts>
                    a = set.Create<NodeWithAllTypesOfPorts>(),
                    b = set.Create<NodeWithAllTypesOfPorts>();

                set.Connect(a, NodeWithAllTypesOfPorts.KernelPorts.OutputBuffer, b, NodeWithAllTypesOfPorts.KernelPorts.InputBuffer);

                batch.SetTransientBuffer(new InputPair(set, b, new InputPortArrayID(NodeWithAllTypesOfPorts.KernelPorts.InputBuffer.Port)), array);

                var batchHandle = set.SubmitDeferredInputBatch(new JobHandle(), batch);

                LogAssert.Expect(UnityEngine.LogType.Exception, new Regex("Cannot assign a buffer to an input port on a node that is already connected to something else"));

                set.Update();

                set.Destroy(a, b);
            }
        }
    }
}
