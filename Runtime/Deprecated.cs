using System;
using Unity.Collections;
using Unity.Entities;

namespace Unity.DataFlowGraph
{
    [Obsolete("Use ComponentNode + connections instead", true)]
    public interface INodeMemoryInputTag { }

    [Obsolete("Use ComponentNode + connections instead", true)]
    public struct NodeMemoryInput<TTag, TBufferToMove> { }

    [Obsolete("", true)]
    public class NativeAllowReinterpretationAttribute : Attribute { }

    [Obsolete("Use ComponentNode instead", true)]
    public abstract class MemoryInputSystem<TTag, TBufferToMove> { }

    public partial class NodeSet
    {
        /// <summary>
        /// Query whether <see cref="Dispose"/> has been called on the <see cref="NodeSet"/>.
        /// </summary>
        [Obsolete("Use IsCreated instead")]
        public bool IsDisposed()
        {
            return m_IsDisposed;
        }
    }

#if !ENTITIES_0_12_OR_NEWER
    static class ForwardCompatibility
    {
        static public ArchetypeChunkBufferType<T> GetBufferTypeHandle<T>(this ComponentSystemBase self, bool isReadOnly = false)
            where T : struct, IBufferElementData
                => self.GetArchetypeChunkBufferType<T>(isReadOnly);

        static public ArchetypeChunkEntityType GetEntityTypeHandle(this ComponentSystemBase self)
            => self.GetArchetypeChunkEntityType();

        static public ArchetypeChunkComponentType<T> GetComponentTypeHandle<T>(this ComponentSystemBase self, bool isReadOnly = false)
            where T : struct, IComponentData
                => self.GetArchetypeChunkComponentType<T>(isReadOnly);

        static public BufferAccessor<T> GetBufferAccessor<T>(this ArchetypeChunk self, BufferTypeHandle<T> bufferComponentType)
            where T : struct, IBufferElementData
                => self.GetBufferAccessor((ArchetypeChunkBufferType<T>)bufferComponentType);

        static public NativeArray<T> GetNativeArray<T>(this ArchetypeChunk self, ComponentTypeHandle<T> chunkComponentType)
            where T : struct, IComponentData
                => self.GetNativeArray((ArchetypeChunkComponentType<T>)chunkComponentType);
    }

    struct ComponentTypeHandle<T>
    {
        ArchetypeChunkComponentType<T> m_Value;

        public static implicit operator ArchetypeChunkComponentType<T>(ComponentTypeHandle<T> v)
            => v.m_Value;

        public static implicit operator ComponentTypeHandle<T>(ArchetypeChunkComponentType<T> v)
            => new ComponentTypeHandle<T> {m_Value = v};
    }

    struct BufferTypeHandle<T>
        where T : struct, IBufferElementData
    {
        ArchetypeChunkBufferType<T> m_Value;

        public static implicit operator ArchetypeChunkBufferType<T>(BufferTypeHandle<T> v)
            => v.m_Value;

        public static implicit operator BufferTypeHandle<T>(ArchetypeChunkBufferType<T> v)
            => new BufferTypeHandle<T> {m_Value = v};
    }

    struct EntityTypeHandle
    {
        ArchetypeChunkEntityType m_Value;

        public static implicit operator ArchetypeChunkEntityType(EntityTypeHandle v)
            => v.m_Value;

        public static implicit operator EntityTypeHandle(ArchetypeChunkEntityType v)
            => new EntityTypeHandle {m_Value = v};
    }
#endif
}
