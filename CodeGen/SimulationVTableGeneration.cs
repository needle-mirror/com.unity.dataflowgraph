using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace Unity.DataFlowGraph.CodeGen
{
    partial class NodeDefinitionProcessor
    {
        [System.Flags]
        enum PresentSingularCallbacks
        {
            Init    = 1 << 0,
            Update  = 1 << 1,
            Destroy = 1 << 2
        }

        PresentSingularCallbacks m_PresentCallbacks;

        /// <summary>
        /// List of <see cref="IMsgHandler{TMsg}"/> and <see cref="IMsgHandlerGeneric{TMsg}"/> implemented on a <see cref="INodeData"/>
        /// </summary>
        List<GenericInstanceType> m_MessageHandlers = new List<GenericInstanceType>();
        /// <summary>
        /// Pair of message type and the connected field (being of <see cref="MessageInput{TDefinition, TMsg}"/> with tmsg = MessageType)
        /// for each of the matching handlers in <see cref="m_MessageHandlers"/>
        /// </summary>
        List<(TypeReference MessageType, FieldReference ClosedField)> m_DeclaredInputMessagePorts = new List<(TypeReference, FieldReference)>();

        void ParsePotentialMessageInput(FieldReference root, TypeReference innerType, TypeReference substitutedInnerType)
        {
            if (innerType is GenericInstanceType instantiatedPort &&
                substitutedInnerType is GenericInstanceType instantiatedSubstitutedInnerType)
            {
                var open = innerType.Open();

                if (open.RefersToSame(m_Lib.PortArrayType))
                {
                    ParsePotentialMessageInput(root, instantiatedPort.GenericArguments[0], instantiatedSubstitutedInnerType.GenericArguments[0]);
                    return;
                }
                else if (open.RefersToSame(m_Lib.GetPort(DFGLibrary.PortClass.MessageInput).Type))
                {
                    m_DeclaredInputMessagePorts.Add((instantiatedSubstitutedInnerType.GenericArguments[1], root));
                }
            }
        }

        void ParseDeclaredMessageInputs()
        {
            if (SimulationPortImplementation == null)
                return;

            foreach (var field in SimulationPortImplementation.InstantiatedFields())
                ParsePotentialMessageInput(field.Instantiated, field.Definition.FieldType, field.SubstitutedType);
        }

        void ParseCallbacks()
        {
            if (NodeDataImplementation == null)
                return;

            foreach (var iface in NodeDataImplementation.InstantiatedInterfaces())
            {
                if ((iface.Definition.RefersToSame(m_Lib.IMessageHandlerInterface) || iface.Definition.RefersToSame(m_Lib.IMessageHandlerGenericInterface)) &&
                    iface.Instantiated is GenericInstanceType messageHandler)
                    m_MessageHandlers.Add(messageHandler);
                else if (iface.Definition.RefersToSame(m_Lib.IInitInterface))
                    m_PresentCallbacks |= PresentSingularCallbacks.Init;
                else if (iface.Definition.RefersToSame(m_Lib.IDestroyInterface))
                    m_PresentCallbacks |= PresentSingularCallbacks.Destroy;
                else if (iface.Definition.RefersToSame(m_Lib.IUpdateInterface))
                    m_PresentCallbacks |= PresentSingularCallbacks.Update;
            }
        }

        void CreateVTableInitializer(Diag d)
        {
            if (NodeDataImplementation == null)
                return;

            var handlerMethod = new MethodDefinition(
                MakeSymbol("InstallHandlers"),
                MethodAttributes.Private,
                Module.TypeSystem.Void
            )
            { HasThis = true };

            //  void DFG_GC_InstallHandlers () {
            //      // install init, destroy, update
            //      // install messages...
            //  }

            InsertRegularCallbacks(handlerMethod, d);
            InsertMessageHandlers(handlerMethod, d);

            var il = handlerMethod.Body.GetILProcessor();
            il.Emit(OpCodes.Ret);

            DefinitionRoot.Methods.Add(handlerMethod);

            EmitCallToMethodInDefaultConstructor(FormClassInstantiatedMethodReference(handlerMethod));
        }

        void InsertMessageHandlers(MethodDefinition handlerMethod, Diag d)
        {
            if (StaticSimulationPort == null || m_MessageHandlers.Count == 0)
                return;

            //  // Where Field... : MessageInput<TNodeDefinition, T>
            //  VirtualTable.InstallMessageHandler<TNodeDefinition, TNodeData, T>(SimulationPorts.Field...)...;
            var il = handlerMethod.Body.GetILProcessor();

            foreach (var fieldInfo in m_DeclaredInputMessagePorts)
            {
                var messageHandler = m_MessageHandlers.Single(h => h.GenericArguments[0].RefersToSame(fieldInfo.MessageType));

                var vTableMessageInstaller = m_Lib.GetVTableMessageInstallerForHandler(messageHandler, fieldInfo.ClosedField.FieldType);

                var installer = vTableMessageInstaller.MakeGenericInstanceMethod(
                    InstantiatedDefinition,
                    NodeDataImplementation,
                    fieldInfo.MessageType
                );

                il.Emit(OpCodes.Nop);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldflda, m_Lib.VirtualTableField);
                il.Emit(OpCodes.Ldsflda, StaticSimulationPort);
                il.Emit(OpCodes.Ldfld, fieldInfo.ClosedField);
                il.Emit(OpCodes.Call, installer);
            }
        }

        void InsertRegularCallbacks(MethodDefinition handlerMethod, Diag d)
        {
            var il = handlerMethod.Body.GetILProcessor();

            void InstallHandlerIfPresent(PresentSingularCallbacks flag, MethodReference installer)
            {
                if (!m_PresentCallbacks.HasFlag(flag))
                    return;

                var closedInstaller = installer.MakeGenericInstanceMethod(InstantiatedDefinition, NodeDataImplementation);

                il.Emit(OpCodes.Nop);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldflda, m_Lib.VirtualTableField);
                il.Emit(OpCodes.Call, closedInstaller);
            }

            //  VirtualTable.InstallUpdateHandler<TNodeDefinition, TNodeData>();
            InstallHandlerIfPresent(PresentSingularCallbacks.Update, m_Lib.VTableUpdateInstaller);
            //  VirtualTable.InstallInitHandler<TNodeDefinition, TNodeData>();
            InstallHandlerIfPresent(PresentSingularCallbacks.Init, m_Lib.VTableInitInstaller);
            //  VirtualTable.InstallDestroyHandler<TNodeDefinition, TNodeData>();
            InstallHandlerIfPresent(PresentSingularCallbacks.Destroy, m_Lib.VTableDestroyInstaller);
        }
    }
}
