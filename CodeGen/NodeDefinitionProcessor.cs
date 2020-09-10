﻿using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace Unity.DataFlowGraph.CodeGen
{
    partial class NodeDefinitionProcessor : DefinitionProcessor
    {
        internal TypeReference
            NodeDataImplementation,
            SimulationPortImplementation;

        internal TypeReference
            KernelPortImplementation,
            GraphKernelImplementation,
            KernelDataImplementation;

        internal FieldReference StaticSimulationPort, StaticKernelPort;


        internal DFGLibrary.NodeDefinitionKind? Kind { get; private set; }
        internal DFGLibrary.NodeTraitsKind? TraitsKind { get; private set; }

        MethodDefinition[] m_ExistingConstructors;
        MethodDefinition m_Constructor;
        /// <summary>
        /// Generic-context-preserved most-derived node definition class
        /// <seealso cref="Kind"/>
        /// </summary>
        TypeReference m_BaseNodeDefinitionReference;

        public NodeDefinitionProcessor(DFGLibrary library, TypeDefinition td)
            : base(library, td)
        {
        }

        public override void ParseSymbols(Diag diag)
        {
            (Kind, m_BaseNodeDefinitionReference) = DetermineNodeDefinition(diag);
            TraverseAndCollectDeclarations(diag, InstantiatedDefinition);

            m_ExistingConstructors = DefinitionRoot
                .GetConstructors()
                // Exclude class constructors (".cctor")
                .Where(c => c.Name == ".ctor")
                .ToArray();

            TraitsKind = DetermineTraitsKind();

            ParseDeclaredMessageInputs();
            ParseOldStyleCallbacks();
            ParseNewStyleCallbacks();

            (StaticSimulationPort, StaticKernelPort) = RecoverStaticPortFieldsFromNodeDefinition(m_BaseNodeDefinitionReference);
        }

        public override void AnalyseConsistency(Diag diag)
        {
            if (!Kind.HasValue)
            {
                diag.DFG_IE_03(this);
                return;
            }

            // Identify cases where the user has defined their port definition outside the NodeDefinition.
            if (SimulationPortImplementation == null &&
                (Kind.Value == DFGLibrary.NodeDefinitionKind.Simulation || Kind.Value == DFGLibrary.NodeDefinitionKind.SimulationKernel))
            {
                diag.DFG_UE_14(this, null, nameof(ISimulationPortDefinition));
                return;
            }
            if (KernelPortImplementation == null &&
                (Kind.Value == DFGLibrary.NodeDefinitionKind.Kernel || Kind.Value == DFGLibrary.NodeDefinitionKind.SimulationKernel))
            {
                diag.DFG_UE_14(this, null, nameof(IKernelPortDefinition));
                return;
            }

            if (!TraitsKind.HasValue)
            {
                diag.DFG_UE_07(this, null);
                return;
            }

            var union = new[]
            {
                NodeDataImplementation,
                SimulationPortImplementation,
                GraphKernelImplementation,
                KernelPortImplementation,
                KernelDataImplementation

            };

            var nonNullUnion = union.Where(d => d != null);

            if (nonNullUnion.Distinct().Count() != nonNullUnion.Count())
            {
                diag.DFG_UE_02(this, new AggrTypeContext(union));
            }

            // Determine kernel composition
            var kernelTriple = new[] { GraphKernelImplementation, KernelPortImplementation, KernelDataImplementation };
            var nonNullKernelAspects = kernelTriple.Where(i => i != null);

            if (nonNullKernelAspects.Any())
            {
                // test whether they all exist (since some did)
                if (nonNullKernelAspects.Distinct().Count() != kernelTriple.Count())
                {
                    diag.DFG_UE_03(this, new AggrTypeContext(kernelTriple));
                }
                else
                {
                    // Make sure that the generic parameter used for IGraphKernel matches the declaration found in the definition.
                    var graphKernelIFace = GraphKernelImplementation.InstantiatedInterfaces().First(i => i.Definition.RefersToSame(m_Lib.IGraphKernelInterface));
                    var genericArgumentList = ((GenericInstanceType) graphKernelIFace.Instantiated).GenericArguments;
                    if (!KernelDataImplementation.RefersToSame(genericArgumentList[0]))
                        diag.DFG_UE_13(this, genericArgumentList[0]);
                    if (!KernelPortImplementation.RefersToSame(genericArgumentList[1]))
                        diag.DFG_UE_13(this, genericArgumentList[1]);
                }

                var aspects = Kind.Value.HasKernelAspects();

                if (aspects.HasValue && !aspects.Value)
                    diag.DFG_UE_04(this, Kind.Value, new AggrTypeContext(kernelTriple));
            }
            // Make sure that the generic parameter used for the Simulation/KernelNode base class matches the declaration
            // found in the definition.
            if (!Kind.Value.IsScaffolded() && Kind.Value != DFGLibrary.NodeDefinitionKind.Naked)
            {
                var genericArgumentList = ((GenericInstanceType) m_BaseNodeDefinitionReference).GenericArguments;

                switch (Kind.Value)
                {
                    case DFGLibrary.NodeDefinitionKind.Simulation:
                        if (!SimulationPortImplementation.RefersToSame(genericArgumentList[0]))
                            diag.DFG_UE_12(this, genericArgumentList[0], nameof(ISimulationPortDefinition));
                        break;
                    case DFGLibrary.NodeDefinitionKind.Kernel:
                        if (!KernelPortImplementation.RefersToSame(genericArgumentList[0]))
                            diag.DFG_UE_12(this, genericArgumentList[0], nameof(IKernelPortDefinition));
                        break;
                    case DFGLibrary.NodeDefinitionKind.SimulationKernel:
                        if (!SimulationPortImplementation.RefersToSame(genericArgumentList[0]))
                            diag.DFG_UE_12(this, genericArgumentList[0], nameof(ISimulationPortDefinition));
                        if (!KernelPortImplementation.RefersToSame(genericArgumentList[1]))
                            diag.DFG_UE_12(this, genericArgumentList[1], nameof(IKernelPortDefinition));
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(Kind));
                }
            }

            // Make sure we've found the expected simulation/kernel port definition static field(s).
            if (Kind.Value.HasKernelAspects().HasValue && Kind.Value.HasKernelAspects().Value && StaticKernelPort == null)
                diag.DFG_IE_06(this, nameof(IKernelPortDefinition));
            if (Kind.Value.HasSimulationPorts().HasValue && Kind.Value.HasSimulationPorts().Value && StaticSimulationPort == null)
                diag.DFG_IE_06(this, nameof(ISimulationPortDefinition));

            if(m_ExistingConstructors.Any())
            {
                if (m_ExistingConstructors.Length > 1 || m_ExistingConstructors[0].Parameters.Count > 0 || (m_ExistingConstructors[0].Attributes & MethodAttributes.Public) == 0)
                    diag.DFG_UE_05(this, new MethodLocationContext(m_ExistingConstructors[0]));
            }
            var nameClashes = GetSymbolNameOverlaps(DefinitionRoot);

            if (nameClashes.Any())
                diag.DFG_UE_06(this, new AggrTypeContext(nameClashes));

            // check only old or new style
            var implementedMessageTypes =
                m_NodeDataMessageTypes.Count > 0 ? m_NodeDataMessageTypes : m_OldMessageHandlers.Select(g => g.GenericArguments[0]);

            foreach (var messageHandler in m_OldMessageHandlers)
            {
                if (!Kind.Value.IsScaffolded())
                    diag.DFG_UE_15(this, messageHandler, messageHandler);
                else if (m_NodeDataMessageTypes.Count > 0)
                    diag.DFG_UE_10(this, messageHandler);
            }

            // check all declared message input ports are implemented
            foreach (var messagePort in m_DeclaredInputMessagePorts)
            {
                if (!implementedMessageTypes.Any(t => t.RefersToSame(messagePort.MessageType)))
                    diag.DFG_UE_11(this, new FieldLocationContext(messagePort.ClosedField));
            }

            // Scan the hierarchy for potential implementations of old callbacks
            if (m_PresentCallbacks != 0 || !Kind.Value.IsScaffolded())
            {
                for (var node = DefinitionRoot; !node.RefersToSame(m_BaseNodeDefinitionReference); node = node.BaseType.Resolve())
                {
                    void CheckAndReport(PresentSingularCallbacks cb, MethodReference method, TypeReference newInterface)
                    {
                        if (!node.Overrides(method))
                            return;
                        if (!Kind.Value.IsScaffolded())
                            diag.DFG_UE_15(this, newInterface, method);
                        else if (m_PresentCallbacks.HasFlag(cb))
                            diag.DFG_UE_10(this, method);
                    }
                    CheckAndReport(PresentSingularCallbacks.Init, m_Lib.OldInitCallback, m_Lib.IInitInterface);
                    CheckAndReport(PresentSingularCallbacks.Destroy, m_Lib.OldDestroyCallback, m_Lib.IDestroyInterface);
                    CheckAndReport(PresentSingularCallbacks.Update, m_Lib.OldUpdateCallback, m_Lib.IUpdateInterface);
                }
            }
        }

        public override void PostProcess(Diag diag, out bool mutated)
        {
            CreateTraitsExpression(diag);
            CreateVTableInitializer(diag);
            mutated = true;
        }

        MethodDefinition EmitCallToMethodInDefaultConstructor(MethodReference target)
        {
            MethodDefinition GetConstructor()
            {
                if (m_Constructor != null)
                    return m_Constructor;

                m_Constructor = m_ExistingConstructors.FirstOrDefault();

                if (m_Constructor != null)
                    return m_Constructor;

                var methodAttributes = MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName;
                var gen = new MethodDefinition(".ctor", methodAttributes, Module.TypeSystem.Void);

                gen.Body.Instructions.Add(Instruction.Create(OpCodes.Nop));
                gen.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));

                DefinitionRoot.Methods.Add(gen);

                return m_Constructor = gen;
            }

            var body = GetConstructor().Body;

            var last = body.Instructions[body.Instructions.Count - 1];
            body.Instructions.RemoveAt(body.Instructions.Count - 1);

            body.Instructions.Add(Instruction.Create(OpCodes.Nop));
            body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            body.Instructions.Add(Instruction.Create(OpCodes.Call, target));

            body.Instructions.Add(last);

            return m_Constructor;
        }
    }
}
