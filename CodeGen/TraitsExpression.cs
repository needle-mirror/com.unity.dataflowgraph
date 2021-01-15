using System.Linq;
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
        /// Create a matching <see cref="NodeTraitsBase"/> type given a <paramref name="kind"/>
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
                case DFGLibrary.NodeTraitsKind._5:
                    instance.GenericArguments.Add(NodeDataImplementation);
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
            var newMethod = CreateEmptyProtectedInternalMethodOverride(m_Lib.Get_BaseTraitsDefinition);

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

        void CreateSimulationStorageTraitsOverride(Diag d)
        {
            var newMethod = CreateEmptyProtectedInternalMethodOverride(m_Lib.Get_SimulationStorageTraits);

            newMethod.Body.InitLocals = true;
            newMethod.Body.Variables.Add(new VariableDefinition(m_Lib.SimulationStorageDefinitionType));

            //  protected override SimulationStorageDefinition SimulationStorageTraits => {
            //      return SimulationStorageDefinition.Create<TNodeData, TSimPorts>(nodeDataIsManaged: ?, isScaffolded: ?);
            //  }
            var il = newMethod.Body.GetILProcessor();
            il.Emit(OpCodes.Nop);

            GenericInstanceMethod genMethod;
            if (NodeDataImplementation != null)
            {
                var nodeDataIsManaged = NodeDataImplementation.Resolve().CustomAttributes.Any(a => a.AttributeType.RefersToSame(m_Lib.ManagedNodeDataAttribute));
                il.Emit(nodeDataIsManaged ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);

                if (SimulationPortImplementation != null)
                    genMethod = m_Lib.SimulationStorageDefinitionCreateMethod.MakeGenericInstanceMethod(DefinitionRoot, NodeDataImplementation, SimulationPortImplementation);
                else
                    genMethod = m_Lib.SimulationStorageDefinitionNoPortsCreateMethod.MakeGenericInstanceMethod(DefinitionRoot, NodeDataImplementation);
            }
            else
            {
                genMethod = m_Lib.SimulationStorageDefinitionNoDataCreateMethod.MakeGenericInstanceMethod(SimulationPortImplementation);
            }
            il.Emit(OpCodes.Call, genMethod);

            il.Emit(OpCodes.Stloc_0);
            il.Emit(OpCodes.Ldloc_0);
            il.Emit(OpCodes.Ret);

            DefinitionRoot.Methods.Add(newMethod);

            var property = new PropertyDefinition(nameof(NodeDefinition.SimulationStorageTraits), PropertyAttributes.None, m_Lib.SimulationStorageDefinitionType) { HasThis = true, GetMethod = newMethod };
            DefinitionRoot.Properties.Add(property);
        }

        void CreateKernelStorageTraitsOverride(Diag d)
        {
            var newMethod = CreateEmptyProtectedInternalMethodOverride(m_Lib.Get_KernelStorageTraits);

            newMethod.Body.InitLocals = true;
            newMethod.Body.Variables.Add(new VariableDefinition(m_Lib.KernelStorageDefinitionType));

            //  protected override KernelStorageDefinition KernelStorageTraits => {
            //      return KernelStorageDefinition.Create<TKernelData, TKernelPortDefinition, TUserKernel>(isComponentNode: ?);
            //  }
            var il = newMethod.Body.GetILProcessor();
            il.Emit(OpCodes.Nop);

            var isComponentNode = DefinitionRoot.RefersToSame(m_Lib.InternalComponentNodeType);
            il.Emit(isComponentNode ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);

            var causesSideEffects = GraphKernelImplementation.Resolve().CustomAttributes.Any(ca => ca.AttributeType.RefersToSame(m_Lib.CausesSideEffectsAttributeType));
            il.Emit(causesSideEffects ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);

            var genMethod = m_Lib.KernelStorageDefinitionCreateMethod.MakeGenericInstanceMethod(DefinitionRoot, KernelDataImplementation, KernelPortImplementation, GraphKernelImplementation);
            il.Emit(OpCodes.Call, genMethod);

            il.Emit(OpCodes.Stloc_0);
            il.Emit(OpCodes.Ldloc_0);
            il.Emit(OpCodes.Ret);

            DefinitionRoot.Methods.Add(newMethod);

            var property = new PropertyDefinition(nameof(NodeDefinition.KernelStorageTraits), PropertyAttributes.None, m_Lib.KernelStorageDefinitionType) { HasThis = true, GetMethod = newMethod };
            DefinitionRoot.Properties.Add(property);
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

            if (NodeDataImplementation != null || SimulationPortImplementation != null)
                CreateSimulationStorageTraitsOverride(d);

            if (KernelDataImplementation != null && KernelPortImplementation != null && GraphKernelImplementation != null)
                CreateKernelStorageTraitsOverride(d);
        }
    }
}
