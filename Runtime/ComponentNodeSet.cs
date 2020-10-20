using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace Unity.DataFlowGraph
{
    public partial class NodeSetAPI
    {
        internal ComponentSystemBase HostSystem;
        BlitList<AtomicSafetyManager.ECSTypeAndSafety> m_ActiveComponentTypes;
        /// <summary>
        /// Contains the last <see cref="JobHandle"/> returned from
        /// <see cref="Update(JobHandle, ComponentSystemDispatch)"/>.
        /// </summary>
        JobHandle m_LastJobifiedUpdateHandle;

        internal NodeSetAPI(ComponentSystemBase hostSystem, ComponentSystemDispatch _)
            : this(hostSystem, InternalDispatch.Tag)
        {
            if (hostSystem == null)
            {
                // In case of cascading constructors, an object can be partially constructed but still be 
                // GC collected and finalized.
                InternalDispose();
                throw new ArgumentNullException(nameof(hostSystem));
            }

            m_ActiveComponentTypes = new BlitList<AtomicSafetyManager.ECSTypeAndSafety>(0, Allocator.Persistent);
        }
   
        internal JobHandle Update(JobHandle inputDeps, ComponentSystemDispatch _)
        {
            if (HostSystem == null)
                throw new InvalidOperationException($"This {typeof(NodeSet)} was not created together with a job component system");

            UpdateInternal(inputDeps);

            m_LastJobifiedUpdateHandle = ProtectFenceFromECSTypes(DataGraph.RootFence);
            return m_LastJobifiedUpdateHandle;
        }
        
        unsafe JobHandle ProtectFenceFromECSTypes(JobHandle inputDeps)
        {

#if ENABLE_UNITY_COLLECTIONS_CHECKS

            var componentSafetyManager = &HostSystem.World.EntityManager.GetCheckedEntityDataAccess()->DependencyManager->Safety;

            for (int i = 0; i < m_ActiveComponentTypes.Count; ++i)
            {
                m_ActiveComponentTypes[i].CopySafetyHandle(componentSafetyManager);
            }

            inputDeps = AtomicSafetyManager.MarkHandlesAsUsed(inputDeps, m_ActiveComponentTypes.Pointer, m_ActiveComponentTypes.Count);
#endif

            return inputDeps;
        }

        internal void RegisterECSPorts(PortDescription desc)
        {
            if (HostSystem == null)
                return;

            foreach(var c in desc.ComponentTypes)
            {
                AddWriter(c);
            }
        }

        unsafe void AddWriter(ComponentType component)
        {
            // TODO: take argument instead. AtomicSafetyManager does not yet support read-only
            // For now, DFG takes write dependency on every type in the graph
            component.AccessModeType = ComponentType.AccessMode.ReadWrite;

            if (!HasReaderOrWriter(component))
            {
                if (component.IsZeroSized)
                    throw new InvalidNodeDefinitionException($"ECS types on ports cannot be zero-sized ({component})");

                HostSystem.CheckedState()->AddReaderWriter(component);

                m_ActiveComponentTypes.Add(new AtomicSafetyManager.ECSTypeAndSafety { Type = component });
            }
        }

        bool HasReaderOrWriter(ComponentType c)
        {
            for (int i = 0; i < m_ActiveComponentTypes.Count; ++i)
            {
                if (m_ActiveComponentTypes[i].Type.TypeIndex == c.TypeIndex)
                {
                    return true;
                }
            }

            return false;
        }

        internal BlitList<AtomicSafetyManager.ECSTypeAndSafety> GetActiveComponentTypes()
            => m_ActiveComponentTypes;
    }
}
