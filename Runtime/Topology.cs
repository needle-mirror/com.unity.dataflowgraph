using System;
using Unity.Collections;

namespace Unity.DataFlowGraph
{
    using Topology = TopologyAPI<ValidatedHandle, InputPortArrayID, OutputPortArrayID>;

    struct FlatTopologyMap : Topology.Database.ITopologyFromVertex, IDisposable
    {
        public const int InvalidConnection = Topology.Database.InvalidConnection;
        public TopologyIndex this[ValidatedHandle vertex] { get => m_Indexes[vertex.Versioned.Index]; set => m_Indexes[vertex.Versioned.Index] = value; }
        public ref TopologyIndex GetRef(ValidatedHandle vertex) => ref m_Indexes[vertex.Versioned.Index];
        BlitList<TopologyIndex> m_Indexes;

        public bool IsCreated => m_Indexes.IsCreated;

        public void EnsureSize(int size) => m_Indexes.EnsureSize(size);

        public void Dispose() => m_Indexes.Dispose();

        public FlatTopologyMap(int capacity, Allocator allocator)
        {
            m_Indexes = new BlitList<TopologyIndex>(0, allocator);
            m_Indexes.Reserve(capacity);
        }

        public FlatTopologyMap Clone()
        {
            return new FlatTopologyMap { m_Indexes = m_Indexes.Copy() };
        }
    }

    public partial class NodeSetAPI
    {
        internal const int InvalidConnection = Topology.Database.InvalidConnection;

        Topology.Database m_Database = new Topology.Database(capacity: 16, allocator: Allocator.Persistent);
        FlatTopologyMap m_Topology = new FlatTopologyMap(capacity: 16, allocator: Allocator.Persistent);

        internal Topology.CacheAPI.VersionTracker TopologyVersion => m_TopologyVersion;
        Topology.CacheAPI.VersionTracker m_TopologyVersion = Topology.CacheAPI.VersionTracker.Create();

        /// <summary>
        /// The kind of connection.
        /// </summary>
        public enum ConnectionType
        {
            /// <summary>
            /// Standard connectivity.
            /// </summary>
            Normal,
            /// <summary>
            /// Connection type which allows feeding information back to an upstream node without forming a cycle in
            /// the graph. Cycle avoidance is achieved by considering this connection to introduce an update delay.
            /// </summary>
            Feedback
        }

        /// <summary>
        /// Create a persistent connection between an output port on the source node and an input port of matching type
        /// on the destination node.
        /// </summary>
        /// <remarks>
        /// Multiple connections to a single <see cref="DataInput{TDefinition,TType}"/> on a node are not permitted.
        /// <see cref="ConnectionType.Feedback"/> is only allowed for data ports.
        /// </remarks>
        /// <exception cref="InvalidOperationException">Thrown if the request is invalid.</exception>
        /// <exception cref="ArgumentException">Thrown if the destination input port is already connected.</exception>
        public void Connect(NodeHandle sourceHandle, OutputPortID sourcePort, NodeHandle destHandle, InputPortID destinationPort, ConnectionType dataConnectionType = ConnectionType.Normal)
        {
            Connect(sourceHandle, new OutputPortArrayID(sourcePort), destHandle, new InputPortArrayID(destinationPort), dataConnectionType);
        }

        /// <summary>
        /// Overload of <see cref="Connect(NodeHandle,OutputPortID,NodeHandle,InputPortID,ConnectionType)"/>
        /// targeting a destination port array with an index parameter.
        /// </summary>
        /// <exception cref="IndexOutOfRangeException">Thrown if the index is out of range with respect to the port array.</exception>
        public void Connect(NodeHandle sourceHandle, OutputPortID sourcePort, NodeHandle destHandle, InputPortID destPortArray, int destArrayIndex, ConnectionType dataConnectionType = ConnectionType.Normal)
        {
            Connect(sourceHandle, new OutputPortArrayID(sourcePort), destHandle, new InputPortArrayID(destPortArray, destArrayIndex), dataConnectionType);
        }

        /// <summary>
        /// Overload of <see cref="Connect(NodeHandle,OutputPortID,NodeHandle,InputPortID,ConnectionType)"/>
        /// with a source port array with an index parameter.
        /// </summary>
        /// <exception cref="IndexOutOfRangeException">Thrown if the index is out of range with respect to the port array.</exception>
        public void Connect(NodeHandle sourceHandle, OutputPortID sourcePortArray, int sourceArrayIndex, NodeHandle destHandle, InputPortID destinationPort, ConnectionType dataConnectionType = ConnectionType.Normal)
        {
            Connect(sourceHandle, new OutputPortArrayID(sourcePortArray, sourceArrayIndex), destHandle, new InputPortArrayID(destinationPort), dataConnectionType);
        }

        /// <summary>
        /// Overload of <see cref="Connect(NodeHandle,OutputPortID,NodeHandle,InputPortID,ConnectionType)"/>
        /// with a source port array with an index parameter and targeting a destination port array with an index parameter.
        /// </summary>
        /// <exception cref="IndexOutOfRangeException">Thrown if the index is out of range with respect to a port array.</exception>
        public void Connect(NodeHandle sourceHandle, OutputPortID sourcePortArray, int sourceArrayIndex, NodeHandle destHandle, InputPortID destPortArray, int destArrayIndex, ConnectionType dataConnectionType = ConnectionType.Normal)
        {
            Connect(sourceHandle, new OutputPortArrayID(sourcePortArray, sourceArrayIndex), destHandle, new InputPortArrayID(destPortArray, destArrayIndex), dataConnectionType);
        }

        /// <summary>
        /// Removes a previously made connection between an output port on the source node and an input port on the
        /// destination node.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if the request is invalid.</exception>
        /// <exception cref="ArgumentException">Thrown if the connection did not previously exist.</exception>
        public void Disconnect(NodeHandle sourceHandle, OutputPortID sourcePort, NodeHandle destHandle, InputPortID destinationPort)
        {
            Disconnect(sourceHandle, new OutputPortArrayID(sourcePort), destHandle, new InputPortArrayID(destinationPort));
        }

        /// <summary>
        /// Overload of <see cref="Disconnect(NodeHandle,OutputPortID,NodeHandle,InputPortID)"/>
        /// targeting a destination port array with an index parameter.
        /// </summary>
        /// <exception cref="IndexOutOfRangeException">Thrown if the index is out of range with respect to the port array.</exception>
        public void Disconnect(NodeHandle sourceHandle, OutputPortID sourcePort, NodeHandle destHandle, InputPortID destPortArray, int destArrayIndex)
        {
            Disconnect(sourceHandle, new OutputPortArrayID(sourcePort), destHandle, new InputPortArrayID(destPortArray, destArrayIndex));
        }

        /// <summary>
        /// Overload of <see cref="Disconnect(NodeHandle,OutputPortID,NodeHandle,InputPortID)"/>
        /// with a source port array with an index parameter.
        /// </summary>
        /// <exception cref="IndexOutOfRangeException">Thrown if the index is out of range with respect to the port array.</exception>
        public void Disconnect(NodeHandle sourceHandle, OutputPortID sourcePortArray, int sourceArrayIndex, NodeHandle destHandle, InputPortID destPort)
        {
            Disconnect(sourceHandle, new OutputPortArrayID(sourcePortArray, sourceArrayIndex), destHandle, new InputPortArrayID(destPort));
        }

        /// <summary>
        /// Overload of <see cref="Disconnect(NodeHandle,OutputPortID,NodeHandle,InputPortID)"/>
        /// with a source port array with an index parameter and targeting a destination port array with an index parameter.
        /// </summary>
        /// <exception cref="IndexOutOfRangeException">Thrown if the index is out of range with respect to a port array.</exception>
        public void Disconnect(NodeHandle sourceHandle, OutputPortID sourcePortArray, int sourceArrayIndex, NodeHandle destHandle, InputPortID destPortArray, int destArrayIndex)
        {
            Disconnect(sourceHandle, new OutputPortArrayID(sourcePortArray, sourceArrayIndex), destHandle, new InputPortArrayID(destPortArray, destArrayIndex));
        }

        /// <summary>
        /// Removes a previously made connection between an output data port on the source node and an input data port on
        /// the destination node (see <see cref="Disconnect(NodeHandle,OutputPortID,NodeHandle,InputPortID)"/>)
        /// but preserves the last data contents that was transmitted along the connection at the destination node's data
        /// input port. The data persists until a new connection is made to that data input port.
        /// <seealso cref="SetData{TType}(NodeHandle, InputPortID, in TType)"/>
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if the request is invalid.</exception>
        /// <exception cref="ArgumentException">Thrown if the connection did not previously exist.</exception>
        public void DisconnectAndRetainValue(NodeHandle sourceHandle, OutputPortID sourcePort, NodeHandle destHandle, InputPortID destinationPort)
        {
            DisconnectAndRetainValue(sourceHandle, new OutputPortArrayID(sourcePort), destHandle, new InputPortArrayID(destinationPort));
        }

        /// <summary>
        /// Overload of <see cref="DisconnectAndRetainValue(NodeHandle,OutputPortID,NodeHandle,InputPortID)"/>
        /// targeting a port array with an index parameter.
        /// </summary>
        /// <exception cref="IndexOutOfRangeException">Thrown if the index is out of range with respect to the port array.</exception>
        public void DisconnectAndRetainValue(NodeHandle sourceHandle, OutputPortID sourcePort, NodeHandle destHandle, InputPortID destPortArray, int index)
        {
            DisconnectAndRetainValue(sourceHandle, new OutputPortArrayID(sourcePort), destHandle, new InputPortArrayID(destPortArray, index));
        }

        internal void DisconnectAll(NodeHandle handle) => UncheckedDisconnectAll(ref Nodes[handle.VHandle]);

        void Connect(NodeHandle sourceHandle, OutputPortArrayID sourcePort, NodeHandle destHandle, InputPortArrayID destinationPort, ConnectionType dataConnectionType)
        {
            var source = new OutputPair(this, sourceHandle, sourcePort);
            var dest = new InputPair(this, destHandle, destinationPort);

            // Connectivity correctness does not imply port existence (e.g. ComponentNodes)
            var sourcePortDef = GetVirtualPort(source);
            var destPortDef = GetVirtualPort(dest);

            if (sourcePortDef.IsPortArray != sourcePort.IsArray)
                throw new InvalidOperationException(sourcePortDef.IsPortArray
                    ? "An array index is required when connecting from an array port."
                    : "An array index can only be given when connecting from an array port.");

            if (destPortDef.IsPortArray != destinationPort.IsArray)
                throw new InvalidOperationException(destPortDef.IsPortArray
                    ? "An array index is required when connecting to an array port."
                    : "An array index can only be given when connecting to an array port.");

            uint connectionCategory = (uint)sourcePortDef.Category;
            if (sourcePortDef.Category != destPortDef.Category)
            {
                if (sourcePortDef.Category != PortDescription.Category.Message || destPortDef.Category != PortDescription.Category.Data)
                    throw new InvalidOperationException($"Port category between source ({sourcePortDef.Category}) and destination ({destPortDef.Category}) are not compatible");
                connectionCategory = PortDescription.MessageToDataConnectionCategory;
            }

            if (dataConnectionType != ConnectionType.Normal && sourcePortDef.Category != PortDescription.Category.Data)
                throw new InvalidOperationException($"Cannot create a feedback connection for non-Data Port");

            // TODO: Adapters?
            if (sourcePortDef.Type != destPortDef.Type)
                throw new InvalidOperationException($"Cannot connect source type ({sourcePortDef.Type}) to destination type ({destPortDef.Type})");

            Connect((connectionCategory, sourcePortDef.TypeHash, dataConnectionType), source, dest);
        }

        void Disconnect(NodeHandle sourceHandle, OutputPortArrayID sourcePort, NodeHandle destHandle, InputPortArrayID destinationPort)
        {
            var source = new OutputPair(this, sourceHandle, sourcePort);
            var dest = new InputPair(this, destHandle, destinationPort);

            // Connectivity correctness does not imply port existence (e.g. ComponentNodes)
            var sourcePortDef = GetVirtualPort(source);
            var destPortDef = GetVirtualPort(dest);

            if (sourcePortDef.IsPortArray != sourcePort.IsArray)
                throw new InvalidOperationException(sourcePortDef.IsPortArray
                    ? "An array index is required when connecting from an array port."
                    : "An array index can only be given when connecting from an array port.");

            if (destPortDef.IsPortArray != destinationPort.IsArray)
                throw new InvalidOperationException(destPortDef.IsPortArray
                    ? "An array index is required when disconnecting from an array port."
                    : "An array index can only be given when disconnecting from an array port.");

            Disconnect(source, dest);
        }

        void DisconnectAndRetainValue(NodeHandle sourceHandle, OutputPortArrayID sourcePort, NodeHandle destHandle, InputPortArrayID destinationPort)
        {
            var source = new OutputPair(this, sourceHandle, sourcePort);
            var dest = new InputPair(this, destHandle, destinationPort);

            var portDef = GetFormalPort(dest);

            if (portDef.HasBuffers)
                throw new InvalidOperationException($"Cannot retain data on a data port which includes buffers");

            if (portDef.Category != PortDescription.Category.Data)
                throw new InvalidOperationException($"Cannot retain data on a non-data port");

            Disconnect(source, dest);
            m_Diff.RetainData(dest);
        }

        void Disconnect(in OutputPair source, in InputPair dest)
        {
            ref readonly var connection = ref m_Database.FindConnection(ref m_Topology, source.Handle, source.Port, dest.Handle, dest.Port);

            if (!connection.Valid)
            {
                CheckPortArrayBounds(source);
                CheckPortArrayBounds(dest);

                throw new ArgumentException("Connection doesn't exist!");
            }

            DisconnectConnection(connection, ref Nodes[source.Handle]);


            if (connection.TraversalFlags == (uint)PortDescription.Category.Data << (int)PortDescription.CategoryShift.FeedbackConnection)
            {
                // Disconnect the backwards dependency.
                ref readonly var backConnection =
                    ref m_Database.FindConnection(ref m_Topology, dest.Handle, default, source.Handle, default);

                m_Diff.DisconnectData(backConnection);


                m_Database.DisconnectAndRelease(ref m_Topology, backConnection);
            }

            SignalTopologyChanged();
        }

        internal void Connect((uint Category, TypeHash Type, ConnectionType ConnType) semantics, in OutputPair source, in InputPair dest)
        {
            // TODO: Not ideal, but we cannot detect entity -> entity connections ahead of time,
            // so we need to dynamically test and keep track of dependencies.
            if(HostSystem != null)
            {
                if (source.Port.PortID.Storage.IsECSPort && dest.Port.PortID.Storage.IsECSPort)
                    AddWriter(source.Port.PortID.ECSType);

#if DFG_ASSERTIONS
                if (source.Port.PortID.Storage.IsECSPort && !HasReaderOrWriter(source.Port.PortID.ECSType))
                    throw new AssertionException($"Unregistered {source.Port.PortID.Storage} for source");

                if (dest.Port.PortID.Storage.IsECSPort && !HasReaderOrWriter(dest.Port.PortID.ECSType))
                    throw new AssertionException($"Unregistered {dest.Port.PortID.Storage} for dest");
#endif
            }

            CheckPortArrayBounds(source);
            CheckPortArrayBounds(dest);

            if ((semantics.Category & ((uint)PortDescription.Category.Data | PortDescription.MessageToDataConnectionCategory)) != 0)
            {
                // Ensure we don't end up with multiple connections on the same Data input. The only exception is if we
                // have only Message->Data connections all to the same input.
                for (var it = m_Topology[dest.Handle].InputHeadConnection; it != InvalidConnection; it = m_Database[it].NextInputConnection)
                {
                    ref readonly var conn = ref m_Database[it];
                    if (conn.DestinationInputPort != dest.Port)
                        continue;
                    if (semantics.Category == PortDescription.MessageToDataConnectionCategory && conn.TraversalFlags == PortDescription.MessageToDataConnectionCategory)
                        continue;
                    throw new ArgumentException("Cannot connect to an already connected Data input port");
                }
            }

            // TODO: Check recursion at some point - as well.
            if (m_Database.ConnectionExists(ref m_Topology, source.Handle, source.Port, dest.Handle, dest.Port))
                throw new ArgumentException("Connection already exists!");

            if (semantics.Category == (uint)PortDescription.Category.DomainSpecific)
            {
                var handler = GetDSLHandler(semantics.Type);
                handler.Connect(this, source.Handle.ToPublicHandle(), source.Port.PortID, dest.Handle.ToPublicHandle(), dest.Port.PortID);
            }

            if (semantics.ConnType != ConnectionType.Feedback)
            {
                ref readonly var newC = ref m_Database.Connect(ref m_Topology, semantics.Category, source.Handle, source.Port, dest.Handle, dest.Port);

                if (semantics.Category == (uint)PortDescription.Category.Data)
                    m_Diff.ConnectData(newC);
            }
            else
            {
                ref readonly var newC = ref m_Database.Connect(ref m_Topology, semantics.Category << (int)PortDescription.CategoryShift.FeedbackConnection, source.Handle, source.Port, dest.Handle, dest.Port);

                // Feedback connections are always data connections.
                m_Diff.ConnectData(newC);

                // Create the backwards dependency so that traversal order is correct.
                newC = ref m_Database.Connect(
                    ref m_Topology, semantics.Category << (int)PortDescription.CategoryShift.BackConnection,
                    dest.Handle, default,
                    source.Handle, default);

                m_Diff.ConnectData(newC);
            }

            // everything good
            SignalTopologyChanged();
        }

        void DisconnectConnection(in Topology.Connection connection, ref InternalNodeData source)
        {
            if (connection.TraversalFlags == (uint)PortDescription.Category.DomainSpecific)
            {
                var leftPort = GetFormalPort(new OutputPair(connection));
                var handler = m_ConnectionHandlerMap[leftPort.TypeHash];

                handler.Disconnect(this, connection.Source.ToPublicHandle(), connection.SourceOutputPort.PortID, connection.Destination.ToPublicHandle(), connection.DestinationInputPort.PortID);
            }
            else if((connection.TraversalFlags & PortDescription.k_MaskForAnyData) != 0)
            {
                m_Diff.DisconnectData(connection);
            }

            m_Database.DisconnectAndRelease(ref m_Topology, connection);
        }

        void UncheckedDisconnectAll(ref InternalNodeData node)
        {
            var index = m_Topology[node.Handle];

            bool topologyChanged = false;

            for(var it = index.InputHeadConnection; it != InvalidConnection; it = m_Database[it].NextInputConnection)
            {
                ref readonly var connection = ref m_Database[it];
                DisconnectConnection(connection, ref Nodes[connection.Source]);
                topologyChanged = true;
            }

            for (var it = index.OutputHeadConnection; it != InvalidConnection; it = m_Database[it].NextOutputConnection)
            {
                ref readonly var connection = ref m_Database[it];
                DisconnectConnection(connection, ref Nodes[connection.Source]);
                topologyChanged = true;
            }

            if (topologyChanged)
                SignalTopologyChanged();
        }

        void SignalTopologyChanged()
        {
            m_TopologyVersion.SignalTopologyChanged();
        }

        /// <remarks>
        /// Does not enumerate forwarded topology.
        /// </remarks>
        internal Topology.Database.InputTopologyEnumerable GetInputs(ValidatedHandle handle)
        {
            return m_Database.GetInputs(m_Topology[handle]);
        }

        /// <remarks>
        /// Does not enumerate forwarded topology.
        /// </remarks>
        internal Topology.Database.OutputTopologyEnumerable GetOutputs(ValidatedHandle handle)
        {
            return m_Database.GetOutputs(m_Topology[handle]);
        }

        // TODO: Fix these to return .ReadOnly when blitlist supports generic enumeration on readonlys
        internal VersionedList<InternalNodeData> GetInternalData() => Nodes;
        internal Topology.Database GetTopologyDatabase_ForTesting() => m_Database;
        internal FlatTopologyMap GetTopologyMap_ForTesting() => m_Topology;
    }
}
