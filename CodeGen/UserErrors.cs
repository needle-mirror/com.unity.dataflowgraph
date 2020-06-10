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
            Error(nameof(DFG_UE_08), context, "Field must be public non-static", field);
        }

        public void DFG_UE_09(IDefinitionContext context, FieldLocationContext field)
        {
            Error(nameof(DFG_UE_09), context, "Invalid port type (should be MessageInput/Output, DataInput/Output, DSLInput/Output, or a PortArray<> of any of those types)", field);
        }
    }
}
