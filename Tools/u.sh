#!/usr/bin/env bash
# Helper for driving the live Unity Editor from the shell.
#   Tools/u.sh wait            -> block until the editor answers & is not compiling/updating
#   Tools/u.sh errors          -> print console errors/warnings (compact)
#   Tools/u.sh eval '<C#>'     -> run C# (return value printed)
#   Tools/u.sh evalf file.cs   -> run C# file
cd "$(dirname "$0")/.."
U="unity command"
F="--caller plugin --skill unity-cli"
case "$1" in
  wait)
    for i in $(seq 1 ${2:-200}); do
      r=$($U eval $F --code 'return (UnityEditor.EditorApplication.isCompiling || UnityEditor.EditorApplication.isUpdating) ? "busy" : "idle";' --format json 2>/dev/null)
      if echo "$r" | grep -q '"result": *"idle"'; then echo "idle"; exit 0; fi
      sleep 5
    done; echo "timeout"; exit 1 ;;
  errors)
    $U console $F --level ${2:-error} --tail ${3:-40} --format json 2>/dev/null | python -c "
import sys,json
d=json.load(sys.stdin)
data=d.get('data') or {}
res=data.get('result', data)
if isinstance(res,str):
    try: res=json.loads(res)
    except Exception: pass
ents=res.get('entries',[]) if isinstance(res,dict) else []
seen=set()
for e in ents:
    m=(e.get('message') or e.get('condition') or '').strip().split('\n')[0][:400]
    if m in seen: continue
    seen.add(m); print('-', e.get('type') or e.get('level'), m)
print('total', len(ents))
" ;;
  eval)
    $U eval $F --timeout ${3:-600000} --code "$2" --format json 2>&1 | python -c "
import sys,json
raw=sys.stdin.read()
try:
    d=json.loads(raw)
except Exception:
    print(raw[-2000:]); sys.exit(1)
data=d.get('data') or {}
res=data.get('result', data)
if isinstance(res,str):
    try: res=json.loads(res)
    except Exception: pass
if isinstance(res,dict):
    if res.get('diagnostics'): print('DIAG', res['diagnostics'])
    if res.get('output'): print('OUT', res['output'])
    print(res.get('result'))
else: print(res)
if not d.get('success'): print('ERR', d.get('errors'))
" ;;
  evalf)
    $U eval_file $F --timeout ${3:-600000} --file "$(cygpath -w "$2" 2>/dev/null || echo "$2")" --format json 2>&1 | python -c "
import sys,json
raw=sys.stdin.read()
try:
    d=json.loads(raw)
except Exception:
    print(raw[-3000:]); sys.exit(1)
data=d.get('data') or {}
res=data.get('result', data)
if isinstance(res,str):
    try: res=json.loads(res)
    except Exception: pass
if isinstance(res,dict):
    if res.get('diagnostics'): print('DIAG', res['diagnostics'])
    if res.get('output'): print('OUT', res['output'])
    print(res.get('result'))
else: print(res)
if not d.get('success'): print('ERR', d.get('errors'))
" ;;
esac
