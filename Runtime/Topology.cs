using System;
using Unity.Collections;

namespace Unity.DataFlowGraph
{
    public partial class NodeSet : IDisposable
    {
        TopologyDatabase m_Topology = new TopologyDatabase(capacity: 16, allocator: Allocator.Persistent);

        internal TopologyCacheAPI.VersionTracker TopologyVersion => m_TopologyVersion;
        TopologyCacheAPI.VersionTracker m_TopologyVersion = TopologyCacheAPI.VersionTracker.Create();

        /// <summary>
        /// Create a persistent connection between an output port on the source node and an input port of matching type
        /// on the destination node.
        /// </summary>
        /// <remarks>
        /// Multiple connections to a single <see cref="DataInput{TDefinition,TType}"/> on a node are not permitted.
        /// </remarks>
        /// <exception cref="InvalidOperationException">Thrown if the request is invalid.</exception>
        /// <exception cref="ArgumentException">Thrown if the destination input port is already connected.</exception>
        public void Connect(NodeHandle sourceHandle, OutputPortID sourcePort, NodeHandle destHandle, InputPortID destinationPort)
        {
            Connect(sourceHandle, sourcePort, destHandle, new InputPortArrayID(destinationPort));
        }

        /// <summary>
        /// Overload of <see cref="Connect(Unity.DataFlowGraph.NodeHandle,Unity.DataFlowGraph.OutputPortID,Unity.DataFlowGraph.NodeHandle,Unity.DataFlowGraph.InputPortID)"/>
        /// targeting a destination port array with an index parameter.
        /// </summary>
        /// <exception cref="IndexOutOfRangeException">Thrown if the index is out of range with respect to the port array.</exception>
        public void Connect(NodeHandle sourceHandle, OutputPortID sourcePort, NodeHandle destHandle, InputPortID destinationPortArray, ushort index)
        {
            Connect(sourceHandle, sourcePort, destHandle, new InputPortArrayID(destinationPortArray, index));
        }

        /// <summary>
        /// Removes a previously made connection between an output port on the source node and an input port on the
        /// destination node.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if the request is invalid.</exception>
        /// <exception cref="ArgumentException">Thrown if the connection did not previously exist.</exception>
        public void Disconnect(NodeHandle sourceHandle, OutputPortID sourcePort, NodeHandle destHandle, InputPortID destinationPort)
        {
            Disconnect(sourceHandle, sourcePort, destHandle, new InputPortArrayID(destinationPort));
        }

        /// <summary>
        /// Overload of <see cref="Disconnect(Unity.DataFlowGraph.NodeHandle,Unity.DataFlowGraph.OutputPortID,Unity.DataFlowGraph.NodeHandle,Unity.DataFlowGraph.InputPortID)"/>
        /// targeting a destination port array with an index parameter.
        /// </summary>
        /// <exception cref="IndexOutOfRangeException">Thrown if the index is out of range with respect to the port array.</exception>
        public void Disconnect(NodeHandle sourceHandle, OutputPortID sourcePort, NodeHandle destHandle, InputPortID destinationPortArray, ushort index)
        {
            Disconnect(sourceHandle, sourcePort, destHandle, new InputPortArrayID(destinationPortArray, index));
        }

        /// <summary>
        /// Removes a previously made connection between an output data port on the source node and an input data port on
        /// the destination node (see <see cref="Disconnect(Unity.DataFlowGraph.NodeHandle,Unity.DataFlowGraph.OutputPortID,Unity.DataFlowGraph.NodeHandle,Unity.DataFlowGraph.InputPortID)"/>)
        /// but preserves the last data contents that was transmitted along the connection at the destination node's data
        /// input port. The data persists until a new connection is made to that data input port.
        /// <seealso cref="SetData{TType}(NodeHandle, InputPortID, in TType)"/>
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if the request is invalid.</exception>
        /// <exception cref="ArgumentException">Thrown if the connection did not previously exist.</exception>
        public void DisconnectAndRetainValue(NodeHandle sourceHandle, OutputPortID sourcePort, NodeHandle destHandle, InputPortID destinationPort)
        {
            DisconnectAndRetainValue(sourceHandle, sourcePort, destHandle, new InputPortArrayID(destinationPort));
        }

        /// <summary>
        /// Overload of <see cref="DisconnectAndRetainValue(Unity.DataFlowGraph.NodeHandle,Unity.DataFlowGraph.OutputPortID,Unity.DataFlowGraph.NodeHandle,Unity.DataFlowGraph.InputPortID)"/>
        /// targeting a port array with an index parameter.
        /// </summary>
        /// <exception cref="IndexOutOfRangeException">Thrown if the index is out of range with respect to the port array.</exception>
        public void DisconnectAndRetainValue(NodeHandle sourceHandle, OutputPortID sourcePort, NodeHandle destHandle, InputPortID destinationPortArray, ushort index)
        {
            DisconnectAndRetainValue(sourceHandle, sourcePort, destHandle, new InputPortArrayID(destinationPortArray, index));
        }

        internal void DisconnectAll(NodeHandle handle)
        {
            NodeVersionCheck(handle.VHandle);
            UncheckedDisconnectAll(ref m_Nodes[handle.VHandle.Index]);
        }

        void Connect(NodeHandle sourceHandle, OutputPortID sourcePort, NodeHandle destHandle, InputPortArrayID destinationPort)
        {
            NodeVersionCheck(sourceHandle.VHandle);
            NodeVersionCheck(destHandle.VHandle);

            var sourcePortDef = GetFunctionality(sourceHandle).GetPortDescription(sourceHandle).Outputs[sourcePort.Port];
            var destPortDef = GetFunctionality(destHandle).GetPortDescription(destHandle).Inputs[destinationPort.PortID.Port];

            if (destPortDef.IsPortArray != destinationPort.IsArray)
                throw new InvalidOperationException(destPortDef.IsPortArray
                    ? "An array index is required when connecting to an array port."
                    : "An array index can only be given when connecting to an array port.");

            // TODO: Handle Msg -> data
            if (sourcePortDef.PortUsage != destPortDef.PortUsage)
                throw new InvalidOperationException($"Port usage between source ({sourcePortDef.PortUsage}) and destination ({destPortDef.PortUsage}) are not compatible");

            // TODO: Adapters?
            if (sourcePortDef.Type != destPortDef.Type)
                throw new InvalidOperationException($"Cannot connect source type ({sourcePortDef.Type}) to destination type ({destPortDef.Type})");

            UncheckedTypedConnect((FlagsFromUsage(sourcePortDef.PortUsage), sourcePortDef.Type), sourceHandle, sourcePort, destHandle, destinationPort);
        }

        void Disconnect(NodeHandle sourceHandle, OutputPortID sourcePort, NodeHandle destHandle, InputPortArrayID destinationPort)
        {
            NodeVersionCheck(sourceHandle.VHandle);
            NodeVersionCheck(destHandle.VHandle);

            var destPortDef = GetFunctionality(destHandle).GetPortDescription(destHandle).Inputs[destinationPort.PortID.Port];

            if (destPortDef.IsPortArray != destinationPort.IsArray)
                throw new InvalidOperationException(destPortDef.IsPortArray
                    ? "An array index is required when disconnecting from an array port."
                    : "An array index can only be given when disconnecting from an array port.");

            UncheckedDisconnect(sourceHandle, sourcePort, destHandle, destinationPort);
        }

        void DisconnectAndRetainValue(NodeHandle sourceHandle, OutputPortID sourcePort, NodeHandle destHandle, InputPortArrayID destinationPort)
        {
            var portDef = GetFunctionality(destHandle).GetPortDescription(destHandle).Inputs[destinationPort.PortID.Port];

            if (portDef.HasBuffers)
                throw new InvalidOperationException($"Cannot retain data on a data port which includes buffers");

            if (portDef.PortUsage != Usage.Data)
                throw new InvalidOperationException($"Cannot retain data on a non-data port");

            Disconnect(sourceHandle, sourcePort, destHandle, destinationPort);

            // TODO: Double resolve - fix in follow up PR
            ResolvePublicDestination(ref destHandle, ref destinationPort);
            m_Diff.RetainData(destHandle, destinationPort);
        }

        void UncheckedDisconnect(NodeHandle sourceHandle, OutputPortID sourcePort, NodeHandle destHandle, InputPortArrayID destinationPort)
        {
            ResolvePublicSource(ref sourceHandle, ref sourcePort);
            ResolvePublicDestination(ref destHandle, ref destinationPort);

            ref var connection = ref m_Topology.FindConnection(sourceHandle, sourcePort, destHandle, destinationPort);

            if (!connection.Valid)
            {
                if (destinationPort.IsArray && destinationPort.ArrayIndex >= GetPortArraySize_Unchecked(destHandle, destinationPort.PortID))
                    throw new IndexOutOfRangeException("PortArray index out of bounds.");

                throw new ArgumentException("Connection doesn't exist!");
            }

            DisconnectConnection(ref connection, ref m_Nodes[sourceHandle.VHandle.Index]);
            SignalTopologyChanged();
        }

        internal void UncheckedTypedConnect((TraversalFlags Class, Type Type) semantics, NodeHandle sourceHandle, OutputPortID sourcePort, NodeHandle destHandle, InputPortArrayID destinationPort)
        {
            ResolvePublicSource(ref sourceHandle, ref sourcePort);
            ResolvePublicDestination(ref destHandle, ref destinationPort);

            if (destinationPort.IsArray && destinationPort.ArrayIndex >= GetPortArraySize_Unchecked(destHandle, destinationPort.PortID))
                throw new IndexOutOfRangeException("PortArray index out of bounds.");

            // TODO: Check recursion at some point - as well.
            if (m_Topology.ConnectionExists(sourceHandle, sourcePort, destHandle, destinationPort))
                throw new ArgumentException("Connection already exists!");

            if (semantics.Class == TraversalFlags.DSL)
            {
                var handler = GetDSLHandler(semantics.Type);
                handler.Connect(this, sourceHandle, sourcePort, destHandle, destinationPort.PortID);
            }

            m_Topology.Connect(semantics.Class, sourceHandle, sourcePort, destHandle, destinationPort);
            // everything good
            SignalTopologyChanged();
        }

        void DisconnectConnection(ref Connection connection, ref InternalNodeData source)
        {
            if (connection.ConnectionType == TraversalFlags.DSL)
            {
                var leftPort = m_NodeFunctionalities[source.TraitsIndex].GetPortDescription(connection.SourceHandle).Outputs[connection.SourceOutputPort.Port];
                var handler = m_ConnectionHandlerMap[leftPort.Type];

                handler.Disconnect(this, connection.SourceHandle, connection.SourceOutputPort, connection.DestinationHandle, connection.DestinationInputPort.PortID);
            }

            m_Topology.DisconnectAndRelease(ref connection);
        }

        void UncheckedDisconnectAll(ref InternalNodeData node)
        {
            // TODO: This is code duplication from TopologyDatabase, reason is
            // calling DisconnectAll() on topology database disregards DSL callbacks.
            // So we iterate manually here.

            ref var index = ref m_Topology.Indexes[node.VHandle.Index];

            bool topologyChanged = false;

            var it = index.InputHeadConnection;
            while (true)
            {
                ref var connection = ref m_Topology.Connections[it];

                if (!connection.Valid)
                    break;

                it = connection.NextInputConnection;

                DisconnectConnection(ref connection, ref m_Nodes[connection.SourceHandle.VHandle.Index]);
                topologyChanged = true;
            }

            it = index.OutputHeadConnection;
            while (true)
            {
                ref var connection = ref m_Topology.Connections[it];

                if (!connection.Valid)
                    break;

                it = connection.NextOutputConnection;

                DisconnectConnection(ref connection, ref m_Nodes[connection.SourceHandle.VHandle.Index]);
                topologyChanged = true;
            }

            if (topologyChanged)
                SignalTopologyChanged();
        }

        /// <remarks>
        /// Assumes validated input handles. Output handles are validated.
        /// </remarks>
        /// <returns>True if arguments changed</returns>
        internal bool ResolvePublicDestination(ref NodeHandle destHandle, ref InputPortArrayID destinationPort)
        {
            for (var fH = m_Nodes[destHandle.VHandle.Index].ForwardedPortHead; fH != ForwardPortHandle.Invalid; fH = m_ForwardingTable[fH].NextIndex)
            {
                ref var forwarding = ref m_ForwardingTable[fH];

                if (!forwarding.IsInput)
                    continue;

                InputPortID port = forwarding.GetOriginInputPortID();

                // Forwarded port list are monotonically increasing by port, so we can break out early
                if (port.Port > destinationPort.PortID.Port)
                    break;

                if (port != destinationPort.PortID)
                    continue;

                if (!Exists(forwarding.Replacement))
                    throw new InvalidOperationException("Replacement node for previously registered forward doesn't exist anymore");

                destHandle = forwarding.Replacement;
                destinationPort = destinationPort.IsArray
                    ? new InputPortArrayID(forwarding.GetReplacedInputPortID(), destinationPort.ArrayIndex)
                    : new InputPortArrayID(forwarding.GetReplacedInputPortID());

                return true;
            }

            return false;
        }

        /// <remarks>
        /// Assumes validated input handles. Output handles are validated.
        /// </remarks>
        /// <returns>True if arguments changed</returns>
        bool ResolvePublicSource(ref NodeHandle sourceHandle, ref OutputPortID sourcePort)
        {
            for (var fH = m_Nodes[sourceHandle.VHandle.Index].ForwardedPortHead; fH != ForwardPortHandle.Invalid; fH = m_ForwardingTable[fH].NextIndex)
            {
                ref var forwarding = ref m_ForwardingTable[fH];

                if (forwarding.IsInput)
                    continue;

                OutputPortID port = forwarding.GetOriginOutputPortID();

                // Forwarded port list are monotonically increasing by port, so we can break out early
                if (port.Port > sourcePort.Port)
                    break;

                if (port != sourcePort)
                    continue;

                if (!Exists(forwarding.Replacement))
                    throw new InvalidOperationException("Replacement node for previously registered forward doesn't exist anymore");

                sourceHandle = forwarding.Replacement;
                sourcePort = forwarding.GetReplacedOutputPortID();

                return true;
            }

            return false;
        }

        void SignalTopologyChanged()
        {
            m_TopologyVersion.SignalTopologyChanged();
        }

        static TraversalFlags FlagsFromUsage(Usage use)
        {
            switch (use)
            {
                case Usage.Message:
                    return TraversalFlags.Message;
                case Usage.Data:
                    return TraversalFlags.DataFlow;
                case Usage.DomainSpecific:
                    return TraversalFlags.DSL;
            }

            throw new ArgumentOutOfRangeException(nameof(use));
        }

        // TODO: Fix these to return .ReadOnly when blitlist supports generic enumeration on readonlys
        internal BlitList<InternalNodeData> GetInternalData() => m_Nodes;

        internal BlitList<TopologyIndex> GetInternalTopologyIndices() => m_Topology.Indexes;
        internal BlitList<Connection> GetInternalEdges() => m_Topology.Connections;
        internal TopologyDatabase GetTopologyDatabase() => m_Topology;

        internal BlitList<int> GetFreeEdges() => m_Topology.m_FreeConnections;
    }
}
