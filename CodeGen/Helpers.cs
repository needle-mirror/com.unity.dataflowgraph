﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Rocks;
using Mono.Collections.Generic;

namespace Unity.DataFlowGraph.CodeGen
{
#if DFG_TIME_POST_PROCESSING
    class CompilationWatch : IDisposable
    {
        string m_Name;
        System.Diagnostics.Stopwatch m_Watch = new System.Diagnostics.Stopwatch();

        public CompilationWatch(string markerName)
        {
            m_Name = markerName;
            m_Watch.Start();
        }
        
        public void Dispose()
        {
            m_Watch.Stop();
            Console.WriteLine($"DFG: {m_Watch.ElapsedMilliseconds} ms compilation of {m_Name}");
        }
    }
#endif

    static class HelperExtensions
    {
        /// <summary>
        /// Traverses instance fields and <see cref="IEnumerable"/> collections marked <see cref="NSymbolAttribute"/>
        /// to see if they are null, in which case a <see cref="Diag.DFG_IE_01"/> error is produced in the 
        /// <paramref name="d"/>.
        /// </summary>
        static public void DiagnoseNullSymbolFields(this Diag d, IDefinitionContext parent)
        {
            var fieldsAnnotatedWithSymbols = parent
                .GetType()
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy)
                .Where(f => f.GetCustomAttributes().Any(a => a is NSymbolAttribute));

            foreach (var field in fieldsAnnotatedWithSymbols)
            {
                var value = field.GetValue(parent);
                if(value == null)
                {
                    d.DFG_IE_01(parent, field);
                }
                // Is it a collection of not null things?
                else if(typeof(IEnumerable).IsAssignableFrom(value.GetType()))
                {
                    foreach(var subValue in (IEnumerable)value)
                    {
                        if(subValue == null)
                            d.DFG_IE_01(parent, field);
                    }
                }
            }
        }

        static public bool Overrides(this TypeDefinition type, MethodReference baseFunction)
        {
            foreach(var method in type.GetMethods().Where(m => m.Name == baseFunction.Name))
            {
                if (!method.IsVirtual || method.IsNewSlot)
                    continue;

                if (method.Parameters.Count != baseFunction.Parameters.Count)
                    continue;

                for(int i = 0; i < method.Parameters.Count; ++i)
                {
                    if (!method.Parameters[i].ParameterType.RefersToSame(baseFunction.Parameters[i].ParameterType))
                        continue;
                }

                return true;
            }

            return false;
        }

        static public bool IsOrImplements(this TypeReference type, TypeReference subtype)
        {
            if (type == null)
                return false;

            if (type.RefersToSame(subtype))
                return true;

            var def1 = type.Resolve();

            // Check interface hierachy
            foreach(var iface in def1.Interfaces)
            {
                if (IsOrImplements(iface.InterfaceType, subtype))
                    return true;
            }

            // Check inheritance hierarchy
            if (def1 != null && def1.BaseType != null && def1.BaseType.IsOrImplements(subtype))
                return true;
            
            return false;
        }

        /// <summary>
        /// Partial implementation of https://github.com/jbevain/cecil/blob/master/Mono.Cecil/MetadataResolver.cs#L366
        /// </summary>
        static public bool RefersToSame(this TypeReference a, TypeReference b)
        {
            if (ReferenceEquals(a, b))
                return true;

            if (a == null || b == null)
                return false;

            // if (a.etype != b.etype)
            //    return false;

            //if (a.IsGenericParameter)
            //    return RefersToSame((GenericParameter)a, (GenericParameter)b);

            //if (a.IsTypeSpecification())
            //    return AreSame((TypeSpecification)a, (TypeSpecification)b);

            if (a.Name != b.Name || a.Namespace != b.Namespace)
                return false;

            //TODO: check scope

            return RefersToSame(a.DeclaringType, b.DeclaringType);
        }

        /// <summary>
        /// Get a Cecil <see cref="TypeReference"/> from a <see cref="System.Type"/> and import it into the module if necessary.
        /// </summary>
        public static TypeReference GetImportedReference(this ModuleDefinition module, Type type)
        {
            // TODO: Find a better way to identify if this is a type local to the module.
            if (type.Module.Name == module.Name)
                return module.GetType(type.FullName);

            return module.ImportReference(type);
        }

        /// <summary>
        /// This takes a partly open / closed <see cref="GenericInstanceType"/> and substitutes generic arguments in recursively.
        /// </summary>
        /// <param name="partial">
        /// A partly closed / open expression like SomeType{X, SomethingElse{Y}, Z}.
        /// </param>
        /// <param name="substitutionList">
        /// A replacement list like {int, A, B}
        /// </param>
        /// <returns>
        /// A new <see cref="GenericInstanceType"/> if <paramref name="partial"/> is itself a <see cref="GenericInstanceType"/>,
        /// otherwise <paramref name="partial"/> directly.
        /// 
        /// Produces SomeType{int, SomethingElse{A}, B} for the examples given.
        /// </returns>
        /// <remarks>
        /// If you want to instantiate a generic type that is not a <see cref="GenericInstanceType"/> (eg. <code>typeof(MyGeneric{,,}</code>), you need to instantiate it firstly
        /// using <see cref="TypeReferenceRocks.MakeGenericInstanceType(TypeReference, TypeReference[])"/>.
        /// </remarks>
        static TypeReference InstantiateOpenTemplate(this TypeReference partial, Collection<TypeReference> substitutionList)
        {
            var unsubstituted = partial as GenericInstanceType;

            if (unsubstituted == null)
                return partial;

            var substituted = new GenericInstanceType(partial.Resolve());

            for (int i = 0; i < unsubstituted.GenericArguments.Count; ++i)
            {
                var potentiallyGenericSubArg = unsubstituted.GenericArguments[i];
                if (potentiallyGenericSubArg.IsGenericParameter)
                {
                    var genericParameter = (GenericParameter) potentiallyGenericSubArg;
                    if (genericParameter.Position < substitutionList.Count)
                        potentiallyGenericSubArg = substitutionList[genericParameter.Position];
                }

                // No top-level change, try recursive substitution
                if (potentiallyGenericSubArg == unsubstituted.GenericArguments[i])
                    potentiallyGenericSubArg = InstantiateOpenTemplate(potentiallyGenericSubArg, substitutionList);

                substituted.GenericArguments.Add(potentiallyGenericSubArg);
            }

            return substituted;
        }

        internal static TypeReference InstantiateOpenTemplate_ForTesting(TypeReference partial, Collection<TypeReference> substitutionList)
            => partial.InstantiateOpenTemplate(substitutionList);

        /// <summary>
        /// Returns the base class in the generically instantiated context of it's referenced parent,
        /// instead of an open situation like <see cref="TypeDefinition.BaseType"/>.
        /// </summary>
        static public TypeReference InstantiatedBaseType(this TypeReference derived)
        {
            var resolvedDerived = derived.Resolve();
            var partlyReferencedBase = resolvedDerived.BaseType;

            var baseGeneric = partlyReferencedBase as GenericInstanceType;
            var derivedGeneric = derived as GenericInstanceType;

            // if base isn't generic, then we don't care.
            // if derived isn't generic, then base is concretely closed and we don't have to do substitution
            if (baseGeneric == null || derivedGeneric == null)
                return partlyReferencedBase;

            // base might at some level depend on open generic arguments from derived.
            var substitutedBase = partlyReferencedBase.InstantiateOpenTemplate(derivedGeneric.GenericArguments);

            return substitutedBase;
        }

        /// <summary>
        /// Returns whether any of the <paramref name="genericPair"/>'s instantiation's generic arguments stem from the
        /// origin definition (ie. not substituted - therefore at least partly "open").
        /// </summary>
        static public bool IsCompletelyClosed(this (TypeDefinition Definition, TypeReference Instantiated) genericPair)
        {
            if(genericPair.Instantiated is GenericInstanceType genericType)
            {
                if (genericPair.Definition.GenericParameters.Count != genericType.GenericArguments.Count)
                    return false;

                for (int i = 0; i < genericPair.Definition.GenericParameters.Count; ++i)
                    if (genericPair.Definition.GenericParameters[i] == genericType.GenericArguments[i])
                        return false;
            }

            return true;
        }

        /// <summary>
        /// Returns an enumeration of instantiated (with respect to the declaring class) nested types.
        /// </summary>
        public static IEnumerable<(TypeDefinition Definition, TypeReference Instantiated)> InstantiatedNestedTypes(this TypeReference type)
        {
            TypeDefinition resolvedType = type.Resolve();
            foreach (var nested in resolvedType.NestedTypes)
            {
                TypeReference potentiallyInstantiatedType = nested;

                if (nested.HasGenericParameters)
                {
                    // Make an open generic type
                    var genericNested = nested.MakeGenericInstanceType(nested.GenericParameters.ToArray());

                    if (type is GenericInstanceType genericType)
                    {
                        // Substitute parent's closed generic args
                        genericNested = (GenericInstanceType)genericNested.InstantiateOpenTemplate(genericType.GenericArguments);
                    }

                    potentiallyInstantiatedType = genericNested;
                }

                yield return (nested, potentiallyInstantiatedType);
            }
        }
    }


}
