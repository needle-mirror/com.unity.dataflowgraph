using NUnit.Framework;

namespace Unity.DataFlowGraph.CodeGen.Tests
{
    public class DFGLibraryTests
    {

        [Test]
        public void CanCreate_DFGLibrary_And_ParseValidateProcess()
        {
            using (var thisAssembly = AssemblyManager.LoadThisTestAssemblyAgain())
            {
                var lib = new DFGLibrary(thisAssembly.Assembly.MainModule);
                var diag = new Diag();

                lib.ParseSymbols(diag);
                lib.AnalyseConsistency(diag);

                CollectionAssert.IsEmpty(diag.Messages);
            }
        }

        [Test]
        public void DFGLibrary_NeverMutates()
        {
            using (var thisAssembly = AssemblyManager.LoadThisTestAssemblyAgain())
            {
                var lib = new DFGLibrary(thisAssembly.Assembly.MainModule);
                var diag = new Diag();

                lib.ParseSymbols(diag);
                lib.AnalyseConsistency(diag);
                lib.PostProcess(diag, out var mutated);

                Assert.IsFalse(mutated);
                CollectionAssert.IsEmpty(diag.Messages);
            }
        }
    }
}
