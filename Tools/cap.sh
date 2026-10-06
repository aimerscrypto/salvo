#!/usr/bin/env bash
# Capture the Game view; the tool writes under Assets/, so move it out to the scratch folder.
cd "$(dirname "$0")/.."
OUT="${CAP_DIR:-C:/Users/NAC/AppData/Local/Temp/claude/C--Users-NAC-Desktop-unity-multiplayer-fps/a52ae35d-368e-48ee-9020-94a72946b13a/scratchpad}"
unity command capture_game_view --caller plugin --skill unity-cli --save_path "Temp/Captures/${1:-cap}.png" >/dev/null 2>&1
mv -f "Assets/Temp/Captures/${1:-cap}.png" "$OUT/${1:-cap}.png" 2>/dev/null && echo "$OUT/${1:-cap}.png"
rm -rf Assets/Temp Assets/Temp.meta
