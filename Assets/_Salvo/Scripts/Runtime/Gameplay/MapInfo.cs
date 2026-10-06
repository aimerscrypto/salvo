using System.Collections.Generic;
using UnityEngine;

namespace Salvo
{
    /// <summary>Per-map metadata: spawns, bot roam points, menu camera path, showcase spot.</summary>
    public class MapInfo : MonoBehaviour
    {
        public static MapInfo I { get; private set; }

        public string displayName = "Outpost";
        [Tooltip("Combatants below this height die (e.g. falling into the sea).")]
        public float killHeight = GameRules.KillHeight;
        public Transform[] spawnsFfa;
        public Transform[] spawnsRed;
        public Transform[] spawnsBlue;
        public Transform[] roamPoints;
        public Transform[] menuCameraPath;
        public Transform showcaseSpot;
        public Transform showcaseCamera;
        public Light sun;
        public AudioClip ambience;

        void Awake()
        {
            I = this;
            if (sun != null) RenderSettings.sun = sun;
        }

        void Start()
        {
            Settings.ApplySun();
            if (ambience != null) AudioManager.Ambience(ambience, 0.3f);
        }

        void OnDestroy()
        {
            if (I == this) I = null;
        }
    }

    /// <summary>Chooses the safest spawn: far from living enemies, out of their line of sight.</summary>
    public static class SpawnSelector
    {
        public struct Spawn
        {
            public Vector3 position;
            public float yaw;
        }

        static readonly List<Transform> candidates = new List<Transform>();

        public static Spawn Pick(Team team, bool teamMode)
        {
            var map = MapInfo.I;
            candidates.Clear();
            if (map != null)
            {
                if (teamMode && team == Team.Red && map.spawnsRed != null) candidates.AddRange(map.spawnsRed);
                else if (teamMode && team == Team.Blue && map.spawnsBlue != null) candidates.AddRange(map.spawnsBlue);
                else if (map.spawnsFfa != null) candidates.AddRange(map.spawnsFfa);
            }
            candidates.RemoveAll(t => t == null);
            if (candidates.Count == 0) return new Spawn { position = Vector3.up * 2f, yaw = 0f };

            Transform best = null;
            float bestScore = float.MinValue;
            foreach (var sp in candidates)
            {
                float score = Random.Range(0f, 6f); // a little randomness so spawns rotate
                float nearest = 999f;
                foreach (var c in Combatant.All)
                {
                    if (!c.Alive) continue;
                    bool enemy = !teamMode || c.Team != team;
                    float d = Vector3.Distance(c.transform.position, sp.position);
                    if (enemy)
                    {
                        nearest = Mathf.Min(nearest, d);
                        if (d < 45f && !Physics.Linecast(c.EyePosition, sp.position + Vector3.up * 1.5f, Layers.WorldMask, QueryTriggerInteraction.Ignore))
                            score -= 40f;
                    }
                    else if (d < 1.5f) score -= 30f; // don't stack on teammates
                }
                score += Mathf.Min(nearest, 40f);
                if (score > bestScore) { bestScore = score; best = sp; }
            }
            return new Spawn { position = best.position + Vector3.up * 0.05f, yaw = best.eulerAngles.y };
        }
    }
}
