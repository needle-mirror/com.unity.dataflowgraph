using System;
using System.Diagnostics;
using Unity.Collections;

namespace Unity.DataFlowGraph
{
    unsafe struct InternalNodeData
    {
        public bool HasKernelData => KernelData != null;
        public bool IsCreated => UserData != null;
        public void* UserData;
        // TODO: Can fold with allocation above
        // TODO: Ideally we wouldn't have a conditionally null field here (does node have kernel data?)
        public RenderKernelFunction.BaseData* KernelData;
        // TODO: Could live only with the version?
        public ValidatedHandle Self;
        public int TraitsIndex;
        // Head of linked list.
        public ForwardPortHandle ForwardedPortHead;
        public ArraySizeEntryHandle PortArraySizesHead;
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
    /// Use <see cref="NodeSet.Exists(NodeHandle)"/> to test whether the handle
    /// (still) refers to a valid instance.
    /// <seealso cref="NodeSet.Create{TDefinition}"/>
    /// <seealso cref="NodeSet.Destroy(NodeHandle)"/>
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

        string DebugDisplay() => NodeHandleDebugView.DebugDisplay(this);
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
    /// <seealso cref="NodeSet.CastHandle{TDefinition}(NodeHandle)"/>
    /// </summary>
    [DebuggerDisplay("{DebugDisplay(), nq}")]
    [DebuggerTypeProxy(typeof(NodeHandleDebugView<>))]
    public struct NodeHandle<TDefinition> : IEquatable<NodeHandle<TDefinition>>
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

        string DebugDisplay() => NodeHandleDebugView.DebugDisplay(this);
    }

    /// <summary>
    /// An internal handle always exists (unless destructive APIs are called),
    /// and can only be obtained together with a check (or from a checked place).
    /// A NodeHandle is assumed to not be checked.
    /// You automatically get a <see cref="ValidatedHandle"/> out when resolving
    /// public node handle + port id pair. 
    /// 
    /// <seealso cref="NodeSet.ResolvePublicDestination(NodeHandle, ref InputPortID, out InternalHandle)"/>
    /// <seealso cref="NodeSet.ResolvePublicSource(NodeHandle, ref InputPortID, out InternalHandle)"/>
    /// 
    /// Additionally, you can convert a <see cref="NodeHandle"/> to an <see cref="ValidatedHandle"/> through
    /// <see cref="NodeSet.Validate(NodeHandle)"/>
    /// </summary>
    [DebuggerDisplay("{m_UntypedHandle, nq}")]
    #pragma warning disable 660, 661 // We do not want Equals(object) nor GetHashCode()"
    readonly struct ValidatedHandle : IEquatable<ValidatedHandle>
    {
        readonly NodeHandle m_UntypedHandle;

        internal VersionedHandle VHandle => m_UntypedHandle.VHandle;
        internal ushort NodeSetID => m_UntypedHandle.NodeSetID;

        public static bool operator ==(ValidatedHandle left, ValidatedHandle right)
        {
            return left.m_UntypedHandle == right.m_UntypedHandle;
        }

        public static bool operator !=(ValidatedHandle left, ValidatedHandle right)
        {
            return left.m_UntypedHandle != right.m_UntypedHandle;
        }

        public static ValidatedHandle CheckAndConvert(NodeSet set, NodeHandle handle)
        {
            if (set.Exists(handle))
                return new ValidatedHandle(handle.VHandle);

            if (handle == default)
                throw new ArgumentException("Node is invalid");

            if (set.NodeSetID != handle.NodeSetID)
                throw new ArgumentException("Node was created in another NodeSet");

            throw new ArgumentException("Node is disposed or invalid");
        }

        public static void Bump(ref ValidatedHandle handle)
        {
            var v = handle.m_UntypedHandle.VHandle;
            v.Version++;
            handle = new ValidatedHandle(v);
        }

        public static ValidatedHandle Create(int index, ushort nodeSetID)
        {
            return new ValidatedHandle(new VersionedHandle(index, 1, nodeSetID));
        }

        public NodeHandle ToPublicHandle() => m_UntypedHandle;

        public bool Equals(ValidatedHandle other)
        {
            return this == other;
        }

        ValidatedHandle(VersionedHandle vHandle)
        {
            m_UntypedHandle = new NodeHandle(vHandle);
        }
    }
    #pragma warning restore 660, 661
}
