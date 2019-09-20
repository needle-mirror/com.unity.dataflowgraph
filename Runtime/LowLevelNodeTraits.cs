using System;
using System.Reflection;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Collections;
using System.Runtime.CompilerServices;

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

        public readonly struct InputDeclaration
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
        }

        public readonly struct OutputDeclaration
        {
            public readonly SimpleType Type;
            /// <summary>
            /// The offset for the actual storage in case of an DataOutput{,}
            /// </summary>
            public readonly int PatchOffset;

            public readonly OutputPortID PortNumber;

            public OutputDeclaration(SimpleType type, int patchOffset, OutputPortID portNumber)
            {
                Type = type;
                PatchOffset = patchOffset;
                PortNumber = portNumber;
            }
        }

        internal readonly BlitList<InputDeclaration> Inputs;
        internal readonly BlitList<OutputDeclaration> Outputs;
        /// <summary>
        /// List of offsets of all Buffer<T> instances relative to the beginning of the IKernelDataPorts structure.
        /// </summary>
        internal readonly BlitList<int> OutputBufferOffsets;

        public DataPortDeclarations(Type definitionType, Type kernelPortType)
        {
            (Inputs, Outputs, OutputBufferOffsets) = GenerateDataPortDeclarations(definitionType, kernelPortType);
        }

        static (BlitList<InputDeclaration> inputs, BlitList<OutputDeclaration> outputs, BlitList<int> outputBufferOffsets)
        GenerateDataPortDeclarations(Type definitionType, Type kernelPortType)
        {
            // Offset from the start of the field of the data port to the pointer. A bit of a hack.
            const int k_PtrOffset = 0;

            var inputs = new BlitList<InputDeclaration>(0);
            var outputs = new BlitList<OutputDeclaration>(0);
            var outputBufferOffsets = new BlitList<int>(0);

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
                        outputs.Add(
                            new OutputDeclaration(
                                new SimpleType(dataType),
                                offsetOfWholePortDeclaration + k_PtrOffset,
                                (OutputPortID)assignedPortNumberField.GetValue(portValue)
                            )
                        );

                        if (dataType.IsConstructedGenericType && dataType.GetGenericTypeDefinition() == typeof(Buffer<>))
                        {
                            outputBufferOffsets.Add(offsetOfWholePortDeclaration);
                        }
                        else
                        {
                            foreach (var fieldInfo in dataType.GetFields(BindingFlags.Instance | BindingFlags.Public))
                            {
                                if (fieldInfo.FieldType.IsConstructedGenericType && fieldInfo.FieldType.GetGenericTypeDefinition() == typeof(Buffer<>))
                                {
                                    outputBufferOffsets.Add(offsetOfWholePortDeclaration + UnsafeUtility.GetFieldOffset(fieldInfo));
                                }
                            }
                        }
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
        public struct VirtualTable
        {
            public RenderKernelFunction KernelFunction;

            public static VirtualTable Create()
            {
                VirtualTable ret;
                ret.KernelFunction = RenderKernelFunction.PureVirtual;
                return ret;
            }
        }

        public struct StorageDefinition
        {
            public readonly SimpleType NodeData, KernelData, Kernel, KernelPorts, SimPorts;
            public readonly bool NodeDataIsManaged;

            internal StorageDefinition(bool nodeDataIsManaged, SimpleType nodeData, SimpleType simPorts, SimpleType kernelData, SimpleType kernelPorts, SimpleType kernel)
            {
                NodeDataIsManaged = nodeDataIsManaged;
                NodeData = nodeData;
                SimPorts = simPorts;
                KernelData = kernelData;
                Kernel = kernel;
                KernelPorts = kernelPorts;
            }
        }

        public readonly StorageDefinition Storage;
        public readonly VirtualTable VTable;
        public readonly DataPortDeclarations DataPorts;

        public bool HasKernelData => !VTable.KernelFunction.IsPureVirtual;

        public bool IsCreated { get; private set; }

        public void Dispose()
        {
            if (!IsCreated)
                throw new ObjectDisposedException("LowLevelNodeTraits disposed or not created");

            DataPorts.Dispose();

            IsCreated = false;
        }

        internal LowLevelNodeTraits(StorageDefinition def, VirtualTable table, DataPortDeclarations portDeclarations)
        {
            IsCreated = true;
            Storage = def;
            VTable = table;
            DataPorts = portDeclarations;
        }

        internal LowLevelNodeTraits(StorageDefinition def, VirtualTable table)
        {
            IsCreated = true;
            Storage = def;
            VTable = table;
            DataPorts = new DataPortDeclarations();
        }

    }
}
