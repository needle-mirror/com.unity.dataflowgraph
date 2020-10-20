using System;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.DataFlowGraph
{
    class DefaultManagedAllocator<T> : IManagedMemoryPoolAllocator
        where T : struct
    {
        public int ObjectSize => UnsafeUtility.SizeOf<T>();

        public unsafe void* AllocatePrepinnedGCArray(int count, out ulong gcHandle)
        {
            var gc = new T[count];
            return UnsafeUtility.PinGCArrayAndGetDataAddress(gc, out gcHandle);
        }
    }

    abstract class NodeTraitsBase
    {
        internal abstract IManagedMemoryPoolAllocator ManagedAllocator { get; }

        internal abstract LLTraitsHandle CreateNodeTraits(System.Type superType, SimulationStorageDefinition simStorage, KernelStorageDefinition kernelStorage);
        internal virtual INodeData DebugGetNodeData(NodeSetAPI set, NodeHandle handle) => null;
        internal virtual IKernelData DebugGetKernelData(NodeSetAPI set, NodeHandle handle) => null;
        internal virtual IKernelData DebugGetKernelData(KernelLayout.Pointers kPointers) => null;
        internal virtual IGraphKernel DebugGetKernel(KernelLayout.Pointers kPointers) => null;
        internal virtual IKernelPortDefinition DebugGetKernelPorts(KernelLayout.Pointers kPointers) => null;
    }

    sealed class NodeTraits<TSimPorts> : NodeTraitsBase
        where TSimPorts : struct, ISimulationPortDefinition
    {
        internal override LLTraitsHandle CreateNodeTraits(System.Type superType, SimulationStorageDefinition simStorage, KernelStorageDefinition kernelStorage)
            => LowLevelTraitsFactory.Create(simStorage);
        internal override IManagedMemoryPoolAllocator ManagedAllocator => throw new NotImplementedException();
    }

    sealed class NodeTraits<TNodeData, TSimPorts> : NodeTraitsBase
        where TNodeData : struct, INodeData
        where TSimPorts : struct, ISimulationPortDefinition
    {
        DefaultManagedAllocator<TNodeData> m_Allocator = new DefaultManagedAllocator<TNodeData>();

        internal override INodeData DebugGetNodeData(NodeSetAPI set, NodeHandle handle) => set.GetNodeData<TNodeData>(handle);

        internal override LLTraitsHandle CreateNodeTraits(System.Type superType, SimulationStorageDefinition simStorage, KernelStorageDefinition kernelStorage)
            => LowLevelTraitsFactory.Create(simStorage);
        internal override IManagedMemoryPoolAllocator ManagedAllocator => m_Allocator;
    }

    sealed class NodeTraits<TKernelData, TKernelPortDefinition, TGraphKernel> : NodeTraitsBase
        where TKernelData : struct, IKernelData
        where TKernelPortDefinition : struct, IKernelPortDefinition
        where TGraphKernel : struct, IGraphKernel<TKernelData, TKernelPortDefinition>
    {
        internal override IKernelData DebugGetKernelData(NodeSetAPI set, NodeHandle handle) => set.GetKernelData<TKernelData>(handle);
        internal override unsafe IKernelData DebugGetKernelData(KernelLayout.Pointers kPointers) => UnsafeUtility.AsRef<TKernelData>(kPointers.Data);
        internal override unsafe IGraphKernel DebugGetKernel(KernelLayout.Pointers kPointers) => UnsafeUtility.AsRef<TGraphKernel>(kPointers.Kernel);
        internal override unsafe IKernelPortDefinition DebugGetKernelPorts(KernelLayout.Pointers kPointers) => UnsafeUtility.AsRef<TKernelPortDefinition>(kPointers.Ports);

        internal override LLTraitsHandle CreateNodeTraits(System.Type superType, SimulationStorageDefinition simStorage, KernelStorageDefinition kernelStorage)
            => LowLevelTraitsFactory<TKernelData, TKernelPortDefinition, TGraphKernel>.Create(superType, simStorage, kernelStorage);
        internal override IManagedMemoryPoolAllocator ManagedAllocator => throw new NotImplementedException();
    }

    sealed class NodeTraits<TNodeData, TKernelData, TKernelPortDefinition, TKernel> : NodeTraitsBase
        where TNodeData : struct, INodeData
        where TKernelData : struct, IKernelData
        where TKernelPortDefinition : struct, IKernelPortDefinition
        where TKernel : struct, IGraphKernel<TKernelData, TKernelPortDefinition>
    {
        DefaultManagedAllocator<TNodeData> m_Allocator = new DefaultManagedAllocator<TNodeData>();

        internal override IKernelData DebugGetKernelData(NodeSetAPI set, NodeHandle handle) => set.GetKernelData<TKernelData>(handle);
        internal override INodeData DebugGetNodeData(NodeSetAPI set, NodeHandle handle) => set.GetNodeData<TNodeData>(handle);
        internal override unsafe IKernelData DebugGetKernelData(KernelLayout.Pointers kPointers) => UnsafeUtility.AsRef<TKernelData>(kPointers.Data);
        internal override unsafe IGraphKernel DebugGetKernel(KernelLayout.Pointers kPointers) => UnsafeUtility.AsRef<TKernel>(kPointers.Kernel);
        internal override unsafe IKernelPortDefinition DebugGetKernelPorts(KernelLayout.Pointers kPointers) => UnsafeUtility.AsRef<TKernelPortDefinition>(kPointers.Ports);

        internal override LLTraitsHandle CreateNodeTraits(System.Type superType, SimulationStorageDefinition simStorage, KernelStorageDefinition kernelStorage)
            => LowLevelTraitsFactory<TKernelData, TKernelPortDefinition, TKernel>.Create(superType, simStorage, kernelStorage);
        internal override IManagedMemoryPoolAllocator ManagedAllocator => m_Allocator;
    }

    public partial class NodeSetAPI
    {
        const int InvalidTraitSlot = 0;
    }
}
