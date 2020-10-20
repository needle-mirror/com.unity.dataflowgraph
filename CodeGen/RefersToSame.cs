using System;
using Mono.Cecil;

namespace Unity.DataFlowGraph.CodeGen
{
    static partial class HelperExtensions
    {
        static bool AreSame(GenericParameter a, GenericParameter b)
        {
            return a.Position == b.Position;
        }

        static bool AreSame(GenericInstanceType a, GenericInstanceType b)
        {
            if (a.GenericArguments.Count != b.GenericArguments.Count)
                return false;

            for (int i = 0; i < a.GenericArguments.Count; i++)
                if (!RefersToSame(a.GenericArguments[i], b.GenericArguments[i]))
                    return false;

            return true;
        }

        static bool AreSame(TypeSpecification a, TypeSpecification b)
        {
            if (!RefersToSame(a.ElementType, b.ElementType))
                return false;

            if (a.IsGenericInstance && b.IsGenericInstance)
                return AreSame((GenericInstanceType)a, (GenericInstanceType)b);
            else if (a.IsGenericInstance != b.IsGenericInstance)
                return false;

            var aIsMod = a.IsRequiredModifier || a.IsOptionalModifier;
            var bIsMod = b.IsRequiredModifier || b.IsOptionalModifier;

            if (aIsMod && bIsMod)
                return RefersToSame(((IModifierType)a).ModifierType, ((IModifierType)b).ModifierType);
            else if (aIsMod != bIsMod)
                return false;

            if (a.IsArray)
                throw new NotSupportedException();

            return true;
        }

        static AssemblyNameReference GetScopeAssemblyName(TypeReference type)
        {
            var scope = type.Scope;

            if (scope == null)
                return null;

            switch (scope.MetadataScopeType)
            {
                case MetadataScopeType.AssemblyNameReference:
                    return (AssemblyNameReference)scope;

                case MetadataScopeType.ModuleDefinition:
                    return ((ModuleDefinition)scope).Assembly.Name;

                case MetadataScopeType.ModuleReference:
                    return type.Module.Assembly.Name;
            }

            throw new NotSupportedException();
        }

        static bool AreSame(AssemblyNameReference a, AssemblyNameReference b)
        {
            if (ReferenceEquals(a, b))
                return true;

            if (a == null || b == null)
                return false;

            if (a.Name != b.Name)
                return false;

            if (!Equals(a.Version, b.Version))
                return false;

            if (a.Culture != b.Culture)
                return false;

            if (CompareBytes(a.PublicKeyToken, b.PublicKeyToken) != 0)
                return false;

            return true;
        }


        static int CompareBytes(byte[] a, byte[] b)
        {
            if (ReferenceEquals(a, b))
                return 0;

            if (a == null)
                return -1;

            var diff = a.Length - b.Length;

            if (diff != 0)
                return diff;

            for (int i = 0; i < a.Length; i++)
            {
                diff = a[i] - b[i];

                if (diff != 0)
                    return diff;
            }

            return 0;
        }

        /// <summary>
        /// Implementation of https://github.com/jbevain/cecil/blob/master/Mono.Cecil/MetadataResolver.cs#L366
        /// </summary>
        static public bool RefersToSame(this TypeReference a, TypeReference b)
        {
            if (ReferenceEquals(a, b))
                return true;

            if (a == null || b == null)
                return false;

            if (a.MetadataType != b.MetadataType)
                return false;

            if (a.IsGenericParameter && b.IsGenericParameter)
                return AreSame((GenericParameter)a, (GenericParameter)b);
            else if (a.IsGenericParameter != b.IsGenericParameter)
                return false;

            if (a is TypeSpecification tsa)
                if (b is TypeSpecification tsb)
                    return AreSame(tsa, tsb);
                else
                    return false;

            if (a.Name != b.Name || a.Namespace != b.Namespace)
                return false;

            if (!RefersToSame(a.DeclaringType, b.DeclaringType))
                return false;

            if (a.DeclaringType == null && b.DeclaringType == null)
                return true;
            else if ((a.DeclaringType == null) != (b.DeclaringType == null))
                return false;

            if (a.IsPrimitive && b.IsPrimitive)
                return true;

            // non-nested types are scoped by their assembly
            return AreSame(GetScopeAssemblyName(a), GetScopeAssemblyName(b));
        }
    }
}
