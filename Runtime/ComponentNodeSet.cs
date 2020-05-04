using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace Unity.DataFlowGraph
{
    public partial class NodeSet
    {
        internal ComponentSystemBase HostSystem;
        BlitList<AtomicSafetyManager.ECSTypeAndSafety> m_ActiveComponentTypes;
        /// <summary>
        /// Contains the last <see cref="JobHandle"/> returned from
        /// <see cref="Update(JobHandle)"/>.
        /// </summary>
        JobHandle m_LastJobifiedUpdateHandle;

        /// <summary>
        /// Initializes this <see cref="NodeSet"/> in a mode that's compatible with running together with ECS,
        /// through the use of <see cref="ComponentNode"/>s.
        /// The <paramref name="hostSystem"/> and this instance are tied together from this point, and you must
        /// update this set using the <see cref="Update(JobHandle)"/> function.
        /// See also <seealso cref="NodeSet()"/>.
        /// </summary>
        /// <remarks>
        /// Any instantiated nodes with <see cref="IKernelPortDefinition"/>s containing ECS types will be added
        /// as dependencies to <paramref name="hostSystem"/>.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// Thrown if the <paramref name="hostSystem"/> is null
        /// </exception>
        public NodeSet(ComponentSystemBase hostSystem)
            : this()
        {
            if (hostSystem == null)
            {
                // In case of cascading constructors, an object can be partially constructed but still be 
                // GC collected and finalized.
                Dispose();
                throw new ArgumentNullException(nameof(hostSystem));
            }

            HostSystem = hostSystem;
            m_ActiveComponentTypes = new BlitList<AtomicSafetyManager.ECSTypeAndSafety>(0, Allocator.Persistent);
        }

        /// <summary>
        /// Overload of <see cref="Update()"/>. Use this function inside a <see cref="ComponentSystemBase"/>.
        /// </summary>
        /// <remarks>
        /// This function is only compatible if you used the <see cref="NodeSet(ComponentSystemBase)"/> constructor.
        /// </remarks>
        /// <param name="inputDeps">
        /// Input dependencies derived from <see cref="JobComponentSystem.OnUpdate(JobHandle)"/> or <see cref="SystemBase.Dependency"/>, pass the 
        /// input dependencies into this function.
        /// </param>
        /// <returns>
        /// A <see cref="JobHandle"/> that should be returned or included in a dependency chain inside 
        /// <see cref="JobComponentSystem.OnUpdate(JobHandle)"/> or assigned to <see cref="SystemBase.Dependency"/>.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// Can be thrown if this <see cref="NodeSet"/> was created without using the ECS constructor 
        /// <see cref="NodeSet(ComponentSystemBase)"/>, in which case you need to use the 
        /// <see cref="Update()"/> function instead.
        /// See also base documentation for <see cref="Update"/>
        /// </exception>
        public JobHandle Update(JobHandle inputDeps)
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
            var componentSafetyManager = HostSystem.World.EntityManager.SafetyHandles;

            for(int i = 0; i < m_ActiveComponentTypes.Count; ++i)
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

        void AddWriter(ComponentType component)
        {
            // TODO: take argument instead. AtomicSafetyManager does not yet support read-only
            // For now, DFG takes write dependency on every type in the graph
            component.AccessModeType = ComponentType.AccessMode.ReadWrite;

            if (!HasReaderOrWriter(component))
            {
                if (component.IsZeroSized)
                    throw new InvalidNodeDefinitionException($"ECS types on ports cannot be zero-sized ({component})");

                HostSystem.AddReaderWriter(component);
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

