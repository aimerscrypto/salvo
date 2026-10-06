"""
Turns the CC0 recordings in Tools/SoundSources (see CREDITS.md there) into game-ready clips:
decode -> mono 44.1 kHz -> slice multi-event recordings at their onsets -> trim, de-click,
loudness-match -> Assets/_Salvo/Audio/... with the names the game loads.

usage: python Tools/process_sounds.py            build everything
       python Tools/process_sounds.py --analyze  print the detected events per source
"""
import os
import subprocess
import sys
import numpy as np
from scipy import signal
from scipy.io import wavfile

SR = 44100
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
SRC = os.path.join(ROOT, "Tools", "SoundSources")
OUT = os.path.join(ROOT, "Assets", "_Salvo", "Audio")


def load(name):
    raw = subprocess.run(["ffmpeg", "-v", "quiet", "-i", os.path.join(SRC, name + ".mp3"), "-ac", "1", "-ar", str(SR), "-f", "f32le", "-"],
                         capture_output=True, check=True).stdout
    return np.frombuffer(raw, dtype=np.float32).astype(np.float64)


def envelope(x, ms=5):
    n = max(1, int(SR * ms / 1000))
    return np.sqrt(np.convolve(x * x, np.ones(n) / n, mode="same"))


def onsets(x, thresh_db=-28, min_gap=0.18, hp=150):
    """Start times of distinct events: envelope rises above threshold relative to the peak."""
    b, a = signal.butter(2, hp / (SR / 2), "high")
    env = envelope(signal.lfilter(b, a, x))
    peak = env.max() or 1
    on = env > peak * 10 ** (thresh_db / 20)
    starts, last = [], -1e9
    for i in np.flatnonzero(on[1:] & ~on[:-1]) + 1:
        if (i - last) / SR >= min_gap:
            starts.append(i)
            last = i
    return starts, env


def cut(x, start, length, pre=0.004):
    s = max(0, start - int(pre * SR))
    return x[s:s + int(length * SR)].copy()


def tidy(x, fade_out=0.04, tail_db=-50):
    """Trim trailing silence, short fade in/out, remove DC."""
    x = x - np.mean(x)
    env = envelope(x, 10)
    peak = env.max() or 1
    alive = np.flatnonzero(env > peak * 10 ** (tail_db / 20))
    if len(alive):
        x = x[:min(len(x), alive[-1] + int(0.02 * SR))]
    fi = min(len(x), int(0.002 * SR))
    fo = min(len(x), int(fade_out * SR))
    x[:fi] *= np.linspace(0, 1, fi)
    x[-fo:] *= np.linspace(1, 0, fo) ** 2
    return x


def level(x, peak=0.9, rms_target=None):
    if rms_target:
        r = np.sqrt(np.mean(x ** 2)) or 1
        x = x * (rms_target / r)
    m = np.max(np.abs(x)) or 1
    if m > peak or not rms_target:
        x = x * (peak / m)   # limit (rms matched) or normalize (peak only)
    return x


def save(path, x):
    full = os.path.join(OUT, path + ".wav")
    os.makedirs(os.path.dirname(full), exist_ok=True)
    wavfile.write(full, SR, (np.clip(x, -1, 1) * 32767).astype(np.int16))


def events(name, count, length, thresh_db=-28, min_gap=0.25, skip=0, rms=0.12, tail_db=-45):
    """The `count` strongest separated events of a recording, each `length` seconds max."""
    x = load(name)
    starts, env = onsets(x, thresh_db, min_gap)
    # rank events by their peak, keep the strongest ones (in time order)
    ranked = sorted(starts[skip:], key=lambda s: -env[s:s + int(0.08 * SR)].max())[:count]
    return [level(tidy(cut(x, s, length), tail_db=tail_db), 0.9, rms) for s in sorted(ranked)]


def analyze():
    for f in sorted(os.listdir(SRC)):
        if not f.endswith(".mp3"):
            continue
        name = f[:-4]
        x = load(name)
        starts, env = onsets(x)
        print(f"{name:14s} {len(x) / SR:6.2f}s  events at: " + " ".join(f"{s / SR:.2f}" for s in starts[:24]))


RNG = np.random.default_rng(7)


def bandpass(x, lo, hi, order=2):
    if lo:
        b, a = signal.butter(order, lo / (SR / 2), "high")
        x = signal.lfilter(b, a, x)
    if hi:
        b, a = signal.butter(order, hi / (SR / 2), "low")
        x = signal.lfilter(b, a, x)
    return x


def thump(freq0, freq1, length, decay):
    """Low body of a muzzle blast: a fast downward sine sweep with an exponential decay."""
    t = np.arange(int(length * SR)) / SR
    f = freq1 + (freq0 - freq1) * np.exp(-t / 0.025)
    ph = 2 * np.pi * np.cumsum(f) / SR
    return np.sin(ph) * np.exp(-t / decay) * np.minimum(1, t / 0.0015)


def saturate(x, drive):
    """Soft clipping: denser, louder shot without hard digital clipping."""
    return np.tanh(x * drive) / np.tanh(drive)


def room_ir(kind, seed):
    """
    Synthetic impulse response of where the shot is fired.
    outdoor: sparse slap-back echoes off buildings (40-260 ms) and a long dark diffuse decay;
    indoor: dense early reflections and a short bright decay.
    """
    rng = np.random.default_rng(seed)
    if kind == "out":
        n = int(2.2 * SR); t = np.arange(n) / SR
        ir = rng.standard_normal(n) * np.exp(-t / 0.42) * np.minimum(1, t / 0.06) * 0.18
        ir = bandpass(ir, 160, 2200)
        for _ in range(7):                         # discrete slaps off walls
            d = rng.uniform(0.04, 0.26); g = rng.uniform(0.25, 0.55) * np.exp(-d / 0.3)
            i = int(d * SR)
            ir[i:i + 64] += g * np.hanning(64) * rng.choice([-1, 1])
    else:
        n = int(0.9 * SR); t = np.arange(n) / SR
        ir = rng.standard_normal(n) * np.exp(-t / 0.13) * np.minimum(1, t / 0.004) * 0.35
        ir = bandpass(ir, 120, 6000)
        for _ in range(18):
            d = rng.uniform(0.003, 0.035); i = int(d * SR)
            ir[i:i + 16] += rng.uniform(0.2, 0.6) * np.hanning(16) * rng.choice([-1, 1])
    return ir


def gunshot(name, close_len, sub_hz, sub_gain, drive, seed):
    """
    Close-up shot + separate outdoor and indoor tails. The close layer is short so automatic fire
    stays crisp; the tail is restarted on every shot by the game, so a burst rings out once at the end.
    """
    x = load(name)
    s, _ = onsets(x, -20)
    dry = cut(x, s[0], close_len + 0.6, 0.002)
    dry = dry - np.mean(dry)
    # transient: emphasize the first 25 ms (the crack) a little
    n = len(dry); t = np.arange(n) / SR
    dry = dry * (1 + 0.6 * np.exp(-t / 0.02))
    body = thump(sub_hz * 2.2, sub_hz, close_len + 0.6, 0.07) * sub_gain * np.max(np.abs(dry))
    close = saturate(dry / np.max(np.abs(dry)) + body / np.max(np.abs(dry)), drive)
    env = np.ones(n); fo = int(close_len * SR)
    env[fo:] = 0; ramp = int(0.08 * SR)
    env[fo - ramp:fo] = np.linspace(1, 0, ramp) ** 2
    close = level(tidy((close * env)[:fo], 0.02, -60), 0.98)
    tails = {}
    for kind in ("out", "in"):
        wet = signal.fftconvolve(dry[:int(0.25 * SR)], room_ir(kind, seed + (0 if kind == "out" else 50)))
        wet = wet[:int((2.0 if kind == "out" else 0.85) * SR)]
        tails[kind] = level(tidy(wet, 0.3, -60), 0.9)
    return close, tails["out"], tails["in"]


def sequence(length, parts):
    """Lay recorded segments (source, start, end, at, gain) on a timeline: one reload, start to finish."""
    out = np.zeros(int(length * SR))
    cache = {}
    for src, a, b, at, gain in parts:
        if src not in cache:
            cache[src] = load(src)
        seg = cache[src][int(a * SR):int(b * SR)].copy()
        seg = seg - np.mean(seg)
        fi, fo = int(0.006 * SR), int(0.04 * SR)
        seg[:fi] *= np.linspace(0, 1, fi); seg[-fo:] *= np.linspace(1, 0, fo)
        i = int(at * SR)
        seg = seg[:max(0, len(out) - i)]
        out[i:i + len(seg)] += seg * gain
    return level(tidy(out, 0.05, -55), 0.9)


def takes(name, count, length, gap=0.6, thresh=-30):
    """Separate takes of a repeated action (e.g. a casing dropped several times): onsets after silence."""
    x = load(name)
    st, env = onsets(x, thresh, 0.1)
    starts = [s for k, s in enumerate(st) if k == 0 or (s - st[k - 1]) / SR > gap]
    ranked = sorted(starts, key=lambda s: -env[s:s + int(0.05 * SR)].max())[:count]
    return [level(tidy(cut(x, s, length), 0.08, -50), 0.9, 0.08) for s in sorted(ranked)]


def crack(seed):
    """Supersonic bullet passing close: an N-wave snap with a short bright slap and air tail."""
    rng = np.random.default_rng(seed)
    n = int(0.35 * SR); out = np.zeros(n)
    w = int(rng.uniform(0.00035, 0.0006) * SR)
    nwave = np.linspace(1, -1, w)
    i0 = int(0.002 * SR)
    out[i0:i0 + w] += nwave
    t = np.arange(n) / SR
    out += bandpass(rng.standard_normal(n), 2500, 9000) * np.exp(-t / 0.012) * 0.25
    out += bandpass(rng.standard_normal(n), 400, 3000) * np.exp(-t / 0.09) * np.minimum(1, t / 0.01) * 0.06
    for _ in range(4):
        d = int(rng.uniform(0.008, 0.05) * SR)
        out[i0 + d:i0 + d + w] += nwave * rng.uniform(0.15, 0.35)
    return level(tidy(out, 0.05, -60), 0.95)


def build():
    W = "Weapons/"
    # gunshots: punchy close layer + outdoor / indoor tails (fired in the open vs under a roof)
    for i, (name, out, close_len, sub_hz, sub_gain, drive) in enumerate((
            ("viper_fire", "Viper", 0.42, 70, 0.35, 1.8),
            ("hornet_fire", "Hornet", 0.26, 75, 0.3, 1.9),
            ("ranger_fire", "Ranger", 0.34, 62, 0.4, 2.0),
            ("bulldog_fire", "Bulldog", 0.6, 48, 0.55, 2.0),
            ("longbow_fire", "Longbow", 0.75, 42, 0.6, 2.2))):
        close, tail_out, tail_in = gunshot(name, close_len, sub_hz, sub_gain, drive, 11 + i)
        save(W + out + "_Fire", close)
        save(W + out + "_TailOut", tail_out)
        save(W + out + "_TailIn", tail_in)
    save(W + "Empty_Click", events("dry_fire", 1, 0.25, rms=0.1)[0])
    # full reloads, laid out to match each weapon's reload animation (mag out ~15-35%, seated ~72%)
    save(W + "Viper_Reload", sequence(1.45, [
        ("pistol_reload_full", 0.0, 0.42, 0.14, 1.0),       # mag release + mag out
        ("pistol_reload_full", 0.52, 0.98, 0.62, 1.0),      # new mag in
        ("pistol_reload_full", 1.05, 1.6, 0.92, 1.0)]))     # slide released
    save(W + "Hornet_Reload", sequence(1.85, [
        ("smg_reload", 0.0, 0.62, 0.22, 1.0),
        ("smg_reload", 1.55, 2.32, 0.78, 1.0),
        ("smg_reload", 3.0, 3.75, 1.2, 0.9)]))
    save(W + "Ranger_Reload", sequence(2.15, [
        ("rifle_reload", 0.12, 0.48, 0.02, 0.6),            # grip shift
        ("rifle_reload", 0.5, 0.98, 0.3, 1.0),              # mag release + out
        ("rifle_reload", 2.08, 2.66, 1.0, 1.0),             # new mag in
        ("rifle_reload", 3.08, 3.42, 1.44, 1.0),            # seat
        ("rifle_reload", 3.62, 3.98, 1.7, 0.9)]))           # bolt release
    save(W + "Longbow_Reload", sequence(2.7, [
        ("rifle_reload", 0.5, 0.98, 0.38, 1.0),
        ("rifle_reload", 2.08, 2.66, 1.3, 1.0),
        ("rifle_reload", 3.08, 3.42, 1.86, 1.0),
        ("rifle_reload", 6.45, 6.9, 2.2, 0.6)]))            # shouldered
    # one shell pushed into the tube
    x = load("shell_load")
    st, env = onsets(x, -24, 0.2)
    best = max(st, key=lambda s: env[s:s + int(0.05 * SR)].max())
    save(W + "Shell_Load", level(tidy(cut(x, best, 0.42), 0.06), 0.9, 0.13))
    # draw / handling rattle
    x = load("handling")
    st, env = onsets(x, -24, 0.4)
    best = sorted(st, key=lambda s: -env[s:s + int(0.05 * SR)].max())[:3]
    for k, s in enumerate(sorted(best)):
        save(W + f"Equip_{k}", level(tidy(cut(x, s, 0.45, 0.03), 0.08), 0.9, 0.09))
    # spent brass hitting the ground
    for k, e in enumerate(takes("casings_concrete", 4, 0.7) + takes("casings_tile", 4, 0.7)):
        save(f"Surfaces/Casing_{k}", e)
    # supersonic cracks for near misses
    for k in range(3):
        save(f"Feedback/Crack_{k}", crack(100 + k))
    # foley
    # AR15 handling: the first event is the magazine coming out, the strongest later one seating it
    x = load("ar15_mag")
    s, env = onsets(x, -24, 0.35)
    later = [i for i in s if i > 0.9 * SR]
    seat = max(later, key=lambda i: env[i:i + int(0.08 * SR)].max()) if later else s[-1]
    save(W + "Mag_Out", level(tidy(cut(x, s[0], 0.55)), 0.9, 0.1))
    save(W + "Mag_In", level(tidy(cut(x, seat, 0.55)), 0.9, 0.12))
    x = load("pistol_reload")
    save(W + "Slide_Rack", level(tidy(x, 0.05), 0.9, 0.12))
    # pump: one full back-and-forward cycle (two clacks ~0.4 s apart)
    x = load("pump")
    s, _ = onsets(x, -24, 0.3)
    save(W + "Pump_Action", level(tidy(cut(x, s[0], 0.85), 0.06), 0.9, 0.13))
    # bolt: lift, back, forward, down (~1.4 s)
    x = load("bolt")
    s, _ = onsets(x, -26, 0.15)
    save(W + "Bolt_Action", level(tidy(cut(x, s[0], 1.5), 0.08), 0.9, 0.12))
    # distant gunfire layer: the first report with its rolling outdoor tail, highs removed
    x = load("distant")
    s, _ = onsets(x, -18, 1.0)
    d = cut(x, s[0], 2.2)
    b, a = signal.butter(2, 900 / (SR / 2), "low")
    save("Surfaces/Distant_Shot", level(tidy(signal.lfilter(b, a, d), 0.6, -55), 0.9))
    # footsteps: 6 single steps per surface
    for src, surf, gap in (("steps_sand", "Sand", 0.3), ("steps_stone", "Stone", 0.3), ("steps_wood", "Wood", 0.3), ("steps_metal", "Metal", 0.3)):
        for i, e in enumerate(events(src, 6, 0.38, thresh_db=-22, min_gap=gap, rms=0.11)):
            save(f"Surfaces/Step_{surf}_{i}", e)
    # bullet impacts
    for src, surf in (("impact_sand", "Sand"), ("impact_stone", "Stone"), ("impact_metal", "Metal")):
        for i, e in enumerate(events(src, 3, 0.7, thresh_db=-22, min_gap=0.25, rms=0.12)):
            save(f"Surfaces/Impact_{surf}_{i}", e)
    print("sounds built ->", OUT)


if __name__ == "__main__":
    analyze() if "--analyze" in sys.argv else build()
