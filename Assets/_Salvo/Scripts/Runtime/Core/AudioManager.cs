using UnityEngine;

namespace Salvo
{
    /// <summary>
    /// Pooled one-shot audio (2D UI/first-person + 3D world) and a music channel.
    /// Category volumes come from Settings; no AudioMixer needed.
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager I { get; private set; }

        const int PoolSize = 28;
        AudioSource[] pool;
        int next;
        AudioSource music;
        AudioSource ambience;
        float musicTarget;

        public static SoundBank Bank => GameConfig.I != null ? GameConfig.I.sounds : null;

        void Awake()
        {
            I = this;
            pool = new AudioSource[PoolSize];
            for (int i = 0; i < PoolSize; i++)
            {
                var go = new GameObject("sfx" + i);
                go.transform.SetParent(transform, false);
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.dopplerLevel = 0f;
                s.rolloffMode = AudioRolloffMode.Custom;
                s.SetCustomCurve(AudioSourceCurveType.CustomRolloff, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));
                pool[i] = s;
            }
            music = gameObject.AddComponent<AudioSource>();
            music.loop = true;
            music.playOnAwake = false;
            music.spatialBlend = 0f;
            ambience = gameObject.AddComponent<AudioSource>();
            ambience.loop = true;
            ambience.playOnAwake = false;
        }

        AudioSource Next()
        {
            // prefer a free source; otherwise steal the oldest
            for (int k = 0; k < PoolSize; k++)
            {
                var s = pool[(next + k) % PoolSize];
                if (!s.isPlaying) { next = (next + k + 1) % PoolSize; return s; }
            }
            var steal = pool[next];
            next = (next + 1) % PoolSize;
            return steal;
        }

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
        {
            if (clip == null || I == null) return;
            var s = I.Next();
            s.transform.position = pos;
            s.spatialBlend = 1f;
            s.minDistance = 2f;
            s.maxDistance = maxDistance;
            s.pitch = pitch;
            s.volume = volume * Settings.Data.sfx;
            s.priority = 128;
            s.clip = clip;
            s.Play();
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
