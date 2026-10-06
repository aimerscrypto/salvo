#!/usr/bin/env bash
# Capture hip + ADS views for weapons ids in $@ (practice match must be running)
cd "$(dirname "$0")/.."
for w in "$@"; do
  slot=0; [ "$w" = "0" ] && slot=1; [ "$w" = "5" ] && slot=2
  Tools/u.sh eval "var me = Salvo.Combatant.Local; var l = me.Loadout.Value; if ($w != 0 && $w != 5) l.primary = $w; me.Loadout.Value = l; me.ServerRespawn(new UnityEngine.Vector3(0, 1.3f, -2.5f), 0); return \"ok\";" >/dev/null
  sleep 2
  Tools/u.sh eval "var me = Salvo.Combatant.Local; me.Motor.Input.yaw = 0; me.Motor.Input.pitch = 2; me.Weapons.In.selectSlot = $slot; return \"ok\";" >/dev/null
  sleep 2; Tools/cap.sh "hip_$w" >/dev/null
  Tools/u.sh eval 'Salvo.WeaponController.DebugForceAim = true; return "ok";' >/dev/null
  sleep 1.5; Tools/cap.sh "ads_$w" >/dev/null
  Tools/u.sh eval 'Salvo.WeaponController.DebugForceAim = false; return "ok";' >/dev/null
  sleep 0.5
done
echo done
