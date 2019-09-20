using System;
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
    public class TestRunConfig : IPrebuildSetup
    {
        static readonly string ForceIL2CPPBuildEnvVar = "FORCE_IL2CPP_BUILD";
        static readonly string ForceBurstCompileEnvVar = "FORCE_BURST_COMPILE";
        static readonly string ForceSamplesImportEnvVar = "FORCE_SAMPLES_IMPORT";

        static bool? GetEnvVarEnabled(string name) => 
            Environment.GetEnvironmentVariable(name) == null ? default(bool?) :
            Environment.GetEnvironmentVariable(name) == "0" ? false : true;

        static bool? ForceIL2CPPBuild => GetEnvVarEnabled(ForceIL2CPPBuildEnvVar);

        static bool? ForceBurstCompile => GetEnvVarEnabled(ForceBurstCompileEnvVar);

        static bool? ForceSamplesImport => GetEnvVarEnabled(ForceSamplesImportEnvVar);

#if UNITY_EDITOR
        public static bool EnableBurstCompilation
        {
            // FIXME: Burst Editor Settings are not properly exposed. Use reflection to hack into it.
            //   static bool Burst.Editor.BurstEditorOptions.EnableBurstCompilation
            get =>
                (bool)AppDomain.CurrentDomain.Load("Unity.Burst.Editor")
                    .GetType("Unity.Burst.Editor.BurstEditorOptions")
                    .GetMethod("get_EnableBurstCompilation")
                    .Invoke(null, null);
            set =>
                AppDomain.CurrentDomain.Load("Unity.Burst.Editor")
                    .GetType("Unity.Burst.Editor.BurstEditorOptions")
                    .GetMethod("set_EnableBurstCompilation")
                    .Invoke(null, new object[] {value});
        }

        public static bool EnableBurstCompileSynchronously
        {
            // FIXME: Burst Editor Settings are not properly exposed. Use reflection to hack into it.
            //   static bool Burst.Editor.BurstEditorOptions.EnableBurstCompileSynchronously
            get =>
                (bool)AppDomain.CurrentDomain.Load("Unity.Burst.Editor")
                    .GetType("Unity.Burst.Editor.BurstEditorOptions")
                    .GetMethod("get_EnableBurstCompileSynchronously")
                    .Invoke(null, null);
            set =>
                AppDomain.CurrentDomain.Load("Unity.Burst.Editor")
                    .GetType("Unity.Burst.Editor.BurstEditorOptions")
                    .GetMethod("set_EnableBurstCompileSynchronously")
                    .Invoke(null, new object[] {value});
        }
#endif

        public static bool IsIL2CPPBuild => 
#if ENABLE_IL2CPP
            true;
#else
            false;
#endif

        public void Setup()
        {
#if UNITY_EDITOR
            if (ForceIL2CPPBuild != null)
            {
                PlayerSettings.SetScriptingBackend(
                    EditorUserBuildSettings.selectedBuildTargetGroup,
                    (bool) ForceIL2CPPBuild ? ScriptingImplementation.IL2CPP : ScriptingImplementation.Mono2x);
            }

            if (ForceBurstCompile != null)
            {
                EnableBurstCompilation = (bool) ForceBurstCompile;
                EnableBurstCompileSynchronously = (bool) ForceBurstCompile;

                // FIXME: Burst AOT Settings are not properly exposed. Use reflection to hack into it.
                //   var burstAOTSettings =
                //       Burst.Editor.BurstPlatformAotSettings.GetOrCreateSettings(EditorUserBuildSettings.selectedStandaloneTarget);
                //   burstAOTSettings.DisableBurstCompilation = !(bool) ForceBurstCompile;
                //   burstAOTSettings.Save(EditorUserBuildSettings.selectedStandaloneTarget);

                var burstAOTSettingsType =
                    AppDomain.CurrentDomain.Load("Unity.Burst.Editor")
                        .GetType("Unity.Burst.Editor.BurstPlatformAotSettings");

                var burstAOTSettings =
                    burstAOTSettingsType.GetMethod("GetOrCreateSettings", BindingFlags.Static | BindingFlags.NonPublic)
                        .Invoke(null, new object[] {EditorUserBuildSettings.selectedStandaloneTarget});

                burstAOTSettingsType.GetField("DisableBurstCompilation", BindingFlags.NonPublic | BindingFlags.Instance)
                    .SetValue(burstAOTSettings, !(bool) ForceBurstCompile);

                burstAOTSettingsType.GetMethod("Save", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(burstAOTSettings, new object[] {EditorUserBuildSettings.selectedStandaloneTarget});
            }

            if (ForceSamplesImport != null && (bool) ForceSamplesImport)
            {
                var thisPkg = PackageManager.PackageInfo.FindForAssembly(Assembly.GetExecutingAssembly());

                // Try to import the samples the Package Manager way, if not, do it ourselves.
                // (This fails because the Package Manager list is still refreshing at initial application launch)
                var samples = PackageManager.UI.Sample.FindByPackage(thisPkg.name, thisPkg.version);
                if (samples.Any())
                {
                    foreach (var sample in samples)
                    {
                        if (!sample.isImported)
                        {
                            if (!sample.Import())
                                throw new InvalidOperationException($"Failed to import sample \"{sample.displayName}\".");
                        }
                    }
                }
                else if (!Directory.Exists(Path.Combine(Application.dataPath, "Samples")))
                {
                    string samplesPath = null;
                    foreach (var path in new[] {"Samples", "Samples~"}.Select(dir => Path.Combine(thisPkg.resolvedPath, dir)))
                    {
                        if (Directory.Exists(path))
                            samplesPath = path;
                    }
                    if (samplesPath == null)
                        throw new InvalidOperationException("Could not find package Samples directory");
                    FileUtil.CopyFileOrDirectory(samplesPath, Path.Combine(Application.dataPath, "Samples"));
                    AssetDatabase.Refresh();
                }
            }
#endif
        }

        [Test]
        public void IL2CPP_IsInUse()
        {
            if (ForceIL2CPPBuild != null)
            {
                // This only really makes sense for the Editor, or, on standalone platforms where the Editor build step
                // runs with the same environment as the Player invocation.
                Assert.AreEqual(
                    (bool) ForceIL2CPPBuild,
                    IsIL2CPPBuild,
                    $"{ForceIL2CPPBuildEnvVar} environment variable is {((bool) ForceIL2CPPBuild ? "enabled" : "disabled")}," +
                    $" but we {(IsIL2CPPBuild ? "are" : "are not")} running IL2CPP");
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
                // This only really makes sense for the Editor, or, on standalone platforms where the Editor build step
                // runs with the same environment as the Player invocation.
                Assert.AreEqual(
                    (bool) ForceBurstCompile,
                    BurstConfig.IsBurstEnabled,
                    $"{ForceBurstCompileEnvVar} environment variable is {((bool) ForceBurstCompile ? "enabled" : "disabled")}," +
                    $" but we {(BurstConfig.IsBurstEnabled ? "are" : "are not")} Burst compiling");

            }

            if (!BurstConfig.IsBurstEnabled)
                Assert.Ignore("Burst compilation is disabled.");
        }

        [Test]
        public void PackageSamples_ArePresent()
        {
            // Look for any one known sample Type and presume this indicates that they have been properly imported.
            bool sampleDetected =
                AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetType("TimeExample.TimeExample") != null);

            if (ForceSamplesImport != null && (bool) ForceSamplesImport)
            {
                // This only really makes sense for the Editor, or, on standalone platforms where the Editor build step
                // runs with the same environment as the Player invocation.
                Assert.IsTrue(
                    sampleDetected,
                    $"{ForceSamplesImportEnvVar} environment variable is enabled, but no package samples were detected.");

            }

            if (!sampleDetected)
                Assert.Ignore("Package samples not detected.");
        }
    }
}
