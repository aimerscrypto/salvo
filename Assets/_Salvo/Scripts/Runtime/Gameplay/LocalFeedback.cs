using UnityEngine;

namespace Salvo
{
    /// <summary>Audio + camera reactions for the local player (footsteps, jumps, landings, damage).</summary>
    public class LocalFeedback : MonoBehaviour
    {
        Combatant c;

        public void Bind(Combatant combatant)
        {
            c = combatant;
            var m = c.Motor;
            var bank = AudioManager.Bank;
            m.Footstep += () =>
            {
                if (bank == null) return;
                AudioManager.Play2D(bank.Step(Surfaces.Under(c.transform.position)), c.Motor.Crouching ? 0.45f : 0.95f, Random.Range(0.92f, 1.08f));
            };
            // push-off: a scuff of the surface you jump from
            m.Jumped += () => { if (bank != null) AudioManager.Play2D(bank.Step(Surfaces.Under(c.transform.position)), 0.55f, Random.Range(1.02f, 1.12f)); };
            m.Landed += speed =>
            {
                float k = Mathf.Clamp01(speed / 14f);
                AudioManager.Play2D(k < 0.4f && bank?.landLight != null ? bank.landLight : bank?.land, k * 0.6f + 0.4f, Random.Range(0.92f, 1.05f));
                // the surface you land on: sand puff, wood thump, metal clang
                if (bank != null) AudioManager.Play2D(bank.Step(Surfaces.Under(c.transform.position)), k * 0.5f + 0.55f, Random.Range(0.85f, 0.95f));
                GameCamera.I?.LandImpact(speed);
            };
            m.SlideStarted += () => AudioManager.Play2D(bank?.slide, 0.8f);
            m.JumpPadUsed += () => GameCamera.I?.Shake(0.15f);
            c.Damaged += (dmg, from, attacker, zone) =>
            {
                AudioManager.Play2D(bank?.hurt, Mathf.Clamp01(dmg / 40f) * 0.6f + 0.35f, Random.Range(0.9f, 1.1f));
                GameCamera.I?.Shake(Mathf.Clamp(dmg / 120f, 0.06f, 0.3f));
                Hud.I?.OnLocalDamaged(dmg, from);
            };
            c.Died += (killer, weapon, head, dir) => AudioManager.Play2D(bank?.death, 0.8f);
        }
    }
}
