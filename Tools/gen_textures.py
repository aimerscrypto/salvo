"""Generate SALVO's small procedural texture set (all tileable where it matters).
python Tools/gen_textures.py
"""
import os
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
OUT = os.path.join(ROOT, "Assets", "_Salvo", "Art", "Textures")
os.makedirs(OUT, exist_ok=True)
rng = np.random.default_rng(7)


def fft_noise(n, beta, lo=0.0, hi=1e9):
    """Periodic (tileable) noise with 1/f^beta spectrum, band-limited to [lo, hi] cycles."""
    white = rng.standard_normal((n, n))
    f = np.fft.fft2(white)
    fx = np.fft.fftfreq(n) * n
    r = np.sqrt(fx[None, :] ** 2 + fx[:, None] ** 2)
    r[0, 0] = 1
    amp = 1.0 / r ** beta
    amp[(r < lo) | (r > hi)] = 0
    out = np.real(np.fft.ifft2(f * amp))
    out -= out.min()
    out /= out.max()
    return out


def save(arr, name, mode="L"):
    img = Image.fromarray(np.clip(arr * 255, 0, 255).astype(np.uint8), mode)
    img.save(os.path.join(OUT, name))
    print("wrote", name)


N = 512
# Detail: R = fine grain centred on 0.5 (shader does lerp(1, r*2, strength)), G = camo blobs
grain = fft_noise(N, 1.1, lo=6, hi=200)
grain = 0.5 + (grain - grain.mean()) * 0.9
blobs = fft_noise(N, 2.2, lo=2, hi=24)
detail = np.stack([grain, blobs, np.full((N, N), 0.5), np.ones((N, N))], -1)
save(detail, "T_Detail.png", "RGBA")

# Clouds: soft, low-frequency, tileable
c = fft_noise(N, 1.8, lo=1.5, hi=60)
c = (c - 0.3) / 0.7
save(np.clip(c, 0, 1), "T_CloudNoise.png")


def radial(n, fn):
    y, x = np.mgrid[0:n, 0:n]
    r = np.sqrt((x - n / 2 + 0.5) ** 2 + (y - n / 2 + 0.5) ** 2) / (n / 2)
    return fn(r)


# Soft puff (smoke / dust), white with alpha
S = 128
puff_noise = fft_noise(S, 1.6, lo=1, hi=20)
a = radial(S, lambda r: np.clip(1 - r, 0, 1) ** 1.6) * (0.65 + 0.35 * puff_noise)
a = np.clip((a - 0.08) * 1.4, 0, 1)
save(np.stack([np.ones((S, S)), np.ones((S, S)), np.ones((S, S)), a], -1), "T_Puff.png", "RGBA")

# Muzzle flash: stylized 5-point star with hard-ish edge (matte, not bloom)
img = Image.new("RGBA", (256, 256), (0, 0, 0, 0))
d = ImageDraw.Draw(img)
cx = cy = 128
pts = []
for k in range(10):
    ang = np.pi / 2 + k * np.pi / 5
    rad = 120 if k % 2 == 0 else 46
    pts.append((cx + rad * np.cos(ang), cy - rad * np.sin(ang)))
d.polygon(pts, fill=(255, 214, 120, 255))
pts2 = []
for k in range(10):
    ang = np.pi / 2 + np.pi / 5 + k * np.pi / 5
    rad = 70 if k % 2 == 0 else 30
    pts2.append((cx + rad * np.cos(ang), cy - rad * np.sin(ang)))
d.polygon(pts2, fill=(255, 245, 210, 255))
d.ellipse((cx - 26, cy - 26, cx + 26, cy + 26), fill=(255, 252, 235, 255))
img = img.filter(ImageFilter.GaussianBlur(1.2))
img.save(os.path.join(OUT, "T_MuzzleFlash.png"))
print("wrote T_MuzzleFlash.png")

# Bullet hole decal: dark core, cracked rim
img = Image.new("RGBA", (128, 128), (0, 0, 0, 0))
d = ImageDraw.Draw(img)
for k in range(9):
    ang = rng.uniform(0, 2 * np.pi)
    ln = rng.uniform(26, 50)
    d.line((64, 64, 64 + ln * np.cos(ang), 64 + ln * np.sin(ang)), fill=(40, 34, 30, 150), width=2)
d.ellipse((40, 40, 88, 88), fill=(70, 60, 52, 170))
d.ellipse((50, 50, 78, 78), fill=(28, 24, 22, 255))
img = img.filter(ImageFilter.GaussianBlur(0.8))
img.save(os.path.join(OUT, "T_BulletHole.png"))
print("wrote T_BulletHole.png")

# Tracer: horizontal streak, bright head fading tail (u = along)
W, H = 256, 16
u = np.linspace(0, 1, W)[None, :]
v = np.abs(np.linspace(-1, 1, H))[:, None]
alpha = np.clip(u ** 1.5, 0, 1) * np.clip(1 - v ** 2, 0, 1)
tr = np.stack([np.ones((H, W)), np.full((H, W), 0.93), np.full((H, W), 0.75), alpha], -1)
save(tr, "T_Tracer.png", "RGBA")

# Spark/shard: small diamond
img = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
d = ImageDraw.Draw(img)
d.polygon([(32, 2), (40, 32), (32, 62), (24, 32)], fill=(255, 236, 190, 255))
img.save(os.path.join(OUT, "T_Spark.png"))
print("wrote T_Spark.png")

# Soft blob shadow for characters/pickups (cheap contact shadow)
a = radial(64, lambda r: np.clip(1 - r, 0, 1) ** 1.5)
save(np.stack([np.zeros((64, 64))] * 3 + [a * 0.6], -1), "T_BlobShadow.png", "RGBA")

# Ground grain: larger, softer variation for terrain (separate tiling)
g = fft_noise(N, 1.4, lo=2, hi=120)
save(0.5 + (g - g.mean()) * 0.8, "T_GroundGrain.png")
