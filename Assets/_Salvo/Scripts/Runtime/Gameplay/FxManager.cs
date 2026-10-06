using UnityEngine;
using UnityEngine.Rendering;

namespace Salvo
{
    /// <summary>
    /// Every combat effect goes through a handful of world-space particle systems created in code
    /// (dust, sparks, shards, smoke, tracers, decals, flashes, shells): each system is a single
    /// draw call no matter how many shots are in flight.
    /// </summary>
    public class FxManager : MonoBehaviour
    {
        public static FxManager I { get; private set; }

        ParticleSystem dust, sparks, shards, smoke, tracers, decals, flashes, shells;
        static readonly Color DustColor = new Color(0.86f, 0.8f, 0.68f, 0.9f);

        void Awake()
        {
            I = this;
            var cfg = GameConfig.I;
            var puffMat = cfg.fxAlpha;
            dust = Make("Dust", puffMat, 300, ParticleSystemRenderMode.Billboard, 0f, true);
            smoke = Make("Smoke", puffMat, 120, ParticleSystemRenderMode.Billboard, -0.05f, true);
            sparks = Make("Sparks", MaterialWithTex(puffMat, cfg.sparkTexture, "SparkMat"), 200, ParticleSystemRenderMode.Stretch, 1.5f, false);
            var sr = sparks.GetComponent<ParticleSystemRenderer>(); sr.velocityScale = 0.03f; sr.lengthScale = 1f;
            shards = Make("Shards", MaterialWithTex(puffMat, cfg.sparkTexture, "ShardMat"), 200, ParticleSystemRenderMode.Billboard, 1.6f, false);
            tracers = Make("Tracers", cfg.fxTracer, 96, ParticleSystemRenderMode.Stretch, 0f, false);
            var tr = tracers.GetComponent<ParticleSystemRenderer>(); tr.velocityScale = 0.016f; tr.lengthScale = 0f;
            decals = Make("Decals", cfg.fxDecal, 72, ParticleSystemRenderMode.Billboard, 0f, false);
            var dr = decals.GetComponent<ParticleSystemRenderer>(); dr.alignment = ParticleSystemRenderSpace.World;
            var dcol = decals.colorOverLifetime; dcol.enabled = true; dcol.color = FadeOut(0.85f);
            flashes = Make("Flashes", cfg.fxFlash, 32, ParticleSystemRenderMode.Billboard, 0f, false);
            shells = Make("Shells", cfg.weaponMaterial, 48, ParticleSystemRenderMode.Mesh, 2.2f, false);
            var shr = shells.GetComponent<ParticleSystemRenderer>();
            shr.mesh = BuildShellMesh();
            shr.alignment = ParticleSystemRenderSpace.World;
            var shm = shells.main; shm.startRotation3D = true;
        }

        void OnDestroy() { if (I == this) I = null; }

        static Material MaterialWithTex(Material baseMat, Texture tex, string name)
        {
            var m = new Material(baseMat) { name = name };
            if (tex != null) m.SetTexture("_BaseMap", tex);
            return m;
        }

        static Gradient FadeOut(float holdUntil)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, holdUntil), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        ParticleSystem Make(string name, Material mat, int max, ParticleSystemRenderMode mode, float gravity, bool grow)
        {
            var go = new GameObject("FX_" + name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            main.gravityModifier = gravity;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = false;
            var col = ps.colorOverLifetime; col.enabled = true; col.color = FadeOut(grow ? 0.25f : 0.6f);
            if (grow)
            {
                var sz = ps.sizeOverLifetime; sz.enabled = true;
                sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.5f, 1f, 1.4f));
                var lv = ps.limitVelocityOverLifetime; lv.enabled = true; lv.dampen = 0.15f; lv.limit = 0.5f;
            }
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = mode;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.sortMode = ParticleSystemSortMode.None;
            ps.Play();
            return ps;
        }

        static Mesh BuildShellMesh()
        {
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            var mesh = Instantiate(tmp.GetComponent<MeshFilter>().sharedMesh);
            Destroy(tmp);
            var v = mesh.vertices;
            for (int i = 0; i < v.Length; i++) v[i] = new Vector3(v[i].x * 0.011f, v[i].y * 0.016f, v[i].z * 0.011f);
            mesh.vertices = v;
            mesh.RecalculateBounds();
            return mesh;
        }

        static void Emit(ParticleSystem ps, Vector3 pos, Vector3 vel, float size, float life, Color col, float rot = 0f)
        {
            var ep = new ParticleSystem.EmitParams
            {
                position = pos, velocity = vel, startSize = size, startLifetime = life, startColor = col,
                rotation = rot, applyShapeToPosition = false
            };
            ps.Emit(ep, 1);
        }

        // ------------------------------------------------------------------ public API
        public void Dust(Vector3 pos, Vector3 normal, int count, Color color)
        {
            for (int i = 0; i < count; i++)
                Emit(dust, pos + Random.insideUnitSphere * 0.05f, (normal + Random.insideUnitSphere * 0.6f) * Random.Range(0.6f, 1.6f),
                     Random.Range(0.18f, 0.35f), Random.Range(0.35f, 0.7f), color, Random.Range(0f, 360f));
        }

        public void Impact(Vector3 pos, Vector3 normal, bool decal = true)
        {
            Dust(pos + normal * 0.03f, normal, 3, DustColor);
            for (int i = 0; i < 3; i++)
                Emit(sparks, pos + normal * 0.02f, (normal * 2f + Random.insideUnitSphere * 2.2f) * Random.Range(1.2f, 2.6f),
                     0.035f, Random.Range(0.12f, 0.25f), new Color(1f, 0.86f, 0.55f));
            if (decal)
            {
                var ep = new ParticleSystem.EmitParams
                {
                    position = pos + normal * 0.012f,
                    rotation3D = Quaternion.LookRotation(-normal, Mathf.Abs(normal.y) > 0.9f ? Vector3.forward : Vector3.up).eulerAngles + new Vector3(0, 0, Random.Range(0f, 360f)),
                    startSize = Random.Range(0.11f, 0.15f), startLifetime = 14f, startColor = Color.white, applyShapeToPosition = false
                };
                decals.Emit(ep, 1);
            }
        }

        public void CharacterHit(Vector3 pos, Vector3 dir, Color color, bool head)
        {
            int n = head ? 9 : 5;
            for (int i = 0; i < n; i++)
                Emit(shards, pos, (-dir * 1.5f + Random.insideUnitSphere * 2.5f + Vector3.up) * Random.Range(1f, 2.2f),
                     Random.Range(0.05f, 0.1f), Random.Range(0.3f, 0.55f), i % 3 == 0 ? Color.white : color, Random.Range(0f, 360f));
        }

        public void Tracer(Vector3 from, Vector3 to, Color color)
        {
            Vector3 d = to - from;
            float dist = d.magnitude;
            if (dist < 1.5f) return;
            const float speed = 420f;
            Emit(tracers, from + d / dist * 0.8f, d / dist * speed, 0.05f, Mathf.Max(0.02f, (dist - 1f) / speed), color);
        }

        public void MuzzleFlash(Vector3 pos, float size)
        {
            Emit(flashes, pos, Vector3.zero, size, 0.05f, Color.white, Random.Range(0f, 360f));
            Emit(smoke, pos, Random.insideUnitSphere * 0.3f + Vector3.up * 0.4f, 0.25f, 0.6f, new Color(0.9f, 0.9f, 0.88f, 0.35f), Random.Range(0f, 360f));
        }

        public void DeathPoof(Vector3 pos, Color team)
        {
            for (int i = 0; i < 10; i++)
                Emit(smoke, pos + Random.insideUnitSphere * 0.4f, Random.insideUnitSphere * 1.6f + Vector3.up * 0.6f,
                     Random.Range(0.5f, 0.9f), Random.Range(0.6f, 1.1f), new Color(0.95f, 0.93f, 0.88f, 0.85f), Random.Range(0f, 360f));
            for (int i = 0; i < 12; i++)
                Emit(shards, pos, Random.insideUnitSphere * 4f + Vector3.up * 2.5f, Random.Range(0.06f, 0.12f), Random.Range(0.5f, 0.9f), i % 2 == 0 ? team : Color.white, Random.Range(0f, 360f));
        }

        public void EjectShell(Vector3 pos, Vector3 right, bool shotgun)
        {
            var ep = new ParticleSystem.EmitParams
            {
                position = pos,
                velocity = right * Random.Range(1.6f, 2.4f) + Vector3.up * Random.Range(1.2f, 2f) + Random.insideUnitSphere * 0.3f,
                rotation3D = Random.insideUnitSphere * 180f,
                angularVelocity3D = Random.insideUnitSphere * 720f,
                startSize = shotgun ? 1.5f : 1f,
                startLifetime = 0.55f,
                startColor = shotgun ? new Color(0.82f, 0.25f, 0.2f) : new Color(0.85f, 0.66f, 0.3f),
                applyShapeToPosition = false
            };
            shells.Emit(ep, 1);
        }

        public GameObject CreateBlobShadow(Transform parent) => null;

        // ------------------------------------------------------------------ shots
        public void PlayShotLocal(Combatant shooter, ShotData shot)
        {
#if DEVELOPMENT_BUILD
            Bootstrap.FrameShots++;
#endif
            var def = GameConfig.I.Weapon(shot.weapon);
            Vector3 muzzle = shooter.IsLocalHuman ? LocalMuzzle(shooter) : shooter.Visual != null ? shooter.Visual.MuzzlePosition : shot.origin;
            if (!shooter.IsLocalHuman)
            {
                MuzzleFlash(muzzle, def != null && def.pellets > 1 ? 0.55f : 0.4f);
                AudioManager.Gunshot(def, muzzle, Random.Range(0.95f, 1.05f));
                RemoteCasing(muzzle, def);
            }
            PlayEnds(shot, muzzle, def);
        }

        public void PlayShotRemote(Combatant shooter, ShotData shot)
        {
#if DEVELOPMENT_BUILD
            Bootstrap.FrameShots++;
#endif
            var def = GameConfig.I.Weapon(shot.weapon);
            if (def == null) return;
            if (def.fireMode == FireMode.Melee)
            {
                AudioManager.Play3D(AudioManager.Bank?.meleeSwing, shot.origin, 0.8f);
                return;
            }
            Vector3 muzzle = shooter.Visual != null ? shooter.Visual.MuzzlePosition : shot.origin;
            MuzzleFlash(muzzle, def.pellets > 1 ? 0.55f : 0.4f);
            AudioManager.Gunshot(def, muzzle, Random.Range(0.95f, 1.05f));
            RemoteCasing(muzzle, def);
            PlayEnds(shot, muzzle, def);
            // whiz when a shot passes close to the local player
            var me = Combatant.Local;
            if (me != null && me.Alive && shot.ends != null && shot.ends.Length > 0)
            {
                Vector3 a = shot.origin, b = shot.ends[0];
                Vector3 p = me.EyePosition;
                Vector3 ab = b - a;
                float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(0.01f, ab.sqrMagnitude));
                if ((a + ab * t - p).sqrMagnitude < 3f * 3f && t > 0.05f && t < 0.98f)
                    AudioManager.NearMiss(a + ab * t);
            }
        }

        /// <summary>Other players' brass, heard only up close (semi / auto guns eject on every shot).</summary>
        static void RemoteCasing(Vector3 muzzle, WeaponDef def)
        {
            if (def == null || def.fireMode == FireMode.Pump || def.fireMode == FireMode.Bolt || def.fireMode == FireMode.Melee) return;
            var me = Combatant.Local;
            if (me == null || (me.EyePosition - muzzle).sqrMagnitude > 12f * 12f || Random.value > 0.6f) return;
            AudioManager.Casing(muzzle, false, false);
        }

        static Vector3 LocalMuzzle(Combatant c)
        {
            var vm = c.GetComponent<ViewModel>();
            return vm != null ? vm.MuzzleWorld : c.EyePosition;
        }

        void PlayEnds(ShotData shot, Vector3 muzzle, WeaponDef def)
        {
            if (shot.ends == null) return;
            Color tc = def != null ? def.tracerColor : Color.white;
            for (int i = 0; i < shot.ends.Length; i++)
            {
                Vector3 end = shot.ends[i];
                if (def == null || def.pellets <= 1 || i % 2 == 0) Tracer(muzzle, end, tc);
                switch (shot.endKinds[i])
                {
                    case 0:
                        Impact(end, shot.normals[i]);
                        if (i == 0 && AudioManager.Bank != null)
                            AudioManager.PlayWorld(AudioManager.Bank.Impact(Surfaces.At(end, shot.normals[i])), end, 0.5f, Random.Range(0.9f, 1.1f), 30f);
                        break;
                    case 1:
                        CharacterHit(end, (end - shot.origin).normalized, new Color(1f, 0.85f, 0.4f), false);
                        break;
                }
            }
        }
    }
}
