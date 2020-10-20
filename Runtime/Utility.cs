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
            => CAlloc(new SimpleType(type), allocator);

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

        /// <summary>
        /// Reallocates a region of memory given its old pointer and size. Old content is preserved.
        /// </summary>
        /// <remarks>
        /// It is valid to pass in a null pointer and zero size for the old allocation.
        /// If the new size is zero, a null pointer will be returned and the old memory freed if non-null.
        /// </remarks>
        public static unsafe void* ReAlloc(void* oldPointer, int oldSize, SimpleType newSize, Allocator allocator)
        {
#if DFG_ASSERTIONS
            if (oldSize < 0 || newSize.Size < 0)
                throw new AssertionException("Reallocation given negative size");

            if (oldPointer == null && oldSize != 0)
                throw new AssertionException("Reallocation given a null pointer and non-zero size");
#endif

            var newPointer = newSize.Size > 0 ? UnsafeUtility.Malloc(newSize.Size, newSize.Align, allocator) : null;

            var preserveSize = Math.Min(newSize.Size, oldSize);
            if (preserveSize > 0)
                UnsafeUtility.MemCpy(newPointer, oldPointer, preserveSize);

            UnsafeUtility.Free(oldPointer, allocator);

            return newPointer;
        }

        public static unsafe JobHandle CombineDependencies(JobHandle a, JobHandle b, JobHandle c, JobHandle d)
        {
            var array = stackalloc JobHandle[4] { a, b, c, d };
            return JobHandleUnsafeUtility.CombineDependencies(array, 4);
        }

        /// <summary>
        /// Implementation of Collections.LowLevel.Unsafe.UnsafeUtilityExtensions.AddressOf{T}(in T) without a struct
        /// constraint.
        /// </summary>
        public static unsafe void* AddressOfEvenIfManaged<T>(in T value)
        {
            // This body is generated during ILPP.
            throw new NotImplementedException();
        }
    }
}
