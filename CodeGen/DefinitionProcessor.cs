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
    }
}
