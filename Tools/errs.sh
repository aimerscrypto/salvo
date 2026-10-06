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
