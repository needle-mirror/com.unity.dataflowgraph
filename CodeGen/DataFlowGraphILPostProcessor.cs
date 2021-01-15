using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Unity.CompilationPipeline.Common.ILPostProcessing;

namespace Unity.DataFlowGraph.CodeGen
{
    internal class DataFlowGraphILPostProcessor : ILPostProcessor
    {
        // FIXME: Adapted from EntitiesILPostProcessors - refactor to share code
        public override ILPostProcessResult Process(ICompiledAssembly compiledAssembly)
        {
            if (!WillProcess(compiledAssembly))
                return null;

#if DFG_TIME_POST_PROCESSING
            using (var watch = new CompilationWatch(compiledAssembly.Name))
#endif
            {

                var assemblyDefinition = AssemblyDefinitionFor(compiledAssembly);

                Diag diag = new Diag();

                try
                {
                    AssemblyVisitor visitor = new AssemblyVisitor();

                    visitor.Prepare(diag, assemblyDefinition);

                    if (!visitor.Process(diag, out var madeAnyChange) || !madeAnyChange)
                        return new ILPostProcessResult(null, diag.Messages);
                }
                catch (Exception e)
                {
                    diag.DFG_IE_04(e);
                    return new ILPostProcessResult(null, diag.Messages);
                }

                var pe = new MemoryStream();
                var pdb = new MemoryStream();
                var writerParameters = new WriterParameters
                {
                    SymbolWriterProvider = new PortablePdbWriterProvider(),
                    SymbolStream = pdb,
                    WriteSymbols = true
                };

                assemblyDefinition.Write(pe, writerParameters);
                return new ILPostProcessResult(new InMemoryAssembly(pe.ToArray(), pdb.ToArray()), diag.Messages);
            }
        }

        // FIXME: Boilerplate copied from EntitiesILPostProcessors - refactor to share code
        public override ILPostProcessor GetInstance()
        {
            return this;
        }

        // FIXME: Adapted from EntitiesILPostProcessors - refactor to share code
        public override bool WillProcess(ICompiledAssembly compiledAssembly)
        {
            if (compiledAssembly.Name == "Unity.DataFlowGraph")
                return true;
            return compiledAssembly.References.Any(f => Path.GetFileName(f) == "Unity.DataFlowGraph.dll") &&
                   !compiledAssembly.Name.Contains("CodeGen.Tests");
        }

        // FIXME: Boilerplate copied from EntitiesILPostProcessors - refactor to share code
        class PostProcessorAssemblyResolver : IAssemblyResolver
        {
            private readonly string[] _references;
            Dictionary<string, AssemblyDefinition> _cache = new Dictionary<string, AssemblyDefinition>();
            private ICompiledAssembly _compiledAssembly;
            private AssemblyDefinition _selfAssembly;

            public PostProcessorAssemblyResolver(ICompiledAssembly compiledAssembly)
            {
                _compiledAssembly = compiledAssembly;
                _references = compiledAssembly.References;
            }

            public void Dispose()
            {
            }

            public AssemblyDefinition Resolve(AssemblyNameReference name)
            {
                return Resolve(name, new ReaderParameters(ReadingMode.Deferred));
            }

            public AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
            {
                lock (_cache)
                {
                    if (name.Name == _compiledAssembly.Name)
                        return _selfAssembly;

                    var fileName = FindFile(name);
                    if (fileName == null)
                        return null;

                    var lastWriteTime = File.GetLastWriteTime(fileName);

                    var cacheKey = fileName + lastWriteTime.ToString();

                    if (_cache.TryGetValue(cacheKey, out var result))
                        return result;

                    parameters.AssemblyResolver = this;

                    var ms = MemoryStreamFor(fileName);

                    var pdb = fileName + ".pdb";
                    if (File.Exists(pdb))
                        parameters.SymbolStream = MemoryStreamFor(pdb);

                    var assemblyDefinition = AssemblyDefinition.ReadAssembly(ms, parameters);
                    _cache.Add(cacheKey, assemblyDefinition);
                    return assemblyDefinition;
                }
            }

            private string FindFile(AssemblyNameReference name)
            {
                var fileName = _references.FirstOrDefault(r => Path.GetFileName(r) == name.Name + ".dll");
                if (fileName != null)
                    return fileName;

                // perhaps the type comes from an exe instead
                fileName = _references.FirstOrDefault(r => Path.GetFileName(r) == name.Name + ".exe");
                if (fileName != null)
                    return fileName;

                //Unfortunately the current ICompiledAssembly API only provides direct references.
                //It is very much possible that a postprocessor ends up investigating a type in a directly
                //referenced assembly, that contains a field that is not in a directly referenced assembly.
                //if we don't do anything special for that situation, it will fail to resolve.  We should fix this
                //in the ILPostProcessing api. As a workaround, we rely on the fact here that the indirect references
                //are always located next to direct references, so we search in all directories of direct references we
                //got passed, and if we find the file in there, we resolve to it.
                foreach (var parentDir in _references.Select(Path.GetDirectoryName).Distinct())
                {
                    var candidate = Path.Combine(parentDir,name.Name + ".dll");
                    if (File.Exists(candidate))
                        return candidate;
                }

                return null;
            }

            static MemoryStream MemoryStreamFor(string fileName)
            {
                return Retry(10, TimeSpan.FromSeconds(1), () => {
                    byte[] byteArray;
                    using (var fs = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        byteArray = new byte[fs.Length];
                        var readLength = fs.Read(byteArray, 0, (int) fs.Length);
                        if (readLength != fs.Length)
                            throw new InvalidOperationException("File read length is not full length of file.");
                    }

                    return new MemoryStream(byteArray);
                });
            }

            private static MemoryStream Retry(int retryCount, TimeSpan waitTime, Func<MemoryStream> func)
            {
                try
                {
                    return func();
                }
                catch (IOException)
                {
                    if (retryCount == 0)
                        throw;
                    Console.WriteLine($"Caught IO Exception, trying {retryCount} more times");
                    Thread.Sleep(waitTime);
                    return Retry(retryCount - 1, waitTime, func);
                }
            }

            public void AddAssemblyDefinitionBeingOperatedOn(AssemblyDefinition assemblyDefinition)
            {
                _selfAssembly = assemblyDefinition;
            }
        }

        // FIXME: Boilerplate copied from EntitiesILPostProcessors - refactor to share code
        class PostProcessorReflectionImporterProvider : IReflectionImporterProvider
        {
            public IReflectionImporter GetReflectionImporter(ModuleDefinition module)
            {
                return new PostProcessorReflectionImporter(module);
            }
        }

        // FIXME: Boilerplate copied from EntitiesILPostProcessors - refactor to share code
        class PostProcessorReflectionImporter : DefaultReflectionImporter
        {
            private const string SystemPrivateCoreLib = "System.Private.CoreLib";
            private AssemblyNameReference _correctCorlib;

            public PostProcessorReflectionImporter(ModuleDefinition module) : base(module)
            {
                _correctCorlib = module.AssemblyReferences.FirstOrDefault(a => a.Name == "mscorlib" || a.Name == "netstandard" || a.Name == SystemPrivateCoreLib);
            }

            public override AssemblyNameReference ImportReference(AssemblyName reference)
            {
                if (_correctCorlib != null && reference.Name == SystemPrivateCoreLib)
                    return _correctCorlib;

                return base.ImportReference(reference);
            }
        }

        // FIXME: Boilerplate copied from EntitiesILPostProcessors - refactor to share code
        private static AssemblyDefinition AssemblyDefinitionFor(ICompiledAssembly compiledAssembly)
        {
            var resolver = new PostProcessorAssemblyResolver(compiledAssembly);
            var readerParameters = new ReaderParameters
            {
                SymbolStream = new MemoryStream(compiledAssembly.InMemoryAssembly.PdbData.ToArray()),
                SymbolReaderProvider = new PortablePdbReaderProvider(),
                AssemblyResolver = resolver,
                ReflectionImporterProvider = new PostProcessorReflectionImporterProvider(),
                ReadingMode = ReadingMode.Immediate
            };
            var peStream = new MemoryStream(compiledAssembly.InMemoryAssembly.PeData.ToArray());
            var assemblyDefinition = AssemblyDefinition.ReadAssembly(peStream, readerParameters);

            //apparently, it will happen that when we ask to resolve a type that lives inside Unity.Entities, and we
            //are also postprocessing Unity.Entities, type resolving will fail, because we do not actually try to resolve
            //inside the assembly we are processing. Let's make sure we do that, so that we can use postprocessor features inside
            //unity.entities itself as well.
            resolver.AddAssemblyDefinitionBeingOperatedOn(assemblyDefinition);

            return assemblyDefinition;
        }
    }
}
