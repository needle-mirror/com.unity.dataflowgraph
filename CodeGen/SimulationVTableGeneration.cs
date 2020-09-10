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
        /// List of <see cref="IMsgHandler{TMsg}"/> implemented in <see cref="DefinitionProcessor.InstantiatedDefinition"/>
        /// </summary>
        List<GenericInstanceType> m_OldMessageHandlers = new List<GenericInstanceType>();
        /// <summary>
        /// List of TMsg in <see cref="IMsgHandler{TMsg}"/> implemented on a <see cref="INodeData"/>
        /// </summary>
        List<TypeReference> m_NodeDataMessageTypes = new List<TypeReference>();
        /// <summary>
        /// Pair of message type and the connected field (being of <see cref="MessageInput{TDefinition, TMsg}"/> with tmsg = MessageType)
        /// for each of the matching handlers in <see cref="m_NodeDataMessageTypes"/>
        /// </summary>
        List<(TypeReference MessageType, FieldReference ClosedField)> m_DeclaredInputMessagePorts = new List<(TypeReference, FieldReference)>();

        void ParseDeclaredMessageInputs()
        {
            if (SimulationPortImplementation == null)
                return;

            foreach (var field in SimulationPortImplementation.InstantiatedFields())
            {
                var fType = field.Definition.FieldType;
                GenericInstanceType messageInput = null;
                if (fType.RefersToSame(m_Lib.MessageInputType))
                {
                    messageInput = (GenericInstanceType)field.SubstitutedType;
                }
                else if (fType.RefersToSame(m_Lib.PortArrayType) && ((GenericInstanceType)fType).GenericArguments[0].RefersToSame(m_Lib.MessageInputType))
                {
                    messageInput = (GenericInstanceType)((GenericInstanceType)field.SubstitutedType).GenericArguments[0];
                }

                if (messageInput != null)
                {
                    var messageType = messageInput.GenericArguments[1];
                    m_DeclaredInputMessagePorts.Add((messageType, field.Instantiated));
                }
            }
        }

        void ParseOldStyleCallbacks()
        {
            foreach (var iface in InstantiatedDefinition.InstantiatedInterfaces())
            {
                if (iface.Definition.RefersToSame(m_Lib.IMessageHandlerInterface) && iface.Instantiated is GenericInstanceType messageHandler)
                    m_OldMessageHandlers.Add(messageHandler);
            }
        }

        void ParseNewStyleCallbacks()
        {
            if (NodeDataImplementation == null)
                return;

            foreach (var iface in NodeDataImplementation.InstantiatedInterfaces())
            {
                if (iface.Definition.RefersToSame(m_Lib.IMessageHandlerInterface) && iface.Instantiated is GenericInstanceType messageHandler)
                    m_NodeDataMessageTypes.Add(messageHandler.GenericArguments[0]);
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
            if (StaticSimulationPort == null || m_NodeDataMessageTypes.Count == 0)
                return;

            //  // Where Field... : MessageInput<TNodeDefinition, T>
            //  VirtualTable.InstallMessageHandler<TNodeDefinition, TNodeData, T>(SimulationPorts.Field...)...;
            var il = handlerMethod.Body.GetILProcessor();

            foreach (var fieldInfo in m_DeclaredInputMessagePorts)
            {
                GenericInstanceMethod installer;
                if (fieldInfo.ClosedField.FieldType.RefersToSame(m_Lib.PortArrayType))
                {
                    installer = m_Lib.VTablePortArrayMessageInstaller.MakeGenericInstanceMethod(
                        InstantiatedDefinition,
                        NodeDataImplementation,
                        fieldInfo.MessageType
                    );
                }
                else
                {
                    installer = m_Lib.VTableMessageInstaller.MakeGenericInstanceMethod(
                        InstantiatedDefinition,
                        NodeDataImplementation,
                        fieldInfo.MessageType
                    );
                }

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
