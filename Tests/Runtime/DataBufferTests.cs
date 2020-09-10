using System;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.DataFlowGraph.Tests
{
    public class DataBufferTests
    {
        [Test]
        public void CannotRequest_NegativeBufferSize()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Buffer<long>.SizeRequest(-1));
        }

        public struct Aggregate
        {
            public Buffer<long> SubBuffer1;
            public Buffer<long> SubBuffer2;
        }

        public class KernelBufferOutputNode : KernelNodeDefinition<KernelBufferOutputNode.KernelDefs>
        {
            public struct KernelDefs : IKernelPortDefinition
            {
                public DataOutput<KernelBufferOutputNode, Buffer<long>> Output1, Output2;
                public DataOutput<KernelBufferOutputNode, Aggregate> Output3;
            }

            struct Data : IKernelData { }

            [BurstCompile(CompileSynchronously = true)]
            struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                public void Execute(RenderContext ctx, Data data, ref KernelDefs ports)
                {
                    for (int i = 0; i < 2; ++i)
                    {
                        var buffer = ctx.Resolve(ref i == 0 ? ref ports.Output1 : ref ports.Output2);
                        for (int j = 0; j < buffer.Length; ++j)
                            buffer[j] = j + 1;
                    }

                    var resolved = ctx.Resolve(ref ports.Output3);
                    for (int i = 0; i < 2; ++i)
                    {
                        var subBuffer = i == 0 ? resolved.SubBuffer1 : resolved.SubBuffer2;
                        var buffer = subBuffer.ToNative(ctx);
                        for (int j = 0; j < buffer.Length; ++j)
                            buffer[j] = j + 1;
                    }
                }
            }
        }

        public class KernelBufferInputNode : KernelNodeDefinition<KernelBufferInputNode.KernelDefs>
        {
            public struct KernelDefs : IKernelPortDefinition
            {
                public DataInput<KernelBufferInputNode, Buffer<long>> Input1, Input2;
                public DataInput<KernelBufferInputNode, Aggregate> Input3;
                public DataOutput<KernelBufferInputNode, long> Sum;
            }

            struct Data : IKernelData { }

            [BurstCompile(CompileSynchronously = true)]
            struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                public void Execute(RenderContext ctx, Data data, ref KernelDefs ports)
                {
                    long sum = 0;
                    var buffer = ctx.Resolve(ports.Input1);
                    for (int i = 0; i < buffer.Length; ++i)
                        sum += buffer[i];
                    buffer = ctx.Resolve(ports.Input2);
                    for (int i = 0; i < buffer.Length; ++i)
                        sum += buffer[i];

                    var resolved = ctx.Resolve(ports.Input3);
                    buffer = resolved.SubBuffer1.ToNative(ctx);
                    for (int i = 0; i < buffer.Length; ++i)
                        sum += buffer[i];
                    buffer = resolved.SubBuffer2.ToNative(ctx);
                    for (int i = 0; i < buffer.Length; ++i)
                        sum += buffer[i];

                    ctx.Resolve(ref ports.Sum) = sum;
                }
            }
        }

        [Test]
        public void CanSetSize_OnDataBuffers_UsingWeakAPI()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<KernelBufferOutputNode>();

                set.SetBufferSize(node, (OutputPortID)KernelBufferOutputNode.KernelPorts.Output1, Buffer<long>.SizeRequest(1));
                set.SetBufferSize(node, (OutputPortID)KernelBufferOutputNode.KernelPorts.Output1, new Buffer<long>());

                set.SetBufferSize(node, (OutputPortID)KernelBufferOutputNode.KernelPorts.Output3, new Aggregate { SubBuffer1 = Buffer<long>.SizeRequest(1) });
                set.SetBufferSize(node, (OutputPortID)KernelBufferOutputNode.KernelPorts.Output3, new Aggregate());

                set.Destroy(node);
            }
        }

        [Test]
        public void CannotSetSize_OnDataBuffers_UsingWeakAPI_UsingWrongParameterType()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<KernelBufferOutputNode>();

                Assert.Throws<InvalidOperationException>(() => set.SetBufferSize(node, (OutputPortID)KernelBufferOutputNode.KernelPorts.Output1, 1));

                Assert.Throws<InvalidOperationException>(() => set.SetBufferSize(node, (OutputPortID)KernelBufferOutputNode.KernelPorts.Output3, Buffer<long>.SizeRequest(1)));
                Assert.Throws<InvalidOperationException>(() => set.SetBufferSize(node, (OutputPortID)KernelBufferOutputNode.KernelPorts.Output3, 1));

                set.Destroy(node);
            }
        }

        [Test]
        public void CanSetSize_OnDataBuffers_UsingStrongAPI()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<KernelBufferOutputNode>();

                set.SetBufferSize(node, KernelBufferOutputNode.KernelPorts.Output1, Buffer<long>.SizeRequest(1));
                set.SetBufferSize(node, KernelBufferOutputNode.KernelPorts.Output1, new Buffer<long>());

                set.SetBufferSize(node, KernelBufferOutputNode.KernelPorts.Output3, new Aggregate { SubBuffer1 = Buffer<long>.SizeRequest(1) });
                set.SetBufferSize(node, KernelBufferOutputNode.KernelPorts.Output3, new Aggregate());

                set.Destroy(node);
            }
        }

        [Test]
        public unsafe void CannotSetSize_OnDataBuffers_UsingWeakAPI_UsingInvalidSizeParameter()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<KernelBufferOutputNode>();

                // This could come, for example, from an actual DataOutput<Buffer<long>> instance.
                var invalidBufferSize = new Buffer<long>(null, 1, default);

                Assert.Throws<InvalidOperationException>(() => set.SetBufferSize(node, (OutputPortID)KernelBufferOutputNode.KernelPorts.Output1, invalidBufferSize));

                Assert.Throws<InvalidOperationException>(() => set.SetBufferSize(node, (OutputPortID)KernelBufferOutputNode.KernelPorts.Output3, new Aggregate { SubBuffer1 = invalidBufferSize }));
                Assert.Throws<InvalidOperationException>(() => set.SetBufferSize(node, (OutputPortID)KernelBufferOutputNode.KernelPorts.Output3, invalidBufferSize));

                set.Destroy(node);
            }
        }

        [Test]
        public unsafe void CannotSetSize_OnDataBuffers_UsingStrongAPI_UsingInvalidSizeParameter()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<KernelBufferOutputNode>();

                // This could come, for example, from an actual DataOutput<Buffer<long>> instance.
                var invalidBufferSize = new Buffer<long>(null, 1, default);

                Assert.Throws<InvalidOperationException>(() => set.SetBufferSize(node, KernelBufferOutputNode.KernelPorts.Output1, invalidBufferSize));

                Assert.Throws<InvalidOperationException>(() => set.SetBufferSize(node, KernelBufferOutputNode.KernelPorts.Output3, new Aggregate { SubBuffer1 = invalidBufferSize }));

                set.Destroy(node);
            }
        }

        [Test]
        public unsafe void CannotSetSize_OnDataBuffers_UsingInvalidSizeParameter()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<KernelBufferOutputNode>();

                // This could come, for example, from an actual DataOutput<Buffer<long>> instance.
                var invalidBufferSize = new Buffer<long>(null, 1, default);

                set.SetBufferSize(node, KernelBufferOutputNode.KernelPorts.Output1.Port, Buffer<long>.SizeRequest(1));

                Assert.Throws<InvalidOperationException>(() => set.SetBufferSize(node, KernelBufferOutputNode.KernelPorts.Output1.Port, invalidBufferSize));
                Assert.Throws<InvalidOperationException>(() => set.SetBufferSize(node, KernelBufferOutputNode.KernelPorts.Output1, invalidBufferSize));

                set.SetBufferSize(node, KernelBufferOutputNode.KernelPorts.Output3.Port, new Aggregate { SubBuffer1 = Buffer<long>.SizeRequest(1) });

                Assert.Throws<InvalidOperationException>(() => set.SetBufferSize(node, KernelBufferOutputNode.KernelPorts.Output3.Port, new Aggregate { SubBuffer1 = invalidBufferSize }));
                Assert.Throws<InvalidOperationException>(() => set.SetBufferSize(node, KernelBufferOutputNode.KernelPorts.Output3, new Aggregate { SubBuffer1 = invalidBufferSize }));
                Assert.Throws<InvalidOperationException>(() => set.SetBufferSize(node, KernelBufferOutputNode.KernelPorts.Output3.Port, invalidBufferSize));

                set.Destroy(node);
            }
        }

        [TestCase(0), TestCase(1), TestCase(5), TestCase(20), TestCase(100), TestCase(50000)]
        public void CanWrite_ToDataBuffers(int bufferSize)
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<KernelBufferOutputNode>();

                set.SetBufferSize(node, KernelBufferOutputNode.KernelPorts.Output1, Buffer<long>.SizeRequest(bufferSize));
                set.SetBufferSize(node, KernelBufferOutputNode.KernelPorts.Output2, Buffer<long>.SizeRequest(bufferSize));
                var aggregateBufferSizes = new Aggregate
                {
                    SubBuffer1 = Buffer<long>.SizeRequest(bufferSize),
                    SubBuffer2 = Buffer<long>.SizeRequest(bufferSize)
                };
                set.SetBufferSize(node, KernelBufferOutputNode.KernelPorts.Output3, aggregateBufferSizes);

                set.Update();
                set.Destroy(node);
            }
        }

        [TestCase(0), TestCase(1), TestCase(5), TestCase(20), TestCase(100), TestCase(50000)]
        public void CanWriteAndRead_ExpectedSum_FromNaturalIntegerRange(int bufferSize)
        {
            using (var set = new NodeSet())
            {
                var output = set.Create<KernelBufferOutputNode>();

                var input = set.Create<KernelBufferInputNode>();

                set.SetBufferSize(output, KernelBufferOutputNode.KernelPorts.Output1, Buffer<long>.SizeRequest(bufferSize));
                set.SetBufferSize(output, KernelBufferOutputNode.KernelPorts.Output2, Buffer<long>.SizeRequest(bufferSize));
                var aggregateBufferSizes = new Aggregate
                {
                    SubBuffer1 = Buffer<long>.SizeRequest(bufferSize),
                    SubBuffer2 = Buffer<long>.SizeRequest(bufferSize)
                };
                set.SetBufferSize(output, KernelBufferOutputNode.KernelPorts.Output3, aggregateBufferSizes);

                set.Connect(output, KernelBufferOutputNode.KernelPorts.Output1, input, KernelBufferInputNode.KernelPorts.Input1);
                set.Connect(output, KernelBufferOutputNode.KernelPorts.Output2, input, KernelBufferInputNode.KernelPorts.Input2);
                set.Connect(output, KernelBufferOutputNode.KernelPorts.Output3, input, KernelBufferInputNode.KernelPorts.Input3);

                var value = set.CreateGraphValue(input, KernelBufferInputNode.KernelPorts.Sum);

                set.Update();

                long n = bufferSize;
                Assert.AreEqual(4 * n * (n + 1) / 2, set.GetValueBlocking(value));

                set.ReleaseGraphValue(value);
                set.Destroy(input, output);
            }
        }


        public class GenericOutput<T>
            : SimulationKernelNodeDefinition<GenericOutput<T>.SimPorts, GenericOutput<T>.KernelDefs>
                where T : struct
        {
            public struct SimPorts : ISimulationPortDefinition
            {
                public MessageInput<GenericOutput<T>, T> Input;

                // (To ensure we test cases where data buffer outputs are not the first port output declaration)
                public MessageOutput<GenericOutput<T>, T> Output;
            }

            public struct KernelDefs : IKernelPortDefinition
            {
                public DataOutput<GenericOutput<T>, Buffer<T>> Output;
            }

            struct KernelData : IKernelData
            {
                public T Value;
            }

            [BurstCompile(CompileSynchronously = true)]
            struct Kernel : IGraphKernel<KernelData, KernelDefs>
            {
                public void Execute(RenderContext ctx, KernelData data, ref KernelDefs ports)
                {
                    var output = ctx.Resolve(ref ports.Output);
                    for (int i = 0; i < output.Length; ++i)
                        output[i] = data.Value;
                }
            }

            struct Node : INodeData, IMsgHandler<T>
            {
                public void HandleMessage(in MessageContext ctx, in T msg) => ctx.UpdateKernelData(new KernelData { Value = msg });
            }
        }

        public class GenericInput<T>
            : SimulationKernelNodeDefinition<GenericInput<T>.SimPorts, GenericInput<T>.KernelDefs>
                where T : struct

        {
            public struct SimPorts : ISimulationPortDefinition
            {
                // (To ensure we test cases where inputs are not the first port input declaration)
                public MessageInput<GenericInput<T>, T> Dummy;
            }

            public struct KernelDefs : IKernelPortDefinition
            {
                public DataInput<GenericInput<T>, Buffer<T>> Input;
                public DataOutput<GenericInput<T>, T> Output;
            }

            public struct KernelData : IKernelData
            {
                public T Value;
            }

            [BurstCompile(CompileSynchronously = true)]
            struct Kernel : IGraphKernel<KernelData, KernelDefs>
            {
                public void Execute(RenderContext ctx, KernelData data, ref KernelDefs ports)
                {
                    var input = ctx.Resolve(ports.Input);
                    for (int i = 0; i < input.Length; ++i)
                        ctx.Resolve(ref ports.Output) = input[i];
                }
            }

            struct Node : INodeData, IMsgHandler<T>
            {
                public void HandleMessage(in MessageContext ctx, in T msg) => throw new NotImplementedException();
            }
        }

        public struct CustomStructure
        {
            public float Value;
        }

        [TestCase(typeof(int)), TestCase(typeof(float)), TestCase(typeof(double)), TestCase(typeof(CustomStructure))]
        public void CanWriteAndRead_GenericTypes_AndLoopBackTheSameValue(Type genericType)
        {
            if (genericType == typeof(int))
                TestGenericType(genericType, 50, 5);
            else if (genericType == typeof(float))
                TestGenericType(genericType, 50, 15.0f);
            else if (genericType == typeof(double))
                TestGenericType(genericType, 50, 15.0);
            else if (genericType == typeof(CustomStructure))
                TestGenericType(genericType, 50, new CustomStructure { Value = 55.555f });
        }

        NodeHandle GenericNodeFactory(NodeSet set, bool input, Type genericType)
        {
            if (genericType == typeof(int))
                return input ? (NodeHandle)set.Create<GenericInput<int>>() : set.Create<GenericOutput<int>>();
            if (genericType == typeof(float))
                return input ? (NodeHandle)set.Create<GenericInput<float>>() : set.Create<GenericOutput<float>>();
            if (genericType == typeof(double))
                return input ? (NodeHandle)set.Create<GenericInput<double>>() : set.Create<GenericOutput<double>>();
            if (genericType == typeof(CustomStructure))
                return input ? (NodeHandle)set.Create<GenericInput<CustomStructure>>() : set.Create<GenericOutput<CustomStructure>>();
            return default;
        }

        public void TestGenericType<T>(Type genericType, int testBufferSize, T testValue)
            where T : struct
        {
            using (var set = new NodeSet())
            {
                var output = set.CastHandle<GenericOutput<T>>(GenericNodeFactory(set, false, genericType));
                var input = set.CastHandle<GenericInput<T>>(GenericNodeFactory(set, true, genericType));

                set.SetBufferSize(output, GenericOutput<T>.KernelPorts.Output, Buffer<T>.SizeRequest(testBufferSize));
                set.Connect(output, GenericOutput<T>.KernelPorts.Output, input, GenericInput<T>.KernelPorts.Input);

                set.SendMessage(output, GenericOutput<T>.SimulationPorts.Input, testValue);

                var value = set.CreateGraphValue(input, GenericInput<T>.KernelPorts.Output);

                set.Update();
                set.DataGraph.SyncAnyRendering();

                Assert.AreEqual(testValue, set.GetValueBlocking(value));

                set.ReleaseGraphValue(value);
                set.Destroy(input, output);
            }
        }

        [Test]
        public void CanResizeDataBuffers_InSameFrame_AsDestroyingTargetNode()
        {
            using (var set = new NodeSet())
            {
                var output = set.Create<KernelBufferOutputNode>();

                set.SetBufferSize(output, KernelBufferOutputNode.KernelPorts.Output1, Buffer<long>.SizeRequest(1024));
                set.SetBufferSize(output, KernelBufferOutputNode.KernelPorts.Output2, Buffer<long>.SizeRequest(1024));
                var aggregateBufferSizes = new Aggregate
                {
                    SubBuffer1 = Buffer<long>.SizeRequest(1024),
                    SubBuffer2 = Buffer<long>.SizeRequest(1024)
                };
                set.SetBufferSize(output, KernelBufferOutputNode.KernelPorts.Output3, aggregateBufferSizes);

                set.Destroy(output);

                set.Update();
                set.DataGraph.SyncAnyRendering();
            }
        }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        public class KernelBufferInputReaderNode : KernelNodeDefinition<KernelBufferInputReaderNode.KernelDefs>
        {
            public struct KernelDefs : IKernelPortDefinition
            {
                public DataInput<KernelBufferInputReaderNode, Buffer<float>> Input;
                public DataOutput<KernelBufferInputReaderNode, float> Value;
                public DataOutput<KernelBufferInputReaderNode, int> GotException;
                public DataInput<KernelBufferInputReaderNode, int> ArrayIndexToTest;
            }

            struct Data : IKernelData { }

            struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                public void Execute(RenderContext ctx, Data data, ref KernelDefs ports)
                {
                    ctx.Resolve(ref ports.GotException) = 0;
                    try
                    {
                        ctx.Resolve(ref ports.Value) = ctx.Resolve(ports.Input)[ctx.Resolve(ports.ArrayIndexToTest)];
                    }
                    catch
                    {
                        ctx.Resolve(ref ports.GotException) = 1;
                    }
                }
            }
        }

        [Test]
        public void OutOfBounds_DataBufferRead_ThrowsException()
        {
            using (var set = new NodeSet())
            {
                var source = set.Create<GenericOutput<float>>();
                set.SendMessage(source, GenericOutput<float>.SimulationPorts.Input, 5.0f);
                set.SetBufferSize(source, GenericOutput<float>.KernelPorts.Output, Buffer<float>.SizeRequest(10));

                var node1 = set.Create<KernelBufferInputReaderNode>();
                set.Connect(source, GenericOutput<float>.KernelPorts.Output, node1, KernelBufferInputReaderNode.KernelPorts.Input);
                set.SetData(node1, KernelBufferInputReaderNode.KernelPorts.ArrayIndexToTest, 15);
                var value1 = set.CreateGraphValue(node1, KernelBufferInputReaderNode.KernelPorts.Value);
                var gotException1 = set.CreateGraphValue(node1, KernelBufferInputReaderNode.KernelPorts.GotException);

                var node2 = set.Create<KernelBufferInputReaderNode>();
                set.Connect(source, GenericOutput<float>.KernelPorts.Output, node2, KernelBufferInputReaderNode.KernelPorts.Input);
                set.SetData(node2, KernelBufferInputReaderNode.KernelPorts.ArrayIndexToTest, -5);
                var value2 = set.CreateGraphValue(node2, KernelBufferInputReaderNode.KernelPorts.Value);
                var gotException2 = set.CreateGraphValue(node2, KernelBufferInputReaderNode.KernelPorts.GotException);

                var node3 = set.Create<KernelBufferInputReaderNode>();
                set.Connect(source, GenericOutput<float>.KernelPorts.Output, node3, KernelBufferInputReaderNode.KernelPorts.Input);
                set.SetData(node3, KernelBufferInputReaderNode.KernelPorts.ArrayIndexToTest, 5);
                var value3 = set.CreateGraphValue(node3, KernelBufferInputReaderNode.KernelPorts.Value);
                var gotException3 = set.CreateGraphValue(node3, KernelBufferInputReaderNode.KernelPorts.GotException);

                set.Update();

                Assert.AreEqual(0.0f, set.GetValueBlocking(value1));
                Assert.AreEqual(0.0f, set.GetValueBlocking(value2));
                Assert.AreEqual(5.0f, set.GetValueBlocking(value3));

                Assert.AreEqual(1, set.GetValueBlocking(gotException1));
                Assert.AreEqual(1, set.GetValueBlocking(gotException2));
                Assert.AreEqual(0, set.GetValueBlocking(gotException3));

                set.ReleaseGraphValue(value1);
                set.ReleaseGraphValue(value2);
                set.ReleaseGraphValue(value3);
                set.ReleaseGraphValue(gotException1);
                set.ReleaseGraphValue(gotException2);
                set.ReleaseGraphValue(gotException3);

                set.Destroy(source);
                set.Destroy(node1);
                set.Destroy(node2);
                set.Destroy(node3);
            }
        }

        public class KernelBufferOutputWriterNode : KernelNodeDefinition<KernelBufferOutputWriterNode.KernelDefs>
        {
            public struct KernelDefs : IKernelPortDefinition
            {
                public DataInput<KernelBufferOutputWriterNode, float> Value;
                public DataOutput<KernelBufferOutputWriterNode, Buffer<float>> Output;
                public DataOutput<KernelBufferOutputWriterNode, int> GotException;
                public DataInput<KernelBufferOutputWriterNode, int> ArrayIndexToTest;
            }

            struct Data : IKernelData { }

            struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                public void Execute(RenderContext ctx, Data data, ref KernelDefs ports)
                {
                    ctx.Resolve(ref ports.GotException) = 0;
                    try
                    {
                        var output = ctx.Resolve(ref ports.Output);
                        output[ctx.Resolve(ports.ArrayIndexToTest)] = ctx.Resolve(ports.Value);
                    }
                    catch
                    {
                        ctx.Resolve(ref ports.GotException) = 1;
                    }
                }
            }
        }

        [Test]
        public void OutOfBounds_DataBufferWrite_ThrowsException()
        {
            using (var set = new NodeSet())
            {
                var node1 = set.Create<KernelBufferOutputWriterNode>();
                set.SetBufferSize(node1, KernelBufferOutputWriterNode.KernelPorts.Output, Buffer<float>.SizeRequest(10));
                set.SetData(node1, KernelBufferOutputWriterNode.KernelPorts.ArrayIndexToTest, 15);
                var gotException1 = set.CreateGraphValue(node1, KernelBufferOutputWriterNode.KernelPorts.GotException);

                var node2 = set.Create<KernelBufferOutputWriterNode>();
                set.SetBufferSize(node2, KernelBufferOutputWriterNode.KernelPorts.Output, Buffer<float>.SizeRequest(10));
                set.SetData(node2, KernelBufferOutputWriterNode.KernelPorts.ArrayIndexToTest, -5);
                var gotException2 = set.CreateGraphValue(node2, KernelBufferOutputWriterNode.KernelPorts.GotException);

                var node3 = set.Create<KernelBufferOutputWriterNode>();
                set.SetBufferSize(node3, KernelBufferOutputWriterNode.KernelPorts.Output, Buffer<float>.SizeRequest(10));
                set.SetData(node3, KernelBufferOutputWriterNode.KernelPorts.Value, 7.0f);
                set.SetData(node3, KernelBufferOutputWriterNode.KernelPorts.ArrayIndexToTest, 5);
                var gotException3 = set.CreateGraphValue(node3, KernelBufferOutputWriterNode.KernelPorts.GotException);

                set.Update();

                Assert.AreEqual(1, set.GetValueBlocking(gotException1));
                Assert.AreEqual(1, set.GetValueBlocking(gotException2));
                Assert.AreEqual(0, set.GetValueBlocking(gotException3));

                set.ReleaseGraphValue(gotException1);
                set.ReleaseGraphValue(gotException2);
                set.ReleaseGraphValue(gotException3);

                set.Destroy(node1);
                set.Destroy(node2);
                set.Destroy(node3);
            }
        }

        public class NodeThatWritesToInputBuffer : KernelNodeDefinition<NodeThatWritesToInputBuffer.KernelDefs>
        {
            public struct KernelDefs : IKernelPortDefinition
            {
                public DataInput<NodeThatWritesToInputBuffer, Buffer<long>> Input;
                public DataOutput<NodeThatWritesToInputBuffer, int> GotException;
            }

            struct Data : IKernelData { }

            struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                public void Execute(RenderContext ctx, Data data, ref KernelDefs ports)
                {
                    ctx.Resolve(ref ports.GotException) = 0;

                    var input = ctx.Resolve(ports.Input);

                    var x = input[0];

                    try
                    {
                        input[0] = x;
                    }
                    catch
                    {
                        ctx.Resolve(ref ports.GotException) = 1;
                    }
                }
            }
        }

        [Test]
        public void InputDataBuffers_AreReadOnly()
        {
            // (read, write) already covered in the other tests.

            using (var set = new NodeSet())
            {
                var outputProvider = set.Create<KernelBufferOutputNode>();
                set.SetBufferSize(outputProvider, KernelBufferOutputNode.KernelPorts.Output1, Buffer<long>.SizeRequest(1));
                var nodeThatWritesToInput = set.Create<NodeThatWritesToInputBuffer>();
                var gotException = set.CreateGraphValue(nodeThatWritesToInput, NodeThatWritesToInputBuffer.KernelPorts.GotException);

                set.Connect(outputProvider, KernelBufferOutputNode.KernelPorts.Output1, nodeThatWritesToInput, NodeThatWritesToInputBuffer.KernelPorts.Input);
                set.Update();

                Assert.AreEqual(1, set.GetValueBlocking(gotException));

                set.ReleaseGraphValue(gotException);
                set.Destroy(nodeThatWritesToInput, outputProvider);
            }
        }

        public class StaleKernelChecker : KernelNodeDefinition<StaleKernelChecker.KernelDefs>
        {
            public struct KernelDefs : IKernelPortDefinition
            {
                public DataInput<StaleKernelChecker, Buffer<long>> Input;
                public DataOutput<StaleKernelChecker, Buffer<long>> Output;

                public DataOutput<StaleKernelChecker, int> ErrorCode;
            }

            struct Data : IKernelData { }

            unsafe struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                struct CheatingNativeArrayStorage
                {
                    public NativeArray<long> input, output;
                }

                fixed byte m_NativeArrayStorage[0xFF];

                int m_IsInitialized;

                public void Execute(RenderContext ctx, Data data, ref KernelDefs ports)
                {
                    if (UnsafeUtility.SizeOf<CheatingNativeArrayStorage>() >= 0xFF)
                    {
                        ctx.Resolve(ref ports.ErrorCode) = -1;
                        return;
                    }

                    ctx.Resolve(ref ports.ErrorCode) = 0;

                    if (m_IsInitialized == 0)
                    {
                        GetStorage().input = ctx.Resolve(ports.Input);
                        GetStorage().output = ctx.Resolve(ref ports.Output);

                        m_IsInitialized = 1;
                    }
                    else
                    {
                        long x = 0;

                        try
                        {
                            x = GetStorage().input[0];
                        }
                        catch
                        {
                            ctx.Resolve(ref ports.ErrorCode) += 1;
                        }

                        try
                        {
                            GetStorage().output[0] = x;
                        }
                        catch
                        {
                            ctx.Resolve(ref ports.ErrorCode) += 1;
                        }

                    }
                }

                ref CheatingNativeArrayStorage GetStorage()
                {
                    fixed (byte* storage = m_NativeArrayStorage)
                        return ref Utility.AsRef<CheatingNativeArrayStorage>(storage);
                }
            }
        }

        [Test]
        public void ResolvedDataBuffers_GrowStale_AfterOneUpdate()
        {
            using (var set = new NodeSet())
            {
                var outputProvider = set.Create<KernelBufferOutputNode>();
                var staleNode = set.Create<StaleKernelChecker>();

                set.SetBufferSize(outputProvider, KernelBufferOutputNode.KernelPorts.Output1, Buffer<long>.SizeRequest(1));
                set.SetBufferSize(staleNode, StaleKernelChecker.KernelPorts.Output, Buffer<long>.SizeRequest(1));


                var gotException = set.CreateGraphValue(staleNode, StaleKernelChecker.KernelPorts.ErrorCode);

                set.Connect(outputProvider, KernelBufferOutputNode.KernelPorts.Output1, staleNode, StaleKernelChecker.KernelPorts.Input);

                // first frame is OK
                set.Update();
                Assert.AreEqual(0, set.GetValueBlocking(gotException));

                // from the second frame and onwards, access to stale buffers should happen
                for (int i = 0; i < 10; ++i)
                {
                    set.Update();
                    Assert.AreEqual(2, set.GetValueBlocking(gotException));
                }

                set.ReleaseGraphValue(gotException);
                set.Destroy(staleNode, outputProvider);
            }
        }
#endif

        public struct ComplexAggregate
        {
            public float FloatScalar;
            public Buffer<double> Doubles;
            public short ShortScalar;
            public Buffer<float4> Vectors;
            public byte ByteScalar;
            public Buffer<byte> Bytes;
            public Buffer<float4x4> Matrices;

            public unsafe override bool Equals(object obj)
            {
                return
                    obj is ComplexAggregate right &&
                    FloatScalar == right.FloatScalar &&
                    Doubles.Size == right.Doubles.Size &&
                    UnsafeUtility.MemCmp(Doubles.Ptr, right.Doubles.Ptr, Doubles.Size * sizeof(double)) == 0 &&
                    ShortScalar == right.ShortScalar &&
                    Vectors.Size == right.Vectors.Size &&
                    UnsafeUtility.MemCmp(Vectors.Ptr, right.Vectors.Ptr, Vectors.Size * sizeof(float4)) == 0 &&
                    ByteScalar == right.ByteScalar &&
                    Bytes.Size == right.Bytes.Size &&
                    UnsafeUtility.MemCmp(Bytes.Ptr, right.Bytes.Ptr, Bytes.Size * sizeof(byte)) == 0 &&
                    Matrices.Size == right.Matrices.Size &&
                    UnsafeUtility.MemCmp(Matrices.Ptr, right.Matrices.Ptr, Matrices.Size * sizeof(float4x4)) == 0;
            }

            public override int GetHashCode() => 0;

            public static void RandomizeOutput(in RenderContext ctx, uint seed, ref ComplexAggregate output)
            {
                var r = new Unity.Mathematics.Random(seed);

                var outDoubles = output.Doubles.ToNative(ctx);
                for (int i = 0; i < outDoubles.Length; ++i)
                    outDoubles[i] = r.NextDouble();

                output.ShortScalar = (short)r.NextInt();

                var outVectors = output.Vectors.ToNative(ctx);
                for (int i = 0; i < outVectors.Length; ++i)
                    outVectors[i] = r.NextFloat4();

                output.FloatScalar = r.NextFloat();

                var outBytes = output.Bytes.ToNative(ctx);
                for (int i = 0; i < outBytes.Length; ++i)
                    outBytes[i] = (byte)r.NextInt();

                var outMatrices = output.Matrices.ToNative(ctx);
                for (int i = 0; i < outMatrices.Length; ++i)
                    outMatrices[i] = new float4x4(r.NextFloat4(), r.NextFloat4(), r.NextFloat4(), r.NextFloat4());

                output.ByteScalar = (byte)r.NextInt();
            }

            public static void ArbitraryTransformation(in RenderContext ctx, in ComplexAggregate input, ref ComplexAggregate output)
            {
                var inDoubles = input.Doubles.ToNative(ctx);
                var outDoubles = output.Doubles.ToNative(ctx);
                for (int i = 0; i < Math.Min(inDoubles.Length, outDoubles.Length); ++i)
                    outDoubles[i] = inDoubles[i] * input.ShortScalar;

                output.ShortScalar = (short)(input.ShortScalar * input.ShortScalar);

                var inVectors = input.Vectors.ToNative(ctx);
                var outVectors = output.Vectors.ToNative(ctx);
                for (int i = 0; i < Math.Min(inVectors.Length, outVectors.Length); ++i)
                    outVectors[i] = inVectors[i] * input.FloatScalar;

                output.FloatScalar = input.FloatScalar * input.FloatScalar;

                var inBytes = input.Bytes.ToNative(ctx);
                var outBytes = output.Bytes.ToNative(ctx);
                for (int i = 0; i < Math.Min(inBytes.Length, outBytes.Length); ++i)
                    outBytes[i] = (byte)(inBytes[i] + input.ByteScalar);

                var inMatrices = input.Matrices.ToNative(ctx);
                var outMatrices = output.Matrices.ToNative(ctx);
                for (int i = 0; i < Math.Min(inMatrices.Length, outMatrices.Length); ++i)
                    outMatrices[i] = inMatrices[i] * input.ByteScalar;

                output.ByteScalar = (byte)(input.ByteScalar * input.ByteScalar);
            }
        }

        public class ComplexKernelAggregateNode : KernelNodeDefinition<ComplexKernelAggregateNode.KernelDefs>
        {
            public struct KernelDefs : IKernelPortDefinition
            {
                public DataInput<ComplexKernelAggregateNode, uint> RandomSeed;
                public DataInput<ComplexKernelAggregateNode, ComplexAggregate> Input;
                public DataOutput<ComplexKernelAggregateNode, uint> __padding;
                public DataOutput<ComplexKernelAggregateNode, ComplexAggregate> Output;
            }

            struct Data : IKernelData { }

            [BurstCompile(CompileSynchronously = true)]
            struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                public void Execute(RenderContext ctx, Data data, ref KernelDefs ports)
                {
                    var input = ctx.Resolve(ports.Input);
                    ref var output = ref ctx.Resolve(ref ports.Output);
                    var randomSeed = ctx.Resolve(ports.RandomSeed);
                    if (randomSeed != 0)
                        ComplexAggregate.RandomizeOutput(ctx, randomSeed, ref output);
                    else
                        ComplexAggregate.ArbitraryTransformation(ctx, input, ref output);
                }
            }
        }

        [Test]
        public unsafe void DataBufferResizes_AreCorrectlyIssued_InGraphDiff()
        {
            using (var set = new NodeSet())
            {
                var a = set.Create<ComplexKernelAggregateNode>();

                set.SetBufferSize(a, ComplexKernelAggregateNode.KernelPorts.Output,
                    new ComplexAggregate() { Doubles = Buffer<double>.SizeRequest(100) });

                Assert.AreEqual(1, set.GetCurrentGraphDiff().ResizedDataBuffers.Count);
                Assert.AreEqual(100, set.GetCurrentGraphDiff().ResizedDataBuffers[0].NewSize);
                Assert.AreEqual(
                    UnsafeUtility.GetFieldOffset(typeof(ComplexAggregate).GetField("Doubles")),
                    set.GetCurrentGraphDiff().ResizedDataBuffers[0].LocalBufferOffset);
                Assert.AreEqual(UnsafeUtility.SizeOf<double>(), set.GetCurrentGraphDiff().ResizedDataBuffers[0].ItemType.Size);

                set.SetBufferSize(a, ComplexKernelAggregateNode.KernelPorts.Output,
                    new ComplexAggregate() { Bytes = Buffer<byte>.SizeRequest(75) });

                Assert.AreEqual(2, set.GetCurrentGraphDiff().ResizedDataBuffers.Count);
                Assert.AreEqual(75, set.GetCurrentGraphDiff().ResizedDataBuffers[1].NewSize);
                Assert.AreEqual(
                    UnsafeUtility.GetFieldOffset(typeof(ComplexAggregate).GetField("Bytes")),
                    set.GetCurrentGraphDiff().ResizedDataBuffers[1].LocalBufferOffset);
                Assert.AreEqual(UnsafeUtility.SizeOf<byte>(), set.GetCurrentGraphDiff().ResizedDataBuffers[1].ItemType.Size);

                set.SetBufferSize(a, ComplexKernelAggregateNode.KernelPorts.Output,
                    new ComplexAggregate() { Matrices = Buffer<float4x4>.SizeRequest(125) });

                Assert.AreEqual(3, set.GetCurrentGraphDiff().ResizedDataBuffers.Count);
                Assert.AreEqual(125, set.GetCurrentGraphDiff().ResizedDataBuffers[2].NewSize);
                Assert.AreEqual(
                    UnsafeUtility.GetFieldOffset(typeof(ComplexAggregate).GetField("Matrices")),
                    set.GetCurrentGraphDiff().ResizedDataBuffers[2].LocalBufferOffset);
                Assert.AreEqual(UnsafeUtility.SizeOf<float4x4>(), set.GetCurrentGraphDiff().ResizedDataBuffers[2].ItemType.Size);

                set.SetBufferSize(a, ComplexKernelAggregateNode.KernelPorts.Output,
                    new ComplexAggregate() { Vectors = Buffer<float4>.SizeRequest(50), Matrices = Buffer<float4x4>.SizeRequest(25) });

                Assert.AreEqual(5, set.GetCurrentGraphDiff().ResizedDataBuffers.Count);
                Assert.AreEqual(50, set.GetCurrentGraphDiff().ResizedDataBuffers[3].NewSize);
                Assert.AreEqual(
                    UnsafeUtility.GetFieldOffset(typeof(ComplexAggregate).GetField("Vectors")),
                    set.GetCurrentGraphDiff().ResizedDataBuffers[3].LocalBufferOffset);
                Assert.AreEqual(UnsafeUtility.SizeOf<float4>(), set.GetCurrentGraphDiff().ResizedDataBuffers[3].ItemType.Size);
                Assert.AreEqual(25, set.GetCurrentGraphDiff().ResizedDataBuffers[4].NewSize);
                Assert.AreEqual(
                    UnsafeUtility.GetFieldOffset(typeof(ComplexAggregate).GetField("Matrices")),
                    set.GetCurrentGraphDiff().ResizedDataBuffers[4].LocalBufferOffset);
                Assert.AreEqual(UnsafeUtility.SizeOf<float4x4>(), set.GetCurrentGraphDiff().ResizedDataBuffers[4].ItemType.Size);

                set.Destroy(a);
            }
        }

        [Test]
        public unsafe void ComplexDataBufferAggregates_CanProcessCorrectly()
        {
            using (var set = new NodeSet())
            {
                var a = set.Create<ComplexKernelAggregateNode>();
                var b = set.Create<ComplexKernelAggregateNode>();

                var bufferSizes = new ComplexAggregate()
                {
                    Doubles = Buffer<double>.SizeRequest(100),
                    Vectors = Buffer<float4>.SizeRequest(50),
                    Bytes = Buffer<byte>.SizeRequest(75),
                    Matrices = Buffer<float4x4>.SizeRequest(125)
                };
                set.SetBufferSize(a, ComplexKernelAggregateNode.KernelPorts.Output, bufferSizes);
                set.SetBufferSize(b, ComplexKernelAggregateNode.KernelPorts.Output, bufferSizes);

                uint k_RandomSeed = 79;
                set.SetData(a, ComplexKernelAggregateNode.KernelPorts.RandomSeed, k_RandomSeed);

                set.Connect(a, ComplexKernelAggregateNode.KernelPorts.Output, b, ComplexKernelAggregateNode.KernelPorts.Input);

                set.Update();
                set.DataGraph.SyncAnyRendering();

                // Perform the A node transformation locally.
                var outputADBuf = stackalloc double[bufferSizes.Doubles.GetSizeRequest().Size];
                var outputAVBuf = stackalloc float4[bufferSizes.Vectors.GetSizeRequest().Size];
                var outputABBuf = stackalloc byte[bufferSizes.Bytes.GetSizeRequest().Size];
                var outputAMBuf = stackalloc float4x4[bufferSizes.Matrices.GetSizeRequest().Size];
                var outputA = new ComplexAggregate()
                {
                    Doubles = new Buffer<double>(outputADBuf, bufferSizes.Doubles.GetSizeRequest().Size, default),
                    Vectors = new Buffer<float4>(outputAVBuf, bufferSizes.Vectors.GetSizeRequest().Size, default),
                    Bytes = new Buffer<byte>(outputABBuf, bufferSizes.Bytes.GetSizeRequest().Size, default),
                    Matrices = new Buffer<float4x4>(outputAMBuf, bufferSizes.Matrices.GetSizeRequest().Size, default),
                };
                var fakeRenderContext = new RenderContext(new ValidatedHandle(), set.DataGraph.m_SharedData.SafetyManager);
                ComplexAggregate.RandomizeOutput(fakeRenderContext, k_RandomSeed, ref outputA);

                var knodes = set.DataGraph.GetInternalData();
                ref var aKNode = ref knodes[((NodeHandle)a).VHandle.Index];
                ref var aKPorts = ref Utility.AsRef<ComplexKernelAggregateNode.KernelDefs>(aKNode.Instance.Ports);
                Assert.AreEqual(aKPorts.Output.m_Value, outputA);

                // Perform the B node transformation locally.
                var outputBDBuf = stackalloc double[bufferSizes.Doubles.GetSizeRequest().Size];
                var outputBVBuf = stackalloc float4[bufferSizes.Vectors.GetSizeRequest().Size];
                var outputBBBuf = stackalloc byte[bufferSizes.Bytes.GetSizeRequest().Size];
                var outputBMBuf = stackalloc float4x4[bufferSizes.Matrices.GetSizeRequest().Size];
                var outputB = new ComplexAggregate()
                {
                    Doubles = new Buffer<double>(outputBDBuf, bufferSizes.Doubles.GetSizeRequest().Size, default),
                    Vectors = new Buffer<float4>(outputBVBuf, bufferSizes.Vectors.GetSizeRequest().Size, default),
                    Bytes = new Buffer<byte>(outputBBBuf, bufferSizes.Bytes.GetSizeRequest().Size, default),
                    Matrices = new Buffer<float4x4>(outputBMBuf, bufferSizes.Matrices.GetSizeRequest().Size, default),
                };
                ComplexAggregate.ArbitraryTransformation(fakeRenderContext, outputA, ref outputB);

                ref var bKNode = ref knodes[((NodeHandle)b).VHandle.Index];
                ref var bKPorts = ref Utility.AsRef<ComplexKernelAggregateNode.KernelDefs>(bKNode.Instance.Ports);
                Assert.AreEqual(bKPorts.Output.m_Value, outputB);

                set.Destroy(a, b);
            }
        }


        public class StatefulKernelNode : KernelNodeDefinition<StatefulKernelNode.KernelDefs>
        {
            public struct KernelDefs : IKernelPortDefinition
            {
                public DataInput<StatefulKernelNode, Buffer<long>> Input;
                public DataOutput<StatefulKernelNode, Buffer<long>> Output;
            }

            struct Node : INodeData, IInit
            {
                public void Init(InitContext ctx)
                {
                    ctx.SetKernelBufferSize(new Kernel {stateBuffer = Buffer<long>.SizeRequest(10)});
                }
            }

            internal struct Data : IKernelData { }

            [BurstCompile(CompileSynchronously = true)]
            internal struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                internal Buffer<long> stateBuffer;

                public void Execute(RenderContext ctx, Data data, ref KernelDefs ports)
                {
                    var input = ctx.Resolve(ports.Input);
                    var output = ctx.Resolve(ref ports.Output);
                    var state = ctx.Resolve(stateBuffer);

                    for (int i = 0; i < output.Length; ++i)
                        output[i] = (i < input.Length ? input[i] : 0) + (i < state.Length ? state[i] : 0);

                    for (int i = 0; i < state.Length; ++i)
                        state[i] = i < input.Length ? input[i] : 0;
                }
            }
        }

        [Test]
        public void CanSetSize_OnKernelBuffers_UsingInternalAPI()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<StatefulKernelNode>();

                set.SetKernelBufferSize(
                    set.Nodes.Validate(node.VHandle),
                    new StatefulKernelNode.Kernel
                    {
                        stateBuffer = Buffer<long>.SizeRequest(1)
                    }
                );

                set.Update();

                set.Destroy(node);
            }
        }

        [Test]
        public void Kernel_CanHave_StateBuffer()
        {
            using (var set = new NodeSet())
            {
                var stateNode = set.Create<StatefulKernelNode>();
                var srcNode = set.Create<KernelBufferOutputNode>();
                var sumNode = set.Create<KernelBufferInputNode>();

                set.SetBufferSize(srcNode, KernelBufferOutputNode.KernelPorts.Output1, Buffer<long>.SizeRequest(20));
                set.SetBufferSize(stateNode, StatefulKernelNode.KernelPorts.Output, Buffer<long>.SizeRequest(20));
                set.Connect(srcNode, KernelBufferOutputNode.KernelPorts.Output1, stateNode, StatefulKernelNode.KernelPorts.Input);
                set.Connect(stateNode, StatefulKernelNode.KernelPorts.Output, sumNode, KernelBufferInputNode.KernelPorts.Input1);

                var value = set.CreateGraphValue(sumNode, KernelBufferInputNode.KernelPorts.Sum);

                set.Update();

                Assert.AreEqual(20*(20+1)/2, set.GetValueBlocking(value));

                set.Update();

                Assert.AreEqual(20*(20+1)/2 + 10*(10+1)/2, set.GetValueBlocking(value));

                set.ReleaseGraphValue(value);
                set.Destroy(srcNode, stateNode, sumNode);
            }
        }

        public class KernelNodeWithInvalidSetKernelBufferSize : KernelNodeDefinition<KernelNodeWithInvalidSetKernelBufferSize.KernelDefs>
        {
            public struct KernelDefs : IKernelPortDefinition {}

            struct Node : INodeData, IInit
            {
                public void Init(InitContext ctx)
                {
                    try
                    {
                        // This is invalid as it doesn't pass in the right IGraphKernel type
                        ctx.SetKernelBufferSize(new StatefulKernelNode.Kernel {stateBuffer = Buffer<long>.SizeRequest(10)});
                    }
                    catch (ArgumentException)
                    {
                        Debug.Log("All is good");
                    }
                }
            }

            struct Data : IKernelData { }

            [BurstCompile(CompileSynchronously = true)]
            struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                public void Execute(RenderContext ctx, Data data, ref KernelDefs ports) {}
            }
        }

        [Test]
        public void CannotSetSize_OnKernelBuffers_WithWrongKernelType()
        {
            using (var set = new NodeSet())
            {
                LogAssert.Expect(LogType.Log, "All is good");
                var node = set.Create<KernelNodeWithInvalidSetKernelBufferSize>();
                set.Destroy(node);
            }
        }
    }
}
