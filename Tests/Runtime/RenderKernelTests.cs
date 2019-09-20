﻿using System.Runtime.CompilerServices;
using NUnit.Framework;
using Unity.Burst;
using Unity.Jobs.LowLevel.Unsafe;

namespace Unity.DataFlowGraph.Tests
{
    public class RenderKernelTests
    {
        struct DummyData : IKernelData { }
        struct DummyPorts : IKernelPortDefinition { }

        [BurstCompile(CompileSynchronously = true)]
        struct DummyKernel : IGraphKernel<DummyData, DummyPorts>
        {
            public void Execute(RenderContext ctx, DummyData data, ref DummyPorts ports) { }
        }

        // TODO: Uncomment once issue #229 is fixed
        /*[Test]
        public unsafe void CanAlias_TypedJobDefinition_WithBaseDefinition()
        {
            DummyData data = default;
            DummyPorts ports = default;
            DummyKernel kernel = default;

            var aliasVersion = new RenderKernelFunction.AliasedJobDefinition
            {
                Kernel = (RenderKernelFunction.BaseKernel*)&kernel,
                KernelPorts = (RenderKernelFunction.BasePort*)&ports,
                NodeData = (RenderKernelFunction.BaseData*)&data,
                RenderContext = new RenderContext()
            };

            ref var burstVersion = ref Unsafe.AsRef<GraphKernel<DummyKernel, DummyData, DummyPorts>.Bursted>(&aliasVersion);
            ref var managedVersion = ref Unsafe.AsRef<GraphKernel<DummyKernel, DummyData, DummyPorts>.Managed>(&aliasVersion);

            Assert.IsTrue(aliasVersion.NodeData == burstVersion.m_Data);
            Assert.IsTrue(&data == burstVersion.m_Data);

            Assert.IsTrue(aliasVersion.Kernel == burstVersion.m_Kernel);
            Assert.IsTrue(&kernel == burstVersion.m_Kernel);

            Assert.IsTrue(aliasVersion.KernelPorts == burstVersion.m_Ports);
            Assert.IsTrue(&ports == burstVersion.m_Ports);

            fixed (RenderContext* burstRenderContext = &burstVersion.m_RenderContext)
            {
                Assert.IsTrue(&aliasVersion.RenderContext == burstRenderContext);
            }

            Assert.IsTrue(aliasVersion.NodeData == managedVersion.m_Data);
            Assert.IsTrue(&data == managedVersion.m_Data);

            Assert.IsTrue(aliasVersion.Kernel == managedVersion.m_Kernel);
            Assert.IsTrue(&kernel == managedVersion.m_Kernel);

            Assert.IsTrue(aliasVersion.KernelPorts == managedVersion.m_Ports);
            Assert.IsTrue(&ports == managedVersion.m_Ports);

            fixed (RenderContext* managedRenderContext = &managedVersion.m_RenderContext)
            {
                Assert.IsTrue(&aliasVersion.RenderContext == managedRenderContext);
            }
        } */

        public class BurstedNode : NodeDefinition
            <
                BurstedNode.Data, 
                BurstedNode.SimPorts,
                BurstedNode.KData,
                BurstedNode.KernelDefs,
                BurstedNode.Kernel
            >
        {
            public struct SimPorts : ISimulationPortDefinition { }
            public struct Data : INodeData { }
            public struct KData : IKernelData { }

            public struct KernelDefs : IKernelPortDefinition
            {
                public DataOutput<BurstedNode, bool> Result;
            }

            [BurstCompile(CompileSynchronously = true)]
            public struct Kernel : IGraphKernel<KData, KernelDefs>
            {
                public void Execute(RenderContext ctx, KData data, ref KernelDefs ports)
                {
                    ctx.Resolve(ref ports.Result) =
                        BurstConfig.DetectExecutionEngine() == BurstConfig.ExecutionResult.InsideBurst;
                }
            }
        }

        [Test]
        public void TestUserNodes_DoRunInsideBurst(
            [Values] RenderExecutionModel model
            )
        {
            if(!BurstConfig.IsBurstEnabled)
                Assert.Ignore("Burst is not enabled");

            using (var set = new NodeSet())
            {
                set.RendererModel = model;
                var node = set.Create<BurstedNode>();
                var gv = set.CreateGraphValue(node, BurstedNode.KernelPorts.Result);

                set.Update();

                Assert.IsTrue(set.GetValueBlocking(gv));

                set.Destroy(node);
                set.ReleaseGraphValue(gv);
            }
        }
    }

}
