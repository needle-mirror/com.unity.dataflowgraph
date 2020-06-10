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
        internal NodeSet Set { get; set; }
        internal abstract IManagedMemoryPoolAllocator ManagedAllocator { get; }

        internal abstract LLTraitsHandle CreateNodeTraits(System.Type superType);
        internal virtual INodeData DebugGetNodeData(NodeHandle handle) => null;
        internal virtual IKernelData DebugGetKernelData(NodeHandle handle) => null;
    }

    sealed class NodeTraits<TSimPorts> : NodeTraitsBase
        where TSimPorts : struct, ISimulationPortDefinition
    {
        struct EmptyData : INodeData { }

        internal override LLTraitsHandle CreateNodeTraits(System.Type superType) => LowLevelTraitsFactory<EmptyData, TSimPorts>.Create(superType);
        internal override IManagedMemoryPoolAllocator ManagedAllocator => throw new NotImplementedException();
    }

    sealed class NodeTraits<TNodeData, TSimPorts> : NodeTraitsBase
        where TNodeData : struct, INodeData
        where TSimPorts : struct, ISimulationPortDefinition
    {
        DefaultManagedAllocator<TNodeData> m_Allocator = new DefaultManagedAllocator<TNodeData>();

        /// <summary>
        /// Returns a reference to a node's instance memory.
        /// </summary>
        /// <exception cref="System.ArgumentException">
        /// Thrown if the <paramref name="handle"/> does not refer to a valid node.
        /// </exception>
        public ref TNodeData GetNodeData(NodeHandle handle) => ref Set.GetNodeData<TNodeData>(handle);

        internal override INodeData DebugGetNodeData(NodeHandle handle) => GetNodeData(handle);

        internal override LLTraitsHandle CreateNodeTraits(System.Type superType) => LowLevelTraitsFactory<TNodeData, TSimPorts>.Create(superType);
        internal override IManagedMemoryPoolAllocator ManagedAllocator => m_Allocator;
    }

    sealed class NodeTraits<TKernelData, TKernelPortDefinition, TKernel> : NodeTraitsBase
        where TKernelData : struct, IKernelData
        where TKernelPortDefinition : struct, IKernelPortDefinition
        where TKernel : struct, IGraphKernel<TKernelData, TKernelPortDefinition>
    {
        struct EmptySimPorts : ISimulationPortDefinition { }
        struct EmptyData : INodeData { }

        /// <summary>
        /// Returns a reference to a node's <typeparamref name="TKernelData"/> memory.
        /// Writing to this will update it into the rendering graph
        /// after the next <see cref="NodeSet.Update"/>.
        /// </summary>
        /// <exception cref="System.ArgumentException">
        /// Thrown if the <paramref name="handle"/> does not refer to a valid node.
        /// </exception>
        public ref TKernelData GetKernelData(NodeHandle handle) => ref Set.GetKernelData<TKernelData>(handle);

        internal override IKernelData DebugGetKernelData(NodeHandle handle) => GetKernelData(handle);

        internal override LLTraitsHandle CreateNodeTraits(System.Type superType) => LowLevelTraitsFactory<EmptyData, EmptySimPorts, TKernelData, TKernelPortDefinition, TKernel>.Create(superType);
        internal override IManagedMemoryPoolAllocator ManagedAllocator => throw new NotImplementedException();
    }

    class NodeTraits<TNodeData, TSimPorts, TKernelData, TKernelPortDefinition, TKernel> : NodeTraitsBase
        where TNodeData : struct, INodeData
        where TSimPorts : struct, ISimulationPortDefinition
        where TKernelData : struct, IKernelData
        where TKernelPortDefinition : struct, IKernelPortDefinition
        where TKernel : struct, IGraphKernel<TKernelData, TKernelPortDefinition>
    {
        DefaultManagedAllocator<TNodeData> m_Allocator = new DefaultManagedAllocator<TNodeData>();

        /// <summary>
        /// Returns a reference to a node's <typeparamref name="TKernelData"/> memory.
        /// Writing to this will update it into the rendering graph
        /// after the next <see cref="NodeSet.Update"/>.
        /// </summary>
        /// <exception cref="System.ArgumentException">
        /// Thrown if the <paramref name="handle"/> does not refer to a valid node.
        /// </exception>
        public ref TKernelData GetKernelData(NodeHandle handle) => ref Set.GetKernelData<TKernelData>(handle);

        /// <summary>
        /// See <see cref="NodeTraits{TNodeData, TSimPorts}.GetNodeData(NodeHandle)"/>
        /// </summary>
        public ref TNodeData GetNodeData(NodeHandle handle) => ref Set.GetNodeData<TNodeData>(handle);

        internal override IKernelData DebugGetKernelData(NodeHandle handle) => GetKernelData(handle);
        internal override INodeData DebugGetNodeData(NodeHandle handle) => GetNodeData(handle);

        internal override LLTraitsHandle CreateNodeTraits(System.Type superType) => LowLevelTraitsFactory<TNodeData, TSimPorts, TKernelData, TKernelPortDefinition, TKernel>.Create(superType);
        internal override IManagedMemoryPoolAllocator ManagedAllocator => m_Allocator;
    }

    sealed class NodeTraits<TNodeData, TKernelData, TKernelPortDefinition, TKernel>
        : NodeTraits<TNodeData, NodeTraits<TNodeData, TKernelData, TKernelPortDefinition, TKernel>.EmptySimPorts, TKernelData, TKernelPortDefinition, TKernel>
            where TNodeData : struct, INodeData
            where TKernelData : struct, IKernelData
            where TKernelPortDefinition : struct, IKernelPortDefinition
            where TKernel : struct, IGraphKernel<TKernelData, TKernelPortDefinition>
    {
        public struct EmptySimPorts : ISimulationPortDefinition { }
    }

    public partial class NodeSet
    {
        const int InvalidTraitSlot = 0;
    }
}
