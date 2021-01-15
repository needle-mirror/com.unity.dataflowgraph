using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Unity.DataFlowGraph.CodeGen.Tests
{
    public class InternalErrorTests
    {
        [Test]
        public void DFG_IE_01_IsPresent_OnFaked_DFGLibrary()
        {
            using (var cecilAssembly = AssemblyManager.LoadThisTestAssemblyAgain())
            {
                var library = new DFGLibrary(cecilAssembly.Assembly.MainModule);

                var diag = new Diag();

                library.ParseSymbols(diag);
                library.AnalyseConsistency(diag);

                Assert.False(diag.HasErrors());

                library.INodeDataInterface = null;

                library.AnalyseConsistency(diag);
                Assert.True(diag.HasErrors());
            }
        }

        class NodeWithPortDefinitionWithInitializerImplemented : SimulationNodeDefinition<NodeWithPortDefinitionWithInitializerImplemented.PortDefinition>
        {
            internal struct PortDefinition : ISimulationPortDefinition, IPortDefinitionInitializer
            {
                public void DFG_CG_Initialize() => throw new System.NotImplementedException();
            }
        }

        [Test]
        public void DFG_IE_05_UnexpectedInterfaceImplementation()
        {
            using (var fixture = new DefinitionFixture(typeof(NodeWithPortDefinitionWithInitializerImplemented)))
            {
                fixture.ParseSymbols();
                fixture.ExpectError(new Regex(nameof(Diag.DFG_IE_05)));
                fixture.AnalyseConsistency();
            }
        }
    }
}
