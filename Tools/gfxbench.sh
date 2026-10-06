#!/usr/bin/env bash
# GPU cost experiments on the dev player: one practice run per "-salvo-gfx" variant, prints the
# average frame/GPU time after warm-up. usage: gfxbench.sh <preset> <seconds> "label|spec" ...
cd "$(dirname "$0")/../Builds"
PRESET=$1; SECS=$2; shift 2
for v in "$@"; do
  label=${v%%|*}; spec=${v#*|}
  log="bench_${label}.log"
  ./Windows-Dev/SALVO.exe -salvo-practice -salvo-map ${MAP:-Outpost} -salvo-preset "$PRESET" -salvo-gfx "${spec:-none=0}" -logFile "$log" &
  sleep "$SECS"; taskkill //F //IM SALVO.exe >/dev/null 2>&1; sleep 3
  # skip the first two budget lines (loading + warm-up)
  rtk proxy grep -h "SALVO/Budget" "$log" | tail -n +3 | awk -v l="$label" '{f+=$3; g+=$(NF-1); n++} END { if (n) printf "%-14s frame %6.2f ms (%5.1f fps)  GPU %6.2f ms  [%d samples]\n", l, f/n, 1000/(f/n), g/n, n; else print l, "no data" }'
done
