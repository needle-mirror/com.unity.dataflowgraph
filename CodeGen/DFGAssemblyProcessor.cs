﻿using System;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Unity.DataFlowGraph.CodeGen
{
    /// <summary>
    /// Processor for rewriting parts of the DFG assembly
    /// </summary>
    class DFGAssemblyProcessor : ASTProcessor
    {
        DFGLibrary m_Library;

        /// <summary>
        /// One and two generic parameter variants of
        /// <code>PortInitUtility.GetInitializedPortDef</code> and <code>PortInitUtility.GetInitializedPortDefImp</code>
        /// </summary>
        (MethodReference Original, MethodReference Replacement)[] PortInitUtilityGetInitializedPortDefMethods;

        /// <summary>
        /// <see cref="Utility.AddressOfEvenIfManaged"/> method.
        /// </summary>
        MethodReference UtilityAddressOfMethod;

        public DFGAssemblyProcessor(ModuleDefinition def, DFGLibrary lib)
            : base(def)
        {
            m_Library = lib;
        }

        public override void ParseSymbols(Diag diag)
        {
            var portInitUtilityType = GetImportedReference(typeof(PortInitUtility));
            PortInitUtilityGetInitializedPortDefMethods = new (MethodReference Original, MethodReference Replacement)[2];
            for (var i = 0; i < 2; ++i)
            {
                PortInitUtilityGetInitializedPortDefMethods[i].Original =
                    FindGenericMethod(portInitUtilityType, nameof(PortInitUtility.GetInitializedPortDef), i+1);
                PortInitUtilityGetInitializedPortDefMethods[i].Replacement =
                    FindGenericMethod(portInitUtilityType, nameof(PortInitUtility.GetInitializedPortDefImp), i+1);
            }

            var utilityType = GetImportedReference(typeof(Utility));
            UtilityAddressOfMethod = utilityType.Resolve().Methods.Single(
                m => m.Name == nameof(Utility.AddressOfEvenIfManaged) && m.Parameters.Count == 1);
        }

        public override void AnalyseConsistency(Diag diag)
        {
            if (PortInitUtilityGetInitializedPortDefMethods.Any(m => m.Original == null || m.Replacement == null))
                diag.DFG_IE_01(this, GetType().GetField(nameof(PortInitUtilityGetInitializedPortDefMethods), BindingFlags.Instance | BindingFlags.NonPublic));

            if (UtilityAddressOfMethod == null)
                diag.DFG_IE_01(this, GetType().GetField(nameof(UtilityAddressOfMethod), BindingFlags.Instance | BindingFlags.NonPublic));
        }

        public override void PostProcess(Diag diag, out bool mutated)
        {
            // Make it possible for derived node definitions to override .BaseTraits, .SimulationStorageTraits, and .KernelStorageTraits
            m_Library.Get_BaseTraitsDefinition.Resolve().Attributes = DFGLibrary.MethodProtectedInternalVirtualFlags | Mono.Cecil.MethodAttributes.SpecialName;
            m_Library.Get_SimulationStorageTraits.Resolve().Attributes = DFGLibrary.MethodProtectedInternalVirtualFlags | Mono.Cecil.MethodAttributes.SpecialName;
            m_Library.Get_KernelStorageTraits.Resolve().Attributes = DFGLibrary.MethodProtectedInternalVirtualFlags | Mono.Cecil.MethodAttributes.SpecialName;

            foreach (var kind in (DFGLibrary.NodeTraitsKind[])Enum.GetValues(typeof(DFGLibrary.NodeTraitsKind)))
                m_Library.TraitsKindToType(kind).Resolve().IsPublic = true;

            foreach (var portCreateMethod in m_Library.PortCreateMethods)
                portCreateMethod.Resolve().IsPublic = true;

            m_Library.KernelStorageDefinitionType.Resolve().IsPublic = true;
            m_Library.KernelStorageDefinitionCreateMethod.Resolve().IsPublic = true;
            m_Library.SimulationStorageDefinitionType.Resolve().IsPublic = true;
            m_Library.SimulationStorageDefinitionCreateMethod.Resolve().IsPublic = true;
            m_Library.SimulationStorageDefinitionNoPortsCreateMethod.Resolve().IsPublic = true;
            m_Library.SimulationStorageDefinitionNoDataCreateMethod.Resolve().IsPublic = true;

            m_Library.InputPortIDConstructor.Resolve().IsPublic = true;
            m_Library.OutputPortIDConstructor.Resolve().IsPublic = true;

            m_Library.PortStorageType.Resolve().IsPublic = true;

            m_Library.IPortDefinitionInitializerType.Resolve().IsPublic = true;

            m_Library.VirtualTableField.Resolve().Attributes = Mono.Cecil.FieldAttributes.FamORAssem;
            m_Library.VirtualTableField.FieldType.Resolve().Attributes = Mono.Cecil.TypeAttributes.NestedFamORAssem;

            // Swap the bodies of PortInitUtility.GetInitializedPortDef for PortInitUtility.GetInitializedPortDefImp.
            for (var i = 0; i < 2; ++i)
                PortInitUtilityGetInitializedPortDefMethods[i].Original.Resolve().Body =
                    PortInitUtilityGetInitializedPortDefMethods[i].Replacement.Resolve().Body;

            // Generate an implementation for our local version of the UnsafeUtilityExtensions.AddressOf<T>() method.
            var body = UtilityAddressOfMethod.Resolve().Body;
            body.Instructions.Clear();
            var il = body.GetILProcessor();
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Ret));

            mutated = true;
        }
    }
}
