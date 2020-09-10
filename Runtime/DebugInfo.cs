using System;
using System.Collections.Generic;

namespace Unity.DataFlowGraph
{
    static class DebugInfo
    {
        static Dictionary<ushort, NodeSetAPI> s_RegisteredNodeSets = new Dictionary<ushort, NodeSetAPI>();

        public static void RegisterNodeSetCreation(NodeSetAPI set)
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

        public static void RegisterNodeSetDisposed(NodeSetAPI set)
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

        internal static NodeSetAPI DebugGetNodeSet(ushort nodeSetID)
        {
            return s_RegisteredNodeSets.TryGetValue(nodeSetID, out var set) ? set : null;
        }
    }
}
