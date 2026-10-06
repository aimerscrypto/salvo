"""
Procedural sound design for SALVO (numpy + scipy). Every clip is synthesized, so the project
ships with zero third-party audio. Run: python Tools/gen_audio.py
"""
import os
import numpy as np
from scipy import signal
from scipy.io import wavfile

SR = 44100
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
OUT = os.path.join(ROOT, "Assets", "_Salvo", "Audio")
rng = np.random.default_rng(11)


# ----------------------------------------------------------------------------- helpers
def t(sec):
    return np.arange(int(SR * sec)) / SR


def noise(sec):
    return rng.standard_normal(int(SR * sec))


def env_exp(sec, decay, attack=0.001):
    x = t(sec)
    a = np.clip(x / max(attack, 1e-5), 0, 1)
    return a * np.exp(-x / decay)


def bp(x, lo, hi, order=2):
    sos = signal.butter(order, [lo, min(hi, SR / 2 - 100)], btype="band", fs=SR, output="sos")
    return signal.sosfilt(sos, x)


def lp(x, f, order=2):
    sos = signal.butter(order, min(f, SR / 2 - 100), btype="low", fs=SR, output="sos")
    return signal.sosfilt(sos, x)


def hp(x, f, order=2):
    sos = signal.butter(order, f, btype="high", fs=SR, output="sos")
    return signal.sosfilt(sos, x)


def sweep(f0, f1, sec, curve=2.0):
    x = t(sec)
    k = (x / sec) ** (1 / curve)
    f = f0 + (f1 - f0) * k
    return np.sin(2 * np.pi * np.cumsum(f) / SR)


def mix(*parts):
    n = max(len(p) for p in parts)
    out = np.zeros(n)
    for p in parts:
        out[:len(p)] += p
    return out


def at(x, offset_sec, total=None):
    pad = np.zeros(int(SR * offset_sec))
    y = np.concatenate([pad, x])
    if total:
        y = np.pad(y, (0, max(0, int(SR * total) - len(y))))
    return y


def echo(x, delay, gain, n=3, lowpass=3000):
    out = np.copy(x)
    d = int(SR * delay)
    tap = x
    for i in range(1, n + 1):
        tap = lp(tap, lowpass) * gain
        out = mix(out, at(tap, delay * i))
    return out


def reverb(x, size=0.6, wet=0.25):
    """Tiny Schroeder reverb (4 combs + 2 allpasses)."""
    out = np.zeros(len(x) + int(SR * size * 1.5))
    xp = np.pad(x, (0, len(out) - len(x)))
    acc = np.zeros_like(out)
    for dly, g in ((0.0297, 0.77), (0.0371, 0.74), (0.0411, 0.72), (0.0437, 0.7)):
        d = int(SR * dly * (0.6 + size))
        a = np.zeros(d + 1); a[0] = 1; a[d] = -g
        acc += signal.lfilter([1.0], a, xp)
    acc /= 4
    for dly, g in ((0.005, 0.7), (0.0017, 0.7)):
        d = int(SR * dly)
        b = np.zeros(d + 1); b[0] = -g; b[d] = 1
        a = np.zeros(d + 1); a[0] = 1; a[d] = -g
        acc = signal.lfilter(b, a, acc)
    return xp * (1 - wet) + lp(acc, 6000) * wet


def fade(x, fin=0.002, fout=0.02):
    x = np.copy(x)
    a, b = int(SR * fin), int(SR * fout)
    if a > 0: x[:a] *= np.linspace(0, 1, a)
    if b > 0: x[-b:] *= np.linspace(1, 0, b)
    return x


def save(name, x, peak=0.9, sub=""):
    x = np.tanh(x * 1.1)  # gentle saturation
    m = np.max(np.abs(x)) or 1
    x = fade(x / m * peak)
    folder = os.path.join(OUT, sub) if sub else OUT
    os.makedirs(folder, exist_ok=True)
    wavfile.write(os.path.join(folder, name + ".wav"), SR, (x * 32767).astype(np.int16))


def save_loop(name, x, sub, xfade=1.0):
    """Seamless loop: crossfade the tail into the head."""
    n = int(SR * xfade)
    head, tail = x[:n], x[-n:]
    w = np.linspace(0, 1, n)
    body = x[n:-n] if len(x) > 2 * n else x
    looped = np.concatenate([tail * (1 - w) + head * w, body])
    m = np.max(np.abs(looped)) or 1
    looped = looped / m * 0.85
    folder = os.path.join(OUT, sub)
    os.makedirs(folder, exist_ok=True)
    wavfile.write(os.path.join(folder, name + ".wav"), SR, (looped * 32767).astype(np.int16))


def bell(freq, sec, decay=0.4, partials=((1, 1), (2.76, 0.4), (5.4, 0.2), (8.9, 0.1))):
    x = t(sec)
    y = np.zeros_like(x)
    for mult, amp in partials:
        y += amp * np.sin(2 * np.pi * freq * mult * x) * np.exp(-x / (decay / (1 + mult * 0.3)))
    return y * np.clip(x / 0.003, 0, 1)


def metal_click(freq=3200, sec=0.06, decay=0.012):
    x = t(sec)
    y = sum(a * np.sin(2 * np.pi * freq * m * x) for m, a in ((1, 1), (1.47, 0.6), (2.13, 0.4), (3.07, 0.25)))
    return (y * np.exp(-x / decay) + bp(noise(sec), 2000, 9000) * env_exp(sec, decay * 0.6) * 0.8)


# ----------------------------------------------------------------------------- weapons
def gunshot(body_lp, body_decay, thump_f, thump_amp, tail_sec, tail_amp, crack=1.0, length=1.2, echo_d=0.11, echo_g=0.35):
    transient = hp(noise(0.006), 3000) * env_exp(0.006, 0.0015) * 1.6 * crack
    body = lp(noise(length), body_lp) * env_exp(length, body_decay, 0.0005)
    body = mix(body, bp(noise(length), 900, 3500) * env_exp(length, body_decay * 0.6) * 0.6)
    thump = sweep(thump_f * 2.2, thump_f, 0.25) * env_exp(0.25, 0.05, 0.001) * thump_amp
    tail = lp(noise(tail_sec), 1400) * env_exp(tail_sec, tail_sec * 0.35, 0.02) * tail_amp
    y = mix(transient, body, thump, at(tail, 0.02))
    return echo(y, echo_d, echo_g, n=2, lowpass=1800)


def weapons():
    save("Viper_Fire", gunshot(5200, 0.035, 70, 0.9, 0.5, 0.18, crack=1.2), sub="Weapons")
    save("Hornet_Fire", gunshot(6000, 0.026, 85, 0.7, 0.35, 0.12, crack=1.0, length=0.8), sub="Weapons")
    save("Ranger_Fire", gunshot(4200, 0.05, 60, 1.2, 0.7, 0.25, crack=1.3), sub="Weapons")
    save("Bulldog_Fire", gunshot(2600, 0.11, 48, 1.8, 1.1, 0.4, crack=1.0, length=1.6, echo_d=0.14, echo_g=0.45), sub="Weapons")
    save("Longbow_Fire", gunshot(3600, 0.12, 42, 2.0, 1.6, 0.5, crack=1.8, length=2.0, echo_d=0.19, echo_g=0.5), sub="Weapons")

    # foley
    mag_out = mix(metal_click(2400, 0.08, 0.01), at(bp(noise(0.18), 1500, 6000) * env_exp(0.18, 0.05, 0.02) * 0.5, 0.03))
    save("Mag_Out", mag_out, 0.7, sub="Weapons")
    mag_in = mix(bp(noise(0.12), 800, 5000) * env_exp(0.12, 0.03, 0.02) * 0.5, at(metal_click(2800, 0.07, 0.012), 0.07), at(metal_click(1900, 0.09, 0.018) * 1.2, 0.11))
    save("Mag_In", mag_in, 0.8, sub="Weapons")
    slide = mix(metal_click(2200, 0.05, 0.008), at(bp(noise(0.1), 1200, 5000) * env_exp(0.1, 0.03) * 0.6, 0.02), at(metal_click(3100, 0.08, 0.014) * 1.3, 0.12))
    save("Slide_Rack", slide, 0.8, sub="Weapons")
    pump = mix(bp(noise(0.12), 500, 3000) * env_exp(0.12, 0.04, 0.01), metal_click(1400, 0.1, 0.02) * 0.9,
               at(bp(noise(0.12), 600, 3500) * env_exp(0.12, 0.035, 0.01), 0.17), at(metal_click(1700, 0.12, 0.025), 0.2))
    save("Pump_Action", pump, 0.85, sub="Weapons")
    bolt = mix(metal_click(2600, 0.06, 0.01), at(bp(noise(0.12), 900, 4000) * env_exp(0.12, 0.04) * 0.6, 0.06), at(metal_click(2000, 0.08, 0.015), 0.16), at(metal_click(3300, 0.06, 0.01), 0.28))
    save("Bolt_Action", bolt, 0.85, sub="Weapons")
    shell = mix(metal_click(2100, 0.06, 0.012), at(bp(noise(0.06), 600, 3000) * env_exp(0.06, 0.02) * 0.6, 0.03))
    save("Shell_Load", shell, 0.7, sub="Weapons")
    equip = mix(bp(noise(0.25), 400, 2500) * env_exp(0.25, 0.08, 0.04) * 0.6, at(metal_click(2500, 0.06, 0.01), 0.12))
    save("Equip", equip, 0.6, sub="Weapons")
    save("Empty_Click", metal_click(3500, 0.05, 0.006), 0.6, sub="Weapons")
    whoosh = bp(noise(0.35), 300, 3000) * np.sin(np.linspace(0, np.pi, int(SR * 0.35))) ** 2
    save("Knife_Swing", mix(whoosh, bp(noise(0.35), 2500, 7000) * np.sin(np.linspace(0, np.pi, int(SR * 0.35))) ** 4 * 0.4), 0.7, sub="Weapons")
    save("Knife_Hit", mix(lp(noise(0.2), 600) * env_exp(0.2, 0.04), hp(noise(0.1), 3000) * env_exp(0.1, 0.015) * 0.6), 0.85, sub="Weapons")


# ----------------------------------------------------------------------------- player / world
def player():
    for i in range(5):
        thud = sweep(110 + i * 7, 55, 0.12) * env_exp(0.12, 0.025)
        crunch = bp(noise(0.14), 900 + i * 150, 5200) * env_exp(0.14, 0.03 + i * 0.004, 0.004) * (0.55 + 0.1 * i)
        save(f"Step_{i}", mix(thud * 0.8, at(crunch, 0.004)), 0.6, sub="Player")
    cloth = bp(noise(0.3), 300, 2400) * np.sin(np.linspace(0, np.pi, int(SR * 0.3))) ** 2
    save("Jump", mix(cloth * 0.7, sweep(160, 90, 0.1) * env_exp(0.1, 0.03) * 0.4), 0.5, sub="Player")
    land = mix(sweep(120, 40, 0.25) * env_exp(0.25, 0.06), bp(noise(0.25), 700, 4000) * env_exp(0.25, 0.05) * 0.8)
    save("Land", land, 0.8, sub="Player")
    sl = bp(noise(0.7), 400, 3000) * env_exp(0.7, 0.3, 0.05)
    save("Slide", mix(sl, lp(noise(0.7), 300) * env_exp(0.7, 0.25, 0.05) * 0.8), 0.6, sub="Player")
    boing = sweep(180, 620, 0.45, 1.5) * env_exp(0.45, 0.2, 0.01) * (1 + 0.15 * np.sin(2 * np.pi * 18 * t(0.45)))
    save("JumpPad", mix(boing * 0.7, bp(noise(0.6), 500, 4000) * np.sin(np.linspace(0, np.pi, int(SR * 0.6))) ** 2 * 0.6), 0.8, sub="Player")
    save("Hurt", mix(sweep(140, 60, 0.18) * env_exp(0.18, 0.05), bp(noise(0.15), 400, 2000) * env_exp(0.15, 0.04) * 0.7), 0.75, sub="Player")
    poof = mix(lp(noise(0.7), 1200) * env_exp(0.7, 0.15, 0.005), sweep(240, 60, 0.2) * env_exp(0.2, 0.05) * 0.8, bell(520, 0.5, 0.3) * 0.15)
    save("Death", poof, 0.8, sub="Player")
    save("Impact", mix(lp(noise(0.2), 2500) * env_exp(0.2, 0.025), hp(noise(0.05), 3500) * env_exp(0.05, 0.005) * 0.8), 0.6, sub="Player")
    wz = bp(noise(0.25), 1800, 6000) * np.sin(np.linspace(0, np.pi, int(SR * 0.25))) ** 3
    save("Whiz", wz * np.linspace(1.0, 0.6, len(wz)), 0.5, sub="Player")
    # feedback
    save("Hitmarker", mix(metal_click(2600, 0.06, 0.01) * 0.6, np.sin(2 * np.pi * 1900 * t(0.05)) * env_exp(0.05, 0.012)), 0.6, sub="Feedback")
    save("Headshot", mix(bell(1650, 0.35, 0.18), metal_click(4200, 0.06, 0.01) * 0.5), 0.7, sub="Feedback")
    kill = mix(bell(988, 0.6, 0.35), at(bell(1480, 0.6, 0.35) * 0.8, 0.07), np.sin(2 * np.pi * 120 * t(0.12)) * env_exp(0.12, 0.03) * 0.4)
    save("Kill", kill, 0.75, sub="Feedback")
    pk = mix(bell(784, 0.4, 0.2), at(bell(988, 0.4, 0.2), 0.07), at(bell(1175, 0.6, 0.3), 0.14))
    save("Pickup_Health", pk, 0.6, sub="Feedback")
    save("Pickup_Ammo", mix(metal_click(1800, 0.1, 0.02), at(metal_click(2300, 0.1, 0.02), 0.08), at(metal_click(2000, 0.12, 0.03), 0.16)), 0.6, sub="Feedback")


# ----------------------------------------------------------------------------- UI & stingers
def ui():
    save("Hover", np.sin(2 * np.pi * 1700 * t(0.03)) * env_exp(0.03, 0.006), 0.25, sub="UI")
    save("Click", mix(sweep(900, 500, 0.06) * env_exp(0.06, 0.015), bp(noise(0.02), 2000, 6000) * env_exp(0.02, 0.003) * 0.4), 0.5, sub="UI")
    save("Back", sweep(600, 320, 0.08) * env_exp(0.08, 0.02), 0.45, sub="UI")
    save("Buy", mix(bell(1318, 0.5, 0.25), at(bell(1760, 0.6, 0.3), 0.06), at(metal_click(5000, 0.05, 0.008) * 0.3, 0.0)), 0.6, sub="UI")
    err = mix(signal.sawtooth(2 * np.pi * 140 * t(0.12)) * env_exp(0.12, 0.05), at(signal.sawtooth(2 * np.pi * 120 * t(0.14)) * env_exp(0.14, 0.06), 0.13))
    save("Error", lp(err, 1500), 0.4, sub="UI")
    rw = mix(*[at(bell(f, 0.6, 0.3), i * 0.08) for i, f in enumerate((523, 659, 784, 1047))])
    save("Reward", rw, 0.6, sub="UI")
    lv = mix(*[at(bell(f, 1.2, 0.6), i * 0.1) for i, f in enumerate((392, 523, 659, 784, 1047))],
             at(lp(signal.sawtooth(2 * np.pi * 262 * t(1.2)), 1800) * env_exp(1.2, 0.5, 0.05) * 0.2, 0.4))
    save("LevelUp", reverb(lv, 0.5, 0.25), 0.7, sub="UI")
    save("Countdown", np.sin(2 * np.pi * 880 * t(0.15)) * env_exp(0.15, 0.06), 0.5, sub="UI")

    def horn(freqs, sec):
        y = np.zeros(int(SR * sec))
        for f in freqs:
            y += lp(signal.sawtooth(2 * np.pi * f * t(sec)) * 0.5 + signal.square(2 * np.pi * f * 0.5 * t(sec)) * 0.2, 2200)
        e = np.clip(t(sec) / 0.05, 0, 1) * np.clip((sec - t(sec)) / 0.4, 0, 1)
        return y * e

    save("MatchStart", reverb(mix(horn((262, 330, 392), 0.35), at(horn((392, 494, 587), 0.9), 0.32)), 0.7, 0.3), 0.6, sub="UI")
    vic = mix(*[at(horn((f, f * 1.25, f * 1.5), 0.3 if i < 3 else 1.2), i * 0.18) for i, f in enumerate((262, 330, 392, 523))])
    save("Victory", reverb(vic, 0.8, 0.3), 0.6, sub="UI")
    dft = mix(*[at(horn((f, f * 1.19, f * 1.5), 0.4 if i < 2 else 1.3), i * 0.3) for i, f in enumerate((330, 294, 220))])
    save("Defeat", reverb(dft, 0.8, 0.3), 0.55, sub="UI")


# ----------------------------------------------------------------------------- music & ambience
def epiano(freq, sec, vel=1.0):
    x = t(sec)
    mod = np.sin(2 * np.pi * freq * 2 * x) * 1.2 * np.exp(-x / 0.4)
    y = np.sin(2 * np.pi * freq * x + mod) * np.exp(-x / 1.1)
    y += 0.25 * np.sin(2 * np.pi * freq * 3 * x) * np.exp(-x / 0.25)
    return y * vel * np.clip(x / 0.004, 0, 1)


def music():
    bpm = 88
    beat = 60 / bpm
    bars = 16
    total = bars * 4 * beat
    n = int(SR * total) + SR
    keys = np.zeros(n); bass = np.zeros(n); drums = np.zeros(n); lead = np.zeros(n)
    note = lambda m: 440.0 * 2 ** ((m - 69) / 12)
    prog = [(57, [57, 60, 64, 67]), (53, [53, 57, 60, 64]), (48, [55, 60, 64, 67]), (55, [55, 59, 62, 66])]  # Am7 Fmaj7 Cmaj7 Gmaj7
    def put(buf, sig, sec):
        i = int(sec * SR)
        e = min(n, i + len(sig))
        buf[i:e] += sig[:e - i]
    melody = [76, 74, 72, 71, 72, 74, 76, 79, 77, 76, 74, 72, 71, 72, 69, 67]
    for b in range(bars):
        root, chord = prog[b % 4]
        start = b * 4 * beat
        for k, m in enumerate(chord):  # gentle strum
            put(keys, epiano(note(m), beat * 2.2, 0.32), start + k * 0.018)
            put(keys, epiano(note(m), beat * 1.6, 0.22), start + 2.5 * beat + k * 0.012)
        bs = note(root - 12)
        for hit, ln in ((0, 1.4), (1.75, 0.4), (2.5, 1.2)):
            x = t(beat * ln)
            put(bass, np.sin(2 * np.pi * bs * x) * np.exp(-x / 0.6) * 0.7 * np.clip(x / 0.01, 0, 1), start + hit * beat)
        for q in range(4):  # drums
            put(drums, sweep(120, 45, 0.25) * env_exp(0.25, 0.08) * 0.9 if q in (0, 2) else np.zeros(1), start + q * beat)
            if q in (1, 3):
                put(drums, mix(bp(noise(0.2), 1500, 7000) * env_exp(0.2, 0.05) * 0.45, sweep(220, 160, 0.1) * env_exp(0.1, 0.03) * 0.3), start + q * beat)
            for e8 in (0, 0.5):
                put(drums, hp(noise(0.05), 7000) * env_exp(0.05, 0.012) * (0.18 if e8 == 0 else 0.1), start + (q + e8) * beat)
        if b >= 4 and b % 8 < 6:  # simple lead line in the middle section
            for k in range(2):
                m = melody[(b * 2 + k) % len(melody)]
                x = t(beat * 1.8)
                tone = (np.sin(2 * np.pi * note(m) * x) + 0.3 * signal.square(2 * np.pi * note(m) * x)) * np.exp(-x / 0.9) * np.clip(x / 0.03, 0, 1)
                put(lead, lp(tone, 2500) * 0.16, start + k * 2 * beat + 0.5 * beat)
    song = keys * 0.9 + bass + drums * 0.8 + reverb(lead, 0.7, 0.35)[:n]
    song = reverb(song, 0.5, 0.18)[:int(SR * total) + int(SR * 0.9)]
    # vinyl-ish warmth
    song = lp(song, 7500) + lp(noise(len(song) / SR), 3000) * 0.004
    save_loop("MenuTheme", song, "Music", xfade=0.9)


def ambience():
    sec = 30
    wind = lp(noise(sec), 500, order=2)
    lfo = 0.6 + 0.4 * np.sin(2 * np.pi * 0.07 * t(sec)) * np.sin(2 * np.pi * 0.031 * t(sec) + 1.3)
    gust = bp(noise(sec), 300, 1200) * (0.5 + 0.5 * np.sin(2 * np.pi * 0.05 * t(sec) + 0.5)) ** 3 * 0.35
    amb = wind * lfo + gust
    for k in range(5):  # distant birds
        c = bell(2600 + rng.uniform(-300, 300), 0.18, 0.06, ((1, 1), (2.01, 0.2))) * 0.03
        tw = mix(c, at(c, 0.12))
        amb = mix(amb, at(tw, rng.uniform(1, sec - 2)))[:int(SR * sec)]
    save_loop("Desert_Wind", amb, "Ambience", xfade=2.0)


# ----------------------------------------------------------------------------- surfaces
# Placeholders with the naming the game loads: Audio/Surfaces/<Kind>_<Surface>_<n>.wav
# (Step_Sand_0..., Impact_Metal_0...). Recorded sounds dropped in with the same names replace them.
def surfaces():
    for i in range(5):
        v = 1 + 0.08 * (i - 2)
        # sand: soft heel thud + grainy crunch, no ring
        sand = mix(sweep(95 * v, 45, 0.1) * env_exp(0.1, 0.02) * 0.6,
                   at(bp(noise(0.2), 1200 * v, 7000) * env_exp(0.2, 0.045, 0.01) * 0.8, 0.01),
                   at(hp(noise(0.12), 4000) * env_exp(0.12, 0.02, 0.01) * 0.25, 0.03))
        save(f"Step_Sand_{i}", sand, 0.55, sub="Surfaces")
        # stone / paving: hard short heel click over a dull thud
        stone = mix(sweep(140 * v, 60, 0.08) * env_exp(0.08, 0.015) * 0.7,
                    bp(noise(0.05), 1800 * v, 6500) * env_exp(0.05, 0.006) * 1.1,
                    at(bp(noise(0.06), 2200, 7000) * env_exp(0.06, 0.008) * 0.5, 0.045))
        save(f"Step_Stone_{i}", stone, 0.6, sub="Surfaces")
        # wood: hollow knock with a low body resonance
        knock = sum(a * np.sin(2 * np.pi * f * v * t(0.22)) for f, a in ((165, 1.0), (330, 0.35), (510, 0.2))) * env_exp(0.22, 0.05)
        wood = mix(knock * 0.8, bp(noise(0.08), 700, 3500) * env_exp(0.08, 0.012) * 0.7)
        save(f"Step_Wood_{i}", wood, 0.6, sub="Surfaces")
        # metal (container roofs, catwalk): dull boom of a sheet plus a short clank
        sheet = sum(a * np.sin(2 * np.pi * f * v * t(0.45)) for f, a in ((92, 1.0), (231, 0.5), (377, 0.35), (611, 0.25))) * env_exp(0.45, 0.12)
        metal = mix(sheet * 0.7, metal_click(1300 * v, 0.12, 0.02) * 0.6, bp(noise(0.05), 900, 4000) * env_exp(0.05, 0.01) * 0.5)
        save(f"Step_Metal_{i}", metal, 0.6, sub="Surfaces")
    for i in range(3):
        v = 1 + 0.1 * (i - 1)
        save(f"Impact_Sand_{i}", mix(lp(noise(0.25), 1800 * v) * env_exp(0.25, 0.04), hp(noise(0.3), 3000) * env_exp(0.3, 0.08, 0.01) * 0.4), 0.55, sub="Surfaces")
        save(f"Impact_Stone_{i}", mix(hp(noise(0.05), 3500) * env_exp(0.05, 0.004) * 1.2, lp(noise(0.25), 2400 * v) * env_exp(0.25, 0.03),
                                       at(bp(noise(0.4), 2500, 8000) * env_exp(0.4, 0.09, 0.02) * 0.25, 0.02)), 0.6, sub="Surfaces")
        save(f"Impact_Wood_{i}", mix(sum(a * np.sin(2 * np.pi * f * v * t(0.18)) for f, a in ((240, 1), (520, 0.4))) * env_exp(0.18, 0.03),
                                      bp(noise(0.08), 900, 5000) * env_exp(0.08, 0.01)), 0.6, sub="Surfaces")
        save(f"Impact_Metal_{i}", mix(bell(1250 * v, 0.6, 0.12, ((1, 1), (2.32, 0.6), (3.9, 0.35), (5.1, 0.2))) * 0.8,
                                       hp(noise(0.04), 3000) * env_exp(0.04, 0.004)), 0.6, sub="Surfaces")
    # distant gunfire: what's left of a shot after a few hundred metres - a low boom and a rolling tail
    boom = lp(noise(1.6), 420) * env_exp(1.6, 0.12, 0.004)
    roll = lp(noise(1.6), 260) * env_exp(1.6, 0.55, 0.06) * 0.6
    save("Distant_Shot", echo(mix(boom, at(roll, 0.03)), 0.23, 0.45, n=3, lowpass=500), 0.8, sub="Surfaces")


if __name__ == "__main__":
    import sys
    if "--surfaces" in sys.argv:
        surfaces()
        raise SystemExit
    surfaces()
    weapons()
    player()
    ui()
    ambience()
    music()
    print("audio done ->", OUT)
