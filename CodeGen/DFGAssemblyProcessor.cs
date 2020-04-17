using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Rocks;
using Unity.Burst;

namespace Unity.DataFlowGraph.CodeGen
{
    /// <summary>
    /// Processor for rewriting parts of the DFG assembly
    /// </summary>
    class DFGAssemblyProcessor : ASTProcessor
    {
        DFGLibrary m_Library;

        public DFGAssemblyProcessor(ModuleDefinition def, DFGLibrary lib)
            : base(def)
        {
            m_Library = lib;
        }

        public override void ParseSymbols(Diag diag) { }

        public override void PostProcess(Diag diag, out bool mutated)
        {
            mutated = false;
        }
    }
}
