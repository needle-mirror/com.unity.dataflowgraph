using Unity.Collections;

namespace Unity.DataFlowGraph
{
    /// <summary>
    /// A destruction context provided to a node on destruction.
    /// <seealso cref="NodeDefinition.Destroy(DestroyContext)"/>
    /// </summary>
    public struct DestroyContext
    {
        /// <summary>
        /// A handle uniquely identifying the node that is currently being destroyed.
        /// </summary>
        public NodeHandle Handle => m_Handle.ToPublicHandle();
        /// <summary>
        /// The <see cref="NodeSetAPI"/> associated with this context.
        /// </summary>
        public readonly NodeSetAPI Set;

        internal readonly ValidatedHandle m_Handle;

        internal DestroyContext(ValidatedHandle handle, NodeSetAPI set)
        {
            m_Handle = handle;
            Set = set;
        }
    }

    /// <summary>
    /// Interface for receiving destructor calls on <see cref="INodeData"/>,
    /// whenever a node is destroyed.
    /// 
    /// This supersedes <see cref="NodeDefinition.Destroy(InitContext)"/>
    /// </summary>
    public interface IDestroy
    {
        /// <summary>
        /// Destructor function, called for each destruction of this type.
        /// <seealso cref="NodeSetAPI.Destroy(NodeHandle)"/>
        /// </summary>
        /// <remarks>
        /// It is undefined behaviour to throw an exception from this method.
        /// </remarks>
        void Destroy(DestroyContext context);
    }

}
