﻿using System;

namespace Unity.DataFlowGraph
{
    using Topology = TopologyAPI<ValidatedHandle, InputPortArrayID, OutputPortID>;

    readonly struct InputPair
    {
        public readonly ValidatedHandle Handle;
        public readonly InputPortArrayID Port;

        /// <remarks>
        /// Does not do node validation or port forwarding resolution as an existing connection should have had both done
        /// during the <see cref="NodeSet.Connect"/>.
        /// </remarks>
        public InputPair(Topology.Connection connection)
        {
            Handle = connection.Destination;
            Port = connection.DestinationInputPort;
        }

        public InputPair(NodeSet set, NodeHandle destHandle, InputPortArrayID destinationPort)
        {
            Handle = set.Validate(destHandle);
            var table = set.GetForwardingTable();

            for (var fH = set.GetNode(Handle).ForwardedPortHead; fH != ForwardPortHandle.Invalid; fH = table[fH].NextIndex)
            {
                ref var forwarding = ref table[fH];

                if (!forwarding.IsInput)
                    continue;

                var port = forwarding.GetOriginInputPortID();

                // Forwarded port list are monotonically increasing by port, so we can break out early
                if (forwarding.GetOriginPortCounter() > destinationPort.PortID.Port)
                    break;

                if (port != destinationPort.PortID)
                    continue;

                if (!set.StillExists(forwarding.Replacement))
                    throw new InvalidOperationException("Replacement node for previously registered forward doesn't exist anymore");

                Handle = forwarding.Replacement;
                Port = destinationPort.IsArray
                    ? new InputPortArrayID(forwarding.GetReplacedInputPortID(), destinationPort.ArrayIndex)
                    : new InputPortArrayID(forwarding.GetReplacedInputPortID());

                return;
            }

            Port = destinationPort;
        }
    }

    readonly struct OutputPair
    {
        public readonly ValidatedHandle Handle;
        public readonly OutputPortID Port;

        /// <remarks>
        /// Does not do node validation or port forwarding resolution as an existing connection should have had both done
        /// during the <see cref="NodeSet.Connect"/>.
        /// </remarks>
        public OutputPair(Topology.Connection connection)
        {
            Handle = connection.Source;
            Port = connection.SourceOutputPort;
        }

        public OutputPair(NodeSet set, NodeHandle sourceHandle, OutputPortID sourcePort)
        {
            Handle = set.Validate(sourceHandle);
            var table = set.GetForwardingTable();

            for (var fH = set.GetNode(Handle).ForwardedPortHead; fH != ForwardPortHandle.Invalid; fH = table[fH].NextIndex)
            {
                ref var forwarding = ref table[fH];

                if (forwarding.IsInput)
                    continue;

                var port = forwarding.GetOriginOutputPortID();

                // Forwarded port list are monotonically increasing by port, so we can break out early
                if (forwarding.GetOriginPortCounter() > sourcePort.Port)
                    break;

                if (port != sourcePort)
                    continue;

                if (!set.StillExists(forwarding.Replacement))
                    throw new InvalidOperationException("Replacement node for previously registered forward doesn't exist anymore");

                Handle = forwarding.Replacement;
                Port = forwarding.GetReplacedOutputPortID();

                return;
            }

            Port = sourcePort;
        }
    }
}
