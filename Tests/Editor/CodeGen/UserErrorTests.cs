using System;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Unity.DataFlowGraph.CodeGen.Tests
{
    /*
     *  Automatically detect whether a warning / error has at least one matching test case? 
     * 
     */
     
    public class UserErrorTests
    {
        class NodeWithDuplicateImplementations : SimulationNodeDefinition<NodeWithDuplicateImplementations.SimPorts>
        {
            public struct SimPorts : ISimulationPortDefinition { }
            public struct AdditionalSimPorts : ISimulationPortDefinition { }

        }

        [Test] 
        public void DFG_UE_01_NodeCannotHaveDuplicateImplementations()
        {
            using (var fixture = new DefinitionFixture<NodeWithDuplicateImplementations>())
            {
                fixture.ExpectError(new Regex(nameof(Diag.DFG_UE_01)));
                fixture.ParseAnalyse();
            }
        }

        class NodeWithMultipleImplementations : SimulationNodeDefinition<NodeWithMultipleImplementations.SimPorts>
        {
            public struct SimPorts : ISimulationPortDefinition, INodeData { }
        }

        [Test]
        public void DFG_UE_02_NodeCannotHaveMultipleImplementations()
        {
            using (var fixture = new DefinitionFixture<NodeWithMultipleImplementations>())
            {
                fixture.ParseSymbols();
                fixture.ExpectError(new Regex(nameof(Diag.DFG_UE_02)));
                fixture.AnalyseConsistency();
            }
        }

        class DataNodeWithoutKernel : KernelNodeDefinition<DataNodeWithoutKernel.KernelDefs>
        {
            public struct KernelDefs : IKernelPortDefinition { }
        }


        [Test]
        public void DFG_UE_03_NodeDoesntHaveAllOfKernelTriple()
        {
            using (var fixture = new DefinitionFixture<DataNodeWithoutKernel>())
            {
                fixture.ParseSymbols();
                fixture.ExpectError(new Regex(nameof(Diag.DFG_UE_03)));
                fixture.AnalyseConsistency();
            }
        }

        class SimNodeWithKernelPorts : SimulationNodeDefinition<SimNodeWithKernelPorts.SimPorts>
        {
            public struct KernelDefs : IKernelPortDefinition { }
            public struct KernelData : IKernelData { }

            public struct GraphKernel : IGraphKernel<KernelData, KernelDefs>
            {
                public void Execute(RenderContext ctx, KernelData data, ref KernelDefs ports) { }
            }

            public struct SimPorts : ISimulationPortDefinition { }

        }

        [Test]
        public void DFG_UE_04_SimulationNodeHasKernelAspects()
        {
            using (var fixture = new DefinitionFixture<SimNodeWithKernelPorts>())
            {
                fixture.ParseSymbols();
                fixture.ExpectError(new Regex(nameof(Diag.DFG_UE_04)));
                fixture.AnalyseConsistency();
            }
        }

        class SimpleNode_WithoutCtor : NodeDefinition<NodeWithMultipleImplementations.SimPorts>
        {
            public struct SimPorts : ISimulationPortDefinition { }
        }

        class SimpleNode_WithCtor : SimpleNode_WithoutCtor
        {
            public SimpleNode_WithCtor() { }
        }

        class SimpleNode_WithNonPublicCtor : SimpleNode_WithoutCtor
        {
            SimpleNode_WithNonPublicCtor() { }
        }

        class SimpleNode_WithArgumentCtor : SimpleNode_WithoutCtor
        {
            public SimpleNode_WithArgumentCtor(int _) { }
        }

        class SimpleNode_With2Ctor : SimpleNode_WithoutCtor
        {
            public SimpleNode_With2Ctor() { }
            public SimpleNode_With2Ctor(int _) { }

        }

        class SimpleNode_WithCCtor : SimpleNode_WithoutCtor
        {
            static SimpleNode_WithCCtor() { }
        }

        class SimpleNode_WithCCtorAndCtor : SimpleNode_WithoutCtor
        {
            public SimpleNode_WithCCtorAndCtor() { }
            static SimpleNode_WithCCtorAndCtor() { }
        }

        static Type[] GoodCtors = new[]
        {
            typeof(SimpleNode_WithoutCtor), typeof(SimpleNode_WithCtor), typeof(SimpleNode_WithCCtor), typeof(SimpleNode_WithCCtorAndCtor)
        };

        static Type[] BadCtors = new[]
        {
            typeof(SimpleNode_With2Ctor), typeof(SimpleNode_WithArgumentCtor), typeof(SimpleNode_WithNonPublicCtor)
        };

        [Test]
        public void DFG_UE_05_NodesWithWellFormedConstructorSetups_DoHaveOkayConsistency([ValueSource(nameof(GoodCtors))] Type node)
        {
            using (var fixture = new DefinitionFixture(node))
            {
                fixture.ParseSymbols();
                fixture.AnalyseConsistency();
            }
        }

        [Test]
        public void DFG_UE_05_NodesWithBadlyFormedConstructorSetups_EmitError_InConsistencyCheck([ValueSource(nameof(BadCtors))] Type node)
        {
            using (var fixture = new DefinitionFixture(node))
            {
                fixture.ParseSymbols();
                fixture.ExpectError(new Regex(nameof(Diag.DFG_UE_05)));
                fixture.AnalyseConsistency();
            }
        }

        class NodeThatUsesReservedNames : SimulationNodeDefinition<NodeThatUsesReservedNames.SimPorts>
        {
            public int DFG_CG_Something = 6; // CS0649
            public int DFG_CG_SomethingElse => DFG_CG_Something;
            public int DFG_CG_SomethingCompletelyDifferent() => DFG_CG_Something;

            public struct SimPorts : ISimulationPortDefinition { }
        }

        [Test]
        public void DFG_UE_06_NodeUsesReservedNames()
        {
            using (var fixture = new DefinitionFixture<NodeThatUsesReservedNames>())
            {
                fixture.ParseSymbols();
                fixture.ExpectError(
                    new Regex(
                        $"(?={nameof(Diag.DFG_UE_06)})" +
                        $"(?=.*{nameof(NodeThatUsesReservedNames.DFG_CG_Something)})" +
                        $"(?=.*{nameof(NodeThatUsesReservedNames.DFG_CG_SomethingElse)})" +
                        $"(?=.*{nameof(NodeThatUsesReservedNames.DFG_CG_SomethingCompletelyDifferent)})"
                    )
                );
                fixture.AnalyseConsistency();
            }
        }

        class InvalidNode : NodeDefinition { }

        [Test]
        public void DFG_UE_07_IndeterminateNodeDefinition_DoesNotCompile()
        {
            using (var fixture = new DefinitionFixture<InvalidNode>())
            {
                fixture.ParseSymbols();
                fixture.ExpectError(new Regex(nameof(Diag.DFG_UE_07)));
                fixture.AnalyseConsistency();
            }
        }

        class NodeWithReservedPortDefDeclaration : NodeDefinition<NodeWithReservedPortDefDeclaration.SimPorts>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
                public ushort DFG_CG_GetInputPortCount() => 0;
            }
        }

        [Test]
        public void DFG_UE_06_PortDefinitionCannotUsedReservedMethodNames()
        {
            using (var fixture = new DefinitionFixture<NodeWithReservedPortDefDeclaration>())
            {
                fixture.ParseSymbols();
                fixture.ExpectError(
                    new Regex($"{nameof(Diag.DFG_UE_06)}.*{nameof(NodeWithReservedPortDefDeclaration.SimPorts.DFG_CG_GetInputPortCount)}"));
                fixture.AnalyseConsistency();
            }
        }

        public class NodeWithNonPublicPorts : NodeDefinition<NodeWithNonPublicPorts.SimPorts>, IMsgHandler<int>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
                MessageInput<NodeWithNonPublicPorts, int> Input;
                MessageOutput<NodeWithNonPublicPorts, int> Output;
            }

            public void HandleMessage(in MessageContext ctx, in int msg) {}
        }

        public class NodeWithNonPublicStaticPorts : NodeDefinition<NodeWithNonPublicStaticPorts.SimPorts>, IMsgHandler<int>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
                static MessageInput<NodeWithNonPublicStaticPorts, int> Input;
                static MessageOutput<NodeWithNonPublicStaticPorts, int> Output;
            }

            public void HandleMessage(in MessageContext ctx, in int msg) {}
        }

        public class NodeWithPublicStaticPorts
            : NodeDefinition<NodeWithPublicStaticPorts.SimPorts>
                , IMsgHandler<float>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
                public static MessageInput<NodeWithPublicStaticPorts, float> Input;
                public static MessageOutput<NodeWithPublicStaticPorts, float> Output;
            }

            public void HandleMessage(in MessageContext ctx, in float msg) {}
        }

        [Test]
        public void DFG_UE_08_NodeWithInvalidPortDefinitions(
            [Values(typeof(NodeWithNonPublicPorts), typeof(NodeWithNonPublicStaticPorts), typeof(NodeWithPublicStaticPorts))]Type nodeType)
        {
            using (var fixture = new DefinitionFixture(nodeType))
            {
                fixture.ExpectError(new Regex(nameof(Diag.DFG_UE_08)));
                fixture.ExpectError(new Regex(nameof(Diag.DFG_UE_08)));
                fixture.ParseAnalyse();
            }
        }

        public class KernelNodeWithStaticMembersOnKernelPorts : NodeDefinition<KernelNodeWithStaticMembersOnKernelPorts.Data, KernelNodeWithStaticMembersOnKernelPorts.KernelDefs, KernelNodeWithStaticMembersOnKernelPorts.Kernel>
        {
            public struct Data : IKernelData {}

            public struct KernelDefs : IKernelPortDefinition
            {
                public DataInput<KernelNodeWithStaticMembersOnKernelPorts, int> Input1;
                public DataOutput<KernelNodeWithStaticMembersOnKernelPorts, int> Output2;
                public static int s_InvalidStatic;
            }

            public struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                public void Execute(RenderContext ctx, Data data, ref KernelDefs ports) {}
            }
        }

        public class KernelNodeWithNonPublicMembersOnKernelPorts : NodeDefinition<KernelNodeWithNonPublicMembersOnKernelPorts.Data, KernelNodeWithNonPublicMembersOnKernelPorts.KernelDefs, KernelNodeWithNonPublicMembersOnKernelPorts.Kernel>
        {
            public struct Data : IKernelData {}

            public struct KernelDefs : IKernelPortDefinition
            {
                public DataInput<KernelNodeWithStaticMembersOnKernelPorts, int> Input1;
                public DataOutput<KernelNodeWithStaticMembersOnKernelPorts, int> Output1;
                int s_InvalidMember;
            }

            public struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                public void Execute(RenderContext ctx, Data data, ref KernelDefs ports) {}
            }
        }

        [Test]
        public void DFG_UE_08_KernelNodeWithInvalidPortDefinitions(
            [Values(typeof(KernelNodeWithStaticMembersOnKernelPorts), typeof(KernelNodeWithNonPublicMembersOnKernelPorts))]Type nodeType)
        {
            using (var fixture = new DefinitionFixture(nodeType))
            {
                fixture.ExpectError(new Regex(nameof(Diag.DFG_UE_08)));
                fixture.ParseAnalyse();
            }
        }

        public class NodeWithNonPortTypes_InSimulationPortDefinition : NodeDefinition<NodeWithNonPortTypes_InSimulationPortDefinition.PortDefinition>
        {
            public struct PortDefinition : ISimulationPortDefinition
            {
                public int InvalidMember;
            }
        }

        public class NodeWithNonPortTypes_InKernelPortDefinition : NodeDefinition<NodeWithNonPortTypes_InKernelPortDefinition.Data, NodeWithNonPortTypes_InKernelPortDefinition.KernelDefs, NodeWithNonPortTypes_InKernelPortDefinition.Kernel>
        {
            public struct Data : IKernelData {}

            public struct KernelDefs : IKernelPortDefinition
            {
                public int InvalidMember;
            }

            public struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                public void Execute(RenderContext ctx, Data data, ref KernelDefs ports) {}
            }
        }

        [Test]
        public void DFG_UE_09_NodeWithNonPortTypeDeclarations(
            [Values(typeof(NodeWithNonPortTypes_InSimulationPortDefinition), typeof(NodeWithNonPortTypes_InKernelPortDefinition))]Type nodeType)
        {
            using (var fixture = new DefinitionFixture(nodeType))
            {
                fixture.ExpectError(new Regex(nameof(Diag.DFG_UE_09)));
                fixture.ParseAnalyse();
            }
        }
    }
}
