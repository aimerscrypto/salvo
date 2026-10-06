using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace Salvo.EditorTools
{
    /// <summary>
    /// Generates every data asset the game needs from the Blender/Python outputs:
    /// materials, weapon + skin definitions, the sound bank, prefabs and the GameConfig.
    /// Idempotent: re-running updates assets in place (GUIDs stay stable).
    /// Menu: SALVO/Build/Content
    /// </summary>
    public static class SalvoContentBuilder
    {
        public const string Root = "Assets/_Salvo/";
        const string Mat = Root + "Art/Materials/";
        const string Tex = Root + "Art/Textures/";
        const string Models = Root + "Art/Models/";
        const string Data = Root + "Data/";
        const string Prefabs = Root + "Prefabs/";
        const string Audio = Root + "Audio/";

        [MenuItem("SALVO/Build/Content")]
        public static string BuildAll()
        {
            var log = new System.Text.StringBuilder();
            Dir(Mat); Dir(Data); Dir(Prefabs); Dir(Root + "Resources/"); Dir(Data + "Weapons/"); Dir(Data + "Skins/");
            BuildMaterials(log);
            ReimportModels(log);
            var sounds = BuildSoundBank(log);
            var weapons = BuildWeapons(log);
            var skins = BuildSkins(log);
            var combatant = BuildCombatant(log);
            BuildPickupsAndPads(log);
            BuildVolumeAndUI(log);
            BuildFontAssets(log);
            var variants = BuildShaderVariants(log);
            BuildConfig(weapons, skins, sounds, combatant, log);
            var cfgAsset = AssetDatabase.LoadAssetAtPath<GameConfig>(Root + "Resources/SalvoConfig.asset");
            cfgAsset.shaderVariants = variants;
            EditorUtility.SetDirty(cfgAsset);
            AssetDatabase.SaveAssets();
            Debug.Log("[SALVO] Content built:\n" + log);
            return log.ToString();
        }

        static void Dir(string p) { if (!AssetDatabase.IsValidFolder(p.TrimEnd('/'))) Directory.CreateDirectory(p); AssetDatabase.Refresh(); }

        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a == null) { a = ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(a, path); }
            return a;
        }

        static Color Hex(string h) { ColorUtility.TryParseHtmlString("#" + h, out var c); return c; }
        static Texture2D T(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + name + ".png");
        static AudioClip A(string path) => AssetDatabase.LoadAssetAtPath<AudioClip>(Audio + path + ".wav");

        // ------------------------------------------------------------------ materials
        static Material MakeMat(string name, Shader shader, System.Action<Material> setup)
        {
            string path = Mat + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            setup(m);
            m.enableInstancing = false;
            EditorUtility.SetDirty(m);
            return m;
        }

        static void BuildMaterials(System.Text.StringBuilder log)
        {
            var lit = Shader.Find("Salvo/StylizedLit");
            var fx = Shader.Find("Salvo/FxUnlit");
            var sky = Shader.Find("Salvo/Sky");
            var detail = T("T_Detail");
            MakeMat("M_World", lit, m =>
            {
                m.SetTexture("_DetailMap", detail); m.SetFloat("_DetailScale", 0.32f); m.SetFloat("_DetailStrength", 0.2f);
                m.SetFloat("_ObjectSpace", 0); m.SetFloat("_SpecStrength", 0.04f); m.SetFloat("_RampSoftness", 0.5f);
            });
            MakeMat("M_Character", lit, m =>
            {
                m.SetTexture("_DetailMap", detail); m.SetFloat("_DetailScale", 3f); m.SetFloat("_DetailStrength", 0.08f);
                m.SetFloat("_ObjectSpace", 1); m.SetFloat("_SpecStrength", 0.1f); m.SetFloat("_RampSoftness", 0.45f);
            });
            MakeMat("M_Weapon", lit, m =>
            {
                m.SetTexture("_DetailMap", detail); m.SetFloat("_DetailScale", 6f); m.SetFloat("_DetailStrength", 0.08f);
                m.SetFloat("_ObjectSpace", 1); m.SetFloat("_SpecStrength", 0.22f); m.SetFloat("_SpecSize", 60f); m.SetFloat("_RampSoftness", 0.4f);
            });
            MakeMat("M_SkinTemplate", lit, m =>
            {
                m.SetTexture("_DetailMap", detail); m.SetColor("_BaseColor", Hex("4A515C")); m.SetFloat("_DetailScale", 4f);
                m.SetFloat("_DetailStrength", 0.12f); m.SetFloat("_ObjectSpace", 1); m.SetFloat("_SpecStrength", 0.18f); m.SetFloat("_SpecSize", 50f);
            });
            MakeMat("M_Glass", lit, m =>
            {
                m.SetColor("_BaseColor", Hex("3F6F80")); m.SetFloat("_DetailStrength", 0f); m.SetFloat("_SpecStrength", 0.7f);
                m.SetFloat("_SpecSize", 120f); m.SetFloat("_AOStrength", 0f);
            });
            MakeMat("M_TeamRed", lit, m =>
            {
                m.SetColor("_BaseColor", Hex("E0614F")); m.SetTexture("_DetailMap", detail); m.SetFloat("_DetailScale", 3f);
                m.SetFloat("_DetailStrength", 0.1f); m.SetFloat("_ObjectSpace", 1); m.SetFloat("_SpecStrength", 0.08f);
            });
            MakeMat("M_TeamBlue", lit, m =>
            {
                m.SetColor("_BaseColor", Hex("4A8FD6")); m.SetTexture("_DetailMap", detail); m.SetFloat("_DetailScale", 3f);
                m.SetFloat("_DetailStrength", 0.1f); m.SetFloat("_ObjectSpace", 1); m.SetFloat("_SpecStrength", 0.08f);
            });
            System.Action<Material, Texture2D, int, int, float, float> fxSet = (m, tex, src, dst, depthOff, fog) =>
            {
                m.SetTexture("_BaseMap", tex); m.SetFloat("_SrcBlend", src); m.SetFloat("_DstBlend", dst);
                m.SetFloat("_DepthOffset", depthOff); m.SetFloat("_FogAmount", fog); m.renderQueue = 3000;
            };
            MakeMat("M_FxPuff", fx, m => fxSet(m, T("T_Puff"), 5, 10, 0, 1));
            MakeMat("M_FxTracer", fx, m => fxSet(m, T("T_Tracer"), 5, 10, 0, 0.5f));
            MakeMat("M_FxDecal", fx, m => { fxSet(m, T("T_BulletHole"), 5, 10, -2, 1); m.renderQueue = 2450; });
            MakeMat("M_FxFlash", fx, m => fxSet(m, T("T_MuzzleFlash"), 5, 10, 0, 0));
            MakeMat("M_Sky", sky, m =>
            {
                m.SetTexture("_CloudMap", T("T_CloudNoise"));
                m.SetColor("_ZenithColor", Hex("4F8FC9")); m.SetColor("_HorizonColor", Hex("CFE2EA"));
                m.SetColor("_GroundColor", Hex("D9C8A6")); m.SetColor("_SunColor", Hex("FFF4DC"));
                m.SetFloat("_CloudCover", 0.42f); m.SetFloat("_CloudScale", 0.55f);
            });
            MakeMat("M_SkyHarbor", sky, m =>
            {
                m.SetTexture("_CloudMap", T("T_CloudNoise"));
                m.SetColor("_ZenithColor", Hex("3E86D1")); m.SetColor("_HorizonColor", Hex("D6EAF5"));
                m.SetColor("_GroundColor", Hex("7FB8CC")); m.SetColor("_SunColor", Hex("FFF8E8"));
                m.SetFloat("_CloudCover", 0.32f); m.SetFloat("_CloudScale", 0.5f);
            });
            log.AppendLine("materials");
        }

        static void ReimportModels(System.Text.StringBuilder log)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { Models.TrimEnd('/') }))
                AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);
            log.AppendLine("models reimported with remaps");
        }

        static GameObject Model(string rel) => AssetDatabase.LoadAssetAtPath<GameObject>(Models + rel + ".fbx");

        // ------------------------------------------------------------------ audio
        static SoundBank BuildSoundBank(System.Text.StringBuilder log)
        {
            var b = LoadOrCreate<SoundBank>(Data + "SoundBank.asset");
            b.footsteps = Enumerable.Range(0, 5).Select(i => A("Player/Step_" + i)).ToArray();
            b.jump = A("Player/Jump"); b.land = A("Player/Land"); b.slide = A("Player/Slide"); b.jumpPad = A("Player/JumpPad");
            b.hurt = A("Player/Hurt"); b.death = A("Player/Death"); b.bulletImpact = A("Player/Impact"); b.bulletWhiz = A("Player/Whiz");
            b.hitmarker = A("Feedback/Hitmarker"); b.headshot = A("Feedback/Headshot"); b.killConfirm = A("Feedback/Kill");
            b.pickupHealth = A("Feedback/Pickup_Health"); b.pickupAmmo = A("Feedback/Pickup_Ammo");
            b.emptyClick = A("Weapons/Empty_Click"); b.meleeSwing = A("Weapons/Knife_Swing"); b.meleeHit = A("Weapons/Knife_Hit");
            b.uiHover = A("UI/Hover"); b.uiClick = A("UI/Click"); b.uiBack = A("UI/Back"); b.uiBuy = A("UI/Buy"); b.uiError = A("UI/Error");
            b.uiReward = A("UI/Reward"); b.levelUp = A("UI/LevelUp"); b.countdown = A("UI/Countdown"); b.matchStart = A("UI/MatchStart");
            b.victory = A("UI/Victory"); b.defeat = A("UI/Defeat");
            b.menuMusic = A("Music/MenuTheme"); b.ambience = A("Ambience/Desert_Wind");
            EditorUtility.SetDirty(b);
            log.AppendLine("sound bank");
            return b;
        }

        // ------------------------------------------------------------------ weapons
        static WeaponDef W(string file, byte id, string name, System.Action<WeaponDef> setup)
        {
            var w = LoadOrCreate<WeaponDef>(Data + "Weapons/" + file + ".asset");
            w.id = id; w.displayName = name;
            setup(w);
            w.model = Model("Weapons/W_" + name);
            w.icon = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "Art/Icons/Icon_" + name + ".png");
            EditorUtility.SetDirty(w);
            return w;
        }

        static WeaponDef[] BuildWeapons(System.Text.StringBuilder log)
        {
            var mag = A("Weapons/Mag_Out"); var magIn = A("Weapons/Mag_In"); var equip = A("Weapons/Equip");
            var list = new List<WeaponDef>
            {
                W("W00_Viper", 0, "Viper", w =>
                {
                    w.description = "Reliable semi-auto sidearm. Quick to draw, punishing in steady hands.";
                    w.category = "PISTOL"; w.slot = WeaponSlot.Secondary; w.fireMode = FireMode.Semi; w.ownedByDefault = true; w.supportGrip = SupportGrip.Pistol;
                    w.damage = 27; w.headMultiplier = 1.8f; w.limbMultiplier = 0.85f; w.falloffStart = 15; w.falloffEnd = 38; w.minDamageFactor = 0.6f;
                    w.rpm = 420; w.magSize = 12; w.reserveAmmo = 60; w.reloadTime = 1.35f; w.equipTime = 0.3f; w.moveSpeedMultiplier = 1.04f;
                    w.hipSpread = 1.3f; w.adsSpread = 0.25f; w.moveSpreadAdd = 1.0f; w.airSpreadAdd = 3f; w.spreadPerShot = 0.55f; w.maxBloom = 2.5f; w.bloomRecovery = 6f;
                    w.recoilPitch = 1.5f; w.recoilYaw = 0.4f; w.recoilRecovery = 10f; w.kickBack = 0.05f; w.kickRotation = 6f;
                    w.adsFov = 68; w.adsTime = 0.13f; w.adsMoveMultiplier = 0.85f;
                    w.hipPosition = new Vector3(0.2f, -0.21f, 0.52f); w.hipEuler = new Vector3(0, -1, 0); w.sightDistance = 0.5f;
                    w.tracerColor = new Color(1f, 0.92f, 0.65f); w.shakeOnFire = 0.08f;
                    w.fireSound = A("Weapons/Viper_Fire"); w.reloadOut = mag; w.reloadIn = A("Weapons/Slide_Rack"); w.equipSound = equip;
                }),
                W("W01_Hornet", 1, "Hornet", w =>
                {
                    w.description = "Compact SMG with a blistering fire rate. Owns close quarters, sprays at range.";
                    w.category = "SMG"; w.slot = WeaponSlot.Primary; w.fireMode = FireMode.Auto; w.price = 1200; w.unlockLevel = 2; w.supportGrip = SupportGrip.Foregrip;
                    w.damage = 17; w.headMultiplier = 1.5f; w.limbMultiplier = 0.85f; w.falloffStart = 10; w.falloffEnd = 26; w.minDamageFactor = 0.55f;
                    w.rpm = 880; w.magSize = 32; w.reserveAmmo = 160; w.reloadTime = 1.75f; w.equipTime = 0.35f; w.moveSpeedMultiplier = 1.0f;
                    w.hipSpread = 2.2f; w.adsSpread = 0.9f; w.moveSpreadAdd = 0.8f; w.airSpreadAdd = 2.5f; w.spreadPerShot = 0.16f; w.maxBloom = 2.4f; w.bloomRecovery = 9f;
                    w.recoilPitch = 0.55f; w.recoilYaw = 0.35f; w.recoilRecovery = 12f; w.kickBack = 0.03f; w.kickRotation = 2.5f;
                    w.adsFov = 66; w.adsTime = 0.14f; w.adsMoveMultiplier = 0.85f;
                    w.hipPosition = new Vector3(0.2f, -0.23f, 0.46f); w.hipEuler = new Vector3(0, -1, 0); w.sightDistance = 0.24f;
                    w.tracerColor = new Color(1f, 0.9f, 0.6f); w.shakeOnFire = 0.05f;
                    w.fireSound = A("Weapons/Hornet_Fire"); w.reloadOut = mag; w.reloadIn = magIn; w.equipSound = equip;
                }),
                W("W02_Ranger", 2, "Ranger", w =>
                {
                    w.description = "All-round assault rifle. Controllable recoil and solid damage at every range.";
                    w.category = "ASSAULT RIFLE"; w.slot = WeaponSlot.Primary; w.fireMode = FireMode.Auto; w.ownedByDefault = true;
                    w.damage = 24; w.headMultiplier = 1.6f; w.limbMultiplier = 0.85f; w.falloffStart = 25; w.falloffEnd = 60; w.minDamageFactor = 0.7f;
                    w.rpm = 640; w.magSize = 30; w.reserveAmmo = 120; w.reloadTime = 2.05f; w.equipTime = 0.45f; w.moveSpeedMultiplier = 0.95f;
                    w.hipSpread = 2.6f; w.adsSpread = 0.35f; w.moveSpreadAdd = 1.4f; w.airSpreadAdd = 4f; w.spreadPerShot = 0.22f; w.maxBloom = 2.8f; w.bloomRecovery = 7f;
                    w.recoilPitch = 0.85f; w.recoilYaw = 0.3f; w.recoilRecovery = 9f; w.kickBack = 0.035f; w.kickRotation = 3.5f;
                    w.adsFov = 56; w.adsTime = 0.17f; w.adsMoveMultiplier = 0.75f;
                    w.hipPosition = new Vector3(0.2f, -0.24f, 0.5f); w.hipEuler = new Vector3(0, -1, 0); w.sightDistance = 0.26f;
                    w.tracerColor = new Color(1f, 0.88f, 0.58f); w.shakeOnFire = 0.07f;
                    w.fireSound = A("Weapons/Ranger_Fire"); w.reloadOut = mag; w.reloadIn = magIn; w.equipSound = equip;
                }),
                W("W03_Bulldog", 3, "Bulldog", w =>
                {
                    w.description = "Pump-action shotgun. Nine pellets of bad news inside ten metres.";
                    w.category = "SHOTGUN"; w.slot = WeaponSlot.Primary; w.fireMode = FireMode.Pump; w.price = 1500; w.unlockLevel = 3;
                    w.damage = 13; w.pellets = 9; w.headMultiplier = 1.3f; w.limbMultiplier = 0.9f; w.falloffStart = 7; w.falloffEnd = 20; w.minDamageFactor = 0.3f; w.range = 60;
                    w.rpm = 72; w.magSize = 6; w.reserveAmmo = 30; w.reloadTime = 0.45f; w.reloadPerShell = true; w.equipTime = 0.5f; w.moveSpeedMultiplier = 0.95f;
                    w.hipSpread = 4.8f; w.adsSpread = 3.6f; w.moveSpreadAdd = 0.5f; w.airSpreadAdd = 1.5f; w.spreadPerShot = 0f; w.maxBloom = 0f;
                    w.recoilPitch = 4.5f; w.recoilYaw = 1.2f; w.recoilRecovery = 7f; w.kickBack = 0.09f; w.kickRotation = 12f;
                    w.adsFov = 68; w.adsTime = 0.18f; w.adsMoveMultiplier = 0.85f;
                    w.hipPosition = new Vector3(0.2f, -0.25f, 0.47f); w.hipEuler = new Vector3(0, -1, 0); w.sightDistance = 0.46f; w.adsOffset = new Vector3(0.015f, -0.03f, 0f);
                    w.tracerColor = new Color(1f, 0.85f, 0.55f); w.shakeOnFire = 0.25f;
                    w.fireSound = A("Weapons/Bulldog_Fire"); w.reloadOut = A("Weapons/Shell_Load"); w.reloadIn = A("Weapons/Shell_Load");
                    w.actionSound = A("Weapons/Pump_Action"); w.equipSound = equip;
                }),
                W("W04_Longbow", 4, "Longbow", w =>
                {
                    w.description = "Bolt-action sniper rifle. One clean headshot ends the argument.";
                    w.category = "SNIPER"; w.slot = WeaponSlot.Primary; w.fireMode = FireMode.Bolt; w.price = 2500; w.unlockLevel = 5;
                    w.damage = 95; w.headMultiplier = 2.2f; w.limbMultiplier = 0.8f; w.falloffStart = 200; w.falloffEnd = 300; w.minDamageFactor = 1f; w.range = 400;
                    w.rpm = 46; w.magSize = 5; w.reserveAmmo = 25; w.reloadTime = 2.6f; w.equipTime = 0.6f; w.moveSpeedMultiplier = 0.9f;
                    w.hipSpread = 7f; w.adsSpread = 0f; w.moveSpreadAdd = 3f; w.airSpreadAdd = 8f; w.spreadPerShot = 0f; w.maxBloom = 0f;
                    w.recoilPitch = 5.5f; w.recoilYaw = 0.8f; w.recoilRecovery = 6f; w.kickBack = 0.1f; w.kickRotation = 10f;
                    w.adsFov = 22; w.adsTime = 0.24f; w.scoped = true; w.adsSensitivity = 0.75f; w.adsMoveMultiplier = 0.6f;
                    w.hipPosition = new Vector3(0.19f, -0.23f, 0.44f); w.hipEuler = new Vector3(0, -1, 0); w.sightDistance = 0.12f;
                    w.tracerColor = new Color(1f, 0.95f, 0.8f); w.shakeOnFire = 0.3f;
                    w.fireSound = A("Weapons/Longbow_Fire"); w.reloadOut = mag; w.reloadIn = magIn; w.actionSound = A("Weapons/Bolt_Action"); w.equipSound = equip;
                }),
                W("W05_Fang", 5, "Fang", w =>
                {
                    w.description = "Combat knife. Fast slashes; strike from behind for an instant takedown.";
                    w.category = "MELEE"; w.slot = WeaponSlot.Melee; w.fireMode = FireMode.Melee; w.ownedByDefault = true; w.supportGrip = SupportGrip.None;
                    w.damage = 55; w.headMultiplier = 1f; w.limbMultiplier = 1f; w.range = 2.3f; w.rpm = 110; w.magSize = 1; w.reserveAmmo = 0;
                    w.equipTime = 0.25f; w.moveSpeedMultiplier = 1.08f; w.hipSpread = 0; w.adsSpread = 0;
                    w.hipPosition = new Vector3(0.22f, -0.25f, 0.5f); w.hipEuler = new Vector3(-42, -22, 18); w.sightDistance = 0.3f;
                    w.fireSound = null; w.equipSound = equip;
                }),
            };
            log.AppendLine("weapons: " + list.Count);
            return list.ToArray();
        }

        // ------------------------------------------------------------------ skins
        static SkinDef[] BuildSkins(System.Text.StringBuilder log)
        {
            var defs = new (string name, Rarity r, int price, int lvl, string col, string pat, float ps, float scale, float hl)[]
            {
                ("Factory", Rarity.Common, 0, 1, "4A515C", "000000", 0, 6, 0.18f),
                ("Desert Tan", Rarity.Common, 250, 1, "C9AE80", "000000", 0, 6, 0.12f),
                ("Olive Drab", Rarity.Common, 250, 1, "6E7444", "000000", 0, 6, 0.1f),
                ("Arctic", Rarity.Rare, 600, 3, "E6EAED", "9AA7B2", 0.65f, 5, 0.15f),
                ("Coral", Rarity.Rare, 700, 4, "E8735A", "F2C9A0", 0.0f, 6, 0.15f),
                ("Teal Wave", Rarity.Rare, 700, 4, "2E8C86", "5FB3A8", 0.55f, 4, 0.15f),
                ("Jungle", Rarity.Epic, 1400, 7, "5C7A3A", "2F3D22", 0.9f, 7, 0.1f),
                ("Tiger", Rarity.Epic, 1400, 9, "E3A03A", "2A2420", 1f, 9, 0.12f),
                ("Midnight", Rarity.Epic, 1600, 12, "232838", "3C4A6E", 0.7f, 6, 0.3f),
                ("Bubblegum", Rarity.Legendary, 4000, 18, "F2A6C4", "FFFFFF", 0.7f, 5, 0.25f),
                ("Royal Gold", Rarity.Legendary, 5000, 25, "E8B84A", "C4952F", 0.3f, 4, 0.45f),
            };
            var list = new List<SkinDef>();
            for (int i = 0; i < defs.Length; i++)
            {
                var d = defs[i];
                var s = LoadOrCreate<SkinDef>(Data + $"Skins/S{i:00}_{d.name.Replace(" ", "")}.asset");
                s.id = (byte)i; s.displayName = d.name; s.rarity = d.r; s.price = d.price; s.unlockLevel = d.lvl;
                s.baseColor = Hex(d.col); s.patternColor = Hex(d.pat); s.patternStrength = d.ps; s.patternScale = d.scale; s.highlight = d.hl;
                s.grain = 0.12f;
                EditorUtility.SetDirty(s);
                list.Add(s);
            }
            log.AppendLine("skins: " + list.Count);
            return list.ToArray();
        }

        // ------------------------------------------------------------------ prefabs
        static GameObject BuildCombatant(System.Text.StringBuilder log)
        {
            var go = new GameObject("Combatant");
            go.layer = Layers.Player;
            var cc = go.AddComponent<CharacterController>();
            cc.radius = 0.35f; cc.height = GameRules.StandHeight; cc.center = new Vector3(0, GameRules.StandHeight / 2, 0);
            cc.stepOffset = 0.42f; cc.slopeLimit = 50f; cc.skinWidth = 0.04f; cc.minMoveDistance = 0f;
            go.AddComponent<NetworkObject>();
            var nt = go.AddComponent<NetworkTransform>();
            nt.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
            nt.SyncPositionX = nt.SyncPositionY = nt.SyncPositionZ = true;
            nt.SyncRotAngleX = false; nt.SyncRotAngleY = true; nt.SyncRotAngleZ = false;
            nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
            nt.Interpolate = true;
            nt.UseHalfFloatPrecision = false;
            nt.PositionThreshold = 0.005f;
            nt.RotAngleThreshold = 0.5f;
            go.AddComponent<PlayerMotor>();
            go.AddComponent<WeaponController>();
            go.AddComponent<CharacterVisual>();
            var brain = go.AddComponent<BotBrain>(); brain.enabled = false;
            var lpi = go.AddComponent<LocalPlayerInput>(); lpi.enabled = false;
            go.AddComponent<ViewModel>();
            go.AddComponent<LocalFeedback>();
            go.AddComponent<LoadoutSync>();
            go.AddComponent<Combatant>();
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, Prefabs + "Combatant.prefab");
            Object.DestroyImmediate(go);
            log.AppendLine("combatant prefab");
            return prefab;
        }

        static void BuildPickupsAndPads(System.Text.StringBuilder log)
        {
            // Jump pad (static, not networked)
            var pad = new GameObject("JumpPad");
            var padModel = (GameObject)PrefabUtility.InstantiatePrefab(Model("Props/JumpPad"));
            padModel.transform.SetParent(pad.transform, false);
            var jp = pad.AddComponent<JumpPad>();
            jp.ring = padModel.transform.FindDeep("Ring");
            foreach (var mf in padModel.GetComponentsInChildren<MeshFilter>())
                if (mf.name == "Base") { var mc = mf.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; mf.gameObject.layer = Layers.World; }
            PrefabUtility.SaveAsPrefabAsset(pad, Prefabs + "JumpPad.prefab");
            Object.DestroyImmediate(pad);

            foreach (var (name, model, kind, amount) in new[] { ("PickupHealth", "Props/HealthPack", Pickup.Kind.Health, 50), ("PickupAmmo", "Props/AmmoBox", Pickup.Kind.Ammo, 0) })
            {
                var p = new GameObject(name);
                p.AddComponent<NetworkObject>();
                var pk = p.AddComponent<Pickup>();
                pk.kind = kind; pk.amount = amount; pk.respawnTime = kind == Pickup.Kind.Health ? 20f : 15f;
                var vis = new GameObject("Visual").transform;
                vis.SetParent(p.transform, false);
                vis.localPosition = new Vector3(0, 0.45f, 0);
                var m = (GameObject)PrefabUtility.InstantiatePrefab(Model(model));
                m.transform.SetParent(vis, false);
                m.transform.localScale = Vector3.one * 1.15f;
                pk.visual = vis;
                // base ring so it reads from distance
                var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.DestroyImmediate(ring.GetComponent<Collider>());
                ring.name = "Base";
                ring.transform.SetParent(p.transform, false);
                ring.transform.localScale = new Vector3(1.3f, 0.03f, 1.3f);
                ring.transform.localPosition = new Vector3(0, 0.03f, 0);
                ring.GetComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(Mat + (kind == Pickup.Kind.Health ? "M_TeamRed.mat" : "M_SkinTemplate.mat"));
                PrefabUtility.SaveAsPrefabAsset(p, Prefabs + name + ".prefab");
                Object.DestroyImmediate(p);
            }
            log.AppendLine("pad + pickup prefabs");
        }

        // ------------------------------------------------------------------ config
        static void BuildConfig(WeaponDef[] weapons, SkinDef[] skins, SoundBank sounds, GameObject combatant, System.Text.StringBuilder log)
        {
            var c = LoadOrCreate<GameConfig>(Root + "Resources/SalvoConfig.asset");
            c.weapons = weapons;
            c.skins = skins;
            c.maps = new[]
            {
                new MapDef { sceneName = "Outpost", displayName = "Outpost", blurb = "Sun-baked desert outpost. Containers west, market east, plaza in the middle." },
                new MapDef { sceneName = "Harbor", displayName = "Harbor", blurb = "Whitewashed fishing town on the quay. Mind the water, it bites." },
            };
            c.combatantPrefab = combatant;
            c.trooperModel = Model("Characters/Trooper");
            c.fpArmsModel = Model("Characters/FP_Arms");
            Material M(string n) => AssetDatabase.LoadAssetAtPath<Material>(Mat + n + ".mat");
            c.worldMaterial = M("M_World"); c.characterMaterial = M("M_Character"); c.weaponMaterial = M("M_Weapon");
            c.skinTemplate = M("M_SkinTemplate"); c.glassMaterial = M("M_Glass"); c.teamRed = M("M_TeamRed"); c.teamBlue = M("M_TeamBlue");
            c.fxAlpha = M("M_FxPuff"); c.fxDecal = M("M_FxDecal"); c.fxTracer = M("M_FxTracer"); c.fxFlash = M("M_FxFlash");
            c.puffTexture = T("T_Puff"); c.sparkTexture = T("T_Spark");
            c.playerColors = new[] { "E8735A", "36A89C", "F2B33D", "4A8FD6", "9B6FD6", "6FBF5A", "F28BB2", "F08A3C", "5CC8D9", "D6453A", "B8D64A", "E9E4D8" }.Select(Hex).ToArray();
            c.rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/PC_Renderer.asset");
            c.sounds = sounds;
            c.scopeOverlay = T("T_ScopeOverlay");
            c.botNames = new[] { "Viper", "Mako", "Dune", "Ghost", "Rook", "Nova", "Tango", "Bishop", "Saber", "Jinx", "Kestrel", "Onyx", "Pixel",
                                 "Rocco", "Hex", "Blitz", "Mirage", "Cobalt", "Sly", "Bandit", "Echo", "Flint", "Gecko", "Havoc", "Lynx", "Moxie",
                                 "Noodle", "Pepper", "Quill", "Rascal" };
            c.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(Root + "UI/SalvoPanel.asset");
            c.uiLayout = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(Root + "UI/Salvo.uxml");
            c.uiStyle = AssetDatabase.LoadAssetAtPath<StyleSheet>(Root + "UI/Salvo.uss");
            EditorUtility.SetDirty(c);

            // network prefab list
            var list = LoadOrCreate<NetworkPrefabsList>(Data + "SalvoNetworkPrefabs.asset");
            var have = new HashSet<GameObject>(list.PrefabList.Select(p => p.Prefab));
            foreach (var pf in new[] { combatant })
                if (!have.Contains(pf)) list.Add(new NetworkPrefab { Prefab = pf });
            EditorUtility.SetDirty(list);
            log.AppendLine("config + network prefab list");
        }

        // ------------------------------------------------------------------ warm-up
        /// <summary>SDF font assets with printable ASCII pre-rasterized: no glyph-atlas hitches mid-match.</summary>
        static void BuildFontAssets(System.Text.StringBuilder log)
        {
            Dir(Root + "UI/Fonts/");
            string ascii = "";
            for (int c = 32; c < 127; c++) ascii += (char)c;
            foreach (var name in new[] { "LilitaOne-Regular", "Rubik-Medium", "Rubik-Bold", "Rubik-Black" })
            {
                string path = Root + "UI/Fonts/" + name + " SDF.asset";
                AssetDatabase.DeleteAsset(path);
                var font = AssetDatabase.LoadAssetAtPath<Font>(Root + "Fonts/" + name + ".ttf");
                var fa = UnityEngine.TextCore.Text.FontAsset.CreateFontAsset(font, 64, 6, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024,
                    UnityEngine.TextCore.Text.AtlasPopulationMode.Dynamic, true);
                fa.name = name + " SDF";
                fa.TryAddCharacters(ascii, out _, false);
                AssetDatabase.CreateAsset(fa, path);
                foreach (var tex in fa.atlasTextures) { if (tex != null) { tex.name = name + " Atlas"; AssetDatabase.AddObjectToAsset(tex, fa); } }
                if (fa.material != null) { fa.material.name = name + " Material"; AssetDatabase.AddObjectToAsset(fa.material, fa); }
                EditorUtility.SetDirty(fa);
            }
            AssetDatabase.SaveAssets();
            log.AppendLine("font assets");
        }

        /// <summary>Every keyword combination our shaders actually run with (sun shadows on/soft/off, linear fog).</summary>
        static ShaderVariantCollection BuildShaderVariants(System.Text.StringBuilder log)
        {
            string path = Root + "Art/Shaders/SalvoVariants.shadervariants";
            var svc = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(path);
            if (svc == null) { svc = new ShaderVariantCollection(); AssetDatabase.CreateAsset(svc, path); }
            svc.Clear();
            var lit = Shader.Find("Salvo/StylizedLit");
            var fx = Shader.Find("Salvo/FxUnlit");
            var sky = Shader.Find("Salvo/Sky");
            string[][] litSets =
            {
                new[] { "FOG_LINEAR" },
                new[] { "_MAIN_LIGHT_SHADOWS_CASCADE", "FOG_LINEAR" },
                new[] { "_MAIN_LIGHT_SHADOWS_CASCADE", "_SHADOWS_SOFT", "FOG_LINEAR" },
                new[] { "_MAIN_LIGHT_SHADOWS_CASCADE", "_SHADOWS_SOFT_LOW", "FOG_LINEAR" },
                new[] { "_MAIN_LIGHT_SHADOWS", "_SHADOWS_SOFT", "FOG_LINEAR" },
                new[] { "_MAIN_LIGHT_SHADOWS", "FOG_LINEAR" },
            };
            int added = 0;
            void Add(Shader s, PassType pass, params string[] kw)
            {
                try { if (svc.Add(new ShaderVariantCollection.ShaderVariant(s, pass, kw))) added++; }
                catch (System.ArgumentException) { /* combination not present in this shader */ }
            }
            foreach (var set in litSets) Add(lit, PassType.ScriptableRenderPipeline, set);
            Add(lit, PassType.ShadowCaster);
            Add(lit, PassType.ScriptableRenderPipeline);
            Add(fx, PassType.ScriptableRenderPipeline, "FOG_LINEAR");
            Add(fx, PassType.ScriptableRenderPipeline);
            Add(sky, PassType.ScriptableRenderPipeline);
            EditorUtility.SetDirty(svc);
            log.AppendLine($"shader variants: {added}");
            return svc;
        }

        static void BuildVolumeAndUI(System.Text.StringBuilder log)
        {
            // post-processing: neutral tonemap, gentle grade, soft vignette. No bloom (nothing glows).
            string vpPath = Root + "Art/Materials/SalvoPost.asset";
            var vp = AssetDatabase.LoadAssetAtPath<VolumeProfile>(vpPath);
            if (vp == null) { vp = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(vp, vpPath); }
            T Get<T>() where T : VolumeComponent
            {
                if (!vp.TryGet<T>(out var comp)) { comp = vp.Add<T>(true); AssetDatabase.AddObjectToAsset(comp, vp); }
                comp.active = true;
                return comp;
            }
            var tm = Get<Tonemapping>(); tm.mode.Override(TonemappingMode.Neutral);
            var ca = Get<ColorAdjustments>(); ca.saturation.Override(8f); ca.contrast.Override(10f); ca.postExposure.Override(0.15f);
            var vg = Get<Vignette>(); vg.intensity.Override(0.18f); vg.smoothness.Override(0.45f); vg.color.Override(Color.black);
            var wb = Get<WhiteBalance>(); wb.temperature.Override(6f);
            var lgg = Get<LiftGammaGain>(); lgg.lift.Override(new Vector4(0.98f, 0.99f, 1.04f, 0f));
            EditorUtility.SetDirty(vp);

            // UI Toolkit panel + default runtime theme
            string themePath = Root + "UI/SalvoTheme.tss";
            if (!File.Exists(themePath)) { File.WriteAllText(themePath, "@import url(\"unity-theme://default\");\n"); AssetDatabase.ImportAsset(themePath); }
            var ps = LoadOrCreate<PanelSettings>(Root + "UI/SalvoPanel.asset");
            ps.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(themePath);
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1920, 1080);
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 0.5f;
            ps.clearColor = false;
            EditorUtility.SetDirty(ps);
            log.AppendLine("volume profile + panel settings");
        }
    }
}
