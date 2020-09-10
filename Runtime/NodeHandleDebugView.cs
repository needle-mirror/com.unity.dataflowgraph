using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Unity.Collections;

namespace Unity.DataFlowGraph
{
    class NodeHandleDebugView
    {
        public static string DebugDisplay(NodeHandle handle) =>
            $"{handle.ToString()}, Node: {GetNodeSet(handle)?.GetDefinition(handle).GetType().Name ?? "<INVALID>"}";

        public static object GetDebugInfo(NodeHandle handle)
        {
            var set = GetNodeSet(handle);

            if (set != null)
            {
                var def = set.GetDefinition(handle);
                return new FullDebugInfo
                {
                    VHandle = handle.VHandle,
                    Set = set,
                    Definition = def,
                    Traits = set.GetNodeTraits(handle),
                    InputPorts = GetInputs(set, def, handle).ToArray(),
                    OutputPorts = GetOutputs(set, def, handle).ToArray()
                };
            }
            else
            {
                return new InvalidNodeHandleDebugInfo
                {
                    VHandle = handle.VHandle
                };
            }
        }

        public NodeHandleDebugView(NodeHandle handle)
        {
            DebugInfo = GetDebugInfo(handle);
        }

        static NodeSetAPI GetNodeSet(NodeHandle handle)
        {
            var set = DataFlowGraph.DebugInfo.DebugGetNodeSet(handle.NodeSetID);
            return set != null && set.Exists(handle) ? set : null;
        }

        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public object DebugInfo;

        struct FullDebugInfo
        {
            [DebuggerBrowsable(DebuggerBrowsableState.Never)]
            public VersionedHandle VHandle;
            public NodeSetAPI Set;
            public NodeDefinition Definition;
            public LowLevelNodeTraits Traits;
            public INodeData NodeData => Definition?.BaseTraits.DebugGetNodeData(Set, new NodeHandle(VHandle));
            public IKernelData KernelData => Definition?.BaseTraits.DebugGetKernelData(Set, new NodeHandle(VHandle));
            public InputPort[] InputPorts;
            public OutputPort[] OutputPorts;
        }

        struct InvalidNodeHandleDebugInfo
        {
            public VersionedHandle VHandle;
            public ushort NodeSetID => VHandle.ContainerID;
        }

        [DebuggerDisplay("{DebugDisplay(), nq}")]
        [DebuggerTypeProxy(typeof(InputConnectionDebugView))]
        class InputConnection
        {
            public PortDescription.OutputPort Description;
            public NodeHandle Node;

            string DebugDisplay() =>
                $"{NodeHandleDebugView.DebugDisplay(Node)}, {Description.Category}: \"{Description.Name}\"";
        }

        class InputConnectionDebugView
        {
            [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
            public NodeHandle Node;
            public InputConnectionDebugView(InputConnection connection)
            {
                Node = connection.Node;
            }
        }

        [DebuggerDisplay("{DebugDisplay(), nq}")]
        [DebuggerTypeProxy(typeof(InputPortDebugView))]
        class InputPort
        {
            public PortDescription.InputPort Description;
            public InputConnection[] Connections;

            string DebugDisplay() =>
                $"{Description.Category}: \"{Description.Name}\", Type: {Description.Type}, Connections: {Connections.Length}";
        }

        class InputPortDebugView
        {
            [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
            public InputConnection[] Connections;
            public InputPortDebugView(InputPort inputPort)
            {
                Connections = inputPort.Connections;
            }
        }

        [DebuggerDisplay("{DebugDisplay(), nq}")]
        [DebuggerTypeProxy(typeof(OutputConnectionDebugView))]
        class OutputConnection
        {
            public PortDescription.InputPort Description;
            public NodeHandle Node;

            string DebugDisplay() =>
                $"{NodeHandleDebugView.DebugDisplay(Node)}, {Description.Category}: \"{Description.Name}\"";
        }

        class OutputConnectionDebugView
        {
            [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
            public NodeHandle Node;
            public OutputConnectionDebugView(OutputConnection connection)
            {
                Node = connection.Node;
            }
        }

        [DebuggerDisplay("{DebugDisplay(), nq}")]
        [DebuggerTypeProxy(typeof(OutputPortDebugView))]
        struct OutputPort
        {
            public PortDescription.OutputPort Description;
            public OutputConnection[] Connections;

            string DebugDisplay() =>
                $"{Description.Category}: \"{Description.Name}\", Type: {Description.Type}, Connections: {Connections.Length}";
        }

        class OutputPortDebugView
        {
            [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
            public OutputConnection[] Connections;
            public OutputPortDebugView(OutputPort outputPort)
            {
                Connections = outputPort.Connections;
            }
        }

        static List<InputPort> GetInputs(NodeSetAPI set, NodeDefinition def, NodeHandle handle)
        {
            var ret = new List<InputPort>();

            foreach (var port in def.GetPortDescription(handle).Inputs)
            {
                var cons = new List<InputConnection>();
                foreach (var con in set.GetInputs(set.Nodes.Validate(handle.VHandle)))
                    if (con.DestinationInputPort.PortID == (InputPortID)port)
                        cons.Add(new InputConnection {
                            Node = con.Source.ToPublicHandle(),
                            Description = set.GetDefinitionInternal(con.Source).GetVirtualOutput(con.Source, con.SourceOutputPort)
                        });
                ret.Add(new InputPort {Description = port, Connections = cons.ToArray()});
            }

            return ret;
        }

        static List<OutputPort> GetOutputs(NodeSetAPI set, NodeDefinition def, NodeHandle handle)
        {
            var ret = new List<OutputPort>();

            foreach (var port in def.GetPortDescription(handle).Outputs)
            {
                var cons = new List<OutputConnection>();
                foreach (var con in set.GetOutputs(set.Nodes.Validate(handle.VHandle)))
                    if (con.SourceOutputPort.PortID == (OutputPortID)port)
                        cons.Add(new OutputConnection {
                            Node = con.Destination.ToPublicHandle(),
                            Description = set.GetDefinitionInternal(con.Destination).GetVirtualInput(con.Destination, con.DestinationInputPort)
                        });
                ret.Add(new OutputPort {Description = port, Connections = cons.ToArray()});
            }

            return ret;
        }
    }

    class NodeHandleDebugView<TDefinition>
        where TDefinition : NodeDefinition
    {
        public NodeHandleDebugView(NodeHandle<TDefinition> handle)
        {
            DebugInfo = NodeHandleDebugView.GetDebugInfo(handle);
        }

        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public object DebugInfo;
    }
}
