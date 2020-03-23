using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
using PackageManager = UnityEditor.PackageManager;
#endif

namespace Unity.DataFlowGraph.Tests
{
#if UNITY_EDITOR
    [InitializeOnLoad]
#endif
    public class TestRunConfig : IPrebuildSetup
    {
        static readonly string ForceIL2CPPBuildEnvVar = "FORCE_DFG_CI_IL2CPP_BUILD";
        static readonly string ForceBurstCompileEnvVar = "FORCE_DFG_CI_BURST_COMPILE";
        static readonly string ForceWarningsAsErrorsEnvVar = "FORCE_DFG_CI_WARNINGS_AS_ERRORS";
        static readonly string ForceSamplesImportEnvVar = "FORCE_DFG_CI_SAMPLES_IMPORT";
        static readonly string ForceDFGInternalAssertionsEnvVar = "FORCE_DFG_INTERNAL_ASSERTIONS";

        static bool? GetEnvVarEnabled(string name) =>
#if UNITY_EDITOR
            Environment.GetEnvironmentVariable(name) == null ? default(bool?) :
            Environment.GetEnvironmentVariable(name) == "0" ? false : true;
#else
            GameObject.Find(name + "=true") != null ? true :
            GameObject.Find(name + "=false") != null ? false : default(bool?);
#endif

#if UNITY_EDITOR
        static void BakeEnvVarToBuild(string name)
        {
            var envVar = GetEnvVarEnabled(name);
            if (envVar != null)
                new GameObject(name + ((bool) envVar ? "=true" : "=false"));
        }
#endif

        static bool? ForceIL2CPPBuild => GetEnvVarEnabled(ForceIL2CPPBuildEnvVar);

        static bool? ForceBurstCompile => GetEnvVarEnabled(ForceBurstCompileEnvVar);

        static bool? ForceWarningsAsErrors => GetEnvVarEnabled(ForceWarningsAsErrorsEnvVar);	

        static bool? ForceSamplesImport => GetEnvVarEnabled(ForceSamplesImportEnvVar);

        static bool? ForceDFGInternalAssertions => GetEnvVarEnabled(ForceDFGInternalAssertionsEnvVar);

        const string SamplesAsmDefText = @"
        {
            ""name"": ""Unity.DataFlowGraph.Samples.Test"",
            ""references"": [
                ""Unity.DataFlowGraph"",
                ""Unity.Mathematics"",
                ""Unity.Burst"",
                ""Unity.Collections""
            ]
        }";

#if UNITY_EDITOR
        static readonly List<BuildTargetGroup> ValidBuildTargetGroups =
            Enum.GetValues(typeof(BuildTargetGroup))
                .OfType<BuildTargetGroup>()
                .Where(t => t != BuildTargetGroup.Unknown)
                .Where(t => !typeof(BuildTargetGroup).GetField(t.ToString()).GetCustomAttributes(typeof(ObsoleteAttribute)).Any())
                .ToList();

        static readonly List<BuildTarget> ValidBuildTargets =
            Enum.GetValues(typeof(BuildTarget))
                .OfType<BuildTarget>()
                .Where(t => t != BuildTarget.NoTarget)
                .Where(t => !typeof(BuildTarget).GetField(t.ToString()).GetCustomAttributes(typeof(ObsoleteAttribute)).Any())
                .ToList();

        public static bool EnableBurstCompilation
        {
            // FIXME: Burst Editor Settings are not properly exposed. Use reflection to hack into it.
            //   static bool Burst.Editor.BurstEditorOptions.EnableBurstCompilation
            // Issue #245

            // NOTE: Silent failure on failed reflection is intentional.  Problems setting up Burst compilation
            // (eg. due to change of private API) will be picked up by the Burst_IsEnabled() test below.
            get =>
                (bool)AppDomain.CurrentDomain.Load("Unity.Burst.Editor")
                    ?.GetType("Unity.Burst.Editor.BurstEditorOptions")
                    ?.GetMethod("get_EnableBurstCompilation")
                    ?.Invoke(null, null);
            set =>
                AppDomain.CurrentDomain.Load("Unity.Burst.Editor")
                    ?.GetType("Unity.Burst.Editor.BurstEditorOptions")
                    ?.GetMethod("set_EnableBurstCompilation")
                    ?.Invoke(null, new object[] {value});
        }

        public static bool EnableBurstCompileSynchronously
        {
            // FIXME: Burst Editor Settings are not properly exposed. Use reflection to hack into it.
            //   static bool Burst.Editor.BurstEditorOptions.EnableBurstCompileSynchronously
            // Issue #245

            // NOTE: Silent failure on failed reflection is intentional.  Problems setting up Burst compilation
            // (eg. due to change of private API) will be picked up by the Burst_IsEnabled() test below.
            get =>
                (bool)AppDomain.CurrentDomain.Load("Unity.Burst.Editor")
                    ?.GetType("Unity.Burst.Editor.BurstEditorOptions")
                    ?.GetMethod("get_EnableBurstCompileSynchronously")
                    ?.Invoke(null, null);
            set =>
                AppDomain.CurrentDomain.Load("Unity.Burst.Editor")
                    ?.GetType("Unity.Burst.Editor.BurstEditorOptions")
                    ?.GetMethod("set_EnableBurstCompileSynchronously")
                    ?.Invoke(null, new object[] {value});
        }
#endif

        public static bool IsIL2CPPBuild => 
#if ENABLE_IL2CPP
            true;
#else
            false;
#endif

        public static bool IsDFGInternalAssertionsBuild => 
#if DFG_ASSERTIONS
            true;
#else
            false;
#endif

#if UNITY_EDITOR
        static TestRunConfig()
        {
            if (ForceIL2CPPBuild != null)
            {
                foreach (BuildTargetGroup targetGroup in ValidBuildTargetGroups)
                {
                    PlayerSettings.SetScriptingBackend(
                        targetGroup,
                        (bool) ForceIL2CPPBuild ? ScriptingImplementation.IL2CPP : ScriptingImplementation.Mono2x);
                }
            }

            if (ForceBurstCompile != null)
            {
                EnableBurstCompilation = (bool) ForceBurstCompile;
                EnableBurstCompileSynchronously = (bool) ForceBurstCompile;

                // FIXME: Burst AOT Settings are not properly exposed. Use reflection to hack into it.
                //   var burstAOTSettings =
                //       Burst.Editor.BurstPlatformAotSettings.GetOrCreateSettings(target);
                //   burstAOTSettings.EnableBurstCompilation = (bool) ForceBurstCompile;
                //   burstAOTSettings.Save(target);
                // Issue #245

                var burstAOTSettingsType =
                    AppDomain.CurrentDomain.Load("Unity.Burst.Editor")
                        ?.GetType("Unity.Burst.Editor.BurstPlatformAotSettings");

                foreach (BuildTarget target in ValidBuildTargets)
                {
                    // NOTE: Silent failure on failed reflection is intentional.  Problems setting up Burst compilation
                    // (eg. due to change of private API) will be picked up by the Burst_IsEnabled() test below.
                    var burstAOTSettings =
                        burstAOTSettingsType
                            ?.GetMethod("GetOrCreateSettings", BindingFlags.Static | BindingFlags.NonPublic)
                            ?.Invoke(null, new object[] {target});

                    if (burstAOTSettings != null)
                    {
                        burstAOTSettingsType.GetField("EnableBurstCompilation", BindingFlags.NonPublic | BindingFlags.Instance)
                            ?.SetValue(burstAOTSettings, (bool) ForceBurstCompile);

                        burstAOTSettingsType.GetMethod("Save", BindingFlags.NonPublic | BindingFlags.Instance)
                            ?.Invoke(burstAOTSettings, new object[] {target});
                    }
                }
            }

            var needAssetDBRefresh = false;
            if (ForceSamplesImport != null && (bool) ForceSamplesImport)
            {
                var thisPkg = PackageManager.PackageInfo.FindForAssembly(Assembly.GetExecutingAssembly());

                // Try to import the samples the Package Manager way, if not, do it ourselves.
                // (This fails because the Package Manager list is still refreshing at initial application launch)
                var importedSamplesRoot = Path.Combine(Application.dataPath, "Samples");
                var samples = PackageManager.UI.Sample.FindByPackage(thisPkg.name, thisPkg.version);
                if (samples.Any())
                {
                    importedSamplesRoot = samples.First().importPath;
                    foreach (var sample in samples)
                    {
                        while (!sample.importPath.StartsWith(importedSamplesRoot))
                            importedSamplesRoot = Path.GetDirectoryName(importedSamplesRoot);

                        if (!sample.isImported)
                        {
                            if (!sample.Import())
                                throw new InvalidOperationException($"Failed to import sample \"{sample.displayName}\".");
                        }
                    }
                    if (importedSamplesRoot.Length == 0)
                        throw new InvalidOperationException("Could not find common part of path for imported samples");
                }
                else if (!Directory.Exists(importedSamplesRoot))
                {
                    string samplesPath = null;
                    foreach (var path in new[] {"Samples", "Samples~"}.Select(dir => Path.Combine(thisPkg.resolvedPath, dir)))
                    {
                        if (Directory.Exists(path))
                            samplesPath = path;
                    }
                    if (samplesPath == null)
                        throw new InvalidOperationException("Could not find package Samples directory");
                    FileUtil.CopyFileOrDirectory(samplesPath, importedSamplesRoot);
                    needAssetDBRefresh = true;
                }

                if (!File.Exists(Path.Combine(importedSamplesRoot, "Samples.asmdef")))
                {
                    File.WriteAllText(Path.Combine(importedSamplesRoot, "Samples.asmdef"), SamplesAsmDefText);
                    needAssetDBRefresh = true;
                }
                if (ForceWarningsAsErrors != null)
                    needAssetDBRefresh |= EnableWarningsAsErrorsForAssembliesInPath(importedSamplesRoot, (bool) ForceWarningsAsErrors);
            }

            if (ForceDFGInternalAssertions != null)
            {
                foreach (BuildTargetGroup targetGroup in ValidBuildTargetGroups)
                {
                    var globalDefines =
                        PlayerSettings.GetScriptingDefineSymbolsForGroup(targetGroup);
                    if ((bool) ForceDFGInternalAssertions && !globalDefines.Split(';').Contains("DFG_ASSERTIONS"))
                    {
                        globalDefines += (globalDefines.Length > 0 ? ";" : "") + "DFG_ASSERTIONS";
                        PlayerSettings.SetScriptingDefineSymbolsForGroup(targetGroup, globalDefines);
                    }
                    else if (!(bool) ForceDFGInternalAssertions && globalDefines.Split(';').Contains("DFG_ASSERTIONS"))
                    {
                        globalDefines = String.Join(";", globalDefines.Split(';').Where(s => s != "DFG_ASSERTIONS").ToArray());
                        PlayerSettings.SetScriptingDefineSymbolsForGroup(targetGroup, globalDefines);
                    }
                }
            }

            if (ForceWarningsAsErrors != null)
            {
                var thisPkg = PackageManager.PackageInfo.FindForAssembly(Assembly.GetExecutingAssembly());
                needAssetDBRefresh |= EnableWarningsAsErrorsForAssembliesInPath(thisPkg.resolvedPath, (bool) ForceWarningsAsErrors);
            }

            if (needAssetDBRefresh)
                AssetDatabase.Refresh();
        }

        static bool EnableWarningsAsErrorsForAssembliesInPath(string path, bool enable)
        {
            bool requiresAssetDBRefresh = false;

            var preprocessorConfigs = Directory.EnumerateFiles(path, "*.asmdef", SearchOption.AllDirectories)
                .Where(d => !d.Contains(Path.Combine("", ".Editor", "")))
                .Select(d => Path.Combine(Path.GetDirectoryName(d), "csc.rsp"));

            foreach (var preprocessorConfig in preprocessorConfigs)
            {
                if (!File.Exists(preprocessorConfig) || new FileInfo(preprocessorConfig).Length == 0)
                {
                    if (enable)
                    {
                        File.WriteAllText(preprocessorConfig, "-warnaserror+");
                        requiresAssetDBRefresh = true;
                    }
                }
                else if (File.ReadAllText(preprocessorConfig) == "-warnaserror+")
                {
                    if (!enable)
                    {
                        File.WriteAllText(preprocessorConfig, "");
                        requiresAssetDBRefresh = true;
                    }
                }
                else
                {
                    throw new InvalidOperationException(
                        "Preprocessor config already exists but does not include \"Warnings as Errors\"");
                }
            }

            return requiresAssetDBRefresh;
        }
#endif // UNITY_EDITOR

        public void Setup()
        {
#if UNITY_EDITOR
            foreach (var envVar in new[] {ForceIL2CPPBuildEnvVar, ForceBurstCompileEnvVar, ForceWarningsAsErrorsEnvVar, ForceSamplesImportEnvVar, ForceDFGInternalAssertionsEnvVar})
            {
                BakeEnvVarToBuild(envVar);
            }
#endif // UNITY_EDITOR
        }

        [Test]
        public void IL2CPP_IsInUse()
        {
            if (ForceIL2CPPBuild != null)
            {
                Assert.AreEqual(
                    (bool) ForceIL2CPPBuild,
                    IsIL2CPPBuild,
                    ((bool) ForceIL2CPPBuild ? "Expected" : "Did not expect") + " to be running in IL2CPP");
            }

            if (!IsIL2CPPBuild)
                Assert.Ignore("This is not an IL2CPP build.");
        }

        [Test]
        public void Burst_IsEnabled()
        {
#if UNITY_EDITOR
            if (ForceBurstCompile != null && BurstConfig.IsBurstEnabled)
            {
                Assert.IsTrue(
                    EnableBurstCompileSynchronously,
                    "Expecting job compilation to be synchronous when Burst compiling.");
            }
#endif

            if (ForceBurstCompile != null)
            {
                Assert.AreEqual(
                    (bool) ForceBurstCompile,
                    BurstConfig.IsBurstEnabled,
                    ((bool) ForceBurstCompile ? "Expected" : "Did not expect") + " Burst to be enabled");
            }

            if (!BurstConfig.IsBurstEnabled)
                Assert.Ignore("Burst disabled.");
        }

        [Test]
        public void DFGAssertions_AreEnabled()
        {
            if (ForceDFGInternalAssertions != null)
            {
                Assert.AreEqual(
                    (bool) ForceDFGInternalAssertions,
                    IsDFGInternalAssertionsBuild,
                    ((bool) ForceDFGInternalAssertions ? "Expected" : "Did not expect") + " internal DFG assertions to be in effect.");
            }

            if (!IsDFGInternalAssertionsBuild)
                Assert.Ignore("This build does not have internal DFG assertions enabled.");
        }

        [Test]
        public void PackageSamples_ArePresent()
        {
            // Look for any one known sample Type and presume this indicates that they have been properly imported.
            bool sampleDetected =
                AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetType("Unity.DataFlowGraph.TimeExample.TimeExample") != null);

            if (ForceSamplesImport != null)
            {
                Assert.AreEqual(
                    (bool) ForceSamplesImport,
                    sampleDetected,
                    ((bool) ForceSamplesImport ? "Expected" : "Did not expect") + " to find package samples");
            }

            if (!sampleDetected)
                Assert.Ignore("Package samples not detected.");
        }
    }
}
