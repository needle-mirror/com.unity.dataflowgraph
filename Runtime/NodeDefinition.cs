using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.DataFlowGraph
{
    /// <summary>
    /// Base interface of all nodes. 
    /// <see cref="NodeFunctionality"/>
    /// </summary>
    public interface INodeFunctionality : IDisposable
    {
        // injections
        NodeSet Set { get; set; }
        NodeTraitsBase BaseTraits { get; }

        void OnMessage<T>(in MessageContext ctx, in T msg);
        /// <remarks>
        /// It is undefined behaviour to throw an exception from this method.
        /// </remarks>
        void OnUpdate(NodeHandle handle);

        /// <summary>
        /// Constructor function, called for each instantiation of this type.
        /// <seealso cref="NodeSet.Create{TDefinition}"/>
        /// </summary>
        /// <remarks>
        /// It is undefined behaviour to throw an exception from this method.
        /// </remarks>
        /// <param name="ctx">
        /// Provides initialization context and do-once operations
        /// for this particular node.
        /// <seealso cref="Init(InitContext)"/>
        /// </param>
        void Init(InitContext ctx);
        /// <summary>
        /// Destructor, provides an opportunity to clean up resources related to 
        /// this instance.
        /// <seealso cref="NodeSet.Destroy(NodeHandle)"/>
        /// </summary>
        /// <remarks>
        /// It is undefined behaviour to throw an exception from this method.
        /// </remarks>
        void Destroy(NodeHandle handle);

        // maintenance
        PortDescription GetPortDescription(NodeHandle handle);

        // TODO: Would be nice to be internal
        void GeneratePortDescriptions();
    }

    public interface INodeDefinition : INodeFunctionality
    {
    }

    /// <summary>
    /// Use this to tag your INodeData as being managed,
    /// meaning it will be possible to store non-blittable
    /// data on the type (like references).
    /// </summary>
    public sealed class ManagedAttribute : System.Attribute { }

    /// <summary>
    /// Interface tag to be implemented on a struct, that will contain the 
    /// simulation-side contents of your node's instance data.
    /// </summary>
    public interface INodeData { }

    /// <summary>
    /// Interface tag to be implemented on a struct, that will contain the 
    /// the node definition's simulation port declarations.
    /// <seealso cref="MessageInput{TDefinition, TMsg}"/>
    /// <seealso cref="MessageOutput{TDefinition, TMsg}"/>
    /// <seealso cref="DSLInput{TNodeDefinition, TDSLDefinition, IDSL}"/>
    /// <seealso cref="DSLOutput{TNodeDefinition, TDSLDefinition, IDSL}"/>
    /// </summary>
    public interface ISimulationPortDefinition { }

    static class NodeDefinitionCounter
    {
        internal static int Counter;
    }

    static class NodeDefinitionTypeIndex<TDefinition>
        where TDefinition : INodeFunctionality
    {
        // Note that definition indices start at 1 on purpose, reserving 0 as invalid
        // TODO: Consider thread safety
        internal static readonly int Index = ++NodeDefinitionCounter.Counter;
    }

    /// <summary>
    /// Base class for all node definition declarations. Provides helper
    /// functionality and base implementations around 
    /// <see cref="INodeFunctionality"/>.
    /// 
    /// A <see cref="NodeFunctionality"/> instance exists per existing 
    /// <see cref="NodeSet"/>.
    /// </summary>
    public abstract class NodeFunctionality : INodeDefinition
    {
        /// <summary>
        /// The <see cref="NodeSet"/> associated with this instance of this 
        /// node definition.
        /// </summary>
        public NodeSet Set { get; set; }
        public abstract NodeTraitsBase BaseTraits { get; }

        protected PortDescription AutoPorts;

        /// <summary>
        /// See <see cref="INodeFunctionality.OnUpdate(NodeHandle)"/>
        /// </summary>
        public virtual void OnUpdate(NodeHandle handle) { }
        /// <summary>
        /// See <see cref="INodeFunctionality.Init(InitContext)"/>
        /// </summary>
        public virtual void Init(InitContext ctx) { }
        /// <summary>
        /// See <see cref="INodeFunctionality.Destroy(NodeHandle)"/>
        /// </summary>
        public virtual void Destroy(NodeHandle handle) { }
        /// <summary>
        /// Called when disposing a <see cref="NodeSet"/>.
        /// <seealso cref="NodeSet.Dispose()"/>
        /// </summary>
        public virtual void Dispose() { }

        public virtual void OnMessage<T>(in MessageContext ctx, in T msg)
        {
            if (this is IMsgHandler<T> specificHandler)
            {
                specificHandler.HandleMessage(ctx, msg);
            }
            else
                throw new InvalidOperationException("Node cannot handle messages of type " + typeof(T).Name);
        }

        /// <summary>
        /// Retrieve the runtime type information about this node's input and output ports (see <see cref="PortDescription"/>.
        /// </summary>
        public virtual PortDescription GetPortDescription(NodeHandle handle) => AutoPorts;

        public void GeneratePortDescriptions()
        {
            var ports = new PortDescription();
            ports.Inputs = new List<PortDescription.InputPort>();
            ports.Outputs = new List<PortDescription.OutputPort>();

            var type = GetType();
            var fields = type.GetFields(BindingFlags.Static | BindingFlags.FlattenHierarchy | BindingFlags.NonPublic);

            FieldInfo simulationPortDefinition = null, kernelPortDefinition = null;

            foreach (var fieldInfo in fields)
            {
                if (simulationPortDefinition != null && kernelPortDefinition != null)
                    break;

                if (typeof(ISimulationPortDefinition).IsAssignableFrom(fieldInfo.FieldType))
                {
                    simulationPortDefinition = fieldInfo;
                }
                else if (typeof(IKernelPortDefinition).IsAssignableFrom(fieldInfo.FieldType))
                {
                    kernelPortDefinition = fieldInfo;
                }
            }

            if (simulationPortDefinition != null)
                ParsePortDefinition(simulationPortDefinition, ports, type, true);

            if (kernelPortDefinition != null)
                ParsePortDefinition(kernelPortDefinition, ports, type, false);

            AutoPorts = ports;
        }

        /// <summary>
        /// Emit a message from yourself on a port. Everything connected to it
        /// will receive your message.
        /// </summary>
        protected void EmitMessage<T, TNodeDefinition>(NodeHandle from, MessageOutput<TNodeDefinition, T> port, in T msg)
            where TNodeDefinition : INodeDefinition
        {
            Set.EmitMessage(from, port.Port, msg);
        }

        static void ParsePortDefinition(FieldInfo staticTopLevelField, PortDescription description, Type nodeType, bool isSimulation)
        {
            var qualifiedFieldType = staticTopLevelField.FieldType;
            var topLevelFieldValue = staticTopLevelField.GetValue(null);

            var definitionFields = qualifiedFieldType.GetFields(BindingFlags.Instance | BindingFlags.Public);
            var disallowedFieldTypes =
                qualifiedFieldType.GetFields(BindingFlags.Static | BindingFlags.Public)
                .Concat(qualifiedFieldType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
                .Concat(qualifiedFieldType.GetFields(BindingFlags.NonPublic | BindingFlags.Static))
                .ToList();

            if (disallowedFieldTypes.Count > 0)
                throw new InvalidNodeDefinitionException($"Port definition type {staticTopLevelField} can only contain public non-static members; {disallowedFieldTypes.First()} violates this");

            foreach (var fieldInfo in definitionFields)
            {
                // standalone port message/dsl declarations
                var qualifiedSimFieldType = fieldInfo.FieldType;

                if (!qualifiedSimFieldType.IsConstructedGenericType)
                    throw new InvalidNodeDefinitionException($"Simulation port definition contains disallowed field {qualifiedSimFieldType}.");

                var genericField = qualifiedSimFieldType.GetGenericTypeDefinition();

                var generics = qualifiedSimFieldType.GetGenericArguments();

                bool isPortArray = genericField == typeof(PortArray<>);
                if (isPortArray)
                {
                    // Extract the specifics of the port type inside the port array.
                    genericField = generics[0];
                    if (!genericField.IsConstructedGenericType)
                        throw new InvalidNodeDefinitionException($"Simulation port definition contains disallowed field {qualifiedSimFieldType}.");
                    generics = genericField.GetGenericArguments();
                    genericField = genericField.GetGenericTypeDefinition();
                }

                if (generics.Length < 2)
                    throw new InvalidNodeDefinitionException($"Simulation port definition contains disallowed type {qualifiedSimFieldType}.");

                var genericType = generics[1];

                var portIDFieldInfo = qualifiedSimFieldType.GetField("Port", BindingFlags.Instance | BindingFlags.NonPublic);
                var portFieldValue = fieldInfo.GetValue(topLevelFieldValue);

                if (isSimulation)
                {
                    if (genericField == typeof(MessageInput<,>))
                    {
                        var portNumber = (ushort)description.Inputs.Count;
                        description.Inputs.Add(PortDescription.InputPort.Message(genericType, portNumber, isPortArray, fieldInfo.Name));
                        portIDFieldInfo.SetValue(portFieldValue, new InputPortID(portNumber));
                    }
                    else if (genericField == typeof(MessageOutput<,>))
                    {
                        var portNumber = (ushort)description.Outputs.Count;
                        description.Outputs.Add(PortDescription.OutputPort.Message(genericType, portNumber, fieldInfo.Name));
                        portIDFieldInfo.SetValue(portFieldValue, new OutputPortID { Port = portNumber });
                    }
                    else if (genericField == typeof(DSLInput<,,>))
                    {
                        var portNumber = (ushort)description.Inputs.Count;
                        description.Inputs.Add(PortDescription.InputPort.DSL(genericType, portNumber, fieldInfo.Name));
                        portIDFieldInfo.SetValue(portFieldValue, new InputPortID(portNumber));
                    }
                    else if (genericField == typeof(DSLOutput<,,>))
                    {
                        var portNumber = (ushort)description.Outputs.Count;
                        description.Outputs.Add(PortDescription.OutputPort.DSL(genericType, portNumber, fieldInfo.Name));
                        portIDFieldInfo.SetValue(portFieldValue, new OutputPortID { Port = portNumber });
                    }
                    else
                    {
                        throw new InvalidNodeDefinitionException($"Simulation port definition contains disallowed type {genericField}.");
                    }
                }
                else
                {
                    if (genericField == typeof(DataInput<,>))
                    {
                        // Are any Buffer<T> instances present in type.
                        var hasBuffers =
                            genericType.IsConstructedGenericType && genericType.GetGenericTypeDefinition() == typeof(Buffer<>) ||
                            genericType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy)
                                .Any(bi => bi.FieldType.IsConstructedGenericType && bi.FieldType.GetGenericTypeDefinition() == typeof(Buffer<>));

                        var portNumber = (ushort)description.Inputs.Count;
                        description.Inputs.Add(PortDescription.InputPort.Data(genericType, portNumber, hasBuffers, isPortArray, fieldInfo.Name));
                        portIDFieldInfo.SetValue(portFieldValue, new InputPortID(portNumber));
                    }
                    else if (genericField == typeof(DataOutput<,>))
                    {
                        // Identify any Buffer<T> instances present in type.
                        var bufferInfos = (genericType.IsConstructedGenericType && genericType.GetGenericTypeDefinition() == typeof(Buffer<>)) ?
                            new List<(int Offset, SimpleType ItemType)>() { (0, new SimpleType(genericType.GetGenericArguments()[0])) } :
                            genericType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy)
                                .Where(bi => bi.FieldType.IsConstructedGenericType && bi.FieldType.GetGenericTypeDefinition() == typeof(Buffer<>))
                                .Select(bi => (UnsafeUtility.GetFieldOffset(bi), new SimpleType(bi.FieldType.GetGenericArguments()[0])))
                                .ToList();

                        var portNumber = (ushort)description.Outputs.Count;
                        description.Outputs.Add(PortDescription.OutputPort.Data(genericType, portNumber, bufferInfos, fieldInfo.Name));
                        portIDFieldInfo.SetValue(portFieldValue, new OutputPortID { Port = portNumber });
                    }
                    else
                    {
                        throw new InvalidNodeDefinitionException($"Kernel port definition contains disallowed type {genericField}.");
                    }
                }

                // The first generic for Message/Data/DSLInput/Output is the NodeDefinition class
                if (generics[0] != nodeType)
                    throw new InvalidNodeDefinitionException($"Port definition references incorrect NodeDefinition class {generics[0]}");

                fieldInfo.SetValue(topLevelFieldValue, portFieldValue);
            }

            staticTopLevelField.SetValue(null, topLevelFieldValue);

        }
    }

    /// <summary>
    /// Base class for a simulation-only node.
    /// <seealso cref="NodeFunctionality"/>
    /// </summary>
    public abstract class NodeFunctionality<TSimulationPortDefinition>
        : NodeFunctionality
            where TSimulationPortDefinition : struct, ISimulationPortDefinition
    {
        /// <summary>
        /// The simulation port definition of this node's public contract. Use this to connect together messages and
        /// DSLs between nodes using the various methods of <see cref="NodeSet"/> which require a port.
        /// 
        /// This is the concrete static instance of the <see cref="ISimulationPortDefinition"/> struct used in the
        /// declaration of a node's <see cref="NodeDefinition{TNodeData,TSimulationPortDefinition}"/> or other variant.
        /// <seealso cref="NodeSet.Connect(NodeHandle, OutputPortID, NodeHandle, InputPortID)"/>
        /// </summary>
        public static ref readonly TSimulationPortDefinition SimulationPorts => ref s_SimulationPorts;
        internal static TSimulationPortDefinition s_SimulationPorts;
    }

    /// <summary>
    /// Base class for a combined simulation / rendering node.
    /// <seealso cref="NodeFunctionality"/>
    /// </summary>
    public abstract class NodeFunctionality<TSimulationPortDefinition, TKernelPortDefinition>
        : NodeFunctionality<TSimulationPortDefinition>
            where TSimulationPortDefinition : struct, ISimulationPortDefinition
            where TKernelPortDefinition : struct, IKernelPortDefinition
    {
        /// <summary>
        /// The kernel port definition of this node's public contract.  Use this to connect together data flow in the
        /// rendering part of the graph using the various methods of <see cref="NodeSet"/> which require a port.
        /// 
        /// This is the concrete static instance of the <see cref="IKernelPortDefinition"/> struct used in the
        /// declaration of a node's <see cref="NodeDefinition{TNodeData,TSimulationportDefinition,TKernelData,TKernelPortDefinition,TKernel}"/>.
        /// <seealso cref="NodeSet.Connect(NodeHandle, OutputPortID, NodeHandle, InputPortID)"/>
        /// <seealso cref="IGraphKernel{TKernelData, TKernelPortDefinition}"/>
        /// </summary>
        public static ref readonly TKernelPortDefinition KernelPorts => ref s_KernelPorts;
        internal static TKernelPortDefinition s_KernelPorts;

    }

    /// <summary>
    /// Helper class for defining a simulation-only node, with a empty simulation
    /// port definition.
    /// <seealso cref="NodeFunctionality"/>
    /// </summary>
    public class NodeDefinition<TNodeData>
        : NodeDefinition<TNodeData, NodeDefinition<TNodeData>.SimPorts>
            where TNodeData : struct, INodeData
    {
        public struct SimPorts : ISimulationPortDefinition { }
    }

    /// <summary>
    /// Helper class for defining a simulation-only node.
    /// <seealso cref="NodeFunctionality"/>
    /// </summary>
    public class NodeDefinition<TNodeData, TSimulationPortDefinition>
        : NodeFunctionality<TSimulationPortDefinition>
            where TNodeData : struct, INodeData
            where TSimulationPortDefinition : struct, ISimulationPortDefinition
    {
        protected NodeTraits<TNodeData, TSimulationPortDefinition> Traits = new NodeTraits<TNodeData, TSimulationPortDefinition>();
        public override NodeTraitsBase BaseTraits => Traits;

        /// <summary>
        /// Helper around <see cref="NodeTraits{TNodeData, TSimPorts}.GetNodeData(NodeHandle)"/>.
        /// </summary>
        protected ref TNodeData GetNodeData(NodeHandle handle) => ref Traits.GetNodeData(handle);
    }

    /// <summary>
    /// Helper class for defining a combined simulation / rendering node.
    /// <seealso cref="NodeFunctionality"/>
    /// </summary>
    public abstract class NodeDefinition<TNodeData, TSimulationportDefinition, TKernelData, TKernelPortDefinition, TKernel>
        : NodeFunctionality<TSimulationportDefinition, TKernelPortDefinition>
            where TNodeData : struct, INodeData
            where TSimulationportDefinition : struct, ISimulationPortDefinition
            where TKernelData : struct, IKernelData
            where TKernelPortDefinition : struct, IKernelPortDefinition
            where TKernel : struct, IGraphKernel<TKernelData, TKernelPortDefinition>
    {
        protected NodeTraits<TNodeData, TSimulationportDefinition, TKernelData, TKernelPortDefinition, TKernel> Traits = new NodeTraits<TNodeData, TSimulationportDefinition, TKernelData, TKernelPortDefinition, TKernel>();
        public override NodeTraitsBase BaseTraits => Traits;

        /// <summary>
        /// Helper around <see cref="NodeTraits{TNodeData, TSimPorts, TKernelData, TKernelPortDefinition, TKernel}.GetNodeData(NodeHandle)"/>.
        /// </summary>
        protected ref TNodeData GetNodeData(NodeHandle handle) => ref Traits.GetNodeData(handle);
        /// <summary>
        /// Helper around <see cref="NodeTraits{TNodeData, TSimPorts, TKernelData, TKernelPortDefinition, TKernel}.GetKernelData(NodeHandle)"/>.
        /// </summary>
        protected ref TKernelData GetKernelData(NodeHandle handle) => ref Traits.GetKernelData(handle);
    }

    /// <summary>
    /// Helper class for defining a combined simulation / rendering node, 
    /// without a simulation port definition.
    /// <seealso cref="NodeFunctionality"/>
    /// </summary>
    public abstract class NodeDefinition<TNodeData, TKernelData, TKernelPortDefinition, TKernel>
        : NodeDefinition<TNodeData, NodeDefinition<TNodeData, TKernelData, TKernelPortDefinition, TKernel>.SimPorts, TKernelData, TKernelPortDefinition, TKernel>
            where TNodeData : struct, INodeData
            where TKernelData : struct, IKernelData
            where TKernelPortDefinition : struct, IKernelPortDefinition
            where TKernel : struct, IGraphKernel<TKernelData, TKernelPortDefinition>
    {
        public struct SimPorts : ISimulationPortDefinition { }
    }
}
