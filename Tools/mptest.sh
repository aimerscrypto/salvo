#!/usr/bin/env bash
# Editor joins a running LAN host (player exe) and performs the shoot-the-host test.
cd "$(dirname "$0")/.."
Tools/u.sh wait >/dev/null
for i in 1 2 3; do
  unity command editor_play --caller plugin --skill unity-cli >/dev/null 2>&1; sleep 30
  r=$(Tools/u.sh eval 'return UnityEngine.Application.isPlaying ? "yes" : "no";')
  echo "$r" | grep -q yes && break
done
sleep 10
Tools/u.sh eval 'Salvo.GameFlow.JoinDirect("127.0.0.1"); return "joining";' >/dev/null
for i in $(seq 1 20); do sleep 3; Tools/u.sh eval 'return Salvo.Combatant.Local != null ? "in" : "wait";' | grep -q "^in" && break; done
sleep 3
for t in 1 2; do
  Tools/u.sh eval 'var me = Salvo.Combatant.Local; if (!me.Alive) return "dead"; Salvo.Combatant host = null; foreach (var c in Salvo.Combatant.All) if (!c.IsBot && !c.IsOwner) host = c; var p = host.transform.position + host.transform.forward * 4f; var dir = host.CenterMass - (p + UnityEngine.Vector3.up * 1.62f); me.Motor.Teleport(p, UnityEngine.Mathf.Atan2(dir.x, dir.z) * UnityEngine.Mathf.Rad2Deg); me.Motor.Input.pitch = -UnityEngine.Mathf.Atan2(dir.y, new UnityEngine.Vector2(dir.x, dir.z).magnitude) * UnityEngine.Mathf.Rad2Deg; return "tp; client sees host at " + host.transform.position.ToString("F1") + " hp=" + host.Health.Value;'
  sleep 1.5
  Tools/u.sh eval 'var me = Salvo.Combatant.Local; Salvo.Combatant host = null; foreach (var c in Salvo.Combatant.All) if (!c.IsBot && !c.IsOwner) host = c; var dir = host.CenterMass - me.EyePosition; me.Motor.Input.yaw = UnityEngine.Mathf.Atan2(dir.x, dir.z) * UnityEngine.Mathf.Rad2Deg; me.Motor.Input.pitch = -UnityEngine.Mathf.Atan2(dir.y, new UnityEngine.Vector2(dir.x, dir.z).magnitude) * UnityEngine.Mathf.Rad2Deg; Salvo.WeaponController.DebugForceFire = true; return "fire";' >/dev/null; sleep 0.7
  Tools/u.sh eval 'Salvo.WeaponController.DebugForceFire = false; Salvo.Combatant host = null; foreach (var c in Salvo.Combatant.All) if (!c.IsBot && !c.IsOwner) host = c; var me = Salvo.Combatant.Local; return "host hp=" + host.Health.Value + " hits=" + me.Weapons.ShotsHit + " client sees host " + host.transform.position.ToString("F1") + " me " + me.transform.position.ToString("F1");'
  sleep 4
done
grep -a "SALVO/Server" Builds/host.log | head -4 | cut -c1-260
