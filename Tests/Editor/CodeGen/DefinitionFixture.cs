using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Mono.Cecil;
using NUnit.Framework;
using Unity.CompilationPipeline.Common.Diagnostics;
using Unity.DataFlowGraph.Tests;
using UnityEngine;

namespace Unity.DataFlowGraph.CodeGen.Tests
{
    class DefinitionFixture : IDisposable
    {
        public readonly System.Reflection.Assembly DotNetAssembly;
        public readonly AssemblyDefinition CecilAssembly;
        public readonly Type DotNetType;
        public readonly NodeDefinitionProcessor NodeProcessor;
        public PortDefinitionProcessor SimulationPortProcessor;
        public PortDefinitionProcessor KernelPortProcessor;
        public readonly DFGLibrary Library;
        public readonly TypeReference CecilNodeReference;
        public readonly TypeDefinition CecilNodeDefinition;
        public readonly Diag Diagnostic = new Diag();

        AssemblyManager.LiveAssemblyDefinition m_CecilAssembly;

        List<Regex> m_Warnings = new List<Regex>();
        List<Regex> m_Errors = new List<Regex>();

        public DefinitionFixture(Type type)
        {
            DotNetType = type;
            DotNetAssembly = DotNetType.Assembly;
            m_CecilAssembly = AssemblyManager.LoadArbitraryAssembly(DotNetAssembly);
            CecilAssembly = m_CecilAssembly.Assembly;

            CecilNodeReference = CecilAssembly.MainModule.ImportReference(DotNetType);
            CecilNodeDefinition = CecilNodeReference.Resolve();
            Library = new DFGLibrary(CecilAssembly.MainModule);

            NodeProcessor = new NodeDefinitionProcessor(Library, CecilNodeDefinition);

            Assert.NotNull(DotNetAssembly);
            Assert.NotNull(CecilAssembly);
            Assert.NotNull(DotNetType);
            Assert.NotNull(NodeProcessor);
            Assert.NotNull(Library);
            Assert.NotNull(CecilNodeReference);
            Assert.NotNull(CecilNodeDefinition);
            Assert.False(Diagnostic.HasErrors());
        }

        public void ExpectWarning(Regex r)
        {
            m_Warnings.Add(r);
        }

        public void ExpectError(Regex r)
        {
            m_Errors.Add(r);
        }

        public void ParseSymbols()
        {
            Library.ParseSymbols(Diagnostic);
            NodeProcessor.ParseSymbols(Diagnostic);
            if (NodeProcessor.SimulationPortImplementation != null)
            {
                SimulationPortProcessor =
                    new PortDefinitionProcessor(Library, NodeProcessor.SimulationPortImplementation.Resolve());
                SimulationPortProcessor.ParseSymbols(Diagnostic);
            }
            if (NodeProcessor.KernelPortImplementation != null)
            {
                KernelPortProcessor =
                    new PortDefinitionProcessor(Library, NodeProcessor.KernelPortImplementation.Resolve());
                KernelPortProcessor.ParseSymbols(Diagnostic);
            }

            CheckDiagnostics();
        }

        public void AnalyseConsistency()
        {
            Library.AnalyseConsistency(Diagnostic);
            NodeProcessor.AnalyseConsistency(Diagnostic);
            SimulationPortProcessor?.AnalyseConsistency(Diagnostic);
            KernelPortProcessor?.AnalyseConsistency(Diagnostic);
            CheckDiagnostics();
        }

        public void ParseAnalyse()
        {
            ParseSymbols();
            AnalyseConsistency();
        }

        public void Dispose()
        {
            CheckDiagnostics();

#if DFG_VERBOSE
            // Dump out transcript on success.
            foreach (var message in Diagnostic.Messages)
                Debug.Log($"Encountered expected diagnostic {message.DiagnosticType}: {message.MessageData}");
#endif
            m_CecilAssembly.Dispose();
        }

        void CheckDiagnostics()
        {
            void MatchRegex(List<Regex> expected, DiagnosticType type)
            {
                var filteredMessages = Diagnostic.Messages.Where(m => m.DiagnosticType == type).ToList();

                for(int i = 0; i < Math.Min(expected.Count, filteredMessages.Count); ++i)
                {
                    if(!expected[i].IsMatch(filteredMessages[i].MessageData))
                    {
                        Assert.Fail($"Missing diagnostic matching {expected[i]} (was {filteredMessages[i].MessageData})");
                    }
                }

                if (expected.Count > filteredMessages.Count)
                    Assert.Fail($"Missing diagnostic matching {expected[filteredMessages.Count]}");

                if (filteredMessages.Count > expected.Count)
                    Assert.Fail($"Unexpected diagnostic message {filteredMessages[expected.Count].MessageData}");
            }

            MatchRegex(m_Warnings, DiagnosticType.Warning);
            MatchRegex(m_Errors, DiagnosticType.Error);
        }
    }

    class DefinitionFixture<TNodeDefinition> : DefinitionFixture
        where TNodeDefinition : NodeDefinition
    {

        public DefinitionFixture() : base(typeof(TNodeDefinition))
        {
        }
    }

    class DefinitionFixtureTests
    {
        [Test]
        public void CanMake_Strong_DefinitionFixture()
        {
            using (var fixture = new DefinitionFixture<NodeWithAllTypesOfPorts>())
            {

            }
        }

        [Test]
        public void CanMake_Weak_DefinitionFixture()
        {
            using (var fixture = new DefinitionFixture(typeof(NodeWithAllTypesOfPorts)))
            {

            }
        }
    }
}
