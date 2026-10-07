using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace Salvo
{
    /// <summary>In-match UI: HUD, scoreboard, pause menu and the end-of-match screen.</summary>
    public class Hud : MonoBehaviour
    {
        public static Hud I { get; private set; }

        bool fullResScope;
        /// <summary>
        /// Looking through the scope magnifies the picture, so a reduced render scale turns to mush:
        /// render at full resolution while scoped (most of the screen is the black eyepiece anyway).
        /// </summary>
        void ScopeResolution(bool scoped)
        {
            if (scoped == fullResScope) return;
            fullResScope = scoped;
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
                urp.renderScale = scoped ? 1f : Mathf.Clamp(Settings.Data.renderScale, 0.5f, 1f);
        }

        UIRoot ui;
        Combatant me;
        VisualElement crosshair, chTop, chBottom, chLeft, chRight, chDot, hitmarker, healthFill, healthGhost, killfeed, killBanner, dmgLayer,
            hitNumbers, nameplates, respawn, scope, reloadBar, reloadFill, sbList, endBoard, endRewards;
        Label healthNum, ammoMag, ammoRes, weaponName, timer, scoreRed, scoreBlue, ffaInfo, bannerText, bannerSub, streakLabel, hint,
            respawnKiller, respawnTimer, respawnTitle, fps, codeChip, spawnShield;
        readonly Label[] slots = new Label[3];
        float hitT, bannerT, ghostHealth = 100f, fpsTimer, fpsFrames, lowHealthPulse, damageFlash;
        int lastHealth = 100;
        int multiKills;
        float lastKillTime;
        bool paused, matchEndShown;
        readonly List<(VisualElement el, float t, Vector3 world, float vy)> numbers = new List<(VisualElement, float, Vector3, float)>();
        readonly List<(VisualElement el, float t, Vector3 from)> indicators = new List<(VisualElement, float, Vector3)>();
        readonly Dictionary<Combatant, VisualElement> plates = new Dictionary<Combatant, VisualElement>();

        Volume volume;
        Vignette vignette;
        ColorAdjustments colorAdj;
        float baseVignette = 0.18f;

        public void Init(UIRoot root)
        {
            I = this;
            ui = root;
            crosshair = ui.Q("crosshair");
            chTop = ui.Q("ch-top"); chBottom = ui.Q("ch-bottom"); chLeft = ui.Q("ch-left"); chRight = ui.Q("ch-right"); chDot = ui.Q("ch-dot");
            hitmarker = ui.Q("hitmarker");
            healthFill = ui.Q("health-fill"); healthGhost = ui.Q("health-ghost"); healthNum = ui.Q<Label>("health-num");
            ammoMag = ui.Q<Label>("ammo-mag"); ammoRes = ui.Q<Label>("ammo-res"); weaponName = ui.Q<Label>("weapon-name");
            timer = ui.Q<Label>("timer"); scoreRed = ui.Q<Label>("score-red"); scoreBlue = ui.Q<Label>("score-blue"); ffaInfo = ui.Q<Label>("ffa-info");
            killfeed = ui.Q("killfeed"); killBanner = ui.Q("kill-banner");
            bannerText = ui.Q<Label>("kill-banner-text"); bannerSub = ui.Q<Label>("kill-banner-sub"); streakLabel = ui.Q<Label>("streak");
            dmgLayer = ui.Q("dmg-layer"); hitNumbers = ui.Q("hit-numbers"); nameplates = ui.Q("nameplates");
            respawn = ui.Q("respawn"); respawnKiller = ui.Q<Label>("respawn-killer"); respawnTimer = ui.Q<Label>("respawn-timer"); respawnTitle = ui.Q<Label>("respawn-title");
            scope = ui.Q("scope"); hint = ui.Q<Label>("hint"); reloadBar = ui.Q("reload-bar"); reloadFill = ui.Q("reload-fill");
            fps = ui.Q<Label>("fps"); codeChip = ui.Q<Label>("code-chip"); spawnShield = ui.Q<Label>("spawn-shield");
            for (int i = 0; i < 3; i++) slots[i] = ui.Q<Label>("slot-" + i);
            sbList = ui.Q("sb-list"); endBoard = ui.Q("end-board"); endRewards = ui.Q("end-rewards");
            scope.Add(new ScopeView());

            ui.Q<Button>("btn-resume").clicked += () => SetPaused(false);
            ui.Q<Button>("btn-pause-settings").clicked += () => ui.Menu.OpenSettings(true);
            ui.Q<Button>("btn-pause-loadout").clicked += () => ui.Menu.OpenLoadout();
            ui.Q<Button>("btn-leave").clicked += () => ui.Modal("LEAVE MATCH", NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost && !NetSession.Settings.offline
                ? "You are the host - leaving ends the match for everyone." : "Return to the main menu?", "LEAVE", () => GameFlow.ReturnToMenu(), "STAY", null);
            ui.Q<Button>("btn-end-leave").clicked += () => GameFlow.ReturnToMenu();

            MatchManager.OnKill += OnKill;
            MatchManager.OnMatchEnded += OnMatchEnded;
            MatchManager.OnMatchStarted += OnMatchStarted;

            volume = FindAnyObjectByType<Volume>();
            if (volume != null && volume.profile != null)
            {
                volume.profile.TryGet(out vignette);
                volume.profile.TryGet(out colorAdj);
                if (vignette != null) baseVignette = vignette.intensity.value;
            }
        }

        void OnDestroy()
        {
            ScopeResolution(false);
            MatchManager.OnKill -= OnKill;
            MatchManager.OnMatchEnded -= OnMatchEnded;
            MatchManager.OnMatchStarted -= OnMatchStarted;
        }

        public void ResetHud()
        {
            me = null;
            paused = false;
            matchEndShown = false;
            killfeed.Clear();
            foreach (var p in plates.Values) p.RemoveFromHierarchy();
            plates.Clear();
            ClearTransient();
            SetPost(0f, 0f);
        }

        void ClearTransient()
        {
            foreach (var n in numbers) n.el.RemoveFromHierarchy();
            numbers.Clear();
            foreach (var d in indicators) d.el.RemoveFromHierarchy();
            indicators.Clear();
            hitT = 0; bannerT = 0;
        }

        public void EnterMatch(Combatant c)
        {
            me = c;
            matchEndShown = false;
            ui.Show("hud", true);
            ui.Show("match-end", false);
            SetPaused(false);
            MatchRewards.OnRoundStarted();
            var mm = MatchManager.I;
            bool team = mm != null && mm.IsTeamMode;
            scoreRed.style.display = team ? DisplayStyle.Flex : DisplayStyle.None;
            scoreBlue.style.display = team ? DisplayStyle.Flex : DisplayStyle.None;
            string code = NetSession.JoinCode;
            codeChip.EnableInClassList("hidden", string.IsNullOrEmpty(code));
            codeChip.text = "MATCH CODE  " + code;
            ui.Q<Label>("pause-code").text = string.IsNullOrEmpty(code) ? (NetSession.Settings.offline ? "PRACTICE" : "") : "MATCH CODE: " + code;
            for (int i = 0; i < 3; i++)
            {
                var st = c.Weapons.States[i];
                slots[i].text = (i + 1) + " " + (st != null ? st.def.displayName.ToUpperInvariant() : "-");
            }
            c.Weapons.Equipped += _ => RefreshSlots();
            RefreshSlots();
            if (mm != null && mm.MatchPhase == MatchPhase.Ended) OnMatchEnded();
            AudioManager.Play2D(AudioManager.Bank?.matchStart, 0.6f);
        }

        void RefreshSlots()
        {
            if (me == null) return;
            for (int i = 0; i < 3; i++)
            {
                var st = me.Weapons.States[i];
                slots[i].text = (i + 1) + " " + (st != null ? st.def.displayName.ToUpperInvariant() : "-");
                slots[i].EnableInClassList("active", me.Weapons.CurrentSlot == i);
            }
        }

        // ------------------------------------------------------------------ pause / cursor
        void SetPaused(bool p)
        {
            paused = p;
            ui.Show("pause", p);
            if (p) ui.FocusFirst("pause");
            if (!p) { ui.Show("settings", false); ui.Show("loadout", false); }
        }

        void UpdateCursor()
        {
            bool inMatch = me != null && GameFlow.InMatch;
            bool overlay = ui.AnyOverlayOpen || paused;
            bool lockIt = inMatch && !overlay;
            SalvoInput.GameplayEnabled = lockIt;
            var want = lockIt ? CursorLockMode.Locked : CursorLockMode.None;
            if (UnityEngine.Cursor.lockState != want) UnityEngine.Cursor.lockState = want;
            UnityEngine.Cursor.visible = !lockIt;
        }

        // ------------------------------------------------------------------ events
        public void OnHitConfirmed(int dmg, HitZone zone, bool kill, Vector3 at)
        {
            hitT = 1f;
            var col = kill ? new Color(0.95f, 0.35f, 0.25f) : zone == HitZone.Head ? new Color(1f, 0.78f, 0.25f) : Color.white;
            foreach (var ch in hitmarker.Children()) ch.style.backgroundColor = col;
            hitmarker.style.scale = new Scale(Vector3.one * (kill ? 1.5f : 1f));
            var bank = AudioManager.Bank;
            if (kill) AudioManager.Play2D(bank?.killConfirm, 0.9f);
            else AudioManager.Play2D(zone == HitZone.Head ? bank?.headshot : bank?.hitmarker, 0.7f, Random.Range(0.97f, 1.05f));
            if (Settings.Data.damageNumbers)
            {
                var l = UIRoot.Lbl(dmg.ToString(), zone == HitZone.Head ? "hit-number head" : "hit-number");
                hitNumbers.Add(l);
                numbers.Add((l, 0f, at + Random.insideUnitSphere * 0.25f, 0f));
            }
        }

        public void OnLocalDamaged(int dmg, Vector3 from)
        {
            damageFlash = Mathf.Min(1f, damageFlash + dmg / 50f);
            var arc = new VisualElement();
            arc.AddToClassList("dmg-arc");
            var holder = new VisualElement { pickingMode = PickingMode.Ignore };
            holder.style.position = Position.Absolute;
            holder.Add(arc);
            dmgLayer.Add(holder);
            indicators.Add((holder, 0f, from));
        }

        public void Toast(string text) => ui?.Toast(text, 1.6f);

        void OnKill(KillEvent e)
        {
            var row = new VisualElement();
            row.AddToClassList("kf-row");
            bool mine = me != null && (e.killerId == me.NetworkObjectId || e.victimId == me.NetworkObjectId);
            if (mine) row.AddToClassList("mine");
            if (!string.IsNullOrEmpty(e.killer))
            {
                var k = UIRoot.Lbl(e.killer, "kf-name");
                k.style.color = e.killerColor;
                row.Add(k);
            }
            var wd = GameConfig.I.Weapon(e.weapon);
            row.Add(UIRoot.Lbl(e.weapon == 255 || wd == null ? "FELL" : wd.displayName.ToUpperInvariant(), "kf-weapon"));
            if (e.headshot) row.Add(UIRoot.Lbl("HEAD", "kf-head"));
            var v = UIRoot.Lbl(e.victim, "kf-name");
            v.style.color = e.victimColor;
            row.Add(v);
            killfeed.Insert(0, row);
            row.schedule.Execute(() => row.style.opacity = 0f).StartingIn(5500);
            row.schedule.Execute(() => row.RemoveFromHierarchy()).StartingIn(6000);
            while (killfeed.childCount > 6) killfeed.RemoveAt(killfeed.childCount - 1);

            if (me == null) return;
            if (e.killerId == me.NetworkObjectId)
            {
                multiKills = Time.time - lastKillTime < 4f ? multiKills + 1 : 1;
                lastKillTime = Time.time;
                bannerText.text = "ELIMINATED  " + e.victim.ToUpperInvariant();
                int score = GameRules.ScoreKill + (e.headshot ? GameRules.ScoreHeadshotBonus : 0);
                bannerSub.text = (e.headshot ? "HEADSHOT  " : "") + "+" + score;
                string[] multi = { "", "", "DOUBLE KILL", "TRIPLE KILL", "MULTI KILL", "UNSTOPPABLE" };
                string streakText = multi[Mathf.Clamp(multiKills, 0, multi.Length - 1)];
                if (MatchManager.I != null && MatchManager.I.TryGetScore(me.NetworkObjectId, out var se))
                {
                    if (se.streak == 5) streakText = "KILLING SPREE";
                    else if (se.streak == 10) streakText = "RAMPAGE";
                    else if (se.streak == 15) streakText = "LEGENDARY";
                }
                streakLabel.text = streakText;
                bannerT = 2.2f;
                killBanner.style.scale = new Scale(Vector3.one * 1.25f);
            }
            if (e.victimId == me.NetworkObjectId)
            {
                respawnTitle.text = string.IsNullOrEmpty(e.killer) ? "YOU FELL" : "ELIMINATED";
                respawnKiller.text = string.IsNullOrEmpty(e.killer) ? "" : "by " + e.killer + (wd != null ? "  -  " + wd.displayName : "") + (e.headshot ? "  (HEADSHOT)" : "");
                respawnKiller.style.color = e.killerColor;
            }
        }

        void OnMatchStarted()
        {
            matchEndShown = false;
            ui.Show("match-end", false);
            MatchRewards.OnRoundStarted();
            AudioManager.Play2D(AudioManager.Bank?.matchStart, 0.7f);
        }

        void OnMatchEnded()
        {
            if (me == null || matchEndShown) return;
            matchEndShown = true;
            MatchRewards.ApplyIfPending(true);
            ui.Show("pause", false);
            ui.Show("settings", false);
            ui.Show("loadout", false);
            paused = false;
            ui.Show("match-end", true);
            var mm = MatchManager.I;
            bool won = mm.IsTeamMode ? mm.WinnerTeam.Value == (byte)me.Team : mm.PlacementOf(me.NetworkObjectId) == 1;
            var title = ui.Q<Label>("end-title");
            if (mm.IsTeamMode && mm.WinnerTeam.Value == 0) title.text = "DRAW";
            else if (mm.IsTeamMode) title.text = won ? "VICTORY" : "DEFEAT";
            else { int place = mm.PlacementOf(me.NetworkObjectId); title.text = place == 1 ? "VICTORY" : "#" + place + " PLACE"; }
            title.style.color = won ? new Color(0.95f, 0.7f, 0.24f) : new Color(0.96f, 0.94f, 0.9f);
            ui.Q<Label>("end-sub").text = mm.IsTeamMode ? $"RED {mm.RedScore.Value}  -  {mm.BlueScore.Value} BLUE" : "WINNER: " + mm.Winner.Value.ToString().ToUpperInvariant();
            AudioManager.Play2D(won ? AudioManager.Bank?.victory : AudioManager.Bank?.defeat, 0.8f);

            BuildBoard(endBoard, 10);
            endRewards.Clear();
            int xp = 0, coins = 0;
            foreach (var l in MatchRewards.LastLines)
            {
                var r = new VisualElement();
                r.AddToClassList("reward-row");
                r.Add(UIRoot.Lbl(l.label, "grow"));
                r.Add(UIRoot.Lbl("+" + l.xp + " XP", "reward-xp"));
                r.Add(UIRoot.Lbl("+" + l.coins, "reward-coins"));
                endRewards.Add(r);
                xp += l.xp; coins += l.coins;
            }
            coins += MatchRewards.LastLevelCoins;
            ui.Q<Label>("end-total-xp").text = "+" + xp + " XP";
            ui.Q<Label>("end-total-coins").text = "+" + coins + " COINS";
            ui.Q<Label>("end-level").text = "LEVEL " + Profile.Data.level;
            ui.Q("end-xp-fill").style.width = Length.Percent(Profile.LevelProgress * 100f);
            ui.Q<Label>("end-levelup").text = MatchRewards.LastLevels > 0 ? $"LEVEL UP!  +{MatchRewards.LastLevelCoins} BONUS COINS" : "";
            if (MatchRewards.LastLevels > 0) AudioManager.Play2D(AudioManager.Bank?.levelUp, 0.8f);
            ui.WireSounds(ui.Q("match-end"));
        }

        // ------------------------------------------------------------------ scoreboard
        void BuildBoard(VisualElement list, int max)
        {
            list.Clear();
            var mm = MatchManager.I;
            if (mm == null) return;
            var entries = new List<ScoreEntry>();
            foreach (var e in mm.Scores) entries.Add(e);
            entries.Sort((a, b) =>
            {
                if (mm.IsTeamMode && a.team != b.team) return a.team.CompareTo(b.team);
                int k = b.kills.CompareTo(a.kills);
                return k != 0 ? k : b.score.CompareTo(a.score);
            });
            int rank = 0;
            foreach (var e in entries)
            {
                if (++rank > max * 2) break;
                var row = new VisualElement();
                row.AddToClassList("sb-row");
                if (me != null && e.id == me.NetworkObjectId) row.AddToClassList("me");
                var bar = new VisualElement();
                bar.AddToClassList("sb-team-bar");
                bar.style.backgroundColor = GameConfig.TeamColor((Team)e.team, e.color);
                row.Add(bar);
                row.Add(UIRoot.Lbl(rank.ToString(), "sb-rank"));
                row.Add(UIRoot.Lbl(e.name.ToString() + (e.bot ? "  [BOT]" : ""), "sb-name"));
                row.Add(UIRoot.Lbl(e.kills.ToString(), "sb-cell"));
                row.Add(UIRoot.Lbl(e.deaths.ToString(), "sb-cell"));
                row.Add(UIRoot.Lbl(e.assists.ToString(), "sb-cell"));
                row.Add(UIRoot.Lbl(e.score.ToString(), "sb-cell"));
                row.Add(UIRoot.Lbl(e.bot ? "-" : e.ping.ToString(), "sb-cell"));
                list.Add(row);
            }
        }

        float sbRefresh;

        // ------------------------------------------------------------------ per frame
        void Update()
        {
            if (me == null || !me.IsSpawned)
            {
                if (me != null && !me.IsSpawned) me = null;
                if (GameFlow.InMenu) { UpdateCursor(); return; }
            }

            // fps counter
            fps.EnableInClassList("hidden", !Settings.Data.showFps);
            fpsFrames++; fpsTimer += Time.unscaledDeltaTime;
            if (fpsTimer > 0.5f) { fps.text = Mathf.RoundToInt(fpsFrames / fpsTimer) + " FPS"; fpsFrames = 0; fpsTimer = 0; }

            if (me == null) { UpdateCursor(); return; }

            // pause / scoreboard input (works even while gameplay input is disabled)
            if (SalvoInput.Pause.WasPressedThisFrame() && !ui.IsShown("match-end"))
            {
                if (ui.IsShown("settings") || ui.IsShown("loadout")) { ui.Show("settings", false); ui.Show("loadout", false); ui.Show("pause", true); Settings.Save(); }
                else if (ui.IsShown("modal")) ui.Show("modal", false);
                else SetPaused(!paused);
            }
            bool sb = SalvoInput.Scoreboard.IsPressed() && !paused && !ui.IsShown("match-end");
            ui.Show("scoreboard", sb);
            if (sb)
            {
                sbRefresh -= Time.unscaledDeltaTime;
                if (sbRefresh <= 0) { sbRefresh = 0.5f; BuildBoard(sbList, 10); }
                var mm0 = MatchManager.I;
                ui.Q<Label>("sb-title").text = mm0 != null && mm0.IsTeamMode ? "TEAM DEATHMATCH" : "FREE FOR ALL";
                ui.Q<Label>("sb-sub").text = MapInfo.I != null ? MapInfo.I.displayName.ToUpperInvariant() : "";
            }
            UpdateCursor();

            float dt = Time.deltaTime;
            var w = me.Weapons;
            var def = w.Current;
            var st = w.CurrentState;

            // health
            int hp = me.Health.Value;
            if (hp != shownHp) { shownHp = hp; healthNum.text = hp.ToString(); }
            float frac = hp / (float)GameRules.MaxHealth;
            healthFill.style.width = Length.Percent(frac * 100f);
            healthFill.style.backgroundColor = frac > 0.5f ? new Color(0.49f, 0.79f, 0.42f) : frac > 0.25f ? new Color(0.95f, 0.7f, 0.24f) : new Color(0.89f, 0.38f, 0.31f);
            ghostHealth = hp > ghostHealth ? hp : Mathf.MoveTowards(ghostHealth, hp, dt * 40f);
            healthGhost.style.width = Length.Percent(ghostHealth);
            if (hp != lastHealth) lastHealth = hp;
            spawnShield.EnableInClassList("hidden", !me.SpawnProtected || !me.Alive);

            // ammo
            if (def != null && st != null)
            {
                bool weaponChanged = def != shownDef;
                if (weaponChanged) { shownDef = def; weaponName.text = def.displayName.ToUpperInvariant(); }
                bool melee = def.fireMode == FireMode.Melee;
                if (weaponChanged || st.mag != shownMag || st.reserve != shownRes)
                {
                    shownMag = st.mag; shownRes = st.reserve;
                    ammoMag.text = melee ? "-" : st.mag.ToString();
                    ammoRes.text = melee ? "" : "/ " + st.reserve;
                }
                ammoMag.style.color = !melee && st.mag <= Mathf.Max(1, def.magSize / 4) ? new Color(0.89f, 0.38f, 0.31f) : new Color(0.96f, 0.94f, 0.9f);
                bool needReload = !melee && st.mag == 0 && !w.Reloading;
                hint.EnableInClassList("hidden", !(needReload || w.Reloading));
                hint.text = w.Reloading ? "RELOADING" : st.reserve > 0 ? "PRESS R TO RELOAD" : "NO AMMO";
                reloadBar.EnableInClassList("hidden", !w.Reloading);
                reloadFill.style.width = Length.Percent(w.ReloadProgress * 100f);
            }

            // crosshair
            UpdateCrosshair(def, w);
            hitT = Mathf.MoveTowards(hitT, 0f, dt * 4f);
            hitmarker.style.opacity = hitT;
            if (hitT > 0) hitmarker.style.scale = new Scale(Vector3.one * Mathf.Lerp(1f, hitmarker.resolvedStyle.scale.value.x, 0.85f));

            // banner
            bannerT -= dt;
            killBanner.style.opacity = Mathf.Clamp01(bannerT * 2f);
            killBanner.style.scale = new Scale(Vector3.one * Mathf.Lerp(1f, killBanner.resolvedStyle.scale.value.x, Mathf.Exp(-12f * dt)));

            // scope
            bool scoped = def != null && def.scoped && w.AimT > 0.92f && me.Alive;
            scope.EnableInClassList("hidden", !scoped);
            ScopeResolution(scoped);
            crosshair.style.display = scoped || !me.Alive ? DisplayStyle.None : DisplayStyle.Flex;

            // match info
            var mm = MatchManager.I;
            if (mm != null && mm.IsSpawned)
            {
                int secs = mm.MatchPhase == MatchPhase.Ended ? 0 : Mathf.CeilToInt(Mathf.Max(0, mm.TimeLeft));
                if (secs != shownSecs) { shownSecs = secs; timer.text = SalvoMath.FormatTime(secs); }
                infoTimer -= Time.unscaledDeltaTime;
                bool refreshInfo = infoTimer <= 0f;
                if (refreshInfo) infoTimer = 0.25f;
                timer.style.color = mm.TimeLeft < 30f && mm.MatchPhase == MatchPhase.Live ? new Color(0.89f, 0.38f, 0.31f) : new Color(0.96f, 0.94f, 0.9f);
                if (mm.IsTeamMode)
                {
                    if (refreshInfo) scoreRed.text = mm.RedScore.Value.ToString();
                    if (refreshInfo) scoreBlue.text = mm.BlueScore.Value.ToString();
                    if (refreshInfo) ffaInfo.text = "FIRST TO " + mm.ScoreLimit.Value + (me.Team == Team.Red ? "  -  YOU ARE RED" : me.Team == Team.Blue ? "  -  YOU ARE BLUE" : "");
                }
                else
                {
                    var best = mm.BestEntry();
                    mm.TryGetScore(me.NetworkObjectId, out var mine);
                    if (refreshInfo) ffaInfo.text = $"#{mm.PlacementOf(me.NetworkObjectId)}  -  {mine.kills} / {mm.ScoreLimit.Value}  -  LEADER {best.name} ({best.kills})";
                }
                if (mm.MatchPhase == MatchPhase.Ended)
                    ui.Q<Label>("end-next").text = "NEXT MATCH IN " + Mathf.CeilToInt(Mathf.Max(0, mm.TimeLeft));
            }

            // respawn overlay
            bool dead = !me.Alive && (mm == null || mm.MatchPhase != MatchPhase.Ended);
            respawn.EnableInClassList("hidden", !dead);
            if (dead)
            {
                float left = GameRules.RespawnDelay - (Time.time - deathTime);
                int rs = Mathf.Max(1, Mathf.CeilToInt(left));
                if (rs != shownRespawn) { shownRespawn = rs; respawnTimer.text = rs.ToString(); }
            }
            else deathTime = Time.time;

            UpdateNumbers(dt);
            UpdateIndicators(dt);
            UpdateNameplates();
            UpdatePost(dt, frac);
        }

        float deathTime, infoTimer;
        int shownHp = -1, shownMag = -1, shownRes = -1, shownSecs = -1, shownRespawn = -1;
        WeaponDef shownDef;

        void UpdateCrosshair(WeaponDef def, WeaponController w)
        {
            if (def == null) return;
            var cam = GameCamera.I != null ? GameCamera.I.Cam : null;
            if (cam == null) return;
            float spread = def.fireMode == FireMode.Melee ? 0.6f : w.CurrentSpread();
            // degrees -> pixels at the current vertical fov
            float px = Mathf.Tan(spread * Mathf.Deg2Rad) / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * (ui.Root.resolvedStyle.height * 0.5f);
            float gap = Mathf.Clamp(px, 4f, 160f);
            float s = Settings.Data.crosshairSize;
            float len = 10f * s, th = 2f * Mathf.Max(1f, s);
            Color c = Settings.CrosshairColors[Mathf.Clamp(Settings.Data.crosshairColor, 0, Settings.CrosshairColors.Length - 1)];
            float ads = w.AimT;
            float alpha = 1f - Mathf.SmoothStep(0.3f, 0.9f, ads);
            void Line(VisualElement e, float x, float y, float wd, float ht)
            {
                e.style.left = x; e.style.top = y; e.style.width = wd; e.style.height = ht;
                e.style.backgroundColor = c; e.style.opacity = alpha;
            }
            Line(chTop, -th / 2, -gap - len, th, len);
            Line(chBottom, -th / 2, gap, th, len);
            Line(chLeft, -gap - len, -th / 2, len, th);
            Line(chRight, gap, -th / 2, len, th);
            chDot.style.backgroundColor = c;
            // optics with a red dot: the sight's own reticle takes over while aiming
            var vm = w != null ? w.GetComponent<ViewModel>() : null;
            float dotFade = vm != null && vm.HasReticle ? 1f - Mathf.SmoothStep(0.4f, 0.85f, ads) : 1f;
            chDot.style.opacity = (def.pellets > 1 ? 0.5f : 1f) * dotFade;
        }

        void UpdateNumbers(float dt)
        {
            var cam = GameCamera.I != null ? GameCamera.I.Cam : null;
            for (int i = numbers.Count - 1; i >= 0; i--)
            {
                var n = numbers[i];
                n.t += dt;
                n.vy += dt * 0.8f;
                if (n.t > 0.9f || cam == null) { n.el.RemoveFromHierarchy(); numbers.RemoveAt(i); continue; }
                Vector3 sp = cam.WorldToViewportPoint(n.world + Vector3.up * n.vy);
                if (sp.z < 0) { n.el.style.opacity = 0; numbers[i] = n; continue; }
                n.el.style.left = sp.x * ui.Root.resolvedStyle.width;
                n.el.style.top = (1f - sp.y) * ui.Root.resolvedStyle.height;
                n.el.style.opacity = 1f - Mathf.Clamp01((n.t - 0.5f) / 0.4f);
                numbers[i] = n;
            }
        }

        void UpdateIndicators(float dt)
        {
            var cam = GameCamera.I;
            for (int i = indicators.Count - 1; i >= 0; i--)
            {
                var d = indicators[i];
                d.t += dt;
                if (d.t > 1.4f || cam == null || me == null) { d.el.RemoveFromHierarchy(); indicators.RemoveAt(i); continue; }
                Vector3 to = d.from - me.transform.position;
                to.y = 0;
                Vector3 fwd = cam.transform.forward; fwd.y = 0;
                float ang = Vector3.SignedAngle(fwd, to, Vector3.up);
                d.el.style.rotate = new Rotate(ang);
                d.el.style.opacity = 1f - Mathf.Clamp01((d.t - 0.6f) / 0.8f);
                indicators[i] = d;
            }
        }

        void UpdateNameplates()
        {
            var cam = GameCamera.I != null ? GameCamera.I.Cam : null;
            // remove stale
            List<Combatant> dead = null;
            foreach (var kv in plates) if (kv.Key == null || !kv.Key.IsSpawned) (dead ??= new List<Combatant>()).Add(kv.Key);
            if (dead != null) foreach (var c in dead) { plates[c].RemoveFromHierarchy(); plates.Remove(c); }
            if (cam == null) return;
            float W = ui.Root.resolvedStyle.width, H = ui.Root.resolvedStyle.height;
            Ray aim = new Ray(cam.transform.position, cam.transform.forward);
            foreach (var c in Combatant.All)
            {
                if (c == me) continue;
                if (!plates.TryGetValue(c, out var plate))
                {
                    plate = new VisualElement { pickingMode = PickingMode.Ignore };
                    plate.style.position = Position.Absolute;
                    var name = UIRoot.Lbl("", "nameplate");
                    plate.Add(name);
                    var bar = new VisualElement(); bar.AddToClassList("nameplate-bar");
                    var fill = new VisualElement(); fill.AddToClassList("nameplate-fill");
                    bar.Add(fill); plate.Add(bar);
                    nameplates.Add(plate);
                    plates[c] = plate;
                }
                bool ally = !me.IsEnemyOf(c);
                Vector3 head = c.transform.position + Vector3.up * (c.Motor.Crouching ? 1.6f : 2.15f);
                Vector3 vp = cam.WorldToViewportPoint(head);
                bool show = c.Alive && vp.z > 0 && vp.z < (ally ? 80f : 35f);
                if (show && !ally)
                {
                    // enemies: only when you're looking near them and they're visible
                    Vector3 to = (c.CenterMass - aim.origin);
                    show = Vector3.Angle(aim.direction, to) < 6f + 60f / Mathf.Max(4f, to.magnitude) &&
                           !Physics.Linecast(aim.origin, c.CenterMass, Layers.WorldMask, QueryTriggerInteraction.Ignore);
                }
                plate.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                if (!show) continue;
                plate.style.left = vp.x * W;
                plate.style.top = (1f - vp.y) * H - 24f;
                var lbl = plate[0] as Label;
                if (string.IsNullOrEmpty(lbl.text)) lbl.text = c.Name;
                lbl.style.color = ally ? GameConfig.TeamColor(c.Team, c.ColorNV.Value) : new Color(1f, 0.55f, 0.48f);
                var fillEl = plate[1][0];
                fillEl.style.width = Length.Percent(c.Health.Value);
                plate[1].style.display = ally ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        // ------------------------------------------------------------------ post fx
        void UpdatePost(float dt, float healthFrac)
        {
            damageFlash = Mathf.MoveTowards(damageFlash, 0f, dt * 1.8f);
            float low = healthFrac < 0.35f && me.Alive ? 1f - healthFrac / 0.35f : 0f;
            lowHealthPulse += dt * (2f + low * 3f);
            float pulse = low * (0.75f + 0.25f * Mathf.Sin(lowHealthPulse));
            SetPost(Mathf.Max(pulse, damageFlash * 0.6f), low);
        }

        void SetPost(float hurt, float desat)
        {
            if (vignette != null)
            {
                vignette.intensity.value = Mathf.Lerp(baseVignette, 0.48f, hurt);
                vignette.color.value = Color.Lerp(Color.black, new Color(0.55f, 0.08f, 0.05f), hurt);
            }
            if (colorAdj != null) colorAdj.saturation.value = Mathf.Lerp(8f, -45f, desat);
        }
    }
}
