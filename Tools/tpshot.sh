#!/usr/bin/env bash
# Third-person look-dev: freeze a bot, give it a weapon and move the local player's camera in front
# of it. usage: tpshot.sh <primaryId> <slot 0|1|2> <viewAngle> [name]   (practice match with a bot running)
#   viewAngle: degrees around the bot, 0 = straight in front, positive = towards the bot's right side
cd "$(dirname "$0")/.."
P=${1:-2}; SLOT=${2:-0}; ANG=${3:-30}; NAME=${4:-tp_${P}_${SLOT}_${ANG}}
BOT='Salvo.Combatant bot = null; foreach (var c in Salvo.Combatant.All) if (c != Salvo.Combatant.Local) { bot = c; break; }'
Tools/u.sh eval "$BOT bot.Brain.enabled = false; var l = bot.Loadout.Value; l.primary = (byte)$P; bot.Loadout.Value = l; bot.Weapons.In.selectSlot = $SLOT; bot.Motor.Input.pitch = 0; return \"ok\";" >/dev/null
sleep 1.5
Tools/u.sh eval "$BOT var me = Salvo.Combatant.Local; float yaw = bot.Motor.Input.yaw; var dir = UnityEngine.Quaternion.Euler(0, yaw + $ANG, 0) * UnityEngine.Vector3.forward; var pos = bot.transform.position + dir * 2.0f; me.Motor.Teleport(pos, yaw + $ANG + 180f); me.Motor.Input.pitch = 8; var cam = Salvo.GameCamera.I.GetComponentInChildren<UnityEngine.Camera>(); cam.cullingMask &= ~(1 << Salvo.Layers.ViewModel); return \"ok\";" >/dev/null
sleep 1.2
Tools/cap.sh "$NAME"
Tools/u.sh eval 'var cam = Salvo.GameCamera.I.GetComponentInChildren<UnityEngine.Camera>(); cam.cullingMask |= 1 << Salvo.Layers.ViewModel; return "ok";' >/dev/null
