using Mono.Cecil;
using Mono.Cecil.Cil;
using MethodAttributes = Mono.Cecil.MethodAttributes;

namespace Unity.DataFlowGraph.CodeGen
{
    partial class NodeDefinitionProcessor
    {
        /// <summary>
        /// Determine the optional constituency of a node in terms of <code>NodeTraits<></code> expressions
        /// </summary>
        DFGLibrary.NodeTraitsKind? DetermineTraitsKind()
        {
            var hasKernelLikeConstituency = Kind.Value.HasKernelAspects();

            // It's naked, see if we can infer some constitution
            if (!hasKernelLikeConstituency.HasValue)
            {
                hasKernelLikeConstituency = GraphKernelImplementation != null && KernelDataImplementation != null && KernelPortImplementation != null;
            }

            if (hasKernelLikeConstituency.Value)
            {
                if(NodeDataImplementation != null)
                {
                    if (SimulationPortImplementation != null)
                        return DFGLibrary.NodeTraitsKind._5;

                    return DFGLibrary.NodeTraitsKind._4;
                }

                return DFGLibrary.NodeTraitsKind._3;
            }
            else if (SimulationPortImplementation != null)
            {
                if (NodeDataImplementation != null)
                    return DFGLibrary.NodeTraitsKind._2;

                return DFGLibrary.NodeTraitsKind._1;
            }

            return null;
        }

        /// <summary>
        /// Create a matching <code>NodeTraits<></code> type given a <paramref name="kind"/>
        /// </summary>
        GenericInstanceType CreateTraitsType(DFGLibrary.NodeTraitsKind kind)
        {
            TypeReference definition = m_Lib.TraitsKindToType(kind);
            GenericInstanceType instance = new GenericInstanceType(definition);

            void AddKernelAspects()
            {
                instance.GenericArguments.Add(KernelDataImplementation);
                instance.GenericArguments.Add(KernelPortImplementation);
                instance.GenericArguments.Add(GraphKernelImplementation);
            }

            switch (kind)
            {
                case DFGLibrary.NodeTraitsKind._1:
                    instance.GenericArguments.Add(SimulationPortImplementation);
                    break;

                case DFGLibrary.NodeTraitsKind._2:
                    instance.GenericArguments.Add(NodeDataImplementation);
                    instance.GenericArguments.Add(SimulationPortImplementation);
                    break;

                case DFGLibrary.NodeTraitsKind._3:
                    AddKernelAspects();
                    break;

                case DFGLibrary.NodeTraitsKind._4:
                    instance.GenericArguments.Add(NodeDataImplementation);
                    AddKernelAspects();
                    break;

                case DFGLibrary.NodeTraitsKind._5:
                    instance.GenericArguments.Add(NodeDataImplementation);
                    instance.GenericArguments.Add(SimulationPortImplementation);
                    AddKernelAspects();
                    break;
            }

            return instance;
        }

        (FieldDefinition Def, GenericInstanceType TraitsType) CreateTraitsFields(Diag d, string name, DFGLibrary.NodeTraitsKind kind)
        {
            var fieldType = CreateTraitsType(kind);
            var field = new FieldDefinition(MakeSymbol(name), FieldAttributes.Private, fieldType);
            DefinitionRoot.Fields.Add(field);

            return (field, fieldType);
        }

        MethodDefinition CreateTraitsFieldInitializerMethod(Diag d, DFGLibrary.NodeTraitsKind kind, (FieldDefinition Def, GenericInstanceType TraitsType) field)
        {
            var newMethod = new MethodDefinition(
                MakeSymbol("AssignTraitsOnConstruction"),
                MethodAttributes.Private | MethodAttributes.HideBySig,
                Module.TypeSystem.Void
            );


            //  AssignTraitsOnConstruction() {
            //      this.{CG}m_Traits = new NodeTraits<?>();
            //  }
            newMethod.Body.Instructions.Add(Instruction.Create(OpCodes.Nop));
            newMethod.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            newMethod.Body.Instructions.Add(Instruction.Create(OpCodes.Newobj, kind.GetConstructor(field.TraitsType, m_Lib)));
            newMethod.Body.Instructions.Add(Instruction.Create(OpCodes.Stfld, FormClassInstantiatedFieldReference(field.Def)));
            newMethod.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));

            DefinitionRoot.Methods.Add(newMethod);

            return newMethod;
        }

        PropertyDefinition CreateBaseTraitsOverride(Diag d, FieldDefinition def)
        {
            var newMethod = new MethodDefinition(
                m_Lib.Get_BaseTraitsDefinition.Name,
                DFGLibrary.MethodProtectedOverrideFlags | MethodAttributes.SpecialName,
                m_Lib.NodeTraitsBaseDefinition
            ) { HasThis = true };

            //  protected override NodeTraitsBase BaseTraits => {
            //      return this.{CG}m_Traits;
            //  }
            newMethod.Body.Instructions.Add(Instruction.Create(OpCodes.Nop));
            newMethod.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            newMethod.Body.Instructions.Add(Instruction.Create(OpCodes.Ldfld, FormClassInstantiatedFieldReference(def)));
            newMethod.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));

            DefinitionRoot.Methods.Add(newMethod);

            var property = new PropertyDefinition(nameof(NodeDefinition.BaseTraits), PropertyAttributes.None, m_Lib.NodeTraitsBaseDefinition) { HasThis = true, GetMethod = newMethod };
            DefinitionRoot.Properties.Add(property);

            return property;
        }

        void CreateTraitsExpression(Diag d)
        {
            void EnsureImportedIfNotNull(ref TypeReference t)
            {
                if (t != null)
                    t = EnsureImported(t);
            }
            EnsureImportedIfNotNull(ref NodeDataImplementation);
            EnsureImportedIfNotNull(ref SimulationPortImplementation);
            EnsureImportedIfNotNull(ref KernelPortImplementation);
            EnsureImportedIfNotNull(ref GraphKernelImplementation);
            EnsureImportedIfNotNull(ref KernelDataImplementation);

            var field = CreateTraitsFields(d, "m_Traits", TraitsKind.Value);
            var initializer = CreateTraitsFieldInitializerMethod(d, TraitsKind.Value, field);
            CreateBaseTraitsOverride(d, field.Def);

            EmitCallToMethodInDefaultConstructor(FormClassInstantiatedMethodReference(initializer));
        }
    }
}
