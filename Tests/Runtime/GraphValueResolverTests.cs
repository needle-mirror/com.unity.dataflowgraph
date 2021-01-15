using System;
using System.Linq;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using static Unity.DataFlowGraph.Tests.GraphValueTests;

namespace Unity.DataFlowGraph.Tests
{
    public class GraphValueResolverTests
    {
        [BurstCompile(CompileSynchronously = true)]
        struct GraphValueReadbackJob : IJob
        {
            public GraphValueResolver Resolver;
            public NativeArray<float> Result;
            public GraphValue<float> Value;

            public void Execute()
            {
                Result[0] = Resolver.Resolve(Value);
            }
        }

        [Test]
        public void CanUseGraphValueResolver_ToResolveValues_InAJob([Values] NodeSet.RenderExecutionModel computeType)
        {
            using (var results = new NativeArray<float>(1, Allocator.Persistent))
            using (var set = new RenderGraphTests.PotentiallyJobifiedNodeSet(computeType))
            {
                var root = set.Create<RenderPipe>();

                GraphValue<float> rootValue = set.CreateGraphValue(root, RenderPipe.KernelPorts.Output);

                for (int i = 0; i < 100; ++i)
                {
                    set.SendMessage(root, RenderPipe.SimulationPorts.Input, i);

                    set.Update();

                    GraphValueReadbackJob job;

                    job.Value = rootValue;
                    job.Result = results;
                    job.Resolver = set.GetGraphValueResolver(out var valueResolverDependency);

                    set.InjectDependencyFromConsumer(job.Schedule(valueResolverDependency));

                    // Automatically fences before CopyWorlds. Results is accessible now.
                    set.Update();

                    Assert.AreEqual(i, results[0]);
                    Assert.AreEqual(i, set.GetValueBlocking(rootValue));
                }

                set.Destroy(root);
                set.ReleaseGraphValue(rootValue);
            }
        }

        public struct Aggregate
        {
            public int OriginalInput;
            public Buffer<int> InputPlusOneI;
        }

        public class RenderPipeAggregate
            : KernelNodeDefinition<RenderPipeAggregate.Ports>
        {
            public struct Ports : IKernelPortDefinition
            {
                public DataInput<RenderPipeAggregate, int> Input;
                public DataOutput<RenderPipeAggregate, Aggregate> Output;
                public PortArray<DataOutput<RenderPipeAggregate, Aggregate>> OutputArray;
            }

            struct KernelData : IKernelData { }

            [BurstCompile(CompileSynchronously = true)]
            struct Kernel : IGraphKernel<KernelData, Ports>
            {
                public void Execute(RenderContext ctx, in KernelData data, ref Ports ports)
                {
                    var input = ctx.Resolve(ports.Input);
                    ctx.Resolve(ref ports.Output).OriginalInput = input;
                    var buffer = ctx.Resolve(ref ports.Output).InputPlusOneI.ToNative(ctx);

                    for (int i = 0; i < buffer.Length; ++i)
                    {
                        buffer[i] = input + 1 + i;
                    }
                }
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        struct GraphAggregateReadbackJob : IJob
        {
            public GraphValueResolver Resolver;
            public NativeArray<int> Result;
            public GraphValue<Aggregate> Value;

            public void Execute()
            {
                var aggr = Resolver.Resolve(Value);
                var buffer = aggr.InputPlusOneI.ToNative(Resolver);

                Result[0] = aggr.OriginalInput;

                for (int i = 1; i < Result.Length; ++i)
                    Result[i] = buffer[i - 1];
            }
        }

        [TestCase(0), TestCase(1), TestCase(5), TestCase(500)]
        public void CanUseGraphValueResolver_ToResolveAggregate_WithBuffers_InAJob(int bufferLength)
        {
            using (var results = new NativeArray<int>(bufferLength + 1, Allocator.Persistent))
            using (var set = new RenderGraphTests.PotentiallyJobifiedNodeSet(NodeSet.RenderExecutionModel.MaximallyParallel))
            {
                var root = set.Create<RenderPipeAggregate>();

                GraphValue<Aggregate> rootValue = set.CreateGraphValue(root, RenderPipeAggregate.KernelPorts.Output);

                for (int i = 0; i < 20; ++i)
                {
                    set.SetData(root, RenderPipeAggregate.KernelPorts.Input, i);

                    Aggregate aggr = default;
                    aggr.InputPlusOneI = Buffer<int>.SizeRequest(bufferLength);
                    set.SetBufferSize(root, RenderPipeAggregate.KernelPorts.Output, aggr);

                    set.Update();

                    GraphAggregateReadbackJob job;

                    job.Value = rootValue;
                    job.Result = results;
                    job.Resolver = set.GetGraphValueResolver(out var valueResolverDependency);

                    set.InjectDependencyFromConsumer(job.Schedule(valueResolverDependency));

                    // Automatically fences before CopyWorlds. Results is accessible now.
                    set.Update();

                    for (int z = 0; z < bufferLength + 1; ++z)
                        Assert.AreEqual(i + z, results[z]);
                }

                set.Destroy(root);
                set.ReleaseGraphValue(rootValue);
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        struct GraphBufferSizeReadbackJob : IJob
        {
            public GraphValueResolver Resolver;
            public NativeArray<int> Result;
            public GraphValue<Aggregate> Value;

            public void Execute()
            {
                Result[0] = Resolver.Resolve(Value).InputPlusOneI.ToNative(Resolver).Length;
            }
        }

        [Test]
        public void UpdatedBufferSize_InAggregate_ReflectsInDependent_ReadbackJob()
        {
            const int k_MaxBufferLength = 20;

            using (var results = new NativeArray<int>(1, Allocator.Persistent))
            using (var set = new RenderGraphTests.PotentiallyJobifiedNodeSet(NodeSet.RenderExecutionModel.MaximallyParallel))
            {
                var root = set.Create<RenderPipeAggregate>();

                GraphValue<Aggregate> rootValue = set.CreateGraphValue(root, RenderPipeAggregate.KernelPorts.Output);

                // Test increasing buffer sizes, then decreasing
                foreach (var i in Enumerable.Range(0, k_MaxBufferLength).Concat(Enumerable.Range(0, k_MaxBufferLength).Reverse()))
                {
                    Aggregate aggr = default;
                    aggr.InputPlusOneI = Buffer<int>.SizeRequest(i);
                    set.SetBufferSize(root, RenderPipeAggregate.KernelPorts.Output, aggr);

                    set.Update();

                    GraphBufferSizeReadbackJob job;

                    job.Value = rootValue;
                    job.Result = results;
                    job.Resolver = set.GetGraphValueResolver(out var valueResolverDependency);

                    set.InjectDependencyFromConsumer(job.Schedule(valueResolverDependency));

                    // Automatically fences before CopyWorlds. Results is accessible now.
                    set.Update();

                    Assert.AreEqual(i, results[0], "Buffer size mismatch between expected and actual");
                }

                set.Destroy(root);
                set.ReleaseGraphValue(rootValue);
            }
        }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        struct CheckReadOnlyNess_OfResolvedGraphBuffer : IJob
        {
            public GraphValueResolver Resolver;
            public NativeArray<int> Result;
            public GraphValue<Aggregate> Value;

            public void Execute()
            {
                var aggr = Resolver.Resolve(Value);
                var buffer = aggr.InputPlusOneI.ToNative(Resolver);

                Result[0] = 0;

                try
                {
                    buffer[0] = 1;
                }
                catch (IndexOutOfRangeException)
                {

                }
                catch
                {
                    Result[0] = 1;
                }
            }
        }

        [Test]
        public void ResolvedGraphBuffers_AreReadOnly()
        {
            using (var results = new NativeArray<int>(1, Allocator.Persistent))
            using (var set = new RenderGraphTests.PotentiallyJobifiedNodeSet(NodeSet.RenderExecutionModel.MaximallyParallel))
            {
                var root = set.Create<RenderPipeAggregate>();

                var rootValue = set.CreateGraphValue(root, RenderPipeAggregate.KernelPorts.Output);

                Aggregate aggr = default;
                aggr.InputPlusOneI = Buffer<int>.SizeRequest(1);
                set.SetBufferSize(root, RenderPipeAggregate.KernelPorts.Output, aggr);

                set.Update();

                CheckReadOnlyNess_OfResolvedGraphBuffer job;

                job.Value = rootValue;
                job.Result = results;
                job.Resolver = set.GetGraphValueResolver(out var valueResolverDependency);

                set.InjectDependencyFromConsumer(job.Schedule(valueResolverDependency));

                set.Update();

                Assert.AreEqual(1, results[0]);

                set.Destroy(root);
                set.ReleaseGraphValue(rootValue);
            }
        }

        struct CheckReadOnlyNess_OfResolvedGraphValueArray : IJob
        {
            public GraphValueResolver Resolver;
            public NativeArray<int> Result;
            public GraphValueArray<Aggregate> Value;

            public void Execute()
            {
                var array = Resolver.Resolve(Value);

                Result[0] = 0;

                try
                {
                    array[0] = new Aggregate();
                }
                catch (IndexOutOfRangeException)
                {
                }
                catch
                {
                    Result[0] = 1;
                }
            }
        }

        [Test]
        public void ResolvedGraphValueArrays_AreReadOnly()
        {
            using (var results = new NativeArray<int>(1, Allocator.Persistent))
            using (var set = new RenderGraphTests.PotentiallyJobifiedNodeSet(NodeSet.RenderExecutionModel.MaximallyParallel))
            {
                var root = set.Create<RenderPipeAggregate>();

                var rootValue = set.CreateGraphValueArray(root, RenderPipeAggregate.KernelPorts.OutputArray);

                set.SetPortArraySize(root, RenderPipeAggregate.KernelPorts.OutputArray, 1);

                set.Update();

                CheckReadOnlyNess_OfResolvedGraphValueArray job;

                job.Value = rootValue;
                job.Result = results;
                job.Resolver = set.GetGraphValueResolver(out var valueResolverDependency);

                set.InjectDependencyFromConsumer(job.Schedule(valueResolverDependency));

                set.Update();

                Assert.AreEqual(1, results[0]);

                set.Destroy(root);
                set.ReleaseGraphValueArray(rootValue);
            }
        }

        [Test]
        public void CanResolveGraphValues_OnMainThread_AfterFencing_ResolverDependencies()
        {
            const int k_Size = 5;

            using (var set = new RenderGraphTests.PotentiallyJobifiedNodeSet(NodeSet.RenderExecutionModel.MaximallyParallel))
            {
                var root = set.Create<RenderPipeAggregate>();

                var portArrayValue = set.CreateGraphValueArray(root, RenderPipeAggregate.KernelPorts.OutputArray);
                set.SetPortArraySize(root, RenderPipeAggregate.KernelPorts.OutputArray, k_Size);

                var rootValue = set.CreateGraphValue(root, RenderPipeAggregate.KernelPorts.Output);

                Aggregate aggr = default;
                aggr.InputPlusOneI = Buffer<int>.SizeRequest(k_Size);
                set.SetBufferSize(root, RenderPipeAggregate.KernelPorts.Output, aggr);

                set.Update();

                var resolver = set.GetGraphValueResolver(out var valueResolverDependency);
                valueResolverDependency.Complete();

                var renderGraphAggr = /* ref readonly */ resolver.Resolve(rootValue);
                var array = renderGraphAggr.InputPlusOneI.ToNative(resolver);

                Assert.AreEqual(k_Size, array.Length);
                int readback = 0;
                Assert.DoesNotThrow(() => readback = array[k_Size - 1]);

                var renderPortArray = resolver.Resolve(portArrayValue);
                Assert.AreEqual(k_Size, renderPortArray.Length);
                Assert.DoesNotThrow(() => aggr = renderPortArray[k_Size - 1]);

                // After this, secondary invalidation should make all operations impossible on current resolver
                // and anything that has been resolved from it
                set.Update();

                UtilityAssert.ThrowsEither<InvalidOperationException, ObjectDisposedException>(() => resolver.Resolve(portArrayValue));
                UtilityAssert.ThrowsEither<InvalidOperationException, ObjectDisposedException>(() => aggr = renderPortArray[k_Size - 1]);

                UtilityAssert.ThrowsEither<InvalidOperationException, ObjectDisposedException>(() => resolver.Resolve(rootValue));
                UtilityAssert.ThrowsEither<InvalidOperationException, ObjectDisposedException>(() => renderGraphAggr.InputPlusOneI.ToNative(resolver));
                UtilityAssert.ThrowsEither<InvalidOperationException, ObjectDisposedException>(() => readback = array[k_Size - 1]);

                set.Destroy(root);
                set.ReleaseGraphValue(rootValue);
                set.ReleaseGraphValueArray(portArrayValue);
            }
        }

        [Test]
        public void CanResolveMultipleGraphValues_InSameNodeSetUpdate()
        {
            using (var set = new RenderGraphTests.PotentiallyJobifiedNodeSet(NodeSet.RenderExecutionModel.MaximallyParallel))
            {
                var node1 = set.Create<RenderPipeAggregate>();
                var node2 = set.Create<RenderPipeAggregate>();

                GraphValue<Aggregate> gv1 = set.CreateGraphValue(node1, RenderPipeAggregate.KernelPorts.Output);
                GraphValue<Aggregate> gv2 = set.CreateGraphValue(node2, RenderPipeAggregate.KernelPorts.Output);

                set.Update();

                var job1 =
                    new NullJob { Resolver = set.GetGraphValueResolver(out var valueResolverDependency1) }
                        .Schedule(valueResolverDependency1);
                set.InjectDependencyFromConsumer(job1);

                var job2 =
                    new NullJob { Resolver = set.GetGraphValueResolver(out var valueResolverDependency2) }
                        .Schedule(valueResolverDependency2);
                set.InjectDependencyFromConsumer(job2);

                set.Update();

                set.Destroy(node1);
                set.Destroy(node2);
                set.ReleaseGraphValue(gv1);
                set.ReleaseGraphValue(gv2);
            }
        }

        public enum GraphValueResolverCreation
        {
            ImmediateAcquireAndReadOnMainThread,
            OneFrameStale,
            NonInitialized
        }

        [Test]
        public void CannotResolveCreatedGraphValue_UsingGraphValueResolver_InEdgeCases([Values] GraphValueResolverCreation creationMode)
        {
            using (var set = new RenderGraphTests.PotentiallyJobifiedNodeSet(NodeSet.RenderExecutionModel.MaximallyParallel))
            {
                var root = set.Create<RenderPipeAggregate>();
                GraphValue<Aggregate> rootValue = set.CreateGraphValue(root, RenderPipeAggregate.KernelPorts.Output);

                set.Update();

                switch (creationMode)
                {
                    case GraphValueResolverCreation.ImmediateAcquireAndReadOnMainThread:
                    {
                        var resolver = set.GetGraphValueResolver(out var valueResolverDependency);

                        Assert.Throws<InvalidOperationException>(
                            () =>
                            {
                                /*
                                 * System.InvalidOperationException : The previously scheduled job AtomicSafetyManager:ProtectOutputBuffersFromDataFlowGraph writes to the UNKNOWN_OBJECT_TYPE ProtectOutputBuffersFromDataFlowGraph.WritableDataFlowGraphScope.
                                 * You must call JobHandle.Complete() on the job AtomicSafetyManager:ProtectOutputBuffersFromDataFlowGraph, before you can read from the UNKNOWN_OBJECT_TYPE safely.
                                 */
                                var portContents = resolver.Resolve(rootValue);
                            }
                        );
                        break;
                    }

                    case GraphValueResolverCreation.OneFrameStale:
                    {
                        var resolver = set.GetGraphValueResolver(out var valueResolverDependency);
                        set.Update();
                        // This particular step fences on AtomicSafetyManager:ProtectOutputBuffersFromDataFlowGraph,
                        // leaving access to the resolver technically fine on the main thread (from the job system's point of view),
                        // however the resolver has a copy of old state (potentially reallocated blit lists, indeterministically invalid graph values).

                        valueResolverDependency.Complete();

                        UtilityAssert.ThrowsEither<InvalidOperationException, ObjectDisposedException>(
                            () =>
                            {
                                /*
                                 * System.InvalidOperationException : The UNKNOWN_OBJECT_TYPE has been deallocated, it is not allowed to access it
                                 */
                                var portContents = resolver.Resolve(rootValue);
                            }
                        );
                        break;
                    }

                    case GraphValueResolverCreation.NonInitialized:
                    {
                        Assert.Throws<ObjectDisposedException>(
                            () =>
                            {
                                /*
                                 * System.ObjectDisposedException : Cannot access a disposed object.
                                 * Object name: 'BufferProtectionScope not initialized'.
                                 */
                                var portContents = new GraphValueResolver().Resolve(rootValue);
                            }
                        );

                        break;
                    }

                    default:
                        throw new ArgumentOutOfRangeException();
                }

                set.Update();

                set.Destroy(root);
                set.ReleaseGraphValue(rootValue);
            }
        }

        struct NullJob : IJob
        {
            public GraphValueResolver Resolver;

            public void Execute()
            {
            }
        }

        [Test]
        public void ForgettingToPassJobHandle_BackIntoNodeSet_ThrowsDeferredException()
        {
            if (!JobsUtility.JobDebuggerEnabled)
                Assert.Ignore("JobsDebugger is disabled");

            using (var set = new RenderGraphTests.PotentiallyJobifiedNodeSet(NodeSet.RenderExecutionModel.MaximallyParallel))
            {
                var root = set.Create<RenderPipeAggregate>();

                // Since this is created after an update, it won't be valid in a graph value resolver until next update.
                GraphValue<Aggregate> rootValue = set.CreateGraphValue(root, RenderPipeAggregate.KernelPorts.Output);

                set.Update();

                NullJob job;

                job.Resolver = set.GetGraphValueResolver(out var valueResolverDependency);

                var dependency = job.Schedule(valueResolverDependency);

                /*
                 * System.InvalidOperationException : The previously scheduled job GraphValueResolverTests:NullJob reads from the Unity.Collections.NativeList`1[Unity.DataFlowGraph.DataOutputValue] NullJob.Resolver.Values.
                 * You must call JobHandle.Complete() on the job GraphValueResolverTests:NullJob, before you can write to the Unity.Collections.NativeList`1[Unity.DataFlowGraph.DataOutputValue] safely.
                 */
                Assert.Throws<InvalidOperationException>(() => set.Update());

                // Intended usage
                Assert.DoesNotThrow(() => set.InjectDependencyFromConsumer(dependency));
                set.Update();

                set.Destroy(root);
                set.ReleaseGraphValue(rootValue);
            }
        }

        [Test]
        public void ForgettingToPassJobHandle_IntoScheduledGraphResolverJob_ThrowsImmediateException()
        {
            if (!JobsUtility.JobDebuggerEnabled)
                Assert.Ignore("JobsDebugger is disabled");

            using (var set = new RenderGraphTests.PotentiallyJobifiedNodeSet(NodeSet.RenderExecutionModel.MaximallyParallel))
            {
                var root = set.Create<RenderPipeAggregate>();

                GraphValue<Aggregate> rootValue = set.CreateGraphValue(root, RenderPipeAggregate.KernelPorts.Output);

                set.Update();

                NullJob job;

                job.Resolver = set.GetGraphValueResolver(out var valueResolverDependency);

                /*
                 * System.InvalidOperationException : The previously scheduled job AtomicSafetyManager:ProtectOutputBuffersFromDataFlowGraph writes to the UNKNOWN_OBJECT_TYPE ProtectOutputBuffersFromDataFlowGraph.WritableDataFlowGraphScope.
                 * You are trying to schedule a new job GraphValueResolverTests:NullJob, which reads from the same UNKNOWN_OBJECT_TYPE (via NullJob.Resolver.ReadBuffersScope).
                 * To guarantee safety, you must include AtomicSafetyManager:ProtectOutputBuffersFromDataFlowGraph as a dependency of the newly scheduled job.
                 */
                Assert.Throws<InvalidOperationException>(() => job.Schedule());

                // Intended usage
                Assert.DoesNotThrow(() => job.Schedule(valueResolverDependency).Complete());

                set.Destroy(root);
                set.ReleaseGraphValue(rootValue);
            }
        }

#endif

        enum InvalidGraphValueResult
        {
            NotExecuted = 0,
            ExecutedNoError,
            ExecutedCaughtDisposedException,
            ExecutedCaughtIndexOutOfRangeException,
            ExecutedCaughtUnexpectedException
        }

        struct CheckGraphValueValidity : IJob
        {
            public GraphValueResolver Resolver;
            public NativeArray<InvalidGraphValueResult> Result;
            public GraphValue<Aggregate> Value;

            public void Execute()
            {
                Result[0] = InvalidGraphValueResult.ExecutedNoError;

                try
                {
                    var aggr = Resolver.Resolve(Value);
                }
                catch (ObjectDisposedException)
                {
                    Result[0] = InvalidGraphValueResult.ExecutedCaughtDisposedException;
                }
                catch
                {
                    Result[0] = InvalidGraphValueResult.ExecutedCaughtUnexpectedException;
                }
            }
        }

        struct CheckGraphValueArrayValidity : IJob
        {
            public GraphValueResolver Resolver;
            public NativeArray<int> SizeResult;
            public NativeArray<InvalidGraphValueResult> Result;
            public GraphValueArray<Aggregate> Value;

            public void Execute()
            {
                Result[0] = InvalidGraphValueResult.ExecutedNoError;

                try
                {
                    SizeResult[0] = Resolver.Resolve(Value).Length;
                    var aggr = Resolver.Resolve(Value)[0];
                }
                catch (IndexOutOfRangeException)
                {
                    Result[0] = InvalidGraphValueResult.ExecutedCaughtIndexOutOfRangeException;
                }
                catch
                {
                    Result[0] = InvalidGraphValueResult.ExecutedCaughtUnexpectedException;
                }
            }
        }

        [Test]
        public void GraphValuesCreatedPostRender_DoNotResolveAfterScheduling_InTheSameFrame()
        {
            using (var results = new NativeArray<InvalidGraphValueResult>(1, Allocator.Persistent))
            using (var set = new RenderGraphTests.PotentiallyJobifiedNodeSet(NodeSet.RenderExecutionModel.MaximallyParallel))
            {
                var root = set.Create<RenderPipeAggregate>();

                set.Update();
                // Since this is created after an update, it won't be valid in a graph value resolver until next update.
                GraphValue<Aggregate> rootValue = set.CreateGraphValue(root, RenderPipeAggregate.KernelPorts.Output);

                CheckGraphValueValidity job;

                job.Value = rootValue;
                job.Result = results;
                job.Resolver = set.GetGraphValueResolver(out var valueResolverDependency);

                set.InjectDependencyFromConsumer(job.Schedule(valueResolverDependency));

                // Fences injected dependencies, so we can read result directly
                set.Update();

                Assert.AreEqual(InvalidGraphValueResult.ExecutedCaughtDisposedException, results[0]);

                set.Destroy(root);
                set.ReleaseGraphValue(rootValue);
            }
        }

        [Test]
        public void GraphValueArrayResolver_ReturnType_HasCorrectSize()
        {
            using (var results = new NativeArray<InvalidGraphValueResult>(1, Allocator.Persistent))
            using (var sizeResult = new NativeArray<int>(1, Allocator.Persistent))
            using (var set = new RenderGraphTests.PotentiallyJobifiedNodeSet(NodeSet.RenderExecutionModel.MaximallyParallel))
            {
                var root = set.Create<RenderPipeAggregate>();

                set.SetPortArraySize(root, RenderPipeAggregate.KernelPorts.OutputArray, 1);
                var rootValue = set.CreateGraphValueArray(root, RenderPipeAggregate.KernelPorts.OutputArray);

                set.Update();

                CheckGraphValueArrayValidity job;

                job.Value = rootValue;
                job.Result = results;
                job.SizeResult = sizeResult;
                job.Resolver = set.GetGraphValueResolver(out var valueResolverDependency);

                set.InjectDependencyFromConsumer(job.Schedule(valueResolverDependency));

#if ENABLE_UNITY_COLLECTIONS_CHECKS // We rely on bounds checking for these tests
                set.SetPortArraySize(root, RenderPipeAggregate.KernelPorts.OutputArray, 0);
#endif

                // Fences injected dependencies, so we can read result directly
                set.Update();

                Assert.AreEqual(1, sizeResult[0]);
                Assert.AreEqual(InvalidGraphValueResult.ExecutedNoError, results[0]);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                job.Value = rootValue;
                job.Result = results;
                job.Resolver = set.GetGraphValueResolver(out var secondResolverDependency);

                set.InjectDependencyFromConsumer(job.Schedule(secondResolverDependency));

                set.SetPortArraySize(root, RenderPipeAggregate.KernelPorts.OutputArray, 1);

                // Fences injected dependencies, so we can read result directly
                set.Update();

                Assert.AreEqual(0, sizeResult[0]);
                Assert.AreEqual(InvalidGraphValueResult.ExecutedCaughtIndexOutOfRangeException, results[0]);

                job.Value = rootValue;
                job.Result = results;
                job.Resolver = set.GetGraphValueResolver(out var thirdResolverDependency);

                set.InjectDependencyFromConsumer(job.Schedule(thirdResolverDependency));

                // Fences injected dependencies, so we can read result directly
                set.Update();

                Assert.AreEqual(1, sizeResult[0]);
                Assert.AreEqual(InvalidGraphValueResult.ExecutedNoError, results[0]);
#endif

                set.Destroy(root);
                set.ReleaseGraphValueArray(rootValue);
            }
        }

        [Test]
        public void PostUpdateDisposedGraphValue_FailsToResolveInSimulation_ButStillResolves_InRenderGraph_ForOneFrame()
        {
            using (var results = new NativeArray<InvalidGraphValueResult>(1, Allocator.Persistent))
            using (var set = new RenderGraphTests.PotentiallyJobifiedNodeSet(NodeSet.RenderExecutionModel.MaximallyParallel))
            {
                var root = set.Create<RenderPipeAggregate>();
                // Create before update - it is valid
                GraphValue<Aggregate> rootValue = set.CreateGraphValue(root, RenderPipeAggregate.KernelPorts.Output);

                set.Update();

                // But dispose after update.
                set.ReleaseGraphValue(rootValue);
                // Render graph only gets this notification next update,
                // so the graph value should still be readable inside the render graph, just not in the simulation.
                Assert.Throws<ArgumentException>(() => set.GetValueBlocking(rootValue));

                CheckGraphValueValidity job;

                job.Value = rootValue;
                job.Result = results;
                job.Resolver = set.GetGraphValueResolver(out var valueResolverDependency);

                set.InjectDependencyFromConsumer(job.Schedule(valueResolverDependency));

                // Fences injected dependencies, so we can read result directly
                set.Update();

                Assert.AreEqual(InvalidGraphValueResult.ExecutedNoError, results[0]);

                job.Value = rootValue;
                job.Result = results;
                job.Resolver = set.GetGraphValueResolver(out var secondResolverDependency);

                set.InjectDependencyFromConsumer(job.Schedule(secondResolverDependency));

                // Fences injected dependencies, so we can read result directly
                set.Update();

                Assert.AreEqual(InvalidGraphValueResult.ExecutedCaughtDisposedException, results[0]);

                set.Destroy(root);
            }
        }

        [Test]
        public void PostDeletedGraphValueTargetNode_FailsToResolveInSimulation_ButStillResolves_InRenderGraph_ForOneFrame()
        {
            using (var results = new NativeArray<InvalidGraphValueResult>(1, Allocator.Persistent))
            using (var set = new RenderGraphTests.PotentiallyJobifiedNodeSet(NodeSet.RenderExecutionModel.MaximallyParallel))
            {
                var root = set.Create<RenderPipeAggregate>();
                // Create before update - it is valid
                GraphValue<Aggregate> rootValue = set.CreateGraphValue(root, RenderPipeAggregate.KernelPorts.Output);

                set.Update();

                // But dispose node target after update.
                set.Destroy(root);
                // Render graph only gets this notification next update,
                // so the graph value and node should still be readable inside the render graph, just not in the simulation.
                Assert.Throws<ObjectDisposedException>(() => set.GetValueBlocking(rootValue));

                CheckGraphValueValidity job;

                job.Value = rootValue;
                job.Result = results;
                job.Resolver = set.GetGraphValueResolver(out var valueResolverDependency);

                set.InjectDependencyFromConsumer(job.Schedule(valueResolverDependency));

                // Fences injected dependencies, so we can read result directly
                set.Update();

                Assert.AreEqual(InvalidGraphValueResult.ExecutedNoError, results[0]);

                job.Value = rootValue;
                job.Result = results;
                job.Resolver = set.GetGraphValueResolver(out var secondResolverDependency);

                set.InjectDependencyFromConsumer(job.Schedule(secondResolverDependency));

                // Fences injected dependencies, so we can read result directly
                set.Update();

                Assert.AreEqual(InvalidGraphValueResult.ExecutedCaughtDisposedException, results[0]);

                set.ReleaseGraphValue(rootValue);
            }
        }

    }
}
