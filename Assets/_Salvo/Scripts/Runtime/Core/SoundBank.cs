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
    }
}
