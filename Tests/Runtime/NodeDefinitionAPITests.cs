using System;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using UnityEngine;

namespace Unity.DataFlowGraph.Tests
{
    public class NodeDefinitionAPITests
    {
        class ParametricNode<T> : SimulationNodeDefinition<ParametricNode<T>.EmptyPorts>
        {
            public struct EmptyPorts : ISimulationPortDefinition { }

            struct Node : INodeData
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
            : KernelNodeDefinition<ParametricKernelDataNode<T>.EmptyPorts>
        {
            public struct EmptyPorts : IKernelPortDefinition { }

            struct KernelData : IKernelData
            {
                T m_Member;
            }

            struct Kernel : IGraphKernel<KernelData, EmptyPorts>
            {
                public void Execute(RenderContext ctx, in KernelData data, ref EmptyPorts ports) { }
            }
        }

        [Test]
        public void CreatingKernelData_ContainingManagedData_ThrowsInvalidNodeDefinition()
        {
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

        class ParametricKernelNode<T> : KernelNodeDefinition<ParametricKernelNode<T>.EmptyPorts>
        {
            public struct EmptyPorts : IKernelPortDefinition { }

            internal struct KernelData : IKernelData { }

            struct Kernel : IGraphKernel<KernelData, EmptyPorts>
            {
                T m_Member;

                public void Execute(RenderContext ctx, in KernelData data, ref EmptyPorts ports) { }
            }
        }

        [Test]
        public void CreatingKernel_ContainingManagedData_ThrowsInvalidNodeDefinition()
        {
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

        class StaticKernelNode : KernelNodeDefinition<StaticKernelNode.EmptyPorts>
        {
            public struct EmptyPorts : IKernelPortDefinition { }

            struct Node : INodeData { }

            struct KernelData : IKernelData { }

            [BurstCompile(CompileSynchronously = true)]
            struct Kernel : IGraphKernel<KernelData, EmptyPorts>
            {
                static int m_Member;

                public void Execute(RenderContext ctx, in KernelData data, ref EmptyPorts ports) { }
            }
        }

        [IsNotInstantiable]
        class NodeWithManagedDataTypeWithoutAttribute : SimulationNodeDefinition<NodeWithManagedDataTypeWithoutAttribute.EmptyPorts>
        {
            public struct EmptyPorts : ISimulationPortDefinition { }

            internal struct Data : INodeData
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
                var e = Assert.Throws<InvalidNodeDefinitionException>(() => set.Create<NodeWithManagedDataTypeWithoutAttribute>());
                StringAssert.Contains("is not unmanaged, add the attribute [Managed] to the type if you need to store references in your data", e.Message);
            }
        }

        class NodeWithManagedData : SimulationNodeDefinition<NodeWithManagedData.EmptyPorts>
        {
            public struct EmptyPorts : ISimulationPortDefinition { }

            [Managed]
            struct Data : INodeData
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

        class GenericNonScaffoldedNode<T> : SimulationNodeDefinition<GenericNonScaffoldedNode<T>.Ports>
        {
            public struct Ports : ISimulationPortDefinition
            {

            }

            struct Data : INodeData
            {
                T m_Secret;
            }
        }

        [Test]
        public void CanInstantiate_GenericNonScaffoldedNode()
        {
            using (var set = new NodeSet())
            {
                set.Destroy(set.Create<GenericNonScaffoldedNode<int>>());
            }
        }

        class Node_WithNestedGenericAspects : GenericNonScaffoldedNode<int>
        {
        }

        [Test]
        public void CanInstantiate_NonScaffoldedNode_WithNestedGenericAspects()
        {
            using (var set = new NodeSet())
            {
                set.Destroy(set.Create<Node_WithNestedGenericAspects>());
            }
        }

        [Test]
        public void CannotUpdate_WrongKernelData()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<GraphValueTests.RenderPipe>();
                var @checked = set.Validate(node);

                Assert.DoesNotThrow(
                    () =>
                    {
                        set.UpdateKernelData(@checked, new GraphValueTests.RenderPipe.KernelData());
                    }
                );

                Assert.Throws<InvalidOperationException>(
                    () =>
                    {
                        set.UpdateKernelData(@checked, new ParametricKernelNode<float>.KernelData());
                    }
                );

                Assert.DoesNotThrow(
                    () =>
                    {
                        set.UpdateKernelData(@checked, new GraphValueTests.RenderPipe.KernelData());
                    }
                );

                set.Destroy(node);
            }
        }

#if !ENABLE_IL2CPP  // Issue #581: IL2CPP bug with NodeDefinitions with trait aspects across assembly boundaries.
        public class NodeWithTraitsFromDifferentAssemblies : ExternalKernelNode<NodeWithTraitsFromDifferentAssemblies, float, float, NodeWithTraitsFromDifferentAssemblies.Kernel>
        {
            public struct Kernel : IGraphKernel<EmptyKernelData, KernelDefs>
            {
                public void Execute(RenderContext ctx, in EmptyKernelData data, ref KernelDefs ports) =>
                    ctx.Resolve(ref ports.Output) = ctx.Resolve(ports.Input);
            }
        }

        [Test]
        public void CanInstantiate_NodeDefinition_WithTraits_FromDifferentAssemblies()
        {

            using (var set = new NodeSet())
            {
                Assert.That(typeof(NodeWithTraitsFromDifferentAssemblies.Kernel).Module, Is.EqualTo(typeof(NodeWithTraitsFromDifferentAssemblies).Module));
                Assert.That(typeof(NodeWithTraitsFromDifferentAssemblies.Kernel).Module, Is.Not.EqualTo(typeof(NodeWithTraitsFromDifferentAssemblies.KernelDefs).Module));
                set.Destroy(set.Create<NodeWithTraitsFromDifferentAssemblies>());
            }
        }
#endif  // !ENABLE_IL2CPP
    }
}
