using System;
using Mono.Cecil;
using Mono.Cecil.Rocks;

namespace Unity.DataFlowGraph.CodeGen
{
    partial class NodeDefinitionProcessor
    {
        (DFGLibrary.NodeDefinitionKind?, TypeReference) DetermineNodeDefinition(Diag diag)
        {
            // Determine the top level node definition derivation
            for (TypeReference currentRoot = DefinitionRoot; currentRoot != null;)
            {
                var baseInstantiated = currentRoot.InstantiatedBaseType();
                var kind = m_Lib.IdentifyDefinition(baseInstantiated.Resolve());

                if (kind.HasValue)
                    return (kind, baseInstantiated);

                currentRoot = baseInstantiated;
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
