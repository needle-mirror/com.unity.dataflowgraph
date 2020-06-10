using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Burst;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.DataFlowGraph.Tests
{
    public class GraphValueTests
    {
        public enum GraphValueType
        {
            Typed,
            Untyped
        }

        public struct Data : INodeData
        {

        }

        public struct KernelData : IKernelData
        {
            public float Input;
        }

        public class RenderPipe
            : NodeDefinition<Data, RenderPipe.SimPorts, KernelData, RenderPipe.Ports, RenderPipe.Kernel>
            , IMsgHandler<float>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
                public MessageInput<RenderPipe, float> Input;
            }

            public struct Ports : IKernelPortDefinition
            {
                public DataOutput<RenderPipe, float> Output;
            }

            [BurstCompile(CompileSynchronously = true)]
            public struct Kernel : IGraphKernel<KernelData, Ports>
            {
                public void Execute(RenderContext ctx, KernelData data, ref Ports ports)
                {
                    ctx.Resolve(ref ports.Output) = data.Input;
                }
            }

            public void HandleMessage(in MessageContext ctx, in float msg)
            {
                GetKernelData(ctx.Handle).Input = msg;
            }
        }

        public class RenderAdder
            : NodeDefinition<Data, RenderAdder.SimPorts, KernelData, RenderAdder.Ports, RenderAdder.Kernel>
            , IMsgHandler<float>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
                public MessageInput<RenderAdder, float> Scale;
            }

            public struct Ports : IKernelPortDefinition
            {
                public DataInput<RenderAdder, float> Input;
                public DataOutput<RenderAdder, float> Output;
            }

            [BurstCompile(CompileSynchronously = true)]
            public struct Kernel : IGraphKernel<KernelData, Ports>
            {
                public void Execute(RenderContext ctx, KernelData data, ref Ports ports)
                {
                    ctx.Resolve(ref ports.Output) = ctx.Resolve(ports.Input) * data.Input;
                }
            }

            public void HandleMessage(in MessageContext ctx, in float msg)
            {
                GetKernelData(ctx.Handle).Input = msg;
            }

        }

        [Test]
        public void CanCreate_GraphValueFromKernelPort()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<RenderPipe>();
                var value = set.CreateGraphValue(node, RenderPipe.KernelPorts.Output);

                set.Destroy(node);
                set.ReleaseGraphValue(value);
            }
        }

        [Test]
        public void LeakingGraphValue_WritesDiagnosticError()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<RenderPipe>();
                var value = set.CreateGraphValue(node, RenderPipe.KernelPorts.Output);

                set.Destroy(node);

                LogAssert.Expect(LogType.Error, new Regex("leaked graph value"));
            }
        }

        [Test]
        public void DefaultGraphValue_IsReportedAsInvalid()
        {
            using (var set = new NodeSet())
            {
                GraphValue<int> value = default;
                Assert.IsFalse(set.ValueExists(value));
            }
        }

        [Test]
        public void FreshlyCreatedGraphValue_IsReportedAsValid()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<RenderPipe>();
                var value = set.CreateGraphValue(node, RenderPipe.KernelPorts.Output);
                Assert.IsTrue(set.ValueExists(value));

                set.Destroy(node);
                set.ReleaseGraphValue(value);
            }
        }

        [Test]
        public void GraphValue_IsReportedAsValid_AfterNodeDestruction()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<RenderPipe>();
                var value = set.CreateGraphValue(node, RenderPipe.KernelPorts.Output);

                set.Destroy(node);

                Assert.IsTrue(set.ValueExists(value));

                set.ReleaseGraphValue(value);
            }
        }

        [Test]
        public void GraphValue_IsReportedAsInvalid_AfterRelease()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<RenderPipe>();
                var value = set.CreateGraphValue(node, RenderPipe.KernelPorts.Output);

                set.ReleaseGraphValue(value);

                Assert.IsFalse(set.ValueExists(value));

                set.Destroy(node);
            }
        }

        [Test]
        public void AccessingGraphValueAPIs_WithDefaultValue_ThrowsException()
        {
            using (var set = new NodeSet())
            {
                Assert.Throws<ArgumentException>(() => set.CreateGraphValue(new NodeHandle<RenderPipe>(), RenderPipe.KernelPorts.Output));
                Assert.Throws<ObjectDisposedException>(() => set.ReleaseGraphValue(new GraphValue<float>()));
                Assert.Throws<ObjectDisposedException>(() => set.GetValueBlocking(new GraphValue<float>()));
            }
        }

        [Test]
        public void CreatingGraphValue_WithDestroyedNode_ThrowsException()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<RenderPipe>();

                set.Destroy(node);
                Assert.Throws<ArgumentException>(() => set.CreateGraphValue(node, RenderPipe.KernelPorts.Output));
            }
        }

        [Test]
        public void AccessingGraphValueAPIs_WithDestroyedValue_ThrowsException()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<RenderPipe>();

                var value = set.CreateGraphValue(node, RenderPipe.KernelPorts.Output);
                set.ReleaseGraphValue(value);

                Assert.Throws<ObjectDisposedException>(() => set.GetValueBlocking(value));

                set.Destroy(node);
            }
        }

        [Test]
        public void AccessingGraphValueAPIs_WithDestroyedNode_ThrowsException()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<RenderPipe>();

                var value = set.CreateGraphValue(node, RenderPipe.KernelPorts.Output);
                set.Destroy(node);

                Assert.Throws<ObjectDisposedException>(() => set.GetValueBlocking(value));

                set.ReleaseGraphValue(value);
            }
        }

        [Test]
        public void UpdateNodeSet_WithDestroyedNode_ButAliveGraphValue_Works()
        {
            const int k_NumUpdates = 10;

            using (var set = new NodeSet())
            {
                var node = set.Create<RenderPipe>();

                var value = set.CreateGraphValue(node, RenderPipe.KernelPorts.Output);
                set.Destroy(node);

                for (int i = 0; i < k_NumUpdates; ++i)
                {
                    // Used to break inside FlushGraphValuesJob, since target node wasn't alive.
                    set.Update();
                }

                set.ReleaseGraphValue(value);
            }
        }

        [Test]
        public void CanSynchronouslyPumpValues_ThroughMessages_AndRetrieve_AfterUpdate_ThroughGraphValue([Values] NodeSet.RenderExecutionModel computeType)
        {
            using (var set = new RenderGraphTests.PotentiallyJobifiedNodeSet(computeType))
            {
                var node = set.Create<RenderPipe>();
                var value = set.CreateGraphValue(node, RenderPipe.KernelPorts.Output);

                for (int i = 1; i < 100; ++i)
                {
                    set.SendMessage(node, RenderPipe.SimulationPorts.Input, i);

                    set.Update();

                    var graphValue = set.GetValueBlocking(value);

                    Assert.AreEqual(i, graphValue);
                }

                set.Destroy(node);
                set.ReleaseGraphValue(value);
            }
        }

        [Test]
        public void CanSynchronouslyReadValues_InTreeStructure_AndRetrieve_AfterUpdate_ThroughGraphValue(
            [Values] NodeSet.RenderExecutionModel computeType,
            [Values] GraphValueType typedNess)
        {
            using (var set = new RenderGraphTests.PotentiallyJobifiedNodeSet(computeType))
            {
                var root = set.Create<RenderPipe>();
                var child1 = set.Create<RenderAdder>();
                var child2 = set.Create<RenderAdder>();

                GraphValue<float>
                    rootValue,
                    childValue1,
                    childValue2;

                if (typedNess == GraphValueType.Typed)
                {
                    rootValue = set.CreateGraphValue(root, RenderPipe.KernelPorts.Output);
                    childValue1 = set.CreateGraphValue(child1, RenderAdder.KernelPorts.Output);
                    childValue2 = set.CreateGraphValue(child2, RenderAdder.KernelPorts.Output);
                }
                else
                {
                    rootValue = set.CreateGraphValue<float>((NodeHandle)root, (OutputPortID)RenderPipe.KernelPorts.Output);
                    childValue1 = set.CreateGraphValue<float>((NodeHandle)child1, (OutputPortID)RenderAdder.KernelPorts.Output);
                    childValue2 = set.CreateGraphValue<float>((NodeHandle)child2, (OutputPortID)RenderAdder.KernelPorts.Output);
                }

                set.SendMessage(child1, RenderAdder.SimulationPorts.Scale, 10);
                set.SendMessage(child2, RenderAdder.SimulationPorts.Scale, 20);

                set.Connect(root, RenderPipe.KernelPorts.Output, child1, RenderAdder.KernelPorts.Input);
                set.Connect(root, RenderPipe.KernelPorts.Output, child2, RenderAdder.KernelPorts.Input);

                for (int i = 0; i < 100; ++i)
                {
                    set.SendMessage(root, RenderPipe.SimulationPorts.Input, i);

                    set.Update();

                    Assert.AreEqual(i, set.GetValueBlocking(rootValue));
                    Assert.AreEqual(i * 10, set.GetValueBlocking(childValue1));
                    Assert.AreEqual(i * 20, set.GetValueBlocking(childValue2));
                }

                set.Destroy(root, child1, child2);
                set.ReleaseGraphValue(rootValue);
                set.ReleaseGraphValue(childValue1);
                set.ReleaseGraphValue(childValue2);
            }
        }

        [Test]
        public void CannotCreateWeaklyTypedGraphValue_FromNonData_OutputPortID()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<NodeWithAllTypesOfPorts>();

                Assert.Throws<InvalidOperationException>(() => set.CreateGraphValue<int>((NodeHandle)node, (OutputPortID)NodeWithAllTypesOfPorts.SimulationPorts.MessageOut));

                set.Destroy(node);
            }
        }

        [Test]
        public void CannotCreateWeaklyTypedGraphValue_WithMismatchingType()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<NodeWithAllTypesOfPorts>();

                Assert.AreNotEqual(typeof(float), NodeWithAllTypesOfPorts.KernelPorts.OutputScalar.GetType().GetGenericArguments()[1]);

                Assert.Throws<InvalidOperationException>(() => set.CreateGraphValue<float>((NodeHandle)node, (OutputPortID)NodeWithAllTypesOfPorts.KernelPorts.OutputScalar));

                set.Destroy(node);
            }
        }
    }
}
