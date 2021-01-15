using System;
using System.Collections.Generic;
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

        public class RenderPipe
            : SimulationKernelNodeDefinition<RenderPipe.SimPorts, RenderPipe.Ports>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
                public MessageInput<RenderPipe, float> Input;
            }

            public struct Ports : IKernelPortDefinition
            {
                public DataOutput<RenderPipe, float> Output;
                public PortArray<DataOutput<RenderPipe, float>> OutputArray;
            }

            public struct KernelData : IKernelData
            {
                public float Input;
            }

            [BurstCompile(CompileSynchronously = true)]
            public struct Kernel : IGraphKernel<KernelData, Ports>
            {
                public void Execute(RenderContext ctx, in KernelData data, ref Ports ports)
                {
                    ctx.Resolve(ref ports.Output) = data.Input;
                    var portArray = ctx.Resolve(ref ports.OutputArray);
                    for(int p = 0; p < portArray.Length; ++p)
                        portArray[p] = data.Input + p;
                }
            }

            struct Node : INodeData, IMsgHandler<float>
            {
                public void HandleMessage(MessageContext ctx, in float msg)
                {
                    ctx.UpdateKernelData(new KernelData { Input = msg });
                }
            }
        }

        public class RenderAdder
            : SimulationKernelNodeDefinition<RenderAdder.SimPorts, RenderAdder.Ports>
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

            struct KernelData : IKernelData
            {
                public float Input;
            }

            [BurstCompile(CompileSynchronously = true)]
            struct Kernel : IGraphKernel<KernelData, Ports>
            {
                public void Execute(RenderContext ctx, in KernelData data, ref Ports ports)
                {
                    ctx.Resolve(ref ports.Output) = ctx.Resolve(ports.Input) * data.Input;
                }
            }

            struct Node : INodeData, IMsgHandler<float>
            {
                public void HandleMessage(MessageContext ctx, in float msg)
                {
                    ctx.UpdateKernelData(new KernelData {Input = msg});
                }
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
        public void CanCreate_GraphValueFromKernelPortArray()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<RenderPipe>();

                var value = set.CreateGraphValueArray(node, RenderPipe.KernelPorts.OutputArray);

                set.Destroy(node);
                set.ReleaseGraphValueArray(value);
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
                Assert.Throws<ArgumentException>(() => set.ValueTargetExists(value));

            }
        }

        [Test]
        public void FreshlyCreatedGraphValue_IsReportedAsValidIncludingTarget()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<RenderPipe>();
                var value = set.CreateGraphValue(node, RenderPipe.KernelPorts.Output);
                Assert.IsTrue(set.ValueExists(value));
                Assert.IsTrue(set.ValueTargetExists(value));

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
        public void GraphValueTarget_IsReportedAsInvalid_AfterNodeDestruction()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<RenderPipe>();
                var value = set.CreateGraphValue(node, RenderPipe.KernelPorts.Output);

                set.Destroy(node);

                Assert.False(set.ValueTargetExists(value));

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
                Assert.Throws<ArgumentException>(() => set.ReleaseGraphValue(new GraphValue<float>()));
                Assert.Throws<ArgumentException>(() => set.GetValueBlocking(new GraphValue<float>()));
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

                Assert.Throws<ArgumentException>(() => set.GetValueBlocking(value));

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
        public void CanResolveGraphValue_UsingBlockingAPI_BeforeInitialRoundtrip_AndAfter()
        {
            const int k_NumUpdates = 10;

            using (var set = new NodeSet())
            {
                var node = set.Create<RenderPipe>();
                var value = set.CreateGraphValue(node, RenderPipe.KernelPorts.Output);

                for (int i = 0; i < k_NumUpdates; ++i)
                {
                    Assert.AreEqual(default(float), set.GetValueBlocking(value));
                    set.Update();
                }

                set.Destroy(node);
                set.ReleaseGraphValue(value);
            }
        }

        [Test]
        public void GraphValue_OnAlreadyRoundtrippedNode_DefaultsValue_UntilAnotherUpdate()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<RenderPipe>();
                set.SendMessage(node, RenderPipe.SimulationPorts.Input, 3.14f);
                set.Update();

                var value = set.CreateGraphValue(node, RenderPipe.KernelPorts.Output);
                Assert.AreEqual(default(float), set.GetValueBlocking(value));
                set.Update();

                Assert.AreEqual(3.14f, set.GetValueBlocking(value));

                set.Destroy(node);
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
                set.SetPortArraySize(node, RenderPipe.KernelPorts.OutputArray, 2);
                var arrayValue = set.CreateGraphValueArray(node, RenderPipe.KernelPorts.OutputArray);

                for (int i = 1; i < 100; ++i)
                {
                    set.SendMessage(node, RenderPipe.SimulationPorts.Input, i);

                    set.Update();

                    var graphValue = set.GetValueBlocking(value);
                    var arrayGraphValue = set.GetTestingValueBlocking(arrayValue);

                    Assert.AreEqual(i, graphValue);
                    Assert.AreEqual(i + 1, arrayGraphValue[1]);
                }

                set.Destroy(node);
                set.ReleaseGraphValue(value);
                set.ReleaseGraphValueArray(arrayValue);
            }
        }

        [Test]
        public void GraphValueArrays_CanBeResolved_AndRemainValid_WhenResizing([Values] NodeSet.RenderExecutionModel computeType)
        {
            using (var set = new RenderGraphTests.PotentiallyJobifiedNodeSet(computeType))
            {
                var node = set.Create<RenderPipe>();

                var values = new List<GraphValueArray<float>>();

                for (int i = 1; i < 100; ++i)
                {
                    set.SetPortArraySize(node, RenderPipe.KernelPorts.OutputArray, i);
                    values.Add(set.CreateGraphValueArray(node, RenderPipe.KernelPorts.OutputArray));

                    set.SendMessage(node, RenderPipe.SimulationPorts.Input, i);

                    set.Update();

                    for (int j = 0; j < i; ++j)
                        Assert.AreEqual(j + i, set.GetTestingValueBlocking(values[j])[j]);
                }

                for (int i = 98; i >= 0; --i)
                {
                    set.SetPortArraySize(node, RenderPipe.KernelPorts.OutputArray, i);

                    set.SendMessage(node, RenderPipe.SimulationPorts.Input, i);

                    set.Update();

                    for (int j = 0; j < i; ++j)
                        Assert.AreEqual(j + i, set.GetTestingValueBlocking(values[j])[j]);
                }

                set.Destroy(node);
                foreach (var value in values)
                    set.ReleaseGraphValueArray(value);
            }
        }

        [Test]
        public void GraphValueArray_ResolvedReturnType_HasCorrectSize()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<RenderPipe>();
                set.SetPortArraySize(node, RenderPipe.KernelPorts.OutputArray, 3);
                var value = set.CreateGraphValueArray(node, RenderPipe.KernelPorts.OutputArray);

                set.Update();

                float dummy;
                Assert.AreEqual(3, set.GetTestingValueBlocking(value).Length);
                Assert.DoesNotThrow(() => dummy = set.GetTestingValueBlocking(value)[2]);

                set.SetPortArraySize(node, RenderPipe.KernelPorts.OutputArray, 2);

                set.Update();

                Assert.AreEqual(2, set.GetTestingValueBlocking(value).Length);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                Assert.Throws<IndexOutOfRangeException>(() => dummy = set.GetTestingValueBlocking(value)[2]);
#endif

                set.SetPortArraySize(node, RenderPipe.KernelPorts.OutputArray, 3);

                set.Update();

                Assert.AreEqual(3, set.GetTestingValueBlocking(value).Length);
                Assert.DoesNotThrow(() => dummy = set.GetTestingValueBlocking(value)[2]);

                set.Destroy(node);
                set.ReleaseGraphValueArray(value);
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
                    rootValue = set.CreateGraphValue<float>(root, (OutputPortID)RenderPipe.KernelPorts.Output);
                    childValue1 = set.CreateGraphValue<float>(child1, (OutputPortID)RenderAdder.KernelPorts.Output);
                    childValue2 = set.CreateGraphValue<float>(child2, (OutputPortID)RenderAdder.KernelPorts.Output);
                }

                set.SendMessage(child1, RenderAdder.SimulationPorts.Scale, 10);
                set.SendMessage(child2, RenderAdder.SimulationPorts.Scale, 20);

                set.SetPortArraySize(root, RenderPipe.KernelPorts.OutputArray, 2);
                set.Connect(root, RenderPipe.KernelPorts.Output, child1, RenderAdder.KernelPorts.Input);
                set.Connect(root, RenderPipe.KernelPorts.OutputArray, 1, child2, RenderAdder.KernelPorts.Input);

                for (int i = 0; i < 100; ++i)
                {
                    set.SendMessage(root, RenderPipe.SimulationPorts.Input, i);

                    set.Update();

                    Assert.AreEqual(i, set.GetValueBlocking(rootValue));
                    Assert.AreEqual(i * 10, set.GetValueBlocking(childValue1));
                    Assert.AreEqual((i + 1) * 20, set.GetValueBlocking(childValue2));
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

        [Test]
        public void CannotCreateWeaklyTypedGraphValue_WithPortArrayMismatch()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<RenderPipe>();
                Assert.Throws<InvalidOperationException>(() => set.CreateGraphValue<float>(node, (OutputPortID)RenderPipe.KernelPorts.OutputArray));
                Assert.Throws<InvalidOperationException>(() => set.CreateGraphValueArray<float>(node, (OutputPortID)RenderPipe.KernelPorts.Output));

                set.Destroy(node);
            }
        }

        [Test]
        public void CreatingGraphValue_MarksNodeAsObservable([Values] NodeSet.RenderExecutionModel computeType)
        {
            using (var set = new RenderGraphTests.PotentiallyJobifiedNodeSet(computeType))
            {
                set.RendererOptimizations |= NodeSet.RenderOptimizations.ObservabilityCulling;

                var root = set.Create<RenderPipe>();
                GraphValue<float> rootValue;

                set.Update();
                set.DataGraph.SyncAnyRendering();

                ref readonly var knode = ref set.DataGraph.GetInternalData()[root.VHandle.Index];

                Assert.True((knode.RunState & RenderGraph.KernelNode.Flags.Enabled) == 0);
                Assert.True((knode.RunState & RenderGraph.KernelNode.Flags.HasGraphValue) == 0);

                rootValue = set.CreateGraphValue(root, RenderPipe.KernelPorts.Output);

                set.Update();
                set.DataGraph.SyncAnyRendering();

                Assert.True((knode.RunState & RenderGraph.KernelNode.Flags.Enabled) != 0);
                Assert.True((knode.RunState & RenderGraph.KernelNode.Flags.HasGraphValue) != 0);
                Assert.True((knode.RunState & RenderGraph.KernelNode.Flags.Observable) != 0);
                
                set.Destroy(root);
                set.ReleaseGraphValue(rootValue);
            }
        }
    }
}
