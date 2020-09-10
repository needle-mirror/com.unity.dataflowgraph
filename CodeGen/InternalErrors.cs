using System;
using System.Reflection;

namespace Unity.DataFlowGraph.CodeGen
{
    partial class Diag
    {
        public void DFG_IE_01(IDefinitionContext context, FieldInfo field)
        {
            Error(nameof(DFG_IE_01), context, $"Internal missing symbol on {context}: {field}");
        }

        public void DFG_IE_02(IDefinitionContext context)
        {
            Error(nameof(DFG_IE_02), context, "Externally missing symbols");
        }

        public void DFG_IE_03(IDefinitionContext context)
        {
            Error(nameof(DFG_IE_03), context, "Unable to determine node definition derivation tree");
        }

        public void DFG_IE_04(Exception e, IDefinitionContext context = null)
        {
            Error(nameof(DFG_IE_04), context, $"{e.Message}\n {e.StackTrace}");
        }

        public void DFG_IE_05(IDefinitionContext context)
        {
            Error(nameof(DFG_IE_05), context, "Unexpected interface implementation");
        }

        public void DFG_IE_06(IDefinitionContext context, string fieldName)
        {
            Error(nameof(DFG_IE_06), context, $"Could not find {fieldName} field");
        }
    }

}
