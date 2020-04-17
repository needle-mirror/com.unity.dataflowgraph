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
        /// <summary>
        /// <code>[BurstCompile]</code>
        /// </summary>
        public CustomAttribute BurstCompileAttribute;

        public DFGLibrary(ModuleDefinition def) : base(def) { }

        public override void ParseSymbols(Diag diag)
        {
            BurstCompileAttribute = new CustomAttribute(
                Module
                .ImportReference(typeof(BurstCompileAttribute)
                .GetConstructor(System.Type.EmptyTypes)
            ));

        }
    }

}
