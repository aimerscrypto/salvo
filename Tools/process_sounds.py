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


def build():
    W = "Weapons/"
    # gunshots: single events, keep the natural tail
    for name, out, length in (("viper_fire", "Viper_Fire", 1.2), ("hornet_fire", "Hornet_Fire", 0.8), ("ranger_fire", "Ranger_Fire", 1.3),
                              ("bulldog_fire", "Bulldog_Fire", 1.5), ("longbow_fire", "Longbow_Fire", 1.9)):
        x = load(name)
        starts, _ = onsets(x, -20)
        save(W + out, level(tidy(cut(x, starts[0], length, 0.002), 0.25, -55), 0.95))
    save(W + "Empty_Click", events("dry_fire", 1, 0.25, rms=0.1)[0])
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
