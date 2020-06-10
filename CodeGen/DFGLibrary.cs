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
        /// <code>[BurstCompile]</code>
        /// </summary>
        public CustomAttribute BurstCompileAttribute;
        /// <summary>
        /// <code>typeof(INodeData)</code>
        /// </summary>
        [NSymbol] public TypeReference INodeDataInterface;
        /// <summary>
        /// <code>typeof(ISimulationPortDefinition)</code>
        /// </summary>
        [NSymbol] public TypeReference ISimulationPortDefinitionInterface;
        /// <summary>
        /// <code>typeof(IKernelPortDefinition)</code>
        /// </summary>
        [NSymbol] public TypeReference IKernelPortDefinitionInterface;
        /// <summary>
        /// <code>typeof(IKernelData)</code>
        /// </summary>
        [NSymbol] public TypeReference IKernelDataInterface;
        /// <summary>
        /// <code>typeof(IGraphKernel<,>)</code>
        /// </summary>
        [NSymbol] public TypeReference IGraphKernelInterface;
        /// <summary>
        /// <code>typeof(NodeTraitsBase)</code>
        /// </summary>
        [NSymbol] public TypeReference NodeTraitsBaseDefinition;
        /// <summary>
        /// <code>typeof(NodeDefinition.BaseTraits)</code>
        /// </summary>
        [NSymbol] public MethodReference Get_BaseTraitsDefinition;
        /// <summary>
        /// <see cref="NodeTraitsKind"/>
        /// </summary>
        [NSymbol] List<TypeReference> TraitsDefinitions = new List<TypeReference>();
        /// <summary>
        /// <see cref="NodeDefinitionKind"/>
        /// </summary>
        [NSymbol] List<TypeReference> NodeDefinitions = new List<TypeReference>();

        /// <summary>
        /// typeof(<see cref="IPortDefinitionInitializer"/>)
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
        /// typeof(<see cref="PortStorage"/>)
        /// </summary>
        [NSymbol] public TypeReference PortStorageType;

        /// <summary>
        /// Constructor for <see cref="PortStorage(ushort)"/>
        /// </summary>
        [NSymbol] public MethodReference PortStorageConstructor;

        /// <summary>
        /// typeof(<see cref="PortArray{}"/>)
        /// </summary>
        [NSymbol] public TypeReference PortArrayType;

        /// <summary>
        /// typeof(<see cref="InputPortID"/>), typeof(<see cref="OutputPortID"/>)
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

        public MethodReference FindCreateMethodForPortType(TypeReference portType)
        {
            return PortCreateMethods.First(p => p.DeclaringType.RefersToSame(portType));
        }

        public override void ParseSymbols(Diag diag)
        {
            BurstCompileAttribute = new CustomAttribute(
                Module
                .ImportReference(typeof(BurstCompileAttribute)
                .GetConstructor(System.Type.EmptyTypes)
            ));

            INodeDataInterface = GetImportedReference(typeof(INodeData));
            ISimulationPortDefinitionInterface = GetImportedReference(typeof(ISimulationPortDefinition));
            IKernelPortDefinitionInterface = GetImportedReference(typeof(IKernelPortDefinition));
            IKernelDataInterface = GetImportedReference(typeof(IKernelData));
            IGraphKernelInterface = GetImportedReference(typeof(IGraphKernel));

            NodeDefinitions.Add(GetImportedReference(typeof(SimulationNodeDefinition<>)));
            NodeDefinitions.Add(GetImportedReference(typeof(KernelNodeDefinition<>)));
            NodeDefinitions.Add(GetImportedReference(typeof(SimulationKernelNodeDefinition<,>)));

            var nodeDefinition = GetImportedReference(typeof(NodeDefinition));

            NodeDefinitions.Add(nodeDefinition);
            NodeDefinitions.Add(GetImportedReference(typeof(NodeDefinition<>)));
            NodeDefinitions.Add(GetImportedReference(typeof(NodeDefinition<,>)));
            NodeDefinitions.Add(GetImportedReference(typeof(NodeDefinition<,,>)));
            NodeDefinitions.Add(GetImportedReference(typeof(NodeDefinition<,,,>)));
            NodeDefinitions.Add(GetImportedReference(typeof(NodeDefinition<,,,,>)));

            // TODO: Should change into virtual method instead of property.
            var property = nodeDefinition.Resolve().Properties.First(p => p.Name == nameof(NodeDefinition.BaseTraits));
            var getMethod = property.GetMethod;
            Get_BaseTraitsDefinition = EnsureImported(getMethod);

            NodeTraitsBaseDefinition = GetImportedReference(typeof(NodeTraitsBase));

            TraitsDefinitions.Add(GetImportedReference(typeof(NodeTraits<>)));
            TraitsDefinitions.Add(GetImportedReference(typeof(NodeTraits<,>)));
            TraitsDefinitions.Add(GetImportedReference(typeof(NodeTraits<,,>)));
            TraitsDefinitions.Add(GetImportedReference(typeof(NodeTraits<,,,>)));
            TraitsDefinitions.Add(GetImportedReference(typeof(NodeTraits<,,,,>)));

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
