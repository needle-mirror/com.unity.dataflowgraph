using System;
using System.Linq;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using static Unity.DataFlowGraph.Tests.AtomicSafetyManagerTests;
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
        public void CanUseGraphValueResolver_ToResolveValues_InAJob([Values] NodeSet.RenderExecutionModel computeType, [Values] KernelUpdateMode updateMode)
        {
            using (var results = new NativeArray<float>(1, Allocator.Persistent))
            using (var set = new RenderGraphTests.PotentiallyJobifiedNodeSet(computeType))
            {
                var root = set.Create<RenderPipe>();

                GraphValue<float> rootValue = set.CreateGraphValue(root, RenderPipe.KernelPorts.Output);

                for (int i = 0; i < 100; ++i)
                {
                    set.SendMessage(root, RenderPipe.SimulationPorts.Input, new FloatButWithMode { Value = i, Mode = updateMode });

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
            : NodeDefinition<RenderPipeAggregate.KernelData, RenderPipeAggregate.Ports, RenderPipeAggregate.Kernel>
        {
            public struct Ports : IKernelPortDefinition
            {
                public DataInput<RenderPipeAggregate, int> Input;
                public DataOutput<RenderPipeAggregate, Aggregate> Output;
            }

            public struct KernelData : IKernelData
            {
            }

            [BurstCompile(CompileSynchronously = true)]
            public struct Kernel : IGraphKernel<KernelData, Ports>
            {
                public void Execute(RenderContext ctx, KernelData data, ref Ports ports)
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

                GraphValue<Aggregate> rootValue = set.CreateGraphValue(root, RenderPipeAggregate.KernelPorts.Output);

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

        [Test]
        public void CanResolveGraphValues_OnMainThread_AfterFencing_ResolverDependencies()
        {
            const int k_BufferSize = 5;

            using (var set = new RenderGraphTests.PotentiallyJobifiedNodeSet(NodeSet.RenderExecutionModel.MaximallyParallel))
            {
                var root = set.Create<RenderPipeAggregate>();

                GraphValue<Aggregate> rootValue = set.CreateGraphValue(root, RenderPipeAggregate.KernelPorts.Output);

                Aggregate aggr = default;
                aggr.InputPlusOneI = Buffer<int>.SizeRequest(k_BufferSize);
                set.SetBufferSize(root, RenderPipeAggregate.KernelPorts.Output, aggr);

                set.Update();

                var resolver = set.GetGraphValueResolver(out var valueResolverDependency);
                valueResolverDependency.Complete();

                var renderGraphAggr = /* ref readonly */ resolver.Resolve(rootValue);
                var array = renderGraphAggr.InputPlusOneI.ToNative(resolver);

                Assert.AreEqual(k_BufferSize, array.Length);
                int readback = 0;
                Assert.DoesNotThrow(() => readback = array[k_BufferSize - 1]);

                // After this, secondary invalidation should make all operations impossible on current resolver
                // and anything that has been resolved from it
                set.Update();

                UtilityAssert.ThrowsEither<InvalidOperationException, ObjectDisposedException>(() => resolver.Resolve(rootValue));
                UtilityAssert.ThrowsEither<InvalidOperationException, ObjectDisposedException>(() => readback = array[k_BufferSize - 1]);

                set.Destroy(root);
                set.ReleaseGraphValue(rootValue);
            }
        }

        public class StallingAggregateNode
            : NodeDefinition<StallingAggregateNode.KernelData, StallingAggregateNode.Ports, StallingAggregateNode.Kernel>
        {
            public static bool Wait { get => WaitingStaticFlagStructure.Wait; set => WaitingStaticFlagStructure.Wait = value; }
            public static bool Done { get => WaitingStaticFlagStructure.Done; set => WaitingStaticFlagStructure.Done = value; }
            public static void Reset() => WaitingStaticFlagStructure.Reset();

            public struct Ports : IKernelPortDefinition
            {
                public DataOutput<StallingAggregateNode, Aggregate> Output;
            }

            public struct KernelData : IKernelData { }

            public struct Kernel : IGraphKernel<KernelData, Ports>
            {
                public void Execute(RenderContext ctx, KernelData data, ref Ports ports)
                {
                    WaitingStaticFlagStructure.Execute();
                }
            }
        }

        [Test]
        public void CanResolveMultipleGraphValues_InSameNodeSetUpdate()
        {
            StallingAggregateNode.Reset();

            using (var set = new RenderGraphTests.PotentiallyJobifiedNodeSet(NodeSet.RenderExecutionModel.MaximallyParallel))
            {
                var node1 = set.Create<StallingAggregateNode>();
                var node2 = set.Create<StallingAggregateNode>();

                GraphValue<Aggregate> gv1 = set.CreateGraphValue(node1, StallingAggregateNode.KernelPorts.Output);
                GraphValue<Aggregate> gv2 = set.CreateGraphValue(node1, StallingAggregateNode.KernelPorts.Output);

                set.Update();
                Assume.That(StallingAggregateNode.Done, Is.False);

                var job1 =
                    new NullJob { Resolver = set.GetGraphValueResolver(out var valueResolverDependency1) }
                        .Schedule(valueResolverDependency1);
                set.InjectDependencyFromConsumer(job1);

                var job2 =
                    new NullJob { Resolver = set.GetGraphValueResolver(out var valueResolverDependency2) }
                        .Schedule(valueResolverDependency2);
                set.InjectDependencyFromConsumer(job2);

                StallingAggregateNode.Wait = false;
                set.Update();
                Assert.True(StallingAggregateNode.Done);

                set.Destroy(node1);
                set.Destroy(node2);
                set.ReleaseGraphValue(gv1);
                set.ReleaseGraphValue(gv2);
            }
        }

        public enum GraphValueResolverCreation
        {
            // Indeterministic: See issue #477
            // ImmediateAcquireAndReadOnMainThread,
            OneFrameStale,
            NonInitialized
        }

        [Test]
        public void CannotResolveCreatedGraphValue_UsingGraphValueResolver_InEdgeCases([Values] GraphValueResolverCreation creationMode)
        {
            StallingAggregateNode.Reset();

            using (var set = new RenderGraphTests.PotentiallyJobifiedNodeSet(NodeSet.RenderExecutionModel.MaximallyParallel))
            {
                var root = set.Create<StallingAggregateNode>();
                GraphValue<Aggregate> rootValue = set.CreateGraphValue(root, StallingAggregateNode.KernelPorts.Output);

                set.Update();
                Assume.That(StallingAggregateNode.Done, Is.False);

                switch (creationMode)
                {
                    /* Indeterministic: See issue #477
                     *
                     * case GraphValueResolverCreation.ImmediateAcquireAndReadOnMainThread:
                    {
                        var resolver = set.GetGraphValueResolver(out var valueResolverDependency);

                        Assert.Throws<InvalidOperationException>(
                            () =>
                            {
                                // The previously scheduled job AtomicSafetyManager:ProtectOutputBuffersFromDataFlowGraph writes to the NativeArray ProtectOutputBuffersFromDataFlowGraph.WritableDataFlowGraphScope.
                                // You must call JobHandle.Complete() on the job AtomicSafetyManager:ProtectOutputBuffersFromDataFlowGraph, before you can read from the NativeArray safely.
                                var portContents = resolver.Resolve(rootValue);
                            }
                        );
                        break;
                    }*/

                    case GraphValueResolverCreation.OneFrameStale:
                    {
                        var resolver = set.GetGraphValueResolver(out var valueResolverDependency);
                        StallingAggregateNode.Wait = false;
                        set.Update();
                        // This particular step fences on AtomicSafetyManager:ProtectOutputBuffersFromDataFlowGraph,
                        // leaving access to the resolver technically fine on the main thread (from the job system's point of view),
                        // however the resolver has a copy of old state (potentially reallocated blit lists, indeterministically invalid graph values).

                        valueResolverDependency.Complete();

                        UtilityAssert.ThrowsEither<InvalidOperationException, ObjectDisposedException>(
                            () =>
                            {
                            /**
                                * System.InvalidOperationException: The NativeArray has been deallocated, it is not allowed to access it
                                * */
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
                            /**
                                * ObjectDisposedException: GraphValueResolver.BufferProtectionScope is not initialized
                                * */
                                var portContents = new GraphValueResolver().Resolve(rootValue);
                            }
                        );

                        break;
                    }

                    default:
                        throw new ArgumentOutOfRangeException();
                }

                StallingAggregateNode.Wait = false;
                set.Update();
                Assert.True(StallingAggregateNode.Done);

                set.Destroy(root);
                set.ReleaseGraphValue(rootValue);
            }
        }

        struct StallJob : IJob
        {
            public static bool Wait { get => WaitingStaticFlagStructure.Wait; set => WaitingStaticFlagStructure.Wait = value; }
            public static bool Done { get => WaitingStaticFlagStructure.Done; set => WaitingStaticFlagStructure.Done = value; }
            public static void Reset() => WaitingStaticFlagStructure.Reset();

            public GraphValueResolver Resolver;

            public void Execute()
            {
                WaitingStaticFlagStructure.Execute();
            }
        }

        [Test]
        public void ForgettingToPassJobHandle_BackIntoNodeSet_ThrowsDeferredException()
        {
            if (!JobsUtility.JobDebuggerEnabled)
                Assert.Ignore("JobsDebugger is disabled");

            StallJob.Reset();

            using (var set = new RenderGraphTests.PotentiallyJobifiedNodeSet(NodeSet.RenderExecutionModel.MaximallyParallel))
            {
                var root = set.Create<RenderPipeAggregate>();

                // Since this is created after an update, it won't be valid in a graph value resolver until next update.
                GraphValue<Aggregate> rootValue = set.CreateGraphValue(root, RenderPipeAggregate.KernelPorts.Output);

                set.Update();

                StallJob job;

                job.Resolver = set.GetGraphValueResolver(out var valueResolverDependency);

                var dependency = job.Schedule(valueResolverDependency);

                // (Intended usage:)
                // set.InjectDependencyFromConsumer(job.Schedule(valueResolverDependency));
                Assume.That(StallJob.Done, Is.False);

                /*
                 * System.InvalidOperationException : The previously scheduled job GraphValueResolverTests:StallJob reads from the NativeArray StallJob.Resolver.Values.
                 * You must call JobHandle.Complete() on the job GraphValueResolverTests:StallJob, before you can write to the NativeArray safely.
                 */
                Assert.Throws<InvalidOperationException>(() => set.Update());

                StallJob.Wait = false;

                set.InjectDependencyFromConsumer(dependency);
                set.Update();
                Assert.True(StallJob.Done);

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
        public void ForgettingToPassJobHandle_IntoScheduledGraphResolverJob_ThrowsImmediateException()
        {
            if (!JobsUtility.JobDebuggerEnabled)
                Assert.Ignore("JobsDebugger is disabled");

            StallingAggregateNode.Reset();

            using (var set = new RenderGraphTests.PotentiallyJobifiedNodeSet(NodeSet.RenderExecutionModel.MaximallyParallel))
            {
                var root = set.Create<StallingAggregateNode>();

                GraphValue<Aggregate> rootValue = set.CreateGraphValue(root, StallingAggregateNode.KernelPorts.Output);

                set.Update();
                Assume.That(StallingAggregateNode.Done, Is.False);

                NullJob job;

                job.Resolver = set.GetGraphValueResolver(out var valueResolverDependency);

                // (Intended usage:)
                // job.Schedule(valueResolverDependency).Complete()
                /*
                 * System.InvalidOperationException : The previously scheduled job AtomicSafetyManager:ProtectOutputBuffersFromDataFlowGraph writes to the NativeArray ProtectOutputBuffersFromDataFlowGraph.WritableDataFlowGraphScope.
                 * You are trying to schedule a new job GraphValueResolverTests:NullJob, which reads from the same NativeArray (via StallJob.Resolver.ReadBuffersScope).
                 * To guarantee safety, you must include AtomicSafetyManager:ProtectOutputBuffersFromDataFlowGraph as a dependency of the newly scheduled job.
                 */
                Assert.Throws<InvalidOperationException>(() => job.Schedule().Complete());
                StallingAggregateNode.Wait = false;

                set.Destroy(root);
                set.ReleaseGraphValue(rootValue);
            }

            Assert.True(StallingAggregateNode.Done);
        }

#endif

        enum InvalidGraphValueResult
        {
            NotExecuted = 0,
            ExecutedNoError,
            ExecutedCaughtDisposedException,
            ExecutedCaughtArgumentException,
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
                catch (ArgumentException)
                {
                    Result[0] = InvalidGraphValueResult.ExecutedCaughtArgumentException;
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
