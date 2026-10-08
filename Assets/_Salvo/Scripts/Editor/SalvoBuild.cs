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

        [MenuItem("SALVO/Build/Windows (Release)")]
        public static string BuildRelease() => Build(true);

        /// <summary>IL2CPP needs Visual Studio 2019+ with the C++ workload, located through vswhere.</summary>
        static bool HasCppToolchain()
        {
            string vswhere = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio", "Installer", "vswhere.exe");
            if (!File.Exists(vswhere)) return false;
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(vswhere, "-latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath")
                { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
                using (var p = System.Diagnostics.Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(10000);
                    return !string.IsNullOrWhiteSpace(output);
                }
            }
            catch { return false; }
        }

        public static string Build(bool release)
        {
            var target = NamedBuildTarget.Standalone;
            // IL2CPP needs the "Windows Build Support (IL2CPP)" module; fall back to Mono when it's absent.
            string engines = Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines", "windowsstandalonesupport", "Variations");
            bool hasIl2cpp = Directory.Exists(engines) && Directory.GetDirectories(engines, "*il2cpp*").Length > 0;
            bool hasCpp = HasCppToolchain();
            bool il2cpp = release && hasIl2cpp && hasCpp;
            if (release && !hasIl2cpp) Debug.LogWarning("[SALVO] IL2CPP module not installed - release build uses Mono.");
            else if (release && !hasCpp) Debug.LogWarning("[SALVO] No Visual Studio C++ toolchain (MSVC + Windows SDK) found - release build uses Mono.");
            PlayerSettings.bundleVersion = GameRules.Version;
            PlayerSettings.SetScriptingBackend(target, il2cpp ? ScriptingImplementation.IL2CPP : ScriptingImplementation.Mono2x);
            PlayerSettings.SetIl2CppCompilerConfiguration(target, Il2CppCompilerConfiguration.Release);
            PlayerSettings.enableFrameTimingStats = !release; // dev builds log a CPU/GPU frame budget
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", release ? "Windows" : "Windows-Dev"));
            // the output folder only holds generated builds; start clean so switching Mono <-> IL2CPP works
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            var opts = new BuildPlayerOptions
            {
                scenes = System.Array.ConvertAll(System.Array.FindAll(EditorBuildSettings.scenes, sc => sc.enabled), sc => sc.path),
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

        /// <summary>
        /// Browser build for itch.io and similar hosts: Builds/WebGL (zip its contents and upload as
        /// an HTML5 game). Gzip with a decompression fallback works on hosts that don't send
        /// Content-Encoding headers; the build switches the active target, so switch back afterwards.
        /// </summary>
        [MenuItem("SALVO/Build/WebGL (Browser)")]
        public static string BuildWebGL()
        {
            var target = NamedBuildTarget.WebGL;
            PlayerSettings.bundleVersion = GameRules.Version;
            PlayerSettings.SetScriptingBackend(target, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetIl2CppCompilerConfiguration(target, Il2CppCompilerConfiguration.Release);
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.WebGL.memoryGrowthMode = WebGLMemoryGrowthMode.Geometric;
            PlayerSettings.WebGL.initialMemorySize = 256;
            PlayerSettings.WebGL.maximumMemorySize = 2048;
            PlayerSettings.WebGL.template = "PROJECT:Salvo";   // Assets/WebGLTemplates/Salvo: fills the frame, saves progress
            PlayerSettings.WebGL.nameFilesAsHashes = false;
            PlayerSettings.SetManagedStrippingLevel(target, ManagedStrippingLevel.Minimal);
            PlayerSettings.enableFrameTimingStats = false;
            PlayerSettings.defaultWebScreenWidth = 1280;
            PlayerSettings.defaultWebScreenHeight = 720;
            PlayerSettings.runInBackground = true;   // keep a hosted match ticking in a background tab
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "WebGL"));
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            var opts = new BuildPlayerOptions
            {
                scenes = System.Array.ConvertAll(System.Array.FindAll(EditorBuildSettings.scenes, sc => sc.enabled), sc => sc.path),
                locationPathName = dir,
                target = BuildTarget.WebGL,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            var s = report.summary;
            string msg = $"[SALVO] WebGL build {s.result}: {s.outputPath} {s.totalSize / (1024 * 1024)} MB in {s.totalTime.TotalMinutes:0.0} min, {s.totalErrors} errors";
            Debug.Log(msg);
            return msg;
        }
    }
}
