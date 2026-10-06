using UnityEngine;

namespace Salvo
{
    [CreateAssetMenu(menuName = "SALVO/Sound Bank", fileName = "SoundBank")]
    public class SoundBank : ScriptableObject
    {
        public AudioClip[] footsteps;
        public AudioClip jump;
        public AudioClip land;
        public AudioClip slide;
        public AudioClip jumpPad;
        public AudioClip hitmarker;
        public AudioClip headshot;
        public AudioClip killConfirm;
        public AudioClip hurt;
        public AudioClip death;
        public AudioClip bulletImpact;
        public AudioClip bulletWhiz;
        public AudioClip emptyClick;
        public AudioClip pickupHealth;
        public AudioClip pickupAmmo;
        public AudioClip meleeSwing;
        public AudioClip meleeHit;
        public AudioClip uiHover;
        public AudioClip uiClick;
        public AudioClip uiBack;
        public AudioClip uiBuy;
        public AudioClip uiError;
        public AudioClip uiReward;
        public AudioClip levelUp;
        public AudioClip countdown;
        public AudioClip matchStart;
        public AudioClip victory;
        public AudioClip defeat;
        public AudioClip menuMusic;
        public AudioClip ambience;

        [Header("Surfaces: footsteps and bullet impacts per material")]
        public AudioClip[] stepsSand;
        public AudioClip[] stepsStone;
        public AudioClip[] stepsWood;
        public AudioClip[] stepsMetal;
        public AudioClip[] impactsSand;
        public AudioClip[] impactsStone;
        public AudioClip[] impactsWood;
        public AudioClip[] impactsMetal;

        [Header("Distance")]
        public AudioClip distantShot;   // low rumble layered under far-away gunfire (optional)

        public AudioClip Step(Surface s)
        {
            var set = s == Surface.Sand ? stepsSand : s == Surface.Wood ? stepsWood : s == Surface.Metal ? stepsMetal : stepsStone;
            return Pick(set != null && set.Length > 0 ? set : footsteps);
        }

        public AudioClip Impact(Surface s)
        {
            var set = s == Surface.Sand ? impactsSand : s == Surface.Wood ? impactsWood : s == Surface.Metal ? impactsMetal : impactsStone;
            return set != null && set.Length > 0 ? Pick(set) : bulletImpact;
        }

        int lastPick;
        AudioClip Pick(AudioClip[] set)
        {
            if (set == null || set.Length == 0) return null;
            if (set.Length == 1) return set[0];
            // never the same clip twice in a row
            int i = (lastPick + 1 + Random.Range(0, set.Length - 1)) % set.Length;
            lastPick = i;
            return set[i];
        }
    }
}
