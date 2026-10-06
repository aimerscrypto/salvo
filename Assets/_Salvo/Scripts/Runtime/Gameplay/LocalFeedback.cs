using UnityEngine;

namespace Salvo
{
    /// <summary>Audio + camera reactions for the local player (footsteps, jumps, landings, damage).</summary>
    public class LocalFeedback : MonoBehaviour
    {
        Combatant c;
        int stepIndex;

        public void Bind(Combatant combatant)
        {
            c = combatant;
            var m = c.Motor;
            var bank = AudioManager.Bank;
            m.Footstep += () =>
            {
                if (bank == null || bank.footsteps == null || bank.footsteps.Length == 0) return;
                stepIndex = (stepIndex + 1 + Random.Range(0, bank.footsteps.Length - 1)) % bank.footsteps.Length;
                AudioManager.Play2D(bank.footsteps[stepIndex], c.Motor.Crouching ? 0.25f : 0.5f, Random.Range(0.92f, 1.08f));
            };
            m.Jumped += () => AudioManager.Play2D(bank?.jump, 0.45f, Random.Range(0.95f, 1.05f));
            m.Landed += speed =>
            {
                AudioManager.Play2D(bank?.land, Mathf.Clamp01(speed / 14f) * 0.8f + 0.2f, Random.Range(0.9f, 1.05f));
                GameCamera.I?.LandImpact(speed);
            };
            m.SlideStarted += () => AudioManager.Play2D(bank?.slide, 0.6f);
            m.JumpPadUsed += () => GameCamera.I?.Shake(0.25f);
            c.Damaged += (dmg, from, attacker, zone) =>
            {
                AudioManager.Play2D(bank?.hurt, Mathf.Clamp01(dmg / 40f) * 0.6f + 0.35f, Random.Range(0.9f, 1.1f));
                GameCamera.I?.Shake(Mathf.Clamp(dmg / 70f, 0.12f, 0.5f));
                Hud.I?.OnLocalDamaged(dmg, from);
            };
            c.Died += (killer, weapon, head, dir) => AudioManager.Play2D(bank?.death, 0.8f);
        }
    }
}
