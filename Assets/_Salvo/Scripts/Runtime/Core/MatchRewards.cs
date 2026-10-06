using System.Collections.Generic;
using UnityEngine;

namespace Salvo
{
    /// <summary>Turns the local player's round results into XP/coins exactly once per round.</summary>
    public static class MatchRewards
    {
        public static List<RewardLine> LastLines = new List<RewardLine>();
        public static int LastLevels, LastLevelCoins, LevelBefore, XpBefore;
        public static MatchSummary LastSummary;
        static bool applied = true;
        static float roundStart;

        public static void OnRoundStarted()
        {
            applied = false;
            roundStart = Time.time;
            var me = Combatant.Local;
            if (me != null) { me.Weapons.ShotsFired = 0; me.Weapons.ShotsHit = 0; }
        }

        public static void ApplyIfPending(bool completed)
        {
            if (applied) return;
            var mm = MatchManager.I;
            var me = Combatant.Local;
            if (mm == null || me == null) return;
            if (!mm.TryGetScore(me.NetworkObjectId, out var e)) return;
            applied = true;

            bool won;
            if (mm.IsTeamMode) won = mm.WinnerTeam.Value == (byte)me.Team && me.Team != Team.None;
            else won = mm.PlacementOf(me.NetworkObjectId) == 1 && e.kills > 0;

            var s = new MatchSummary
            {
                kills = e.kills, deaths = e.deaths, assists = e.assists, headshots = e.headshots,
                placement = mm.IsTeamMode ? (won ? 1 : 2) : mm.PlacementOf(me.NetworkObjectId),
                playerCount = mm.Scores.Count, won = completed && won, completed = completed,
                shotsFired = me.Weapons.ShotsFired, shotsHit = me.Weapons.ShotsHit,
                bestStreak = e.streak, seconds = Time.time - roundStart
            };
            LevelBefore = Profile.Data.level;
            XpBefore = Profile.Data.xp;
            LastSummary = s;
            LastLines = Profile.ApplyMatch(s, out LastLevels, out LastLevelCoins);
        }
    }
}
