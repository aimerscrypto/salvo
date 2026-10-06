"""
SALVO hands: posed, gloved stylized hands that wrap real grip geometry.

Every pose is authored in the frame of the weapon marker it attaches to (GripR / GripL), so
Unity only has to parent the hand to the marker with an identity transform - the fingers then
sit exactly on the grip, handguard, pump or foregrip they were wrapped around.

A pose describes the gripped shape as an elliptic cylinder: axis A, and the radii (ru, rv)
along two perpendicular directions U and V. A finger is four joints (knuckle, two finger
joints, tip) given as angles around that cylinder plus a height along the axis; the joints are
pushed out so the straight phalanges rest on the surface instead of cutting into it.

Fingerless tactical gloves: glove on palm and first phalanges, skin on the rest, a padded
knuckle guard. Fingers read clearly against dark gun grips.
"""
import math
import bmesh
from mathutils import Vector, Matrix
from salvo_lib import _from_bm, box, join

GLOVE, PAD, SKIN = "glove", "glove_pad", "skin"

# phalanx radii (proximal, middle, distal) - slightly chunky for the stylized look
R_INDEX = (0.0102, 0.0094, 0.0087)
R_MIDDLE = (0.0106, 0.0098, 0.0090)
R_RING = (0.0100, 0.0093, 0.0086)
R_PINKY = (0.0090, 0.0084, 0.0078)
R_THUMB = (0.0128, 0.0112, 0.0098)


# --------------------------------------------------------------------------------------
# primitives
# --------------------------------------------------------------------------------------
def capsule(name, p0, p1, r0, r1, color, verts=10, cap0=True, cap1=True):
    """Tapered capsule between two points (smooth shaded)."""
    p0, p1 = Vector(p0), Vector(p1)
    d = p1 - p0
    length = max(d.length, 1e-4)
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=False, cap_tris=False, segments=verts,
                          radius1=r0, radius2=r1, depth=length)
    rings = max(4, verts // 2)
    for cap, r, z in ((cap0, r0, -length / 2), (cap1, r1, length / 2)):
        if cap:
            ret = bmesh.ops.create_uvsphere(bm, u_segments=verts, v_segments=rings, radius=r)
            bmesh.ops.translate(bm, vec=Vector((0, 0, z)), verts=ret["verts"])
    rot = d.normalized().to_track_quat("Z", "Y").to_matrix().to_4x4()
    bmesh.ops.transform(bm, matrix=Matrix.Translation((p0 + p1) / 2) @ rot, verts=bm.verts)
    return _from_bm(name, bm, color)


def ellipsoid(name, center, axes, radii, color, segs=12, rings=8):
    """Ellipsoid with arbitrary orientation: axes = 3 orthonormal vectors, radii along each."""
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=segs, v_segments=rings, radius=1.0)
    basis = Matrix((axes[0], axes[1], axes[2])).transposed().to_4x4()
    m = Matrix.Translation(Vector(center)) @ basis @ Matrix.Diagonal((*radii, 1.0))
    bmesh.ops.transform(bm, matrix=m, verts=bm.verts)
    return _from_bm(name, bm, color)


def oriented_box(name, center, axes, size, color, bev=0.004):
    ob = box(name, size, (0, 0, 0), color, bev=bev)
    basis = Matrix((axes[0], axes[1], axes[2])).transposed().to_4x4()
    ob.matrix_world = Matrix.Translation(Vector(center)) @ basis
    return ob


# --------------------------------------------------------------------------------------
# grip frame
# --------------------------------------------------------------------------------------
class Frame:
    """Elliptic cylinder around axis A through `origin`; angle 0 points along U, 90 along V."""

    def __init__(self, A, U, V, ru, rv, origin=(0, 0, 0)):
        self.A, self.U, self.V = Vector(A).normalized(), Vector(U).normalized(), Vector(V).normalized()
        self.ru, self.rv, self.o = ru, rv, Vector(origin)

    def at(self, theta, s, off):
        t = math.radians(theta)
        return (self.o + self.A * s + self.U * ((self.ru + off) * math.cos(t)) +
                self.V * ((self.rv + off) * math.sin(t)))

    def radial(self, theta):
        t = math.radians(theta)
        n = self.U * (math.cos(t) / max(self.ru, 1e-3)) + self.V * (math.sin(t) / max(self.rv, 1e-3))
        return n.normalized()

    def tangent(self, theta):
        return self.A.cross(self.radial(theta)).normalized()


frame = Frame


def wrapped(f, thetas, heights, radii, knuckle_lift=0.005):
    """Joint positions for a finger wrapped around the frame's surface."""
    pts = []
    r_mean = (f.ru + f.rv) / 2
    for i, (th, s) in enumerate(zip(thetas, heights)):
        r = radii[min(i, len(radii) - 1)]
        # largest bend at this joint -> push out so the chord midpoint touches the surface
        dth = 0.0
        if i > 0:
            dth = max(dth, abs(thetas[i] - thetas[i - 1]))
        if i < len(thetas) - 1:
            dth = max(dth, abs(thetas[i + 1] - thetas[i]))
        half = math.radians(min(dth, 120.0)) / 2
        off = (r_mean + r) / math.cos(half) - r_mean
        if i == 0:
            off += knuckle_lift
        pts.append(f.at(th, s, off))
    return pts


# --------------------------------------------------------------------------------------
# hand assembly
# --------------------------------------------------------------------------------------
def finger(prefix, pts, radii, colors, verts):
    """Chain of phalanges; each joint is capped once (by the segment ending there)."""
    parts = []
    for i in range(len(pts) - 1):
        r1 = radii[i + 1] if i + 1 < len(radii) else radii[i] * 0.92
        parts.append(capsule(f"{prefix}{i}", pts[i], pts[i + 1], radii[i], r1, colors[i], verts=verts, cap0=(i == 0)))
    return parts


def build_hand(name, spec, detail=1.0, forearm=True, team="TEAM"):
    """spec keys: fingers [(pts, radii)], thumb (pts, radii), palm [(center, axes, radii)],
    pad (center, axes, size) | None, wrist (point), dir (forearm direction)."""
    v = max(6, int(round(10 * detail)))
    P = []
    for fi, (pts, radii) in enumerate(spec["fingers"]):
        P += finger(f"{name}_f{fi}_", pts, radii, (GLOVE, SKIN, SKIN), v)
        # knuckle joint (glove)
        P.append(capsule(f"{name}_k{fi}", pts[0], pts[0] + (pts[1] - pts[0]) * 0.05, radii[0] * 1.12, radii[0] * 1.1, GLOVE, verts=v))
    tpts, tr = spec["thumb"]
    P += finger(f"{name}_t", tpts, tr, (GLOVE, GLOVE, SKIN), v)
    for i, (c, axes, radii) in enumerate(spec["palm"]):
        P.append(ellipsoid(f"{name}_palm{i}", c, axes, radii, GLOVE, segs=max(8, int(14 * detail)), rings=max(6, int(9 * detail))))
    if detail >= 0.8:
        # padded knuckle ridge across the knuckle row
        palm_c = Vector(spec["palm"][0][0])
        k0, k1 = Vector(spec["fingers"][0][0][0]), Vector(spec["fingers"][-1][0][0])
        n0, n1 = (k0 - palm_c).normalized(), (k1 - palm_c).normalized()
        P.append(capsule(f"{name}_pad", k0 + n0 * 0.006, k1 + n1 * 0.006, 0.0085, 0.0078, PAD, verts=v))
    wrist = Vector(spec["wrist"])
    d = Vector(spec["dir"]).normalized()
    heel = Vector(spec["palm"][-1][0])
    P.append(capsule(f"{name}_heel", heel, wrist, 0.022, 0.024, GLOVE, verts=v))
    if forearm:
        P.append(capsule(f"{name}_cuffg", wrist, wrist + d * 0.055, 0.0255, 0.028, GLOVE, verts=v + 2))
        P.append(capsule(f"{name}_strap", wrist + d * 0.03, wrist + d * 0.045, 0.0285, 0.0285, PAD, verts=v + 2, cap0=False, cap1=False))
        P.append(capsule(f"{name}_roll", wrist + d * 0.062, wrist + d * 0.09, 0.034, 0.036, "olive_dark", verts=v + 4))
        P.append(capsule(f"{name}_sleeve", wrist + d * 0.08, wrist + d * 0.42, 0.033, 0.044, team, verts=v + 4))
    return join(P, name)


# --------------------------------------------------------------------------------------
# poses
# --------------------------------------------------------------------------------------
def _palm_pair(f, th_main, th_heel, s_main, s_heel, extra=0.0):
    """Two flattened ellipsoids hugging the cylinder: main palm and the heel of the hand."""
    out = []
    for th, s, (rr, rt, ra) in ((th_main, s_main, (0.0145, 0.026, 0.05 + extra)),
                                (th_heel, s_heel, (0.0145, 0.024, 0.042 + extra))):
        n = f.radial(th)
        t = f.tangent(th)
        c = f.at(th, s, rr * 0.75)
        out.append((c, (n, t, f.A), (rr, rt, ra)))
    return out


def pose_pistol_grip(ru=0.017, rv=0.024, trigger=True):
    """Right hand on a vertical pistol grip (marker GripR). Grip axis +Z, forward +Y, right +X."""
    f = frame((0, 0, 1), (1, 0, 0), (0, 1, 0), ru, rv)
    fingers = []
    if trigger:
        # index finger reaches forward along the frame and curls onto the trigger
        fingers.append(([Vector((ru + 0.011, 0.012, 0.047)), Vector((ru + 0.006, 0.045, 0.052)),
                         Vector((0.004, 0.054, 0.051)), Vector((-0.008, 0.049, 0.049))], R_INDEX))
    else:
        fingers.append((wrapped(f, (20, 90, 146, 190), (0.045, 0.046, 0.046, 0.045), R_INDEX), R_INDEX))
    fingers.append((wrapped(f, (18, 88, 142, 186), (0.022, 0.023, 0.023, 0.022), R_MIDDLE), R_MIDDLE))
    fingers.append((wrapped(f, (18, 86, 138, 179), (-0.001, 0.0, 0.0, -0.001), R_RING), R_RING))
    fingers.append((wrapped(f, (22, 84, 132, 168), (-0.023, -0.022, -0.022, -0.023), R_PINKY), R_PINKY))
    thumb = [Vector((ru + 0.004, -0.022, 0.014)), Vector((0.004, -rv - 0.013, 0.036)),
             Vector((-ru - 0.008, -0.017, 0.051)), Vector((-ru - 0.012, 0.013, 0.054))]
    if not trigger:  # fist: thumb closes over the index finger
        thumb[2] = Vector((-ru - 0.006, -0.008, 0.058))
        thumb[3] = Vector((-0.004, 0.022, 0.062))
    palm = _palm_pair(f, -12, -72, 0.004, -0.004)
    n = f.radial(22)
    pad = (f.at(22, 0.012, 0.028), (n, f.tangent(22), f.A), (0.008, 0.026, 0.078))
    return dict(fingers=fingers, thumb=(thumb, R_THUMB), palm=palm, pad=pad,
                wrist=(ru + 0.008, -rv - 0.014, -0.044), dir=(0.3, -0.6, -0.74))


def pose_support_guard(ru, rv):
    """Left hand under a horizontal handguard / pump (marker GripL at the axis).
    Axis +Y (forward), angle 0 = right side (+X), 90 = top (+Z); fingers curl up the right side."""
    f = frame((0, 1, 0), (1, 0, 0), (0, 0, 1), ru, rv)
    fingers = [
        (wrapped(f, (-80, -16, 24, 56), (0.031, 0.033, 0.034, 0.034), R_INDEX), R_INDEX),
        (wrapped(f, (-82, -18, 24, 57), (0.009, 0.010, 0.011, 0.011), R_MIDDLE), R_MIDDLE),
        (wrapped(f, (-80, -20, 18, 49), (-0.013, -0.012, -0.011, -0.011), R_RING), R_RING),
        (wrapped(f, (-76, -20, 12, 40), (-0.033, -0.032, -0.031, -0.031), R_PINKY), R_PINKY),
    ]
    thumb = [f.at(-150, -0.006, 0.02), f.at(-168, 0.022, 0.014), f.at(-182, 0.047, 0.012), f.at(-186, 0.069, 0.012)]
    palm = _palm_pair(f, -118, -158, 0.0, -0.016, extra=-0.006)
    n = f.radial(-125)
    pad = (f.at(-125, 0.0, 0.028), (n, f.tangent(-125), f.A), (0.008, 0.03, 0.07))
    wrist = f.at(-150, -0.05, 0.022)
    return dict(fingers=fingers, thumb=(thumb, R_THUMB), palm=palm, pad=pad,
                wrist=tuple(wrist), dir=(-0.45, -0.62, -0.64))


def pose_support_vertical(ru=0.016, rv=0.016):
    """Left hand on a vertical foregrip (marker GripL): mirror of the pistol grip, no trigger."""
    f = frame((0, 0, 1), (-1, 0, 0), (0, 1, 0), ru, rv)
    fingers = [
        (wrapped(f, (18, 96, 156, 202), (0.03, 0.031, 0.031, 0.03), R_INDEX), R_INDEX),
        (wrapped(f, (16, 94, 154, 200), (0.009, 0.01, 0.01, 0.009), R_MIDDLE), R_MIDDLE),
        (wrapped(f, (16, 92, 150, 194), (-0.012, -0.011, -0.011, -0.012), R_RING), R_RING),
        (wrapped(f, (20, 90, 144, 184), (-0.032, -0.031, -0.031, -0.032), R_PINKY), R_PINKY),
    ]
    thumb = [Vector((-ru - 0.004, -0.02, 0.014)), Vector((-0.004, -rv - 0.013, 0.034)),
             Vector((ru + 0.008, -0.015, 0.042)), Vector((ru + 0.011, 0.012, 0.044))]
    palm = _palm_pair(f, -12, -72, 0.0, -0.006)
    n = f.radial(22)
    pad = (f.at(22, 0.0, 0.028), (n, f.tangent(22), f.A), (0.008, 0.026, 0.074))
    return dict(fingers=fingers, thumb=(thumb, R_THUMB), palm=palm, pad=pad,
                wrist=(-ru - 0.008, -rv - 0.014, -0.044), dir=(-0.42, -0.5, -0.76))


def pose_support_pistol(ru=0.017, rv=0.024):
    """Left hand cupping the shooting hand on a pistol (attached to GripR, same frame).
    Fingers wrap the front of the right hand's fingers, thumb runs forward under the right thumb."""
    big_u, big_v = ru + 0.021, rv + 0.021
    f = frame((0, 0, 1), (-1, 0, 0), (0, 1, 0), big_u, big_v)
    fingers = [
        (wrapped(f, (40, 88, 122, 150), (0.014, 0.014, 0.013, 0.012), R_INDEX, 0.003), R_INDEX),
        (wrapped(f, (38, 86, 120, 148), (-0.007, -0.007, -0.008, -0.008), R_MIDDLE, 0.003), R_MIDDLE),
        (wrapped(f, (38, 84, 116, 142), (-0.028, -0.028, -0.028, -0.028), R_RING, 0.003), R_RING),
        (wrapped(f, (40, 82, 110, 134), (-0.047, -0.047, -0.047, -0.046), R_PINKY, 0.003), R_PINKY),
    ]
    thumb = [Vector((-0.046, -0.02, 0.008)), Vector((-0.047, 0.004, 0.024)),
             Vector((-0.041, 0.03, 0.032)), Vector((-0.033, 0.055, 0.034))]
    palm = _palm_pair(f, -6, -52, -0.012, -0.018)
    n = f.radial(10)
    pad = (f.at(20, -0.016, 0.026), (n, f.tangent(20), f.A), (0.008, 0.026, 0.07))
    return dict(fingers=fingers, thumb=(thumb, R_THUMB), palm=palm, pad=pad,
                wrist=(-0.05, -0.036, -0.05), dir=(-0.44, -0.58, -0.68))


def pose_fist(ru=0.0145, rv=0.0165):
    """Right hand closed around a knife handle (marker GripR, grip axis +Z along the handle)."""
    return pose_pistol_grip(ru, rv, trigger=False)
