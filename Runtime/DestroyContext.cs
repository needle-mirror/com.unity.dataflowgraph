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

        internal readonly ValidatedHandle m_Handle;
        readonly NodeSet m_Set;

        internal DestroyContext(ValidatedHandle handle, NodeSet set)
        {
            m_Handle = handle;
            m_Set = set;
        }
    }

}
