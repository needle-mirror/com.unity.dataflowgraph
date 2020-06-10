using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using NUnit.Framework;
using Unity.DataFlowGraph.Tests;

namespace Unity.DataFlowGraph.CodeGen.Tests
{
    static class AssemblyManager
    {
        /// <summary>
        /// Helper to ease scoped used of loaded assemblies.
        /// </summary>
        public class LiveAssemblyDefinition : IDisposable
        {
            public AssemblyDefinition Assembly;

            private bool m_DisposedValue = false; // To detect redundant calls

            protected virtual void Dispose(bool disposing)
            {
                if (!m_DisposedValue)
                {
                    if (disposing)
                        Assembly?.Dispose();

                    m_DisposedValue = true;
                }
            }

            ~LiveAssemblyDefinition()
            {
                Dispose(false);
            }

            public void Dispose()
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }
        }

        /// <summary>
        /// Helper to ease scoped used of loaded assemblies.
        /// </summary>
        public class LiveAssemblyDefinitionPair : IDisposable
        {
            public Assembly DotNetAssembly;
            public AssemblyDefinition CecilAssembly;

            bool m_DisposedValue = false; // To detect redundant calls

            protected virtual void Dispose(bool disposing)
            {
                if (!m_DisposedValue)
                {
                    if (disposing)
                        CecilAssembly?.Dispose();

                    m_DisposedValue = true;
                }
            }

            ~LiveAssemblyDefinitionPair()
            {
                Dispose(false);
            }

            public void Dispose()
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }
        }

        class OnDemandResolver : IAssemblyResolver
        {
            Dictionary<AssemblyNameReference, LiveAssemblyDefinition> m_CachedAssemblies = new Dictionary<AssemblyNameReference, LiveAssemblyDefinition>();

            public void Dispose()
            {
                foreach (var value in m_CachedAssemblies.Values)
                    value.Dispose();
            }

            public AssemblyDefinition Resolve(AssemblyNameReference name)
            {
                return Resolve(name, new ReaderParameters(ReadingMode.Deferred));
            }

            public AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
            {
                if (m_CachedAssemblies.ContainsKey(name))
                    return m_CachedAssemblies[name].Assembly;

                var assembly = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == name.Name);
                var fileName = assembly.Location;
                parameters.AssemblyResolver = this;
                parameters.SymbolStream = PdbStreamFor(fileName);
                var bytes = File.ReadAllBytes(fileName);

                m_CachedAssemblies[name] = new LiveAssemblyDefinition { Assembly = AssemblyDefinition.ReadAssembly(new MemoryStream(bytes), parameters) };

                return m_CachedAssemblies[name].Assembly;
            }

            static MemoryStream PdbStreamFor(string assemblyLocation)
            {
                var file = Path.ChangeExtension(assemblyLocation, ".pdb");
                if (!File.Exists(file))
                    return null;

                return new MemoryStream(File.ReadAllBytes(file));
            }
        }

        internal static LiveAssemblyDefinition LoadThisTestAssemblyAgain()
            => LoadArbitraryAssembly(Assembly.GetExecutingAssembly());

        internal static LiveAssemblyDefinition LoadArbitraryAssembly(Assembly assembly) =>
            new LiveAssemblyDefinition { Assembly = LoadArbitraryAssemblyInternal(assembly) };

        internal static LiveAssemblyDefinitionPair LoadDFGAssembly()
        {
            var dfgAssembly = typeof(NodeSet).Assembly;
            return new LiveAssemblyDefinitionPair
                {DotNetAssembly = dfgAssembly, CecilAssembly = LoadArbitraryAssemblyInternal(dfgAssembly)};
        }

        internal static LiveAssemblyDefinitionPair LoadDFGTestsAssembly()
        {
            var testAssembly = typeof(BasicAPITests).Assembly;
            return new LiveAssemblyDefinitionPair
                {DotNetAssembly = testAssembly, CecilAssembly = LoadArbitraryAssemblyInternal(testAssembly)};
        }

        static AssemblyDefinition LoadArbitraryAssemblyInternal(Assembly assembly)
        {
            var readerParams = new ReaderParameters();
            // TODO: Could cache and use this all around. But if tests mutate in-memory assemblies, what does that mean?
            // Shouldn't a test start from fresh?
            readerParams.AssemblyResolver = new OnDemandResolver();
            var a = AssemblyDefinition.ReadAssembly(assembly.Location, readerParams);
            Assert.NotNull(a, $"Couldn't load test assembly into Cecil: {assembly.Location}");
            return a;
        }
    }
}
