using System;
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
        /// <see cref="PortInitUtility.GetInitializedPortDef{TPortDefinition}"/>
        /// </summary>
        [NSymbol] MethodReference OriginalPortInitializer;
        /// <summary>
        /// <see cref="PortInitUtility.GetInitializedPortDefImp{TPortDefinition}"/>
        /// </summary>
        [NSymbol] MethodReference ReplacedPortInitializer;

        /// <summary>
        /// <see cref="Utility.AddressOfEvenIfManaged"/> method.
        /// </summary>
        [NSymbol] MethodReference UtilityAddressOfMethod;

        public DFGAssemblyProcessor(ModuleDefinition def, DFGLibrary lib)
            : base(def)
        {
            m_Library = lib;
        }

        public override void ParseSymbols(Diag diag)
        {
            var portInitUtilityType = GetImportedReference(typeof(PortInitUtility)).Resolve();
            OriginalPortInitializer = FindGenericMethod(portInitUtilityType, nameof(PortInitUtility.GetInitializedPortDef), 1);
            ReplacedPortInitializer = FindGenericMethod(portInitUtilityType, nameof(PortInitUtility.GetInitializedPortDefImp), 1);

            var utilityType = GetImportedReference(typeof(Utility));
            UtilityAddressOfMethod = GetUniqueMethod(utilityType.Resolve(), nameof(Utility.AddressOfEvenIfManaged));
        }

        public override void PostProcess(Diag diag, out bool mutated)
        {
            // Make it possible for derived node definitions to override .BaseTraits, .SimulationStorageTraits, and .KernelStorageTraits
            m_Library.Get_BaseTraitsDefinition.Resolve().Attributes = DFGLibrary.MethodProtectedInternalVirtualFlags | Mono.Cecil.MethodAttributes.SpecialName;
            m_Library.Get_SimulationStorageTraits.Resolve().Attributes = DFGLibrary.MethodProtectedInternalVirtualFlags | Mono.Cecil.MethodAttributes.SpecialName;
            m_Library.Get_KernelStorageTraits.Resolve().Attributes = DFGLibrary.MethodProtectedInternalVirtualFlags | Mono.Cecil.MethodAttributes.SpecialName;

            foreach (var kind in (DFGLibrary.NodeTraitsKind[])Enum.GetValues(typeof(DFGLibrary.NodeTraitsKind)))
                m_Library.TraitsKindToType(kind).Resolve().IsPublic = true;

            foreach (var portClass in Enum.GetValues(typeof(DFGLibrary.PortClass)).Cast<DFGLibrary.PortClass>())
            {
                var port = m_Library.GetPort(portClass);

                port.ScalarCreateMethod.Resolve().IsPublic = true;

                // PortArray not available for dsl output
                if(port.ArrayCreateMethod != null)
                    port.ArrayCreateMethod.Resolve().IsPublic = true;
            }

            m_Library.KernelStorageDefinitionType.Resolve().IsPublic = true;
            m_Library.KernelStorageDefinitionCreateMethod.Resolve().IsPublic = true;
            m_Library.SimulationStorageDefinitionType.Resolve().IsPublic = true;
            m_Library.SimulationStorageDefinitionCreateMethod.Resolve().IsPublic = true;
            m_Library.SimulationStorageDefinitionNoPortsCreateMethod.Resolve().IsPublic = true;
            m_Library.SimulationStorageDefinitionNoDataCreateMethod.Resolve().IsPublic = true;

            m_Library.IPortDefinitionInitializerType.Resolve().IsPublic = true;

            m_Library.VirtualTableField.Resolve().Attributes = Mono.Cecil.FieldAttributes.FamORAssem;
            m_Library.VirtualTableField.FieldType.Resolve().Attributes = Mono.Cecil.TypeAttributes.NestedFamORAssem;

            // Swap the bodies of PortInitUtility.GetInitializedPortDef for PortInitUtility.GetInitializedPortDefImp.
            OriginalPortInitializer.Resolve().Body = ReplacedPortInitializer.Resolve().Body;

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
