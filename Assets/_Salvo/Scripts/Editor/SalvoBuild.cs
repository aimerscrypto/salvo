using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Salvo.EditorTools
{
    /// <summary>
    /// Player builds. Release uses IL2CPP (faster runtime, harder to tamper); Dev uses Mono for
    /// quick iteration. Output: Builds/Windows[-Dev]/SALVO.exe
    /// </summary>
    public static class SalvoBuild
    {
        [MenuItem("SALVO/Build/Windows (Dev, Mono)")]
        public static string BuildDev() => Build(false);

        [MenuItem("SALVO/Build/Windows (Release, IL2CPP)")]
        public static string BuildRelease() => Build(true);

        public static string Build(bool release)
        {
            var target = NamedBuildTarget.Standalone;
            PlayerSettings.SetScriptingBackend(target, release ? ScriptingImplementation.IL2CPP : ScriptingImplementation.Mono2x);
            PlayerSettings.SetIl2CppCompilerConfiguration(target, Il2CppCompilerConfiguration.Release);
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", release ? "Windows" : "Windows-Dev"));
            Directory.CreateDirectory(dir);
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/_Salvo/Scenes/Boot.unity", "Assets/_Salvo/Scenes/Transition.unity", "Assets/_Salvo/Scenes/Outpost.unity" },
                locationPathName = Path.Combine(dir, "SALVO.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = release ? BuildOptions.None : BuildOptions.Development,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            var s = report.summary;
            string msg = $"[SALVO] Build {s.result}: {s.outputPath} {s.totalSize / (1024 * 1024)} MB in {s.totalTime.TotalMinutes:0.0} min, {s.totalErrors} errors";
            Debug.Log(msg);
            return msg;
        }
    }
}
