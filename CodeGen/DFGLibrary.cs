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
            Naked,
            Scaffold_1,
            Scaffold_2,
            Scaffold_3,
            Scaffold_4,
            Scaffold_5
        }

        public enum NodeTraitsKind
        {
            _1, _2, _3, _4, _5
        }

        class DummyNode : NodeDefinition, IMsgHandler<object>
        {
            public struct SimPorts : ISimulationPortDefinition { }
            public struct KernelPorts : IKernelPortDefinition { }

            public void HandleMessage(in MessageContext ctx, in object msg) =>
                throw new NotImplementedException();
        }

        class DummyDSL : DSLHandler<object>
        {
            protected override void Connect(ConnectionInfo left, ConnectionInfo right) =>
                throw new NotImplementedException();
            protected override void Disconnect(ConnectionInfo left, ConnectionInfo right) =>
                throw new NotImplementedException();
        }

        /// <summary>
        /// <see cref="Burst.BurstCompileAttribute"/>
        /// </summary>
        public CustomAttribute BurstCompileAttribute;
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
        /// <see cref="IPortDefinitionInitializer.DFG_CG_GetInputPortCount"/>
        /// </summary>
        [NSymbol] public MethodDefinition IPortDefinitionGetInputPortCountMethod;

        /// <summary>
        /// <see cref="IPortDefinitionInitializer.DFG_CG_GetOutputPortCount"/>
        /// </summary>
        [NSymbol] public MethodDefinition IPortDefinitionGetOutputPortCountMethod;

        /// <summary>
        /// <see cref="PortStorage"/>
        /// </summary>
        [NSymbol] public TypeReference PortStorageType;

        /// <summary>
        /// Constructor for <see cref="PortStorage(ushort)"/>
        /// </summary>
        [NSymbol] public MethodReference PortStorageConstructor;

        /// <summary>
        /// <see cref="PortArray{}"/>
        /// </summary>
        [NSymbol] public TypeReference PortArrayType;

        /// <summary>
        /// <see cref="InputPortID"/>, <see cref="OutputPortID"/>
        /// </summary>
        [NSymbol] public TypeReference InputPortIDType, OutputPortIDType;

        /// <summary>
        /// Constructors for <see cref="InputPortID"/> and <see cref="OutputPortID"/>
        /// </summary>
        [NSymbol] public MethodReference InputPortIDConstructor, OutputPortIDConstructor;

        /// <summary>
        /// Create methods (taking Input/OutputPortIDs) of each type of DFG port (eg. MessageInput/Output, DataInput/Output, etc.)
        /// </summary>
        [NSymbol] public List<MethodReference> PortCreateMethods;

        /// <summary>
        /// <see cref="IMsgHandler{TMsg}"/>
        /// </summary>
        [NSymbol] public TypeReference IMessageHandlerInterface;

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
        [NSymbol] public MethodReference VTableMessageInstaller;

        /// <summary>
        /// <see cref="NodeDefinition.SimulationVTable.InstallPortArrayMessageHandler{TNodeDefinition, TNodeData, TMessageData}(PortArray{MessageInput{TNodeDefinition, TMessageData}})"/>
        /// </summary>
        [NSymbol] public MethodReference VTablePortArrayMessageInstaller;

        /// <summary>
        /// <see cref="NodeDefinition.SimulationVTable.InstallDestroyHandler{TNodeDefinition, TNodeData}"/>
        /// </summary>
        [NSymbol] public MethodReference VTableDestroyInstaller;

        /// <summary>
        /// <see cref="NodeDefinition.SimulationVTable.InstallUpdateHandler{TNodeDefinition, TNodeData}"/>
        /// </summary>
        [NSymbol] public MethodReference VTableUpdateInstaller;

        /// <summary>
        /// <see cref="NodeDefinition.Destroy(DestroyContext)"/>
        /// </summary>
        [NSymbol] public MethodReference OldDestroyCallback;

        /// <summary>
        /// <see cref="NodeDefinition.SimulationVTable.InstallInitHandler{TNodeDefinition, TNodeData}"/>
        /// </summary>
        [NSymbol] public MethodReference VTableInitInstaller;

        /// <summary>
        /// <see cref="NodeDefinition.Init(InitContext)"/>
        /// </summary>
        [NSymbol] public MethodReference OldInitCallback;

        /// <summary>
        /// <see cref="NodeDefinition.OnUpdate(in UpdateContext)"/>
        /// </summary>
        [NSymbol] public MethodReference OldUpdateCallback;

        /// <summary>
        /// <see cref="MessageInput{TDefinition, TMsg}"/>
        /// </summary>
        [NSymbol] public TypeReference MessageInputType;

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

        public DFGLibrary(ModuleDefinition def) : base(def) { }

        public NodeDefinitionKind? IdentifyDefinition(TypeReference r)
        {
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

        public MethodReference FindCreateMethodForPortType(TypeReference portType, bool forInput)
        {
            return PortCreateMethods.First(p => p.DeclaringType.RefersToSame(portType) && p.Parameters[0].ParameterType.RefersToSame(forInput ? InputPortIDType : OutputPortIDType));
        }

        public override void ParseSymbols(Diag diag)
        {
            BurstCompileAttribute = new CustomAttribute(
                Module.ImportReference(typeof(BurstCompileAttribute).GetConstructor(Type.EmptyTypes)));
            ManagedNodeDataAttribute = GetImportedReference(typeof(ManagedAttribute));

            INodeDataInterface = GetImportedReference(typeof(INodeData));
            ISimulationPortDefinitionInterface = GetImportedReference(typeof(ISimulationPortDefinition));
            IKernelPortDefinitionInterface = GetImportedReference(typeof(IKernelPortDefinition));
            IKernelDataInterface = GetImportedReference(typeof(IKernelData));
            IGraphKernelInterface = GetImportedReference(typeof(IGraphKernel<,>));
            IMessageHandlerInterface = GetImportedReference(typeof(IMsgHandler<>));
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
#pragma warning disable 618
            NodeDefinitions.Add(GetImportedReference(typeof(NodeDefinition<>)));
            NodeDefinitions.Add(GetImportedReference(typeof(NodeDefinition<,>)));
            NodeDefinitions.Add(GetImportedReference(typeof(NodeDefinition<,,>)));
            NodeDefinitions.Add(GetImportedReference(typeof(NodeDefinition<,,,>)));
            NodeDefinitions.Add(GetImportedReference(typeof(NodeDefinition<,,,,>)));
#pragma warning restore 618

            // TODO: Should change into virtual method instead of property.
            var property = resolvedNodeDefinition.Properties.Single(p => p.Name == nameof(NodeDefinition.BaseTraits));
            Get_BaseTraitsDefinition = EnsureImported(property.GetMethod);
            property = resolvedNodeDefinition.Properties.Single(p => p.Name == nameof(NodeDefinition.SimulationStorageTraits));
            Get_SimulationStorageTraits = EnsureImported(property.GetMethod);
            property = resolvedNodeDefinition.Properties.Single(p => p.Name == nameof(NodeDefinition.KernelStorageTraits));
            Get_KernelStorageTraits = EnsureImported(property.GetMethod);

            NodeTraitsBaseDefinition = GetImportedReference(typeof(NodeTraitsBase));
            SimulationStorageDefinitionType = GetImportedReference(typeof(SimulationStorageDefinition));
            SimulationStorageDefinitionCreateMethod = FindGenericMethod(SimulationStorageDefinitionType, nameof(SimulationStorageDefinition.Create), 3, Module.TypeSystem.Boolean, Module.TypeSystem.Boolean);
            SimulationStorageDefinitionNoPortsCreateMethod = FindGenericMethod(SimulationStorageDefinitionType, nameof(SimulationStorageDefinition.Create), 2, Module.TypeSystem.Boolean, Module.TypeSystem.Boolean);
            SimulationStorageDefinitionNoDataCreateMethod = FindGenericMethod(SimulationStorageDefinitionType, nameof(SimulationStorageDefinition.Create), 1);
            KernelStorageDefinitionType = GetImportedReference(typeof(KernelStorageDefinition));
            KernelStorageDefinitionCreateMethod = FindGenericMethod(KernelStorageDefinitionType, nameof(KernelStorageDefinition.Create), 4, Module.TypeSystem.Boolean);

            VirtualTableField = EnsureImported(resolvedNodeDefinition.Fields.Single(f => f.Name == nameof(NodeDefinition.VirtualTable)));
            var vtableMethods = VirtualTableField.FieldType.Resolve().Methods;

            VTableMessageInstaller = EnsureImported(
                vtableMethods.Single(m => m.Name == nameof(NodeDefinition.SimulationVTable.InstallMessageHandler))
            );
            VTablePortArrayMessageInstaller = EnsureImported(
                vtableMethods.Single(m => m.Name == nameof(NodeDefinition.SimulationVTable.InstallPortArrayMessageHandler))
            );

            VTableInitInstaller = EnsureImported(
                vtableMethods.Single(m => m.Name == nameof(NodeDefinition.SimulationVTable.InstallInitHandler))
            );

            VTableUpdateInstaller = EnsureImported(
                vtableMethods.Single(m => m.Name == nameof(NodeDefinition.SimulationVTable.InstallUpdateHandler))
            );


            VTableDestroyInstaller = EnsureImported(
                vtableMethods.Single(m => m.Name == nameof(NodeDefinition.SimulationVTable.InstallDestroyHandler))
            );

            OldInitCallback = resolvedNodeDefinition.Methods.Single(m => m.Name == nameof(NodeDefinition.Init));
            OldDestroyCallback = resolvedNodeDefinition.Methods.Single(m => m.Name == nameof(NodeDefinition.Destroy));
            OldUpdateCallback = resolvedNodeDefinition.Methods.Single(m => m.Name == nameof(NodeDefinition.OnUpdate));

            MessageInputType = GetImportedReference(typeof(MessageInput<,>));

            TraitsDefinitions.Add(GetImportedReference(typeof(NodeTraits<>)));
            TraitsDefinitions.Add(GetImportedReference(typeof(NodeTraits<,>)));
            TraitsDefinitions.Add(GetImportedReference(typeof(NodeTraits<,,>)));
            TraitsDefinitions.Add(GetImportedReference(typeof(NodeTraits<,,,>)));
            TraitsDefinitions.Add(GetImportedReference(typeof(NodeTraits<,,,>)));

            IPortDefinitionInitializerType = GetImportedReference(typeof(IPortDefinitionInitializer));
            IPortDefinitionInitializedMethod =
                FindMethod(IPortDefinitionInitializerType, nameof(IPortDefinitionInitializer.DFG_CG_Initialize), Module.TypeSystem.UInt16, Module.TypeSystem.UInt16).Resolve();
            IPortDefinitionGetInputPortCountMethod =
                FindMethod(IPortDefinitionInitializerType, nameof(IPortDefinitionInitializer.DFG_CG_GetInputPortCount)).Resolve();
            IPortDefinitionGetOutputPortCountMethod =
                FindMethod(IPortDefinitionInitializerType, nameof(IPortDefinitionInitializer.DFG_CG_GetOutputPortCount)).Resolve();

            PortStorageType = GetImportedReference(typeof(PortStorage));
            PortStorageConstructor = FindConstructor(PortStorageType, Module.TypeSystem.UInt16);

            PortArrayType = GetImportedReference(typeof(PortArray<>));

            InputPortIDType = GetImportedReference(typeof(InputPortID));
            OutputPortIDType = GetImportedReference(typeof(OutputPortID));

            InputPortIDConstructor = FindConstructor(InputPortIDType, PortStorageType);
            OutputPortIDConstructor = FindConstructor(OutputPortIDType, PortStorageType);

            PortCreateMethods = new List<MethodReference>();
            void AddCreateMethod(Type portType, string createMethodName, TypeReference factoryArg)
            {
                var importedType = GetImportedReference(portType);
                PortCreateMethods.Add(FindMethod(importedType, createMethodName, factoryArg));
            }
            AddCreateMethod(typeof(MessageInput<,>), nameof(MessageInput<DummyNode, object>.Create), InputPortIDType);
            AddCreateMethod(typeof(MessageOutput<,>), nameof(MessageOutput<DummyNode, object>.Create), OutputPortIDType);
            AddCreateMethod(typeof(DataInput<,>), nameof(DataInput<DummyNode, int>.Create), InputPortIDType);
            AddCreateMethod(typeof(DataOutput<,>), nameof(DataOutput<DummyNode, int>.Create), OutputPortIDType);
            AddCreateMethod(typeof(DSLInput<,,>), nameof(DSLInput<DummyNode, DummyDSL, object>.Create), InputPortIDType);
            AddCreateMethod(typeof(DSLOutput<,,>), nameof(DSLOutput<DummyNode, DummyDSL, object>.Create), OutputPortIDType);
            AddCreateMethod(typeof(PortArray<>), nameof(PortArray<MessageInput<DummyNode, object>>.Create), InputPortIDType);
            AddCreateMethod(typeof(PortArray<>), nameof(PortArray<MessageOutput<DummyNode, object>>.Create), OutputPortIDType);

            InternalComponentNodeType = GetImportedReference(typeof(InternalComponentNode));
        }

        public override void AnalyseConsistency(Diag diag)
        {
            diag.DiagnoseNullSymbolFields(this);

            if(BurstCompileAttribute == null)
                diag.DFG_IE_02(this);
        }
    }

    static class Extensions
    {
        public static bool IsScaffolded(this DFGLibrary.NodeDefinitionKind kind)
        {
            return (int)kind > (int)DFGLibrary.NodeDefinitionKind.Naked;
        }

        public static bool? HasKernelAspects(this DFGLibrary.NodeDefinitionKind kind)
        {
            switch (kind)
            {
                case DFGLibrary.NodeDefinitionKind.Kernel:
                case DFGLibrary.NodeDefinitionKind.SimulationKernel:
                case DFGLibrary.NodeDefinitionKind.Scaffold_3:
                case DFGLibrary.NodeDefinitionKind.Scaffold_4:
                case DFGLibrary.NodeDefinitionKind.Scaffold_5:
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
                case DFGLibrary.NodeDefinitionKind.Scaffold_1:
                case DFGLibrary.NodeDefinitionKind.Scaffold_2:
                case DFGLibrary.NodeDefinitionKind.Scaffold_4:
                case DFGLibrary.NodeDefinitionKind.Scaffold_5:
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
                case DFGLibrary.NodeDefinitionKind.Scaffold_1:
                case DFGLibrary.NodeDefinitionKind.Scaffold_2:
                case DFGLibrary.NodeDefinitionKind.Scaffold_5:
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
