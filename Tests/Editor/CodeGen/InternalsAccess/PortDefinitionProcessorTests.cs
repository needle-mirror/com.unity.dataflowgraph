using System.Linq;
using NUnit.Framework;

namespace Unity.DataFlowGraph.CodeGen.Tests
{
    class PortDefinitionProcessorTests
    {
        [Test]
        public void IPortDefinitionInitializer_HasExpected_MethodPrefixes()
        {
            using (var testAssembly = AssemblyManager.LoadDFGAssembly())
            {
                var portDefInit = testAssembly.CecilAssembly.MainModule.ImportReference(typeof(IPortDefinitionInitializer));
                var nameClashes = ASTProcessor.GetSymbolNameOverlaps(portDefInit.Resolve());
                Assert.AreEqual(portDefInit.Resolve().Methods.Count, nameClashes.Count());
            }
        }
    }
}
