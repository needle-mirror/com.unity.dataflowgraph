using System;
using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.DataFlowGraph.Tests
{
    public class BasicAPITests
    {
        class TestNode : NodeDefinition<EmptyPorts> {}

        class TestNode2 : NodeDefinition<EmptyPorts> {}
        class TestNode_WithThrowingConstructor : NodeDefinition<EmptyPorts>
        {
            public TestNode_WithThrowingConstructor() => throw new NotImplementedException();
        }

        [Test]
        public void CanCreate_NodeSet()
        {
            using (var set = new NodeSet())
            {
            }
        }

        [Test]
        public void CanCreate_Node_InExistingSet()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<TestNode>();
                Assert.IsTrue(set.Exists(node));
                set.Destroy(node);
            }
        }

        [Test]
        public void Nodes_OnlyExist_InOneNodeSet()
        {
            using (var set = new NodeSet())
            {
                NodeHandle node = set.Create<TestNode>();
                Assert.IsTrue(set.Exists(node));
                using (var altSet = new NodeSet())
                {
                    Assert.IsFalse(altSet.Exists(node));
                }
                set.Destroy(node);
            }
        }

        [Test]
        public void Nodes_AreOnlyValid_InOneNodeSet()
        {
            using (var set = new NodeSet())
            {
                NodeHandle node = set.Create<TestNode>();
                Assert.DoesNotThrow(() => set.Validate(node));
                using (var altSet = new NodeSet())
                {
                    Assert.Throws<ArgumentException>(() => altSet.Validate(node));
                }
                set.Destroy(node);
            }
        }

        [Test]
        public void NodeSetPropagatesExceptions_FromNonInstantiableNodeDefinition()
        {
            using (var set = new NodeSet())
            {
                // As Create<T> actually uses reflection, the concrete exception type is not thrown
                // but conditionally wrapped inside some other target load framework exception type...
                // More info: https://devblogs.microsoft.com/premier-developer/dissecting-the-new-constraint-in-c-a-perfect-example-of-a-leaky-abstraction/
                bool somethingWasCaught = false;

                try
                {
                    set.Create<TestNode_WithThrowingConstructor>();
                }
                catch
                {
                    somethingWasCaught = true;
                }

                Assert.True(somethingWasCaught);
            }
        }

        [Test]
        public void DefaultInitializedHandle_DoesNotExist_InSet()
        {
            using (var set = new NodeSet())
            {
                Assert.IsFalse(set.Exists(new NodeHandle()));
            }
        }

        [Test]
        public void DefaultInitializedHandle_DoesNotExist_InSet_AfterCreatingOneNode_ThatWouldOccupySameIndex()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<TestNode>();
                Assert.IsFalse(set.Exists(new NodeHandle()));
                set.Destroy(node);
            }
        }

        [Test]
        public void CreatedNode_DoesNotExist_AfterBeingDestructed()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<TestNode>();
                Assert.IsTrue(set.Exists(node));
                set.Destroy(node);
                Assert.IsFalse(set.Exists(node));
            }
        }


        [Test]
        public void SetReturnsCorrectClass_ForCreatedNodes()
        {
            using (var set = new NodeSet())
            {
                var a = set.Create<TestNode2>();
                var b = set.Create<TestNode>();
                var c = set.Create<TestNode2>();

                Assert.AreEqual(
                    set.GetDefinition(a),
                    set.LookupDefinition<TestNode2>().Definition
                );

                Assert.AreEqual(
                    set.GetDefinition(b),
                    set.LookupDefinition<TestNode>().Definition
                );

                Assert.AreEqual(
                    set.GetDefinition(c),
                    set.LookupDefinition<TestNode2>().Definition
                );

                set.Destroy(a, b, c);

            }
        }

        // TODO: Indeterministic and destroys other tests due NativeArray error messages that are printed in random order.
        [Test, Explicit]
        public void LeaksOf_NodeSets_AreReported()
        {
            new NodeSet();

            LogAssert.Expect(LogType.Error, "Leaked NodeSet - remember to call .Dispose() on it!");
            GC.Collect();
            // TODO: Indeterministic, need a better way of catching these logs
            Thread.Sleep(1000);
        }

        [Test]
        public void NodeSetDisposition_CanBeQueried()
        {
            var set = new NodeSet();
            Assert.IsFalse(set.IsDisposed());
            set.Dispose();
            Assert.IsTrue(set.IsDisposed());
        }

        [Test]
        public void LeaksOf_Nodes_AreReported()
        {
            using (var set = new NodeSet())
            {
                LogAssert.Expect(LogType.Error, new Regex("NodeSet leak warnings: "));
                set.Create<TestNode>();
            }
        }

        [Test]
        public void Is_WillCorrectlyTestDefinitionEquality()
        {
            using (var set = new NodeSet())
            {
                var handle = set.Create<TestNode2>();

                Assert.IsFalse(set.Is<TestNode>(handle));
                Assert.IsTrue(set.Is<TestNode2>(handle));

                set.Destroy(handle);
            }
        }

        [Test]
        public void Is_ThrowsOnInvalidAndDestroyedNode()
        {
            using (var set = new NodeSet())
            {
                Assert.Throws<ArgumentException>(() => set.Is<TestNode>(new NodeHandle()));

                var handle = set.Create<TestNode>();
                set.Destroy(handle);

                Assert.Throws<ArgumentException>(() => set.Is<TestNode>(handle));
            }
        }

        [Test]
        public void CastHandle_ThrowsOnInvalidAndDestroyedNode()
        {
            using (var set = new NodeSet())
            {
                Assert.Throws<ArgumentException>(() => set.CastHandle<TestNode>(new NodeHandle()));

                var handle = set.Create<TestNode>();
                set.Destroy(handle);

                Assert.Throws<ArgumentException>(() => set.CastHandle<TestNode>(handle));
            }
        }

        [Test]
        public void CastHandle_WorksForUntypedHandle()
        {
            using (var set = new NodeSet())
            {
                NodeHandle handle = set.Create<TestNode2>();

                Assert.DoesNotThrow(() => set.CastHandle<TestNode2>(handle));
                Assert.Throws<InvalidCastException>(() => set.CastHandle<TestNode>(handle));

                set.Destroy(handle);
            }
        }

        [Test]
        public void As_WillCorrectlyTestDefinitionEquality()
        {
            using (var set = new NodeSet())
            {
                var handle = set.Create<TestNode2>();

                Assert.IsFalse(set.As<TestNode>(handle) is NodeHandle<TestNode> unused1);
                Assert.IsTrue(set.As<TestNode2>(handle) is NodeHandle<TestNode2> unused2);

                set.Destroy(handle);
            }
        }

        [Test]
        public void As_ThrowsOnInvalidAndDestroyedNode()
        {
            using (var set = new NodeSet())
            {
                Assert.Throws<ArgumentException>(() => set.As<TestNode>(new NodeHandle()));

                var handle = set.Create<TestNode>();
                set.Destroy(handle);

                Assert.Throws<ArgumentException>(() => set.As<TestNode>(handle));
            }
        }

        [Test]
        public void AcquiredDefinition_MatchesNodeType()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<TestNode>();

                var supposedDefinition = set.GetDefinition(node);
                var expectedDefinition = set.GetDefinition<TestNode>();

                Assert.AreEqual(supposedDefinition, expectedDefinition);

                set.Destroy(node);
            }
        }

        [Test]
        public void AcquiredDefinition_ThrowsOnDestroyed_AndDefaultConstructed_Nodes()
        {
            using (var set = new NodeSet())
            {
                var node = new NodeHandle();
                Assert.Throws<ArgumentException>(() => set.GetDefinition(node));

                node = set.Create<TestNode>();
                set.Destroy(node);

                Assert.Throws<ArgumentException>(() => set.GetDefinition(node));
            }
        }

        [Test]
        public void CanInstantiateDefinition_WithoutHavingCreatedMatchingNode()
        {
            using (var set = new NodeSet())
            {
                Assert.IsNotNull(set.GetDefinition<TestNode>());
            }
        }

        [Test]
        public void SetInjection_IsPerformedCorrectly_InDefinition()
        {
            using (var set = new NodeSet())
            {
                Assert.AreEqual(set, set.GetDefinition<TestNode>().Set);
            }
        }

        class TestException : System.Exception { }

        class ExceptionInConstructor : NodeDefinition<EmptyPorts>
        {
            protected internal override void Init(InitContext ctx)
            {
                throw new TestException();
            }
        }

        [Test]
        public void ThrowingExceptionFromConstructor_IsNotified_AsUndefinedBehaviour()
        {
            using (var set = new NodeSet())
            {
                LogAssert.Expect(LogType.Error, new Regex("Throwing exceptions from constructors is undefined behaviour"));
                Assert.Throws<TestException>(() => set.Create<ExceptionInConstructor>());
            }
        }

        [Test]
        public void CanDestroy_DuringConstruction()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<DelegateMessageIONode>((InitContext ctx) => set.Destroy(ctx.Handle));
                Assert.IsFalse(set.Exists(node));
            }
        }

        class ExceptionInDestructor : NodeDefinition<EmptyPorts>
        {
            protected internal override void Destroy(NodeHandle handle)
            {
                throw new TestException();
            }
        }


        [Test]
        public void ThrowingExceptionsFromDestructors_IsNotified_AsUndefinedBehaviour()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<ExceptionInDestructor>();

                LogAssert.Expect(LogType.Error, new Regex("Undefined behaviour when throwing exceptions from destructors"));
                LogAssert.Expect(LogType.Exception, new Regex("TestException"));
                set.Destroy(node);
            }
        }
    }
}
