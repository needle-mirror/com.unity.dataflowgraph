using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using ParameterAttributes = Mono.Cecil.ParameterAttributes;

namespace Unity.DataFlowGraph.CodeGen
{
    class NodeDefinitionProcessor : ASTProcessor
    {
        public readonly TypeDefinition DefinitionRoot;

        DFGLibrary m_Lib;

        public NodeDefinitionProcessor(DFGLibrary library, TypeDefinition td)
            : base(td.Module)
        {
            DefinitionRoot = td;
            m_Lib = library;
        }

        public override void ParseSymbols(Diag diag)
        {

        }

        public override void PostProcess(Diag diag, out bool mutated)
        {
            mutated = false;
        }
    }
}
