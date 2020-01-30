using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;

namespace Unity.DataFlowGraph.Tests
{
    public class NodeDefinitionAPITests
    {
        public struct KernelPorts : IKernelPortDefinition { }

        class ParametricNode<T> : NodeDefinition<ParametricNode<T>.Node, EmptyPorts>
        {
            public struct Node : INodeData
            {
                T m_Member;
            }
        }

        [Test]
        public void CreatingNode_ContainingManagedData_ThrowsInvalidNodeDefinition()
        {
            using (var set = new NodeSet())
            {
                NodeHandle n = new NodeHandle();
                Assert.DoesNotThrow(() => n = set.Create<ParametricNode<int>>());
                set.Destroy(n);
                // Bool is special-cased now to be allowed. See #199
                Assert.DoesNotThrow(() => n = set.Create<ParametricNode<bool>>());
                set.Destroy(n);

                Assert.Throws<InvalidNodeDefinitionException>(() => set.Create<ParametricNode<string>>());
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                Assert.Throws<InvalidNodeDefinitionException>(() => set.Create<ParametricNode<NativeArray<int>>>());
#endif
                Assert.Throws<InvalidNodeDefinitionException>(() => set.Create<ParametricNode<GameObject>>());
            }
        }


        class ParametricKernelDataNode<T>
            : NodeDefinition<ParametricKernelDataNode<T>.Node, ParametricKernelDataNode<T>.KernelData, KernelPorts, ParametricKernelDataNode<T>.Kernel>
        {
            public struct Node : INodeData { }

            public struct KernelData : IKernelData
            {
                T m_Member;
            }

            [BurstCompile(CompileSynchronously = true)]
            public struct Kernel : IGraphKernel<KernelData, KernelPorts>
            {
                public void Execute(RenderContext ctx, KernelData data, ref KernelPorts ports) { }
            }
        }

        [Test]
        public void CreatingKernelData_ContainingManagedData_ThrowsInvalidNodeDefinition()
        {
#if !UNITY_EDITOR
            if (JobsUtility.JobCompilerEnabled)
                Assert.Ignore("Skipping test since Burst AOT is broken for generic kernels");
#endif

            using (var set = new NodeSet())
            {
                NodeHandle n = new NodeHandle();
                Assert.DoesNotThrow(() => n = set.Create<ParametricKernelDataNode<int>>());
                set.Destroy(n);
                // Bool is special-cased now to be allowed. See #199
                Assert.DoesNotThrow(() => n = set.Create<ParametricKernelDataNode<bool>>());

                set.Destroy(n);

                Assert.Throws<InvalidNodeDefinitionException>(() => set.Create<ParametricKernelDataNode<string>>());
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                Assert.Throws<InvalidNodeDefinitionException>(() => set.Create<ParametricKernelDataNode<NativeArray<int>>>());
#endif
                Assert.Throws<InvalidNodeDefinitionException>(() => set.Create<ParametricKernelDataNode<GameObject>>());
            }
        }

        class ParametricKernelNode<T> : NodeDefinition<ParametricKernelNode<T>.Node, ParametricKernelNode<T>.KernelData, KernelPorts, ParametricKernelNode<T>.Kernel>
        {
            public struct Node : INodeData { }

            public struct KernelData : IKernelData { }

            [BurstCompile(CompileSynchronously = true)]
            public struct Kernel : IGraphKernel<KernelData, KernelPorts>
            {
                T m_Member;

                public void Execute(RenderContext ctx, KernelData data, ref KernelPorts ports) { }
            }
        }

        [Test]
        public void CreatingKernel_ContainingManagedData_ThrowsInvalidNodeDefinition()
        {
#if !UNITY_EDITOR
            if (JobsUtility.JobCompilerEnabled)
                Assert.Ignore("Skipping test since Burst AOT is broken for generic kernels");
#endif

            using (var set = new NodeSet())
            {
                NodeHandle n = new NodeHandle();
                Assert.DoesNotThrow(() => n = set.Create<ParametricKernelNode<int>>());
                set.Destroy(n);
                // Bool is special-cased now to be allowed. See #199
                Assert.DoesNotThrow(() => n = set.Create<ParametricKernelNode<bool>>());
                set.Destroy(n);

                Assert.Throws<InvalidNodeDefinitionException>(() => set.Create<ParametricKernelNode<string>>());
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                Assert.Throws<InvalidNodeDefinitionException>(() => set.Create<ParametricKernelNode<NativeArray<int>>>());
#endif
                Assert.Throws<InvalidNodeDefinitionException>(() => set.Create<ParametricKernelNode<GameObject>>());
            }
        }

        class StaticKernelNode : NodeDefinition<StaticKernelNode.Node, StaticKernelNode.KernelData, KernelPorts, StaticKernelNode.Kernel>
        {
            public struct Node : INodeData { }

            public struct KernelData : IKernelData { }

            [BurstCompile(CompileSynchronously = true)]
            public struct Kernel : IGraphKernel<KernelData, KernelPorts>
            {
                static int m_Member;

                public void Execute(RenderContext ctx, KernelData data, ref KernelPorts ports) { }
            }
        }

        class NodeWithManagedDataTypeWithoutAttribute : NodeDefinition<NodeWithManagedDataTypeWithoutAttribute.Data, EmptyPorts>
        {
            public struct Data : INodeData
            {
#pragma warning disable 649  // never assigned
                public GameObject g;
#pragma warning restore 649
            }
        }

        [Test]
        public void CreatingNodeDefinition_WithManagedContents_NotDeclaredManaged_Throws_WithHelpfulMessage()
        {
            using (var set = new NodeSet())
            {
                bool exceptionWasCaught = false;
                try
                {
                    set.Create<NodeWithManagedDataTypeWithoutAttribute>();
                }
                catch (InvalidNodeDefinitionException e)
                {
                    StringAssert.Contains("is not unmanaged, add the attribute [Managed] to the type if you need to store references in your data", e.Message);
                    exceptionWasCaught = true;
                }

                Assert.IsTrue(exceptionWasCaught);
            }
        }

        class NodeWithManagedData : NodeDefinition<NodeWithManagedData.Data, EmptyPorts>
        {
            [Managed]
            public struct Data : INodeData
            {
#pragma warning disable 649  // never assigned
                public NodeWithManagedDataTypeWithoutAttribute.Data g;
#pragma warning restore 649
            }
        }

        [Test]
        public void CreatingNodeDefinition_WithManagedContents_DeclaredManaged_IsOK()
        {
            using (var set = new NodeSet())
            {
                set.Destroy(set.Create<NodeWithManagedData>());
            }
        }
    }
}
