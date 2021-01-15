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
        public struct Unchecked
        {
            public readonly NodeHandle Replacement;
            internal readonly PortStorage ReplacedPortID;
            internal readonly PortStorage.EncodedDFGPort OriginPortID;
            public readonly bool IsInput;

            public static Unchecked Input(InputPortID originPortID, NodeHandle replacement, InputPortID replacedPortID)
            {
                return new Unchecked(true, originPortID.Port, replacement, replacedPortID.Storage);
            }

            public static Unchecked Output(OutputPortID originPortID, NodeHandle replacement, OutputPortID replacedPortID)
            {
                return new Unchecked(false, originPortID.Port, replacement, replacedPortID.Storage);
            }

            public ForwardedPort CheckAndConvert(NodeSetAPI set)
            {
                return new ForwardedPort(IsInput, OriginPortID, set.Nodes.Validate(Replacement.VHandle), ReplacedPortID);
            }

            Unchecked(bool isInput, PortStorage.EncodedDFGPort originPortId, NodeHandle replacement, PortStorage replacementPortId)
            {
                IsInput = isInput;
                Replacement = replacement;
                OriginPortID = originPortId;
                ReplacedPortID = replacementPortId;
            }
        }

        public ValidatedHandle Replacement;
        // Forward linked list
        public ForwardPortHandle NextIndex;
        public PortStorage ReplacedPortID;
        public PortStorage.EncodedDFGPort OriginPortID;

        public readonly bool IsInput;

        public bool SimplifyNestedForwardedPort(in ForwardedPort other)
        {
            if (IsInput == other.IsInput && !ReplacedPortID.IsECSPort && ReplacedPortID.DFGPort == other.OriginPortID)
            {
                Replacement = other.Replacement;
                ReplacedPortID = other.ReplacedPortID;
                return true;
            }

            return false;
        }

        public PortStorage.EncodedDFGPort GetOriginEncoded()
        {
            return OriginPortID;
        }

        public InputPortID GetOriginInputPortID()
        {
#if DFG_ASSERTIONS
            if (!IsInput)
                throw new AssertionException("Forwarded port does not represent an input");
#endif

            return new InputPortID(new PortStorage(OriginPortID));
        }

        public OutputPortID GetOriginOutputPortID()
        {
#if DFG_ASSERTIONS
            if (IsInput)
                throw new AssertionException("Forwarded port does not represent an output");
#endif

            return new OutputPortID (new PortStorage(OriginPortID));
        }

        public InputPortID GetReplacedInputPortID()
        {
#if DFG_ASSERTIONS
            if (!IsInput)
                throw new AssertionException("Forwarded port does not represent an input");
#endif

            return new InputPortID(ReplacedPortID);
        }

        public OutputPortID GetReplacedOutputPortID()
        {
#if DFG_ASSERTIONS
            if (IsInput)
                throw new AssertionException("Forwarded port does not represent an output");
#endif

            return new OutputPortID (ReplacedPortID);
        }

        ForwardedPort(bool isInput, PortStorage.EncodedDFGPort originPortId, ValidatedHandle replacement, PortStorage replacementPortId)
        {
            IsInput = isInput;
            Replacement = replacement;
            OriginPortID = originPortId;
            ReplacedPortID = replacementPortId;
            NextIndex = ForwardPortHandle.Invalid;
        }
    }

    public partial class NodeSetAPI
    {
        FreeList<ForwardedPort> m_ForwardingTable = new FreeList<ForwardedPort>(Allocator.Persistent);

        /// <summary>
        /// Input list is assumed to have at least one entry.
        /// </summary>
        void MergeForwardConnectionsToTable(ref InternalNodeData node, /* in */ BlitList<ForwardedPort.Unchecked> forwardedConnections)
        {
            var @checked = forwardedConnections[0].CheckAndConvert(this);
            var currentIndex = m_ForwardingTable.Allocate();
            node.ForwardedPortHead = currentIndex;
            m_ForwardingTable[currentIndex] = @checked;

            // Merge temporary forwarded connections into forwarding table
            for (int i = 1; i < forwardedConnections.Count; ++i)
            {
                @checked = forwardedConnections[i].CheckAndConvert(this);

                var next = m_ForwardingTable.Allocate();

                m_ForwardingTable[currentIndex].NextIndex = next;
                currentIndex = next;

                m_ForwardingTable[currentIndex] = @checked;
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

                ref var nodeBeingForwadedTo = ref Nodes[originForward.Replacement];

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
                m_ForwardingTable.Release(current);
                current = next;
            }

            node.ForwardedPortHead = ForwardPortHandle.Invalid;
        }

        // Testing.
        internal FreeList<ForwardedPort> GetForwardingTable() => m_ForwardingTable;
    }
}
