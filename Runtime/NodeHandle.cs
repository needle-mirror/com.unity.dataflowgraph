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
        public VersionedHandle VHandle;
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
    [DebuggerDisplay("{VHandle, nq}")]
    public readonly struct NodeHandle : IEquatable<NodeHandle>
    {

        internal readonly VersionedHandle VHandle;

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
    [DebuggerDisplay("{VHandle, nq}")]
    public struct NodeHandle<TDefinition> : IEquatable<NodeHandle<TDefinition>>
        where TDefinition : INodeDefinition
    {
        internal readonly VersionedHandle VHandle;

        internal NodeHandle(VersionedHandle vHandle)
        {
            VHandle = vHandle;
        }

        public static implicit operator NodeHandle(NodeHandle<TDefinition> handle) { return new NodeHandle(handle.VHandle); }

        public bool Equals(NodeHandle<TDefinition> other)
        {
            return VHandle == other.VHandle;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
                return false;
            return obj is NodeHandle<TDefinition> handle && Equals(handle);
        }

        public override int GetHashCode()
        {
            return VHandle.Index;
        }

        public static bool operator ==(NodeHandle<TDefinition> left, NodeHandle<TDefinition> right)
        {
            return left.VHandle == right.VHandle;
        }

        public static bool operator !=(NodeHandle<TDefinition> left, NodeHandle<TDefinition> right)
        {
            return left.VHandle != right.VHandle;
        }

    }

}
