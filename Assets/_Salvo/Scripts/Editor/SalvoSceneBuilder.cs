using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Unity.AI.Navigation;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Salvo.EditorTools
{
    /// <summary>
    /// Builds Boot, Transition and Outpost scenes from generated content.
    /// Map FBX naming contract: NC_* render-only, SP_/ROAM_/CAM_/PAD_/PK_ markers -> gameplay objects.
    /// Menu: SALVO/Build/Scenes
    /// </summary>
    public static class SalvoSceneBuilder
    {
        const string Root = SalvoContentBuilder.Root;
        const string SceneDir = Root + "Scenes/";

        [MenuItem("SALVO/Build/Scenes")]
        public static string BuildAll()
        {
            var log = new System.Text.StringBuilder();
            if (!AssetDatabase.IsValidFolder(SceneDir.TrimEnd('/'))) AssetDatabase.CreateFolder(Root.TrimEnd('/'), "Scenes");
            BuildBoot(log);
            BuildTransition(log);
            var scenes = new List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(SceneDir + "Boot.unity", true),
                new EditorBuildSettingsScene(SceneDir + "Transition.unity", true),
            };
            foreach (var theme in Themes)
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Art/Models/Map/" + theme.name + ".fbx") == null) continue;
                BuildMap(theme, log);
                scenes.Add(new EditorBuildSettingsScene(SceneDir + theme.name + ".unity", true));
            }
            EditorBuildSettings.scenes = scenes.ToArray();
            EditorSceneManager.OpenScene(SceneDir + "Boot.unity");
            Debug.Log("[SALVO] Scenes built:\n" + log);
            return log.ToString();
        }

        // ------------------------------------------------------------------ boot
        static void BuildBoot(System.Text.StringBuilder log)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cfg = AssetDatabase.LoadAssetAtPath<GameConfig>(Root + "Resources/SalvoConfig.asset");

            var boot = new GameObject("SALVO");
            boot.AddComponent<Bootstrap>();
            boot.AddComponent<AudioManager>();
            boot.AddComponent<FxManager>();
            var doc = boot.AddComponent<UIDocument>();
            doc.panelSettings = cfg.panelSettings;
            doc.visualTreeAsset = cfg.uiLayout;
            boot.AddComponent<Hud>();
            boot.AddComponent<UIRoot>();

            var nmGo = new GameObject("NetworkManager");
            var utp = nmGo.AddComponent<UnityTransport>();
            var nm = nmGo.AddComponent<NetworkManager>();
            nm.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = utp,
                ConnectionApproval = true,
                EnableSceneManagement = true,
                TickRate = 30,
                ClientConnectionBufferTimeout = 15,
                LoadSceneTimeOut = 60,
                ForceSamePrefabs = true,
            };
            nm.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(Root + "Data/SalvoNetworkPrefabs.asset"));

            var post = new GameObject("PostProcess");
            var vol = post.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 1;
            vol.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(Root + "Art/Materials/SalvoPost.asset");

            EditorSceneManager.SaveScene(scene, SceneDir + "Boot.unity");
            log.AppendLine("Boot");
        }

        static void BuildTransition(System.Text.StringBuilder log)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camGo = new GameObject("TransitionCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.07f, 0.09f, 0.13f);
            cam.cullingMask = 0;
            camGo.AddComponent<AudioListener>();
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.renderShadows = false;
            EditorSceneManager.SaveScene(scene, SceneDir + "Transition.unity");
            log.AppendLine("Transition");
        }

        // ------------------------------------------------------------------ maps
        public class Theme
        {
            public string name, display, sky = "M_Sky", ambience = "Desert_Wind";
            public Color sun = new Color(1f, 0.94f, 0.84f), ambSky, ambEquator, ambGround, fog;
            public float sunIntensity = 1.45f, fogStart = 55f, fogEnd = 260f, killHeight = GameRules.KillHeight, shadowStrength = 0.82f;
            public Vector3 sunEuler = new Vector3(47f, -38f, 0f);
            /// <summary>Half size (x, z) of the walled play area: invisible player-only walls rise above it. Zero = none.</summary>
            public Vector2 clipHalf;
        }

        public static readonly Theme[] Themes =
        {
            new Theme
            {
                name = "Outpost", display = "Outpost", clipHalf = new Vector2(31f, 37f),
                ambSky = new Color(0.64f, 0.72f, 0.86f), ambEquator = new Color(0.74f, 0.70f, 0.64f), ambGround = new Color(0.52f, 0.44f, 0.36f),
                fog = new Color(0.80f, 0.86f, 0.89f),
            },
            new Theme
            {
                name = "Harbor", display = "Harbor", sky = "M_SkyHarbor", killHeight = -1.6f,
                sun = new Color(1f, 0.96f, 0.9f), sunIntensity = 1.35f, sunEuler = new Vector3(52f, 128f, 0f), shadowStrength = 0.75f,
                ambSky = new Color(0.62f, 0.74f, 0.92f), ambEquator = new Color(0.72f, 0.76f, 0.80f), ambGround = new Color(0.46f, 0.50f, 0.54f),
                fog = new Color(0.78f, 0.87f, 0.94f), fogStart = 70f, fogEnd = 320f,
            },
        };

        /// <summary>
        /// Invisible walls on the perimeter line, 60 m tall, that only players collide with (bullets,
        /// grenades and camera checks ignore the PlayerClip layer), so a run-and-jump off a roof can't
        /// carry anyone over the perimeter wall.
        /// </summary>
        static void PlayerClip(Vector2 half, Transform parent)
        {
            var root = new GameObject("PlayerClip").transform;
            root.SetParent(parent, false);
            const float H = 60f, T = 1f;
            void Wall(string n, Vector3 center, Vector3 size)
            {
                var go = new GameObject(n) { layer = Layers.PlayerClip };
                go.transform.SetParent(root, false);
                go.transform.position = center;
                go.AddComponent<BoxCollider>().size = size;
            }
            Wall("ClipE", new Vector3(half.x, H / 2, 0), new Vector3(T, H, half.y * 2 + T));
            Wall("ClipW", new Vector3(-half.x, H / 2, 0), new Vector3(T, H, half.y * 2 + T));
            Wall("ClipN", new Vector3(0, H / 2, half.y), new Vector3(half.x * 2 + T, H, T));
            Wall("ClipS", new Vector3(0, H / 2, -half.y), new Vector3(half.x * 2 + T, H, T));
        }

        static void BuildMap(Theme theme, System.Text.StringBuilder log)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var mapAsset = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Art/Models/Map/" + theme.name + ".fbx");
            var map = (GameObject)PrefabUtility.InstantiatePrefab(mapAsset);
            PrefabUtility.UnpackPrefabInstance(map, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            map.name = theme.name;

            var spFfa = new List<Transform>(); var spRed = new List<Transform>(); var spBlue = new List<Transform>();
            var roam = new List<Transform>(); var cams = new List<Transform>();
            Transform showcase = null, showcaseCam = null;
            var padPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Prefabs/JumpPad.prefab");
            var healthPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Prefabs/PickupHealth.prefab");
            var ammoPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Prefabs/PickupAmmo.prefab");
            var gameplay = new GameObject("Gameplay").transform;
            int colliders = 0, renderOnly = 0;

            foreach (var t in map.GetComponentsInChildren<Transform>(true).ToArray())
            {
                if (t == map.transform) continue;
                string n = t.name;
                var mf = t.GetComponent<MeshFilter>();
                if (mf != null)
                {
                    var mr = t.GetComponent<MeshRenderer>();
                    bool far = n.StartsWith("NC_Mesa") || n.StartsWith("NC_Dunes") || n.StartsWith("NC_Far") || n.StartsWith("NC_Sea") || n.StartsWith("NC_Hill") || n.StartsWith("NC_Island") || n.StartsWith("NC_Light") || n.StartsWith("NC_Sail");
                    if (n.StartsWith("NC_"))
                    {
                        t.gameObject.layer = Layers.Default;
                        GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic);
                        renderOnly++;
                    }
                    else
                    {
                        t.gameObject.layer = Layers.World;
                        var mc = t.gameObject.AddComponent<MeshCollider>();
                        mc.sharedMesh = mf.sharedMesh;
                        GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.NavigationStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
                        colliders++;
                    }
                    mr.shadowCastingMode = far || n == "Ground" ? ShadowCastingMode.Off : ShadowCastingMode.On;
                    mr.lightProbeUsage = LightProbeUsage.Off;
                    mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    mr.allowOcclusionWhenDynamic = true;
                    continue;
                }
                // Blender empties arrive with up = Blender local +Y (our "facing"), forward = -Blender Z.
                if (n.StartsWith("SP_") || n.StartsWith("ROAM_") || n.StartsWith("CAM_") || n.StartsWith("SHOWCASE") || n.StartsWith("PAD_") || n.StartsWith("PK_"))
                    t.rotation = Quaternion.LookRotation(-t.up, -t.forward);
                if (n.StartsWith("SP_FFA")) spFfa.Add(t);
                else if (n.StartsWith("SP_RED")) spRed.Add(t);
                else if (n.StartsWith("SP_BLUE")) spBlue.Add(t);
                else if (n.StartsWith("ROAM_")) roam.Add(t);
                else if (n.StartsWith("CAM_")) cams.Add(t);
                else if (n == "SHOWCASE") showcase = t;
                else if (n == "SHOWCASE_CAM") showcaseCam = t;
                else if (n.StartsWith("PAD_"))
                {
                    var m = Regex.Match(n, @"_v([0-9.]+)_h([0-9.]+)");
                    var pad = (GameObject)PrefabUtility.InstantiatePrefab(padPrefab, scene);
                    pad.transform.SetParent(gameplay, false);
                    pad.transform.SetPositionAndRotation(t.position, Quaternion.Euler(0, t.eulerAngles.y, 0));
                    var jp = pad.GetComponent<JumpPad>();
                    jp.launchVelocity = new Vector3(0, float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                                                       float.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture));
                    pad.name = "JumpPad_" + n.Substring(4, n.IndexOf("_v") - 4);
                }
                else if (n.StartsWith("PK_HEALTH") || n.StartsWith("PK_AMMO"))
                {
                    var pk = (GameObject)PrefabUtility.InstantiatePrefab(n.StartsWith("PK_HEALTH") ? healthPrefab : ammoPrefab, scene);
                    PrefabUtility.UnpackPrefabInstance(pk, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                    pk.transform.SetParent(gameplay, false);
                    pk.transform.position = t.position;
                    pk.name = n;
                }
            }
            cams.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            if (theme.clipHalf != Vector2.zero) PlayerClip(theme.clipHalf, gameplay);
            foreach (var s in spFfa.Concat(spRed).Concat(spBlue)) s.tag = "SpawnPoint";

            // ---- lighting
            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = theme.sun;
            sun.intensity = theme.sunIntensity;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = theme.shadowStrength;
            sun.shadowBias = 0.05f;
            sun.shadowNormalBias = 0.4f;
            sunGo.transform.rotation = Quaternion.Euler(theme.sunEuler);
            sunGo.AddComponent<UniversalAdditionalLightData>();

            RenderSettings.skybox = AssetDatabase.LoadAssetAtPath<Material>(Root + "Art/Materials/" + theme.sky + ".mat");
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = theme.ambSky;
            RenderSettings.ambientEquatorColor = theme.ambEquator;
            RenderSettings.ambientGroundColor = theme.ambGround;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = theme.fog;
            RenderSettings.fogStartDistance = theme.fogStart;
            RenderSettings.fogEndDistance = theme.fogEnd;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.reflectionIntensity = 0f;
            var ls = new LightingSettings { name = theme.name + "Lighting", bakedGI = false, realtimeGI = false, autoGenerate = false };
            AssetDatabase.DeleteAsset(SceneDir + theme.name + "Lighting.lighting");
            AssetDatabase.CreateAsset(ls, SceneDir + theme.name + "Lighting.lighting");
            Lightmapping.lightingSettings = ls;

            // ---- camera
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.fieldOfView = 55f;
            cam.cullingMask = ~0;
            camGo.AddComponent<AudioListener>();
            var cdata = camGo.AddComponent<UniversalAdditionalCameraData>();
            cdata.renderPostProcessing = true;
            cdata.antialiasing = AntialiasingMode.None;
            cdata.renderShadows = true;
            cdata.stopNaN = false;
            cdata.dithering = true;
            camGo.AddComponent<GameCamera>();
            if (cams.Count > 0) camGo.transform.SetPositionAndRotation(cams[0].position, cams[0].rotation);

            // ---- gameplay singletons
            var mm = new GameObject("MatchManager");
            mm.AddComponent<NetworkObject>();
            mm.AddComponent<MatchManager>();
            var info = new GameObject("MapInfo").AddComponent<MapInfo>();
            info.displayName = theme.display;
            info.killHeight = theme.killHeight;
            info.spawnsFfa = spFfa.ToArray(); info.spawnsRed = spRed.ToArray(); info.spawnsBlue = spBlue.ToArray();
            info.roamPoints = roam.ToArray(); info.menuCameraPath = cams.ToArray();
            info.showcaseSpot = showcase; info.showcaseCamera = showcaseCam; info.sun = sun;
            info.ambience = AssetDatabase.LoadAssetAtPath<AudioClip>(Root + "Audio/Ambience/" + theme.ambience + ".wav");
            new GameObject("MenuBackdrop").AddComponent<MenuBackdrop>();

            // ---- navmesh for bots
            var surf = map.AddComponent<NavMeshSurface>();
            surf.collectObjects = CollectObjects.Children;
            surf.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;
            surf.layerMask = 1 << Layers.World;
            surf.BuildNavMesh();
            string navPath = SceneDir + theme.name + "_NavMesh.asset";
            AssetDatabase.DeleteAsset(navPath);
            AssetDatabase.CreateAsset(surf.navMeshData, navPath);
            EditorUtility.SetDirty(surf);

            // in-scene NetworkObjects need their GlobalObjectIdHash generated before saving
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, SceneDir + theme.name + ".unity");
            foreach (var no in Object.FindObjectsByType<NetworkObject>(FindObjectsSortMode.None))
            {
                var m = typeof(NetworkObject).GetMethod("OnValidate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                m?.Invoke(no, null);
                EditorUtility.SetDirty(no);
            }
            EditorSceneManager.SaveScene(scene, SceneDir + theme.name + ".unity");
            log.AppendLine($"{theme.name}: {colliders} colliders, {renderOnly} render-only, spawns {spFfa.Count}/{spRed.Count}/{spBlue.Count}, roam {roam.Count}, cams {cams.Count}, showcase {(showcase != null)}");
        }
    }
}
