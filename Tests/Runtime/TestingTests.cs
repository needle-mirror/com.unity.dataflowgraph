using System;
using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.DataFlowGraph.Tests
{
    public class TestingTests
    {
        [Test]
        public void CanRun_SimulationTest_ForNode()
        {
            bool wasRun = false;
            using (var set = new NodeSet())
            {
                var node = set.Create<EmptyNodeAndData>();
                set.SendTest(node, (EmptyNodeAndData.EmptyData _) => wasRun = true);
                set.Destroy(node);
            }
            Assert.True(wasRun);
        }

        [Test]
        public void CanRun_SimulationTest_FromInsideSimulationTest()
        {
            bool wasRun = false;
            using (var set = new NodeSet())
            {
                var node = set.Create<EmptyNodeAndData>();
                set.SendTest<EmptyNodeAndData.EmptyData>(node,
                    ctx => ctx.SendTest(node, (EmptyNodeAndData.EmptyData _) => wasRun = true));
                set.Destroy(node);
            }
            Assert.True(wasRun);
        }

        [Test]
        public void CannotEnqueue_SimulationTest_ForNonExistentNode()
        {
            using (var set = new NodeSet())
            {
                Assert.Throws<ArgumentException>(
                    () => set.SendTest(default, (EmptyNodeAndData.EmptyData _) => {}));
            }
        }

        [Test]
        public void CannotEnqueue_SimulationTest_ForDestroyedNode()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<EmptyNodeAndData>();
                set.Destroy(node);
                Assert.Throws<ArgumentException>(
                    () => set.SendTest(node, (EmptyNodeAndData.EmptyData _) => {}));
            }
        }

        [Test]
        public void CannotEnqueue_SimulationTest_ForNode_WithNoData()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<EmptyNode>();
                // Note, SendTest function signature requires an INodeData instance be provided, so this test ends
                // up being similar to the one checking for matching INodeData types.
                Assert.Throws<InvalidOperationException>(
                    () => set.SendTest(node, (EmptyNodeAndData.EmptyData _) => {}));
                set.Destroy(node);
            }
        }

        [Test]
        public void CannotEnqueue_SimulationTest_ForNode_WithIncorrectDataType()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<PassthroughTest<int>>();
                Assert.Throws<InvalidOperationException>(
                    () => set.SendTest(node, (EmptyNodeAndData.EmptyData _) => {}));
                set.Destroy(node);
            }
        }

        [Test]
        public void TestExceptions_CanBeCollected_AfterNextUpdate()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<EmptyNodeAndData>();

                set.SendTest(node, (EmptyNodeAndData.EmptyData _) => Assert.Fail());
                set.Update();
                Assert.Throws<NUnit.Framework.AssertionException>(() => set.ThrowCollectedTestException());

                set.Destroy(node);
            }
        }

        [Test]
        public void TestExceptions_CannotBeCollected_BeforeCallingUpdate()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<EmptyNodeAndData>();

                set.SendTest(node, (EmptyNodeAndData.EmptyData _) => Assert.Fail());
                Assert.DoesNotThrow(() => set.ThrowCollectedTestException());

                set.Update();
                set.Destroy(node);
                Assert.Throws<NUnit.Framework.AssertionException>(() => set.ThrowCollectedTestException());
            }
        }

        [Test]
        public void TestExceptions_AreLogged_OnNodeSetDispose()
        {
            var set = new NodeSet();
            var node = set.Create<EmptyNodeAndData>();

            set.SendTest(node, (EmptyNodeAndData.EmptyData _) => Assert.Fail());
            set.SendTest(node, (EmptyNodeAndData.EmptyData _) => throw new WarningException());

            set.Destroy(node);

            LogAssert.Expect(LogType.Error, new Regex("2 pending test exception"));
            LogAssert.Expect(LogType.Exception, "AssertionException");
            LogAssert.Expect(LogType.Exception, new Regex("WarningException"));

            set.Dispose();
        }

        [Test]
        public void TestExceptions_AreRethrown_InOrder()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<EmptyNodeAndData>();

                set.SendTest(node, (EmptyNodeAndData.EmptyData _) => Assert.Fail());
                set.SendTest(node, (EmptyNodeAndData.EmptyData _) => throw new WarningException());
                set.SendTest(node, (EmptyNodeAndData.EmptyData _) => throw new InvalidOperationException());

                set.Update();

                Assert.Throws<NUnit.Framework.AssertionException>(() => set.ThrowCollectedTestException());
                Assert.Throws<WarningException>(() => set.ThrowCollectedTestException());
                Assert.Throws<InvalidOperationException>(() => set.ThrowCollectedTestException());
                Assert.DoesNotThrow(() => set.ThrowCollectedTestException());

                set.Destroy(node);
            }
        }

        [Test]
        public void TestExceptions_AreCarriedForward_ByUpdate_IfUncollected()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<EmptyNodeAndData>();

                set.SendTest(node, (EmptyNodeAndData.EmptyData _) => Assert.Fail());

                set.Update();

                set.SendTest(node, (EmptyNodeAndData.EmptyData _) => throw new WarningException());

                set.Update();

                Assert.Throws<NUnit.Framework.AssertionException>(() => set.ThrowCollectedTestException());
                Assert.Throws<WarningException>(() => set.ThrowCollectedTestException());
                Assert.DoesNotThrow(() => set.ThrowCollectedTestException());

                set.Destroy(node);
            }
        }

        [Test]
        public void UncollectedTestExceptions_AreCarriedForward_ByUpdate()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<EmptyNodeAndData>();

                set.SendTest(node, (EmptyNodeAndData.EmptyData _) => Assert.Fail());
                set.SendTest(node, (EmptyNodeAndData.EmptyData _) => throw new WarningException());

                set.Update();

                Assert.Throws<NUnit.Framework.AssertionException>(() => set.ThrowCollectedTestException());

                set.Update();

                Assert.Throws<WarningException>(() => set.ThrowCollectedTestException());
                Assert.DoesNotThrow(() => set.ThrowCollectedTestException());

                set.Destroy(node);
            }
        }

        [Test]
        public void OriginalTestException_IsRethrown()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<EmptyNodeAndData>();

                Exception capturedException = default;
                set.SendTest(node, (EmptyNodeAndData.EmptyData _) =>
                {
                    try
                    {
                        Assert.Fail();
                    }
                    catch (Exception e)
                    {
                        capturedException = e;
                        throw;
                    }
                });

                set.Update();

                bool didCatchException = false;
                try
                {
                    set.ThrowCollectedTestException();
                }
                catch (Exception e)
                {
                    Assert.AreEqual(capturedException, e);
                    didCatchException = true;
                }
                Assert.True(didCatchException);

                set.Destroy(node);
            }
        }

        // This is essentially a prototype demonstrating one possible approach to how we can support NodeSet.SendTest
        // in a future where simulation is run asynchronously.
        unsafe struct TestDelegateJob<TNodeData> : IJob, IDisposable
            where TNodeData : struct, INodeData
        {
            struct TestDelegateJobData
            {
                public SimulationTestFunctionWithContext<TNodeData> Fn;
                public NodeSet Set;
                public NodeHandle Handle;
                public ExceptionDispatchInfo Exception;

                public void Execute()
                {
                    try
                    {
                        Fn(new SimulationTestContext<TNodeData>(Set, Handle));
                    }
                    catch (Exception e)
                    {
                        Exception = ExceptionDispatchInfo.Capture(e);
                    }
                }
            }

            [NativeDisableUnsafePtrRestriction]
            void* DataPtr;
            ulong GCHandle;

            public void Execute()
            {
                UnsafeUtility.AsRef<TestDelegateJobData>(DataPtr).Execute();
            }

            public TestDelegateJob(SimulationTestFunctionWithContext<TNodeData> testDelegate, NodeSet set, NodeHandle handle)
            {
                var pinned = new TestDelegateJobData();
                DataPtr = UnsafeUtility.PinGCObjectAndGetAddress(pinned, out GCHandle);
                UnsafeUtility.AsRef<TestDelegateJobData>(DataPtr) =
                    new TestDelegateJobData {Fn = testDelegate, Set = set, Handle = handle};
            }

            public void RethrowCaughtException()
            {
                UnsafeUtility.AsRef<TestDelegateJobData>(DataPtr).Exception?.Throw();
            }

            public void Dispose()
            {
                UnsafeUtility.ReleaseGCObject(GCHandle);
            }
        }

        [Test]
        public void CanInvoke_TestFunction_ThroughJob()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<EmptyNodeAndData>();

                SimulationTestFunctionWithContext<EmptyNodeAndData.EmptyData> testDel =
                    (ctx) => { Assert.Fail(); };

                using (var job = new TestDelegateJob<EmptyNodeAndData.EmptyData>(testDel, set, node))
                {
                    var jobHandle = job.Schedule();
                    jobHandle.Complete();
                    Assert.Throws<NUnit.Framework.AssertionException>(() => job.RethrowCaughtException());
                }

                set.Destroy(node);
            }
        }
    }
}
