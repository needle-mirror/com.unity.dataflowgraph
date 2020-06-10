using System;
using Unity.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.DataFlowGraph
{
    /// <summary>
    /// Helper which is strictly only available inside a node's <see cref="IGraphKernel{TKernelData,TKernelPortDefinition}.Execute"/>
    /// implementation allowing it to resolve its data ports to actual instance data.
    /// </summary>
    public readonly struct RenderContext
    {
        [NativeDisableUnsafePtrRestriction]
        internal readonly unsafe AtomicSafetyManager* m_SafetyManager;
        internal readonly ValidatedHandle m_CurrentNode;

        internal unsafe RenderContext(in ValidatedHandle handle, AtomicSafetyManager* safetyManager)
        {
            m_SafetyManager = safetyManager;
            m_CurrentNode = handle;
        }

        /// <summary>
        /// Resolves a <see cref="DataOutput{TDefinition,TType}"/> port to a writable data instance returned by reference.
        /// </summary>
        /// <remarks>
        /// Any existing data is undefined and should be fully overwritten by a node's implementation of
        /// <see cref="IGraphKernel{TKernelData,TKernelPortDefinition}.Execute"/>.
        /// </remarks>
        public ref TType Resolve<TNodeDefinition, TType>(ref DataOutput<TNodeDefinition, TType> output)
            where TNodeDefinition : NodeDefinition
            where TType : struct
        {
            ThrowIfEmpty();
            return ref output.m_Value;
        }

        /// <summary>
        /// Resolves a <see cref="DataInput{TDefinition,TType}"/> port to a readable data instance.
        /// </summary>
        public unsafe TType Resolve<TNodeDefinition, TType>(in DataInput<TNodeDefinition, TType> input)
            where TNodeDefinition : NodeDefinition
            where TType : struct
        {
            ThrowIfEmpty();
            return Unsafe.AsRef<TType>(input.Ptr);
        }

        /// <summary>
        /// Resolves a <see cref="DataInput{TDefinition,TType}"/> port of type <see cref="Buffer{T}"/> to a read only
        /// <see cref="NativeArray{T}"/>.
        /// <seealso cref="Buffer{T}.ToNative(Unity.DataFlowGraph.RenderContext)"/>
        /// </summary>
        public NativeArray<T> Resolve<TNodeDefinition, T>(in DataInput<TNodeDefinition, Buffer<T>> inputBuffer)
            where TNodeDefinition : NodeDefinition
            where T : struct
        {
            return Resolve<TNodeDefinition, Buffer<T>>(inputBuffer).ToNative(this);
        }

        /// <summary>
        /// Resolves a <see cref="DataOutput{TDefinition,TType}"/> port of type <see cref="Buffer{T}"/> to a mutable
        /// <see cref="NativeArray{T}"/>.
        /// <seealso cref="Buffer{T}.ToNative(Unity.DataFlowGraph.RenderContext)"/>
        /// </summary>
        /// <remarks>
        /// Any existing data is undefined and should be fully overwritten by a node's implementation of
        /// <see cref="IGraphKernel{TKernelData,TKernelPortDefinition}.Execute"/>.
        /// </remarks>
        public NativeArray<T> Resolve<TNodeDefinition, T>(ref DataOutput<TNodeDefinition, Buffer<T>> outputBuffer)
            where TNodeDefinition : NodeDefinition
            where T : struct
        {
            return Resolve<TNodeDefinition, Buffer<T>>(ref outputBuffer).ToNative(this);
        }

        internal unsafe NativeArray<T> Resolve<T>(in Buffer<T> buffer)
            where T : struct
        {
            ThrowIfEmpty();

            var ret = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(buffer.Ptr, buffer.Size, Allocator.Invalid);

            if (buffer.OwnerNode == m_CurrentNode)
                m_SafetyManager->MarkNativeArrayAsReadWrite(ref ret);
            else
                m_SafetyManager->MarkNativeArrayAsReadOnly(ref ret);

            return ret;
        }

        /// <summary>
        /// A resolved <see cref="PortArray{TInputPort}"/> (see
        /// <see cref="RenderContext.Resolve{TNodeDefinition,TType}(ref Unity.DataFlowGraph.DataOutput{TNodeDefinition,TType})"/>).
        /// </summary>
        [DebuggerDisplay("Size = {Size}")]
        [DebuggerTypeProxy(typeof(ResolvedPortArrayDebugView<,>))]
        public readonly struct ResolvedPortArray<TDefinition, TType>
            where TType : struct
            where TDefinition : NodeDefinition
        {
            /// <summary>
            /// The number of elements in the array.
            /// </summary>
            public ushort Length => PortArray.Size;

            /// <summary>
            /// Indexer for retrieving values of incoming connections to this port array.
            /// </summary>
            /// <returns>The value of the i'th connection.</returns>

            public unsafe TType this[int i]
            {
                get
                {
                    if ((uint)i >= Length)
                        throw new IndexOutOfRangeException();

                    return Unsafe.AsRef<TType>(PortArray[(ushort)i].Ptr);
                }
            }

            readonly PortArray<DataInput<TDefinition, TType>> PortArray;

            internal ResolvedPortArray(in PortArray<DataInput<TDefinition, TType>> portArray)
            {
                PortArray = portArray;
            }
        }

        internal sealed class ResolvedPortArrayDebugView<TDefinition, TType>
            where TType : struct
            where TDefinition : NodeDefinition
        {
            private ResolvedPortArray<TDefinition, TType> m_Array;

            public ResolvedPortArrayDebugView(ResolvedPortArray<TDefinition, TType> array)
            {
                m_Array = array;
            }

            public TType[] Items
            {
                get
                {
                    var ret = new TType[m_Array.Length];
                    for (int i = 0; i < m_Array.Length; ++i)
                        ret[i] = m_Array[i];
                    return ret;
                }
            }
        }

        /// <summary>
        /// Resolves a <see cref="PortArray{TInputPort}"/> of data inputs so that individual items in the array may be
        /// accessed.
        /// </summary>
        public ResolvedPortArray<TDefinition, TType> Resolve<TDefinition, TType>(in PortArray<DataInput<TDefinition, TType>> input)
            where TType : struct
            where TDefinition : NodeDefinition
        {
            ThrowIfEmpty();
            return new ResolvedPortArray<TDefinition, TType>(input);
        }

        unsafe void ThrowIfEmpty()
        {
            if (m_SafetyManager == null)
                throw new ObjectDisposedException("RenderContext not created or destroyed");
        }
    }
}
