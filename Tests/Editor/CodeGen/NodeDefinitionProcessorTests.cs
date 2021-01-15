using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.DataFlowGraph.CodeGen.Tests.Archetypes;

namespace Unity.DataFlowGraph.CodeGen.Tests
{
    class NodeDefinitionProcessorTests
    {
        public static Dictionary<DFGLibrary.NodeDefinitionKind, Type> ArchetypeFrom;

        static NodeDefinitionProcessorTests()
        {
            ArchetypeFrom = new Dictionary<DFGLibrary.NodeDefinitionKind, Type>();

            ArchetypeFrom[DFGLibrary.NodeDefinitionKind.Simulation]         = typeof(SimulationNode);
            ArchetypeFrom[DFGLibrary.NodeDefinitionKind.Kernel]             = typeof(KernelNode);
            ArchetypeFrom[DFGLibrary.NodeDefinitionKind.SimulationKernel]   = typeof(SimulationKernelNode);
            ArchetypeFrom[DFGLibrary.NodeDefinitionKind.Naked]              = typeof(NakedNode);
        }

        [Test]
        public void CanDetermine_NodeDefinitionKind([Values]DFGLibrary.NodeDefinitionKind kind)
        {
            using (var fixture = new DefinitionFixture(ArchetypeFrom[kind]))
            {
                fixture.ParseAnalyse();
                Assert.AreEqual(fixture.NodeProcessor.Kind, kind);
            }
        }

        [Test]
        public void GivenSomeNodeKind_AssociatedStructs_AreParsedAndFound_Correctly([Values]DFGLibrary.NodeDefinitionKind kind)
        {
            using (var fixture = new DefinitionFixture(ArchetypeFrom[kind]))
            {
                fixture.ParseAnalyse();

                switch (kind)
                {
                    case DFGLibrary.NodeDefinitionKind.Simulation:
                        Assert.NotNull(fixture.NodeProcessor.SimulationPortImplementation);

                        // (node data can conditionally exist)
                        Assert.Null(fixture.NodeProcessor.KernelPortImplementation);
                        Assert.Null(fixture.NodeProcessor.KernelDataImplementation);
                        Assert.Null(fixture.NodeProcessor.GraphKernelImplementation);
                        break;
                    case DFGLibrary.NodeDefinitionKind.Kernel:
                        Assert.NotNull(fixture.NodeProcessor.KernelPortImplementation);
                        Assert.NotNull(fixture.NodeProcessor.KernelDataImplementation);
                        Assert.NotNull(fixture.NodeProcessor.GraphKernelImplementation);

                        Assert.Null(fixture.NodeProcessor.SimulationPortImplementation);
                        Assert.Null(fixture.NodeProcessor.NodeDataImplementation);
                        break;
                    case DFGLibrary.NodeDefinitionKind.SimulationKernel:
                        Assert.NotNull(fixture.NodeProcessor.SimulationPortImplementation);
                        Assert.NotNull(fixture.NodeProcessor.KernelPortImplementation);
                        Assert.NotNull(fixture.NodeProcessor.KernelDataImplementation);
                        Assert.NotNull(fixture.NodeProcessor.GraphKernelImplementation);
                        break;
                    case DFGLibrary.NodeDefinitionKind.Naked:
                        break;
                }
            }
        }

        class GenericNonScaffoldedNode_WithOpenNodeData<T> : SimulationNodeDefinition<GenericNonScaffoldedNode_WithOpenNodeData<T>.Ports>
        {
            public struct Ports : ISimulationPortDefinition
            {

            }

            struct Data<TSomethingElse> : INodeData
            {
                T m_Secret;
            }
        }

        [Test]
        public void Parser_DoesNotPickUp_OpenGenericAspects()
        {
            using (var fixture = new DefinitionFixture<GenericNonScaffoldedNode_WithOpenNodeData<int>>())
            {
                fixture.ParseSymbols();

                Assert.Null(fixture.NodeProcessor.NodeDataImplementation);
            }
        }

        class BaseNodeDefinition_WithHiddenPorts : NodeDefinition
        {
            struct Ports : ISimulationPortDefinition
            {

            }
        }

        class ExtendedNodeDefinition_ThatHasInaccessiblePorts : BaseNodeDefinition_WithHiddenPorts
        {

        }

        [Test]
        public void Parser_DoesNotPickUp_HiddenSuperClassAspects()
        {
            using (var fixture = new DefinitionFixture<BaseNodeDefinition_WithHiddenPorts>())
            {
                fixture.ParseSymbols();
                fixture.AnalyseConsistency();
            }

            using (var fixture = new DefinitionFixture<ExtendedNodeDefinition_ThatHasInaccessiblePorts>())
            {
                fixture.ParseSymbols();
                fixture.ExpectError(new Regex(nameof(Diag.DFG_UE_07)));
                fixture.AnalyseConsistency();
            }
        }
    }
}
