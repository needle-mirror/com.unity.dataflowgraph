using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;

namespace Unity.DataFlowGraph
{
    class AssertionException : Exception { public AssertionException(string msg) : base(msg) { } }

    class InternalException : Exception { public InternalException(string msg) : base(msg) { } }

    static class Utility
    {
        /// <summary>
        /// Allocates appropriate storage for the type,
        /// zero-initialized.
        /// </summary>
        /// <remarks>
        /// Free using UnsafeUtility.Free()
        /// </remarks>
        public static unsafe void* CAlloc(Type type, Allocator allocator)
        {
            var size = UnsafeUtility.SizeOf(type);
            var ptr = UnsafeUtility.Malloc(size, 16, allocator);
            UnsafeUtility.MemClear(ptr, size);
            return ptr;
        }

        /// <summary>
        /// Allocates appropriate storage for the type,
        /// zero-initialized.
        /// </summary>
        /// <remarks>
        /// Free using UnsafeUtility.Free()
        /// </remarks>
        public static unsafe TType* CAlloc<TType>(Allocator allocator)
            where TType : unmanaged
        {
            var ptr = (TType*)UnsafeUtility.Malloc(sizeof(TType), UnsafeUtility.AlignOf<TType>(), allocator);
            UnsafeUtility.MemClear(ptr, sizeof(TType));
            return ptr;
        }

        /// <summary>
        /// Allocates appropriate storage for the type,
        /// zero-initialized.
        /// </summary>
        /// <remarks>
        /// Free using UnsafeUtility.Free()
        /// </remarks>
        public static unsafe void* CAlloc(SimpleType type, Allocator allocator)
        {
            var ptr = UnsafeUtility.Malloc(type.Size, type.Align, allocator);
            UnsafeUtility.MemClear(ptr, type.Size);
            return ptr;
        }

        public static unsafe JobHandle CombineDependencies(JobHandle a, JobHandle b, JobHandle c, JobHandle d)
        {
            var array = stackalloc JobHandle[4] { a, b, c, d };
            return JobHandleUnsafeUtility.CombineDependencies(array, 4);
        }

        /// <summary>
        /// Local implementation of <see cref="System.Runtime.CompilerServices.Unsafe.AsRef{T}(in T)"/>
        /// </summary>
        public static ref T AsRef<T>(in T source)
        {
            // This body is generated during ILPP.
            throw new NotImplementedException();
        }

        /// <summary>
        /// Local implementation of <see cref="System.Runtime.CompilerServices.Unsafe.AsRef{T}(void*)"/>
        /// </summary>
        public static unsafe ref T AsRef<T>(void* source)
        {
            // This body is generated during ILPP.
            throw new NotImplementedException();
        }

        /// <summary>
        /// Local implementation of <see cref="System.Runtime.CompilerServices.Unsafe.AsPointer{T}(ref T)"/> but taking an
        /// "in" parameter instead of by "ref".
        /// </summary>
        public static unsafe void* AsPointer<T>(in T value)
        {
            // This body is generated during ILPP.
            throw new NotImplementedException();
        }
    }
}
