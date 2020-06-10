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
            if (assemblyDefinition.Name.Name == "Unity.DataFlowGraph.CodeGen.Tests" ||
                assemblyDefinition.Name.Name == "Unity.DataFlowGraph.CodeGen.InternalsAccess.Tests")
                return;

            var lib = new DFGLibrary(assemblyDefinition.MainModule);

            Processors.Add(lib);

            if (assemblyDefinition.Name.Name == "Unity.DataFlowGraph")
                Processors.Add(new DFGAssemblyProcessor(assemblyDefinition.MainModule, lib));

            var nodeTypes = AccumulateNodeDefinitions(assemblyDefinition.MainModule);

            Processors.AddRange(nodeTypes.Select(nt => new NodeDefinitionProcessor(lib, nt)));

            var portTypes = AccumulatePortDefinitions(assemblyDefinition.MainModule);
            Processors.AddRange(portTypes.Select(nt => new PortDefinitionProcessor(lib, nt)));
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
            var nodeDefinitions = new List<TypeDefinition>();
            var nodeDefinition = module.GetImportedReference(typeof(NodeDefinition));

            foreach (var type in module.GetAllTypes())
            {
                if (type.IsClass && !type.IsAbstract)
                {
                    for (var baseType = type.BaseType; baseType != null; baseType = baseType.Resolve().BaseType)
                    {
                        if (baseType.IsOrImplements(nodeDefinition))
                        {
                            nodeDefinitions.Add(type);
                            break;
                        }
                    }
                }
            }

            return nodeDefinitions;
        }

        public static List<TypeDefinition> AccumulatePortDefinitions(ModuleDefinition module)
        {
            var portDefinitions = new List<TypeDefinition>();
            var simulationPortDefinition = module.GetImportedReference(typeof(ISimulationPortDefinition));
            var kernelPortDefinition = module.GetImportedReference(typeof(IKernelPortDefinition));

            foreach (var type in module.GetAllTypes())
            {
                if (type.IsValueType && type.HasInterfaces)
                {
                    if (type.Interfaces.Any(i => i.InterfaceType.IsOrImplements(simulationPortDefinition) ||
                                                 i.InterfaceType.IsOrImplements(kernelPortDefinition)))
                    {
                        portDefinitions.Add(type);
                    }
                }
            }

            return portDefinitions;
        }
    }
}
