using UnityEngine;

namespace Salvo
{
    /// <summary>
    /// Pooled one-shot audio (2D UI/first-person + 3D world) and a music channel.
    /// World sounds behave like they do outdoors: inverse-distance falloff, highs fading with
    /// distance (air absorption), muffled through walls, gunfire arriving at the speed of sound
    /// with a distant rumble and an echo off nearby buildings.
    /// Category volumes come from Settings; no AudioMixer needed.
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager I { get; private set; }

        const int PoolSize = 32;
        AudioSource[] pool;
        AudioLowPassFilter[] filters;
        int next;
        AudioSource music;
        AudioSource ambience;
        float musicTarget;

        public static SoundBank Bank => GameConfig.I != null ? GameConfig.I.sounds : null;

        void Awake()
        {
            I = this;
            pool = new AudioSource[PoolSize];
            filters = new AudioLowPassFilter[PoolSize];
            // roughly inverse-distance (natural) falloff, reaching silence at maxDistance
            var rolloff = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.04f, 1f), new Keyframe(0.1f, 0.5f),
                new Keyframe(0.2f, 0.26f), new Keyframe(0.4f, 0.12f), new Keyframe(0.7f, 0.045f), new Keyframe(1f, 0f));
            for (int i = 0; i < PoolSize; i++)
            {
                var go = new GameObject("sfx" + i);
                go.transform.SetParent(transform, false);
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.dopplerLevel = 0f;
                s.rolloffMode = AudioRolloffMode.Custom;
                s.SetCustomCurve(AudioSourceCurveType.CustomRolloff, rolloff);
                s.spread = 30f;
                pool[i] = s;
                var lp = go.AddComponent<AudioLowPassFilter>();
                lp.lowpassResonanceQ = 1f;
                lp.enabled = false;
                filters[i] = lp;
            }
            music = gameObject.AddComponent<AudioSource>();
            music.loop = true;
            music.playOnAwake = false;
            music.spatialBlend = 0f;
            ambience = gameObject.AddComponent<AudioSource>();
            ambience.loop = true;
            ambience.playOnAwake = false;
        }

        int lastIndex;
        AudioSource Next()
        {
            // prefer a free source; otherwise steal the oldest
            for (int k = 0; k < PoolSize; k++)
            {
                int i = (next + k) % PoolSize;
                if (!pool[i].isPlaying) { next = (i + 1) % PoolSize; lastIndex = i; filters[i].enabled = false; return pool[i]; }
            }
            lastIndex = next;
            next = (next + 1) % PoolSize;
            filters[lastIndex].enabled = false;
            return pool[lastIndex];
        }

        /// <summary>Where the player hears from (the active audio listener / camera).</summary>
        AudioListener listener;
        Vector3 Ear
        {
            get
            {
                if (listener == null || !listener.isActiveAndEnabled) listener = FindFirstObjectByType<AudioListener>();
                return listener != null ? listener.transform.position : Vector3.zero;
            }
        }

        public const float SpeedOfSound = 343f;

        public static void Play2D(AudioClip clip, float volume = 1f, float pitch = 1f, bool ui = false)
        {
            if (clip == null || I == null) return;
            var s = I.Next();
            s.transform.localPosition = Vector3.zero;
            s.spatialBlend = 0f;
            s.pitch = pitch;
            s.volume = volume * (ui ? Settings.Data.ui : Settings.Data.sfx);
            s.priority = 64;
            s.clip = clip;
            s.Play();
        }

        public static void Play3D(AudioClip clip, Vector3 pos, float volume = 1f, float pitch = 1f, float maxDistance = 45f)
            => PlayWorld(clip, pos, volume, pitch, maxDistance);

        /// <summary>
        /// A sound in the world. range: silent beyond it. occlude: muffle when a wall blocks the line
        /// to the listener. travel: arrive after distance / speed of sound (gunfire). extraDelay and
        /// maxCutoff shape reflections (echoes).
        /// </summary>
        public static void PlayWorld(AudioClip clip, Vector3 pos, float volume = 1f, float pitch = 1f, float range = 45f,
            bool occlude = true, bool travel = false, float extraDelay = 0f, float maxCutoff = 22000f)
        {
            if (clip == null || I == null) return;
            Vector3 ear = I.Ear;
            float dist = Vector3.Distance(ear, pos);
            if (dist > range) return;
            // air absorption: distant sounds lose their highs
            float cutoff = Mathf.Lerp(22000f, 2600f, Mathf.SmoothStep(0f, 1f, (dist - 8f) / Mathf.Max(1f, range * 0.7f)));
            if (occlude && dist > 1.5f && Physics.Linecast(ear, pos + (ear - pos).normalized * 0.4f, Layers.WorldMask, QueryTriggerInteraction.Ignore))
            {
                volume *= 0.5f;
                cutoff = Mathf.Min(cutoff, 1100f);
            }
            cutoff = Mathf.Min(cutoff, maxCutoff);
            var s = I.Next();
            var lp = I.filters[I.lastIndex];
            lp.cutoffFrequency = cutoff;
            lp.enabled = cutoff < 20000f;
            s.transform.position = pos;
            s.spatialBlend = 1f;
            s.minDistance = 1f;
            s.maxDistance = range;
            s.pitch = pitch;
            s.volume = volume * Settings.Data.sfx;
            s.priority = 128;
            s.clip = clip;
            float delay = (travel ? dist / SpeedOfSound : 0f) + extraDelay;
            if (delay > 0.015f) s.PlayDelayed(delay); else s.Play();
        }

        static float lastEcho;

        /// <summary>
        /// Gunfire somebody else fired: the crack (speed-of-sound delayed, highs fading with distance),
        /// a low rumble that takes over far away, and an echo off the surroundings.
        /// </summary>
        public static void Gunshot(AudioClip fire, Vector3 pos, float pitch)
        {
            if (fire == null || I == null) return;
            float dist = Vector3.Distance(I.Ear, pos);
            float far = Mathf.Clamp01((dist - 20f) / 60f);
            PlayWorld(fire, pos, Mathf.Lerp(1f, 0.55f, far), pitch, 170f, true, true);
            if (far > 0f && Bank != null && Bank.distantShot != null)
                PlayWorld(Bank.distantShot, pos, far * 0.9f, pitch * Random.Range(0.94f, 1.04f), 260f, false, true);
            Echo(fire, pos, pitch);
        }

        /// <summary>First-person gunfire: the dry shot plays 2D; the world answers with an echo.</summary>
        public static void OwnGunshot(AudioClip fire, Vector3 pos, float pitch) => Echo(fire, pos, pitch);

        /// <summary>
        /// One reflection: close walls give a quick slap-back, open ground a late, soft tail.
        /// Throttled so automatic fire doesn't stack dozens of echoes.
        /// </summary>
        static void Echo(AudioClip fire, Vector3 pos, float pitch)
        {
            if (I == null || Time.time - lastEcho < 0.09f) return;
            lastEcho = Time.time;
            float sum = 0f; int hits = 0;
            for (int i = 0; i < 6; i++)
            {
                Vector3 d = Quaternion.Euler(0, i * 60f + Random.Range(-15f, 15f), 0) * Vector3.forward;
                if (Physics.Raycast(pos + Vector3.up * 0.3f, d, out var hit, 40f, Layers.WorldMask, QueryTriggerInteraction.Ignore)) { sum += hit.distance; hits++; }
            }
            float wall = hits > 0 ? sum / hits : 40f;
            float delay = Mathf.Clamp(2f * wall / SpeedOfSound, 0.05f, 0.24f);
            float vol = Mathf.Lerp(0.22f, 0.1f, Mathf.InverseLerp(5f, 35f, wall)) * Mathf.Lerp(0.6f, 1f, hits / 6f);
            var tail = Bank != null && Bank.distantShot != null ? Bank.distantShot : fire;
            PlayWorld(tail, pos, vol, pitch * 0.92f, 170f, false, true, delay, 1500f);
        }

        public static void PlayUI(AudioClip clip, float volume = 1f) => Play2D(clip, volume, Random.Range(0.97f, 1.03f), true);

        public static void Music(AudioClip clip, float volume = 0.6f)
        {
            if (I == null) return;
            if (I.music.clip != clip)
            {
                I.music.clip = clip;
                if (clip != null) I.music.Play(); else I.music.Stop();
            }
            I.musicTarget = volume;
        }

        public static void Ambience(AudioClip clip, float volume = 0.35f)
        {
            if (I == null) return;
            if (I.ambience.clip != clip)
            {
                I.ambience.clip = clip;
                if (clip != null) I.ambience.Play(); else I.ambience.Stop();
            }
            I.ambience.volume = volume * Settings.Data.sfx;
        }

        void Update()
        {
            float target = musicTarget * Settings.Data.music;
            music.volume = Mathf.MoveTowards(music.volume, target, Time.unscaledDeltaTime * 0.5f);
        }
    }
}
