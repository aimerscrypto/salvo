#!/usr/bin/env bash
# Enter play mode and start an offline practice match (bots optional: $1 = bot fill)
cd "$(dirname "$0")/.."
unity command editor_stop --caller plugin --skill unity-cli >/dev/null 2>&1; sleep 3
unity command editor_play --caller plugin --skill unity-cli >/dev/null 2>&1; sleep ${WAIT:-35}
Tools/u.sh eval "Salvo.Hud.SkipPicker = true; Salvo.GameFlow.StartLocal(new Salvo.MatchSettings{ fillWithBots = ${1:-6}, offline = true }, false); return \"started\";" >/dev/null
sleep 25
Tools/u.sh eval 'var me = Salvo.Combatant.Local; return me == null ? "no local player" : "local at " + me.transform.position.ToString("F1");'
