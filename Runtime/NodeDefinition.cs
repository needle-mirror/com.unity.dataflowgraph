using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using static Unity.DataFlowGraph.ReflectionTools;
using Unity.Collections;

namespace Unity.DataFlowGraph
{
    /// <summary>
    /// Use this to tag your <see cref="INodeData"/> as being managed,
    /// meaning it will be possible to store non-blittable
    /// data on the type (like references).
    /// </summary>
    public sealed class ManagedAttribute : Attribute { }

    /// <summary>
    /// Use this attribute on a <see cref="IGraphKernel{TKernelData, TKernelPortDefinition}"/> implementation
    /// to express that it has to be executed since it could cause invisible side effects.
    /// <seealso cref="NodeSet.RenderOptimizations"/>
    /// </summary>
    [AttributeUsage(AttributeTargets.Struct)]
    public class CausesSideEffectsAttribute : Attribute { }

    /// <summary>
    /// Interface tag to be implemented on a struct, that will contain the simulation-side contents of your node's instance
    /// data. The same struct should also carry any of the desired <seealso cref="IInit"/>, <seealso cref="IDestroy"/>,
    /// <seealso cref="IUpdate"/> and/or <seealso cref="IMsgHandler{TMsg}"/> implementations that the node would like handlers for.
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
        /// Structure containing virtual (managed) behaviour for the simulation system
        /// </summary>
        internal struct SimulationVTable
        {
            internal MessageDelivery.Prototype[] MessageHandlers;
            internal InitDelivery.Prototype InitHandler;
            internal DestroyDelivery.Prototype DestroyHandler;
            internal UpdateDelivery.Prototype UpdateHandler;

            void InstallMessageHandlerInternal(MessageDelivery.Prototype dlg, ushort port)
            {
                if (MessageHandlers == null)
                    MessageHandlers = new MessageDelivery.Prototype[port + 1];

                if (port >= MessageHandlers.Length)
                {
                    var temp = new MessageDelivery.Prototype[1 + port + port / 2];
                    for (int i = 0; i < MessageHandlers.Length; ++i)
                        temp[i] = MessageHandlers[i];

                    MessageHandlers = temp;
                }

                MessageHandlers[port] = dlg;
            }

            public void InstallMessageHandler<TNodeDefinition, TNodeData, TMessageData>(MessageInput<TNodeDefinition, TMessageData> port)
                where TNodeDefinition : NodeDefinition
                where TNodeData : struct, INodeData, IMsgHandler<TMessageData>
                where TMessageData : struct
            {
                InstallMessageHandlerInternal(Trampolines<TNodeDefinition>.HandleMessage<TMessageData, TNodeData>, port.Port.Port.CategoryCounter);
            }

            public void InstallPortArrayMessageHandler<TNodeDefinition, TNodeData, TMessageData>(PortArray<MessageInput<TNodeDefinition, TMessageData>> port)
                where TNodeDefinition : NodeDefinition
                where TNodeData : struct, INodeData, IMsgHandler<TMessageData>
                where TMessageData : struct
            {
                InstallMessageHandlerInternal(Trampolines<TNodeDefinition>.HandleMessage<TMessageData, TNodeData>, port.GetPortID().Port.CategoryCounter);
            }

            public void InstallMessageHandlerGeneric<TNodeDefinition, TNodeData, TMessageData>(MessageInput<TNodeDefinition, TMessageData> port)
                where TNodeDefinition : NodeDefinition
                where TNodeData : struct, INodeData, IMsgHandlerGeneric<TMessageData>
                where TMessageData : struct
            {
                InstallMessageHandlerInternal(Trampolines<TNodeDefinition>.HandleMessageGeneric<TMessageData, TNodeData>, port.Port.Port.CategoryCounter);
            }

            public void InstallPortArrayMessageHandlerGeneric<TNodeDefinition, TNodeData, TMessageData>(PortArray<MessageInput<TNodeDefinition, TMessageData>> port)
                where TNodeDefinition : NodeDefinition
                where TNodeData : struct, INodeData, IMsgHandlerGeneric<TMessageData>
                where TMessageData : struct
            {
                InstallMessageHandlerInternal(Trampolines<TNodeDefinition>.HandleMessageGeneric<TMessageData, TNodeData>, port.GetPortID().Port.CategoryCounter);
            }

            public void InstallDestroyHandler<TNodeDefinition, TNodeData>()
                where TNodeDefinition : NodeDefinition
                where TNodeData : struct, INodeData, IDestroy
            {
                DestroyHandler = Trampolines<TNodeDefinition>.HandleDestroy<TNodeData>;
            }

            public void InstallUpdateHandler<TNodeDefinition, TNodeData>()
                where TNodeDefinition : NodeDefinition
                where TNodeData : struct, INodeData, IUpdate
            {
                UpdateHandler = Trampolines<TNodeDefinition>.HandleUpdate<TNodeData>;
            }

            public void InstallInitHandler<TNodeDefinition, TNodeData>()
                where TNodeDefinition : NodeDefinition
                where TNodeData : struct, INodeData, IInit
            {
                InitHandler = Trampolines<TNodeDefinition>.HandleInit<TNodeData>;
            }

            internal MessageDelivery.Prototype GetMessageHandler(InputPortID port)
            {
                if (MessageHandlers == null || port.Port.CategoryCounter >= MessageHandlers.Length)
                    return null;

                return MessageHandlers[port.Port.CategoryCounter];
            }
        }

        internal SimulationVTable VirtualTable;

        /// <summary>
        /// The <see cref="NodeSetAPI"/> associated with this instance of this
        /// node definition.
        /// </summary>
        protected internal NodeSetAPI Set { get; internal set; }
        internal virtual NodeTraitsBase BaseTraits { get { throw new NotImplementedException(); } }
        internal virtual SimulationStorageDefinition SimulationStorageTraits { get { return SimulationStorageDefinition.Empty; } }
        internal virtual KernelStorageDefinition KernelStorageTraits { get { return default; } }

        internal PortDescription AutoPorts, PublicPorts;
        internal IndexablePortDescription PortMap;

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
            var validated = Set.Nodes.Validate(handle.VHandle);
            if (Set.GetDefinitionInternal(validated) != this)
                throw new ArgumentException("Node type does not correspond to this NodeDefinition");
            return GetPortDescriptionInternal(validated);
        }

        unsafe internal void UpdateInternal(in UpdateContext context)
        {

#if DFG_ASSERTIONS
            if (VirtualTable.UpdateHandler == null)
                throw new AssertionException("Updating node that can't be updated");
#endif

            UpdateDelivery delivery;
            delivery.Context = context;
            delivery.Node = Set.GetNodeDataRaw(delivery.Context.InternalHandle());
            VirtualTable.UpdateHandler(delivery);
        }

        unsafe internal void DestroyInternal(DestroyContext context)
        {
            if(VirtualTable.DestroyHandler != null)
            {
                DestroyDelivery delivery;
                delivery.Context = context;
                delivery.Node = Set.GetNodeDataRaw(context.m_Handle);
                VirtualTable.DestroyHandler(delivery);
            }
        }

        unsafe internal void InitInternal(InitContext context)
        {
            if(VirtualTable.InitHandler != null)
            {
                InitDelivery delivery;
                delivery.Context = context;
                delivery.Node = Set.GetNodeDataRaw(context.InternalHandle());
                VirtualTable.InitHandler(delivery);
            }
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
            => PortMap.Lookup(id.PortID);

        /// <summary>
        /// Indexer for getting a description given an <see cref="OutputPortID"/>.
        /// </summary>
        internal virtual PortDescription.OutputPort GetFormalOutput(ValidatedHandle handle, OutputPortArrayID id)
            => PortMap.Lookup(id.PortID);

        unsafe internal void OnMessage<T>(in MessageContext ctx, in T msg)
        {
            var nodeDataHandler = VirtualTable.GetMessageHandler(ctx.Port);

            if (nodeDataHandler != null)
            {
                MessageDelivery d;
                d.Context = ctx;
                d.Node = Set.GetNodeDataRaw(d.Context.InternalHandle());
                d.Message = Utility.AddressOfEvenIfManaged(msg);
                nodeDataHandler(d);
            }
            else
                throw new InvalidOperationException("Node cannot handle messages of type " + typeof(T).Name);
        }

        /// <summary>
        /// Used to return the entire port description of a node.
        /// Any entry must be valid for <see cref="GetFormalInput(ValidatedHandle, InputPortArrayID)"/>
        /// or <see cref="GetFormalOutput(ValidatedHandle, OutputPortID)"/>.
        /// </summary>
        internal virtual PortDescription GetPortDescriptionInternal(ValidatedHandle handle) => PublicPorts;

        internal void GeneratePortDescriptions()
        {
            AutoPorts.Inputs = new List<PortDescription.InputPort>();
            AutoPorts.Outputs = new List<PortDescription.OutputPort>();
            AutoPorts.ComponentTypes = new List<ComponentType>();

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
                ParsePortDefinition(simulationPortDefinition, AutoPorts, type, true);

            if (kernelPortDefinition != null)
                ParsePortDefinition(kernelPortDefinition, AutoPorts, type, false);

            PublicPorts.Inputs = AutoPorts.Inputs.Where(p => p.IsPublic).ToList();
            PublicPorts.Outputs = AutoPorts.Outputs.Where(p => p.IsPublic).ToList();

            PortMap = new IndexablePortDescription(AutoPorts);
        }

        /// <summary>
        /// Emit a message from yourself on a port. Everything connected to it
        /// will receive your message.
        /// </summary>
        [Obsolete("Functionality moved to MessageContext/UpdateContext.EmitMessage", true)]
        protected void EmitMessage<T, TNodeDefinition>(NodeHandle from, MessageOutput<TNodeDefinition, T> port, in T msg)
            where TNodeDefinition : NodeDefinition
        {
            Set.EmitMessage(Set.Nodes.Validate(from.VHandle), new OutputPortArrayID(port.Port), msg);
        }

        static void ParsePortDefinition(FieldInfo staticTopLevelField, PortDescription description, Type nodeType, bool isSimulation)
        {
            ushort dataInputCounter = default;
            ushort dataOutputCounter = default;
            ushort msgInputCounter = default;
            ushort msgOutputCounter = default;
            ushort dslInputCounter = default;
            ushort dslOutputCounter = default;

            var qualifiedFieldType = staticTopLevelField.FieldType;
            var topLevelFieldValue = staticTopLevelField.GetValue(null);

            var definitionFields = qualifiedFieldType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var disallowedFieldTypes =
                qualifiedFieldType.GetFields(BindingFlags.Static | BindingFlags.Public)
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
                        description.Inputs.Add(PortDescription.InputPort.Message(genericType, new InputPortID(new PortStorage(msgInputCounter++, PortStorage.Category.Message)), isPortArray, fieldInfo.IsPublic, fieldInfo.Name));
                    }
                    else if (genericField == typeof(MessageOutput<,>))
                    {
                        description.Outputs.Add(PortDescription.OutputPort.Message(genericType, new OutputPortID(new PortStorage(msgOutputCounter++, PortStorage.Category.Message)), isPortArray, fieldInfo.IsPublic, fieldInfo.Name));
                    }
                    else if (genericField == typeof(DSLInput<,,>))
                    {
                        description.Inputs.Add(PortDescription.InputPort.DSL(genericType, new InputPortID(new PortStorage(dslInputCounter++, PortStorage.Category.DSL)), fieldInfo.IsPublic, fieldInfo.Name));
                    }
                    else if (genericField == typeof(DSLOutput<,,>))
                    {
                        description.Outputs.Add(PortDescription.OutputPort.DSL(genericType, new OutputPortID(new PortStorage(dslOutputCounter++, PortStorage.Category.DSL)), fieldInfo.IsPublic, fieldInfo.Name));
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
                                new InputPortID(new PortStorage(dataInputCounter++, PortStorage.Category.Data)),
                                hasBuffers,
                                isPortArray,
                                fieldInfo.IsPublic,
                                fieldInfo.Name
                            )
                        );
                    }
                    else if (genericField == typeof(DataOutput<,>))
                    {
                        var bufferInfos = new List<PortDescription.OutputPort.BufferEntryInsidePort>();

                        if (!IsBufferDefinition(genericType))
                        {
                            ushort numBuffers = 0;
                            var recursiveBuffers = WalkTypeInstanceFields(genericType, BindingFlags.Public, IsBufferDefinition)
                                .Select(fi => new PortDescription.OutputPort.BufferEntryInsidePort(numBuffers++, UnsafeUtility.GetFieldOffset(fi), new SimpleType(fi.FieldType.GetGenericArguments()[0])));

                            bufferInfos.AddRange(recursiveBuffers);

                            if (bufferInfos.Count == 0 && typeof(IComponentData).IsAssignableFrom(genericType))
                            {
                                description.ComponentTypes.Add(new ComponentType(genericType, ComponentType.AccessMode.ReadWrite));
                            }
                        }
                        else
                        {
                            var bufferType = genericType.GetGenericArguments()[0];

                            bufferInfos.Add(new PortDescription.OutputPort.BufferEntryInsidePort(0, 0, new SimpleType(bufferType)));

                            if (typeof(IBufferElementData).IsAssignableFrom(bufferType))
                            {
                                description.ComponentTypes.Add(new ComponentType(bufferType, ComponentType.AccessMode.ReadWrite));
                            }
                        }

                        description.Outputs.Add(
                            PortDescription.OutputPort.Data(
                                genericType,
                                new OutputPortID(new PortStorage(dataOutputCounter++, PortStorage.Category.Data)),
                                bufferInfos,
                                isPortArray,
                                fieldInfo.IsPublic,
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

    public partial class NodeSetAPI
    {
        /// <summary>
        /// Returns whether this node type's input and output ports are always fixed (see <see cref="GetStaticPortDescription()"/>).
        /// </summary>
        public bool HasStaticPortDescription<TDefinition>()
            where TDefinition : NodeDefinition, new()
                => true;

        /// <summary>
        /// Returns whether this node type's input and output ports are always fixed (see <see cref="GetStaticPortDescription()"/>).
        /// </summary>
        public bool HasStaticPortDescription(NodeHandle handle)
            => true;

        /// <summary>
        /// Retrieve the static type information about this node type's input and output ports (see <see cref="PortDescription"/>).
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown if this node type does not have a static port description (<see cref="HasStaticPortDescription{TDefinition}"/>)
        /// </exception>
        public PortDescription GetStaticPortDescription<TDefinition>()
            where TDefinition : NodeDefinition, new()
        {
            if (!HasStaticPortDescription<TDefinition>())
                throw new InvalidOperationException("Node type does not have a static port description");

            return GetDefinition<TDefinition>().GetPortDescriptionInternal(new ValidatedHandle());
        }

        /// <summary>
        /// Retrieve the static type information about this node type's input and output ports (see <see cref="PortDescription"/>).
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown if this node type does not have a static port description (<see cref="HasStaticPortDescription"/>)
        /// </exception>
        public PortDescription GetStaticPortDescription(NodeHandle handle)
        {
            if (!HasStaticPortDescription(handle))
                throw new InvalidOperationException("Node type does not have a static port description");

            return GetDefinition(handle).GetPortDescriptionInternal(new ValidatedHandle());
        }

        /// <summary>
        /// Retrieve the runtime type information about the given node's input and output ports (see <see cref="PortDescription"/>.
        /// </summary>
        public PortDescription GetPortDescription(NodeHandle handle)
        {
            var validated = Nodes.Validate(handle.VHandle);
            return GetDefinitionInternal(validated).GetPortDescriptionInternal(validated);
        }
    }

    /// <summary>
    /// Extra PortDefinition interface synthesized onto all <see cref="ISimulationPortDefinition"/> and
    /// <see cref="IKernelPortDefinition"/> types during ILPP to offer a way to initialize those user defined types.
    /// </summary>
    interface IPortDefinitionInitializer
    {
        void DFG_CG_Initialize();
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
            ret.DFG_CG_Initialize();
            return ret;
        }
    }

    /// <summary>
    /// Base class for a simulation-only node. The presence of an <see cref="INodeData"/> struct as a nested type in derived
    /// classes will automatically be discovered and used to hold simulation-side node data and declare the simulation handlers
    /// for the node.
    /// <seealso cref="NodeDefinition"/>
    /// </summary>
    public abstract class SimulationNodeDefinition<TSimulationPortDefinition>
        : NodeDefinition
            where TSimulationPortDefinition : struct, ISimulationPortDefinition
    {
        /// <summary>
        /// The simulation port definition of this node's public contract. Use this to connect together messages and
        /// DSLs between nodes using the various methods of <see cref="NodeSetAPI"/> which require a port.
        ///
        /// This is the concrete static instance of the <see cref="ISimulationPortDefinition"/> struct used in the
        /// declaration of a node's <see cref="NodeDefinition{TNodeData,TSimulationPortDefinition}"/> or other variant.
        /// <seealso cref="NodeSetAPI.Connect(NodeHandle, OutputPortID, NodeHandle, InputPortID)"/>
        /// </summary>
        public static readonly TSimulationPortDefinition SimulationPorts = PortInitUtility.GetInitializedPortDef<TSimulationPortDefinition>();
    }

    /// <summary>
    /// Base class for a rendering-only node. The presence of <see cref="IKernelData"/> and <see cref="IGraphKernel"/> structs
    /// as nested types in derived classes is mandatory and will automatically be discovered and used to hold rendering-side
    /// node data and declare the rendering function.
    /// <seealso cref="NodeDefinition"/>
    /// </summary>
    public abstract class KernelNodeDefinition<TKernelPortDefinition>
        : NodeDefinition
            where TKernelPortDefinition : struct, IKernelPortDefinition
    {
        /// <summary>
        /// The kernel port definition of this node's public contract.  Use this to connect together data flow in the
        /// rendering part of the graph using the various methods of <see cref="NodeSetAPI"/> which require a port.
        ///
        /// This is the concrete static instance of the <see cref="IKernelPortDefinition"/> struct used in the
        /// declaration of a node's <see cref="NodeDefinition{TKernelData,TKernelPortDefinition,TKernel}"/>.
        /// <seealso cref="NodeSetAPI.Connect(NodeHandle, OutputPortID, NodeHandle, InputPortID)"/>
        /// <seealso cref="IGraphKernel{TKernelData, TKernelPortDefinition}"/>
        /// </summary>
        public static readonly TKernelPortDefinition KernelPorts = PortInitUtility.GetInitializedPortDef<TKernelPortDefinition>();
    }

    /// <summary>
    /// Base class for a combined simulation / rendering node. The presence of <see cref="IKernelData"/> and <see cref="IGraphKernel"/>
    /// structs as nested types in derived classes is mandatory and will automatically be discovered and used to hold rendering-side
    /// node data and declare the rendering function. The presence of an <see cref="INodeData"/> struct as a nested type in derived
    /// classes will automatically be discovered and used to hold simulation-side node data and declare the simulation handlers
    /// for the node.
    /// <seealso cref="NodeDefinition"/>
    /// </summary>
    public abstract class SimulationKernelNodeDefinition<TSimulationPortDefinition, TKernelPortDefinition>
        : SimulationNodeDefinition<TSimulationPortDefinition>
            where TSimulationPortDefinition : struct, ISimulationPortDefinition
            where TKernelPortDefinition : struct, IKernelPortDefinition
    {
        /// <summary>
        /// The kernel port definition of this node's public contract.  Use this to connect together data flow in the
        /// rendering part of the graph using the various methods of <see cref="NodeSetAPI"/> which require a port.
        ///
        /// This is the concrete static instance of the <see cref="IKernelPortDefinition"/> struct used in the
        /// declaration of a node's <see cref="NodeDefinition{TNodeData,TSimulationportDefinition,TKernelData,TKernelPortDefinition,TKernel}"/>.
        /// <seealso cref="NodeSetAPI.Connect(NodeHandle, OutputPortID, NodeHandle, InputPortID)"/>
        /// <seealso cref="IGraphKernel{TKernelData, TKernelPortDefinition}"/>
        /// </summary>
        public static readonly TKernelPortDefinition KernelPorts = PortInitUtility.GetInitializedPortDef<TKernelPortDefinition>();
    }
}
