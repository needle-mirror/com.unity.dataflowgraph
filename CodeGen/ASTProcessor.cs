using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Unity.CompilationPipeline.Common.Diagnostics;

namespace Unity.DataFlowGraph.CodeGen
{
    class Diag
    {
        public List<DiagnosticMessage> Messages = new List<DiagnosticMessage>();

        public void Exception(string contents) =>
            AddDiagnostic(contents, DiagnosticType.Error);

        public void Error(string contents) =>
            AddDiagnostic(contents, DiagnosticType.Error);

        public void Warning(string contents) =>
            AddDiagnostic(contents, DiagnosticType.Warning);

        public bool HasErrors()
        {
            return Messages.Any(m => m.DiagnosticType == DiagnosticType.Error);
        }

        void AddDiagnostic(string contents, DiagnosticType diagType)
        {
            var message = new DiagnosticMessage();
            message.DiagnosticType = diagType;
            message.MessageData = contents;
            Messages.Add(message);
        }
    }

    /// <summary>
    /// Base class for something that wants to parse / analyse / process Cecil ASTs 
    /// related to DataFlowGraph
    /// </summary>
    abstract class ASTProcessor
    {
        /// <summary>
        /// The module the processor analyses / affects
        /// </summary>
        public readonly ModuleDefinition Module;

        protected ASTProcessor(ModuleDefinition module) => Module = module;

        /// <summary>
        /// The step where the points of interest in the module is parsed
        /// </summary>
        /// <remarks>
        /// Always called first.
        /// </remarks>
        public abstract void ParseSymbols(Diag diag);
        /// <summary>
        /// The step where the basic parse is analysed for rule violations and consistency
        /// </summary>
        /// <remarks>
        /// Always called after Parse step.
        /// </remarks>
        public virtual void AnalyseConsistency(Diag diag) { }
        /// <summary>
        /// A chance for post processing the module.
        /// </summary>
        /// <remarks>
        /// Always called after Analyse and Parse steps.
        /// </remarks>
        /// <param name="mutated">
        /// True if changes were made to the assembly.
        /// </param>
        public virtual void PostProcess(Diag diag, out bool mutated) { mutated = false; }

        /// <summary>
        /// Make a (simple) clone of the <paramref name="completelyOpenMethod"/> function, that is pointing to the generic
        /// instantiation of <paramref name="closedOuterType"/>, instead of being completely open.
        /// </summary>
        protected MethodReference DeriveEnclosedMethodReference(MethodReference completelyOpenMethod, GenericInstanceType closedOuterType)
        {
            var reference = new MethodReference(completelyOpenMethod.Name, completelyOpenMethod.ReturnType, closedOuterType);

            foreach (var genericParameter in completelyOpenMethod.GenericParameters)
                reference.GenericParameters.Add(new GenericParameter(genericParameter.Name, reference));

            foreach (var parameter in completelyOpenMethod.Parameters)
                reference.Parameters.Add(new ParameterDefinition(parameter.Name, parameter.Attributes, Module.ImportReference(parameter.ParameterType)));

            return reference;

        }
    }
}
