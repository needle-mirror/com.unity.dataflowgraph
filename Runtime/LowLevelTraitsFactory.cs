using System;
using System.Linq;
using System.Reflection;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Burst;

namespace Unity.DataFlowGraph
{
    struct LowLevelTraitsFactory<TNodeData, TSimPorts, TKernelData, TKernelPortDefinition, TUserKernel>
       where TNodeData : struct, INodeData
       where TSimPorts : struct, ISimulationPortDefinition
       where TKernelData : struct, IKernelData
       where TKernelPortDefinition : struct, IKernelPortDefinition
       where TUserKernel : struct, IGraphKernel<TKernelData, TKernelPortDefinition>
    {
        /// <param name="hostNodeType">
        /// Specifically the type which has the TKernelPortDefinition as a field (can be the whole node definition).
        /// </param>
        internal static LLTraitsHandle Create(Type hostNodeType)
        {
            bool nodeDataIsManaged = typeof(TNodeData).GetCustomAttributes().Any(a => a is ManagedAttribute);
            ValidateRulesForStorage(hostNodeType, nodeDataIsManaged);

            var vtable = LowLevelNodeTraits.VirtualTable.Create();

            if (BurstConfig.IsBurstEnabled && typeof(TUserKernel).GetCustomAttributes().Any(a => a is BurstCompileAttribute))
                vtable.KernelFunction = RenderKernelFunction.GetBurstedFunction<TKernelData, TKernelPortDefinition, TUserKernel>();
            else
                vtable.KernelFunction = RenderKernelFunction.GetManagedFunction<TKernelData, TKernelPortDefinition, TUserKernel>();

            var traits = new LowLevelNodeTraits(
               CreateStorage(nodeDataIsManaged),
               vtable,
               new DataPortDeclarations(hostNodeType, typeof(TKernelPortDefinition))
           );

            var handle = LLTraitsHandle.Create();
            handle.Resolve() = traits;
            return handle;
        }

        static LowLevelNodeTraits.StorageDefinition CreateStorage(bool nodeDataIsManaged)
        {
            return new LowLevelNodeTraits.StorageDefinition(
                nodeDataIsManaged,
                SimpleType.Create<TNodeData>(),
                SimpleType.Create<TSimPorts>(),
                SimpleType.Create<TKernelData>(),
                SimpleType.Create<TKernelPortDefinition>(),
                SimpleType.Create<TUserKernel>()
            );
        }

        static void ValidateRulesForStorage(Type hostNodeType, bool nodeDataIsManaged)
        {
            LowLevelTraitsFactory<TNodeData, TSimPorts>.ValidateRulesForStorage(hostNodeType, nodeDataIsManaged);

            if (!UnsafeUtility.IsUnmanaged<TKernelData>())
                throw new InvalidNodeDefinitionException($"Kernel data type {typeof(TKernelData)} on node definition {hostNodeType} is not unmanaged");

            if (!UnsafeUtility.IsUnmanaged<TUserKernel>())
                throw new InvalidNodeDefinitionException($"Kernel type {typeof(TUserKernel)} on node definition {hostNodeType} is not unmanaged");
        }
    }

    struct LowLevelTraitsFactory<TNodeData, TSimPorts>
       where TNodeData : struct, INodeData
       where TSimPorts : struct, ISimulationPortDefinition
    {

        /// <param name="hostNodeType">
        /// Specifically the type which has the TKernelPortDefinition as a field (can be the whole node definition).
        /// </param>
        internal static LLTraitsHandle Create(Type hostNodeType)
        {
            bool nodeDataIsManaged = typeof(TNodeData).GetCustomAttributes().Any(a => a is ManagedAttribute);
            ValidateRulesForStorage(hostNodeType, nodeDataIsManaged);
            var traits = new LowLevelNodeTraits(CreateStorage(nodeDataIsManaged), LowLevelNodeTraits.VirtualTable.Create());
            var handle = LLTraitsHandle.Create();
            handle.Resolve() = traits;
            return handle;
        }

        static LowLevelNodeTraits.StorageDefinition CreateStorage(bool nodeDataIsManaged)
        {
            return new LowLevelNodeTraits.StorageDefinition(nodeDataIsManaged, SimpleType.Create<TNodeData>(), SimpleType.Create<TSimPorts>(), new SimpleType(), new SimpleType(), new SimpleType());
        }

        internal static void ValidateRulesForStorage(Type hostNodeType, bool nodeDataIsManaged)
        {
            if (!nodeDataIsManaged && !UnsafeUtility.IsUnmanaged<TNodeData>())
                throw new InvalidNodeDefinitionException($"Node data type {typeof(TNodeData)} on node definition {hostNodeType} is not unmanaged, " +
                    $"add the attribute [Managed] to the type if you need to store references in your data");
        }
    }
}
