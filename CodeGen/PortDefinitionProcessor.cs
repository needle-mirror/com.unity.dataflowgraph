using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace Unity.DataFlowGraph.CodeGen
{
    class PortDefinitionProcessor : DefinitionProcessor
    {
        public enum InputOrOutputPortType { Input, Output }

        struct PortInfo
        {
            public FieldDefinition PortField;
            public TypeReference PortType;
            public bool IsInput;
        }

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
            foreach (var portField in DefinitionRoot.Fields)
            {
                if (!portField.IsPublic || portField.IsStatic)
                {
                    diag.DFG_UE_08(this, portField);
                    continue;
                }

                var fieldType = portField.FieldType.GetElementType();
                bool isArray = fieldType.RefersToSame(m_Lib.PortArrayType);
                var portType = isArray ? ((GenericInstanceType) portField.FieldType).GenericArguments[0] : portField.FieldType;

                var isInputOrOutput = IdentifyInputOrOutputPortType(portType);
                if (!isInputOrOutput.HasValue)
                {
                    diag.DFG_UE_09(this, portField);
                    continue;
                }

                m_PortInfos.Add(new PortInfo{IsInput = isInputOrOutput.Value == InputOrOutputPortType.Input, PortField = portField, PortType = fieldType});
            }
        }

        public override void AnalyseConsistency(Diag diag)
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

            DefinitionRoot.Methods.Add(
                SynthesizePortInitializer(m_Lib.IPortDefinitionInitializedMethod));

            DefinitionRoot.Methods.Add(
                SynthesizePortCounter(m_Lib.IPortDefinitionGetInputPortCountMethod, (ushort)m_PortInfos.Count(p => p.IsInput)));

            DefinitionRoot.Methods.Add(
                SynthesizePortCounter(m_Lib.IPortDefinitionGetOutputPortCountMethod, (ushort)m_PortInfos.Count(p => !p.IsInput)));

            mutated = true;
        }

        MethodDefinition SynthesizePortInitializer(MethodDefinition interfaceMethod)
        {
            var method = CreateEmptyInterfaceMethodImplementation(interfaceMethod);

            // Emit IL to initialization of all ports for either an ISimulationPortDefinition or IKernelPortDefinition.
            // For example, for the test node NodeWithAllTypesOfPorts, we would produce the following IL:
            //     MessageIn = MessageInput<NodeWithAllTypesOfPorts, int>.Create(new InputPortID(new PortStorage(uniqueInputPort++)));
            //     MessageArrayIn = PortArray<MessageInput<NodeWithAllTypesOfPorts, int>>.Create(new InputPortID(new PortStorage(uniqueInputPort++)));
            //     MessageOut = MessageOutput<NodeWithAllTypesOfPorts, int>.Create(new OutputPortID(new PortStorage(uniqueOutputPort++)));
            //     DSLIn = DSLInput<NodeWithAllTypesOfPorts, DSL, TestDSL>.Create(new InputPortID(new PortStorage(uniqueInputPort++)));
            //     DSLOut = DSLOutput<NodeWithAllTypesOfPorts, DSL, TestDSL>.Create(new OutputPortID(new PortStorage(uniqueOutputPort++)));
            var il = method.Body.GetILProcessor();
            il.Emit(OpCodes.Nop);

            foreach (var portInfo in m_PortInfos)
            {
                var portField = portInfo.PortField;
                var portFieldRef = FormClassInstantiatedFieldReference(portField);
                MethodReference portCreateMethod = m_Lib.FindCreateMethodForPortType(portInfo.PortType);

                var genPortCreateMethod = DeriveEnclosedMethodReference(portCreateMethod, (GenericInstanceType)portField.FieldType);
                // Take the port type (eg. DataInput`2) and add the positional generic arguments (eg. DataInput`2<!0,!1>)
                // to form the proper return type.
                genPortCreateMethod.ReturnType =
                    EnsureImported(portInfo.PortType).MakeGenericInstanceType(portInfo.PortType.GenericParameters.ToArray());

                EmitPortInitializerIL(il, portInfo.IsInput, method, genPortCreateMethod, portFieldRef);
            }
            il.Emit(OpCodes.Ret);
            return method;
        }

        void EmitPortInitializerIL(ILProcessor il, bool isInput, MethodDefinition method, MethodReference genPortCreateMethod, FieldReference portFieldRef)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(isInput ? OpCodes.Ldarg_1 : OpCodes.Ldarg_2);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Conv_U2);
            il.Emit(OpCodes.Starg_S, isInput ? method.Parameters[0] : method.Parameters[1]);
            il.Emit(OpCodes.Newobj, m_Lib.PortStorageConstructor);
            il.Emit(OpCodes.Newobj, isInput ? m_Lib.InputPortIDConstructor : m_Lib.OutputPortIDConstructor);
            il.Emit(OpCodes.Call, genPortCreateMethod);
            il.Emit(OpCodes.Stfld, portFieldRef);
        }

        static InputOrOutputPortType? IdentifyInputOrOutputPortType(TypeReference portType)
        {
            if (portType.GetElementType().FullName == typeof(MessageInput<,>).FullName)
                return InputOrOutputPortType.Input;
            if (portType.GetElementType().FullName == typeof(MessageOutput<,>).FullName)
                return InputOrOutputPortType.Output;
            if (portType.GetElementType().FullName == typeof(DataInput<,>).FullName)
                return InputOrOutputPortType.Input;
            if (portType.GetElementType().FullName == typeof(DataOutput<,>).FullName)
                return InputOrOutputPortType.Output;
            if (portType.GetElementType().FullName == typeof(DSLInput<,,>).FullName)
                return InputOrOutputPortType.Input;
            if (portType.GetElementType().FullName == typeof(DSLOutput<,,>).FullName)
                return InputOrOutputPortType.Output;
            return null;
        }

        MethodDefinition SynthesizePortCounter(MethodDefinition interfaceMethod, ushort count)
        {
            var method = CreateEmptyInterfaceMethodImplementation(interfaceMethod);

            // Emit IL to achieve:
            //     return <count>;
            var il = method.Body.GetILProcessor();
            il.Emit(OpCodes.Ldc_I4, count);
            il.Emit(OpCodes.Ret);
            return method;
        }
    }
}
