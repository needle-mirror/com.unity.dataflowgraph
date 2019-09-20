using System;

namespace Unity.DataFlowGraph
{
    public partial class NodeSet
    {
        /// <summary>
        /// See <see cref="Connect(Unity.DataFlowGraph.NodeHandle,Unity.DataFlowGraph.OutputPortID,Unity.DataFlowGraph.NodeHandle,Unity.DataFlowGraph.InputPortID)"/>
        /// </summary>
        public void Connect<TMsg, TSource, TDestination>(
            NodeHandle<TSource> sourceHandle,
            MessageOutput<TSource, TMsg> sourcePort,
            NodeHandle<TDestination> destHandle,
            MessageInput<TDestination, TMsg> destPort
        )
            where TSource : INodeDefinition
            where TDestination : INodeDefinition, IMsgHandler<TMsg>
        {
            NodeVersionCheck(sourceHandle.VHandle);
            NodeVersionCheck(destHandle.VHandle);

            UncheckedTypedConnect((TraversalFlags.Message, typeof(TMsg)), sourceHandle, sourcePort.Port, destHandle, new InputPortArrayID(destPort.Port));
        }

        /// <summary>
        /// Overload of <see cref="Connect(Unity.DataFlowGraph.NodeHandle,Unity.DataFlowGraph.OutputPortID,Unity.DataFlowGraph.NodeHandle,Unity.DataFlowGraph.InputPortID)"/>
        /// targeting a destination port array with an index parameter.
        /// </summary>
        /// <exception cref="IndexOutOfRangeException">Thrown if the index is out of range with respect to the port array.</exception>
        public void Connect<TMsg, TSource, TDestination>(
            NodeHandle<TSource> sourceHandle,
            MessageOutput<TSource, TMsg> sourcePort,
            NodeHandle<TDestination> destHandle,
            PortArray<MessageInput<TDestination, TMsg>> destPortArray,
            ushort index
        )
            where TSource : INodeDefinition
            where TDestination : INodeDefinition, IMsgHandler<TMsg>
        {
            NodeVersionCheck(sourceHandle.VHandle);
            NodeVersionCheck(destHandle.VHandle);

            UncheckedTypedConnect((TraversalFlags.Message, typeof(TMsg)), sourceHandle, sourcePort.Port, destHandle, new InputPortArrayID(destPortArray.Port, index));
        }

        /// <summary>
        /// See <see cref="Connect(Unity.DataFlowGraph.NodeHandle,Unity.DataFlowGraph.OutputPortID,Unity.DataFlowGraph.NodeHandle,Unity.DataFlowGraph.InputPortID)"/>
        /// </summary>
        public void Connect<TDSLHandler, TDSL, TSource, TDestination>(
            NodeHandle<TSource> sourceHandle,
            DSLOutput<TSource, TDSLHandler, TDSL> sourcePort,
            NodeHandle<TDestination> destHandle,
            DSLInput<TDestination, TDSLHandler, TDSL> destPort
        )
            where TSource : INodeDefinition, TDSL
            where TDestination : INodeDefinition, TDSL
            where TDSLHandler : DSLHandler<TDSL>, new()
            where TDSL : class
        {
            NodeVersionCheck(sourceHandle.VHandle);
            NodeVersionCheck(destHandle.VHandle);

            UncheckedTypedConnect((TraversalFlags.DSL, typeof(TDSLHandler)), sourceHandle, sourcePort.Port, destHandle, new InputPortArrayID(destPort.Port));
        }

        /// <summary>
        /// See <see cref="Connect(Unity.DataFlowGraph.NodeHandle,Unity.DataFlowGraph.OutputPortID,Unity.DataFlowGraph.NodeHandle,Unity.DataFlowGraph.InputPortID)"/>
        /// </summary>
        public void Connect<TType, TSource, TDestination>(
            NodeHandle<TSource> sourceHandle,
            DataOutput<TSource, TType> sourcePort,
            NodeHandle<TDestination> destHandle,
            DataInput<TDestination, TType> destPort
        )
            where TSource : INodeDefinition
            where TDestination : INodeDefinition
            where TType : struct
        {
            NodeVersionCheck(sourceHandle.VHandle);
            NodeVersionCheck(destHandle.VHandle);

            UncheckedTypedConnect((TraversalFlags.DataFlow, typeof(TType)), sourceHandle, sourcePort.Port, destHandle, new InputPortArrayID(destPort.Port));
        }

        /// <summary>
        /// Overload of <see cref="Connect(Unity.DataFlowGraph.NodeHandle,Unity.DataFlowGraph.OutputPortID,Unity.DataFlowGraph.NodeHandle,Unity.DataFlowGraph.InputPortID)"/>
        /// targeting a destination port array with an index parameter.
        /// </summary>
        /// <exception cref="IndexOutOfRangeException">Thrown if the index is out of range with respect to the port array.</exception>
        public void Connect<TType, TSource, TDestination>(
            NodeHandle<TSource> sourceHandle,
            DataOutput<TSource, TType> sourcePort,
            NodeHandle<TDestination> destHandle,
            PortArray<DataInput<TDestination, TType>> destPortArray,
            ushort index
        )
            where TSource : INodeDefinition
            where TDestination : INodeDefinition
            where TType : struct
        {
            NodeVersionCheck(sourceHandle.VHandle);
            NodeVersionCheck(destHandle.VHandle);

            UncheckedTypedConnect((TraversalFlags.DataFlow, typeof(TType)), sourceHandle, sourcePort.Port, destHandle, new InputPortArrayID(destPortArray.Port, index));
        }

        /// <summary>
        /// See <see cref="Disconnect(Unity.DataFlowGraph.NodeHandle,Unity.DataFlowGraph.OutputPortID,Unity.DataFlowGraph.NodeHandle,Unity.DataFlowGraph.InputPortID)"/>
        /// </summary>
        public void Disconnect<TMsg, TSource, TDestination>(
            NodeHandle<TSource> sourceHandle,
            MessageOutput<TSource, TMsg> sourcePort,
            NodeHandle<TDestination> destHandle,
            MessageInput<TDestination, TMsg> destPort
        )
            where TSource : INodeDefinition
            where TDestination : INodeDefinition, IMsgHandler<TMsg>
        {
            NodeVersionCheck(sourceHandle.VHandle);
            NodeVersionCheck(destHandle.VHandle);

            UncheckedDisconnect(sourceHandle, sourcePort.Port, destHandle, new InputPortArrayID(destPort.Port));
        }

        /// <summary>
        /// Overload of <see cref="Disconnect(Unity.DataFlowGraph.NodeHandle,Unity.DataFlowGraph.OutputPortID,Unity.DataFlowGraph.NodeHandle,Unity.DataFlowGraph.InputPortID)"/>
        /// targeting a destination port array with an index parameter.
        /// </summary>
        /// <exception cref="IndexOutOfRangeException">Thrown if the index is out of range with respect to the port array.</exception>
        public void Disconnect<TMsg, TSource, TDestination>(
            NodeHandle<TSource> sourceHandle,
            MessageOutput<TSource, TMsg> sourcePort,
            NodeHandle<TDestination> destHandle,
            PortArray<MessageInput<TDestination, TMsg>> destPortArray,
            ushort index
        )
            where TSource : INodeDefinition
            where TDestination : INodeDefinition, IMsgHandler<TMsg>
        {
            NodeVersionCheck(sourceHandle.VHandle);
            NodeVersionCheck(destHandle.VHandle);

            UncheckedDisconnect(sourceHandle, sourcePort.Port, destHandle, new InputPortArrayID(destPortArray.Port, index));
        }

        /// <summary>
        /// See <see cref="Disconnect(Unity.DataFlowGraph.NodeHandle,Unity.DataFlowGraph.OutputPortID,Unity.DataFlowGraph.NodeHandle,Unity.DataFlowGraph.InputPortID)"/>
        /// </summary>
        public void Disconnect<TDSLHandler, TDSL, TSource, TDestination>(
            NodeHandle<TSource> sourceHandle,
            DSLOutput<TSource, TDSLHandler, TDSL> sourcePort,
            NodeHandle<TDestination> destHandle,
            DSLInput<TDestination, TDSLHandler, TDSL> destPort
        )
            where TSource : INodeDefinition, TDSL
            where TDestination : INodeDefinition, TDSL
            where TDSLHandler : DSLHandler<TDSL>, new()
            where TDSL : class
        {
            NodeVersionCheck(sourceHandle.VHandle);
            NodeVersionCheck(destHandle.VHandle);

            UncheckedDisconnect(sourceHandle, sourcePort.Port, destHandle, new InputPortArrayID(destPort.Port));
        }

        /// <summary>
        /// See <see cref="Disconnect(Unity.DataFlowGraph.NodeHandle,Unity.DataFlowGraph.OutputPortID,Unity.DataFlowGraph.NodeHandle,Unity.DataFlowGraph.InputPortID)"/>
        /// </summary>
        public void Disconnect<TType, TSource, TDestination>(
            NodeHandle<TSource> sourceHandle,
            DataOutput<TSource, TType> sourcePort,
            NodeHandle<TDestination> destHandle,
            DataInput<TDestination, TType> destPort
        )
            where TSource : INodeDefinition
            where TDestination : INodeDefinition
            where TType : struct
        {
            NodeVersionCheck(sourceHandle.VHandle);
            NodeVersionCheck(destHandle.VHandle);

            UncheckedDisconnect(sourceHandle, sourcePort.Port, destHandle, new InputPortArrayID(destPort.Port));
        }

        /// <summary>
        /// Overload of <see cref="Disconnect(Unity.DataFlowGraph.NodeHandle,Unity.DataFlowGraph.OutputPortID,Unity.DataFlowGraph.NodeHandle,Unity.DataFlowGraph.InputPortID)"/>
        /// targeting a destination port array with an index parameter.
        /// </summary>
        /// <exception cref="IndexOutOfRangeException">Thrown if the index is out of range with respect to the port array.</exception>
        public void Disconnect<TType, TSource, TDestination>(
            NodeHandle<TSource> sourceHandle,
            DataOutput<TSource, TType> sourcePort,
            NodeHandle<TDestination> destHandle,
            PortArray<DataInput<TDestination, TType>> destPortArray,
            ushort index
        )
            where TSource : INodeDefinition
            where TDestination : INodeDefinition
            where TType : struct
        {
            NodeVersionCheck(sourceHandle.VHandle);
            NodeVersionCheck(destHandle.VHandle);

            UncheckedDisconnect(sourceHandle, sourcePort.Port, destHandle, new InputPortArrayID(destPortArray.Port, index));
        }

        /// <summary>
        /// See <see cref="DisconnectAndRetainValue(Unity.DataFlowGraph.NodeHandle,Unity.DataFlowGraph.OutputPortID,Unity.DataFlowGraph.NodeHandle,Unity.DataFlowGraph.InputPortID)"/>
        /// </summary>
        public void DisconnectAndRetainValue<TType, TSource, TDestination>(
            NodeHandle<TSource> sourceHandle,
            DataOutput<TSource, TType> sourcePort,
            NodeHandle<TDestination> destHandle,
            DataInput<TDestination, TType> destPort
        )
            where TSource : INodeDefinition, new()
            where TDestination : INodeDefinition, new()
            where TType : struct
        {
            DisconnectAndRetainValue(sourceHandle, sourcePort, destHandle, new InputPortArrayID(destPort.Port));
        }

        /// <summary>
        /// Overload of <see cref="DisconnectAndRetainValue(Unity.DataFlowGraph.NodeHandle,Unity.DataFlowGraph.OutputPortID,Unity.DataFlowGraph.NodeHandle,Unity.DataFlowGraph.InputPortID)"/>
        /// targeting a port array with an index parameter.
        /// </summary>
        /// <exception cref="IndexOutOfRangeException">Thrown if the index is out of range with respect to the port array.</exception>
        public void DisconnectAndRetainValue<TType, TSource, TDestination>(
            NodeHandle<TSource> sourceHandle,
            DataOutput<TSource, TType> sourcePort,
            NodeHandle<TDestination> destHandle,
            PortArray<DataInput<TDestination, TType>> destPortArray,
            ushort index
        )
            where TSource : INodeDefinition, new()
            where TDestination : INodeDefinition, new()
            where TType : struct
        {
            DisconnectAndRetainValue(sourceHandle, sourcePort, destHandle, new InputPortArrayID(destPortArray.Port, index));
        }

        public void Connect<TTask, TDSLHandler, TDSL, TSource, TDestination>(
            NodeHandle<TSource> sourceHandle,
            DSLOutput<TSource, TDSLHandler, TDSL> sourcePort,
            NodeInterfaceLink<TTask, TDestination> destHandle
        )
            where TSource : INodeDefinition, TDSL
            where TDestination : TTask, INodeDefinition
            where TTask : ITaskPort<TTask>
            where TDSLHandler : DSLHandler<TDSL>, new()
            where TDSL : class
        {
            Connect(sourceHandle, sourcePort.Port, destHandle, ((TTask)GetFunctionality(destHandle)).GetPort(destHandle));
        }

        public void Connect<TTask, TMsg, TSource, TDestination>(
            NodeHandle<TSource> sourceHandle,
            MessageOutput<TSource, TMsg> sourcePort,
            NodeInterfaceLink<TTask, TDestination> destHandle
        )
            where TSource : INodeDefinition
            where TDestination : TTask, INodeDefinition, IMsgHandler<TMsg>
            where TTask : ITaskPort<TTask>
        {
            Connect(sourceHandle, sourcePort.Port, destHandle, ((TTask)GetFunctionality(destHandle)).GetPort(destHandle));
        }

        public void Connect<TTask>(
            NodeHandle sourceHandle,
            OutputPortID sourcePort,
            NodeInterfaceLink<TTask> destHandle
        )
            where TTask : ITaskPort<TTask>
        {
            var f = GetFunctionality(destHandle);
            if (f is TTask task)
            {
                Connect(sourceHandle, sourcePort, destHandle, task.GetPort(destHandle));
            }
            else
                throw new InvalidOperationException(
                    $"Cannot connect source to destination. Destination not of type {typeof(TTask).Name}");
        }

        public void Connect<TTask, TType, TSource, TDestination>(
            NodeHandle<TSource> sourceHandle,
            DataOutput<TSource, TType> sourcePort,
            NodeInterfaceLink<TTask, TDestination> destHandle
        )
            where TSource : INodeDefinition
            where TDestination : INodeDefinition, TTask
            where TTask : ITaskPort<TTask>
            where TType : struct
        {
            Connect(sourceHandle, sourcePort.Port, destHandle, ((TTask)GetFunctionality(destHandle)).GetPort(destHandle));
        }

        public void Disconnect<TTask, TDSLHandler, TDSL, TSource, TDestination>(
            NodeHandle<TSource> sourceHandle,
            DSLOutput<TSource, TDSLHandler, TDSL> sourcePort,
            NodeInterfaceLink<TTask, TDestination> destHandle
        )
            where TSource : INodeDefinition, TDSL
            where TDestination : INodeDefinition, TDSL, TTask
            where TDSLHandler : DSLHandler<TDSL>, new()
            where TDSL : class
            where TTask : ITaskPort<TTask>
        {
            Disconnect(sourceHandle, sourcePort.Port, destHandle, ((TTask)GetFunctionality(destHandle)).GetPort(destHandle));
        }

        public void Disconnect<TTask, TMsg, TSource, TDestination>(
            NodeHandle<TSource> sourceHandle,
            MessageOutput<TSource, TMsg> sourcePort,
            NodeInterfaceLink<TTask, TDestination> destHandle
        )
            where TSource : INodeDefinition
            where TTask : ITaskPort<TTask>
            where TDestination : INodeDefinition, TTask, IMsgHandler<TMsg>
        {
            Disconnect(sourceHandle, sourcePort.Port, destHandle, ((TTask)GetFunctionality(destHandle)).GetPort(destHandle));
        }

        public void Disconnect<TTask, TType, TSource, TDestination>(
            NodeHandle<TSource> sourceHandle,
            DataOutput<TSource, TType> sourcePort,
            NodeInterfaceLink<TTask, TDestination> destHandle
        )
            where TSource : INodeDefinition
            where TDestination : INodeDefinition, TTask
            where TType : struct
            where TTask : ITaskPort<TTask>
        {
            Disconnect(sourceHandle, sourcePort.Port, destHandle, ((TTask)GetFunctionality(destHandle)).GetPort(destHandle));
        }

        public void Disconnect<TTask>(
            NodeHandle sourceHandle,
            OutputPortID sourcePort,
            NodeInterfaceLink<TTask> destHandle
        )
            where TTask : ITaskPort<TTask>
        {
            var f = GetFunctionality(destHandle);
            if (f is TTask task)
            {
                Disconnect(sourceHandle, sourcePort, destHandle, task.GetPort(destHandle));
            }
            else
                throw new InvalidOperationException(
                    $"Cannot disconnect source from destination. Destination not of type {typeof(TTask).Name}");
        }

        /// <summary>
        /// See <see cref="DisconnectAndRetainValue(Unity.DataFlowGraph.NodeHandle,Unity.DataFlowGraph.OutputPortID,Unity.DataFlowGraph.NodeHandle,Unity.DataFlowGraph.InputPortID)"/>
        /// </summary>
        void DisconnectAndRetainValue<TType, TSource, TDestination>(
            NodeHandle<TSource> sourceHandle,
            DataOutput<TSource, TType> sourcePort,
            NodeHandle<TDestination> destHandle,
            InputPortArrayID destPort
        )
            where TSource : INodeDefinition, new()
            where TDestination : INodeDefinition, new()
            where TType : struct
        {
            NodeVersionCheck(sourceHandle.VHandle);
            NodeVersionCheck(destHandle.VHandle);

            var portDef = GetFunctionality(destHandle).GetPortDescription(destHandle).Inputs[destPort.PortID.Port];

            if (portDef.HasBuffers)
                throw new InvalidOperationException($"Cannot retain data on a data port which includes buffers");

            UncheckedDisconnect(sourceHandle, sourcePort.Port, destHandle, destPort);

            NodeHandle untypedHandle = destHandle;

            // TODO: Double public resolve. Fix in follow-up PR.
            ResolvePublicDestination(ref untypedHandle, ref destPort);
            m_Diff.RetainData(untypedHandle, destPort);
        }
    }
}
