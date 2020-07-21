using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using static Unity.DataFlowGraph.ReflectionTools;

namespace Unity.DataFlowGraph
{
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
        where TDefinition : NodeDefinition
    {
        // Note that definition indices start at 1 on purpose, reserving 0 as invalid
        // TODO: Consider thread safety
        internal static readonly int Index = ++NodeDefinitionCounter.Counter;
    }

    /// <summary>
    /// Base class for all node definition declarations. Provides helper
    /// functionality and base implementations around
    /// <see cref="NodeDefinition"/>.
    ///
    /// A <see cref="NodeDefinition"/> instance exists per existing
    /// <see cref="NodeSet"/>.
    /// </summary>
    public abstract class NodeDefinition
    {
        /// <summary>
        /// The <see cref="NodeSet"/> associated with this instance of this
        /// node definition.
        /// </summary>
        protected internal NodeSet Set { get; internal set; }
        internal virtual NodeTraitsBase BaseTraits { get { throw new NotImplementedException(); } }

        internal PortDescription AutoPorts;

        /// <remarks>
        /// It is undefined behaviour to throw an exception from this method.
        /// </remarks>
        protected internal virtual void OnUpdate(in UpdateContext ctx) { }
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
        protected internal virtual void Init(InitContext ctx) { }
        /// <summary>
        /// Destructor, provides an opportunity to clean up resources related to
        /// this instance.
        /// <seealso cref="NodeSet.Destroy(NodeHandle)"/>
        /// </summary>
        /// <remarks>
        /// It is undefined behaviour to throw an exception from this method.
        /// </remarks>
        protected internal virtual void Destroy(DestroyContext ctx) { }
        /// <summary>
        /// Called when disposing a <see cref="NodeSet"/>.
        /// <seealso cref="NodeSet.Dispose()"/>
        /// </summary>
        protected internal virtual void Dispose() { }

        /// <summary>
        /// Returns whether this node type's input and output ports are always fixed (see <see cref="GetStaticPortDescription()"/>).
        /// </summary>
        public bool HasStaticPortDescription => true;

        /// <summary>
        /// Retrieve the static type information about this node type's input and output ports (see <see cref="PortDescription"/>).
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown if this node type does not have a static port description (<see cref="HasStaticPortDescription"/>)
        /// </exception>
        public PortDescription GetStaticPortDescription()
        {
            if (!HasStaticPortDescription)
                throw new InvalidOperationException("Node type does not have a static port description");
            return GetPortDescriptionInternal(new ValidatedHandle());
        }

        /// <summary>
        /// Retrieve the runtime type information about the given node's input and output ports (see <see cref="PortDescription"/>.
        /// </summary>
        public PortDescription GetPortDescription(NodeHandle handle)
        {
            var validated = Set.Validate(handle);
            if (Set.GetDefinitionInternal(validated) != this)
                throw new ArgumentException("Node type does not correspond to this NodeDefinition");
            return GetPortDescriptionInternal(validated);
        }

        /// <summary>
        /// Indexer for getting a "virtual" input port description. A virtual port may not formally
        /// exist, but a description of it can still be retrieved.
        /// <seealso cref="GetFormalInput(ValidatedHandle, InputPortArrayID)"/>
        /// </summary>
        internal virtual PortDescription.InputPort GetVirtualInput(ValidatedHandle handle, InputPortArrayID id)
            => GetFormalInput(handle, id);

        /// <summary>
        /// Indexer for getting a "virtual" port description. A virtual port may not formally
        /// exist, but a description of it can still be retrieved.
        /// <seealso cref="GetFormalOutput(ValidatedHandle, InputPortArrayID)"/>
        /// /// </summary>
        internal virtual PortDescription.OutputPort GetVirtualOutput(ValidatedHandle handle, OutputPortArrayID id)
            => GetFormalOutput(handle, id);

        /// <summary>
        /// Indexer for getting a description given an <see cref="InputPortArrayID"/>.
        /// </summary>
        internal virtual PortDescription.InputPort GetFormalInput(ValidatedHandle handle, InputPortArrayID id)
            => AutoPorts.Inputs[id.PortID.Port];

        /// <summary>
        /// Indexer for getting a description given an <see cref="OutputPortID"/>.
        /// </summary>
        internal virtual PortDescription.OutputPort GetFormalOutput(ValidatedHandle handle, OutputPortArrayID id)
            => AutoPorts.Outputs[id.PortID.Port];

        internal void OnMessage<T>(in MessageContext ctx, in T msg)
        {
            if (this is IMsgHandler<T> specificHandler)
            {
                specificHandler.HandleMessage(ctx, msg);
            }
            else
                throw new InvalidOperationException("Node cannot handle messages of type " + typeof(T).Name);
        }

        /// <summary>
        /// Used to return the entire port description of a node.
        /// Any entry must be valid for <see cref="GetFormalInput(ValidatedHandle, InputPortArrayID)"/>
        /// or <see cref="GetFormalOutput(ValidatedHandle, OutputPortID)"/>.
        /// </summary>
        internal virtual PortDescription GetPortDescriptionInternal(ValidatedHandle handle) => AutoPorts;

        internal void GeneratePortDescriptions()
        {
            var ports = new PortDescription();
            ports.Inputs = new List<PortDescription.InputPort>();
            ports.Outputs = new List<PortDescription.OutputPort>();
            ports.ComponentTypes = new List<ComponentType>();

            var type = GetType();
            var fields = type.GetFields(BindingFlags.Static | BindingFlags.FlattenHierarchy | BindingFlags.Public);

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

            Set.RegisterECSPorts(ports);
        }

        /// <summary>
        /// Emit a message from yourself on a port. Everything connected to it
        /// will receive your message.
        /// </summary>
        [Obsolete("Functionality moved to MessageContext/UpdateContext.EmitMessage")]
        protected void EmitMessage<T, TNodeDefinition>(NodeHandle from, MessageOutput<TNodeDefinition, T> port, in T msg)
            where TNodeDefinition : NodeDefinition
        {
            Set.EmitMessage(Set.Validate(from), new OutputPortArrayID(port.Port), msg);
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

                if (isSimulation)
                {
                    if (genericField == typeof(MessageInput<,>))
                    {
                        description.Inputs.Add(PortDescription.InputPort.Message(genericType, (ushort)description.Inputs.Count, isPortArray, fieldInfo.Name));
                    }
                    else if (genericField == typeof(MessageOutput<,>))
                    {
                        description.Outputs.Add(PortDescription.OutputPort.Message(genericType, (ushort)description.Outputs.Count, isPortArray, fieldInfo.Name));
                    }
                    else if (genericField == typeof(DSLInput<,,>))
                    {
                        description.Inputs.Add(PortDescription.InputPort.DSL(genericType, (ushort)description.Inputs.Count, fieldInfo.Name));
                    }
                    else if (genericField == typeof(DSLOutput<,,>))
                    {
                        description.Outputs.Add(PortDescription.OutputPort.DSL(genericType, (ushort)description.Outputs.Count, fieldInfo.Name));
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
                        bool hasBuffers = true;

                        if(!IsBufferDefinition(genericType))
                        {
                            hasBuffers = WalkTypeInstanceFields(genericType, BindingFlags.Public, IsBufferDefinition).Any();

                            if (!hasBuffers && typeof(IComponentData).IsAssignableFrom(genericType))
                            {
                                description.ComponentTypes.Add(new ComponentType(genericType, ComponentType.AccessMode.ReadOnly));
                            }
                        }
                        else
                        {
                            var bufferType = genericType.GetGenericArguments()[0];

                            if (typeof(IBufferElementData).IsAssignableFrom(bufferType))
                            {
                                description.ComponentTypes.Add(new ComponentType(bufferType, ComponentType.AccessMode.ReadOnly));
                            }
                        }

                        description.Inputs.Add(
                            PortDescription.InputPort.Data(
                                genericType,
                                (ushort)description.Inputs.Count,
                                hasBuffers,
                                isPortArray,
                                fieldInfo.Name
                            )
                        );
                    }
                    else if (genericField == typeof(DataOutput<,>))
                    {
                        var bufferInfos = new List<(int Offset, SimpleType ItemType)>();

                        if (!IsBufferDefinition(genericType))
                        {
                            var recursiveBuffers = WalkTypeInstanceFields(genericType, BindingFlags.Public, IsBufferDefinition)
                                .Select(fi => (UnsafeUtility.GetFieldOffset(fi), new SimpleType(fi.FieldType.GetGenericArguments()[0])));

                            bufferInfos.AddRange(recursiveBuffers);

                            if (bufferInfos.Count == 0 && typeof(IComponentData).IsAssignableFrom(genericType))
                            {
                                description.ComponentTypes.Add(new ComponentType(genericType, ComponentType.AccessMode.ReadWrite));
                            }
                        }
                        else
                        {
                            var bufferType = genericType.GetGenericArguments()[0];

                            bufferInfos.Add((0, new SimpleType(bufferType)));

                            if (typeof(IBufferElementData).IsAssignableFrom(bufferType))
                            {
                                description.ComponentTypes.Add(new ComponentType(bufferType, ComponentType.AccessMode.ReadWrite));
                            }
                        }

                        description.Outputs.Add(
                            PortDescription.OutputPort.Data(
                                genericType,
                                (ushort)description.Outputs.Count,
                                bufferInfos,
                                fieldInfo.Name
                            )
                        );
                    }
                    else
                    {
                        throw new InvalidNodeDefinitionException($"Kernel port definition contains disallowed type {genericField}.");
                    }
                }

                // The first generic for Message/Data/DSLInput/Output is the NodeDefinition class
                if (generics[0] != nodeType)
                    throw new InvalidNodeDefinitionException($"Port definition references incorrect NodeDefinition class {generics[0]}");
            }
        }
    }

    /// <summary>
    /// Extra PortDefinition interface synthesized onto all <see cref="ISimulationPortDefinition"/> and
    /// <see cref="IKernelPortDefinition"/> types during ILPP to offer a way to initialize those user defined types.
    /// </summary>
    interface IPortDefinitionInitializer
    {
        void DFG_CG_Initialize(ushort uniqueInputPort, ushort uniqueOutputPort);
        ushort DFG_CG_GetInputPortCount();
        ushort DFG_CG_GetOutputPortCount();
    }

    static class PortInitUtility
    {
        public static TPortDefinition GetInitializedPortDef<TPortDefinition>()
            where TPortDefinition : struct
        {
            // This body is replaced with the contents of GetInitializedPortDefImp during ILPP.
            throw new NotImplementedException();
        }

        internal static TPortDefinition GetInitializedPortDefImp<TPortDefinition>()
            where TPortDefinition : struct, IPortDefinitionInitializer
        {
            var ret = default(TPortDefinition);
            ret.DFG_CG_Initialize(0, 0);
            return ret;
        }

        public static TPortDefinition GetInitializedPortDef<TPortDefinition, TOtherPortDefinition>()
            where TPortDefinition : struct
            where TOtherPortDefinition : struct
        {
            // This body is replaced with the contents of GetInitializedPortDefImp during ILPP.
            throw new NotImplementedException();
        }

        internal static TPortDefinition GetInitializedPortDefImp<TPortDefinition, TOtherPortDefinition>()
            where TPortDefinition : struct, IPortDefinitionInitializer
            where TOtherPortDefinition : struct, IPortDefinitionInitializer
        {
            var ret = default(TPortDefinition);
            ret.DFG_CG_Initialize(default(TOtherPortDefinition).DFG_CG_GetInputPortCount(), default(TOtherPortDefinition).DFG_CG_GetOutputPortCount());
            return ret;
        }
    }

    /// <summary>
    /// Base class for a simulation-only node.
    /// <seealso cref="NodeDefinition"/>
    /// </summary>
    public abstract class SimulationNodeDefinition<TSimulationPortDefinition>
        : NodeDefinition
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
        public static readonly TSimulationPortDefinition SimulationPorts =
            PortInitUtility.GetInitializedPortDef<TSimulationPortDefinition>();
    }

    /// <summary>
    /// Base class for a rendering-only node.
    /// <seealso cref="NodeDefinition"/>
    /// </summary>
    public abstract class KernelNodeDefinition<TKernelPortDefinition>
        : NodeDefinition
            where TKernelPortDefinition : struct, IKernelPortDefinition
    {
        /// <summary>
        /// The kernel port definition of this node's public contract.  Use this to connect together data flow in the
        /// rendering part of the graph using the various methods of <see cref="NodeSet"/> which require a port.
        ///
        /// This is the concrete static instance of the <see cref="IKernelPortDefinition"/> struct used in the
        /// declaration of a node's <see cref="NodeDefinition{TKernelData,TKernelPortDefinition,TKernel}"/>.
        /// <seealso cref="NodeSet.Connect(NodeHandle, OutputPortID, NodeHandle, InputPortID)"/>
        /// <seealso cref="IGraphKernel{TKernelData, TKernelPortDefinition}"/>
        /// </summary>
        public static readonly TKernelPortDefinition KernelPorts =
            PortInitUtility.GetInitializedPortDef<TKernelPortDefinition>();
    }

    /// <summary>
    /// Base class for a combined simulation / rendering node.
    /// <seealso cref="NodeDefinition"/>
    /// </summary>
    public abstract class SimulationKernelNodeDefinition<TSimulationPortDefinition, TKernelPortDefinition>
        : SimulationNodeDefinition<TSimulationPortDefinition>
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
        public static readonly TKernelPortDefinition KernelPorts =
            PortInitUtility.GetInitializedPortDef<TKernelPortDefinition, TSimulationPortDefinition>();
    }

    /// <summary>
    /// Helper class for defining a simulation-only node, with no simulation node data.
    /// <seealso cref="NodeDefinition"/>
    /// </summary>
    public abstract class NodeDefinition<TSimulationPortDefinition>
        : SimulationNodeDefinition<TSimulationPortDefinition>
            where TSimulationPortDefinition : struct, ISimulationPortDefinition
    {
    }

    /// <summary>
    /// Helper class for defining a simulation-only node.
    /// <seealso cref="NodeDefinition"/>
    /// </summary>
    public abstract class NodeDefinition<TNodeData, TSimulationPortDefinition>
        : SimulationNodeDefinition<TSimulationPortDefinition>
            where TNodeData : struct, INodeData
            where TSimulationPortDefinition : struct, ISimulationPortDefinition
    {
        /// <summary>
        /// Helper around <see cref="NodeTraits{TNodeData, TSimPorts}.GetNodeData(NodeHandle)"/>.
        /// </summary>
        protected ref TNodeData GetNodeData(NodeHandle handle) => ref Set.GetNodeData<TNodeData>(handle);
    }

    /// <summary>
    /// Helper class for defining a rendering-only node.
    /// <seealso cref="NodeDefinition"/>
    /// </summary>
    public abstract class NodeDefinition<TKernelData, TKernelPortDefinition, TKernel>
        : KernelNodeDefinition<TKernelPortDefinition>
            where TKernelData : struct, IKernelData
            where TKernelPortDefinition : struct, IKernelPortDefinition
            where TKernel : struct, IGraphKernel<TKernelData, TKernelPortDefinition>
    {
        /// <summary>
        /// Helper around <see cref="NodeTraits{TKernelData, TKernelPortDefinition, TKernel}.GetKernelData(NodeHandle)"/>.
        /// </summary>
        protected ref TKernelData GetKernelData(NodeHandle handle) => ref Set.GetKernelData<TKernelData>(handle);
    }

    /// <summary>
    /// Helper class for defining a combined simulation / rendering node.
    /// <seealso cref="NodeDefinition"/>
    /// </summary>
    public abstract class NodeDefinition<TNodeData, TSimulationPortDefinition, TKernelData, TKernelPortDefinition, TKernel>
        : SimulationKernelNodeDefinition<TSimulationPortDefinition, TKernelPortDefinition>
            where TNodeData : struct, INodeData
            where TSimulationPortDefinition : struct, ISimulationPortDefinition
            where TKernelData : struct, IKernelData
            where TKernelPortDefinition : struct, IKernelPortDefinition
            where TKernel : struct, IGraphKernel<TKernelData, TKernelPortDefinition>
    {
        /// <summary>
        /// Helper around <see cref="NodeTraits{TNodeData, TKernelData, TKernelPortDefinition, TKernel}.GetNodeData(NodeHandle)"/>.
        /// </summary>
        protected ref TNodeData GetNodeData(NodeHandle handle) => ref Set.GetNodeData<TNodeData>(handle);

        /// <summary>
        /// Helper around <see cref="NodeTraits{TNodeData, TKernelData, TKernelPortDefinition, TKernel}.GetKernelData(NodeHandle)"/>.
        /// </summary>
        protected ref TKernelData GetKernelData(NodeHandle handle) => ref Set.GetKernelData<TKernelData>(handle);
    }

    /// <summary>
    /// Helper class for defining a combined simulation / rendering node,
    /// without a simulation port definition.
    /// <seealso cref="NodeDefinition"/>
    /// </summary>
    public abstract class NodeDefinition<TNodeData, TKernelData, TKernelPortDefinition, TKernel>
        : KernelNodeDefinition<TKernelPortDefinition>
            where TNodeData : struct, INodeData
            where TKernelData : struct, IKernelData
            where TKernelPortDefinition : struct, IKernelPortDefinition
            where TKernel : struct, IGraphKernel<TKernelData, TKernelPortDefinition>
    {
        /// <summary>
        /// Helper around <see cref="NodeTraits{TNodeData, TKernelData, TKernelPortDefinition, TKernel}.GetNodeData(NodeHandle)"/>.
        /// </summary>
        protected ref TNodeData GetNodeData(NodeHandle handle) => ref Set.GetNodeData<TNodeData>(handle);

        /// <summary>
        /// Helper around <see cref="NodeTraits{TNodeData, TKernelData, TKernelPortDefinition, TKernel}.GetKernelData(NodeHandle)"/>.
        /// </summary>
        protected ref TKernelData GetKernelData(NodeHandle handle) => ref Set.GetKernelData<TKernelData>(handle);
    }
}
