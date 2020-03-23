using System;
using System.Collections.Generic;

namespace Unity.DataFlowGraph
{
    static class DebugInfo
    {
        static Dictionary<ushort, NodeSet> s_RegisteredNodeSets = new Dictionary<ushort, NodeSet>();

        public static void RegisterNodeSetCreation(NodeSet set)
        {
            try
            {
                s_RegisteredNodeSets.Add(set.NodeSetID, set);
            }
            catch (ArgumentException)
            {
                // Clear out the existing NodeSet as it will from now on be impossible to definitively resolve NodeHandles
                // to their owning NodeSet.
                s_RegisteredNodeSets.Remove(set.NodeSetID);
                throw new InvalidOperationException("Conflicting NodeSet unique IDs.");
            }
        }

        public static void RegisterNodeSetDisposed(NodeSet set)
        {
            try
            {
                s_RegisteredNodeSets.Remove(set.NodeSetID);
            }
            catch (ArgumentNullException)
            {
                throw new InternalException("Could not unregister NodeSet.");
            }
        }

        internal static NodeSet DebugGetNodeSet(ushort nodeSetID)
        {
            return s_RegisteredNodeSets.TryGetValue(nodeSetID, out var set) ? set : null;
        }
    }
}
