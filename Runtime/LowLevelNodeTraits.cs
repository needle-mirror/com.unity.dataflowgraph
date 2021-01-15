using System;
using System.Reflection;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Collections;
using static Unity.DataFlowGraph.ReflectionTools;
using System.Linq;
using System.ComponentModel;
#if DFG_ASSERTIONS
using Unity.Mathematics;
#endif

namespace Unity.DataFlowGraph
{
    public class InvalidNodeDefinitionException : ArgumentException
    {
        public InvalidNodeDefinitionException(string message) : base(message)
        {
        }
    }

    struct SimpleType
    {
        static readonly int[] k_AlignmentFromSizeLUT = new int[MaxAlignment] {16, 1, 2, 1, 4, 1, 2, 1, 8, 1, 2, 1, 4, 1, 2, 1};

        /// <summary>
        /// The largest alignment value we will ever see on any platform for any type.
        /// </summary>
        public const int MaxAlignment = 16;

        public readonly int Size;
        public readonly int Align;

        public SimpleType(int size, int align)
        {
            Size = size;
            Align = align;

#if DFG_ASSERTIONS
            if (Size < 0)
                throw new AssertionException("Invalid size value");

            if (Size != 0 && (Align < 1 || Align > Size || Size % Align != 0 || math.countbits(Align) != 1))
                throw new AssertionException("Invalid alignment value");
#endif
        }

        public SimpleType(Type type)
        {
            Size = UnsafeUtility.SizeOf(type);

#if DFG_ASSERTIONS
            if (Size <= 0)
                throw new AssertionException("SizeOf returned invalid size");
#endif

            // Identify worst case alignment requirements (since UnsafeUtility.AlignOf(type) doesn't exist)
            // Size must be a multiple of alignment, alignment must be a power of two, and assume we don't need alignment higher than "MaxAlignment".
            // Perform a table lookup instead of doing the real evaluation.
            //    Align = MaxAlignment;
            //    while (Size % Align != 0)
            //        Align >>= 1;
            Align = k_AlignmentFromSizeLUT[Size & (MaxAlignment-1)];

#if DFG_ASSERTIONS
            if (Align < 1 || Align > Size || Size % Align != 0 || math.countbits(Align) != 1)
                throw new AssertionException("Badly calculated alignment");

#if !ENABLE_IL2CPP // This reflection is problematic for IL2CPP
            var alignOfGenericMethod = typeof(UnsafeUtility).GetMethod(nameof(UnsafeUtility.AlignOf), BindingFlags.Static | BindingFlags.Public);
            var alignOfMethod = alignOfGenericMethod.MakeGenericMethod(type);
            var actualAlign = (int) alignOfMethod.Invoke(null, new object[0]);
            if (actualAlign > Align || Align % actualAlign != 0)
                throw new AssertionException("Calculated alignment incompatible with real alignment");
#endif // ENABLE_IL2CPP
#endif // DFG_ASSERTIONS
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

        /// <summary>
        /// Enforce a minimum alignment requirement on the existing type (this may require increasing size as size must
        /// be a multiple of alignment)
        /// </summary>
        /// <remarks>Alignment must always be a power-of-two</remarks>
        public SimpleType GetMinAlignedTyped(int minAlignment)
        {
#if DFG_ASSERTIONS
            if (math.countbits(minAlignment) != 1)
                throw new AssertionException("Alignment must be a power-of-two");
#endif
            return minAlignment <= Align ? this : new SimpleType(((Size - 1) / minAlignment + 1) * minAlignment, minAlignment);
        }
    }

    readonly struct DataPortDeclarations : IDisposable
    {
        public const int k_MaxInputSize = 1 << 16;

        public unsafe readonly struct InputDeclaration
        {
            public readonly SimpleType Type;
            /// <summary>
            /// Patch offset for a pointer living in a DataInput{,} on a IKernelDataPorts structure
            /// </summary>
            public readonly int PatchOffset;

            public readonly bool IsArray;

            public InputDeclaration(SimpleType type, int patchOffset, bool isArray)
            {
                Type = type;
                PatchOffset = patchOffset;
                IsArray = isArray;
            }

            /// <summary>
            /// Returns a pointer to the <see cref="DataInput{TDefinition, TType}.Ptr"/> field,
            /// that this port declaration represents.
            /// </summary>
            public DataInputStorage* GetStorageLocation(RenderKernelFunction.BasePort* ports)
            {
                return (DataInputStorage*)((byte*)ports + PatchOffset);
            }

            /// <summary>
            /// If this port declaration is a port array, return the appropriate patch location inside
            /// the port array's nth port (using the potential array index)
            /// Otherwise, <see cref="GetStorageLocation(RenderKernelFunction.BasePort*)"/>
            /// </summary>
            public DataInputStorage* GetStorageLocation(RenderKernelFunction.BasePort* ports, ushort potentialArrayIndex)
            {
                if (!IsArray)
                    return GetStorageLocation(ports);

                return AsPortArray(ports).NthInputStorage(potentialArrayIndex);
            }

            public ref UntypedDataInputPortArray AsPortArray(RenderKernelFunction.BasePort* ports)
            {
#if DFG_ASSERTIONS
                if (!IsArray)
                    throw new AssertionException("Bad cast to UntypedDataInputPortArray");
#endif
                return ref *(UntypedDataInputPortArray*)((byte*)ports + PatchOffset);
            }
        }

        /// <summary>
        /// Low level information about an instance of a <see cref="DataOutput{TDefinition, TType}"/> contained
        /// in a <see cref="IKernelPortDefinition"/> implementation.
        /// </summary>
        public readonly unsafe struct OutputDeclaration
        {
            public struct BufferIndexEnumerator
            {
                ushort m_Index, m_Max;

                internal BufferIndexEnumerator(ushort bufferCount)
                {
                    m_Index = UInt16.MaxValue;
                    m_Max = bufferCount;
                }
                public bool MoveNext() => ++m_Index < m_Max;
                public BufferIndexEnumerator GetEnumerator() => this;
                public PortBufferIndex Current => new PortBufferIndex(m_Index);
            }

            /// <summary>
            /// The simple type of TType in a <see cref="DataOutput{TDefinition, TType}"/>
            /// </summary>
            public readonly SimpleType Type;

            /// <summary>
            /// The simple type of the element of a nested <see cref="Buffer{T}"/> inside a <see cref="DataOutput{TDefinition, TType}"/>,
            /// or just the equivalent representation of the entire contained non-special cased TType.
            /// </summary>
            public readonly SimpleType ElementOrType;

            /// <summary>
            /// The offset for the actual storage in case of an <see cref="DataOutput{TDefinition, TType}"/>
            /// </summary>
            public readonly int PatchOffset;

            public readonly ushort BufferCount;

            public BufferIndexEnumerator BufferIndices => new BufferIndexEnumerator(BufferCount);

            /// <summary>
            /// Index of the first local Buffer<T> offset in the master list maintained by the <see cref="DataPortDeclarations"/>
            /// </summary>
            public readonly ushort BufferListStartIndex;

            public readonly bool IsArray;

            public OutputDeclaration(SimpleType type, SimpleType typeOrElement, int patchOffset, (ushort bufferListIndex, ushort bufferCount) bufferOffsets, bool isArray)
            {
                Type = type;
                ElementOrType = typeOrElement;
                PatchOffset = patchOffset;
                IsArray = isArray;
                (BufferListStartIndex, BufferCount) = bufferOffsets;
            }

            public void* Resolve(RenderKernelFunction.BasePort* ports, ushort potentialArrayIndex)
            {
#if DFG_ASSERTIONS
                if (ports == null)
                    throw new AssertionException("Unexpected null pointer in DataOutput value dereferencing");
#endif
                if (!IsArray)
                    return (byte*)ports + PatchOffset;

                return AsPortArray(ports).Get(Type, potentialArrayIndex);
            }

            public ref UntypedDataOutputPortArray AsPortArray(RenderKernelFunction.BasePort* ports)
            {
#if DFG_ASSERTIONS
                if (!IsArray)
                    throw new AssertionException("Bad cast to UntypedDataOutputPortArray");
#endif
                return ref *(UntypedDataOutputPortArray*)((byte*)ports + PatchOffset);
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

            public ref BufferDescription AsUntyped(RenderKernelFunction.BaseKernel* kernel)
                => ref *(BufferDescription*)((byte*)kernel + Offset);
        }

        /// <summary>
        /// List of offsets of all Buffer<T> instances relative to the beginning of each port within which it is found.
        /// </summary>
        readonly BlitList<BufferOffset> m_OutputBufferOffsets;

        public DataPortDeclarations(Type definitionType, Type kernelPortType)
        {
            (Inputs, Outputs, m_OutputBufferOffsets) = GenerateDataPortDeclarations(definitionType, kernelPortType);
        }

        public unsafe ref BufferDescription GetAggregateOutputBuffer(RenderKernelFunction.BasePort* ports, in OutputDeclaration output, PortBufferIndex bufferIndex, ushort potentialArrayIndex)
        {
#if DFG_ASSERTIONS
            if (bufferIndex.Value > output.BufferCount)
                throw new AssertionException("Buffer index out of range");
#endif
            var byteOffset = m_OutputBufferOffsets[output.BufferListStartIndex + bufferIndex.Value].Offset;

            if (!output.IsArray)
                return ref *(BufferDescription*)((byte*)ports + output.PatchOffset + byteOffset);

            return ref *(BufferDescription*)((byte*)output.AsPortArray(ports).Get(output.Type, potentialArrayIndex) + byteOffset);
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
                foreach (var potentialPortFieldInfo in kernelPortType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                {
                    ValidateFieldOnKernelPort(potentialPortFieldInfo);

                    var portType = potentialPortFieldInfo.FieldType;

                    if (!portType.IsConstructedGenericType)
                        throw new InvalidNodeDefinitionException($"Simulation port definition contains disallowed field {portType}.");

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

                    if (genericPortType == typeof(DataInput<,>))
                    {
                        if (UnsafeUtility.SizeOf(dataType) > k_MaxInputSize)
                            throw new InvalidNodeDefinitionException($"Node input data structure types cannot have a sizeof larger than {k_MaxInputSize}");

                        inputs.Add(new InputDeclaration(new SimpleType(dataType), offsetOfWholePortDeclaration + k_PtrOffset, isPortArray));
                    }
                    else if (genericPortType == typeof(DataOutput<,>))
                    {
                        SimpleType typeOrElement;

                        ushort previousPortLastBufferIndex = (ushort)outputBufferOffsets.Count;
                        if (IsBufferDefinition(dataType))
                        {
                            // Compute the simple type of an element inside a buffer if possible
                            typeOrElement = new SimpleType(dataType.GetGenericArguments()[0]);

                            outputBufferOffsets.Add(new BufferOffset(0));
                        }
                        else
                        {
                            // otherwise the entire value (breaks for aggregates)
                            typeOrElement = new SimpleType(dataType);

                            foreach (var field in WalkTypeInstanceFields(dataType, BindingFlags.Public, IsBufferDefinition))
                                outputBufferOffsets.Add(new BufferOffset(UnsafeUtility.GetFieldOffset(field)));
                        }

                        SimpleType type = new SimpleType(dataType);
                        if (isPortArray)
                        {
                            // For PortArrays, we require a minimum alignment in order to support packing flag into LSBs of pointers
                            type = type.GetMinAlignedTyped(DataInputStorage.MinimumInputAlignment);
                        }

                        outputs.Add(
                            new OutputDeclaration(
                                type,
                                typeOrElement,
                                offsetOfWholePortDeclaration + k_PtrOffset,
                                (previousPortLastBufferIndex, (ushort)(outputBufferOffsets.Count - previousPortLastBufferIndex)),
                                isPortArray
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

            if (m_OutputBufferOffsets.IsCreated)
                m_OutputBufferOffsets.Dispose();
        }

        static void ValidateFieldOnKernelPort(FieldInfo info)
        {
            if (info.IsStatic)
                throw new InvalidNodeDefinitionException($"Kernel port structures cannot have static fields ({info})");
        }

        static void ValidateDataPortType(FieldInfo port, Type internalPortType)
        {
            if (!UnsafeUtility.IsUnmanaged(internalPortType))
                throw new InvalidNodeDefinitionException($"Data port type {internalPortType} in {port} is not unmanaged");
        }

        /// <summary>
        /// Returns an <see cref="OutputPortID"/> matching the <paramref name="index"/>
        /// into <see cref="Outputs"/>
        public OutputPortID GetPortIDForOutputIndex(int index)
        {
#if DFG_ASSERTIONS
            if (index >= Outputs.Count)
                throw new AssertionException("Asking for a port not included in this node definition");
#endif
            return new OutputPortID(new PortStorage((ushort)index, PortStorage.Category.Data));
        }

        /// <summary>
        /// Returns an <see cref="InputPortID"/> matching the <paramref name="index"/>
        /// into <see cref="Inputs"/>
        public InputPortID GetPortIDForInputIndex(int index)
        {
#if DFG_ASSERTIONS
            if (index >= Inputs.Count)
                throw new AssertionException("Asking for a port not included in this node definition");
#endif

            return new InputPortID(new PortStorage((ushort)index, PortStorage.Category.Data));
        }

        public unsafe ref readonly OutputDeclaration FindOutputDataPort(OutputPortID port)
            => ref *Outputs.Ref(FindOutputDataPortNumber(port));

        public unsafe ref readonly InputDeclaration FindInputDataPort(InputPortID port)
            => ref *Inputs.Ref(FindInputDataPortNumber(port));

        public int FindOutputDataPortNumber(OutputPortID port)
        {
            var encoded = port.Port;
#if DFG_ASSERTIONS
            if (encoded.Category != PortStorage.Category.Data)
                throw new AssertionException("Looking up non-data port");

            if (encoded.CategoryCounter >= Outputs.Count)
                throw new AssertionException("Out of bounds port id, coming from non-matching port definition");
#endif
            return encoded.CategoryCounter;
        }

        public int FindInputDataPortNumber(InputPortID port)
        {
            var encoded = port.Port;
#if DFG_ASSERTIONS
            if (encoded.Category != PortStorage.Category.Data)
                throw new AssertionException("Looking up non-data port");

            if (encoded.CategoryCounter >= Inputs.Count)
                throw new AssertionException("Out of bounds port id, coming from non-matching port definition");
#endif
            return encoded.CategoryCounter;
        }
    }

    unsafe struct LLTraitsHandle : IDisposable
    {
        public bool IsCreated => m_Traits != null;

        [NativeDisableUnsafePtrRestriction]
        void* m_Traits;

        internal ref LowLevelNodeTraits Resolve()
        {
            return ref UnsafeUtility.AsRef<LowLevelNodeTraits>(m_Traits);
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

        public readonly VirtualTable VTable;
        public readonly SimulationStorageDefinition SimulationStorage;
        public readonly KernelStorageDefinition KernelStorage;
        public readonly DataPortDeclarations DataPorts;
        public readonly KernelLayout KernelLayout;

        public bool HasKernelData => VirtualTable.IsMethodImplemented(VTable.KernelFunction);

        public bool IsCreated { get; private set; }

        public void Dispose()
        {
            if (!IsCreated)
                throw new ObjectDisposedException("LowLevelNodeTraits disposed or not created");

            DataPorts.Dispose();
            KernelStorage.Dispose();

            IsCreated = false;
        }

        internal LowLevelNodeTraits(SimulationStorageDefinition simDef, KernelStorageDefinition kernelDef, VirtualTable table, DataPortDeclarations portDeclarations, KernelLayout kernelLayout)
        {
            IsCreated = true;
            SimulationStorage = simDef;
            KernelStorage = kernelDef;
            VTable = table;
            DataPorts = portDeclarations;
            KernelLayout = kernelLayout;
        }

        internal LowLevelNodeTraits(SimulationStorageDefinition simDef, VirtualTable table)
        {
            IsCreated = true;
            SimulationStorage = simDef;
            KernelStorage = default;
            VTable = table;
            DataPorts = new DataPortDeclarations();
            KernelLayout = default;
        }

    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public readonly struct TypeHash : IEquatable<TypeHash>
    {
        readonly Int32 m_TypeHash;

        internal static TypeHash Create<TType>() => new TypeHash(BurstRuntime.GetHashCode32<TType>());
        internal static TypeHash CreateSlow(Type t) => new TypeHash(BurstRuntime.GetHashCode32(t));

        public bool Equals(TypeHash other)
        {
            return m_TypeHash == other.m_TypeHash;
        }

        public override bool Equals(object obj)
        {
            return obj is TypeHash other && Equals(other);
        }

        public override int GetHashCode()
        {
            return m_TypeHash;
        }

        public static bool operator ==(TypeHash left, TypeHash right)
        {
            return left.m_TypeHash == right.m_TypeHash;
        }

        public static bool operator !=(TypeHash left, TypeHash right)
        {
            return left.m_TypeHash != right.m_TypeHash;
        }

        TypeHash(Int32 typeHash)
        {
            m_TypeHash = typeHash;
        }
    }

    readonly struct SimulationStorageDefinition
    {
        struct EmptyType { }

        public readonly SimpleType NodeData, SimPorts;

        public readonly TypeHash NodeDataHash;

        public readonly bool NodeDataIsManaged;

        internal static readonly SimulationStorageDefinition Empty = new SimulationStorageDefinition(false, default, SimpleType.Create<EmptyType>(), SimpleType.Create<EmptyType>());

        static internal SimulationStorageDefinition Create<TDefinition, TNodeData, TSimPorts>(bool nodeDataIsManaged)
            where TDefinition : NodeDefinition
            where TNodeData : struct, INodeData
            where TSimPorts : struct, ISimulationPortDefinition
        {
            ValidateRulesForStorage<TDefinition, TNodeData>(nodeDataIsManaged);
            return new SimulationStorageDefinition(
                nodeDataIsManaged,
                TypeHash.Create<TNodeData>(),
                SimpleType.Create<TNodeData>(),
                SimpleType.Create<TSimPorts>()
            );
        }

        static internal SimulationStorageDefinition Create<TDefinition, TNodeData>(bool nodeDataIsManaged)
            where TDefinition : NodeDefinition
            where TNodeData : struct, INodeData
        {
            ValidateRulesForStorage<TDefinition, TNodeData>(nodeDataIsManaged);
            return new SimulationStorageDefinition(
                nodeDataIsManaged,
                TypeHash.Create<TNodeData>(),
                SimpleType.Create<TNodeData>(),
                SimpleType.Create<EmptyType>()
            );
        }

        static internal SimulationStorageDefinition Create<TSimPorts>()
            where TSimPorts : struct, ISimulationPortDefinition
        {
            return new SimulationStorageDefinition(
                false,
                default,
                SimpleType.Create<EmptyType>(),
                SimpleType.Create<TSimPorts>()
            );
        }

        SimulationStorageDefinition(bool nodeDataIsManaged, TypeHash nodeDataHash, SimpleType nodeData, SimpleType simPorts)
        {
            NodeData = nodeData;
            SimPorts = simPorts;
            NodeDataHash = nodeDataHash;
            NodeDataIsManaged = nodeDataIsManaged;
        }

        static void ValidateRulesForStorage<TDefinition, TNodeData>(bool nodeDataIsManaged)
            where TDefinition : NodeDefinition
            where TNodeData : struct, INodeData
        {
            if (!nodeDataIsManaged && !UnsafeUtility.IsUnmanaged<TNodeData>())
                throw new InvalidNodeDefinitionException($"Node data type {typeof(TNodeData)} on node definition {typeof(TDefinition)} is not unmanaged, " +
                    $"add the attribute [Managed] to the type if you need to store references in your data");
        }
    }

    readonly struct KernelStorageDefinition : IDisposable
    {
        public readonly struct BufferInfo
        {
            public BufferInfo(ushort bufferIndexInKernel, int offset, SimpleType itemType)
            {
                Offset = new DataPortDeclarations.BufferOffset(offset);
                ItemType = itemType;
                BufferIndex = new KernelBufferIndex(bufferIndexInKernel);
            }
            public readonly DataPortDeclarations.BufferOffset Offset;
            public readonly SimpleType ItemType;
            public readonly KernelBufferIndex BufferIndex;
        }

        public readonly SimpleType KernelData, Kernel, KernelPorts;

        public readonly BlitList<BufferInfo> KernelBufferInfos;
        public readonly bool IsComponentNode;
        public readonly RenderGraph.KernelNode.Flags InitialExecutionFlags;
        public readonly TypeHash KernelHash, KernelDataHash;

        static internal KernelStorageDefinition Create<TDefinition, TKernelData, TKernelPortDefinition, TUserKernel>(bool isComponentNode, bool causesSideEffects)
            where TDefinition : NodeDefinition
            where TKernelData : struct, IKernelData
            where TKernelPortDefinition : struct, IKernelPortDefinition
            where TUserKernel : struct, IGraphKernel<TKernelData, TKernelPortDefinition>
        {
            ValidateRulesForStorage<TDefinition, TKernelData, TKernelPortDefinition, TUserKernel>();

            // Nodes are nominally enabled to ensure they run even when culling is disabled
            RenderGraph.KernelNode.Flags flags = RenderGraph.KernelNode.Flags.Enabled;

            if (isComponentNode)
                flags |= RenderGraph.KernelNode.Flags.IsComponentNode;

            if (causesSideEffects)
                flags |= RenderGraph.KernelNode.Flags.CausesSideEffects;

            return new KernelStorageDefinition(
                isComponentNode,
                flags,
                SimpleType.Create<TKernelData>(),
                SimpleType.Create<TKernelPortDefinition>(),
                SimpleType.Create<TUserKernel>(),
                TypeHash.Create<TUserKernel>(),
                TypeHash.Create<TKernelData>(),
                typeof(TUserKernel)
            );
        }

        KernelStorageDefinition(bool isComponentNode, RenderGraph.KernelNode.Flags initialFlags, SimpleType kernelData, SimpleType kernelPorts, SimpleType kernel, TypeHash kernelHash, TypeHash kernelDataHash, Type kernelType)
        {
            KernelData = kernelData;
            Kernel = kernel;
            KernelPorts = kernelPorts;
            KernelHash = kernelHash;
            KernelDataHash = kernelDataHash;
            InitialExecutionFlags = initialFlags;
            IsComponentNode = isComponentNode;
            KernelBufferInfos = new BlitList<BufferInfo>(0);

            foreach (var field in  WalkTypeInstanceFields(kernelType, BindingFlags.Public | BindingFlags.NonPublic, IsBufferDefinition))
            {
                KernelBufferInfos.Add(new BufferInfo((ushort)KernelBufferInfos.Count, UnsafeUtility.GetFieldOffset(field), new SimpleType(field.FieldType.GetGenericArguments()[0])));
            }
        }

        public void Dispose()
        {
            if (KernelBufferInfos.IsCreated)
                KernelBufferInfos.Dispose();
        }

        static void ValidateRulesForStorage<TDefinition, TKernelData, TKernelPortDefinition, TUserKernel>()
            where TDefinition : NodeDefinition
            where TKernelData : struct, IKernelData
            where TKernelPortDefinition : struct, IKernelPortDefinition
            where TUserKernel : struct, IGraphKernel<TKernelData, TKernelPortDefinition>
        {
            if (!UnsafeUtility.IsUnmanaged<TKernelData>())
                throw new InvalidNodeDefinitionException($"Kernel data type {typeof(TKernelData)} on node definition {typeof(TDefinition)} is not unmanaged");

            if (!UnsafeUtility.IsUnmanaged<TUserKernel>())
                throw new InvalidNodeDefinitionException($"Kernel type {typeof(TUserKernel)} on node definition {typeof(TDefinition)} is not unmanaged");
        }
    }
}
