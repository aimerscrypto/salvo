using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Salvo.EditorTools
{
    /// <summary>
    /// One-click, idempotent project configuration: layers, physics matrix, a lean URP setup
    /// (Forward, no extra lights, 2 cascades, 32-bit HDR), player/quality/time settings and
    /// shader stripping. Menu: SALVO/Setup/Configure Project.
    /// </summary>
    public static class SalvoProjectSetup
    {
        const string UrpAssetPath = "Assets/Settings/PC_RPAsset.asset";
        const string RendererPath = "Assets/Settings/PC_Renderer.asset";

        [MenuItem("SALVO/Setup/Configure Project")]
        public static string Run()
        {
            var log = new System.Text.StringBuilder();
            SetupLayers(log);
            SetupPhysics(log);
            SetupUrp(log);
            SetupQualityAndPlayer(log);
            AssetDatabase.SaveAssets();
            Debug.Log("[SALVO] Project configured:\n" + log);
            return log.ToString();
        }

        static void SetupLayers(System.Text.StringBuilder log)
        {
            var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tm.FindProperty("layers");
            void Set(int i, string n) { layers.GetArrayElementAtIndex(i).stringValue = n; }
            Set(Layers.World, "World");
            Set(Layers.Hitbox, "Hitbox");
            Set(Layers.Player, "Player");
            Set(Layers.Pickup, "Pickup");
            Set(Layers.ViewModel, "ViewModel");
            Set(Layers.Debris, "Debris");
            var tags = tm.FindProperty("tags");
            foreach (var t in new[] { "SpawnPoint", "JumpPad" })
            {
                bool has = false;
                for (int i = 0; i < tags.arraySize; i++) if (tags.GetArrayElementAtIndex(i).stringValue == t) has = true;
                if (!has) { tags.InsertArrayElementAtIndex(tags.arraySize); tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = t; }
            }
            tm.ApplyModifiedPropertiesWithoutUndo();
            log.AppendLine("layers/tags");
        }

        static void SetupPhysics(System.Text.StringBuilder log)
        {
            for (int a = 0; a < 32; a++)
                for (int b = 0; b < 32; b++)
                    Physics.IgnoreLayerCollision(a, b, false);
            // view model never touches anything
            for (int i = 0; i < 32; i++) Physics.IgnoreLayerCollision(Layers.ViewModel, i, true);
            // hitboxes are query-only (raycasts); they never collide with bodies
            for (int i = 0; i < 32; i++) Physics.IgnoreLayerCollision(Layers.Hitbox, i, true);
            // pickups only overlap players
            for (int i = 0; i < 32; i++) Physics.IgnoreLayerCollision(Layers.Pickup, i, i != Layers.Player);
            // debris only lands on the world
            for (int i = 0; i < 32; i++) Physics.IgnoreLayerCollision(Layers.Debris, i, !(i == Layers.World || i == Layers.Default || i == Layers.Debris));
            Physics.IgnoreLayerCollision(Layers.Player, Layers.Player, false);

            Physics.autoSyncTransforms = false;
            Physics.reuseCollisionCallbacks = true;
            Physics.defaultSolverIterations = 4;
            Physics.defaultSolverVelocityIterations = 1;
            Physics.queriesHitTriggers = false;
            Physics.queriesHitBackfaces = false;
            Time.fixedDeltaTime = 1f / 50f;
            Time.maximumDeltaTime = 0.1f;
            log.AppendLine("physics matrix + time");
        }

        static void SetupUrp(System.Text.StringBuilder log)
        {
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpAssetPath);
            var so = new SerializedObject(urp);
            void I(string p, int v) { var sp = so.FindProperty(p); if (sp != null) sp.intValue = v; else log.AppendLine("missing " + p); }
            void Fl(string p, float v) { var sp = so.FindProperty(p); if (sp != null) sp.floatValue = v; else log.AppendLine("missing " + p); }
            void B(string p, bool v) { var sp = so.FindProperty(p); if (sp != null) sp.boolValue = v; else log.AppendLine("missing " + p); }
            B("m_RequireDepthTexture", false);
            B("m_RequireOpaqueTexture", false);
            B("m_SupportsTerrainHoles", false);
            B("m_SupportsHDR", true);
            I("m_HDRColorBufferPrecision", 0);   // 32 bit R11G11B10: HDR for tonemapping at LDR bandwidth
            I("m_MSAA", 2);
            Fl("m_RenderScale", 1f);
            B("m_EnableLODCrossFade", false);
            I("m_MainLightRenderingMode", 1);
            B("m_MainLightShadowsSupported", true);
            I("m_MainLightShadowmapResolution", 2048);
            I("m_AdditionalLightsRenderingMode", 0); // disabled: one sun, zero light loops
            B("m_AdditionalLightShadowsSupported", false);
            B("m_ReflectionProbeBlending", false);
            B("m_ReflectionProbeBoxProjection", false);
            B("m_ReflectionProbeAtlas", false);
            Fl("m_ShadowDistance", 55f);
            I("m_ShadowCascadeCount", 2);
            Fl("m_Cascade2Split", 0.3f);
            Fl("m_ShadowDepthBias", 1.0f);
            Fl("m_ShadowNormalBias", 0.8f);
            B("m_SoftShadowsSupported", true);
            I("m_SoftShadowQuality", 1);   // low: 4-tap, plenty for a stylized look
            B("m_UseSRPBatcher", true);
            B("m_SupportsDynamicBatching", false);
            B("m_SupportsLightCookies", false);
            B("m_SupportsLightLayers", false);
            I("m_GPUResidentDrawerMode", 0);
            I("m_ColorGradingMode", 0);
            I("m_ColorGradingLutSize", 16);
            I("m_ShaderVariantLogLevel", 0);
            so.ApplyModifiedPropertiesWithoutUndo();

            var rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            var rso = new SerializedObject(rd);
            rso.FindProperty("m_RenderingMode").intValue = 0; // Forward
            rso.FindProperty("m_DepthPrimingMode").intValue = 0;
            var ims = rso.FindProperty("m_IntermediateTextureMode"); if (ims != null) ims.intValue = 0; // Auto
            rso.ApplyModifiedPropertiesWithoutUndo();

            // SSAO: keep the feature but make it cheap; the Settings menu toggles it at runtime.
            var ssao = rd.rendererFeatures.FirstOrDefault(f => f != null && f.GetType().Name == "ScreenSpaceAmbientOcclusion");
            if (ssao != null)
            {
                var s = new SerializedObject(ssao);
                void SI(string p, int v) { var sp = s.FindProperty(p); if (sp != null) sp.intValue = v; }
                void SF(string p, float v) { var sp = s.FindProperty(p); if (sp != null) sp.floatValue = v; }
                void SB(string p, bool v) { var sp = s.FindProperty(p); if (sp != null) sp.boolValue = v; }
                SB("m_Settings.Downsample", true);
                SB("m_Settings.AfterOpaque", false);
                SI("m_Settings.Source", 1);          // depth normals
                SI("m_Settings.NormalSamples", 0);   // low
                SF("m_Settings.Intensity", 0.6f);
                SF("m_Settings.DirectLightingStrength", 0.15f);
                SF("m_Settings.Radius", 0.25f);
                SI("m_Settings.Samples", 2);         // low
                SI("m_Settings.BlurQuality", 2);     // low
                SF("m_Settings.Falloff", 40f);
                s.ApplyModifiedPropertiesWithoutUndo();
                ssao.SetActive(false); // off by default; enabled by the High preset
            }
            EditorUtility.SetDirty(urp);
            EditorUtility.SetDirty(rd);
            log.AppendLine("urp asset + renderer");

            // Use the PC asset everywhere (runtime settings scale it per preset).
            GraphicsSettings.defaultRenderPipeline = urp;
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = urp;
            }
            QualitySettings.SetQualityLevel(QualitySettings.names.Length - 1, true);
        }

        static void SetupQualityAndPlayer(System.Text.StringBuilder log)
        {
            QualitySettings.vSyncCount = 0;
            QualitySettings.realtimeReflectionProbes = false;
            QualitySettings.softParticles = false;
            QualitySettings.billboardsFaceCameraPosition = false;
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable;
            QualitySettings.skinWeights = SkinWeights.TwoBones;
            QualitySettings.lodBias = 1.5f;

            PlayerSettings.companyName = "Salvo Games";
            PlayerSettings.productName = "SALVO";
            PlayerSettings.runInBackground = true;   // never stall the host while alt-tabbed
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.allowFullscreenSwitch = true;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.gcIncremental = true;
            PlayerSettings.usePlayerLog = true;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11 });
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Standalone, ManagedStrippingLevel.Low);
            PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Standalone, ApiCompatibilityLevel.NET_Standard);
            PlayerSettings.stripEngineCode = true;
            PlayerSettings.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
            PlayerSettings.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);

            // Shader stripping: no lightmaps, linear fog only, no instancing variants we don't use.
            var gs = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
            void G(string p, int v) { var sp = gs.FindProperty(p); if (sp != null) sp.intValue = v; }
            void GB(string p, bool v) { var sp = gs.FindProperty(p); if (sp != null) sp.boolValue = v; }
            G("m_LightmapStripping", 1);
            GB("m_LightmapKeepPlain", false);
            GB("m_LightmapKeepDirCombined", false);
            GB("m_LightmapKeepDynamicPlain", false);
            GB("m_LightmapKeepDynamicDirCombined", false);
            GB("m_LightmapKeepShadowMask", false);
            GB("m_LightmapKeepSubtractive", false);
            G("m_FogStripping", 1);
            GB("m_FogKeepLinear", true);
            GB("m_FogKeepExp", false);
            GB("m_FogKeepExp2", false);
            G("m_InstancingStripping", 0);
            gs.ApplyModifiedPropertiesWithoutUndo();

            var audio = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/AudioManager.asset")[0]);
            var rv = audio.FindProperty("m_RealVoiceCount"); if (rv != null) rv.intValue = 32;
            var vv = audio.FindProperty("m_VirtualVoiceCount"); if (vv != null) vv.intValue = 128;
            audio.ApplyModifiedPropertiesWithoutUndo();
            log.AppendLine("quality/player/graphics/audio");
        }
    }
}
