using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Unity.Collections;

namespace Unity.DataFlowGraph
{
    using Topology = TopologyAPI<ValidatedHandle, InputPortArrayID, OutputPortArrayID>;

    class NodeHandleDebugView
    {

        [DebuggerDisplay("Count = {Array.Length}")]
        public struct Collection<TType>
        {
            [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
            public TType[] Array;
        }

        static Collection<T> Collect<T>(IEnumerable<T> items) => new Collection<T> { Array = items.ToArray() };

        static Collection<T> Collect<T>(List<T> items) => new Collection<T> { Array = items.ToArray() };

        public static string DebugDisplay(NodeHandle handle) =>
            $"{GetNodeSet(handle)?.GetDefinition(handle).GetType().Name ?? "<INVALID>"}: {handle.ToString()}";

        public static object GetDebugInfo(NodeHandle handle)
        {
            var set = GetNodeSet(handle);

            if (set != null)
            {
                var def = set.GetDefinition(handle);
                var forwarded = GetForwardedPorts(set, def, handle);

                return new FullDebugInfo
                {
                    VHandle = handle.VHandle,
                    Set = set,
                    Definition = def,
                    Traits = set.GetNodeTraits(handle),
                    InputPorts = GetInputs(set, def, handle),
                    OutputPorts = GetOutputs(set, def, handle),
                    ForwardedInputs = forwarded.Inputs,
                    ForwardedOutputs = forwarded.Outputs
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

        public struct FullDebugInfo
        {
            [DebuggerBrowsable(DebuggerBrowsableState.Never)]
            public VersionedHandle VHandle;
            public NodeSetAPI Set;
            public NodeDefinition Definition;
            public LowLevelNodeTraits Traits;
            public INodeData NodeData => Definition?.BaseTraits.DebugGetNodeData(Set, new NodeHandle(VHandle));
            public IKernelData KernelData => Definition?.BaseTraits.DebugGetKernelData(Set, new NodeHandle(VHandle));
            public Collection<InputPort> InputPorts;
            public Collection<OutputPort> OutputPorts;
            public Collection<Forward<PortDescription.InputPort>> ForwardedInputs;
            public Collection<Forward<PortDescription.OutputPort>> ForwardedOutputs;

        }

        public struct InvalidNodeHandleDebugInfo
        {
            public VersionedHandle VHandle;
            public ushort NodeSetID => VHandle.ContainerID;
        }

        [DebuggerDisplay("{DebugDisplay(), nq}")]
        public class InputConnection
        {
            [DebuggerBrowsable(DebuggerBrowsableState.Never)]
            public PortDescription.OutputPort Description;
            [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
            public NodeHandle Node;

            string DebugDisplay() =>
                $"{NodeHandleDebugView.DebugDisplay(Node)}, {Description.Category}: \"{Description.Name}\"";
        }

        [DebuggerDisplay("{DebugDisplay(), nq}")]
        public class InputPort
        {
            [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
            public Collection<InputConnection> Connections;
            public PortDescription.InputPort Description;

            string DebugDisplay() =>
                $"{Description.Category}: \"{Description.Name}\", Type: {Description.Type}, Connections: {Connections.Array.Length}";
        }


        [DebuggerDisplay("{DebugDisplay(), nq}")]
        public class OutputConnection
        {
            [DebuggerBrowsable(DebuggerBrowsableState.Never)]
            public PortDescription.InputPort Description;
            [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
            public NodeHandle Node;

            string DebugDisplay() =>
                $"{NodeHandleDebugView.DebugDisplay(Node)}, {Description.Category}: \"{Description.Name}\"";
        }

        [DebuggerDisplay("{DebugDisplay(), nq}")]
        public struct OutputPort
        {
            [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
            public Collection<OutputConnection> Connections;
            public PortDescription.OutputPort Description;

            string DebugDisplay() =>
                $"{Description.Category}: \"{Description.Name}\", Type: {Description.Type}, Connections: {Connections.Array.Length}";
        }

        [DebuggerDisplay("{DebugDisplay(), nq}")]
        public struct Forward<TPort>
            where TPort : PortDescription.IPort
        {
            public NodeHandle Replacement;
            public TPort OriginPort;
            public TPort ReplacementPort;

            internal string DebugDisplay()
            {
                var replacedName = ReplacementPort.Name ?? $"?({ReplacementPort.Type.Name})";
                var originName = OriginPort.Name ?? $"?({OriginPort.Type.Name})";

                var set = GetNodeSet(Replacement);
                return $"{originName} -> {set.GetDefinition(Replacement)}.{replacedName}";
            }
        }

        static (Collection<Forward<PortDescription.InputPort>> Inputs, Collection<Forward<PortDescription.OutputPort>> Outputs) GetForwardedPorts(NodeSetAPI set, NodeDefinition self, NodeHandle origin)
        {
            var inputs = new List<Forward<PortDescription.InputPort>>();
            var outputs = new List<Forward<PortDescription.OutputPort>>();

            var table = set.GetForwardingTable();

            var validated = set.Nodes.Validate(origin.VHandle);

            for (var fP = set.Nodes[validated].ForwardedPortHead; fP != ForwardPortHandle.Invalid; fP = table[fP].NextIndex)
            {
                ref var forward = ref table[fP];

                if (forward.IsInput)
                {
                    Forward<PortDescription.InputPort> input;
                    input.Replacement = forward.Replacement.ToPublicHandle();
                    input.OriginPort = self.GetFormalInput(validated, new InputPortArrayID(forward.GetOriginInputPortID()));
                    input.ReplacementPort = set.GetVirtualPort(new InputPair(set, forward.Replacement.ToPublicHandle(), new InputPortArrayID(forward.GetReplacedInputPortID())));
                    inputs.Add(input);
                }
                else
                {
                    Forward<PortDescription.OutputPort> output;
                    output.Replacement = forward.Replacement.ToPublicHandle();
                    output.OriginPort = self.GetFormalOutput(validated, new OutputPortArrayID(forward.GetOriginOutputPortID()));
                    output.ReplacementPort = set.GetVirtualPort(
                        new OutputPair(set, forward.Replacement.ToPublicHandle(), new OutputPortArrayID(forward.GetReplacedOutputPortID()))
                    );
                    outputs.Add(output);
                }
            }

            return (Collect(inputs), Collect(outputs));
        }

        static Collection<InputPort> GetInputs(NodeSetAPI set, NodeDefinition def, NodeHandle handle)
        {
            var validated = set.Nodes.Validate(handle.VHandle);
            var ret = new Dictionary<InputPortID, List<Topology.Connection>>();

            // Add all known ports to the table
            foreach (var port in def.AutoPorts.Inputs)
            {
                ret.Add(port, new List<Topology.Connection>());
            }

            // Add all existing connections
            foreach (var con in set.GetInputs(validated))
            {
                var port = con.DestinationInputPort.PortID;
                // This can happen for eg. component nodes that do not report "ports" in the auto ports
                if (!ret.ContainsKey(port))
                {
                    ret.Add(port, new List<Topology.Connection>());
                }

                ret[port].Add(con);
            }

            var aggr = ret.Select(
                kv => new InputPort
                {
                    Description = def.GetVirtualInput(validated, new InputPortArrayID(kv.Key)),
                    Connections = Collect(
                        kv.Value.Select(
                            c => new InputConnection
                            {
                                Description = set.GetDefinitionInternal(c.Source).GetVirtualOutput(c.Source, c.SourceOutputPort),
                                Node = c.Source.ToPublicHandle()
                            }
                        )
                    )
                }
            ).ToList();

            // Ensure ports are listed in declaration order (which port IDs are)
            aggr.Sort((a, b) => (int)a.Description.PortID.Port.CategoryCounter - (int)b.Description.PortID.Port.CategoryCounter);

            return Collect(aggr);

        }

        static Collection<OutputPort> GetOutputs(NodeSetAPI set, NodeDefinition def, NodeHandle handle)
        {
            var validated = set.Nodes.Validate(handle.VHandle);
            var ret = new Dictionary<OutputPortID, List<Topology.Connection>>();

            // Add all known ports to the table
            foreach (var port in def.AutoPorts.Outputs)
            {
                ret.Add(port, new List<Topology.Connection>());
            }

            // Add all existing connections
            foreach (var con in set.GetOutputs(validated))
            {
                var port = con.SourceOutputPort.PortID;
                // This can happen for eg. component nodes that do not report "ports" in the auto ports
                if (!ret.ContainsKey(port))
                {
                    ret.Add(port, new List<Topology.Connection>());
                }

                ret[port].Add(con);
            }

            var aggr = ret.Select(
                kv => new OutputPort
                {
                    Description = def.GetVirtualOutput(validated, new OutputPortArrayID(kv.Key)),
                    Connections = Collect(
                        kv.Value.Select(
                            c => new OutputConnection
                            {
                                Description = set.GetDefinitionInternal(c.Destination).GetVirtualInput(c.Destination, c.DestinationInputPort),
                                Node = c.Destination.ToPublicHandle()
                            }
                        )
                    )
                }
            ).ToList();

            // Ensure ports are listed in declaration order (which port IDs are)
            aggr.Sort((a, b) => (int)a.Description.PortID.Port.CategoryCounter - (int)b.Description.PortID.Port.CategoryCounter);

            return Collect(aggr);
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
