using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Salvo
{
    [Serializable]
    public class WeaponSkinEntry
    {
        public int weapon;
        public int skin;
    }

    [Serializable]
    public class PlayerStats
    {
        public int kills, deaths, assists, headshots, wins, matches, shotsFired, shotsHit, bestStreak;
        public float playSeconds;
        public float Accuracy => shotsFired > 0 ? (float)shotsHit / shotsFired : 0f;
        public float KD => deaths > 0 ? (float)kills / deaths : kills;
    }

    [Serializable]
    public class ProfileData
    {
        public int version = 1;
        public string playerName = "";
        public int level = 1;
        public int xp;
        public int coins = 500;
        public List<int> ownedWeapons = new List<int>();
        public List<WeaponSkinEntry> ownedSkins = new List<WeaponSkinEntry>();
        public List<WeaponSkinEntry> equippedSkins = new List<WeaponSkinEntry>();
        public int primary = 2;
        public int secondary = 0;
        public int colorIndex;
        public PlayerStats stats = new PlayerStats();
        public long lastDailyClaimTicks;
        public int dailyStreak;
        public bool firstRun = true;
    }

    /// <summary>One line on the post-match rewards screen.</summary>
    public struct RewardLine
    {
        public string label;
        public int xp;
        public int coins;
        public RewardLine(string l, int x, int c) { label = l; xp = x; coins = c; }
    }

    public class MatchSummary
    {
        public int kills, deaths, assists, headshots, placement, playerCount, shotsFired, shotsHit, bestStreak;
        public bool won, completed;
        public float seconds;
    }

    /// <summary>
    /// Local player profile + economy. Saved as JSON with an HMAC so casual edits invalidate the file.
    /// Purely client-side progression (P2P host model has no trusted backend); all mutations go through here.
    /// </summary>
    public static class Profile
    {
        public static ProfileData Data { get; private set; }
        public static event Action Changed;

        const string FileName = "profile.json";
        static readonly byte[] Key = Encoding.UTF8.GetBytes("s4lv0::pr0f1le::v1::7b1e");

        static string PathOf => System.IO.Path.Combine(Application.persistentDataPath, FileName);

        public static void Load()
        {
            Data = null;
            try
            {
                if (File.Exists(PathOf))
                {
                    var lines = File.ReadAllText(PathOf).Split('\n');
                    if (lines.Length >= 2 && Sign(lines[1]) == lines[0].Trim())
                        Data = JsonUtility.FromJson<ProfileData>(lines[1]);
                    else
                        Debug.LogWarning("[SALVO] Profile signature mismatch - starting fresh.");
                }
            }
            catch (Exception e) { Debug.LogWarning("[SALVO] Profile load failed: " + e.Message); }

            if (Data == null) Data = new ProfileData();
            if (string.IsNullOrWhiteSpace(Data.playerName)) Data.playerName = "Recruit" + UnityEngine.Random.Range(100, 999);
            EnsureDefaults();
        }

        static void EnsureDefaults()
        {
            var cfg = GameConfig.I;
            if (cfg == null) return;
            foreach (var w in cfg.weapons)
                if (w != null && w.ownedByDefault && !Data.ownedWeapons.Contains(w.id)) Data.ownedWeapons.Add(w.id);
            if (!Owns(Data.primary) || cfg.Weapon(Data.primary).slot != WeaponSlot.Primary)
                Data.primary = FirstOwned(WeaponSlot.Primary);
            if (!Owns(Data.secondary) || cfg.Weapon(Data.secondary).slot != WeaponSlot.Secondary)
                Data.secondary = FirstOwned(WeaponSlot.Secondary);
            Data.level = Mathf.Clamp(Data.level, 1, MaxLevel);
        }

        static int FirstOwned(WeaponSlot slot)
        {
            foreach (var w in GameConfig.I.weapons)
                if (w != null && w.slot == slot && Owns(w.id)) return w.id;
            return 0;
        }

        public static void Save()
        {
            if (Data == null) return;
            try
            {
                string json = JsonUtility.ToJson(Data);
                File.WriteAllText(PathOf, Sign(json) + "\n" + json);
            }
            catch (Exception e) { Debug.LogWarning("[SALVO] Profile save failed: " + e.Message); }
            Changed?.Invoke();
        }

        static string Sign(string json)
        {
            using (var h = new HMACSHA256(Key))
                return Convert.ToBase64String(h.ComputeHash(Encoding.UTF8.GetBytes(json)));
        }

        // ------------------------------------------------------------------ ownership
        public static bool Owns(int weaponId) => Data.ownedWeapons.Contains(weaponId);

        public static bool OwnsSkin(int weaponId, int skinId)
        {
            if (skinId == 0) return true; // factory finish is free
            foreach (var e in Data.ownedSkins) if (e.weapon == weaponId && e.skin == skinId) return true;
            return false;
        }

        public static int EquippedSkin(int weaponId)
        {
            foreach (var e in Data.equippedSkins) if (e.weapon == weaponId) return e.skin;
            return 0;
        }

        public static void EquipSkin(int weaponId, int skinId)
        {
            if (!OwnsSkin(weaponId, skinId)) return;
            foreach (var e in Data.equippedSkins) if (e.weapon == weaponId) { e.skin = skinId; Save(); return; }
            Data.equippedSkins.Add(new WeaponSkinEntry { weapon = weaponId, skin = skinId });
            Save();
        }

        public static void EquipWeapon(WeaponDef w)
        {
            if (w == null || !Owns(w.id)) return;
            if (w.slot == WeaponSlot.Primary) Data.primary = w.id;
            else if (w.slot == WeaponSlot.Secondary) Data.secondary = w.id;
            Save();
        }

        public enum BuyResult { Ok, AlreadyOwned, NotEnoughCoins, Locked }

        public static BuyResult BuyWeapon(WeaponDef w)
        {
            if (Owns(w.id)) return BuyResult.AlreadyOwned;
            if (Data.level < w.unlockLevel) return BuyResult.Locked;
            if (Data.coins < w.price) return BuyResult.NotEnoughCoins;
            Data.coins -= w.price;
            Data.ownedWeapons.Add(w.id);
            Save();
            return BuyResult.Ok;
        }

        public static BuyResult BuySkin(WeaponDef w, SkinDef s)
        {
            if (OwnsSkin(w.id, s.id)) return BuyResult.AlreadyOwned;
            if (Data.level < s.unlockLevel) return BuyResult.Locked;
            if (Data.coins < s.price) return BuyResult.NotEnoughCoins;
            Data.coins -= s.price;
            Data.ownedSkins.Add(new WeaponSkinEntry { weapon = w.id, skin = s.id });
            Save();
            return BuyResult.Ok;
        }

        // ------------------------------------------------------------------ progression
        public const int MaxLevel = 60;
        public static int XpForLevel(int level) => 900 + 225 * (level - 1);

        /// <summary>Adds XP, returns number of levels gained (level-up coin bonus applied).</summary>
        public static int AddXp(int amount, out int bonusCoins)
        {
            bonusCoins = 0;
            int gained = 0;
            Data.xp += amount;
            while (Data.level < MaxLevel && Data.xp >= XpForLevel(Data.level))
            {
                Data.xp -= XpForLevel(Data.level);
                Data.level++;
                gained++;
                bonusCoins += 150 + 25 * Data.level;
            }
            if (Data.level >= MaxLevel) Data.xp = 0;
            Data.coins += bonusCoins;
            return gained;
        }

        public static float LevelProgress => Data.level >= MaxLevel ? 1f : Mathf.Clamp01((float)Data.xp / XpForLevel(Data.level));

        /// <summary>Compute and apply rewards for a finished (or abandoned) match.</summary>
        public static List<RewardLine> ApplyMatch(MatchSummary s, out int levelsGained, out int levelCoins)
        {
            var lines = new List<RewardLine>();
            if (s.kills > 0) lines.Add(new RewardLine($"Eliminations x{s.kills}", s.kills * 100, s.kills * 15));
            if (s.headshots > 0) lines.Add(new RewardLine($"Headshots x{s.headshots}", s.headshots * 25, s.headshots * 5));
            if (s.assists > 0) lines.Add(new RewardLine($"Assists x{s.assists}", s.assists * 40, s.assists * 5));
            if (s.completed)
            {
                lines.Add(new RewardLine("Match completed", 250, 40));
                if (s.won) lines.Add(new RewardLine("Victory", 500, 120));
                else if (s.placement == 2) lines.Add(new RewardLine("2nd place", 300, 70));
                else if (s.placement == 3) lines.Add(new RewardLine("3rd place", 200, 40));
            }
            if (s.bestStreak >= 5) lines.Add(new RewardLine($"Streak x{s.bestStreak}", 30 * s.bestStreak, 4 * s.bestStreak));

            int xp = 0, coins = 0;
            foreach (var l in lines) { xp += l.xp; coins += l.coins; }
            Data.coins += coins;
            levelsGained = AddXp(xp, out levelCoins);

            var st = Data.stats;
            st.kills += s.kills; st.deaths += s.deaths; st.assists += s.assists; st.headshots += s.headshots;
            st.shotsFired += s.shotsFired; st.shotsHit += s.shotsHit; st.playSeconds += s.seconds;
            st.bestStreak = Mathf.Max(st.bestStreak, s.bestStreak);
            if (s.completed) st.matches++;
            if (s.won) st.wins++;
            Save();
            return lines;
        }

        // ------------------------------------------------------------------ daily reward
        public static bool DailyAvailable => DateTime.UtcNow.Ticks - Data.lastDailyClaimTicks > TimeSpan.FromHours(20).Ticks;

        public static int ClaimDaily()
        {
            if (!DailyAvailable) return 0;
            bool keepsStreak = DateTime.UtcNow.Ticks - Data.lastDailyClaimTicks < TimeSpan.FromHours(48).Ticks;
            Data.dailyStreak = keepsStreak ? Mathf.Min(Data.dailyStreak + 1, 7) : 1;
            int amount = 100 + 50 * (Data.dailyStreak - 1);
            Data.coins += amount;
            Data.lastDailyClaimTicks = DateTime.UtcNow.Ticks;
            Save();
            return amount;
        }

        public static void SetName(string n)
        {
            n = (n ?? "").Trim();
            if (n.Length < 2) return;
            if (n.Length > 16) n = n.Substring(0, 16);
            Data.playerName = n;
            Save();
        }
    }
}
