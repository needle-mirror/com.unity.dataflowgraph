using System;
using Unity.Collections;

namespace Unity.DataFlowGraph
{
    struct ForwardPortHandle
    {
        public static ForwardPortHandle Invalid => new ForwardPortHandle { Index = 0 };
        public int Index;

        public static implicit operator ForwardPortHandle(int arg)
        {
            return new ForwardPortHandle { Index = arg };
        }

        public static implicit operator int(ForwardPortHandle handle)
        {
            return handle.Index;
        }

        public override string ToString()
        {
            return $"{Index}";
        }
    }

    struct ForwardedPort
    {
        public NodeHandle Replacement;
        // Forward linked list
        public ForwardPortHandle NextIndex;
        ushort m_OriginPortID, m_ReplacedPortID;

        public readonly bool IsInput;

        public static ForwardedPort Input(InputPortID originPortID, NodeHandle replacement, InputPortID replacedPortID)
        {
            return new ForwardedPort(true, originPortID.Port, replacement, replacedPortID.Port);
        }

        public static ForwardedPort Output(OutputPortID originPortID, NodeHandle replacement, OutputPortID replacedPortID)
        {
            return new ForwardedPort(false, originPortID.Port, replacement, replacedPortID.Port);
        }

        public bool SimplifyNestedForwardedPort(in ForwardedPort other)
        {
            if (IsInput == other.IsInput && m_ReplacedPortID == other.m_OriginPortID)
            {
                Replacement = other.Replacement;
                m_ReplacedPortID = other.m_ReplacedPortID;
                return true;
            }

            return false;
        }

        public ushort GetOriginPortCounter()
        {
            return m_OriginPortID;
        }

        public InputPortID GetOriginInputPortID()
        {
            if (!IsInput)
                throw new InvalidOperationException("Assertion error: Forwarded port does not represent an input");

            return new InputPortID(m_OriginPortID);
        }

        public OutputPortID GetOriginOutputPortID()
        {
            if (IsInput)
                throw new InvalidOperationException("Assertion error: Forwarded port does not represent an output");

            return new OutputPortID { Port = m_OriginPortID };
        }

        public InputPortID GetReplacedInputPortID()
        {
            if (!IsInput)
                throw new InvalidOperationException("Assertion error: Forwarded port does not represent an input");

            return new InputPortID(m_ReplacedPortID);
        }

        public OutputPortID GetReplacedOutputPortID()
        {
            if (IsInput)
                throw new InvalidOperationException("Assertion error: Forwarded port does not represent an output");

            return new OutputPortID { Port = m_ReplacedPortID };
        }

        ForwardedPort(bool isInput, ushort originPortId, NodeHandle replacement, ushort replacementPortId)
        {
            IsInput = isInput;
            Replacement = replacement;
            m_OriginPortID = originPortId;
            m_ReplacedPortID = replacementPortId;
            NextIndex = ForwardPortHandle.Invalid;
        }
    }

    public partial class NodeSet
    {
        BlitList<int> m_FreeForwardingTables = new BlitList<int>(0, Allocator.Persistent);
        BlitList<ForwardedPort> m_ForwardingTable = new BlitList<ForwardedPort>(0, Allocator.Persistent);

        void MergeForwardConnectionsToTable(ref InternalNodeData node, /* in */ BlitList<ForwardedPort> forwardedConnections)
        {
            var currentIndex = AllocateForwardConnection();
            node.ForwardedPortHead = currentIndex;
            ref ForwardedPort root = ref m_ForwardingTable[currentIndex];
            root = forwardedConnections[0];

            if (!Exists(forwardedConnections[0].Replacement))
                throw new ArgumentException($"Replacement node on forward request 0 doesn't exist");

            // Merge temporary forwarded connections into forwarding table
            for (int i = 1; i < forwardedConnections.Count; ++i)
            {
                if (!Exists(forwardedConnections[i].Replacement))
                    throw new ArgumentException($"Replacement node on forward request {i} doesn't exist");

                var next = AllocateForwardConnection();

                m_ForwardingTable[currentIndex].NextIndex = next;
                currentIndex = next;

                m_ForwardingTable[currentIndex] = forwardedConnections[i];
            }

            // resolve recursive list of forwarded connections (done in separate loop to simplify initial construction of list),
            // and rewrite forwarding table 1:1


            for (
                var currentCandidateHandle = node.ForwardedPortHead;
                currentCandidateHandle != ForwardPortHandle.Invalid;
                currentCandidateHandle = m_ForwardingTable[currentCandidateHandle].NextIndex
            )
            {
                ref var originForward = ref m_ForwardingTable[currentCandidateHandle];

                ref var nodeBeingForwadedTo = ref m_Nodes[originForward.Replacement.VHandle.Index];

                for (var fH = nodeBeingForwadedTo.ForwardedPortHead; fH != ForwardPortHandle.Invalid; fH = m_ForwardingTable[fH].NextIndex)
                {
                    ref var recursiveForward = ref m_ForwardingTable[fH];

                    if (originForward.SimplifyNestedForwardedPort(recursiveForward))
                        break;
                }

                // As all forwarding tables are simplified as much as possible in order of instantiation,
                // no recursion here is needed to keep simplifying the table (each reference to a child
                // table has already been simplified - thus, recursively solved up front).

                // This relies on the fact that nodes are instantiated in order.
            }

        }

        void CleanupForwardedConnections(ref InternalNodeData node)
        {
            var current = node.ForwardedPortHead;

            while (current != ForwardPortHandle.Invalid)
            {
                var next = m_ForwardingTable[current].NextIndex;
                ReleaseForwardConnection(current);
                current = next;
            }

            node.ForwardedPortHead = ForwardPortHandle.Invalid;
        }

        ForwardPortHandle AllocateForwardConnection()
        {
            if (m_FreeForwardingTables.Count > 0)
            {
                var index = m_FreeForwardingTables[m_FreeForwardingTables.Count - 1];
                m_FreeForwardingTables.PopBack();
                return index;
            }

            var value = new ForwardedPort();
            m_ForwardingTable.Add(value);

            return m_ForwardingTable.Count - 1;
        }

        void ReleaseForwardConnection(ForwardPortHandle handle)
        {
            m_FreeForwardingTables.Add(handle.Index);
        }

        // Testing.
        internal BlitList<int> GetFreeForwardingTables() => m_FreeForwardingTables;
        internal BlitList<ForwardedPort> GetForwardingTable() => m_ForwardingTable;
    }
}
