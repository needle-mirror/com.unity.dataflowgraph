using System;
using Mono.Cecil;

namespace Unity.DataFlowGraph.CodeGen
{
    partial class Diag
    {
        public void DFG_UE_01(IDefinitionContext context, ILocationContext duplicate)
        {
            Error(nameof(DFG_UE_01), context, "Duplicate interface implementation", duplicate);
        }

        public void DFG_UE_02(IDefinitionContext context, ILocationContext duplicate)
        {
            Error(nameof(DFG_UE_02), context, "Same instance type contains multiple interface implementation", duplicate);
        }

        public void DFG_UE_03(IDefinitionContext context, ILocationContext seq)
        {
            Error(nameof(DFG_UE_03), context, "Node definition defined some but not all of a required kernel triple (data, kernel, ports)", seq);
        }

        public void DFG_UE_04(IDefinitionContext context, DFGLibrary.NodeDefinitionKind kind, ILocationContext seq)
        {
            Error(nameof(DFG_UE_04), context, $"Node definition kind {kind} was not expected to contain kernel aspects (data, kernel, ports)", seq);
        }

        public void DFG_UE_05(IDefinitionContext context, ILocationContext seq)
        {
            Error(nameof(DFG_UE_05), context, $"Node definition can only have one optional public parameterless constructor", seq);
        }

        public void DFG_UE_06(IDefinitionContext context, ILocationContext seq)
        {
            Error(nameof(DFG_UE_06), context, $"Definition declares a member with a reserved name", seq);
        }

        public void DFG_UE_07(IDefinitionContext context, ILocationContext seq)
        {
            Error(nameof(DFG_UE_07), context, $"Unable to parse the kind of node definition implemented, mark it abstract if it's not supposed to be used directly", seq);
        }

        public void DFG_UE_08(IDefinitionContext context, FieldLocationContext field)
        {
            Error(nameof(DFG_UE_08), context, "Field must be non-static", field);
        }

        public void DFG_UE_09(IDefinitionContext context, FieldLocationContext field)
        {
            Error(nameof(DFG_UE_09), context, "Invalid port type (should be MessageInput/Output, DataInput/Output, DSLInput/Output, or a PortArray<> of any of those types)", field);
        }

        /// <summary>
        /// Using both old/new handlers
        /// </summary>
        [Obsolete]
        public void DFG_UE_10(IDefinitionContext context, MemberLocationContext oldStyleHandler)
        {
            Error(nameof(DFG_UE_10), context,$"Definition declares new style handlers, but still implements old style handler", oldStyleHandler);
        }

        /// <summary>
        /// Declared message port does not have a matching handler
        /// </summary>
        internal void DFG_UE_11(IDefinitionContext context, FieldLocationContext messagePortDeclaration)
        {
            Error(nameof(DFG_UE_11), context, $"Definition declares an input message port with no matching handler", messagePortDeclaration);
        }

        public void DFG_UE_12(IDefinitionContext context, TypeLocationContext type, string portDefinitionInterface)
        {
            Error(nameof(DFG_UE_12), context, $"Port definition does not match generic parameter in {portDefinitionInterface}", type);
        }

        public void DFG_UE_13(IDefinitionContext context, TypeLocationContext type)
        {
            Error(nameof(DFG_UE_13), context, $"Kernel aspect does not match generic parameter in {nameof(IGraphKernel)}", type);
        }

        public void DFG_UE_14(IDefinitionContext context, ILocationContext seq, string portDefinitionInterface)
        {
            Error(nameof(DFG_UE_14), context, $"{portDefinitionInterface} must be defined within the node definition scope", seq);
        }

        [Obsolete]
        public void DFG_UE_15(IDefinitionContext context, TypeReference newInterface, MemberLocationContext oldStyleHandler)
        {
            Error(nameof(DFG_UE_15), context, $"New-style definition implementing old style handler ({newInterface.PrettyName()} should be implemented on an {nameof(INodeData)} struct within the definition)", oldStyleHandler);
        }

        public void DFG_UE_16(IDefinitionContext context, TypeReference handlerType1, TypeReference handlerType2)
        {
            Error(nameof(DFG_UE_16), context, $"Ambiguous implementation of both {handlerType1.PrettyName()} and {handlerType2.PrettyName()}");
        }
    }
}
