using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Unity.CompilationPipeline.Common.Diagnostics;

namespace Unity.DataFlowGraph.CodeGen
{
    interface ILocationContext
    {
        /// <summary>
        /// A human readable name describing in further detail exactly where the problem is, preferably in user code.
        /// Used, for example, in <see cref="Diag"> messages reported in the log.
        /// </summary>
        string GetLocationName();
    }

    interface IDefinitionContext
    {
        /// <summary>
        /// A human readable name describing what this processor is working on. Used, for example, in
        /// <see cref="Diag"> messages reported in the log.
        /// </summary>
        string GetContextName();
    }

    class TypeLocationContext : ILocationContext
    {
        TypeReference m_Ref;

        public static implicit operator TypeLocationContext(TypeReference reference)
        {
            return new TypeLocationContext(reference);
        }

        public TypeLocationContext(TypeReference reference)
        {
            if (reference == null)
                throw new ArgumentNullException(nameof(reference));

            m_Ref = reference;
        }

        public string GetLocationName()
        {
            return m_Ref.PrettyName();
        }
    }

    class MemberLocationContext : ILocationContext
    {
        MemberReference m_Ref;

        public static implicit operator MemberLocationContext(MemberReference reference)
        {
            return new MemberLocationContext(reference);
        }

        public MemberLocationContext(MemberReference reference)
        {
            if (reference == null)
                throw new ArgumentNullException(nameof(reference));

            m_Ref = reference;
        }

        public string GetLocationName()
        {
            return m_Ref.PrettyName();
        }
    }

    class AggrTypeContext : ILocationContext
    {
        string m_Name;

        public AggrTypeContext(IEnumerable<TypeReference> list)
        {
            m_Name = string.Join(", ", list.Select(n => n == null ? "<null>" : n.PrettyName()).ToArray());
        }

        public AggrTypeContext(IEnumerable<MemberReference> list)
        {
            m_Name = string.Join(", ", list.Select(n => n == null ? "<null>" : n.PrettyName()).ToArray());
        }

        public string GetLocationName()
        {
            return m_Name;
        }
    }

    class MethodLocationContext : ILocationContext
    {
        MethodReference m_Ref;

        public static implicit operator MethodLocationContext(MethodReference reference)
        {
            return new MethodLocationContext(reference);
        }

        public MethodLocationContext(MethodReference reference)
        {
            if (reference == null)
                throw new ArgumentNullException(nameof(reference));

            m_Ref = reference;
        }

        public string GetLocationName()
        {
            return m_Ref.PrettyName() + "(" + string.Join(",", m_Ref.Parameters.Select(p => p.ParameterType)) + ")";
        }
    }

    class FieldLocationContext : ILocationContext
    {
        FieldReference m_Ref;

        public static implicit operator FieldLocationContext(FieldReference reference)
        {
            return new FieldLocationContext(reference);
        }

        public FieldLocationContext(FieldReference reference)
        {
            if (reference == null)
                throw new ArgumentNullException(nameof(reference));

            m_Ref = reference;
        }

        public string GetLocationName()
        {
            return m_Ref.FullName;
        }
    }

    partial class Diag
    {
        public List<DiagnosticMessage> Messages = new List<DiagnosticMessage>();

        void Error(string errorName, IDefinitionContext definition, string contents, ILocationContext location = null)
        {
            var message = new DiagnosticMessage();
            message.DiagnosticType = DiagnosticType.Error;
            message.MessageData = $"Error {errorName}: While processing {definition?.GetContextName()}: {contents}";
            if (location != null)
                message.MessageData += $": {location.GetLocationName()}";
            Messages.Add(message);
        }

        public bool HasErrors()
        {
            return Messages.Any() && Messages.Any(m => m.DiagnosticType == DiagnosticType.Error);
        }
    }

}
