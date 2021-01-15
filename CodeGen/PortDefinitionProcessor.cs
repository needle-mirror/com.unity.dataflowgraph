using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace Unity.DataFlowGraph.CodeGen
{
    struct PortInfo
    {
        public InstantiatedField Field;
        // DataInput<,,> etc.
        public GenericInstanceType StrippedPortType;
        public DFGLibrary.PortClass Classification;
        public bool IsArray;
    }

    class PortDefinitionProcessor : DefinitionProcessor
    {
        List<PortInfo> m_PortInfos = new List<PortInfo>();

        public PortDefinitionProcessor(DFGLibrary library, TypeDefinition td)
            : base(library, td)
        {
        }

        public override string GetContextName()
        {
            return DefinitionRoot.FullName;
        }

        public override void ParseSymbols(Diag diag)
        {
            foreach (var field in InstantiatedDefinition.InstantiatedFields())
            {
                if (field.Definition.IsStatic)
                {
                    diag.DFG_UE_08(this, field.Instantiated);
                    continue;
                }

                var isArray = field.SubstitutedType.Open().RefersToSame(m_Lib.PortArrayType);
                if(field.SubstitutedType.IsGenericInstance)
                {
                    var strippedType = (GenericInstanceType)EnsureImported(isArray ? field.GenericType.GenericArguments[0] : field.SubstitutedType);

                    if(m_Lib.ClassifyPort(strippedType) is var portClass)
                    {
                        m_PortInfos.Add(new PortInfo { IsArray = isArray, Field = field, StrippedPortType = strippedType, Classification = portClass.Value });
                        continue;
                    }
                }

                diag.DFG_UE_09(this, field.Instantiated);
            }
        }

        protected override void OnAnalyseConsistency(Diag diag)
        {
            if (DefinitionRoot.Interfaces.Any(i => i.InterfaceType.RefersToSame(m_Lib.IPortDefinitionInitializerType)))
            {
                diag.DFG_IE_05(this);
            }
            else
            {
                var nameClashes = GetSymbolNameOverlaps(DefinitionRoot);

                if (nameClashes.Any())
                    diag.DFG_UE_06(this, new AggrTypeContext(nameClashes));
            }
        }

        public override void PostProcess(Diag diag, out bool mutated)
        {
            DefinitionRoot.Interfaces.Add(new InterfaceImplementation(m_Lib.IPortDefinitionInitializerType));
            DefinitionRoot.Methods.Add(SynthesizePortInitializer(m_Lib.IPortDefinitionInitializedMethod));
            mutated = true;
        }

        MethodDefinition SynthesizePortInitializer(MethodDefinition interfaceMethod)
        {
            var method = CreateEmptyInterfaceMethodImplementation(interfaceMethod);

            // Emit IL to initialization of all ports for either an ISimulationPortDefinition or IKernelPortDefinition.
            // For example, for the test node NodeWithAllTypesOfPorts, we would produce the following IL:
            //     (literal = <hardcoded literal computed in here using portCounters, counted per category & input/output>)
            //     MessageInput<NodeWithAllTypesOfPorts, int>.ILPP_Create(out MessageIn, literal);
            //     MessageInput<NodeWithAllTypesOfPorts, int>.ILPP_CreatePortArray(out MessageArrayIn, literal);
            //     MessageOutput<NodeWithAllTypesOfPorts, int>.ILPP_Create(out MessageOut, literal);
            //     DSLInput<NodeWithAllTypesOfPorts, DSL, TestDSL>.ILPP_Create(out DSLIn, literal);
            //     DSLOutput<NodeWithAllTypesOfPorts, DSL, TestDSL>.ILPP_Create(out DSLOut, literal);

            ushort[] portCounters = new ushort[6];

            var il = method.Body.GetILProcessor();
            il.Emit(OpCodes.Nop);

            foreach (var portInfo in m_PortInfos)
            {
                MethodReference portCreateMethod = m_Lib.FindCreateMethodForPortType(portInfo);
                portCreateMethod = DeriveEnclosedMethodReference(portCreateMethod, portInfo.StrippedPortType);

                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldflda, portInfo.Field.Instantiated);
                il.Emit(OpCodes.Ldc_I4, portCounters[(int)portInfo.Classification]++);
                il.Emit(OpCodes.Call, portCreateMethod);
            }

            il.Emit(OpCodes.Ret);
            return method;
        }
    }
}
