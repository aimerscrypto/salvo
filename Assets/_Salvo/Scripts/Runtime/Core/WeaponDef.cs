using UnityEngine;

namespace Salvo
{
    [CreateAssetMenu(menuName = "SALVO/Weapon", fileName = "Weapon")]
    public class WeaponDef : ScriptableObject
    {
        [Header("Identity")]
        public byte id;
        public string displayName = "Weapon";
        [TextArea] public string description;
        public WeaponSlot slot;
        public FireMode fireMode;
        public string category = "RIFLE";

        [Header("Economy")]
        public int price;
        public int unlockLevel = 1;
        public bool ownedByDefault;

        [Header("Damage")]
        public float damage = 25f;
        public float headMultiplier = 1.6f;
        public float limbMultiplier = 0.85f;
        public int pellets = 1;
        public float falloffStart = 20f;
        public float falloffEnd = 50f;
        [Range(0, 1)] public float minDamageFactor = 0.6f;
        public float range = 250f;

        [Header("Handling")]
        public float rpm = 600f;
        public int magSize = 30;
        public int reserveAmmo = 120;
        public float reloadTime = 2f;
        public bool reloadPerShell;
        public float equipTime = 0.4f;
        public float moveSpeedMultiplier = 1f;

        [Header("Accuracy (degrees)")]
        public float hipSpread = 2f;
        public float adsSpread = 0.25f;
        public float moveSpreadAdd = 1.5f;
        public float airSpreadAdd = 4f;
        public float spreadPerShot = 0.35f;
        public float maxBloom = 3f;
        public float bloomRecovery = 7f;

        [Header("Recoil")]
        public float recoilPitch = 1f;
        public float recoilYaw = 0.35f;
        public float recoilRecovery = 9f;
        public float kickBack = 0.04f;
        public float kickRotation = 5f;

        [Header("Aim Down Sights")]
        public float adsFov = 55f;
        public float adsTime = 0.16f;
        public bool scoped;
        public float adsSensitivity = 0.8f;
        public float adsMoveMultiplier = 0.75f;

        [Header("Presentation")]
        public GameObject model;
        public Vector3 hipPosition = new Vector3(0.11f, -0.11f, 0.2f);
        public Vector3 hipEuler = new Vector3(0f, -2f, 0f);
        public float sightDistance = 0.17f;
        public Vector3 adsOffset;
        public SupportGrip supportGrip;
        public float viewScale = 0.5f;
        public Color tracerColor = new Color(1f, 0.9f, 0.6f);
        public float shakeOnFire = 0.15f;
        public Texture2D icon;

        [Header("Audio")]
        public AudioClip fireSound;      // close-up shot, short so automatic fire stays crisp
        public AudioClip tailOutdoor;    // the shot ringing off buildings in the open
        public AudioClip tailIndoor;     // ...and under a roof / between close walls
        public AudioClip reloadSequence; // whole magazine reload, timed to the reload animation
        public AudioClip reloadOut;
        public AudioClip reloadIn;       // per-shell weapons: one shell going in
        public AudioClip actionSound;   // pump / bolt / slide
        public AudioClip equipSound;

        public float FireInterval => 60f / Mathf.Max(1f, rpm);

        public float DamageAt(float distance, HitZone zone)
        {
            float t = Mathf.InverseLerp(falloffStart, falloffEnd, distance);
            float d = damage * Mathf.Lerp(1f, minDamageFactor, t);
            switch (zone)
            {
                case HitZone.Head: d *= headMultiplier; break;
                case HitZone.Limb: d *= limbMultiplier; break;
            }
            return d;
        }
    }
}
