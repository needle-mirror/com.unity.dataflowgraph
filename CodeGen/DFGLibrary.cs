using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Rocks;
using Unity.Burst;

namespace Unity.DataFlowGraph.CodeGen
{
    /// <summary>
    /// Local imports / rules of commonly used things from the main
    /// data flow graph assembly
    /// </summary>
    class DFGLibrary : ASTProcessor
    {
        public const MethodAttributes MethodProtectedInternalVirtualFlags = MethodAttributes.FamORAssem | MethodAttributes.NewSlot | MethodAttributes.Virtual | MethodAttributes.HideBySig;
        public const MethodAttributes MethodProtectedInternalOverrideFlags = MethodAttributes.FamORAssem | MethodAttributes.ReuseSlot | MethodAttributes.Virtual | MethodAttributes.HideBySig;
        public const MethodAttributes MethodProtectedOverrideFlags = MethodAttributes.Family | MethodAttributes.ReuseSlot | MethodAttributes.Virtual | MethodAttributes.HideBySig;
        public const MethodAttributes MethodPublicFinalFlags = MethodAttributes.Public | MethodAttributes.Final | MethodAttributes.NewSlot | MethodAttributes.Virtual | MethodAttributes.HideBySig;

        public enum NodeDefinitionKind
        {
            /// <summary>
            /// <code>typeof(SimulationNodeDefinition<></code>
            /// </summary>
            Simulation,
            /// <summary>
            /// <code>typeof(KernelNodeDefinition<></code>
            /// </summary>
            Kernel,
            /// <summary>
            /// <code>typeof(SimulationKernelNodeDefinition<></code>
            /// </summary>
            SimulationKernel,
            Naked
        }

        public enum NodeTraitsKind
        {
            _1, _2, _3, _4, _5
        }

        public enum PortClass
        {
            MessageInput,
            MessageOutput,
            DataInput,
            DataOutput,
            DSLInput,
            DSLOutput
        }

        class DummyNode : NodeDefinition, IMsgHandler<object>
        {
            public struct SimPorts : ISimulationPortDefinition { }
            public struct KernelPorts : IKernelPortDefinition { }

            public void HandleMessage(MessageContext ctx, in object msg) =>
                throw new NotImplementedException();
        }

        class DummyDSL : DSLHandler<object>
        {
            protected override void Connect(ConnectionInfo left, ConnectionInfo right) =>
                throw new NotImplementedException();
            protected override void Disconnect(ConnectionInfo left, ConnectionInfo right) =>
                throw new NotImplementedException();
        }

        public struct PortType
        {
            public TypeReference Type;
            public MethodReference ScalarCreateMethod;
            public MethodReference ArrayCreateMethod;
        }

        /// <summary>
        /// <see cref="Burst.BurstCompileAttribute"/>
        /// </summary>
        public CustomAttribute BurstCompileAttribute;
        /// <summary>
        /// <see cref="CausesSideEffectsAttribute"/>
        /// </summary>
        [NSymbol] public TypeReference CausesSideEffectsAttributeType;
        /// <summary>
        /// <see cref="ManagedAttribute"/>
        /// </summary>
        [NSymbol] public TypeReference ManagedNodeDataAttribute;
        /// <summary>
        /// <see cref="INodeData"/>
        /// </summary>
        [NSymbol] public TypeReference INodeDataInterface;
        /// <summary>
        /// <see cref="ISimulationPortDefinition"/>
        /// </summary>
        [NSymbol] public TypeReference ISimulationPortDefinitionInterface;
        /// <summary>
        /// <see cref="IKernelPortDefinition"/>
        /// </summary>
        [NSymbol] public TypeReference IKernelPortDefinitionInterface;
        /// <summary>
        /// <see cref="IKernelData"/>
        /// </summary>
        [NSymbol] public TypeReference IKernelDataInterface;
        /// <summary>
        /// <see cref="IGraphKernel{TKernelData, TKernelPortDefinition}"/>
        /// </summary>
        [NSymbol] public TypeReference IGraphKernelInterface;
        /// <summary>
        /// <see cref="NodeTraitsBase"/>
        /// </summary>
        [NSymbol] public TypeReference NodeTraitsBaseDefinition;
        /// <summary>
        /// <see cref="NodeDefinition.BaseTraits"/>
        /// </summary>
        [NSymbol] public MethodReference Get_BaseTraitsDefinition;
        /// <summary>
        /// <see cref="SimulationStorageDefinition"/>
        /// </summary>
        [NSymbol] public TypeReference SimulationStorageDefinitionType;
        /// <summary>
        /// <see cref="SimulationStorageDefinition.Create{TNodeData, TSimPorts}(bool)"/>
        /// </summary>
        [NSymbol] public MethodReference SimulationStorageDefinitionCreateMethod;
        /// <summary>
        /// <see cref="SimulationStorageDefinition.Create{TNodeData}(bool)"/>
        /// </summary>
        [NSymbol] public MethodReference SimulationStorageDefinitionNoPortsCreateMethod;
        /// <summary>
        /// <see cref="SimulationStorageDefinition.Create{TSimPorts}()"/>
        /// </summary>
        [NSymbol] public MethodReference SimulationStorageDefinitionNoDataCreateMethod;
        /// <summary>
        /// <see cref="NodeDefinition.SimulationStorageTraits"/>
        /// </summary>
        [NSymbol] public MethodReference Get_SimulationStorageTraits;
        /// <summary>
        /// <see cref="KernelStorageDefinition"/>
        /// </summary>
        [NSymbol] public TypeReference KernelStorageDefinitionType;
        /// <summary>
        /// <see cref="KernelStorageDefinition.Create()"/>
        /// </summary>
        [NSymbol] public MethodReference KernelStorageDefinitionCreateMethod;
        /// <summary>
        /// <see cref="NodeDefinition.KernelStorageTraits"/>
        /// </summary>
        [NSymbol] public MethodReference Get_KernelStorageTraits;
        /// <summary>
        /// <see cref="NodeTraitsKind"/>
        /// </summary>
        [NSymbol] List<TypeReference> TraitsDefinitions = new List<TypeReference>();
        /// <summary>
        /// <see cref="NodeDefinitionKind"/>
        /// </summary>
        [NSymbol] List<TypeReference> NodeDefinitions = new List<TypeReference>();

        /// <summary>
        /// <see cref="IPortDefinitionInitializer"/>
        /// </summary>
        [NSymbol] public TypeReference IPortDefinitionInitializerType;

        /// <summary>
        /// <see cref="IPortDefinitionInitializer.DFG_CG_Initialize"/>
        /// </summary>
        [NSymbol] public MethodDefinition IPortDefinitionInitializedMethod;

        /// <summary>
        /// <see cref="PortArray{}"/>
        /// </summary>
        [NSymbol] public TypeReference PortArrayType;

        /// <summary>
        /// Types of DFG ports (eg. MessageInput/Output, DataInput/Output, etc.) and their associated creation methods (both array and non-array variants)
        /// </summary>
        List<PortType> m_Ports;

        /// <summary>
        /// <see cref="IMsgHandler{TMsg}"/>
        /// </summary>
        [NSymbol] public TypeReference IMessageHandlerInterface;

        /// <summary>
        /// <see cref="IMsgHandlerGeneric{TMsg}"/>
        /// </summary>
        [NSymbol] public TypeReference IMessageHandlerGenericInterface;

        /// <summary>
        /// <see cref="IInit"/>
        /// </summary>
        [NSymbol] public TypeReference IInitInterface;

        /// <summary>
        /// <see cref="IUpdate"/>
        /// </summary>
        [NSymbol] public TypeReference IUpdateInterface;

        /// <summary>
        /// <see cref="IDestroy"/>
        /// </summary>
        [NSymbol] public TypeReference IDestroyInterface;

        /// <summary>
        /// <see cref="NodeDefinition.VirtualTable"/>
        /// </summary>
        [NSymbol] public FieldReference VirtualTableField;

        /// <summary>
        /// <see cref="NodeDefinition.SimulationVTable.InstallMessageHandler{TNodeDefinition, TNodeData, TMessageData}(MessageInput{TNodeDefinition, TMessageData})"/>
        /// </summary>
        [NSymbol] MethodReference VTableMessageInstaller;

        /// <summary>
        /// <see cref="NodeDefinition.SimulationVTable.InstallPortArrayMessageHandler{TNodeDefinition, TNodeData, TMessageData}(PortArray{MessageInput{TNodeDefinition, TMessageData}})"/>
        /// </summary>
        [NSymbol] MethodReference VTablePortArrayMessageInstaller;

        /// <summary>
        /// <see cref="NodeDefinition.SimulationVTable.InstallMessageHandlerGeneric{TNodeDefinition, TNodeData, TMessageData}(MessageInput{TNodeDefinition, TMessageData})"/>
        /// </summary>
        [NSymbol] MethodReference VTableMessageGenericInstaller;

        /// <summary>
        /// <see cref="NodeDefinition.SimulationVTable.InstallPortArrayMessageHandlerGeneric{TNodeDefinition, TNodeData, TMessageData}(PortArray{MessageInput{TNodeDefinition, TMessageData}})"/>
        /// </summary>
        [NSymbol] MethodReference VTablePortArrayMessageGenericInstaller;

        /// <summary>
        /// <see cref="NodeDefinition.SimulationVTable.InstallDestroyHandler{TNodeDefinition, TNodeData}"/>
        /// </summary>
        [NSymbol] public MethodReference VTableDestroyInstaller;

        /// <summary>
        /// <see cref="NodeDefinition.SimulationVTable.InstallUpdateHandler{TNodeDefinition, TNodeData}"/>
        /// </summary>
        [NSymbol] public MethodReference VTableUpdateInstaller;

        /// <summary>
        /// <see cref="NodeDefinition.SimulationVTable.InstallInitHandler{TNodeDefinition, TNodeData}"/>
        /// </summary>
        [NSymbol] public MethodReference VTableInitInstaller;

        /// <summary>
        /// <see cref="KernelNodeDefinition{TKernelPortDefinition}.KernelPorts"/>
        /// </summary>
        [NSymbol] public FieldReference KernelNodeDefinition_KernelPortsField;

        /// <summary>
        /// <see cref="SimulationNodeDefinition{TSimulationPortDefinition}.SimulationPorts"/>
        /// </summary>
        [NSymbol] public FieldReference SimulationNodeDefinition_SimulationPortsField;

        /// <summary>
        /// <see cref="SimulationKernelNodeDefinition{TSimulationPortDefinition,TKernelPortDefinition}.KernelPorts"/>
        /// </summary>
        [NSymbol] public FieldReference SimulationKernelNodeDefinition_KernelPortsField;

        /// <summary>
        /// <see cref="ComponentNode"/>
        /// </summary>
        [NSymbol] public TypeReference InternalComponentNodeType;

        /// <summary>
        /// <see cref="System.ValueType"/>
        /// </summary>
        [NSymbol] public TypeReference ValueTypeType;

        public DFGLibrary(ModuleDefinition def) : base(def) { }

        public NodeDefinitionKind? IdentifyDefinition(TypeReference r)
        {
            // Drop instantiation so we can match against NodeDefinition<,> etc.
            r = r.Open();

            for(int i = 0; i < NodeDefinitions.Count; ++i)
            {
                if(r.RefersToSame(NodeDefinitions[i]))
                    return (NodeDefinitionKind)i;
            }

            return null;
        }

        public TypeReference DefinitionKindToType(NodeDefinitionKind kind)
        {
            return NodeDefinitions[(int)kind];
        }

        public TypeReference TraitsKindToType(NodeTraitsKind kind)
        {
            return TraitsDefinitions[(int)kind];
        }

        public PortClass? ClassifyPort(TypeReference potentialPort)
        {
            potentialPort = potentialPort.Open();
            for (int i = 0; i < m_Ports.Count; ++i)
            {
                if (m_Ports[i].Type.RefersToSame(potentialPort))
                    return (PortClass)i;
            }

            return null;
        }

        static Type GetPortTypeByEnum(PortClass klass)
        {
            switch (klass)
            {
                case PortClass.MessageInput: return typeof(MessageInput<,>);
                case PortClass.MessageOutput: return typeof(MessageOutput<,>);
                case PortClass.DataInput: return typeof(DataInput<,>);
                case PortClass.DataOutput: return typeof(DataOutput<,>);
                case PortClass.DSLInput: return typeof(DSLInput<,,>);
                case PortClass.DSLOutput: return typeof(DSLOutput<,,>);
            }

            throw new ArgumentOutOfRangeException();
        }

        public MethodReference GetVTableMessageInstallerForHandler(TypeReference messageHandler, TypeReference portType)
        {
            var forIMsgHandler = messageHandler.Open().RefersToSame(IMessageHandlerInterface);

            if (portType.Open().RefersToSame(PortArrayType))
                return forIMsgHandler ? VTablePortArrayMessageInstaller : VTablePortArrayMessageGenericInstaller;

            return forIMsgHandler ? VTableMessageInstaller : VTableMessageGenericInstaller;
        }

        public MethodReference FindCreateMethodForPortType(PortInfo info)
        {
            var slot = GetPort(info.Classification);
            return info.IsArray ? slot.ArrayCreateMethod : slot.ScalarCreateMethod;
        }

        public PortType GetPort(PortClass klass) => m_Ports[(int)klass];

        public override void ParseSymbols(Diag diag)
        {
            BurstCompileAttribute = new CustomAttribute(
                Module.ImportReference(typeof(BurstCompileAttribute).GetConstructor(Type.EmptyTypes)));
            CausesSideEffectsAttributeType = GetImportedReference(typeof(CausesSideEffectsAttribute));
            ManagedNodeDataAttribute = GetImportedReference(typeof(ManagedAttribute));

            INodeDataInterface = GetImportedReference(typeof(INodeData));
            ISimulationPortDefinitionInterface = GetImportedReference(typeof(ISimulationPortDefinition));
            IKernelPortDefinitionInterface = GetImportedReference(typeof(IKernelPortDefinition));
            IKernelDataInterface = GetImportedReference(typeof(IKernelData));
            IGraphKernelInterface = GetImportedReference(typeof(IGraphKernel<,>));
            IMessageHandlerInterface = GetImportedReference(typeof(IMsgHandler<>));
            IMessageHandlerGenericInterface = GetImportedReference(typeof(IMsgHandlerGeneric<>));
            IUpdateInterface = GetImportedReference(typeof(IUpdate));
            IDestroyInterface = GetImportedReference(typeof(IDestroy));
            IInitInterface = GetImportedReference(typeof(IInit));

            var simNodeDefinition = GetImportedReference(typeof(SimulationNodeDefinition<>));
            NodeDefinitions.Add(simNodeDefinition);
            var kernelNodeDefinition = GetImportedReference(typeof(KernelNodeDefinition<>));
            NodeDefinitions.Add(kernelNodeDefinition);
            var simKernelNodeDefinition = GetImportedReference(typeof(SimulationKernelNodeDefinition<,>));
            NodeDefinitions.Add(simKernelNodeDefinition);

            SimulationNodeDefinition_SimulationPortsField =
                EnsureImported(simNodeDefinition.Resolve().Fields.Single(f => f.Name == nameof(SimulationNodeDefinition<DummyNode.SimPorts>.SimulationPorts)));
            KernelNodeDefinition_KernelPortsField =
                EnsureImported(kernelNodeDefinition.Resolve().Fields.Single(f => f.Name == nameof(SimulationKernelNodeDefinition<DummyNode.SimPorts, DummyNode.KernelPorts>.KernelPorts)));
            SimulationKernelNodeDefinition_KernelPortsField =
                EnsureImported(kernelNodeDefinition.Resolve().Fields.Single(f => f.Name == nameof(SimulationKernelNodeDefinition<DummyNode.SimPorts, DummyNode.KernelPorts>.KernelPorts)));

            var nodeDefinition = GetImportedReference(typeof(NodeDefinition));
            var resolvedNodeDefinition = nodeDefinition.Resolve();

            NodeDefinitions.Add(nodeDefinition);

            NodeTraitsBaseDefinition = GetImportedReference(typeof(NodeTraitsBase));
            SimulationStorageDefinitionType = GetImportedReference(typeof(SimulationStorageDefinition));
            SimulationStorageDefinitionCreateMethod = FindGenericMethod(SimulationStorageDefinitionType, nameof(SimulationStorageDefinition.Create), 3, Module.TypeSystem.Boolean);
            SimulationStorageDefinitionNoPortsCreateMethod = FindGenericMethod(SimulationStorageDefinitionType, nameof(SimulationStorageDefinition.Create), 2, Module.TypeSystem.Boolean);
            SimulationStorageDefinitionNoDataCreateMethod = FindGenericMethod(SimulationStorageDefinitionType, nameof(SimulationStorageDefinition.Create), 1);
            KernelStorageDefinitionType = GetImportedReference(typeof(KernelStorageDefinition));
            KernelStorageDefinitionCreateMethod = FindGenericMethod(KernelStorageDefinitionType, nameof(KernelStorageDefinition.Create), 4, Module.TypeSystem.Boolean, Module.TypeSystem.Boolean);

            // TODO: Should change into virtual method instead of property.
            var property = resolvedNodeDefinition.Properties.Single(p => p.Name == nameof(NodeDefinition.BaseTraits));
            Get_BaseTraitsDefinition = EnsureImported(property.GetMethod);
            Get_BaseTraitsDefinition.ReturnType = NodeTraitsBaseDefinition;
            property = resolvedNodeDefinition.Properties.Single(p => p.Name == nameof(NodeDefinition.SimulationStorageTraits));
            Get_SimulationStorageTraits = EnsureImported(property.GetMethod);
            Get_SimulationStorageTraits.ReturnType = SimulationStorageDefinitionType;
            property = resolvedNodeDefinition.Properties.Single(p => p.Name == nameof(NodeDefinition.KernelStorageTraits));
            Get_KernelStorageTraits = EnsureImported(property.GetMethod);
            Get_KernelStorageTraits.ReturnType = KernelStorageDefinitionType;

            VirtualTableField = EnsureImported(resolvedNodeDefinition.Fields.Single(f => f.Name == nameof(NodeDefinition.VirtualTable)));
            var vtableType = VirtualTableField.FieldType.Resolve();

            VTableMessageInstaller = GetUniqueMethod(vtableType, nameof(NodeDefinition.SimulationVTable.InstallMessageHandler));
            VTablePortArrayMessageInstaller = GetUniqueMethod(vtableType, nameof(NodeDefinition.SimulationVTable.InstallPortArrayMessageHandler));
            VTableInitInstaller = GetUniqueMethod(vtableType, nameof(NodeDefinition.SimulationVTable.InstallInitHandler));
            VTableUpdateInstaller = GetUniqueMethod(vtableType, nameof(NodeDefinition.SimulationVTable.InstallUpdateHandler));
            VTableDestroyInstaller = GetUniqueMethod(vtableType, nameof(NodeDefinition.SimulationVTable.InstallDestroyHandler));
            VTableMessageGenericInstaller = GetUniqueMethod(vtableType, nameof(NodeDefinition.SimulationVTable.InstallMessageHandlerGeneric));
            VTablePortArrayMessageGenericInstaller = GetUniqueMethod(vtableType, nameof(NodeDefinition.SimulationVTable.InstallPortArrayMessageHandlerGeneric));

            TraitsDefinitions.Add(GetImportedReference(typeof(NodeTraits<>)));
            TraitsDefinitions.Add(GetImportedReference(typeof(NodeTraits<,>)));
            TraitsDefinitions.Add(GetImportedReference(typeof(NodeTraits<,,>)));
            TraitsDefinitions.Add(GetImportedReference(typeof(NodeTraits<,,,>)));
            TraitsDefinitions.Add(GetImportedReference(typeof(NodeTraits<,,,>)));

            IPortDefinitionInitializerType = GetImportedReference(typeof(IPortDefinitionInitializer));
            IPortDefinitionInitializedMethod = GetUniqueMethod(IPortDefinitionInitializerType.Resolve(), nameof(IPortDefinitionInitializer.DFG_CG_Initialize)).Resolve();

            PortArrayType = GetImportedReference(typeof(PortArray<>));

            m_Ports = new List<PortType>();
            foreach(var enumValue in Enum.GetValues(typeof(PortClass)).Cast<PortClass>())
            {
                var type = GetImportedReference(GetPortTypeByEnum(enumValue)).Resolve();
                var parentInstantiatedReference = type.MakeGenericInstanceType(type.GenericParameters.ToArray());
                var portArrayInstantiationOfParentInstantiation = PortArrayType.MakeGenericInstanceType(parentInstantiatedReference);

                var parentInstantiatedReferenceByReference = new ByReferenceType(parentInstantiatedReference);
                var portArrayInstantiationOfParentInstantiationByReference = new ByReferenceType(portArrayInstantiationOfParentInstantiation);

                m_Ports.Add(new PortType
                {
                    Type = type,
                    ScalarCreateMethod = FindMethod(
                        type,
                        nameof(DataInput<DummyNode, int>.ILPP_Create),
                        parentInstantiatedReferenceByReference,
                        Module.TypeSystem.UInt16
                    ),
                    ArrayCreateMethod = FindMethod(
                        type,
                        nameof(DataInput<DummyNode, int>.ILPP_CreatePortArray),
                        portArrayInstantiationOfParentInstantiationByReference,
                        Module.TypeSystem.UInt16
                    )
                });
            }

            InternalComponentNodeType = GetImportedReference(typeof(InternalComponentNode));
            ValueTypeType = GetImportedReference(typeof(System.ValueType));
        }

        protected override void OnAnalyseConsistency(Diag diag)
        {
            if(BurstCompileAttribute == null)
                diag.DFG_IE_02(this);
        }
    }

    static class Extensions
    {
        public static bool? HasKernelAspects(this DFGLibrary.NodeDefinitionKind kind)
        {
            switch (kind)
            {
                case DFGLibrary.NodeDefinitionKind.Kernel:
                case DFGLibrary.NodeDefinitionKind.SimulationKernel:
                    return true;
                case DFGLibrary.NodeDefinitionKind.Naked:
                    return null;
            };

            return false;
        }

        public static bool? HasSimulationAspects(this DFGLibrary.NodeDefinitionKind kind)
        {
            switch (kind)
            {
                case DFGLibrary.NodeDefinitionKind.Simulation:
                case DFGLibrary.NodeDefinitionKind.SimulationKernel:
                    return true;
                case DFGLibrary.NodeDefinitionKind.Naked:
                    return null;
            };

            return false;
        }

        public static bool? HasSimulationPorts(this DFGLibrary.NodeDefinitionKind kind)
        {
            switch (kind)
            {
                case DFGLibrary.NodeDefinitionKind.Simulation:
                case DFGLibrary.NodeDefinitionKind.SimulationKernel:
                    return true;
                case DFGLibrary.NodeDefinitionKind.Naked:
                    return null;
            };

            return false;
        }

        public static MethodReference GetConstructor(this DFGLibrary.NodeTraitsKind kind, GenericInstanceType instantiation, DFGLibrary instance)
        {
            var cref = instance.EnsureImported(instance.TraitsKindToType(kind).Resolve().GetConstructors().First());
            return new MethodReference(cref.Name, cref.ReturnType, instantiation)
            {
                HasThis = cref.HasThis,
                ExplicitThis = cref.ExplicitThis,
                CallingConvention = cref.CallingConvention
            };
        }
    }
}
