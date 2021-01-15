using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Rocks;

namespace Unity.DataFlowGraph.CodeGen
{
    /// <summary>
    /// Annotation for a symbol that must always exist.
    /// See <see cref="HelperExtensions.DiagnoseNullSymbolFields(Diag, IDefinitionContext)"/>
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class NSymbolAttribute : Attribute { }

    /// <summary>
    /// Base class for something that wants to parse / analyse / process Cecil ASTs
    /// related to DataFlowGraph
    /// </summary>
    abstract class ASTProcessor : IDefinitionContext
    {
        /// <summary>
        /// Any codegenerated symbol should have this prepended.
        /// <seealso cref="MakeSymbol(string)"/>
        /// </summary>
        const string k_InjectedSymbolPrefix = "DFG_CG_";
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
        public void AnalyseConsistency(Diag diag)
        {
            diag.DiagnoseNullSymbolFields(this);
            OnAnalyseConsistency(diag);
        }
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


        public virtual string GetContextName()
        {
            return Module.Name;
        }

        protected virtual void OnAnalyseConsistency(Diag d) { }

        /// <summary>
        /// Make a (simple) clone of the <paramref name="completelyOpenMethod"/> function, that is pointing to the generic
        /// instantiation of <paramref name="closedOuterType"/>, instead of being completely open.
        /// </summary>
        protected MethodReference DeriveEnclosedMethodReference(MethodReference completelyOpenMethod, TypeReference closedOuterType)
        {
            var reference = new MethodReference(completelyOpenMethod.Name, completelyOpenMethod.ReturnType, closedOuterType)
            {
                HasThis = completelyOpenMethod.HasThis,
                ExplicitThis = completelyOpenMethod.ExplicitThis,
                CallingConvention = completelyOpenMethod.CallingConvention
            };

            foreach (var genericParameter in completelyOpenMethod.GenericParameters)
                reference.GenericParameters.Add(new GenericParameter(genericParameter.Name, reference));

            foreach (var parameter in completelyOpenMethod.Parameters)
                reference.Parameters.Add(new ParameterDefinition(parameter.Name, parameter.Attributes, EnsureImported(parameter.ParameterType)));

            return reference;
        }

        /// <summary>
        /// Use this function to decorate a generated symbol in a standardized way.
        /// </summary>
        protected string MakeSymbol(string localName)
        {
            return k_InjectedSymbolPrefix + localName;
        }

        /// <summary>
        /// Returns an enumeration of potential name clashes existing in <paramref name="def"/>
        /// together with <see cref="MakeSymbol(string)"/>
        /// </summary>
        public static IEnumerable<MemberReference> GetSymbolNameOverlaps(TypeDefinition def)
        {
            foreach(var m in def.Methods)
            {
                if (m.Name.StartsWith(k_InjectedSymbolPrefix))
                    yield return m;
            }

            foreach (var f in def.Fields)
            {
                if (f.Name.StartsWith(k_InjectedSymbolPrefix))
                    yield return f;
            }

            foreach (var p in def.Properties)
            {
                if (p.Name.StartsWith(k_InjectedSymbolPrefix))
                    yield return p;
            }

            // Name hiding is allowed through inheritance, so we don't need to scan recursively.
            // It will generate CS0108 normally, though not in IL.
            yield break;
        }

        /// <summary>
        /// Get a Cecil <see cref="TypeReference"/> from a <see cref="System.Type"/> and import it into the current
        /// <see cref="Module"/> if necessary.
        /// </summary>
        public TypeReference GetImportedReference(Type type)
        {
            return Module.GetImportedReference(type);
        }

        /// <summary>
        /// Ensure that the given Cecil <see cref="TypeReference"/> is imported into the current <see cref="Module"/> if
        /// necessary.
        /// </summary>
        public TypeReference EnsureImported(TypeReference type)
        {
            if (type.Module == Module)
                return type;

            var generic = type as GenericInstanceType;
            if (generic == null)
                return Module.ImportReference(type);

            var importedArgs = generic.GenericArguments.Select(a => EnsureImported(a)).ToArray();
            return EnsureImported(generic.Resolve()).MakeGenericInstanceType(importedArgs);
        }

        /// <summary>
        /// Ensure that the given Cecil <see cref="FieldReference"/> is imported into the current <see cref="Module"/> if
        /// necessary.
        /// </summary>
        public FieldReference EnsureImported(FieldReference field)
        {
            if (field.Module == Module)
                return field;

            return Module.ImportReference(field);
        }

        /// <summary>
        /// Ensure that the given Cecil <see cref="MethodReference"/> is imported into the current <see cref="Module"/> if
        /// necessary.
        /// </summary>
        public MethodReference EnsureImported(MethodReference method)
        {
            if (method.Module == Module)
                return method;

            return Module.ImportReference(method);
        }

        /// <summary>
        /// Find a generic Cecil <see cref="MethodReference"/> by its name and parameters for the given type and import
        /// it into the current <see cref="Module"/> if necessary.
        /// </summary>
        public MethodReference FindGenericMethod(TypeReference type, string name, int genericCount, params TypeReference[] parameters)
        {
            foreach (var m in type.Resolve().Methods)
            {
                if (m.Name == name && m.Parameters.Count == parameters.Length && m.GenericParameters.Count == genericCount)
                {
                    int i;
                    for (i = 0; i < parameters.Length; ++i)
                        if (!m.Parameters[i].ParameterType.RefersToSame(parameters[i]))
                            break;
                    if (i == parameters.Length)
                        return EnsureImported(m);
                }
            }
            return null;
        }

        /// <summary>
        /// Find a Cecil <see cref="MethodReference"/> by its name and parameters for the given type and import
        /// it into the current <see cref="Module"/> if necessary.
        /// </summary>
        public MethodReference FindMethod(TypeReference type, string name, params TypeReference[] parameters)
        {
            return FindGenericMethod(type, name, 0, parameters);
        }

        /// <summary>
        /// Find a Cecil <see cref="MethodReference"/> by its unique name, asserts existance and import
        /// it into the current <see cref="Module"/> if necessary.
        /// </summary>
        public MethodReference GetUniqueMethod(TypeDefinition definition, string name)
        {
            return EnsureImported(definition.Methods.Single(m => m.Name == name));
        }

        /// <summary>
        /// Try to find a Cecil <see cref="MethodReference"/> by its unique name and import
        /// it into the current <see cref="Module"/> if necessary.
        /// </summary>
        public MethodReference FindUniqueMethod(TypeDefinition definition, string name)
        {
            var method = definition.Methods.FirstOrDefault(m => m.Name == name);
            if (method != null)
                return EnsureImported(method);
            return null;
        }

        /// <summary>
        /// Find a Cecil <see cref="MethodReference"/> for a type constructor given its parameter list and import
        /// it into the current <see cref="Module"/> if necessary.
        /// </summary>
        public MethodReference FindConstructor(TypeReference type, params TypeReference[] parameters)
        {
            return FindMethod(type, ".ctor", parameters);
        }

        /// <summary>
        /// Create a bodiless <see cref="MethodDefinition"/> to implement the given interface method.
        /// </summary>
        public MethodDefinition CreateEmptyInterfaceMethodImplementation(MethodDefinition interfaceMethod)
        {
            var method = new MethodDefinition(interfaceMethod.Name, DFGLibrary.MethodPublicFinalFlags,EnsureImported(interfaceMethod.ReturnType));
            foreach (var p in interfaceMethod.Parameters)
                method.Parameters.Add(new ParameterDefinition(p.Name, p.Attributes, EnsureImported(p.ParameterType)));
            return method;
        }
    }
}
