using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Rocks;

namespace Unity.DataFlowGraph.CodeGen
{
    class AssemblyVisitor
    {
        internal List<ASTProcessor> Processors = new List<ASTProcessor>();

        public void Prepare(Diag diag, AssemblyDefinition assemblyDefinition)
        {
            if (assemblyDefinition.Name.Name == "Unity.DataFlowGraph.CodeGen.Tests")
                return;

            var lib = new DFGLibrary(assemblyDefinition.MainModule);

            Processors.Add(lib);

            if (assemblyDefinition.Name.Name == "Unity.DataFlowGraph")
                Processors.Add(new DFGAssemblyProcessor(assemblyDefinition.MainModule, lib));

            var nodeTypes = AccumulateNodeDefinitions(assemblyDefinition.MainModule);

            Processors.AddRange(nodeTypes.Select(nt => new NodeDefinitionProcessor(lib, nt)));
        }

        /// <returns>True on success, false on any error (see <paramref name="diag"/>)</returns>
        public bool Process(Diag diag, out bool madeAChange)
        {
            madeAChange = false;

            Processors.ForEach(n => n.ParseSymbols(diag));
            Processors.ForEach(n => n.AnalyseConsistency(diag));

            if (diag.HasErrors())
                return false;

            bool anyMutations = false;

            Processors.ForEach(
                node =>
                {
                    node.PostProcess(diag, out var locallyMutated);
                    anyMutations |= locallyMutated;
                }
            );

            madeAChange |= anyMutations;

            return !diag.HasErrors();
        }

        public static List<TypeDefinition> AccumulateNodeDefinitions(ModuleDefinition module)
        {
            var results = new List<TypeDefinition>();

            // Pick up all node definitions
            foreach (var type in module.GetAllTypes())
            {
                if (type.IsClass && !type.IsAbstract)
                {
                    for (var baseType = type.BaseType; baseType != null; baseType = baseType.Resolve().BaseType)
                    {
                        if (baseType.FullName == typeof(NodeDefinition).FullName) // TODO: Use IsOrImplements in future
                        {
                            results.Add(type);
                            break;
                        }
                    }
                }
            }

            return results;
        }


    }
}
