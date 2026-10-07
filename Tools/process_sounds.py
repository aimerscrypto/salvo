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

# mix targets (active loudness, dB full scale, before the game's own volume): bigger guns are
# louder, and a reload sits ~9 dB under a rifle shot so it reads clearly between bursts
SHOT_DB = {"Viper": -7.5, "Hornet": -7.5, "Ranger": -7.0, "Bulldog": -8.0, "Longbow": -8.0}
RELOAD_DB = -14.0


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


def gunshot(name, close_len, sub_hz, sub_gain, drive, seed, at=0.0, natural_tail=0.0):
    """
    Close-up shot + separate outdoor and indoor tails. The close layer is short so automatic fire
    stays crisp; the tail is restarted on every shot by the game, so a burst rings out once at the end.
    at: use the shot starting after this time (recordings with several shots).
    natural_tail: seconds of the recording's own outdoor echo to use as the outdoor tail instead
    of a synthetic one (field recordings made outdoors).
    """
    x = load(name)
    s, _ = onsets(x, -20)
    s = [i for i in s if i >= int((at - 0.05) * SR)] if at else s
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
    if natural_tail:
        a = s[0] + int((close_len - 0.08) * SR)
        tail = x[a:a + int(natural_tail * SR)].copy()
        tail -= np.mean(tail)
        fi = int(0.08 * SR); tail[:fi] *= np.linspace(0, 1, fi)
        fo = int(0.4 * SR); tail[-fo:] *= np.linspace(1, 0, fo) ** 2
        tails["out"] = level(tail, 0.9)
    return close, tails["out"], tails["in"]


def compress(x, thresh_db=-30, ratio=5.0, smooth_ms=12):
    """
    Downward compressor: loud clicks are pulled toward the quiet handling noise between them, so
    a reload reads clearly at the same peak level instead of being a few spikes over near-silence.
    """
    env = envelope(x, 3)
    peak = env.max() or 1
    db = 20 * np.log10(np.maximum(env / peak, 1e-6))
    gr = np.maximum(0, db - thresh_db) * (1 - 1 / ratio)
    k = max(1, int(SR * smooth_ms / 1000))
    gr = np.convolve(gr, np.ones(k) / k, mode="same")   # smooth the gain so it doesn't crackle
    return x * 10 ** (-gr / 20)


def active_db(x):
    """Loudness while something is sounding: RMS of the 50 ms windows above -26 dB of the loudest."""
    n = int(0.05 * SR)
    r = np.array([np.sqrt(np.mean(x[i:i + n] ** 2)) for i in range(0, max(1, len(x) - n), n)])
    a = r[r > r.max() * 0.05]
    return 20 * np.log10(np.sqrt(np.mean(a ** 2)) + 1e-9)


def loudness(x, target_db, ceiling=0.95):
    """Bring a clip to a loudness target; peaks that would pass the ceiling are soft-limited."""
    x = x * 10 ** ((target_db - active_db(x)) / 20)
    return ceiling * np.tanh(x / ceiling)


def whole_raw(name, a, b):
    """[a, b] of a recording, DC removed, short fades; no leveling."""
    x = load(name)
    seg = x[int(a * SR):int(b * SR)].copy()
    seg -= np.mean(seg)
    fi, fo = int(0.003 * SR), int(0.04 * SR)
    seg[:fi] *= np.linspace(0, 1, fi); seg[-fo:] *= np.linspace(1, 0, fo)
    return seg


def resample(x, rate):
    """Play faster (rate > 1, higher) or slower (rate < 1, lower), like tape."""
    return signal.resample(x, int(len(x) / rate))


def whole(name, a, b):
    """A recording as one piece (no slicing), trimmed to [a, b], de-clicked and leveled."""
    x = load(name)
    seg = x[int(a * SR):int(b * SR)].copy()
    seg -= np.mean(seg)
    fi, fo = int(0.004 * SR), int(0.06 * SR)
    seg[:fi] *= np.linspace(0, 1, fi); seg[-fo:] *= np.linspace(1, 0, fo)
    return loudness(compress(seg), RELOAD_DB)


def chain(parts, xfade=0.03):
    """Pieces of one or more recordings, (source, start, end), joined with equal-power crossfades."""
    n = int(xfade * SR)
    out = None
    for src, a, b in parts:
        x = load(src)
        seg = x[int(a * SR):int(b * SR)].copy()
        seg -= np.mean(seg)
        if out is None:
            out = seg
            continue
        t = np.linspace(0, np.pi / 2, n)
        out[-n:] = out[-n:] * np.cos(t) + seg[:n] * np.sin(t)
        out = np.concatenate([out, seg[n:]])
    fo = int(0.06 * SR); out[-fo:] *= np.linspace(1, 0, fo)
    return loudness(out, RELOAD_DB)


def tighten(name, keep, xfade=0.03):
    """One recording with the silent pauses between its parts shortened (equal-power crossfades)."""
    x = load(name)
    n = int(xfade * SR)
    out = x[int(keep[0][0] * SR):int(keep[0][1] * SR)].copy()
    for a, b in keep[1:]:
        seg = x[int(a * SR):int(b * SR)].copy()
        t = np.linspace(0, np.pi / 2, n)
        out[-n:] = out[-n:] * np.cos(t) + seg[:n] * np.sin(t)
        out = np.concatenate([out, seg[n:]])
    out -= np.mean(out)
    fo = int(0.06 * SR); out[-fo:] *= np.linspace(1, 0, fo)
    return loudness(compress(out), RELOAD_DB)


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
    for i, (name, out, close_len, sub_hz, sub_gain, drive, at, natural_tail) in enumerate((
            ("viper_fire", "Viper", 0.42, 70, 0.35, 1.8, 0.0, 0.0),
            ("hornet_fire", "Hornet", 0.26, 75, 0.3, 1.9, 0.0, 0.0),
            ("ranger_fire", "Ranger", 0.34, 62, 0.4, 2.0, 0.0, 0.0),
            ("shotgun_12ga", "Bulldog", 0.45, 52, 0.45, 1.2, 0.78, 0.0),     # 12-gauge, second of four shots
            ("rifle_308", "Longbow", 0.6, 48, 0.4, 1.2, 8.5, 0.0))):        # .308 bolt rifle (its own echo is under wind noise)
        close, tail_out, tail_in = gunshot(name, close_len, sub_hz, sub_gain, drive, 11 + i, at, natural_tail)
        save(W + out + "_Fire", loudness(close, SHOT_DB[out], 0.98))
        save(W + out + "_TailOut", tail_out)
        save(W + out + "_TailIn", tail_in)
    save(W + "Empty_Click", events("dry_fire", 1, 0.25, rms=0.1)[0])
    # full reloads: one continuous recording per gun. The game's reload time and the animation's
    # mag-out / mag-in / charging-handle moments are set to this recording (SalvoContentBuilder).
    save(W + "Ranger_Reload", whole("rifle_reload_game", 0.0, 1.87))      # out 0.03, seated 0.84, charge 1.68
    save(W + "Viper_Reload", whole("pistol_reload_full", 0.0, 1.5))       # out 0.02, in 0.62, slide 1.28
    # bolt rifle: work the bolt open, swap the magazine, run the bolt home
    save(W + "Longbow_Reload", chain([("bolt_cycle", 5.70, 6.25),            # lift + back (0.07, 0.35)
                                      ("rifle_reload_game", 0.0, 1.0),       # mag out 0.55, seated 1.36
                                      ("bolt_cycle", 6.30, 6.95)]))          # forward + down (1.60, 1.99)
    # the Uzi take has two long pauses (hand to pouch, hand to bolt): shorten the pauses only
    save(W + "Hornet_Reload", tighten("smg_reload", [(0.0, 0.6), (1.45, 2.3), (2.9, 3.75)]))  # out 0.05, in 1.1, charge 1.61
    # one shell pushed into the tube (shell against the lifter, then the click past the latch)
    save(W + "Shell_Load", loudness(whole_raw("shell_insert_a", 0.55, 1.12), RELOAD_DB + 1))
    # one pump stroke: back (0.11) and forward (0.36) in time with the view-model pump
    save(W + "Pump_Action", loudness(whole_raw("pump_cycle", 3.30, 3.98), RELOAD_DB + 1))
    # one bolt cycle after a shot: lift, back, forward, down (~1.1 s)
    save(W + "Bolt_Action", loudness(whole_raw("bolt_cycle", 5.70, 6.95), RELOAD_DB + 1))
    # body foley
    P = "Player/"
    save(P + "Land", loudness(whole_raw("jump_gravel", 0.27, 0.95), -14))
    save(P + "Land_Light", loudness(whole_raw("land", 0.08, 0.55), -16))
    save(P + "Slide", loudness(whole_raw("slide", 0.0, 1.23), -18))
    save(P + "Hurt", loudness(whole_raw("grunt", 0.0, 0.32), -14))
    save(P + "Death", loudness(resample(whole_raw("grunt", 0.0, 0.32), 0.82), -13))
    save(W + "Knife_Swing", loudness(whole_raw("knife_whoosh", 0.42, 0.74), -16))
    save(W + "Knife_Hit", loudness(whole_raw("knife_stab", 0.26, 0.47), -13))
    for k, r in enumerate((0.9, 1.0, 1.12)):
        save(f"Surfaces/Impact_Wood_{k}", loudness(resample(whole_raw("wood_hit", 0.16, 0.38), r), -15))
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
