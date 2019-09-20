using System;
using System.Reflection;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace Unity.DataFlowGraph
{
    unsafe struct ManagedMemoryAllocator : IDisposable
    {
        internal unsafe struct Page
        {
            class ManagedPage256
            {
                public const int PageSize = 1 << 8;

                public struct Blob
                {
                    public fixed byte Storage[PageSize];
                    // There needs to be an object reference to turn this type into a managed object, that is GC tracked.
                    object m_ManagedObject;
                }

                public Blob Data = default;
            }

            class ManagedPage1K
            {
                public const int PageSize = 1 << 10;

                public struct Blob
                {
                    public fixed byte Storage[PageSize];
                    // There needs to be an object reference to turn this type into a managed object, that is GC tracked.
                    object m_ManagedObject;
                }

                public Blob Data = default;
            }

            class ManagedPage4K
            {
                public const int PageSize = 1 << 12;

                public struct Blob
                {
                    public fixed byte Storage[PageSize];
                    // There needs to be an object reference to turn this type into a managed object, that is GC tracked.
                    object m_ManagedObject;
                }

                public Blob Data = default;
            }

            class ManagedPage16K
            {
                public const int PageSize = 1 << 14;

                public struct Blob
                {
                    public fixed byte Storage[PageSize];
                    // There needs to be an object reference to turn this type into a managed object, that is GC tracked.
                    object m_ManagedObject;
                }

                public Blob Data = default;
            }

            internal int m_Capacity;
            internal int m_ObjectSizeAligned;
            internal ulong m_StrongHandle;
            byte* m_FreeStore;
            int* m_FreeQueue;
            internal int m_FreeObjects;

            static int s_DataMemoryOffset;

            static Page()
            {
                var dataField = typeof(ManagedPage1K).GetField("Data", BindingFlags.Instance | BindingFlags.Public);
                s_DataMemoryOffset = UnsafeUtility.GetFieldOffset(dataField);
            }

            public static void InitializePage(Page* page, int objectSize, int objectAlignment, int desiredPoolSize)
            {
                var alignMask = objectAlignment - 1;
                objectSize = (objectSize + alignMask) & ~alignMask;

                page->m_ObjectSizeAligned = objectSize;

                var desiredMemory = objectSize * desiredPoolSize;
                var bits = Mathf.Log(desiredMemory) / Mathf.Log(2);
                var power = (int)Mathf.Clamp(Mathf.RoundToInt(bits) * 0.5f, 4f, 7f) * 2;
                var finalMemory = 1 << power;

                object managedBlock = null;

                switch (finalMemory)
                {
                    case ManagedPage256.PageSize: managedBlock = new ManagedPage256(); break;
                    case ManagedPage1K.PageSize: managedBlock = new ManagedPage1K(); break;
                    case ManagedPage4K.PageSize: managedBlock = new ManagedPage4K(); break;
                    case ManagedPage16K.PageSize: managedBlock = new ManagedPage16K(); break;
                }

                System.Diagnostics.Debug.Assert(managedBlock != null);

                page->m_FreeObjects = page->m_Capacity = finalMemory / objectSize;

                page->m_FreeStore = s_DataMemoryOffset + (byte*)UnsafeUtility.PinGCObjectAndGetAddress(managedBlock, out page->m_StrongHandle);
                page->m_FreeQueue = (int*)UnsafeUtility.Malloc(sizeof(int) * page->m_Capacity, UnsafeUtility.AlignOf<int>(), Allocator.Persistent);

                System.Diagnostics.Debug.Assert(page->m_FreeStore != null);
                System.Diagnostics.Debug.Assert(page->m_FreeQueue != null);

                UnsafeUtility.MemClear(page->m_FreeQueue, sizeof(int) * page->m_Capacity);

                var nextLastObject = page->m_Capacity - 1;

                for (int i = 0; i < page->m_Capacity; ++i)
                {
                    // mark objects as free in reverse order, so that they are allocated from start
                    page->m_FreeQueue[i] = nextLastObject--;
                }
            }

            public static void DestroyPage(Page* page)
            {
                if (page != null && page->m_StrongHandle != 0 && page->m_FreeStore != null)
                {
                    // TODO: May not be needed.
                    UnsafeUtility.MemClear(page->m_FreeStore, page->m_Capacity * page->m_ObjectSizeAligned);
                    UnsafeUtility.ReleaseGCObject(page->m_StrongHandle);
                    UnsafeUtility.Free(page->m_FreeQueue, Allocator.Persistent);
                }
            }

            public void* Alloc()
            {
                if (m_FreeObjects == 0)
                    return null;

                var newObjectPosition = m_FreeQueue[m_FreeObjects - 1];
                m_FreeObjects--;
                // (memory is always cleared on release, to avoid retaining references)
                return m_FreeStore + newObjectPosition * m_ObjectSizeAligned;
            }

            public bool Free(void* pointer)
            {
                var position = LookupPosition(pointer);
                if (position == -1)
                    return false;

                m_FreeQueue[m_FreeObjects] = position;
                m_FreeObjects++;
                UnsafeUtility.MemClear(pointer, m_ObjectSizeAligned);

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
                var position = delta / m_ObjectSizeAligned;

                if (position < 0 || position > m_Capacity)
                    return -1;

                return (int)position;
            }

            internal int ObjectsInUse()
            {
                return m_Capacity - m_FreeObjects;
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
            public int ObjectAlign;
            public int PoolSize;
        }

        public bool IsCreated => m_Impl != null;

        internal PageNode* GetHeadPage() => &m_Impl->Head;

        Impl* m_Impl;

        /// <summary>
        /// Creates and initializes the managed memory allocator.
        /// </summary>
        /// <param name="objectSize">
        /// This is the size, in bytes, of the object to be allocated.
        /// This is constant for an allocator.
        /// Must be positive and non-zero.
        /// </param>
        /// <param name="objectAlign">
        /// The required alignment for the object.
        /// Must be a power of two, positive and non-zero.
        /// </param>
        /// <param name="desiredPoolSize">
        /// A desired size of a paged pool. Higher numbers may be more optimized
        /// for many and frequent allocations/deallocations, while lower numbers 
        /// may relieve GC pressure.
        /// </param>
        public ManagedMemoryAllocator(int objectSize, int objectAlign, int desiredPoolSize = 16)
        {
            if (desiredPoolSize < 1)
                throw new ArgumentException("Pool size must be at least one", nameof(desiredPoolSize));

            if (objectAlign < 1)
                throw new ArgumentException("Alignment must be at least one", nameof(objectAlign));

            if (objectSize < 1)
                throw new ArgumentException("Sizeof object must be at least one", nameof(objectSize));

            if (!Mathf.IsPowerOfTwo(objectAlign))
                throw new ArgumentException("Alignment must be a power of two", nameof(objectAlign));

            m_Impl = (Impl*)UnsafeUtility.Malloc(sizeof(Impl), UnsafeUtility.AlignOf<Impl>(), Allocator.Persistent);
            m_Impl->ObjectSize = objectSize;
            m_Impl->ObjectAlign = objectAlign;
            m_Impl->PoolSize = desiredPoolSize;
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
            Page.InitializePage(&node->MemoryPage, m_Impl->ObjectSize, m_Impl->ObjectAlign, m_Impl->PoolSize);
            node->Next = null;
        }

        void FreeNode(PageNode* node)
        {
            Page.DestroyPage(&node->MemoryPage);
            UnsafeUtility.Free(node, Allocator.Persistent);
        }
    }
}


