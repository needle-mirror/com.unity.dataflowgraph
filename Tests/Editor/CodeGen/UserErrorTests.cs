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
                public void Execute(RenderContext ctx, in KernelData data, ref KernelDefs ports) { }
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

        class SimpleNode_WithoutCtor : SimulationNodeDefinition<SimpleNode_WithoutCtor.SimPorts>
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

        class NodeWithReservedPortDefDeclaration : SimulationNodeDefinition<NodeWithReservedPortDefDeclaration.SimPorts>
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

        public class NodeWithNonPublicStaticPorts : SimulationNodeDefinition<NodeWithNonPublicStaticPorts.SimPorts>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
                static MessageInput<NodeWithNonPublicStaticPorts, int> Input;
                static MessageOutput<NodeWithNonPublicStaticPorts, int> Output;
            }

            struct NodeData : INodeData, IMsgHandler<int>
            {
                public void HandleMessage(MessageContext ctx, in int msg) {}
            }
        }

        public class NodeWithPublicStaticPorts : SimulationNodeDefinition<NodeWithPublicStaticPorts.SimPorts>
        {
            public struct SimPorts : ISimulationPortDefinition
            {
                public static MessageInput<NodeWithPublicStaticPorts, float> Input;
                public static MessageOutput<NodeWithPublicStaticPorts, float> Output;
            }

            struct NodeData : INodeData, IMsgHandler<float>
            {
                public void HandleMessage(MessageContext ctx, in float msg) {}
            }
        }

        [Test]
        public void DFG_UE_08_NodeWithInvalidPortDefinitions(
            [Values(typeof(NodeWithNonPublicStaticPorts), typeof(NodeWithPublicStaticPorts))]Type nodeType)
        {
            using (var fixture = new DefinitionFixture(nodeType))
            {
                fixture.ExpectError(new Regex(nameof(Diag.DFG_UE_08)));
                fixture.ExpectError(new Regex(nameof(Diag.DFG_UE_08)));
                fixture.ParseAnalyse();
            }
        }

        public class KernelNodeWithStaticMembersOnKernelPorts : KernelNodeDefinition<KernelNodeWithStaticMembersOnKernelPorts.KernelDefs>
        {
            public struct KernelDefs : IKernelPortDefinition
            {
                public DataInput<KernelNodeWithStaticMembersOnKernelPorts, int> Input1;
                public DataOutput<KernelNodeWithStaticMembersOnKernelPorts, int> Output2;
                public static int s_InvalidStatic;
            }

            struct Data : IKernelData {}

            struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                public void Execute(RenderContext ctx, in Data data, ref KernelDefs ports) {}
            }
        }

        [Test]
        public void DFG_UE_08_KernelNodeWithInvalidPortDefinitions([Values(typeof(KernelNodeWithStaticMembersOnKernelPorts))]Type nodeType)
        {
            using (var fixture = new DefinitionFixture(nodeType))
            {
                fixture.ExpectError(new Regex(nameof(Diag.DFG_UE_08)));
                fixture.ParseAnalyse();
            }
        }

        public class NodeWithNonPortTypes_InSimulationPortDefinition : SimulationNodeDefinition<NodeWithNonPortTypes_InSimulationPortDefinition.PortDefinition>
        {
            public struct PortDefinition : ISimulationPortDefinition
            {
                public int InvalidMember;
            }
        }

        public class NodeWithNonPortTypes_InKernelPortDefinition : KernelNodeDefinition<NodeWithNonPortTypes_InKernelPortDefinition.KernelDefs>
        {
            public struct KernelDefs : IKernelPortDefinition
            {
                public int InvalidMember;
            }

            struct Data : IKernelData {}

            struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                public void Execute(RenderContext ctx, in Data data, ref KernelDefs ports) {}
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

        public class NodeWithMissingHandlers : SimulationNodeDefinition<NodeWithMissingHandlers.PortDefinition>
        {
            public struct PortDefinition : ISimulationPortDefinition
            {
                public MessageInput<NodeWithMissingHandlers, int> Port;
            }
        }

        [Test]
        public void DFG_UE_11_NodeWithMissingHandlers()
        {
            using (var fixture = new DefinitionFixture<NodeWithMissingHandlers>())
            {
                fixture.ParseSymbols();
                fixture.ExpectError(new Regex(nameof(Diag.DFG_UE_11)));
                fixture.AnalyseConsistency();
            }
        }

        class SimulationNodeWithPortGenericMismatch : SimulationNodeDefinition<SimNodeWithKernelPorts.SimPorts>
        {
            public struct PortDefinition : ISimulationPortDefinition {}
        }

        class KernelNodeWithPortGenericMismatch : KernelNodeDefinition<SimNodeWithKernelPorts.KernelDefs>
        {
            public struct PortDefinition : IKernelPortDefinition {}
            public struct KernelData : IKernelData {}
            public struct GraphKernel : IGraphKernel<KernelData, PortDefinition>
            {
                public void Execute(RenderContext ctx, in KernelData data, ref PortDefinition ports) {}
            }
        }

        class SimulationKernelNodeWithKernelPortGenericMismatch : SimulationKernelNodeDefinition<SimulationKernelNodeWithKernelPortGenericMismatch.SimPortDefinition, SimNodeWithKernelPorts.KernelDefs>
        {
            public struct SimPortDefinition : ISimulationPortDefinition {}
            public struct KernelPortDefinition : IKernelPortDefinition {}
            public struct KernelData : IKernelData {}
            public struct GraphKernel : IGraphKernel<KernelData, KernelPortDefinition>
            {
                public void Execute(RenderContext ctx, in KernelData data, ref KernelPortDefinition ports) {}
            }
        }

        class SimulationKernelNodeWithSimPortGenericMismatch : SimulationKernelNodeDefinition<SimNodeWithKernelPorts.SimPorts, SimulationKernelNodeWithSimPortGenericMismatch.KernelPortDefinition>
        {
            public struct SimPortDefinition : ISimulationPortDefinition {}
            public struct KernelPortDefinition : IKernelPortDefinition {}
            public struct KernelData : IKernelData {}
            public struct GraphKernel : IGraphKernel<KernelData, KernelPortDefinition>
            {
                public void Execute(RenderContext ctx, in KernelData data, ref KernelPortDefinition ports) {}
            }
        }

        [Test]
        public void DFG_UE_12_NodeWithMismatchingPortDefinition_AndBaseClassGenerics(
            [Values(typeof(SimulationNodeWithPortGenericMismatch), typeof(KernelNodeWithPortGenericMismatch), typeof(SimulationKernelNodeWithKernelPortGenericMismatch), typeof(SimulationKernelNodeWithSimPortGenericMismatch))]Type nodeType)
        {
            using (var fixture = new DefinitionFixture(nodeType))
            {
                fixture.ParseSymbols();
                fixture.ExpectError(new Regex(nameof(Diag.DFG_UE_12)));
                fixture.AnalyseConsistency();
            }
        }

        class NodeWithKernelDataGenericMismatch : KernelNodeDefinition<NodeWithKernelDataGenericMismatch.KernelPortDefinition>
        {
            public struct KernelPortDefinition : IKernelPortDefinition {}
            public struct LocalKernelData : IKernelData {}
            public struct GraphKernel : IGraphKernel<SimNodeWithKernelPorts.KernelData, KernelPortDefinition>
            {
                public void Execute(RenderContext ctx, in SimNodeWithKernelPorts.KernelData data, ref KernelPortDefinition ports) {}
            }
        }

        class NodeWithKernelPortGenericMismatch : KernelNodeDefinition<NodeWithKernelPortGenericMismatch.KernelPortDefinition>
        {
            public struct KernelPortDefinition : IKernelPortDefinition {}
            public struct LocalKernelData : IKernelData {}
            public struct GraphKernel : IGraphKernel<LocalKernelData, SimNodeWithKernelPorts.KernelDefs>
            {
                public void Execute(RenderContext ctx, in LocalKernelData data, ref SimNodeWithKernelPorts.KernelDefs ports) {}
            }
        }

        [Test]
        public void DFG_UE_13_NodeWithMismatchingKernelDefinitionAspects_AndKernelGenerics(
            [Values(typeof(NodeWithKernelDataGenericMismatch), typeof(NodeWithKernelPortGenericMismatch))]Type nodeType)
        {
            using (var fixture = new DefinitionFixture(nodeType))
            {
                fixture.ParseSymbols();
                fixture.ExpectError(new Regex(nameof(Diag.DFG_UE_13)));
                fixture.AnalyseConsistency();
            }
        }

        struct ExternalSimulationPortDefinition : ISimulationPortDefinition { }
        class NodeWithExternallyDefinedSimulationPortDefinition : SimulationNodeDefinition<ExternalSimulationPortDefinition> { }

        class NodeWithExternallyDefinedSimulationPortDefinition2 : SimulationKernelNodeDefinition<ExternalSimulationPortDefinition, NodeWithExternallyDefinedSimulationPortDefinition2.KernelDefs>
        {
            public struct KernelDefs : IKernelPortDefinition { }
            struct KernelData : IKernelData { }
            struct GraphKernel : IGraphKernel<KernelData, KernelDefs>
            {
                public void Execute(RenderContext ctx, in KernelData data, ref KernelDefs ports) { }
            }
        }

        struct ExternalKernelPortDefinition : IKernelPortDefinition { }
        class NodeWithExternallyDefinedKernelPortDefinition : KernelNodeDefinition<ExternalKernelPortDefinition> { }

        class NodeWithExternallyDefinedKernelPortDefinition2 : SimulationKernelNodeDefinition<NodeWithExternallyDefinedKernelPortDefinition2.SimDefs, ExternalKernelPortDefinition>
        {
            public struct SimDefs : ISimulationPortDefinition { }
            struct KernelData : IKernelData { }
            struct GraphKernel : IGraphKernel<KernelData, ExternalKernelPortDefinition>
            {
                public void Execute(RenderContext ctx, in KernelData data, ref ExternalKernelPortDefinition ports) { }
            }
        }

        [Test]
        public void DFG_UE_14_NodeWithPortDefinition_DefinedOutsideNodeDefinition(
            [Values(typeof(NodeWithExternallyDefinedSimulationPortDefinition), typeof(NodeWithExternallyDefinedSimulationPortDefinition2), typeof(NodeWithExternallyDefinedKernelPortDefinition), typeof(NodeWithExternallyDefinedKernelPortDefinition2))]Type nodeType)
        {
            using (var fixture = new DefinitionFixture(nodeType))
            {
                fixture.ParseSymbols();
                fixture.ExpectError(new Regex(nameof(Diag.DFG_UE_14)));
                fixture.AnalyseConsistency();
            }
        }

        public class NodeWithClashingMsgHandlerTypes : SimulationNodeDefinition<NodeWithClashingMsgHandlerTypes.Ports>
        {
            public struct Ports : ISimulationPortDefinition
            {
                public MessageInput<NodeWithClashingMsgHandlerTypes, int> In;
            }

            struct Handlers : INodeData, IMsgHandler<int>, IMsgHandlerGeneric<int>
            {
                void IMsgHandler<int>.HandleMessage(MessageContext ctx, in int msg)
                    => throw new NotImplementedException();

                void IMsgHandlerGeneric<int>.HandleMessage(MessageContext ctx, in int msg)
                    => throw new NotImplementedException();
            }
        }

        [Test]
        public void DFG_UE_16_NodeWithClashingMsgHandlerTypes()
        {
            using (var fixture = new DefinitionFixture(typeof(NodeWithClashingMsgHandlerTypes)))
            {
                fixture.ParseSymbols();
                fixture.ExpectError(new Regex(nameof(Diag.DFG_UE_16)));
                fixture.AnalyseConsistency();
            }
        }
    }
}
