using System;
using Unity.DataFlowGraph.Detail;

namespace Unity.DataFlowGraph
{
    public partial class NodeSetAPI
    {
        /// <summary>
        /// Connects two compatible endpoints together, that could be of category <see cref="PortDescription.Category.Data"/>.
        /// See <see cref="ConnectionType"/> for more info on extra connection properties in this case.
        /// See <see cref="IEndpoint"/> for more information on creating endpoints.
        /// <seealso cref="Connect(NodeHandle, OutputPortID, NodeHandle, InputPortID, ConnectionType)"/>
        /// </summary>
        public void Connect<TLeft, TRight>(in TLeft lhs, in TRight rhs, ConnectionType connectionType = ConnectionType.Normal)
            where TLeft : struct, IConcrete, IOutputEndpoint, IConnectableWith<TRight>, IPotentialDataEndpoint
            where TRight : struct, IConcrete, IInputEndpoint, IConnectableWith<TLeft>, IPotentialDataEndpoint
                => ConnectInternal(lhs, rhs, connectionType);

        /// <summary>
        /// Connects two compatible endpoints together.
        /// See <see cref="IEndpoint"/> for more information on creating endpoints.
        /// <seealso cref="Connect(NodeHandle, OutputPortID, NodeHandle, InputPortID, ConnectionType)"/>
        /// </summary>
        public void Connect<TLeft, TRight>(in TLeft lhs, in TRight rhs)
            where TLeft : struct, IConcrete, IOutputEndpoint, IConnectableWith<TRight>
            where TRight : struct, IConcrete, IInputEndpoint, IConnectableWith<TLeft>
                => ConnectInternal(lhs, rhs, ConnectionType.Normal);

        void ConnectInternal<TLeft, TRight>(in TLeft lhs, in TRight rhs, ConnectionType connectionType)
            where TLeft : struct, IConcrete, IOutputEndpoint, IConnectableWith<TRight>
            where TRight : struct, IConcrete, IInputEndpoint, IConnectableWith<TLeft>
        {
            if (!lhs.IsWeak)
            {
                uint category = (uint)lhs.Category;

                if (lhs.Category == PortDescription.Category.Message && rhs.Category == PortDescription.Category.Data)
                    category = PortDescription.MessageToDataConnectionCategory;

                Connect((category, lhs.Type, connectionType), new OutputPair(this, lhs.Endpoint), new InputPair(this, rhs.Endpoint));
            }
            else
            {
                Connect(lhs.Endpoint.Handle, lhs.Endpoint.PortID, rhs.Endpoint.Handle, rhs.Endpoint.PortID, connectionType);
            }
        }

        /// <summary>
        /// Disconnects two previously joined endpoints.
        /// See <see cref="IEndpoint"/> for more information on creating endpoints.
        /// See <see cref="Disconnect(NodeHandle, OutputPortID, NodeHandle, InputPortID)"/>
        /// </summary>
        public void Disconnect<TLeft, TRight>(in TLeft lhs, in TRight rhs)
            where TLeft : struct, IConcrete, IOutputEndpoint, IConnectableWith<TRight>
            where TRight : struct, IConcrete, IInputEndpoint, IConnectableWith<TLeft>
        {
            if (!lhs.IsWeak)
                Disconnect(new OutputPair(this, lhs.Endpoint), new InputPair(this, rhs.Endpoint));
            else
                Disconnect(lhs.Endpoint.Handle, lhs.Endpoint.PortID, rhs.Endpoint.Handle, rhs.Endpoint.PortID);
        }

        /// <summary>
        /// Removes a previously made connection between two endpoints (see <see cref="Disconnect"/>),
        /// but preserves the last data contents that was transmitted along the connection at the destination node's data
        /// input port. The data persists until a new connection is made to that data input port.
        /// See <see cref="IEndpoint"/> for more information on creating endpoints.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if the request is invalid.</exception>
        /// <exception cref="ArgumentException">Thrown if the connection did not previously exist.</exception>
        public void DisconnectAndRetainValue<TLeft, TRight>(in TLeft lhs, in TRight rhs)
            where TLeft : struct, IConcrete, IOutputEndpoint, IConnectableWith<TRight>
            where TRight : struct, IConcrete, IInputEndpoint, IConnectableWith<TLeft>
        {
            DisconnectAndRetainValue(lhs.Endpoint.Handle, lhs.Endpoint.PortID, rhs.Endpoint.Handle, rhs.Endpoint.PortID);
        }
    }
}
