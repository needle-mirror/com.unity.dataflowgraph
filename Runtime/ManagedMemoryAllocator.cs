using System;
using System.Reflection;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace Unity.DataFlowGraph
{
    /// <summary>
    /// Interface for allocating bulk amounts of some particular managed struct, as an array.
    /// </summary>
    unsafe interface IManagedMemoryPoolAllocator
    {
        /// <summary>
        /// <code>UnsafeUtility.SizeOf<T>()</code>
        /// </summary>
        int ObjectSize { get; }
        /// <summary>
        /// Allocate a <code>T[]</code> array of <paramref>count</paramref> size and immediately pin it using 
        /// <see cref="UnsafeUtility.PinGCArrayAndGetDataAddress(Array, out ulong)"/>.
        /// </summary>
        /// <param name="count">
        /// The array size.
        /// </param>
        /// <param name="gcHandle">
        /// The output gc handle from a <see cref="UnsafeUtility.PinGCArrayAndGetDataAddress(Array, out ulong)"/> operation.
        /// </param>
        /// <returns>
        /// The result of a <see cref="UnsafeUtility.PinGCArrayAndGetDataAddress(Array, out ulong)"/> operation.
        /// </returns>
        void* AllocatePrepinnedGCArray(int count, out ulong gcHandle);
    }

    unsafe struct ManagedMemoryAllocator : IDisposable
    {
        internal unsafe struct Page
        {
            internal int Capacity;
            internal int ObjectSize;
            internal ulong StrongHandle;
            byte* m_FreeStore;
            int* m_FreeQueue;
            internal int FreeObjects;

            public static void InitializePage(Page* page, IManagedMemoryPoolAllocator allocator, int objectSize, int poolSize)
            {
                page->ObjectSize = objectSize;
                page->FreeObjects = page->Capacity = poolSize;

                page->m_FreeStore = (byte*)allocator.AllocatePrepinnedGCArray(poolSize, out page->StrongHandle);
                page->m_FreeQueue = (int*)UnsafeUtility.Malloc(sizeof(int) * page->Capacity, UnsafeUtility.AlignOf<int>(), Allocator.Persistent);

                if (page->m_FreeStore == null || page->m_FreeQueue == null)
                    throw new OutOfMemoryException();
                
                var nextLastObject = page->Capacity - 1;

                for (int i = 0; i < page->Capacity; ++i)
                {
                    // mark objects as free in reverse order, so that they are allocated from start
                    page->m_FreeQueue[i] = nextLastObject--;
                }
            }

            public static void DestroyPage(Page* page)
            {
                if (page != null && page->StrongHandle != 0 && page->m_FreeStore != null)
                {
                    // TODO: May not be needed.
                    UnsafeUtility.MemClear(page->m_FreeStore, page->Capacity * page->ObjectSize);
                    UnsafeUtility.ReleaseGCObject(page->StrongHandle);
                    UnsafeUtility.Free(page->m_FreeQueue, Allocator.Persistent);
                }
            }

            public void* Alloc()
            {
                if (FreeObjects == 0)
                    return null;

                var newObjectPosition = m_FreeQueue[FreeObjects - 1];
                FreeObjects--;
                // (memory is always cleared on release, to avoid retaining references)
                return m_FreeStore + newObjectPosition * ObjectSize;
            }

            public bool Free(void* pointer)
            {
                var position = LookupPosition(pointer);
                if (position == -1)
                    return false;

                m_FreeQueue[FreeObjects] = position;
                FreeObjects++;
                UnsafeUtility.MemClear(pointer, ObjectSize);

                // TODO: Can check if position exists in free queue, and throw exception for double free'ing
                return true;
            }

            public bool Contains(void* pointer)
            {
                return LookupPosition(pointer) != -1;
            }

            int LookupPosition(void* pointer)
            {
                var delta = (byte*)pointer - m_FreeStore;
                var position = delta / ObjectSize;

                if (position < 0 || position > Capacity)
                    return -1;

                return (int)position;
            }

            internal int ObjectsInUse()
            {
                return Capacity - FreeObjects;
            }
        }

        internal struct PageNode
        {
            public Page MemoryPage;
            public PageNode* Next;
        }

        struct Impl
        {
            public PageNode Head;
            public int ObjectSize;
            public int PoolSize;
        }

        public bool IsCreated => m_Impl != null;

        internal PageNode* GetHeadPage() => &m_Impl->Head;

        Impl* m_Impl;
        IManagedMemoryPoolAllocator m_ClientAllocator;

        /// <summary>
        /// Creates and initializes the managed memory allocator.
        /// </summary>
        /// <param name="clientAllocator">
        /// Pool allocator to be used in this item allocator.
        /// </param>
        /// <param name="desiredPoolSize">
        /// A desired size of a paged pool. Higher numbers may be more optimized
        /// for many and frequent allocations/deallocations, while lower numbers 
        /// may relieve GC pressure.
        /// </param>
        public ManagedMemoryAllocator(IManagedMemoryPoolAllocator clientAllocator, int desiredPoolSize = 16)
        {
            if (clientAllocator == null)
                throw new ArgumentNullException(nameof(clientAllocator));

            if (desiredPoolSize < 1)
                throw new ArgumentException("Pool size must be at least one", nameof(desiredPoolSize));

            var objectSize = clientAllocator.ObjectSize;

            if (objectSize < 1)
                throw new ArgumentException("Sizeof object must be at least one", nameof(IManagedMemoryPoolAllocator.ObjectSize));

            m_Impl = (Impl*)UnsafeUtility.Malloc(sizeof(Impl), UnsafeUtility.AlignOf<Impl>(), Allocator.Persistent);
            m_Impl->ObjectSize = objectSize;
            m_Impl->PoolSize = desiredPoolSize;
            m_ClientAllocator = clientAllocator;

            InitializeNode(GetHeadPage());
        }

        /// <summary>
        /// Allocates an object with the properties given in the constructor.
        /// Contents guaranteed to be zero-initialized.
        /// Throws exception if out of memory, otherwise won't fail.
        /// </summary>
        /// <remarks>
        /// Must be free'd through ManagedMemoryAllocator.Free().
        /// </remarks>
        public void* Alloc()
        {
            if (!IsCreated)
                throw new ObjectDisposedException("Managed memory allocator disposed");

            PageNode* current = null;
            void* memory = null;

            // see if we have a page in the list with a free object
            for (current = GetHeadPage(); ; current = current->Next)
            {
                memory = current->MemoryPage.Alloc();
                if (memory != null)
                    return memory;

                // reached end of list, have to create a new
                if (current->Next == null)
                {
                    current->Next = CreateNode();
                    memory = current->Next->MemoryPage.Alloc();

                    if (memory != null)
                        return memory;

                    throw new OutOfMemoryException();
                }
            }
        }

        /// <summary>
        /// Free's a previously allocated object through Alloc().
        /// Inputting a pointer acquired anywhere else (including null pointers) is 
        /// undefined behaviour.
        /// </summary>
        public void Free(void* memory)
        {
            if (!IsCreated)
                throw new ObjectDisposedException("Managed memory allocator disposed");

            for (var current = GetHeadPage(); current != null; current = current->Next)
            {
                if (current->MemoryPage.Free(memory))
                    return;
            }

            // (will throw for null pointers as well)
            throw new ArgumentException("Attempt to free invalid managed memory pointer");
        }

        /// <summary>
        /// Disposes and releases all allocations back to the system.
        /// Will print diagnostics about potential memory leaks.
        /// </summary>
        public void Dispose()
        {
            if (!IsCreated)
                throw new ObjectDisposedException("Managed memory allocator disposed");

            int memoryLeaks = 0;
            PageNode* head = GetHeadPage();
            PageNode* current = head;

            while (current != null)
            {
                memoryLeaks += current->MemoryPage.ObjectsInUse();

                var old = current;
                current = current->Next;

                // The head page node is allocated in-place in the Impl, so it shouldn't be free'd here.
                // Small memory locality optimization for few instances of many types.
                if (old != head)
                    FreeNode(old);
            }

            if (memoryLeaks > 0)
                Debug.LogWarning($"{memoryLeaks} memory leak(s) found while disposing ManagedMemoryAllocator");

            UnsafeUtility.Free(m_Impl, Allocator.Persistent);

            this = new ManagedMemoryAllocator();
        }

        PageNode* CreateNode()
        {
            var node = (PageNode*)UnsafeUtility.Malloc(sizeof(PageNode), UnsafeUtility.AlignOf<PageNode>(), Allocator.Persistent);

            if (node == null)
                throw new OutOfMemoryException();

            InitializeNode(node);

            return node;
        }

        void InitializeNode(PageNode* node)
        {
            Page.InitializePage(&node->MemoryPage, m_ClientAllocator, m_Impl->ObjectSize, m_Impl->PoolSize);
            node->Next = null;
        }

        void FreeNode(PageNode* node)
        {
            Page.DestroyPage(&node->MemoryPage);
            UnsafeUtility.Free(node, Allocator.Persistent);
        }
    }
}


