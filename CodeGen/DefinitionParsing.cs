using System;
using Mono.Cecil;

namespace Unity.DataFlowGraph.CodeGen
{
    partial class NodeDefinitionProcessor
    {
        (FieldReference Sim, FieldReference Kernel) RecoverStaticPortFieldsFromNodeDefinition(TypeReference node)
        {
            FieldReference sim = null, kernel = null;

            var rawNodeDefinition = m_Lib.DefinitionKindToType(DFGLibrary.NodeDefinitionKind.Naked);

            // Keep drilling down a bit more, to recover static SimulationPorts and KernelPorts.
            // Note, it's a bug to _not_ hit the base node definition (otherwise we shouldn't be in this function)
            for(; !node.RefersToSame(rawNodeDefinition); node = node.InstantiatedBaseType())
            {
                switch(m_Lib.IdentifyDefinition(node))
                {
                    case DFGLibrary.NodeDefinitionKind.SimulationKernel:
                        kernel = new FieldReference(m_Lib.SimulationKernelNodeDefinition_KernelPortsField.Name, m_Lib.SimulationKernelNodeDefinition_KernelPortsField.FieldType, EnsureImported(node));
                        break;
                    case DFGLibrary.NodeDefinitionKind.Simulation:
                        sim  = new FieldReference(m_Lib.SimulationNodeDefinition_SimulationPortsField.Name, m_Lib.SimulationNodeDefinition_SimulationPortsField.FieldType, EnsureImported(node));
                        return (sim, kernel);
                    case DFGLibrary.NodeDefinitionKind.Kernel:
                        kernel = new FieldReference(m_Lib.KernelNodeDefinition_KernelPortsField.Name, m_Lib.KernelNodeDefinition_KernelPortsField.FieldType, EnsureImported(node));
                        return (sim, kernel);
                }
            }

            return (null, null);
        }

        (DFGLibrary.NodeDefinitionKind?, TypeReference) DetermineNodeDefinition(Diag diag)
        {
            // Determine the top level node definition derivation
            for (TypeReference currentRoot = DefinitionRoot; currentRoot != null;)
            {
                var firstNodeDefinitionHierarchyCandidate = currentRoot.InstantiatedBaseType();
                var kind = m_Lib.IdentifyDefinition(firstNodeDefinitionHierarchyCandidate.Resolve());

                if (kind.HasValue)
                    return (kind, firstNodeDefinitionHierarchyCandidate);

                currentRoot = firstNodeDefinitionHierarchyCandidate;
            }

            return (null, null);
        }

        void TraverseAndCollectDeclarations(Diag d, TypeReference root)
        {
            if (!Kind.HasValue)
                return;

            if (Kind.Value.IsScaffolded())
                TraverseOldStyle(d);
            else
                TraverseNewStyle(d, root);
        }

        bool DoesDefinitionRootHaveAccessTo(TypeReference node, TypeDefinition declaration)
        {
            if (node == InstantiatedDefinition || declaration.IsNestedPublic || declaration.IsNestedAssembly /* ie. IsNestedInternal */ || declaration.IsNestedFamily)
                return true;

            return false;
        }

        void TraverseNewStyle(Diag d, TypeReference node)
        {
            void Scan()
            {
                void TestAndAssign(ref TypeReference resultLocation, TypeReference interfaceWeAreLookingFor)
                {
                    if (node.IsOrImplements(interfaceWeAreLookingFor))
                    {
                        if (resultLocation != null)
                            d.DFG_UE_01(this, (TypeLocationContext)interfaceWeAreLookingFor);
                        else
                            resultLocation = node;
                    }
                }

                TestAndAssign(ref NodeDataImplementation, m_Lib.INodeDataInterface);
                TestAndAssign(ref SimulationPortImplementation, m_Lib.ISimulationPortDefinitionInterface);
                TestAndAssign(ref GraphKernelImplementation, m_Lib.IGraphKernelInterface);
                TestAndAssign(ref KernelPortImplementation, m_Lib.IKernelPortDefinitionInterface);
                TestAndAssign(ref KernelDataImplementation, m_Lib.IKernelDataInterface);
            }

            Scan();

            foreach (var nested in node.InstantiatedNestedTypes())
            {
                if (!nested.IsCompletelyClosed())
                    continue;

                // TODO: Warn the user about declaring inaccessible aspects?
                if (DoesDefinitionRootHaveAccessTo(node, nested.Definition))
                    TraverseNewStyle(d, nested.Instantiated);
            }

            // Search base classes for more aspects.
            var baseClass = node.InstantiatedBaseType();

            if (baseClass != null)
                TraverseNewStyle(d, baseClass);
        }

        void TraverseOldStyle(Diag d)
        {
            var genericArgumentList = ((GenericInstanceType)m_BaseNodeDefinitionReference).GenericArguments;

            switch (Kind.Value)
            {
                case DFGLibrary.NodeDefinitionKind.Scaffold_1:
                    SimulationPortImplementation = genericArgumentList[0];
                    break;
                case DFGLibrary.NodeDefinitionKind.Scaffold_2:
                    NodeDataImplementation = genericArgumentList[0];
                    SimulationPortImplementation = genericArgumentList[1];
                    break;
                case DFGLibrary.NodeDefinitionKind.Scaffold_3:
                    KernelDataImplementation = genericArgumentList[0];
                    KernelPortImplementation = genericArgumentList[1];
                    GraphKernelImplementation = genericArgumentList[2];
                    break;
                case DFGLibrary.NodeDefinitionKind.Scaffold_4:
                    NodeDataImplementation = genericArgumentList[0];
                    KernelDataImplementation = genericArgumentList[1];
                    KernelPortImplementation = genericArgumentList[2];
                    GraphKernelImplementation = genericArgumentList[3];
                    break;
                case DFGLibrary.NodeDefinitionKind.Scaffold_5:
                    NodeDataImplementation = genericArgumentList[0];
                    SimulationPortImplementation = genericArgumentList[1];
                    KernelDataImplementation = genericArgumentList[2];
                    KernelPortImplementation = genericArgumentList[3];
                    GraphKernelImplementation = genericArgumentList[4];
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(Kind));
            }
        }
    }


}
