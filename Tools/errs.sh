#!/usr/bin/env bash
# Refresh, wait for compile, print unique compile errors
cd "$(dirname "$0")/.."
unity command clear_console --caller plugin --skill unity-cli >/dev/null 2>&1
unity command eval --caller plugin --skill unity-cli --code 'UnityEditor.AssetDatabase.Refresh(); return "ok";' >/dev/null 2>&1
sleep 6
Tools/u.sh wait >/dev/null
unity command console --caller plugin --skill unity-cli --level ${1:-error} --tail 80 --format json 2>/dev/null | python -c "
import sys,json
d=json.load(sys.stdin); r=d.get('data') or {}; r=r.get('result',r)
if isinstance(r,str): r=json.loads(r)
seen=set()
for e in r.get('entries',[]):
    m=(e.get('message') or '').split('\n')[0]
    if m in seen or 'api/exec' in m or 'state_change' in m: continue
    seen.add(m); print(m[:300])
print('--', len(seen), 'unique')
"
# compile errors can be missing from the console (e.g. cleared before a no-op refresh): ask the editor
until unity command eval --caller plugin --skill unity-cli --code 'return UnityEditor.EditorApplication.isCompiling ? "busy" : "idle";' 2>/dev/null | grep -q idle; do sleep 3; done
unity command eval --caller plugin --skill unity-cli --code 'return UnityEditor.EditorUtility.scriptCompilationFailed ? "COMPILE FAILED" : "";' 2>/dev/null | grep -o "COMPILE FAILED"
