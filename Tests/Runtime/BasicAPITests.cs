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
        class TestNode : NodeDefinition<TestNode.Node>
        {
            public struct Node : INodeData { }
        }

        class TestNode2 : NodeDefinition<TestNode2.Node>
        {
            public struct Node : INodeData { }
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
                    set.GetFunctionality(a),
                    set.LookupDefinition<TestNode2>().Functionality
                );

                Assert.AreEqual(
                    set.GetFunctionality(b),
                    set.LookupDefinition<TestNode>().Functionality
                );

                Assert.AreEqual(
                    set.GetFunctionality(c),
                    set.LookupDefinition<TestNode2>().Functionality
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
        public void LeaksOf_Nodes_AreReported()
        {
            using (var set = new NodeSet())
            {
                LogAssert.Expect(LogType.Error, new Regex("NodeSet leak warnings: "));
                set.Create<TestNode>();
            }
        }

        [Test]
        public void Is_WillCorrectlyTestFunctionalityEquality()
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
        public void As_WillCorrectlyTestFunctionalityEquality()
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
        public void AcquiredFunctionality_MatchesNodeType()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<TestNode>();

                var supposedFunctionality = set.GetFunctionality(node);
                var expectedFunctionality = set.GetFunctionality<TestNode>();

                Assert.AreEqual(supposedFunctionality, expectedFunctionality);

                set.Destroy(node);
            }
        }

        [Test]
        public void AcquiredFunctionality_ThrowsOnDestroyed_AndDefaultConstructed_Nodes()
        {
            using (var set = new NodeSet())
            {
                var node = new NodeHandle();
                Assert.Throws<ArgumentException>(() => set.GetFunctionality(node));

                node = set.Create<TestNode>();
                set.Destroy(node);

                Assert.Throws<ArgumentException>(() => set.GetFunctionality(node));
            }
        }

        [Test]
        public void CanInstantiateFunctionality_WithoutHavingCreatedMatchingNode()
        {
            using (var set = new NodeSet())
            {
                Assert.IsNotNull(set.GetFunctionality<TestNode>());
            }
        }

        [Test]
        public void SetInjection_IsPerformedCorrectly_InFunctionality()
        {
            using (var set = new NodeSet())
            {
                Assert.AreEqual(set, set.GetFunctionality<TestNode>().Set);
            }
        }

        class TestException : System.Exception { }

        class ExceptionInConstructor : NodeDefinition<ExceptionInConstructor.Node>
        {
            public struct Node : INodeData { }

            public override void Init(InitContext ctx)
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

        class ExceptionInDestructor : NodeDefinition<ExceptionInDestructor.Node>
        {
            public struct Node : INodeData { }

            public override void Destroy(NodeHandle handle)
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
