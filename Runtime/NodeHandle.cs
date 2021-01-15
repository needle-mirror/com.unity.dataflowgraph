using System;
using System.Diagnostics;
using Unity.Collections;

namespace Unity.DataFlowGraph
{
    unsafe struct InternalNodeData : IVersionedItem
    {
        public bool HasKernelData => KernelData != null;
        public bool Valid => UserData != null;

        public void* UserData;
        // TODO: Can fold with allocation above
        // TODO: Ideally we wouldn't have a conditionally null field here (does node have kernel data?)
        public RenderKernelFunction.BaseData* KernelData;
        // TODO: Could live only with the version?
        public ValidatedHandle Handle { get; set; }
        public int TraitsIndex;
        // Head of linked list.
        public ForwardPortHandle ForwardedPortHead;
        public ArraySizeEntryHandle PortArraySizesHead;
        public int UpdateIndex;

        public void Dispose()
        {
            UserData = null;
            KernelData = null;
        }
    }

    /// <summary>
    /// An untyped handle to any type of node instance.
    /// A handle can be thought of as a reference or an ID to an instance,
    /// and you can use with the various APIs in <see cref="NodeSet"/> to
    /// interact with the node.
    ///
    /// A valid handle is guaranteed to not be equal to a default initialized
    /// node handle. After a handle is destroyed, any handle with this value
    /// will be invalid.
    ///
    /// Use <see cref="NodeSetAPI.Exists(NodeHandle)"/> to test whether the handle
    /// (still) refers to a valid instance.
    /// <seealso cref="NodeSetAPI.Create{TDefinition}"/>
    /// <seealso cref="NodeSetAPI.Destroy(NodeHandle)"/>
    /// </summary>
    [DebuggerDisplay("{DebugDisplay(), nq}")]
    [DebuggerTypeProxy(typeof(NodeHandleDebugView))]
    public readonly struct NodeHandle : IEquatable<NodeHandle>
    {
        internal readonly VersionedHandle VHandle;
        internal ushort NodeSetID => VHandle.ContainerID;

        internal NodeHandle(VersionedHandle handle)
        {
            VHandle = handle;
        }

        public static bool operator ==(NodeHandle left, NodeHandle right)
        {
            return left.VHandle == right.VHandle;
        }

        public static bool operator !=(NodeHandle left, NodeHandle right)
        {
            return left.VHandle != right.VHandle;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
                return false;
            return obj is NodeHandle handle && Equals(handle);
        }

        public override int GetHashCode()
        {
            return VHandle.Index;
        }

        public bool Equals(NodeHandle other)
        {
            return this == other;
        }

        public override string ToString()
        {
            return $"Index: {VHandle.Index}, Version: {VHandle.Version}, NodeSetID: {NodeSetID}";
        }

        internal string DebugDisplay() => NodeHandleDebugView.DebugDisplay(this);
    }

    /// <summary>
    /// A strongly typed version of a <see cref="NodeHandle"/>.
    ///
    /// A strongly typed version can automatically decay to an untyped
    /// <see cref="NodeHandle"/>, but the other way around requires a cast.
    ///
    /// Strongly typed handles are pre-verified and subsequently can be a lot
    /// more efficient in usage, as no type checks need to be performed
    /// internally.
    ///
    /// <seealso cref="NodeSetAPI.CastHandle{TDefinition}(NodeHandle)"/>
    /// </summary>
    [DebuggerDisplay("{DebugDisplay(), nq}")]
    [DebuggerTypeProxy(typeof(NodeHandleDebugView<>))]
    public partial struct NodeHandle<TDefinition> : IEquatable<NodeHandle<TDefinition>>
        where TDefinition : NodeDefinition
    {
        readonly NodeHandle m_UntypedHandle;

        internal VersionedHandle VHandle => m_UntypedHandle.VHandle;

        internal NodeHandle(VersionedHandle vHandle)
        {
            m_UntypedHandle = new NodeHandle(vHandle);
        }

        public static implicit operator NodeHandle(NodeHandle<TDefinition> handle) { return handle.m_UntypedHandle; }

        public bool Equals(NodeHandle<TDefinition> other)
        {
            return m_UntypedHandle == other.m_UntypedHandle;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
                return false;
            return obj is NodeHandle<TDefinition> handle && Equals(handle);
        }

        public override int GetHashCode()
        {
            return m_UntypedHandle.GetHashCode();
        }

        public static bool operator ==(NodeHandle<TDefinition> left, NodeHandle<TDefinition> right)
        {
            return left.m_UntypedHandle == right.m_UntypedHandle;
        }

        public static bool operator !=(NodeHandle<TDefinition> left, NodeHandle<TDefinition> right)
        {
            return left.m_UntypedHandle != right.m_UntypedHandle;
        }

        internal string DebugDisplay() => NodeHandleDebugView.DebugDisplay(this);
    }

    static class HelperExtensions
    {
        public static NodeHandle ToPublicHandle(this ValidatedHandle handle)
            => new NodeHandle(handle.Versioned);
    }
}
