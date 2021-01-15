using System;
using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.DataFlowGraph.Tests
{
    public class BasicAPITests
    {
        [IsNotInstantiable]
        class TestNode_WithThrowingConstructor : SimulationNodeDefinition<TestNode_WithThrowingConstructor.EmptyPorts>
        {
            public struct EmptyPorts : ISimulationPortDefinition { }

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
                var node = set.Create<EmptyNode>();
                Assert.IsTrue(set.Exists(node));
                set.Destroy(node);
            }
        }

        [Test]
        public void Nodes_OnlyExist_InOneNodeSet()
        {
            using (var set = new NodeSet())
            {
                NodeHandle node = set.Create<EmptyNode>();
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
                NodeHandle node = set.Create<EmptyNode>();
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
                var node = set.Create<EmptyNode>();
                Assert.IsFalse(set.Exists(new NodeHandle()));
                set.Destroy(node);
            }
        }

        [Test]
        public void CreatedNode_DoesNotExist_AfterBeingDestructed()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<EmptyNode>();
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
                var a = set.Create<EmptyNode2>();
                var b = set.Create<EmptyNode>();
                var c = set.Create<EmptyNode2>();

                Assert.AreEqual(
                    set.GetDefinition(a),
                    set.LookupDefinition<EmptyNode2>().Definition
                );

                Assert.AreEqual(
                    set.GetDefinition(b),
                    set.LookupDefinition<EmptyNode>().Definition
                );

                Assert.AreEqual(
                    set.GetDefinition(c),
                    set.LookupDefinition<EmptyNode2>().Definition
                );

                set.Destroy(a, b, c);

            }
        }

        // TODO: Indeterministic and destroys other tests due NativeArray error messages that are printed in random order.
        [Test, Explicit]
        public void LeaksOf_NodeSets_AreReported()
        {
            new NodeSet();

            LogAssert.Expect(LogType.Error, "A Native Collection has not been disposed, resulting in a memory leak");
            GC.Collect();
            // TODO: Indeterministic, need a better way of catching these logs
            Thread.Sleep(1000);
        }

        [Test]
        public void NodeSetDisposition_CanBeQueried()
        {
            var set = new NodeSet();
            Assert.IsTrue(set.IsCreated);
            set.Dispose();
            Assert.IsFalse(set.IsCreated);
        }

        [Test]
        public void LeaksOf_Nodes_AreReported()
        {
            using (var set = new NodeSet())
            {
                LogAssert.Expect(LogType.Error, new Regex("NodeSet leak warnings: "));
                set.Create<EmptyNode>();
            }
        }

        [Test]
        public void Is_WillCorrectlyTestDefinitionEquality()
        {
            using (var set = new NodeSet())
            {
                var handle = set.Create<EmptyNode2>();

                Assert.IsFalse(set.Is<EmptyNode>(handle));
                Assert.IsTrue(set.Is<EmptyNode2>(handle));

                set.Destroy(handle);
            }
        }

        [Test]
        public void Is_ThrowsOnInvalidAndDestroyedNode()
        {
            using (var set = new NodeSet())
            {
                Assert.Throws<ArgumentException>(() => set.Is<EmptyNode>(new NodeHandle()));

                var handle = set.Create<EmptyNode>();
                set.Destroy(handle);

                Assert.Throws<ArgumentException>(() => set.Is<EmptyNode>(handle));
            }
        }

        [Test]
        public void CastHandle_ThrowsOnInvalidAndDestroyedNode()
        {
            using (var set = new NodeSet())
            {
                Assert.Throws<ArgumentException>(() => set.CastHandle<EmptyNode>(new NodeHandle()));

                var handle = set.Create<EmptyNode>();
                set.Destroy(handle);

                Assert.Throws<ArgumentException>(() => set.CastHandle<EmptyNode>(handle));
            }
        }

        [Test]
        public void CastHandle_WorksForUntypedHandle()
        {
            using (var set = new NodeSet())
            {
                NodeHandle handle = set.Create<EmptyNode2>();

                Assert.DoesNotThrow(() => set.CastHandle<EmptyNode2>(handle));
                Assert.Throws<InvalidCastException>(() => set.CastHandle<EmptyNode>(handle));

                set.Destroy(handle);
            }
        }

        [Test]
        public void As_WillCorrectlyTestDefinitionEquality()
        {
            using (var set = new NodeSet())
            {
                var handle = set.Create<EmptyNode2>();

                Assert.IsFalse(set.As<EmptyNode>(handle) is NodeHandle<EmptyNode> unused1);
                Assert.IsTrue(set.As<EmptyNode2>(handle) is NodeHandle<EmptyNode2> unused2);

                set.Destroy(handle);
            }
        }

        [Test]
        public void As_ThrowsOnInvalidAndDestroyedNode()
        {
            using (var set = new NodeSet())
            {
                Assert.Throws<ArgumentException>(() => set.As<EmptyNode>(new NodeHandle()));

                var handle = set.Create<EmptyNode>();
                set.Destroy(handle);

                Assert.Throws<ArgumentException>(() => set.As<EmptyNode>(handle));
            }
        }

        [Test]
        public void AcquiredDefinition_MatchesNodeType()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<EmptyNode>();

                var supposedDefinition = set.GetDefinition(node);
                var expectedDefinition = set.GetDefinition<EmptyNode>();

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

                node = set.Create<EmptyNode>();
                set.Destroy(node);

                Assert.Throws<ArgumentException>(() => set.GetDefinition(node));
            }
        }

        [Test]
        public void CanInstantiateDefinition_WithoutHavingCreatedMatchingNode()
        {
            using (var set = new NodeSet())
            {
                Assert.IsNotNull(set.GetDefinition<EmptyNode>());
            }
        }

        [Test]
        public void SetInjection_IsPerformedCorrectly_InDefinition()
        {
            using (var set = new NodeSet())
            {
                Assert.AreEqual(set, set.GetDefinition<EmptyNode>().Set);
            }
        }

        class TestException : System.Exception { }

        [IsNotInstantiable]
        class ExceptionInConstructor : SimulationNodeDefinition<ExceptionInConstructor.EmptyPorts>
        {
            public struct EmptyPorts : ISimulationPortDefinition { }

            struct Node : INodeData, IInit
            {
                public void Init(InitContext ctx)
                {
                    throw new TestException();
                }
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

        [IsNotInstantiable]
        class ExceptionInDestructor : SimulationNodeDefinition<ExceptionInDestructor.EmptyPorts>
        {
            public struct EmptyPorts : ISimulationPortDefinition { }

            struct Node : INodeData, IDestroy
            {
                public void Destroy(DestroyContext ctx)
                {
                    throw new TestException();
                }
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

        public class DestroyHandler : SimulationNodeDefinition<DestroyHandler.MyPorts>
        {
            public struct MyPorts : ISimulationPortDefinition { }

            internal struct NodeData : INodeData, IDestroy
            {
                public static bool Called;

                public void Destroy(DestroyContext ctx)
                {
                    Called = true;
                }
            }
        }

        [Test]
        public void CanCall_CodeGenerated_DestroyHandler()
        {
            DestroyHandler.NodeData.Called = false;

            using (var set = new NodeSet())
            {
                var node = set.Create<DestroyHandler>();
                Assert.False(DestroyHandler.NodeData.Called);
                set.Destroy(node);
                Assert.True(DestroyHandler.NodeData.Called);
            }

            DestroyHandler.NodeData.Called = false;
        }

        public class CommonContextTestNode : SimulationKernelNodeDefinition<CommonContextTestNode.SimPorts, CommonContextTestNode.KernelDefs>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
                public MessageInput<CommonContextTestNode, int> In;
                public MessageOutput<CommonContextTestNode, int> Out;
                public PortArray<MessageOutput<CommonContextTestNode, int>> ArrayOut;
            }

            public struct KernelDefs : IKernelPortDefinition { }

            struct Node : INodeData, IMsgHandler<int>, IUpdate
            {
                public void Update(UpdateContext ctx)
                    => throw new System.NotImplementedException();

                public void HandleMessage(MessageContext ctx, in int msg)
                    => throw new NotImplementedException();
            }

            internal struct EmptyKernelData : IKernelData { }

            internal struct Kernel : IGraphKernel<EmptyKernelData, KernelDefs>
            {
                public void Execute(RenderContext ctx, in EmptyKernelData data, ref KernelDefs ports) { }
            }
        }

        [Test]
        public void Contexts_CanBeCast_ToCommonContext()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<CommonContextTestNode>();
                set.SetPortArraySize(node, CommonContextTestNode.SimulationPorts.ArrayOut, 3);

                void TestCommonContext(CommonContext ctx)
                {
                    Assert.AreEqual(ctx.Set.NodeSetID, set.NodeSetID);
                    Assert.AreEqual(ctx.Handle, (NodeHandle)node);

                    Assert.DoesNotThrow(() => ctx.EmitMessage(CommonContextTestNode.SimulationPorts.Out, 22));
                    Assert.DoesNotThrow(() => ctx.EmitMessage(CommonContextTestNode.SimulationPorts.ArrayOut, 2, 22));
                    Assert.DoesNotThrow(() => ctx.UpdateKernelBuffers(new CommonContextTestNode.Kernel()));
                    Assert.DoesNotThrow(() => ctx.UpdateKernelData(new CommonContextTestNode.EmptyKernelData()));
                    Assert.DoesNotThrow(() => ctx.RegisterForUpdate());
                    Assert.DoesNotThrow(() => ctx.RemoveFromUpdate());
                }

                BlitList<ForwardedPort.Unchecked> ports = default;
                TestCommonContext(new InitContext(set.Validate(node), NodeDefinitionTypeIndex<CommonContextTestNode>.Index, set, ref ports));

                TestCommonContext(new UpdateContext(set, set.Validate(node)));

                TestCommonContext(new MessageContext(set, new InputPair(set, node, new InputPortArrayID((InputPortID)CommonContextTestNode.SimulationPorts.In))));

                set.Destroy(node);
            }
        }
    }
}
