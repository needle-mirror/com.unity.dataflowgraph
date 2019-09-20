using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.DataFlowGraph
{
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
        public static unsafe void* CAlloc(SimpleType type, Allocator allocator)
        {
            var ptr = UnsafeUtility.Malloc(type.Size, type.Align, allocator);
            UnsafeUtility.MemClear(ptr, type.Size);
            return ptr;
        }
    }
}
