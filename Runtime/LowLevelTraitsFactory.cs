using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Burst;
using Unity.Collections;
using static Unity.DataFlowGraph.ReflectionTools;

namespace Unity.DataFlowGraph
{
    readonly struct KernelLayout
    {
        public readonly unsafe struct Pointers
        {
            [NativeDisableUnsafePtrRestriction] readonly public RenderKernelFunction.BasePort* Ports;
            [NativeDisableUnsafePtrRestriction] readonly public RenderKernelFunction.BaseData* Data;
            [NativeDisableUnsafePtrRestriction] readonly public RenderKernelFunction.BaseKernel* Kernel;

            public Pointers(RenderKernelFunction.BasePort* ports, RenderKernelFunction.BaseData* data, RenderKernelFunction.BaseKernel* kernel)
            {
                Ports = ports;
                Data = data;
                Kernel = kernel;
            }
        }

        readonly SimpleType Combined;
        readonly int DataOffset;
        readonly int KernelOffset;

        struct Layout<TUserKernel, TKernelData, TKernelPortDefinition>
           where TUserKernel : struct, IGraphKernel<TKernelData, TKernelPortDefinition>
           where TKernelData : struct, IKernelData
           where TKernelPortDefinition : struct, IKernelPortDefinition
        {
            public static KernelLayout KernelLayout = Calculate();

#pragma warning disable 649  // Never assigned; these are placeholders for the memory used by the Kernel
            public TKernelPortDefinition Ports;
            public TKernelData Data;
            public TUserKernel Kernel;
#pragma warning restore 649

            static KernelLayout Calculate()
            {
                var type = typeof(Layout<TUserKernel, TKernelData, TKernelPortDefinition>);
                return new KernelLayout(
                    SimpleType.Create<Layout<TUserKernel, TKernelData, TKernelPortDefinition>>(),
                    UnsafeUtility.GetFieldOffset(type.GetField("Data")),
                    UnsafeUtility.GetFieldOffset(type.GetField("Kernel")));
            }

            void Reassign_ToAvoid_CompilerWarning()
            {
                Ports = default;
                Data = default;
                Kernel = default;
            }
        }

        public static KernelLayout Calculate<TUserKernel, TKernelData, TKernelPortDefinition>()
           where TUserKernel : struct, IGraphKernel<TKernelData, TKernelPortDefinition>
           where TKernelData : struct, IKernelData
           where TKernelPortDefinition : struct, IKernelPortDefinition
        {
            return Layout<TUserKernel, TKernelData, TKernelPortDefinition>.KernelLayout;
        }

        /// <summary>
        /// Must be free'd using <see cref="Free(in Pointers, Allocator)"/>/>
        /// </summary>
        public unsafe Pointers Allocate(Allocator allocator)
        {
            return VirtualReconstruct(Utility.CAlloc(Combined, allocator));
        }

        /// <summary>
        /// Reconstruct a kernel layout as if it existed in that memory location.
        /// </summary>
        public unsafe Pointers VirtualReconstruct(void* location)
        {
            var basePointer = (byte*)location;
            return new Pointers(
                (RenderKernelFunction.BasePort*)basePointer,
                (RenderKernelFunction.BaseData*)(basePointer + DataOffset),
                (RenderKernelFunction.BaseKernel*)(basePointer + KernelOffset)
            );
        }

        /// <summary>
        /// Treating <paramref name="source"/> and <paramref name="destination"/> as if
        /// they come from the same <see cref="KernelLayout"/>, copy the memory from the
        /// <paramref name="source"/> to the <paramref name="destination"/>.
        /// </summary>
        public unsafe void Blit(in Pointers source, ref Pointers destination)
        {
            UnsafeUtility.MemCpy(destination.Ports, source.Ports, Combined.Size);
        }

        public unsafe void Free(in Pointers p, Allocator allocator)
        {
            UnsafeUtility.Free(p.Ports, allocator);
        }

        KernelLayout(SimpleType combined, int dataOffset, int kernelOffset)
        {
            Combined = combined;
            DataOffset = dataOffset;
            KernelOffset = kernelOffset;
        }
    }

    struct LowLevelTraitsFactory<TKernelData, TKernelPortDefinition, TUserKernel>
       where TKernelData : struct, IKernelData
       where TKernelPortDefinition : struct, IKernelPortDefinition
       where TUserKernel : struct, IGraphKernel<TKernelData, TKernelPortDefinition>
    {
        /// <param name="hostNodeType">
        /// Specifically the type which has the TKernelPortDefinition as a field (can be the whole node definition).
        /// </param>
        internal static LLTraitsHandle Create(Type hostNodeType, SimulationStorageDefinition simStorage, KernelStorageDefinition kernelStorage)
        {
            var vtable = LowLevelNodeTraits.VirtualTable.Create();

#if DFG_PER_NODE_PROFILING
            vtable.KernelMarker = new Profiling.ProfilerMarker(hostNodeType.Name);
#endif
            if (BurstConfig.IsBurstEnabled && typeof(TUserKernel).GetCustomAttributes().Any(a => a is BurstCompileAttribute))
                vtable.KernelFunction = RenderKernelFunction.GetBurstedFunction<TKernelData, TKernelPortDefinition, TUserKernel>();
            else
                vtable.KernelFunction = RenderKernelFunction.GetManagedFunction<TKernelData, TKernelPortDefinition, TUserKernel>();

            var traits = new LowLevelNodeTraits(
               simStorage,
               kernelStorage,
               vtable,
               new DataPortDeclarations(hostNodeType, typeof(TKernelPortDefinition)),
               KernelLayout.Calculate<TUserKernel, TKernelData, TKernelPortDefinition>()
           );

            var handle = LLTraitsHandle.Create();
            handle.Resolve() = traits;
            return handle;
        }
    }

    struct LowLevelTraitsFactory
    {

        /// <param name="hostNodeType">
        /// Specifically the type which has the TKernelPortDefinition as a field (can be the whole node definition).
        /// </param>
        internal static LLTraitsHandle Create(SimulationStorageDefinition simStorage)
        {
            var traits = new LowLevelNodeTraits(simStorage, LowLevelNodeTraits.VirtualTable.Create());
            var handle = LLTraitsHandle.Create();
            handle.Resolve() = traits;
            return handle;
        }
    }
}
