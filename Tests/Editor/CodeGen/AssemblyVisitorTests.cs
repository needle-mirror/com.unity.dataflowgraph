using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Unity.CompilationPipeline.Common.Diagnostics;
using UnityEngine.TestTools;

namespace Unity.DataFlowGraph.CodeGen.Tests
{
    public class AssemblyVisitorTests
    {
        class FakeProcessor : ASTProcessor
        {
            public int AnalyseCalls;
            public int ParseCalls;
            public int ProcessCalls;

            public static List<FakeProcessor> CreateList(int count)
            {
                var list = new List<FakeProcessor>();
                while (list.Count < count)
                    list.Add(new FakeProcessor());
                return list;
            }

            public FakeProcessor() : base(null)
            {
            }

            public override void AnalyseConsistency(Diag diag)
            {
                AnalyseCalls++;
            }

            public override void ParseSymbols(Diag diag)
            {
                ParseCalls++;
            }

            public override void PostProcess(Diag diag, out bool mutated)
            {
                ProcessCalls++;

                mutated = false;
            }

        }

        [Test]
        public void AssemblyVisitor_WillNotProcess_CodeGenTestAssembly()
        {
            var diag = new Diag();

            using (var thisAssembly = AssemblyManager.LoadThisTestAssemblyAgain())
            {
                var visitor = new AssemblyVisitor();

                visitor.Prepare(diag, thisAssembly.Assembly);
                CollectionAssert.IsEmpty(diag.Messages);
                CollectionAssert.IsEmpty(visitor.Processors);

                var ret = visitor.Process(diag, out var madeAChange);
                Assert.True(ret);
                Assert.False(madeAChange);
                CollectionAssert.IsEmpty(diag.Messages);
            }
        }

        static List<Type> FindDotNetNodeDefinitions(Assembly assembly)
        {
            var mostBasicNodeDefinition = typeof(NodeDefinition);
            var nodes = assembly.GetTypes().Where(t => !t.IsAbstract && t.IsClass && mostBasicNodeDefinition.IsAssignableFrom(t)).ToList();
            return nodes;
        }

        [Test]
        public void AccumulateNodeDefinitions_FindsAllNodeDefinitions()
        {
            using (var testAssembly = AssemblyManager.LoadDFGTestsAssembly())
            {
                var mainModule = testAssembly.CecilAssembly.MainModule;
                var cecilTypes = AssemblyVisitor.AccumulateNodeDefinitions(mainModule);
                var dotNetNodes = FindDotNetNodeDefinitions(testAssembly.DotNetAssembly);

                Assert.AreEqual(dotNetNodes.Count, cecilTypes.Count);
                Assert.That(dotNetNodes.Count, Is.GreaterThan(100), "Expecting at least 100 NodeDefinitions in the DFG test assembly");

                var dotNetTypes = dotNetNodes.Select(n => mainModule.ImportReference(n)).ToList();

                // Sort them first to remove 15 second O(N^2) resolve comparison nightmare
                cecilTypes.Sort((a, b) => string.Compare(a.FullName, b.FullName));
                dotNetTypes.Sort((a, b) => string.Compare(a.FullName, b.FullName));

                for(int i = 0; i < cecilTypes.Count; ++i)
                {
                    Assert.AreEqual(cecilTypes[i].FullName, dotNetTypes[i].FullName);
                }
            } 
        }

        [Test]
        public void AssemblyVisitor_ListsNodeDefinitionProcessors_ForAllNodeDefinitions()
        {
            using (var testAssembly = AssemblyManager.LoadDFGTestsAssembly())
            {
                var visitor = new AssemblyVisitor();

                visitor.Prepare(new Diag(), testAssembly.CecilAssembly);

                var mainModule = testAssembly.CecilAssembly.MainModule;
                var cecilTypes = AssemblyVisitor.AccumulateNodeDefinitions(mainModule);

                Assert.GreaterOrEqual(visitor.Processors.Count, cecilTypes.Count);

                var nodeDefinitionProcessors = visitor.Processors.Where(p => p is NodeDefinitionProcessor).Cast<NodeDefinitionProcessor>().ToList();

                Assert.AreEqual(nodeDefinitionProcessors.Count, cecilTypes.Count);

                foreach(var ndp in nodeDefinitionProcessors)
                {
                    Assert.NotNull(cecilTypes.Find(ct => ct.FullName == ndp.DefinitionRoot.FullName));
                }
            }
        }

        [Test]
        public void AssemblyVisitor_CreatesDFGAssemblyProcessor_ForDFGAssembly()
        {
            using (var testAssembly = AssemblyManager.LoadDFGAssembly())
            {
                var visitor = new AssemblyVisitor();

                visitor.Prepare(new Diag(), testAssembly.CecilAssembly);

                var dfgAssemblyProcessors = visitor.Processors.Where(p => p is DFGAssemblyProcessor).Cast<DFGAssemblyProcessor>().ToList();

                Assert.AreEqual(1, dfgAssemblyProcessors.Count);

                Assert.AreEqual(dfgAssemblyProcessors[0].Module.Name, "Unity.DataFlowGraph.dll");
            }
        }

        [Test]
        public void AssemblyVisitor_CallsParseAnalyseProcess_ForAllASTProcessors()
        {
            var visitor = new AssemblyVisitor();
            var fakeProcessors = FakeProcessor.CreateList(10);
            visitor.Processors = fakeProcessors.ToList<ASTProcessor>();

            foreach (var fp in fakeProcessors)
            {
                Assert.Zero(fp.AnalyseCalls);
                Assert.Zero(fp.ParseCalls);
                Assert.Zero(fp.ProcessCalls);
            }

            visitor.Process(new Diag(), out bool madeAChange);

            foreach (var fp in fakeProcessors)
            {
                Assert.AreEqual(1, fp.AnalyseCalls);
                Assert.AreEqual(1, fp.ParseCalls);
                Assert.AreEqual(1, fp.ProcessCalls);
            }
        }

        [Test]
        public void AssemblyVisitor_AlwaysCreatesDFGLibrary()
        {
            using (var testAssembly = AssemblyManager.LoadDFGTestsAssembly())
            {
                var visitor = new AssemblyVisitor();

                visitor.Prepare(new Diag(), testAssembly.CecilAssembly);

                Assert.AreEqual(1, visitor.Processors.Count(p => p is DFGLibrary));
            }
        }

        public enum VisitStage
        {
            Parse, Analyse, Process
        }

        class EmitDiagnostic : FakeProcessor
        {
            readonly VisitStage m_VisitStage;
            readonly DiagnosticType m_DiagType;

            public EmitDiagnostic(VisitStage visitStage, DiagnosticType diagType)
            {
                m_VisitStage = visitStage;
                m_DiagType = diagType;
            }

            public string DiagnosticMessage => $"{m_VisitStage}{m_DiagType}";

            public override void ParseSymbols(Diag diag)
            {
                if (m_VisitStage == VisitStage.Parse)
                    RecordDiag(diag);
                base.ParseSymbols(diag);
            }

            public override void AnalyseConsistency(Diag diag)
            {
                if (m_VisitStage == VisitStage.Analyse)
                    RecordDiag(diag);
                base.AnalyseConsistency(diag);
            }

            public override void PostProcess(Diag diag, out bool madeAChange)
            {
                if (m_VisitStage == VisitStage.Process)
                    RecordDiag(diag);
                base.PostProcess(diag, out madeAChange);
            }

            void RecordDiag(Diag diag)
            {
                if (m_DiagType == DiagnosticType.Error)
                    diag.Error(DiagnosticMessage);
                else if (m_DiagType == DiagnosticType.Warning)
                    diag.Warning(DiagnosticMessage);
            }
        }

        [Test]
        public void EmittingError_DuringEarlySteps_CausesProcess_ToNotBeCalled_AndReturnFalse([Values(VisitStage.Parse, VisitStage.Analyse)] VisitStage step)
        {
            var visitor = new AssemblyVisitor();
            var fakeProcessors = FakeProcessor.CreateList(10);
            visitor.Processors = fakeProcessors.ToList<ASTProcessor>();

            var fakeError = new EmitDiagnostic(step, DiagnosticType.Error);

            visitor.Processors.Insert(visitor.Processors.Count/2, fakeError);
            
            var ret = visitor.Process(new Diag(), out bool madeAChange);
            Assert.False(ret);

            Assert.AreEqual(1, fakeError.AnalyseCalls);
            Assert.AreEqual(1, fakeError.ParseCalls);
            Assert.AreEqual(0, fakeError.ProcessCalls);
            foreach (var fp in fakeProcessors)
            {
                Assert.AreEqual(1, fp.AnalyseCalls);
                Assert.AreEqual(1, fp.ParseCalls);
                Assert.AreEqual(0, fp.ProcessCalls);
            }
        }

        [Test]
        public void EmittingError_InPostProcess_CausesProcess_ToReturnFalse()
        {
            var visitor = new AssemblyVisitor();
            var fakeProcessors = FakeProcessor.CreateList(10);
            visitor.Processors = fakeProcessors.ToList<ASTProcessor>();

            var fakeError = new EmitDiagnostic(VisitStage.Process, DiagnosticType.Error);

            visitor.Processors.Insert(visitor.Processors.Count/2, fakeError);

            var ret = visitor.Process(new Diag(), out bool madeAChange);
            Assert.False(ret);

            Assert.AreEqual(1, fakeError.AnalyseCalls);
            Assert.AreEqual(1, fakeError.ParseCalls);
            Assert.AreEqual(1, fakeError.ProcessCalls);
            foreach (var fp in fakeProcessors)
            {
                Assert.AreEqual(1, fp.AnalyseCalls);
                Assert.AreEqual(1, fp.ParseCalls);
                Assert.AreEqual(1, fp.ProcessCalls);
            }
        }

        [Test]
        public void EmittedDiagnostic_AreRecorded([Values] VisitStage stage, [Values] DiagnosticType diagType)
        {
            var visitor = new AssemblyVisitor();
            visitor.Processors = FakeProcessor.CreateList(10).ToList<ASTProcessor>();

            var emitDiag = new EmitDiagnostic(stage, diagType);

            visitor.Processors.Insert(visitor.Processors.Count/2, emitDiag);

            var diag = new Diag();
            var ret = visitor.Process(diag, out bool madeAChange);
            Assert.AreEqual(diagType == DiagnosticType.Warning, ret);

            Assert.AreEqual(1, diag.Messages.Count);
            Assert.AreEqual(diagType, diag.Messages[0].DiagnosticType);
            Assert.AreEqual(emitDiag.DiagnosticMessage, diag.Messages[0].MessageData);
        }

        class MakeAChange : FakeProcessor
        {
            public override void PostProcess(Diag diag, out bool madeAChange)
            {
                base.PostProcess(diag, out madeAChange);
                madeAChange = true;
            }
        }

        [Test]
        public void MutatingProcessor_CausesVisitor_ToMakeAChange()
        {
            var visitor = new AssemblyVisitor();
            visitor.Processors = FakeProcessor.CreateList(10).ToList<ASTProcessor>();

            var change = new MakeAChange();

            visitor.Processors.Insert(visitor.Processors.Count/2, change);

            var ret = visitor.Process(new Diag(), out bool madeAChange);
            Assert.True(ret);

            Assert.AreEqual(1, change.AnalyseCalls);
            Assert.AreEqual(1, change.ParseCalls);
            Assert.AreEqual(1, change.ProcessCalls);

            Assert.True(madeAChange);
        }

        [Test]
        public void NonMutatingProcessor_CausesVisitor_NotToMakeAChange()
        {
            var visitor = new AssemblyVisitor();
            visitor.Processors = FakeProcessor.CreateList(10).ToList<ASTProcessor>();

            var ret = visitor.Process(new Diag(), out bool madeAChange);
            Assert.True(ret);

            Assert.False(madeAChange);
        }

        // Encountered a lot of "sharing violation" of the test DLL on recompilation (happens if you don't dispose AssemblyDefinition). Would be good to actually test that somehow.
        [UnityTest, Explicit]
        public IEnumerator CanRecompile_AndReloadDomain_WithoutErrors()
        {
            // Use the Assert class to test conditions.
            // Use yield to skip a frame.
            yield return null;
        }
    }
}
