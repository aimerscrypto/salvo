using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Salvo
{
    /// <summary>Main menu, play, loadout, shop, profile and settings pages.</summary>
    public class MenuUI
    {
        readonly UIRoot ui;
        readonly MatchSettings play = new MatchSettings { fillWithBots = 6, maxPlayers = 8 };
        int shopSkinWeapon = 2;
        int settingsTab;
        WeaponDef loadoutSel;
        bool settingsFromPause;

        static readonly int[] MaxPlayerOptions = { 2, 4, 6, 8, 10 };

        public MenuUI(UIRoot root)
        {
            ui = root;
            // main
            ui.Q<Button>("btn-play").clicked += () => { BuildPlayOptions(); ui.ShowMenuPage("play"); };
            ui.Q<Button>("btn-loadout").clicked += OpenLoadout;
            ui.Q<Button>("btn-shop").clicked += () => { BuildShop(); ui.ShowMenuPage("shop"); };
            ui.Q<Button>("btn-profile").clicked += () => { BuildProfile(); ui.ShowMenuPage("profile"); };
            ui.Q<Button>("btn-settings").clicked += () => OpenSettings(false);
            ui.Q<Button>("btn-quit").clicked += () => ui.Modal("QUIT", "Leave SALVO?", "QUIT", Quit, "STAY", null);
            ui.Q<Button>("btn-daily").clicked += ClaimDaily;
            ui.Q<Label>("version").text = "v" + GameRules.Version + "  -  " + Application.unityVersion;

            // play
            ui.Q<Button>("btn-play-back").clicked += Back;
            ui.Q<Button>("btn-quick").clicked += () => { ui.SetConnecting("Searching..."); GameFlow.QuickMatch(play.mode); };
            ui.Q<Button>("btn-host").clicked += () => { ui.SetConnecting("Creating match..."); GameFlow.HostOnline(Clone(play, false)); };
            ui.Q<Button>("btn-join").clicked += () =>
            {
                var code = ui.Q<TextField>("join-code").value;
                if (string.IsNullOrWhiteSpace(code) || code.Trim().Length < 4) { ui.Toast("ENTER A MATCH CODE"); AudioManager.PlayUI(AudioManager.Bank?.uiError); return; }
                ui.SetConnecting("Joining " + code.Trim().ToUpperInvariant() + "...");
                GameFlow.JoinCode(code);
            };
            ui.Q<Button>("btn-practice").clicked += () => { ui.SetConnecting("Starting practice..."); GameFlow.StartLocal(Clone(play, true), false); };
            ui.Q<Button>("btn-lan-host").clicked += () => { ui.SetConnecting("Hosting LAN match..."); GameFlow.StartLocal(Clone(play, false), true); };
            ui.Q<Button>("btn-lan-join").clicked += () => { ui.SetConnecting("Connecting..."); GameFlow.JoinDirect(ui.Q<TextField>("lan-address").value); };
#if UNITY_WEBGL
            // a browser can't open sockets for LAN, and quitting is closing the tab
            ui.Q<Button>("btn-lan-host").parent.parent.style.display = DisplayStyle.None;
            ui.Q<Button>("btn-quit").style.display = DisplayStyle.None;
#endif

            // loadout / shop / profile / settings
            ui.Q<Button>("btn-loadout-back").clicked += Back;
            ui.Q<Button>("btn-shop-back").clicked += Back;
            ui.Q<Button>("btn-profile-back").clicked += Back;
            ui.Q<Button>("btn-save-name").clicked += () =>
            {
                Profile.SetName(NetSession.SanitizeName(ui.Q<TextField>("name-field").value));
                ui.Toast("CALLSIGN SAVED");
                Refresh();
            };
            ui.Q<Button>("btn-settings-back").clicked += () => { Settings.Save(); SalvoInput.SaveOverrides(); if (settingsFromPause) { ui.Show("settings", false); ui.Show("pause", true); } else Back(); };
            ui.Q<Button>("btn-settings-reset").clicked += () => ui.Modal("RESET SETTINGS", "Restore default settings and key bindings?", "RESET", () =>
            {
                Settings.ResetDefaults();
                SalvoInput.ResetOverrides();
                Settings.Save();
                BuildSettings();
            }, "CANCEL", null);
            string[] tabs = { "tab-graphics", "tab-controls", "tab-gameplay", "tab-audio" };
            for (int i = 0; i < tabs.Length; i++)
            {
                int idx = i;
                ui.Q<Button>(tabs[i]).clicked += () => { settingsTab = idx; BuildSettings(); };
            }

            Profile.Changed += Refresh;
            Refresh();
        }

        static MatchSettings Clone(MatchSettings s, bool offline) => new MatchSettings
        {
            mode = s.mode, maxPlayers = s.maxPlayers, fillWithBots = s.fillWithBots, botSkill = s.botSkill,
            isPrivate = s.isPrivate, offline = offline, timeLimit = s.timeLimit, map = s.map, rotateMaps = s.rotateMaps
        };

        void Back()
        {
            MenuState.PreviewWeapon = -1;
            MenuState.PreviewSkin = -1;
            if (GameFlow.InMatch)
            {
                ui.HideMenuPages();
                ui.Show("pause", true);
                return;
            }
            ui.ShowMenuPage("main-menu");
        }

        static void Quit()
        {
            Profile.Save();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        public void Refresh()
        {
            var d = Profile.Data;
            if (d == null) return;
            ui.Q<Label>("player-name").text = d.playerName;
            ui.Q<Label>("level-badge").text = d.level.ToString();
            ui.Q("xp-fill").style.width = Length.Percent(Profile.LevelProgress * 100f);
            ui.Q<Label>("coins").text = d.coins.ToString("N0");
            ui.Q<Label>("shop-coins").text = d.coins.ToString("N0");
            var daily = ui.Q<Button>("btn-daily");
            daily.style.display = Profile.DailyAvailable ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void ClaimDaily()
        {
            int amount = Profile.ClaimDaily();
            if (amount > 0)
            {
                AudioManager.PlayUI(AudioManager.Bank?.uiReward);
                ui.Modal("DAILY REWARD", $"+{amount} coins\nDay {Profile.Data.dailyStreak} streak - come back tomorrow for more!", "NICE", null);
            }
            Refresh();
        }

        // ================================================================== PLAY
        void BuildPlayOptions()
        {
            var box = ui.Q("play-options");
            box.Clear();
            var maps = GameConfig.I.maps;
            var mapNames = new List<string>();
            foreach (var m in maps) mapNames.Add(m.displayName.ToUpperInvariant());
            mapNames.Add("ROTATION");
            box.Add(UIRoot.Row("Map", UIRoot.Cycler(mapNames.ToArray(),
                () => play.rotateMaps ? maps.Length : Mathf.Max(0, System.Array.FindIndex(maps, m => m.sceneName == play.map)),
                v => { play.rotateMaps = v >= maps.Length; play.map = maps[v >= maps.Length ? 0 : v].sceneName; })));
            box.Add(UIRoot.Row("Mode", UIRoot.Cycler(new[] { "FREE FOR ALL", "TEAM DEATHMATCH" }, () => (int)play.mode, v => play.mode = (GameMode)v)));
            box.Add(UIRoot.Row("Fill with bots to", UIRoot.Cycler(new[] { "NO BOTS", "2", "3", "4", "5", "6", "7", "8", "9", "10" },
                () => play.fillWithBots <= 0 ? 0 : play.fillWithBots - 1, v => play.fillWithBots = v == 0 ? 0 : v + 1)));
            box.Add(UIRoot.Row("Bot skill", UIRoot.Cycler(new[] { "EASY", "NORMAL", "HARD" }, () => (int)play.botSkill, v => play.botSkill = (BotSkill)v)));
            box.Add(UIRoot.Row("Max players (online)", UIRoot.Cycler(Array.ConvertAll(MaxPlayerOptions, x => x.ToString()),
                () => Mathf.Max(0, Array.IndexOf(MaxPlayerOptions, play.maxPlayers)), v => play.maxPlayers = MaxPlayerOptions[v])));
            box.Add(UIRoot.Row("Match length", UIRoot.Cycler(new[] { "5 MIN", "8 MIN", "12 MIN" },
                () => play.timeLimit <= 300 ? 0 : play.timeLimit <= 480 ? 1 : 2, v => play.timeLimit = v == 0 ? 300 : v == 1 ? 480 : 720)));
            box.Add(UIRoot.Row("Private (code only)", UIRoot.Cycler(new[] { "NO", "YES" }, () => play.isPrivate ? 1 : 0, v => play.isPrivate = v == 1)));
            var hint = UIRoot.Lbl("Online matches use Unity Relay - no port forwarding needed. Share the match code shown in-game.", "card-sub");
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.marginTop = 16;
            box.Add(hint);
            ui.WireSounds(box);
        }

        // ================================================================== LOADOUT
        public void OpenLoadout()
        {
            loadoutSel = GameConfig.I.Weapon(Profile.Data.primary);
            BuildLoadout();
            if (GameFlow.InMatch) { ui.Show("pause", false); ui.Show("loadout", true); }
            else ui.ShowMenuPage("loadout");
        }

        void BuildLoadout()
        {
            var cfg = GameConfig.I;
            var prim = ui.Q("primary-list");
            var sec = ui.Q("secondary-list");
            prim.Clear(); sec.Clear();
            foreach (var w in cfg.weapons)
            {
                if (w.slot == WeaponSlot.Melee) continue;
                var card = WeaponCard(w, true);
                (w.slot == WeaponSlot.Primary ? prim : sec).Add(card);
            }
            ShowWeaponDetails(loadoutSel);
            ui.WireSounds(ui.Q("loadout"));
        }

        VisualElement WeaponCard(WeaponDef w, bool loadout)
        {
            var d = Profile.Data;
            bool equipped = (w.slot == WeaponSlot.Primary ? d.primary : d.secondary) == w.id;
            var card = new VisualElement();
            card.AddToClassList("card");
            card.style.flexDirection = FlexDirection.Row;
            card.style.alignItems = Align.Center;
            if (loadoutSel == w) card.AddToClassList("selected");
            var icon = new VisualElement();
            icon.AddToClassList("icon-img");
            icon.style.width = 150; icon.style.height = 54;
            if (w.icon != null) icon.style.backgroundImage = w.icon;
            card.Add(icon);
            var col = new VisualElement { style = { flexGrow = 1, marginLeft = 12 } };
            var title = new VisualElement();
            title.AddToClassList("row");
            title.Add(UIRoot.Lbl(w.displayName.ToUpperInvariant(), "card-title"));
            if (equipped) title.Add(UIRoot.Lbl("EQUIPPED", "tag equipped"));
            col.Add(title);
            col.Add(UIRoot.Lbl(w.category, "card-sub"));
            card.Add(col);
            card.RegisterCallback<ClickEvent>(_ =>
            {
                loadoutSel = w;
                MenuState.PreviewWeapon = w.id;
                MenuState.PreviewSkin = -1;
                if (!equipped)
                {
                    Profile.EquipWeapon(w);
                    AudioManager.PlayUI(AudioManager.Bank?.uiClick);
                    RequestLoadoutUpdate();
                }
                BuildLoadout();
            });
            return card;
        }

        void ShowWeaponDetails(WeaponDef w)
        {
            if (w == null) return;
            MenuState.PreviewWeapon = w.id;
            ui.Q<Label>("lo-weapon-name").text = w.displayName.ToUpperInvariant();
            ui.Q<Label>("lo-weapon-desc").text = w.description;
            var stats = ui.Q("lo-stats");
            stats.Clear();
            stats.Add(Stat("DAMAGE", w.damage * Mathf.Max(1, w.pellets) / 110f));
            stats.Add(Stat("FIRE RATE", w.rpm / 900f));
            stats.Add(Stat("RANGE", Mathf.Clamp01(w.falloffEnd / 90f)));
            stats.Add(Stat("ACCURACY", 1f - Mathf.Clamp01(w.hipSpread / 7f)));
            stats.Add(Stat("MOBILITY", Mathf.InverseLerp(0.8f, 1.05f, w.moveSpeedMultiplier)));
            stats.Add(Stat("MAGAZINE", w.magSize / 40f));

            var skins = ui.Q("skin-list");
            skins.Clear();
            int equippedSkin = Profile.EquippedSkin(w.id);
            var cfg = GameConfig.I;
            foreach (var s in cfg.skins)
            {
                var sw = new VisualElement();
                sw.AddToClassList("swatch");
                sw.style.backgroundColor = s.baseColor;
                if (s.patternStrength > 0)
                {
                    var dot = new VisualElement { style = { width = 20, height = 20, borderTopLeftRadius = 10, borderTopRightRadius = 10, borderBottomLeftRadius = 10, borderBottomRightRadius = 10, backgroundColor = s.patternColor, marginLeft = 14, marginTop = 14 } };
                    sw.Add(dot);
                }
                bool owned = Profile.OwnsSkin(w.id, s.id);
                if (s.id == equippedSkin) sw.AddToClassList("selected");
                if (!owned) sw.style.opacity = 0.45f;
                var skinDef = s;
                sw.RegisterCallback<MouseEnterEvent>(_ =>
                {
                    MenuState.PreviewSkin = skinDef.id;
                    ui.Q<Label>("skin-name").text = skinDef.displayName.ToUpperInvariant() + (owned ? "" : $"  -  {skinDef.price:N0} COINS" + (skinDef.unlockLevel > 1 ? $" (LVL {skinDef.unlockLevel})" : ""));
                });
                sw.RegisterCallback<MouseLeaveEvent>(_ => MenuState.PreviewSkin = -1);
                sw.RegisterCallback<ClickEvent>(_ =>
                {
                    if (owned) { Profile.EquipSkin(w.id, skinDef.id); AudioManager.PlayUI(AudioManager.Bank?.uiClick); RequestLoadoutUpdate(); ShowWeaponDetails(w); }
                    else ConfirmSkinPurchase(w, skinDef, () => ShowWeaponDetails(w));
                });
                skins.Add(sw);
            }
            ui.Q<Label>("skin-name").text = cfg.Skin(equippedSkin).displayName.ToUpperInvariant();

            var colors = ui.Q("color-list");
            colors.Clear();
            for (int i = 0; i < cfg.playerColors.Length; i++)
            {
                int idx = i;
                var sw = new VisualElement();
                sw.AddToClassList("swatch");
                sw.style.width = 40; sw.style.height = 40;
                sw.style.backgroundColor = cfg.playerColors[i];
                if (Profile.Data.colorIndex == i) sw.AddToClassList("selected");
                sw.RegisterCallback<ClickEvent>(_ => { Profile.Data.colorIndex = idx; Profile.Save(); AudioManager.PlayUI(AudioManager.Bank?.uiClick); ShowWeaponDetails(w); });
                colors.Add(sw);
            }
        }

        static VisualElement Stat(string name, float v)
        {
            var row = new VisualElement();
            row.AddToClassList("statrow");
            row.Add(UIRoot.Lbl(name, "label"));
            var bar = new VisualElement();
            bar.AddToClassList("statbar");
            var fill = new VisualElement();
            fill.AddToClassList("statfill");
            fill.style.width = Length.Percent(Mathf.Clamp01(v) * 100f);
            bar.Add(fill);
            row.Add(bar);
            return row;
        }

        /// <summary>In a match, loadout changes apply on next respawn.</summary>
        static void RequestLoadoutUpdate()
        {
            var me = Combatant.Local;
            if (me != null) me.GetComponent<LoadoutSync>()?.Push();
        }

        // ================================================================== SHOP
        void BuildShop()
        {
            // every gun is free: the shop sells weapon finishes only
            var grid = ui.Q("shop-grid");
            grid.Clear();
            var cfg = GameConfig.I;
            {
                var bar = new VisualElement { style = { width = Length.Percent(100), marginBottom = 8 } };
                var names = new List<string>();
                var ids = new List<int>();
                foreach (var w in cfg.weapons) if (w.slot != WeaponSlot.Melee) { names.Add(w.displayName.ToUpperInvariant()); ids.Add(w.id); }
                bar.Add(UIRoot.Row("Weapon", UIRoot.Cycler(names.ToArray(), () => Mathf.Max(0, ids.IndexOf(shopSkinWeapon)), v => { shopSkinWeapon = ids[v]; BuildShop(); })));
                grid.Add(bar);
                var wdef = cfg.Weapon(shopSkinWeapon);
                foreach (var s in cfg.skins) if (s.id != 0) grid.Add(ShopSkinCard(wdef, s));
            }
            ui.WireSounds(grid);
            Refresh();
        }

        VisualElement ShopSkinCard(WeaponDef w, SkinDef s)
        {
            bool owned = Profile.OwnsSkin(w.id, s.id);
            bool locked = Profile.Data.level < s.unlockLevel;
            var card = new VisualElement();
            card.AddToClassList("card");
            card.style.width = 250;
            var sw = new VisualElement { style = { height = 90, borderTopLeftRadius = 10, borderTopRightRadius = 10, borderBottomLeftRadius = 10, borderBottomRightRadius = 10, backgroundColor = s.baseColor, marginBottom = 8, flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, overflow = Overflow.Hidden } };
            if (s.patternStrength > 0)
                for (int i = 0; i < 14; i++)
                    sw.Add(new VisualElement { style = { width = 26 + (i * 7) % 20, height = 20 + (i * 11) % 18, marginLeft = (i * 13) % 22, marginTop = (i * 5) % 14, backgroundColor = s.patternColor, borderTopLeftRadius = 10, borderBottomRightRadius = 10, opacity = s.patternStrength } });
            card.Add(sw);
            var t = new VisualElement();
            t.AddToClassList("row");
            t.Add(UIRoot.Lbl(s.displayName.ToUpperInvariant(), "card-title"));
            string rcls = s.rarity == Rarity.Legendary ? "legendary" : s.rarity == Rarity.Epic ? "epic" : s.rarity == Rarity.Rare ? "rare" : "";
            t.Add(UIRoot.Lbl(s.rarity.ToString().ToUpperInvariant(), "tag " + rcls));
            card.Add(t);
            card.Add(UIRoot.Lbl(w.displayName.ToUpperInvariant(), "card-sub"));
            Button b;
            if (owned) b = UIRoot.Btn("OWNED", "secondary small", null);
            else if (locked) b = UIRoot.Btn($"LEVEL {s.unlockLevel}", "secondary small", null);
            else b = UIRoot.Btn($"BUY  {s.price:N0}", "small", () => ConfirmSkinPurchase(w, s, BuildShop));
            if (owned || locked) b.SetEnabled(false);
            card.Add(b);
            card.RegisterCallback<MouseEnterEvent>(_ => { MenuState.PreviewWeapon = w.id; MenuState.PreviewSkin = s.id; });
            return card;
        }

        void ConfirmSkinPurchase(WeaponDef w, SkinDef s, Action after)
        {
            if (Profile.Data.level < s.unlockLevel) { ui.Toast($"REACH LEVEL {s.unlockLevel} TO UNLOCK"); AudioManager.PlayUI(AudioManager.Bank?.uiError); return; }
            ui.Modal("BUY " + s.displayName.ToUpperInvariant(), $"{s.displayName} finish for the {w.displayName}.\nSpend {s.price:N0} coins? You have {Profile.Data.coins:N0}.", "BUY", () =>
            {
                var r = Profile.BuySkin(w, s);
                if (r == Profile.BuyResult.Ok) Profile.EquipSkin(w.id, s.id);
                Purchased(r, s.displayName);
                after?.Invoke();
            }, "CANCEL", null);
        }

        void Purchased(Profile.BuyResult r, string what)
        {
            switch (r)
            {
                case Profile.BuyResult.Ok: AudioManager.PlayUI(AudioManager.Bank?.uiBuy); ui.Toast("UNLOCKED " + what.ToUpperInvariant()); break;
                case Profile.BuyResult.NotEnoughCoins: AudioManager.PlayUI(AudioManager.Bank?.uiError); ui.Toast("NOT ENOUGH COINS"); break;
                case Profile.BuyResult.Locked: AudioManager.PlayUI(AudioManager.Bank?.uiError); ui.Toast("LEVEL TOO LOW"); break;
            }
            Refresh();
        }

        // ================================================================== PROFILE
        void BuildProfile()
        {
            var d = Profile.Data;
            ui.Q<TextField>("name-field").value = d.playerName;
            ui.Q<Label>("pf-level").text = d.level.ToString();
            ui.Q<Label>("pf-xp").text = d.level >= Profile.MaxLevel ? "MAX LEVEL" : $"{d.xp:N0} / {Profile.XpForLevel(d.level):N0} XP";
            ui.Q("pf-xp-fill").style.width = Length.Percent(Profile.LevelProgress * 100f);
            var grid = ui.Q("stats-grid");
            grid.Clear();
            var s = d.stats;
            void Add(string k, string v)
            {
                var c = new VisualElement();
                c.AddToClassList("panel-2");
                c.style.width = 220; c.style.marginLeft = c.style.marginRight = c.style.marginTop = c.style.marginBottom = 6;
                c.Add(UIRoot.Lbl(k, "label"));
                c.Add(UIRoot.Lbl(v, "title-m"));
                grid.Add(c);
            }
            Add("ELIMINATIONS", s.kills.ToString("N0"));
            Add("DEATHS", s.deaths.ToString("N0"));
            Add("K/D", s.KD.ToString("0.00"));
            Add("HEADSHOTS", s.headshots.ToString("N0"));
            Add("ASSISTS", s.assists.ToString("N0"));
            Add("ACCURACY", (s.Accuracy * 100f).ToString("0") + "%");
            Add("MATCHES", s.matches.ToString("N0"));
            Add("WINS", s.wins.ToString("N0"));
            Add("BEST STREAK", s.bestStreak.ToString());
            Add("TIME PLAYED", (s.playSeconds / 3600f).ToString("0.0") + " H");
        }

        // ================================================================== SETTINGS
        public void OpenSettings(bool fromPause)
        {
            settingsFromPause = fromPause;
            BuildSettings();
            if (fromPause) { ui.Show("pause", false); ui.Show("settings", true); }
            else ui.ShowMenuPage("settings");
        }

        void BuildSettings()
        {
            string[] tabs = { "tab-graphics", "tab-controls", "tab-gameplay", "tab-audio" };
            for (int i = 0; i < tabs.Length; i++) ui.Q<Button>(tabs[i]).EnableInClassList("active", i == settingsTab);
            var box = ui.Q("settings-content");
            box.Clear();
            var d = Settings.Data;
            Action apply = () => { Settings.Apply(); Settings.Save(); };
            Action custom = () => { d.preset = (int)GraphicsPreset.Custom; apply(); };
            switch (settingsTab)
            {
                case 0:
                    box.Add(UIRoot.Row("Quality preset", UIRoot.Cycler(new[] { "PERFORMANCE", "BALANCED", "QUALITY", "CUSTOM" }, () => d.preset, v =>
                    {
                        if (v == 3) { d.preset = 3; apply(); return; }
                        Settings.ApplyPreset((GraphicsPreset)v);
                        Settings.Save();
                        box.schedule.Execute(BuildSettings);
                    })));
                    box.Add(UIRoot.Row("Render scale", UIRoot.SliderControl(0.5f, 1f, () => d.renderScale, v => { d.renderScale = v; custom(); }, "0", 100f)));
                    box.Add(UIRoot.Row("Anti-aliasing", UIRoot.Cycler(new[] { "OFF", "MSAA 2X", "MSAA 4X" }, () => d.msaa >= 4 ? 2 : d.msaa >= 2 ? 1 : 0, v => { d.msaa = v == 0 ? 1 : v == 1 ? 2 : 4; custom(); })));
                    box.Add(UIRoot.Row("Shadows", UIRoot.Cycler(new[] { "OFF", "HARD", "SOFT" }, () => d.shadows, v => { d.shadows = v; custom(); })));
                    box.Add(UIRoot.Row("Shadow distance", UIRoot.SliderControl(20f, 80f, () => d.shadowDistance, v => { d.shadowDistance = v; custom(); }, "0")));
                    box.Add(UIRoot.Row("Frame rate limit", UIRoot.Cycler(new[] { "30", "60", "90", "120", "144", "240", "UNLIMITED" }, () => Array.IndexOf(new[] { 30, 60, 90, 120, 144, 240, 0 }, d.fpsCap) < 0 ? 4 : Array.IndexOf(new[] { 30, 60, 90, 120, 144, 240, 0 }, d.fpsCap),
                        v => { d.fpsCap = new[] { 30, 60, 90, 120, 144, 240, 0 }[v]; apply(); })));
                    box.Add(UIRoot.Row("V-Sync", UIRoot.Cycler(new[] { "OFF", "ON" }, () => d.vsync ? 1 : 0, v => { d.vsync = v == 1; apply(); })));
                    box.Add(UIRoot.Row("Display mode", UIRoot.Cycler(new[] { "WINDOWED", "BORDERLESS", "FULLSCREEN" }, () => d.fullscreen, v => { d.fullscreen = v; apply(); })));
                    box.Add(UIRoot.Row("Show FPS", UIRoot.Cycler(new[] { "OFF", "ON" }, () => d.showFps ? 1 : 0, v => { d.showFps = v == 1; apply(); })));
                    break;
                case 1:
                    box.Add(UIRoot.Row("Mouse sensitivity", UIRoot.SliderControl(0.1f, 5f, () => d.sensitivity, v => { d.sensitivity = v; apply(); })));
                    box.Add(UIRoot.Row("Aim sensitivity", UIRoot.SliderControl(0.2f, 2f, () => d.adsSensitivity, v => { d.adsSensitivity = v; apply(); })));
                    box.Add(UIRoot.Row("Invert Y", UIRoot.Cycler(new[] { "OFF", "ON" }, () => d.invertY ? 1 : 0, v => { d.invertY = v == 1; apply(); })));
                    box.Add(UIRoot.Row("Aim mode", UIRoot.Cycler(new[] { "HOLD", "TOGGLE" }, () => d.toggleAim ? 1 : 0, v => { d.toggleAim = v == 1; apply(); })));
                    box.Add(UIRoot.Row("Crouch mode", UIRoot.Cycler(new[] { "HOLD", "TOGGLE" }, () => d.toggleCrouch ? 1 : 0, v => { d.toggleCrouch = v == 1; apply(); })));
                    var rebind = UIRoot.Lbl("KEY BINDINGS  -  click a key, then press the new one", "section-title");
                    box.Add(rebind);
                    foreach (var a in new[] { SalvoInput.Jump, SalvoInput.Crouch, SalvoInput.Slide, SalvoInput.Reload, SalvoInput.Melee, SalvoInput.Slot1, SalvoInput.Slot2, SalvoInput.Slot3, SalvoInput.LastWeapon, SalvoInput.Scoreboard })
                        box.Add(UIRoot.Row(a.name, KeyButton(a)));
                    break;
                case 2:
                    box.Add(UIRoot.Row("Field of view", UIRoot.SliderControl(70f, 110f, () => d.fov, v => { d.fov = Mathf.Round(v); apply(); }, "0")));
                    box.Add(UIRoot.Row("View bob", UIRoot.Cycler(new[] { "REDUCED", "FULL" }, () => d.viewBob ? 1 : 0, v => { d.viewBob = v == 1; apply(); })));
                    box.Add(UIRoot.Row("Crosshair color", UIRoot.Cycler(Settings.CrosshairColorNames, () => d.crosshairColor, v => { d.crosshairColor = v; apply(); })));
                    box.Add(UIRoot.Row("Crosshair size", UIRoot.SliderControl(0.5f, 2f, () => d.crosshairSize, v => { d.crosshairSize = v; apply(); })));
                    box.Add(UIRoot.Row("Damage numbers", UIRoot.Cycler(new[] { "OFF", "ON" }, () => d.damageNumbers ? 1 : 0, v => { d.damageNumbers = v == 1; apply(); })));
                    break;
                case 3:
                    box.Add(UIRoot.Row("Master volume", UIRoot.SliderControl(0f, 1f, () => d.master, v => { d.master = v; apply(); }, "0", 100f)));
                    box.Add(UIRoot.Row("Effects", UIRoot.SliderControl(0f, 1f, () => d.sfx, v => { d.sfx = v; apply(); }, "0", 100f)));
                    box.Add(UIRoot.Row("Music", UIRoot.SliderControl(0f, 1f, () => d.music, v => { d.music = v; apply(); }, "0", 100f)));
                    box.Add(UIRoot.Row("Interface", UIRoot.SliderControl(0f, 1f, () => d.ui, v => { d.ui = v; apply(); }, "0", 100f)));
                    break;
            }
            ui.WireSounds(box);
        }

        InputActionRebindingExtensions.RebindingOperation rebindOp;

        VisualElement KeyButton(InputAction action)
        {
            var b = new Button { text = SalvoInput.BindingName(action) };
            b.AddToClassList("btn");
            b.AddToClassList("secondary");
            b.AddToClassList("small");
            b.style.width = 200;
            b.clicked += () =>
            {
                int idx = SalvoInput.KeyboardBindingIndex(action);
                if (idx < 0) return;
                rebindOp?.Cancel();
                b.text = "PRESS A KEY...";
                SalvoInput.Map.Disable();
                rebindOp = action.PerformInteractiveRebinding(idx)
                    .WithControlsExcluding("<Mouse>/position").WithControlsExcluding("<Mouse>/delta")
                    .WithCancelingThrough("<Keyboard>/escape")
                    .OnComplete(op => { op.Dispose(); rebindOp = null; SalvoInput.Map.Enable(); SalvoInput.SaveOverrides(); b.text = SalvoInput.BindingName(action); })
                    .OnCancel(op => { op.Dispose(); rebindOp = null; SalvoInput.Map.Enable(); b.text = SalvoInput.BindingName(action); })
                    .Start();
            };
            return b;
        }
    }
}
