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

        const int PoolSize = 40;
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

        static float lastRemoteTail;

        /// <summary>
        /// 0 = open air, 1 = under a roof between close walls. Decides which reverb tail a shot gets.
        /// </summary>
        public static float Enclosure(Vector3 pos)
        {
            pos += Vector3.up * 0.3f;
            float roof = Physics.Raycast(pos, Vector3.up, 9f, Layers.WorldMask, QueryTriggerInteraction.Ignore) ? 1f : 0f;
            int close = 0;
            for (int i = 0; i < 6; i++)
            {
                Vector3 d = Quaternion.Euler(0, i * 60f + 15f, 0) * Vector3.forward;
                if (Physics.Raycast(pos, d, 7f, Layers.WorldMask, QueryTriggerInteraction.Ignore)) close++;
            }
            return Mathf.Clamp01(roof * 0.6f + close / 6f * 0.5f);
        }

        AudioSource ownTailOut, ownTailIn;
        float duck;

        AudioSource TailSource(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0f;
            s.priority = 40;
            return s;
        }

        /// <summary>
        /// Your own shot: the close blast (2D, the loudest thing you hear) and the space answering it.
        /// The tail restarts on every shot, so a burst rings out once when you let go, and the
        /// ambience ducks under the shot the way your ears do.
        /// </summary>
        /// <summary>Your own shot sits a little under full scale so reloads, steps and callouts stay audible.</summary>
        const float OwnShotVolume = 0.56f;

        public static void OwnGunshot(WeaponDef d, Vector3 pos, float pitch)
        {
            if (d == null || I == null) return;
            Play2D(d.fireSound, Mathf.Min(1f, OwnShotVolume * d.shotVolume), pitch);
            if (I.ownTailOut == null) { I.ownTailOut = I.TailSource("ownTailOut"); I.ownTailIn = I.TailSource("ownTailIn"); }
            float enc = Enclosure(pos);
            Restart(I.ownTailOut, d.tailOutdoor, (1f - enc) * 0.38f * d.shotVolume, pitch);
            Restart(I.ownTailIn, d.tailIndoor, enc * 0.45f * d.shotVolume, pitch);
            I.duck = Mathf.Max(I.duck, d.pellets > 1 || d.fireMode == FireMode.Bolt ? 1f : 0.6f);
        }

        static void Restart(AudioSource s, AudioClip clip, float volume, float pitch)
        {
            if (clip == null || volume < 0.02f) return;
            s.clip = clip;
            s.pitch = pitch;
            s.volume = volume * Settings.Data.sfx;
            s.Play();
        }

        /// <summary>
        /// Gunfire somebody else fired: the blast (speed-of-sound delayed, highs fading with distance,
        /// muffled through walls), the space ringing around it, and a low rumble far away.
        /// The farther the shooter, the more you hear the space and the less the blast.
        /// </summary>
        public static void Gunshot(WeaponDef d, Vector3 pos, float pitch)
        {
            if (d == null || d.fireSound == null || I == null) return;
            float dist = Vector3.Distance(I.Ear, pos);
            // up close it's the full blast; with distance the punch and highs go first, leaving a
            // dull pop and the space ringing (the close layer is mixed for point-blank, not 60 m)
            float far = Mathf.Clamp01((dist - 8f) / 55f);
            float cutoff = Mathf.Lerp(22000f, 1500f, Mathf.Pow(far, 0.6f));
            PlayWorld(d.fireSound, pos, Mathf.Min(1f, Mathf.Lerp(0.6f, 0.22f, far) * d.shotVolume), pitch, 170f, true, true, 0f, cutoff);
            // one tail at a time across all remote guns: a firefight rings, it doesn't pile up voices
            if (Time.time - lastRemoteTail > 0.11f)
            {
                lastRemoteTail = Time.time;
                float enc = Enclosure(pos);
                var tail = enc > 0.5f ? d.tailIndoor : d.tailOutdoor;
                PlayWorld(tail, pos, Mathf.Lerp(0.3f, 0.42f, far), pitch, 220f, false, true, 0f, Mathf.Lerp(7000f, 1800f, far));
            }
            if (far > 0f && Bank != null && Bank.distantShot != null)
                PlayWorld(Bank.distantShot, pos, far * 0.3f, pitch * Random.Range(0.94f, 1.04f), 260f, false, true);
        }

        /// <summary>A spent case bouncing off the ground a moment after it was ejected.</summary>
        public static void Casing(Vector3 ejectPos, bool shotgunShell, bool own)
        {
            if (I == null || Bank == null) return;
            Vector3 ground = Physics.Raycast(ejectPos, Vector3.down, out var hit, 4f, Layers.WorldMask, QueryTriggerInteraction.Ignore) ? hit.point : ejectPos + Vector3.down * 1.4f;
            float fall = Mathf.Sqrt(2f * Mathf.Max(0.2f, ejectPos.y - ground.y + 0.4f) / 9.81f);
            PlayWorld(Bank.Casing(), ground, own ? 0.35f : 0.3f, shotgunShell ? Random.Range(0.62f, 0.72f) : Random.Range(0.9f, 1.12f), own ? 10f : 14f,
                !own, false, fall + 0.12f, shotgunShell ? 2600f : 22000f);
        }

        /// <summary>A bullet passing close: the supersonic crack, then the whiz.</summary>
        public static void NearMiss(Vector3 pos)
        {
            if (I == null || Bank == null) return;
            PlayWorld(Bank.Crack(), pos, 0.75f, Random.Range(0.92f, 1.1f), 25f, false);
            PlayWorld(Bank.bulletWhiz, pos, 0.3f, Random.Range(0.9f, 1.15f), 20f, false);
        }

        /// <summary>Plays a 2D clip on its own voice and hands it back (e.g. to stop a reload when cancelled).</summary>
        public static AudioSource Play2DTracked(AudioClip clip, float volume = 1f, float pitch = 1f)
        {
            if (clip == null || I == null) return null;
            Play2D(clip, volume, pitch);
            return I.pool[I.lastIndex];
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
            I.ambienceBase = volume;
        }

        float ambienceBase;

        void Update()
        {
            // your own gunfire briefly drowns out the background (fast attack, slow release)
            duck = Mathf.MoveTowards(duck, 0f, Time.unscaledDeltaTime * 1.6f);
            float target = musicTarget * Settings.Data.music * (1f - duck * 0.5f);
            music.volume = Mathf.MoveTowards(music.volume, target, Time.unscaledDeltaTime * (music.volume > target ? 4f : 0.5f));
            ambience.volume = ambienceBase * Settings.Data.sfx * (1f - duck * 0.7f);
        }
    }
}
