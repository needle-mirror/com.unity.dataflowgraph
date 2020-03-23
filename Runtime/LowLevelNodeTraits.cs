using System;
using System.Reflection;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Collections;
using System.Runtime.CompilerServices;
using static Unity.DataFlowGraph.ReflectionTools;

namespace Unity.DataFlowGraph
{
    using UntypedPortArray = PortArray<DataInput<InvalidDefinitionSlot, byte>>;

    public class InvalidNodeDefinitionException : ArgumentException
    {
        public InvalidNodeDefinitionException(string message) : base(message)
        {
        }
    }


    struct SimpleType
    {
        public readonly int Size;
        public readonly int Align;

        public SimpleType(int size, int align)
        {
            Size = size;
            Align = align;
        }

        public SimpleType(Type type)
        {
            Size = UnsafeUtility.SizeOf(type);
            Align = 16; // danger
        }

        public static SimpleType Create<T>()
            where T : struct
        {
            return new SimpleType(UnsafeUtility.SizeOf<T>(), UnsafeUtility.AlignOf<T>());
        }

        public static SimpleType Create<T>(int count)
            where T : struct
        {
            return new SimpleType(UnsafeUtility.SizeOf<T>() * count, UnsafeUtility.AlignOf<T>());
        }
    }

    struct DataPortDeclarations : IDisposable
    {
        public const int k_MaxInputSize = 1 << 16;

        public unsafe readonly struct InputDeclaration
        {
            public readonly SimpleType Type;
            /// <summary>
            /// Patch offset for a pointer living in a DataInput{,} on a IKernelDataPorts structure
            /// </summary>
            public readonly int PatchOffset;

            public readonly InputPortID PortNumber;

            public readonly bool IsArray;

            public InputDeclaration(SimpleType type, int patchOffset, InputPortID portNumber, bool isArray)
            {
                Type = type;
                PatchOffset = patchOffset;
                PortNumber = portNumber;
                IsArray = isArray;
            }

            /// <summary>
            /// Returns a pointer to the <see cref="DataInput{TDefinition, TType}.Ptr"/> field,
            /// that this port declaration represents.
            /// </summary>
            public void** GetPointerToPatch(RenderKernelFunction.BasePort* ports)
            {
                return (void**)((byte*)ports + PatchOffset);
            }

            /// <summary>
            /// If this port declaration is a port array, return the appropriate patch location inside
            /// the port array's nth port (using the potential array index)
            /// Otherwise, <see cref="GetPointerToPatch(RenderKernelFunction.BasePort*)"/>
            /// </summary>
            public void** GetPointerToPatch(RenderKernelFunction.BasePort* ports, ushort potentialArrayIndex)
            {
                if (!IsArray)
                    return (void**)((byte*)ports + PatchOffset);

                return AsPortArray(ports).NthInputPortPointer(potentialArrayIndex);
            }

            public ref UntypedPortArray AsPortArray(RenderKernelFunction.BasePort* ports)
            {
                // Assert IsArray ?
                return ref Unsafe.AsRef<UntypedPortArray>((byte*)ports + PatchOffset);
            }
        }

        /// <summary>
        /// Low level information about an instance of a <see cref="DataOutput{TDefinition, TType}"/> contained
        /// in a <see cref="IKernelPortDefinition"/> implementation.
        /// </summary>
        public unsafe readonly struct OutputDeclaration
        {
            /// <summary>
            /// The simple type of the element of a nested <see cref="Buffer{T}"/> inside a <see cref="DataOutput{TDefinition, TType}"/>,
            /// or just the equivalent representation of the entire contained non-special cased TType.
            /// </summary>
            public readonly SimpleType ElementOrType;
            /// <summary>
            /// The offset for the actual storage in case of an <see cref="DataOutput{TDefinition, TType}"/>
            /// </summary>
            public readonly int PatchOffset;

            public readonly OutputPortID PortNumber;

            public OutputDeclaration(SimpleType typeOrElement, int patchOffset, OutputPortID portNumber)
            {
                ElementOrType = typeOrElement;
                PatchOffset = patchOffset;
                PortNumber = portNumber;
            }

            public void* Resolve(RenderKernelFunction.BasePort* ports)
            {
                return (byte*)ports + PatchOffset;
            }

            public ref BufferDescription GetAggregateBufferAt(RenderKernelFunction.BasePort* ports, int byteOffset)
            {
                return ref *(BufferDescription*)((byte*)ports + PatchOffset + byteOffset);
            }
        }

        internal readonly BlitList<InputDeclaration> Inputs;
        internal readonly BlitList<OutputDeclaration> Outputs;

        public unsafe readonly struct BufferOffset
        {
            internal readonly int Offset;

            public BufferOffset(int offset)
            {
                Offset = offset;
            }

            public ref BufferDescription AsUntyped(RenderKernelFunction.BasePort* kernelPorts)
                => ref *(BufferDescription*)((byte*)kernelPorts + Offset);

            public ref BufferDescription AsUntyped(RenderKernelFunction.BaseKernel* kernel)
                => ref *(BufferDescription*)((byte*)kernel + Offset);
        }

        /// <summary>
        /// List of offsets of all Buffer<T> instances relative to the beginning of the IKernelDataPorts structure.
        /// </summary>
        internal readonly BlitList<BufferOffset> OutputBufferOffsets;

        public DataPortDeclarations(Type definitionType, Type kernelPortType)
        {
            (Inputs, Outputs, OutputBufferOffsets) = GenerateDataPortDeclarations(definitionType, kernelPortType);
        }

        static (BlitList<InputDeclaration> inputs, BlitList<OutputDeclaration> outputs, BlitList<BufferOffset> outputBufferOffsets)
        GenerateDataPortDeclarations(Type definitionType, Type kernelPortType)
        {
            // Offset from the start of the field of the data port to the pointer. A bit of a hack.
            const int k_PtrOffset = 0;

            var inputs = new BlitList<InputDeclaration>(0);
            var outputs = new BlitList<OutputDeclaration>(0);
            var outputBufferOffsets = new BlitList<BufferOffset>(0);

            try
            {
                var kernelPortValue = definitionType.GetField("s_KernelPorts", BindingFlags.FlattenHierarchy | BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null);

                foreach (var potentialPortFieldInfo in kernelPortType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                {
                    ValidateFieldOnKernelPort(potentialPortFieldInfo);

                    var portType = potentialPortFieldInfo.FieldType;

                    if (!portType.IsConstructedGenericType)
                        throw new InvalidNodeDefinitionException($"Simulation port definition contains disallowed field {portType}.");

                    // Acquire the assigned port number of this port declaration
                    var assignedPortNumberField = portType.GetField("Port", BindingFlags.Instance | BindingFlags.NonPublic);

                    var genericPortType = portType.GetGenericTypeDefinition();

                    var genericsForDeclaration = portType.GetGenericArguments();

                    bool isPortArray = genericPortType == typeof(PortArray<>);
                    if (isPortArray)
                    {
                        // Extract the specifics of the port type inside the port array.
                        portType = genericsForDeclaration[0];
                        if (!portType.IsConstructedGenericType)
                            throw new InvalidNodeDefinitionException($"Simulation port definition contains disallowed field {portType}.");
                        genericPortType = portType.GetGenericTypeDefinition();
                        genericsForDeclaration = portType.GetGenericArguments();
                    }

                    if (genericsForDeclaration.Length < 2)
                        throw new InvalidNodeDefinitionException($"Simulation port definition contains disallowed type {portType}.");

                    var dataType = genericsForDeclaration[1];

                    ValidateDataPortType(potentialPortFieldInfo, dataType);

                    var offsetOfWholePortDeclaration = UnsafeUtility.GetFieldOffset(potentialPortFieldInfo);

                    var portValue = potentialPortFieldInfo.GetValue(kernelPortValue);

                    if (genericPortType == typeof(DataInput<,>))
                    {
                        if (UnsafeUtility.SizeOf(dataType) > k_MaxInputSize)
                            throw new InvalidNodeDefinitionException($"Node input data structure types cannot have a sizeof larger than {k_MaxInputSize}");

                        inputs.Add(
                            new InputDeclaration(
                                new SimpleType(dataType),
                                offsetOfWholePortDeclaration + k_PtrOffset,
                                (InputPortID)assignedPortNumberField.GetValue(portValue),
                                isPortArray
                            )
                        );
                    }
                    else if (genericPortType == typeof(DataOutput<,>))
                    {
                        SimpleType type;

                        if (IsBufferDefinition(dataType))
                        {
                            // Compute the simple type of an element inside a buffer if possible
                            type = new SimpleType(dataType.GetGenericArguments()[0]);
                            outputBufferOffsets.Add(new BufferOffset(offsetOfWholePortDeclaration));
                        }
                        else
                        {
                            // otherwise the entire value (breaks for aggregates)
                            type = new SimpleType(dataType);

                            foreach (var field in WalkTypeInstanceFields(dataType, BindingFlags.Public, IsBufferDefinition))
                                outputBufferOffsets.Add(new BufferOffset(offsetOfWholePortDeclaration + UnsafeUtility.GetFieldOffset(field)));
                        }

                        outputs.Add(
                            new OutputDeclaration(
                                type,
                                offsetOfWholePortDeclaration + k_PtrOffset,
                                (OutputPortID)assignedPortNumberField.GetValue(portValue)
                            )
                        );
                    }
                    else
                    {
                        throw new InvalidNodeDefinitionException($"Kernel port definition {kernelPortType} contains other types of fields than DataInput<> and DataOutput<> ({portType})");
                    }
                }
            }
            catch
            {
                inputs.Dispose();
                outputs.Dispose();
                outputBufferOffsets.Dispose();
                throw;
            }
            return (inputs, outputs, outputBufferOffsets);
        }

        public void Dispose()
        {
            if (Inputs.IsCreated)
                Inputs.Dispose();

            if (Outputs.IsCreated)
                Outputs.Dispose();

            if (OutputBufferOffsets.IsCreated)
                OutputBufferOffsets.Dispose();
        }

        static void ValidateFieldOnKernelPort(FieldInfo info)
        {
            if (info.IsStatic)
                throw new InvalidNodeDefinitionException($"Kernel port structures cannot have static fields ({info})");

            if (!info.IsPublic)
                throw new InvalidNodeDefinitionException($"Kernel port structures cannot have non-public fields ({info})");
        }

        static void ValidateDataPortType(FieldInfo port, Type internalPortType)
        {
            if (!UnsafeUtility.IsUnmanaged(internalPortType))
                throw new InvalidNodeDefinitionException($"Data port type {internalPortType} in {port} is not unmanaged");
        }

        public ref /*readonly */ OutputDeclaration FindOutputDataPort(OutputPortID port)
            => ref Outputs[FindOutputDataPortNumber(port)];

        public ref /*readonly */ InputDeclaration FindInputDataPort(InputPortID port)
            => ref Inputs[FindInputDataPortNumber(port)];

        public int FindOutputDataPortNumber(OutputPortID port)
        {
            for (int p = 0; p < Outputs.Count; ++p)
            {
                if (Outputs[p].PortNumber == port)
                    return p;
            }

            throw new InternalException("Matching output port not found");
        }

        public int FindInputDataPortNumber(InputPortID port)
        {
            for (int p = 0; p < Inputs.Count; ++p)
            {
                if (Inputs[p].PortNumber == port)
                    return p;
            }

            throw new InternalException("Matching input port not found");
        }
    }

    unsafe struct LLTraitsHandle : IDisposable
    {
        public bool IsCreated => m_Traits != null;

        [NativeDisableUnsafePtrRestriction]
        void* m_Traits;

        internal ref LowLevelNodeTraits Resolve()
        {
            return ref Unsafe.AsRef<LowLevelNodeTraits>(m_Traits);
        }

        LowLevelNodeTraits DebugDisplay => Resolve();

        /// <summary>
        /// Disposes the LowLevelNodeTraits as well
        /// </summary>
        public void Dispose()
        {
            if (!IsCreated)
                throw new ObjectDisposedException("LLTraitsHandle not created");

            if (Resolve().IsCreated)
                Resolve().Dispose();

            UnsafeUtility.Free(m_Traits, Allocator.Persistent);
            m_Traits = null;
        }

        internal static LLTraitsHandle Create()
        {
            var handle = new LLTraitsHandle
            {
                m_Traits = UnsafeUtility.Malloc(UnsafeUtility.SizeOf<LowLevelNodeTraits>(), UnsafeUtility.AlignOf<LowLevelNodeTraits>(), Allocator.Persistent)
            };

            handle.Resolve() = new LowLevelNodeTraits();

            return handle;
        }
    }

    struct LowLevelNodeTraits : IDisposable
    {
        static IntPtr s_PureInvocation = PureVirtualFunction.GetReflectionData();

        public struct VirtualTable
        {
#if DFG_PER_NODE_PROFILING
            static Profiling.ProfilerMarker PureProfiler = new Profiling.ProfilerMarker("PureInvocation");
            public Profiling.ProfilerMarker KernelMarker;
#endif
            public RenderKernelFunction KernelFunction;

            public static VirtualTable Create()
            {
                VirtualTable ret;
                ret.KernelFunction = RenderKernelFunction.Pure<PureVirtualFunction>(s_PureInvocation);

#if DFG_PER_NODE_PROFILING
                ret.KernelMarker = PureProfiler;
#endif
                return ret;
            }

            public static bool IsMethodImplemented<TFunction>(in TFunction function)
                where TFunction : IVirtualFunctionDeclaration => function.ReflectionData != s_PureInvocation;
        }

        public readonly struct StorageDefinition : IDisposable
        {
            public readonly SimpleType NodeData, KernelData, Kernel, KernelPorts, SimPorts;
            public readonly RuntimeTypeHandle KernelType;
            public struct BufferInfo
            {
                public BufferInfo(int offset, SimpleType itemType)
                {
                    Offset = new DataPortDeclarations.BufferOffset(offset);
                    ItemType = itemType;
                }
                public DataPortDeclarations.BufferOffset Offset;
                public SimpleType ItemType;
            }
            public readonly BlitList<BufferInfo> KernelBufferInfos;
            public readonly bool NodeDataIsManaged, IsComponentNode;

            internal StorageDefinition(bool nodeDataIsManaged, bool isComponentNode, SimpleType nodeData, SimpleType simPorts, SimpleType kernelData, SimpleType kernelPorts, SimpleType kernel, Type kernelType, BlitList<BufferInfo> kernelBufferInfos)
            {
                NodeData = nodeData;
                SimPorts = simPorts;
                KernelData = kernelData;
                Kernel = kernel;
                KernelPorts = kernelPorts;
                KernelType = kernelType.TypeHandle;
                KernelBufferInfos = kernelBufferInfos;
                NodeDataIsManaged = nodeDataIsManaged;
                IsComponentNode = isComponentNode;
            }

            internal StorageDefinition(bool nodeDataIsManaged, bool isComponentNode, SimpleType nodeData, SimpleType simPorts)
            {
                NodeData = nodeData;
                SimPorts = simPorts;
                KernelData = default;
                Kernel = default;
                KernelPorts = default;
                KernelType = default;
                KernelBufferInfos = default;
                NodeDataIsManaged = nodeDataIsManaged;
                IsComponentNode = isComponentNode;
            }

            public void Dispose()
            {
                if (KernelBufferInfos.IsCreated)
                    KernelBufferInfos.Dispose();
            }
        }

        public readonly VirtualTable VTable;
        public readonly StorageDefinition Storage;
        public readonly DataPortDeclarations DataPorts;
        public readonly KernelLayout KernelLayout;

        public bool HasKernelData => VirtualTable.IsMethodImplemented(VTable.KernelFunction);

        public bool IsCreated { get; private set; }

        public void Dispose()
        {
            if (!IsCreated)
                throw new ObjectDisposedException("LowLevelNodeTraits disposed or not created");

            DataPorts.Dispose();
            Storage.Dispose();

            IsCreated = false;
        }

        internal LowLevelNodeTraits(StorageDefinition def, VirtualTable table, DataPortDeclarations portDeclarations, KernelLayout kernelLayout)
        {
            IsCreated = true;
            Storage = def;
            VTable = table;
            DataPorts = portDeclarations;
            KernelLayout = kernelLayout;
        }

        internal LowLevelNodeTraits(StorageDefinition def, VirtualTable table)
        {
            IsCreated = true;
            Storage = def;
            VTable = table;
            DataPorts = new DataPortDeclarations();
            KernelLayout = default;
        }

    }
}
