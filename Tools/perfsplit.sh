#!/usr/bin/env bash
# Where the frame goes, on the dev player at the real desktop resolution (practice match, 6 bots).
# usage: perfsplit.sh <preset> <seconds> "label|gfxspec" ...   (gfxspec: see Bootstrap -salvo-gfx)
# prints per variant: fps, CPU main / render thread, GPU, and the main-thread systems + draw counts
cd "$(dirname "$0")/../Builds"
PRESET=$1; SECS=$2; shift 2
for v in "$@"; do
  label=${v%%|*}; spec=${v#*|}
  log="split_${label}.log"
  ./Windows-Dev/SALVO.exe -salvo-practice -salvo-map ${MAP:-Outpost} -salvo-preset "$PRESET" -salvo-gfx "${spec:-none=0}" -logFile "$log" &
  sleep "$SECS"; taskkill //F //IM SALVO.exe >/dev/null 2>&1; sleep 3
  echo "== $label ($spec)"
  # skip the first two samples (loading + warm-up), average the rest field by field
  rtk proxy grep -h "SALVO/Perf" "$log" | tail -n +3 | awk '{f+=$3; n++} END { if (n) printf "   fps %.1f\n", f/n }'
  rtk proxy grep -h "SALVO/Split" "$log" | tail -n +3 | python -c "
import sys, re
rows = [l for l in sys.stdin if 'Split' in l]
if not rows: print('   no data'); sys.exit()
keys = re.findall(r'([A-Za-z]+) ([0-9.]+|n/a)', rows[0].split('Split]')[1])
acc = {}
for r in rows:
    for k, v in re.findall(r'([A-Za-z]+) ([0-9.]+|n/a)', r.split('Split]')[1]):
        if v != 'n/a': acc.setdefault(k, []).append(float(v))
print('   ' + '  '.join(f'{k} {sum(v)/len(v):.2f}' if sum(v)/len(v) < 1000 else f'{k} {sum(v)/len(v):.0f}' for k, v in acc.items()))
res = re.search(r'res (\S+)', rows[-1]); print('   res', res.group(1) if res else '?')
"
done
