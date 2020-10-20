using System;
using System.Reflection;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Collections;
using static Unity.DataFlowGraph.ReflectionTools;
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

            if (Align < 1 || (Size != 0 && Align > Size) || Size % Align != 0 || math.countbits(Align) != 1)
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
                return ref UnsafeUtility.AsRef<UntypedDataInputPortArray>((byte*)ports + PatchOffset);
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
#if DFG_ASSERTIONS
                if (ports == null)
                    throw new AssertionException("Unexpected null pointer in DataOutput value dereferencing");
#endif
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
                var kernelPortValue = definitionType.GetField("KernelPorts", BindingFlags.FlattenHierarchy | BindingFlags.Static | BindingFlags.Public)?.GetValue(null);

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

                    var portValue = potentialPortFieldInfo.GetValue(kernelPortValue);

                    if (genericPortType == typeof(DataInput<,>))
                    {
                        InputPortID inputID;

                        // Acquire the assigned port number of this port declaration
                        if (isPortArray)
                        {
                            var inputOutputPortID = (InputOutputPortID)portValue
                                .GetType()
                                .GetField("m_PortID", BindingFlags.Instance | BindingFlags.NonPublic)
                                .GetValue(portValue);
                            inputID = inputOutputPortID.InputPort;
                        }
                        else
                        {
                            var dataStorage = (DataInputStorage)portType
                                .GetField("Storage", BindingFlags.Instance | BindingFlags.NonPublic)
                                .GetValue(portValue);
                            inputID = dataStorage.PortID;
                        }

                        if (UnsafeUtility.SizeOf(dataType) > k_MaxInputSize)
                            throw new InvalidNodeDefinitionException($"Node input data structure types cannot have a sizeof larger than {k_MaxInputSize}");

                        inputs.Add(
                            new InputDeclaration(
                                new SimpleType(dataType),
                                offsetOfWholePortDeclaration + k_PtrOffset,
                                inputID,
                                isPortArray
                            )
                        );
                    }
                    else if (genericPortType == typeof(DataOutput<,>))
                    {
                        // Acquire the assigned port number of this port declaration
                        var assignedPortNumberField = portType.GetField("Port", BindingFlags.Instance | BindingFlags.NonPublic);
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
        }

        static void ValidateDataPortType(FieldInfo port, Type internalPortType)
        {
            if (!UnsafeUtility.IsUnmanaged(internalPortType))
                throw new InvalidNodeDefinitionException($"Data port type {internalPortType} in {port} is not unmanaged");
        }

        public ref readonly OutputDeclaration FindOutputDataPort(OutputPortID port)
            => ref Outputs[FindOutputDataPortNumber(port)];

        public ref readonly InputDeclaration FindInputDataPort(InputPortID port)
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

    readonly struct TypeHash : IEquatable<TypeHash>
    {
        readonly Int32 m_TypeHash;

        public static TypeHash Create<TType>() => new TypeHash(BurstRuntime.GetHashCode32<TType>());

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

        public readonly bool NodeDataIsManaged, IsScaffolded;

        internal static readonly SimulationStorageDefinition Empty = new SimulationStorageDefinition(false, false, default, SimpleType.Create<EmptyType>(), SimpleType.Create<EmptyType>());

        static internal SimulationStorageDefinition Create<TDefinition, TNodeData, TSimPorts>(bool nodeDataIsManaged, bool isScaffolded)
            where TDefinition : NodeDefinition
            where TNodeData : struct, INodeData
            where TSimPorts : struct, ISimulationPortDefinition
        {
            ValidateRulesForStorage<TDefinition, TNodeData>(nodeDataIsManaged);
            return new SimulationStorageDefinition(
                nodeDataIsManaged,
                isScaffolded,
                TypeHash.Create<TNodeData>(),
                SimpleType.Create<TNodeData>(),
                SimpleType.Create<TSimPorts>()
            );
        }

        static internal SimulationStorageDefinition Create<TDefinition, TNodeData>(bool nodeDataIsManaged, bool isScaffolded)
            where TDefinition : NodeDefinition
            where TNodeData : struct, INodeData
        {
            ValidateRulesForStorage<TDefinition, TNodeData>(nodeDataIsManaged);
            return new SimulationStorageDefinition(
                nodeDataIsManaged,
                isScaffolded,
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
                false,
                default,
                SimpleType.Create<EmptyType>(),
                SimpleType.Create<TSimPorts>()
            );
        }

        SimulationStorageDefinition(bool nodeDataIsManaged, bool isScaffolded, TypeHash nodeDataHash, SimpleType nodeData, SimpleType simPorts)
        {
            NodeData = nodeData;
            SimPorts = simPorts;
            NodeDataHash = nodeDataHash;
            NodeDataIsManaged = nodeDataIsManaged;
            IsScaffolded = isScaffolded;
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

        public readonly SimpleType KernelData, Kernel, KernelPorts;

        public readonly BlitList<BufferInfo> KernelBufferInfos;
        public readonly bool IsComponentNode;

        public readonly TypeHash KernelHash, KernelDataHash;

        static internal KernelStorageDefinition Create<TDefinition, TKernelData, TKernelPortDefinition, TUserKernel>(bool isComponentNode)
            where TDefinition : NodeDefinition
            where TKernelData : struct, IKernelData
            where TKernelPortDefinition : struct, IKernelPortDefinition
            where TUserKernel : struct, IGraphKernel<TKernelData, TKernelPortDefinition>
        {
            ValidateRulesForStorage<TDefinition, TKernelData, TKernelPortDefinition, TUserKernel>();
            return new KernelStorageDefinition(isComponentNode,
                SimpleType.Create<TKernelData>(),
                SimpleType.Create<TKernelPortDefinition>(),
                SimpleType.Create<TUserKernel>(),
                TypeHash.Create<TUserKernel>(),
                TypeHash.Create<TKernelData>(),
                typeof(TUserKernel)
            );
        }

        KernelStorageDefinition(bool isComponentNode, SimpleType kernelData, SimpleType kernelPorts, SimpleType kernel, TypeHash kernelHash, TypeHash kernelDataHash, Type kernelType)
        {
            KernelData = kernelData;
            Kernel = kernel;
            KernelPorts = kernelPorts;
            KernelHash = kernelHash;
            KernelDataHash = kernelDataHash;
            IsComponentNode = isComponentNode;
            KernelBufferInfos = new BlitList<BufferInfo>(0);

            foreach (var field in  WalkTypeInstanceFields(kernelType, BindingFlags.Public | BindingFlags.NonPublic, IsBufferDefinition))
            {
                KernelBufferInfos.Add(new BufferInfo(UnsafeUtility.GetFieldOffset(field), new SimpleType(field.FieldType.GetGenericArguments()[0])));
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
