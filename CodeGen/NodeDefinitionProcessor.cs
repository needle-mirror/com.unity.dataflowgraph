using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace Unity.DataFlowGraph.CodeGen
{
    partial class NodeDefinitionProcessor : DefinitionProcessor
    {
        internal TypeReference
            NodeDataImplementation,
            SimulationPortImplementation;

        internal TypeReference
            KernelPortImplementation,
            GraphKernelImplementation,
            KernelDataImplementation;

        internal DFGLibrary.NodeDefinitionKind? Kind { get; private set; }
        internal DFGLibrary.NodeTraitsKind? TraitsKind { get; private set; }

        MethodDefinition[] m_ExistingConstructors;
        MethodDefinition m_Constructor;
        /// <summary>
        /// Generic-context-preserved most-derived node definition class
        /// <seealso cref="Kind"/>
        /// </summary>
        TypeReference m_BaseNodeDefinitionReference;

        public NodeDefinitionProcessor(DFGLibrary library, TypeDefinition td)
            : base(library, td)
        {
        }

        public override void ParseSymbols(Diag diag)
        {
            (Kind, m_BaseNodeDefinitionReference) = DetermineNodeDefinition(diag);
            TraverseAndCollectDeclarations(diag, InstantiatedDefinition);

            m_ExistingConstructors = DefinitionRoot
                .GetConstructors()
                // Exclude class constructors (".cctor")
                .Where(c => c.Name == ".ctor")
                .ToArray();

            TraitsKind = DetermineTraitsKind();
        }

        public override void AnalyseConsistency(Diag diag)
        {
            if (!Kind.HasValue)
            {
                diag.DFG_IE_03(this);
                return;
            }

            if (!TraitsKind.HasValue)
            {
                diag.DFG_UE_07(this, null);
                return;
            }

            var union = new[]
            {
                NodeDataImplementation,
                SimulationPortImplementation,
                GraphKernelImplementation,
                KernelPortImplementation,
                KernelDataImplementation

            };

            var nonNullUnion = union.Where(d => d != null);

            if (nonNullUnion.Distinct().Count() != nonNullUnion.Count())
            {
                diag.DFG_UE_02(this, new AggrTypeContext(union));
            }

            // Determine kernel composition
            var kernelTriple = new[] { GraphKernelImplementation, KernelPortImplementation, KernelDataImplementation };
            var nonNullKernelAspects = kernelTriple.Where(i => i != null);

            if (nonNullKernelAspects.Any())
            {
                // test whether they all exist (since some did)
                if (nonNullKernelAspects.Distinct().Count() != kernelTriple.Count())
                {
                    diag.DFG_UE_03(this, new AggrTypeContext(kernelTriple));
                }

                var aspects = Kind.Value.HasKernelAspects();

                if (aspects.HasValue && !aspects.Value)
                    diag.DFG_UE_04(this, Kind.Value, new AggrTypeContext(kernelTriple));
            }

            if(m_ExistingConstructors.Any())
            {
                if (m_ExistingConstructors.Length > 1 || m_ExistingConstructors[0].Parameters.Count > 0 || (m_ExistingConstructors[0].Attributes & MethodAttributes.Public) == 0)
                    diag.DFG_UE_05(this, new MethodLocationContext(m_ExistingConstructors[0]));
            }
            var nameClashes = GetSymbolNameOverlaps(DefinitionRoot);

            if (nameClashes.Any())
                diag.DFG_UE_06(this, new AggrTypeContext(nameClashes));
        }

        public override void PostProcess(Diag diag, out bool mutated)
        {
            CreateTraitsExpression(diag);
            mutated = true;
        }

        MethodDefinition EmitCallToMethodInDefaultConstructor(MethodReference target)
        {
            MethodDefinition GetConstructor()
            {
                if (m_Constructor != null)
                    return m_Constructor;

                m_Constructor = m_ExistingConstructors.FirstOrDefault();

                if (m_Constructor != null)
                    return m_Constructor;

                var methodAttributes = MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName;
                var gen = new MethodDefinition(".ctor", methodAttributes, Module.TypeSystem.Void);

                gen.Body.Instructions.Add(Instruction.Create(OpCodes.Nop));
                gen.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));

                DefinitionRoot.Methods.Add(gen);
                
                return m_Constructor = gen;
            }

            var body = GetConstructor().Body;
            
            var last = body.Instructions[body.Instructions.Count - 1];
            body.Instructions.RemoveAt(body.Instructions.Count - 1);

            body.Instructions.Add(Instruction.Create(OpCodes.Nop));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            body.Instructions.Add(Instruction.Create(OpCodes.Call, target));

            body.Instructions.Add(last);

            return m_Constructor;
        }
    }
}
