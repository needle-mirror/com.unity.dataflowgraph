using Mono.Cecil;

namespace Unity.DataFlowGraph.CodeGen
{
    abstract class DefinitionProcessor : ASTProcessor
    {
        public readonly TypeDefinition DefinitionRoot;
        protected readonly DFGLibrary m_Lib;
        TypeReference m_InstantiatedDefinition;

        protected DefinitionProcessor(DFGLibrary library, TypeDefinition td)
            : base(td.Module)
        {
            DefinitionRoot = td;
            m_Lib = library;
        }

        public override string GetContextName()
        {
            return DefinitionRoot.FullName;
        }

        protected MethodReference FormClassInstantiatedMethodReference(MethodReference original)
            => DeriveEnclosedMethodReference(original, InstantiatedDefinition);

        protected FieldReference FormClassInstantiatedFieldReference(FieldReference original)
            => new FieldReference(original.Name, original.FieldType, InstantiatedDefinition);

        /// <summary>
        /// Whether generic or not, this forms a reference to a scoped class context
        /// (the default in C# - auto inherited, if nothing else specified).
        ///
        /// Eg. if you are in a generic node definition, this returns
        /// <code>MyNodeDefinition<T></code>
        /// and not
        /// <code>MyNodeDefinition<></code>
        /// </summary>
        protected TypeReference InstantiatedDefinition
        {
            get
            {
                if (m_InstantiatedDefinition == null)
                {
                    if (DefinitionRoot.HasGenericParameters)
                    {
                        var instance = new GenericInstanceType(DefinitionRoot);
                        foreach (var parameter in DefinitionRoot.GenericParameters)
                            instance.GenericArguments.Add(parameter);

                        m_InstantiatedDefinition = instance;
                    }
                    else
                    {
                        m_InstantiatedDefinition = DefinitionRoot;
                    }
                }

                return m_InstantiatedDefinition;
            }
        }

        /// <summary>
        /// Create a bodiless <see cref="MethodDefinition"/> to override the given protected internal virtual method.
        /// </summary>
        /// <remarks>
        /// Assumes the method's return type has already been imported into the current module.
        /// </remarks>
        public MethodDefinition CreateEmptyProtectedInternalMethodOverride(MethodReference baseMethod)
        {
#if DFG_ASSERTIONS
            if (!baseMethod.Resolve().IsVirtual)
                throw new AssertionException("Method is not virtual");

            if (baseMethod.Resolve().IsPublic || baseMethod.Resolve().IsPrivate)
                throw new AssertionException("Method is not protected");

            if (!baseMethod.Resolve().IsFamilyOrAssembly && !baseMethod.Resolve().IsAssembly)
                throw new AssertionException("Method is not internal");

            if (baseMethod.ReturnType.Module != Module)
                throw new AssertionException("Method return type is not imported into module");
#endif
            var attributes = DFGLibrary.MethodProtectedInternalOverrideFlags | MethodAttributes.SpecialName;

            // When overriden outside the declaring assembly, a protected internal scope becomes just protected
            if (DefinitionRoot.Scope != baseMethod.DeclaringType.Scope)
                attributes = DFGLibrary.MethodProtectedOverrideFlags | MethodAttributes.SpecialName;

            return new MethodDefinition(baseMethod.Name, attributes, baseMethod.ReturnType) { HasThis = true };
        }
    }
}
