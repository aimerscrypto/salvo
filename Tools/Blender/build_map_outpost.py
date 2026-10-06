"""
OUTPOST - a sun-baked Mediterranean desert outpost. Point-symmetric (180deg about the origin)
so Team Deathmatch is fair; each lane has a container yard at one end and a market at the other.

Blender axes: X east, Y north, Z up. Units: metres. CharacterController: r=0.35, h=1.8, step=0.4.

Naming contract with Unity (SalvoMapImporter):
  NC_*            render only, no collider (foliage, far scenery, small decor)
  SP_FFA_* / SP_RED_* / SP_BLUE_*   spawn points (local -Y = facing)
  ROAM_*          bot roam points
  CAM_*           main-menu camera path (local -Y = look direction... see cam())
  SHOWCASE / SHOWCASE_CAM
  PAD_<n>_v<vy>_h<vh>   jump pad: launch vy up, vh along facing
  PK_HEALTH_* / PK_AMMO_*   pickups
"""
import os
import sys
import math
import random
sys.path.append(os.path.dirname(__file__))
from salvo_lib import *  # noqa
from salvo_lib import _from_bm
import bmesh
from mathutils import Matrix, Vector, Euler

R = random.Random(7)
MESHES = []
MARKERS = []


class Frame:
    """Placement frame: compose translations + z-rotations (used for the mirrored half)."""

    def __init__(self, x=0.0, y=0.0, z=0.0, rz=0.0, parent=None, tag=""):
        m = Matrix.Translation((x, y, z)) @ Matrix.Rotation(math.radians(rz), 4, "Z")
        self.m = parent.m @ m if parent else m
        self.rzdeg = (parent.rzdeg if parent else 0.0) + rz
        self.tag = tag or (parent.tag if parent else "")

    def p(self, x, y, z=0.0):
        return tuple(self.m @ Vector((x, y, z)))

    def sub(self, x, y, z=0.0, rz=0.0):
        return Frame(x, y, z, rz, parent=self)

    def r(self, rot):
        return (rot[0], rot[1], rot[2] + self.rzdeg)

    def n(self, name):
        return name + self.tag

    # primitives in frame space
    def box(self, name, size, at, color, rot=(0, 0, 0), bev=0.04, dense=0.0, **kw):
        o = box(self.n(name), size, self.p(*at), color, rot=self.r(rot), bev=bev, **kw)
        if dense > 0:
            densify(o, dense)
        return o

    def cyl(self, name, r, depth, at, color, rot=(0, 0, 0), **kw):
        return cyl(self.n(name), r, depth, self.p(*at), color, rot=self.r(rot), **kw)

    def wedge(self, name, size, at, color, rot=(0, 0, 0), **kw):
        return wedge(self.n(name), size, self.p(*at), color, rot=self.r(rot), **kw)

    def blob(self, name, r, at, color, **kw):
        rot = kw.pop("rot", (0, 0, 0))
        return blob(self.n(name), r, self.p(*at), color, rot=self.r(rot), **kw)

    def sphere(self, name, r, at, color, **kw):
        return sphere(self.n(name), r, self.p(*at), color, **kw)

    def marker(self, name, at, face_deg=0.0):
        """face_deg: compass-ish yaw in frame space; 0 = facing -Y (south)."""
        e = empty(self.n(name), self.p(*at), (0, 0, face_deg + self.rzdeg))
        MARKERS.append(e)
        return e


def densify(ob, max_len):
    """Subdivide long edges so baked vertex AO has enough resolution on big surfaces."""
    me = ob.data
    bm = bmesh.new()
    bm.from_mesh(me)
    for _ in range(6):
        long_edges = [e for e in bm.edges if e.calc_length() > max_len]
        if not long_edges:
            break
        bmesh.ops.subdivide_edges(bm, edges=long_edges, cuts=1, use_grid_fill=True)
    bm.to_mesh(me)
    bm.free()
    for p in me.polygons:
        p.use_smooth = True
    return ob


def prop(name, parts, nc=False):
    ob = join(parts, ("NC_" if nc else "") + name)
    MESHES.append(ob)
    return ob


# =====================================================================================
# kit
# =====================================================================================
def crate(F, name, x, y, z=0.0, s=1.1, rz=0.0, color="wood"):
    G = F.sub(x, y, z, rz)
    parts = [G.box("crate", (s, s, s), (0, 0, s / 2), color, bev=0.05)]
    t = 0.07
    for sx in (-1, 1):
        parts.append(G.box("rail", (t, s + 0.02, s + 0.02), (sx * (s / 2 - t / 2 + 0.01), 0, s / 2), "wood_dark", bev=0.02))
    parts.append(G.box("band", (s + 0.03, t, s + 0.03), (0, 0, s / 2), "wood_dark", bev=0.02))
    parts.append(G.box("diag", (s * 1.3, 0.05, 0.12), (0, -s / 2 - 0.01, s / 2), "wood_light", rot=(0, 45, 0), bev=0.015))
    parts.append(G.box("diag2", (s * 1.3, 0.05, 0.12), (0, s / 2 + 0.01, s / 2), "wood_light", rot=(0, -45, 0), bev=0.015))
    return prop(name, parts)


def barrel(F, name, x, y, z=0.0, color="red", tipped=False):
    G = F.sub(x, y, z)
    rot = (90, 0, R.uniform(0, 180)) if tipped else (0, 0, 0)
    zc = 0.32 if tipped else 0.45
    parts = [G.cyl("barrel", 0.32, 0.9, (0, 0, zc), color, rot=rot, verts=14, bev=0.03)]
    if not tipped:
        for hz in (0.2, 0.7):
            parts.append(G.cyl("ring", 0.335, 0.05, (0, 0, hz), "dark", verts=14, bev=0.01))
        parts.append(G.cyl("lid", 0.27, 0.03, (0, 0, 0.91), "steel", verts=14, bev=0.01))
    return prop(name, parts)


def sandbags(F, name, x, y, length=3.0, rows=3, rz=0.0):
    G = F.sub(x, y, 0, rz)
    parts = []
    n = max(2, int(length / 0.62))
    for r in range(rows):
        off = 0.31 if r % 2 else 0.0
        for i in range(n - (1 if r % 2 else 0)):
            px = -length / 2 + 0.31 + i * 0.62 + off
            parts.append(G.box("bag", (0.6, 0.42, 0.24), (px, R.uniform(-0.02, 0.02), 0.12 + r * 0.22), "khaki" if (i + r) % 3 else "sand_dark",
                               rot=(0, 0, R.uniform(-4, 4)), bev=0.09, segs=2))
    return prop(name, parts)


CONTAINER_COLORS = ["teal", "coral", "mustard", "navy", "teal_dark", "terracotta"]


def container(F, name, x, y, z=0.0, rz=0.0, color="teal"):
    G = F.sub(x, y, z, rz)
    L, W, H = 6.1, 2.5, 2.6
    parts = [G.box("body", (W, L, H), (0, 0, H / 2), color, bev=0.05, dense=1.6)]
    # corrugation ribs on the long sides
    for i in range(9):
        py = -L / 2 + 0.45 + i * (L - 0.9) / 8
        for sx in (-1, 1):
            parts.append(G.box("rib", (0.08, 0.22, H - 0.3), (sx * (W / 2 + 0.02), py, H / 2), color, bev=0.0))
    # frame corners
    for sx in (-1, 1):
        for sy in (-1, 1):
            parts.append(G.box("post", (0.18, 0.18, H + 0.02), (sx * (W / 2 - 0.06), sy * (L / 2 - 0.06), H / 2), "dark" if color != "navy" else "steel", bev=0.02))
    # door end
    parts.append(G.box("door", (W - 0.3, 0.06, H - 0.3), (0, -L / 2 - 0.02, H / 2), color, bev=0.02))
    for sx in (-0.45, 0.45):
        parts.append(G.box("bar", (0.05, 0.07, H - 0.4), (sx, -L / 2 - 0.06, H / 2), "steel", bev=0.01))
    return prop(name, parts)


def palm(F, name, x, y, z=0.0, h=6.5, lean=8.0, rz=None, nc_crown=True, far=False):
    rz = R.uniform(0, 360) if rz is None else rz
    G = F.sub(x, y, z, rz)
    seg = 4 if far else 7
    parts = []
    pos = Vector((0, 0, 0))
    ang = 0.0
    for i in range(seg):
        ang = lean * (i / seg) ** 1.5
        d = h / seg
        r1 = 0.2 - 0.08 * i / seg
        mid = pos + Vector((0, math.sin(math.radians(ang)) * d / 2, math.cos(math.radians(ang)) * d / 2))
        parts.append(G.cyl("trunk", r1, d * 1.04, tuple(mid), "trunk" if i % 2 else "wood", rot=(-ang, 0, 0), verts=6 if far else 8, bev=0.0 if far else 0.02, segs=1))
        pos = pos + Vector((0, math.sin(math.radians(ang)) * d, math.cos(math.radians(ang)) * d))
    trunk = prop(name + "_trunk", parts)
    crown = []
    top = pos
    for k in range(8):
        a = k * 45 + R.uniform(-10, 10)
        droop = R.uniform(18, 32)
        length = R.uniform(2.2, 2.9)
        G2 = G.sub(top.x, top.y, top.z + 0.05, a)
        # each frond: two tapered segments drooping outward
        crown.append(G2.box("frond", (0.55, length * 0.55, 0.06), (0, length * 0.25, 0.1), "leaf" if k % 2 else "leaf_light",
                            rot=(droop * 0.4, 0, 0), bev=0.0, taper=(0.6, 1.0)))
        crown.append(G2.box("frond2", (0.42, length * 0.55, 0.05), (0, length * 0.7, -0.35), "leaf_dark" if k % 2 else "leaf",
                            rot=(droop, 0, 0), bev=0.0, taper=(0.3, 1.0)))
    for k in range(0 if far else 3):
        crown.append(G.sphere("coco", 0.14, (top.x + math.cos(k * 2.1) * 0.22, top.y + math.sin(k * 2.1) * 0.22, top.z - 0.15), "wood_dark", segs=8, rings=6))
    prop(name + "_crown", crown, nc=nc_crown)
    return trunk


def bush(F, name, x, y, z=0.0, s=1.0):
    G = F.sub(x, y, z)
    parts = [G.blob("b", 0.7 * s, (0, 0, 0.45 * s), "leaf", subdiv=1, jitter=0.2, scale=(1.2, 1.0, 0.8)),
             G.blob("b2", 0.5 * s, (0.5 * s, 0.2 * s, 0.35 * s), "leaf_light", subdiv=1, jitter=0.2),
             G.blob("b3", 0.45 * s, (-0.45 * s, -0.25 * s, 0.3 * s), "leaf_dark", subdiv=1, jitter=0.2)]
    return prop(name, parts, nc=True)


def planter(F, name, x, y, w=2.4, d=1.2, rz=0.0, plant=True):
    G = F.sub(x, y, 0, rz)
    parts = [G.box("pl", (w, d, 0.7), (0, 0, 0.35), "terracotta", bev=0.06),
             G.box("rim", (w + 0.12, d + 0.12, 0.1), (0, 0, 0.72), "terra_dark", bev=0.03),
             G.box("soil", (w - 0.2, d - 0.2, 0.05), (0, 0, 0.7), "wood_dark", bev=0.0)]
    ob = prop(name, parts)
    if plant:
        bush(G, name + "_bush", 0, 0, 0.6, 0.75)
    return ob


def wall_seg(F, name, a, b, h, t=0.8, color="sand_mid", cap="sand_dark", z0=0.0, crenel=False):
    ax, ay = a
    bx, by = b
    L = math.hypot(bx - ax, by - ay)
    ang = math.degrees(math.atan2(by - ay, bx - ax))
    cx, cy = (ax + bx) / 2, (ay + by) / 2
    G = F.sub(cx, cy, z0, ang)
    parts = [G.box("w", (L, t, h), (0, 0, h / 2), color, bev=0.05, dense=1.8),
             G.box("plinth", (L + 0.02, t + 0.14, 0.4), (0, 0, 0.2), cap, bev=0.04)]
    if cap:
        parts.append(G.box("cap", (L + 0.12, t + 0.18, 0.2), (0, 0, h + 0.1), cap, bev=0.05))
    if crenel:
        n = int(L / 1.6)
        for i in range(n):
            parts.append(G.box("cren", (0.8, t + 0.1, 0.5), (-L / 2 + 0.8 + i * L / n, 0, h + 0.45), color, bev=0.05))
    return prop(name, parts)


def window(G, x, y, z, face_rz, accent, w=0.95, h=1.25):
    """Decorative window on a facade; returns parts. (x,y) is on the wall surface."""
    W = G.sub(x, y, z, face_rz)
    return [W.box("win", (w, 0.12, h), (0, 0.0, 0), "navy", bev=0.02),
            W.box("sill", (w + 0.25, 0.22, 0.1), (0, -0.06, -h / 2 - 0.05), "cream", bev=0.03),
            W.box("lintel", (w + 0.2, 0.18, 0.14), (0, -0.04, h / 2 + 0.08), "sand_dark", bev=0.03),
            W.box("shutL", (w * 0.5, 0.06, h), (-w * 0.78, -0.08, 0), accent, bev=0.02),
            W.box("shutR", (w * 0.5, 0.06, h), (w * 0.78, -0.08, 0), accent, bev=0.02)]


def house(F, name, cx, cy, w, d, h, wall="sand", trim="sand_dark", accent="teal", rz=0.0, floors=None,
          door_faces=("S",), parapet=True, roof_props=True, awning=None, parapet_sides="NSEW"):
    G = F.sub(cx, cy, 0, rz)
    parts = [G.box("body", (w, d, h), (0, 0, h / 2), wall, bev=0.06, dense=1.8),
             G.box("base", (w + 0.16, d + 0.16, 0.45), (0, 0, 0.225), trim, bev=0.05),
             G.box("roof", (w + 0.1, d + 0.1, 0.16), (0, 0, h + 0.08), trim, bev=0.04)]
    if parapet:
        t, ph = 0.28, 0.85
        if "N" in parapet_sides:
            parts += [G.box("parN", (w + 0.1, t, ph), (0, d / 2 - t / 2 + 0.05, h + ph / 2 + 0.1), wall, bev=0.05),
                      G.box("parcapN", (w + 0.2, 0.4, 0.1), (0, d / 2 - 0.05, h + ph + 0.12), trim, bev=0.03)]
        if "S" in parapet_sides:
            parts += [G.box("parS", (w + 0.1, t, ph), (0, -d / 2 + t / 2 - 0.05, h + ph / 2 + 0.1), wall, bev=0.05),
                      G.box("parcapS", (w + 0.2, 0.4, 0.1), (0, -d / 2 + 0.05, h + ph + 0.12), trim, bev=0.03)]
        if "E" in parapet_sides:
            parts += [G.box("parE", (t, d - 0.4, ph), (w / 2 - t / 2 + 0.05, 0, h + ph / 2 + 0.1), wall, bev=0.05),
                      G.box("parcapE", (0.4, d, 0.1), (w / 2 - 0.05, 0, h + ph + 0.12), trim, bev=0.03)]
        if "W" in parapet_sides:
            parts += [G.box("parW", (t, d - 0.4, ph), (-w / 2 + t / 2 - 0.05, 0, h + ph / 2 + 0.1), wall, bev=0.05),
                      G.box("parcapW", (0.4, d, 0.1), (-w / 2 + 0.05, 0, h + ph + 0.12), trim, bev=0.03)]
    # windows / doors
    floors = floors or max(1, int(h // 2.9))
    faces = {"S": (0, -d / 2 - 0.05, 0, w), "N": (0, d / 2 + 0.05, 180, w), "E": (w / 2 + 0.05, 0, 90, d), "W": (-w / 2 - 0.05, 0, -90, d)}
    for fname, (fx, fy, frz, span) in faces.items():
        n = max(1, int(span / 2.8))
        for fl in range(floors):
            z = 1.55 + fl * 2.9
            if z + 0.8 > h:
                continue
            for i in range(n):
                off = -span / 2 + span / n * (i + 0.5)
                if fl == 0 and fname in door_faces and i == n // 2:
                    D = G.sub(fx, fy, 0, frz)
                    parts += [D.box("door", (1.25, 0.14, 2.3), (off, 0, 1.15 + 0.4), "wood_dark", bev=0.03),
                              D.box("doorframe", (1.6, 0.2, 0.2), (off, -0.04, 2.45 + 0.4), trim, bev=0.03),
                              D.box("step", (1.8, 0.6, 0.18), (off, -0.25, 0.09 + 0.36), trim, bev=0.04)]
                    continue
                lx = fx + (off if frz in (0, 180) else 0) * (1 if frz == 0 else -1 if frz == 180 else 0)
                ly = fy + (off if frz in (90, -90) else 0)
                parts += window(G, lx, ly, z, frz, accent)
    if awning:
        parts += awning_parts(G, 0, -d / 2 - 0.05, 2.75, min(w - 1.0, 4.0), awning, 0)
    body = prop(name, parts)
    if roof_props:
        rp = [G.cyl("tank", 0.55, 1.1, (w / 4, d / 5, h + 0.75), "white", verts=12, bev=0.04),
              G.cyl("tankleg", 0.6, 0.18, (w / 4, d / 5, h + 0.25), "steel", verts=12, bev=0.02),
              G.box("ac", (0.9, 0.7, 0.6), (-w / 4, -d / 5, h + 0.46), "concrete", bev=0.05)]
        prop(name + "_roofstuff", rp)
    return body


def awning_parts(G, x, y, z, width, colors, rz):
    A = G.sub(x, y, z, rz)
    parts = []
    stripes = max(4, int(width / 0.5))
    sw = width / stripes
    for i in range(stripes):
        parts.append(A.box("stripe", (sw + 0.005, 1.6, 0.05), (-width / 2 + sw * (i + 0.5), -0.75, -0.3), colors[i % 2], rot=(-22, 0, 0), bev=0.0))
    # scalloped front edge
    for i in range(stripes):
        parts.append(A.box("valance", (sw - 0.02, 0.04, 0.22), (-width / 2 + sw * (i + 0.5), -1.5, -0.72), colors[i % 2], bev=0.0))
    for sx in (-1, 1):
        parts.append(A.cyl("rod", 0.03, 1.7, (sx * width / 2, -0.75, -0.3), "dark", rot=(-22 + 90, 0, 0), verts=6, bev=0.0))
    return parts


def stairs(F, name, x, y, z0, width, rise, direction_rz, step_h=0.25, step_d=0.3, color="sand_dark", side=False, wall_side=0, landing=1.0):
    """Straight stair starting at (x,y,z0) climbing towards local +Y (rotated by direction_rz).
    wall_side = +1/-1: an external stair running along a building on that local-X side. It gets a
    landing at the top (you step sideways onto the roof from it) on a masonry pier, and a stepped
    parapet on the open side, so it reads as built access instead of a free-standing flight."""
    G = F.sub(x, y, z0, direction_rz)
    n = max(1, int(round(rise / step_h)))
    sh = rise / n
    parts = []
    for i in range(n):
        parts.append(G.box("step", (width, step_d * (n - i), sh), (0, step_d * i + step_d * (n - i) / 2, sh * i + sh / 2), color if i % 2 else "sand_mid", bev=0.025))
    if side:
        run = n * step_d
        for sx in (-1, 1):
            parts.append(G.box("cheek", (0.22, run, rise + 0.9), (sx * (width / 2 + 0.11), run / 2, (rise + 0.9) / 2 - 0.0), "sand", bev=0.04, dense=1.5))
    if wall_side:
        run = n * step_d
        ox = -wall_side * (width / 2 + 0.1)          # open side, away from the building
        # landing slab flush with the top step, carried by a pier at the open corner
        parts.append(G.box("landing", (width, landing, 0.3), (0, run + landing / 2, rise - 0.15), color, bev=0.03))
        parts.append(G.box("pier", (0.45, 0.45, rise - 0.3), (-wall_side * (width / 2 - 0.22), run + landing - 0.22, (rise - 0.3) / 2), "sand", bev=0.04, dense=1.5))
        # stepped parapet up the open side, around the landing's open side and end
        for i in range(n):
            parts.append(G.box("rail", (0.2, step_d, 0.9), (ox, step_d * i + step_d / 2, sh * (i + 1) + 0.45), "sand", bev=0.03))
        parts.append(G.box("rail_l", (0.2, landing + 0.2, 0.9), (ox, run + landing / 2 + 0.1, rise + 0.45), "sand", bev=0.03))
        parts.append(G.box("rail_e", (width + 0.4, 0.2, 0.9), (0, run + landing + 0.1, rise + 0.45), "sand", bev=0.03))
        parts.append(G.box("cap", (0.26, run + landing + 0.2, 0.08), (ox, (run + landing + 0.2) / 2, rise + 0.94), "cream", bev=0.02))
    return prop(name, parts)


def ramp(F, name, x, y, z0, width, length, rise, direction_rz, color="wood"):
    """Wooden plank ramp, low end at (x,y), climbing along local +Y."""
    G = F.sub(x, y, z0, direction_rz)
    ang = math.degrees(math.atan2(rise, length))
    L = math.hypot(rise, length)
    parts = [G.box("deck", (width, L, 0.16), (0, length / 2, rise / 2 - 0.02), color, rot=(ang, 0, 0), bev=0.03)]
    n = int(L / 0.45)
    for i in range(n):
        t = (i + 0.5) / n
        parts.append(G.box("plank", (width + 0.06, 0.08, 0.04), (0, length * t, rise * t + 0.07), "wood_dark", rot=(ang, 0, 0), bev=0.01))
    for sx in (-1, 1):
        parts.append(G.box("beam", (0.14, L, 0.2), (sx * (width / 2 - 0.07), length / 2, rise / 2 - 0.15), "wood_dark", rot=(ang, 0, 0), bev=0.02))
    parts.append(G.box("leg", (width, 0.2, rise), (0, length - 0.2, rise / 2 - 0.1), "wood_dark", bev=0.03))
    return prop(name, parts)


def stall(F, name, x, y, rz, cloth):
    G = F.sub(x, y, 0, rz)
    parts = [G.box("table", (2.6, 1.1, 0.12), (0, 0, 0.9), "wood_light", bev=0.03),
             G.box("front", (2.6, 0.08, 0.84), (0, -0.52, 0.45), "wood", bev=0.02)]
    for sx in (-1.25, 1.25):
        for sy in (-0.5, 0.5):
            parts.append(G.box("leg", (0.1, 0.1, 0.9), (sx, sy, 0.45), "wood_dark", bev=0.02))
        parts.append(G.box("post", (0.1, 0.1, 2.6), (sx, 0.55, 1.3), "wood_dark", bev=0.02))
    parts += awning_parts(G, 0, 0.6, 2.6, 2.8, cloth, 0)
    body = prop(name, parts)
    goods = []
    fruit = ["coral", "mustard", "leaf_light", "red"]
    for i in range(6):
        gx = -1.0 + i * 0.4
        goods.append(G.box("basket", (0.36, 0.5, 0.16), (gx, 0.05, 1.04), "wood_light", bev=0.03))
        for k in range(3):
            goods.append(G.sphere("fruit", 0.07, (gx + R.uniform(-0.1, 0.1), R.uniform(-0.15, 0.2), 1.16), fruit[i % 4], segs=6, rings=4))
    prop(name + "_goods", goods, nc=True)
    return body


def lamp(F, name, x, y):
    G = F.sub(x, y)
    parts = [G.cyl("pole", 0.07, 3.4, (0, 0, 1.7), "dark", verts=8, bev=0.01),
             G.cyl("base", 0.18, 0.3, (0, 0, 0.15), "dark", verts=8, bev=0.02),
             G.box("arm", (0.06, 0.6, 0.06), (0, 0.28, 3.3), "dark", bev=0.0),
             G.box("head", (0.3, 0.3, 0.36), (0, 0.55, 3.15), "dark", bev=0.03, taper=(0.6, 0.6)),
             G.box("glass", (0.22, 0.22, 0.2), (0, 0.55, 3.08), "cream", bev=0.02)]
    return prop(name, parts, nc=False)


def arch_gate(F, name, x, y, w, h, depth, rz=0.0, color="sand", trim="terracotta", opening=3.2, open_h=3.2):
    """Wall with an archway. Opening centred."""
    G = F.sub(x, y, 0, rz)
    side_w = (w - opening) / 2
    parts = [G.box("l", (side_w, depth, h), (-opening / 2 - side_w / 2, 0, h / 2), color, bev=0.05, dense=1.6),
             G.box("r", (side_w, depth, h), (opening / 2 + side_w / 2, 0, h / 2), color, bev=0.05, dense=1.6),
             G.box("top", (opening + 0.02, depth, h - open_h), (0, 0, open_h + (h - open_h) / 2), color, bev=0.05),
             G.box("cap", (w + 0.2, depth + 0.25, 0.22), (0, 0, h + 0.11), trim, bev=0.05)]
    # segmental arch: voussoirs on a large-radius arc so the clear height stays high
    half = opening / 2
    Rr = half * 1.45
    sag = Rr - math.sqrt(Rr * Rr - half * half)
    cz = open_h - Rr
    amax = math.degrees(math.asin(half / Rr))
    n = 9
    for i in range(n):
        a = -amax + (2 * amax) * (i + 0.5) / n
        px = Rr * math.sin(math.radians(a))
        pz = cz + Rr * math.cos(math.radians(a))
        seg = 2 * Rr * math.sin(math.radians(amax / n)) + 0.04
        parts.append(G.box("vous", (seg, depth + 0.08, 0.34), (px, 0, pz + 0.05), trim, rot=(0, a, 0), bev=0.03))
        # spandrel fill above each stone up to the lintel
        fill_h = open_h - (pz + 0.2)
        if fill_h > 0.02:
            parts.append(G.box("span", (seg, depth, fill_h), (px, 0, pz + 0.2 + fill_h / 2), color, bev=0.0))
    for sx in (-1, 1):
        parts.append(G.box("pier", (0.42, depth + 0.12, open_h - sag), (sx * (half + 0.18), 0, (open_h - sag) / 2), trim, bev=0.04))
    return prop(name, parts)


def fountain(F, name, x, y, z, r=1.8):
    G = F.sub(x, y, z)
    parts = [G.cyl("basin", r, 0.7, (0, 0, 0.35), "concrete", verts=10, bev=0.05),
             G.cyl("lip", r + 0.12, 0.14, (0, 0, 0.72), "cream", verts=10, bev=0.04),
             G.cyl("pillar", 0.32, 1.3, (0, 0, 1.0), "concrete_dk", verts=10, bev=0.04),
             G.cyl("bowl", 0.75, 0.22, (0, 0, 1.6), "cream", verts=10, bev=0.05, r2=0.95),
             G.sphere("finial", 0.18, (0, 0, 1.9), "teal", segs=10, rings=6)]
    body = prop(name, parts)
    water = [G.cyl("water", r - 0.12, 0.05, (0, 0, 0.62), "water", verts=10, bev=0.0),
             G.cyl("water2", 0.7, 0.04, (0, 0, 1.69), "water", verts=10, bev=0.0)]
    prop(name + "_water", water, nc=True)
    return body


def water_tower(F, name, x, y):
    G = F.sub(x, y)
    parts = []
    for sx in (-1, 1):
        for sy in (-1, 1):
            parts.append(G.cyl("leg", 0.14, 7.0, (sx * 1.4, sy * 1.4, 3.5), "wood_dark", rot=(sy * 4, -sx * 4, 0), verts=8, bev=0.02))
    for z in (2.2, 4.6):
        for i, (sx, sy, rr) in enumerate(((0, 1.35, 0), (0, -1.35, 0), (1.35, 0, 90), (-1.35, 0, 90))):
            parts.append(G.box("brace", (2.8, 0.1, 0.16), (sx, sy, z), "wood_dark", rot=(0, 0, rr), bev=0.02))
    legs = prop(name + "_legs", parts)
    tank = [G.cyl("tank", 1.9, 2.4, (0, 0, 8.2), "wood", verts=16, bev=0.05),
            G.cyl("hoop1", 1.95, 0.1, (0, 0, 7.4), "dark", verts=16, bev=0.0),
            G.cyl("hoop2", 1.95, 0.1, (0, 0, 9.0), "dark", verts=16, bev=0.0),
            G.cyl("roof", 2.15, 1.0, (0, 0, 9.9), "terracotta", verts=16, bev=0.04, r2=0.2),
            G.cyl("deck", 2.2, 0.15, (0, 0, 6.95), "wood_dark", verts=16, bev=0.03)]
    prop(name + "_tank", tank, nc=True)
    return legs


def platform(F, name, cx, cy, w, d, h, color="concrete", trim="cream"):
    G = F.sub(cx, cy)
    parts = [G.box("plat", (w, d, h), (0, 0, h / 2), color, bev=0.06, dense=1.6),
             G.box("lip", (w + 0.16, d + 0.16, 0.14), (0, 0, h + 0.02), trim, bev=0.04)]
    return prop(name, parts)


def low_wall(F, name, x, y, length, rz=0.0, h=1.05, color="sand", cap="cream"):
    G = F.sub(x, y, 0, rz)
    parts = [G.box("lw", (length, 0.5, h), (0, 0, h / 2), color, bev=0.05, dense=1.6),
             G.box("lwcap", (length + 0.1, 0.62, 0.12), (0, 0, h + 0.06), cap, bev=0.03)]
    return prop(name, parts)


def pad(F, name, x, y, face_deg, vy, vh):
    F.marker(f"PAD_{name}_v{vy:g}_h{vh:g}", (x, y, 0.0), face_deg)


# =====================================================================================
# layout
# =====================================================================================
def build_half(F, accent, cloth, spawn_team):
    """One half of the map (north when unrotated). F is identity or rotated 180deg."""
    # ---------------- spawn yard (Y 25..36) --------------------------------------------
    arch_gate(F, "YardGate", 0, 25, 10.5, 4.6, 1.0, color="sand", trim="terracotta", opening=3.6, open_h=3.4)
    wall_seg(F, "YardWallW", (-13.2, 25), (-5.25, 25), 4.6, 1.0, "sand", "sand_dark")
    wall_seg(F, "YardWallE", (5.25, 25), (10.0, 25), 4.6, 1.0, "sand", "sand_dark")
    fountain(F, "YardFountain", 0, 31.5, 0, 1.5)
    crate(F, "YardCrate1", -8.5, 33.5, 0, 1.2, 12)
    crate(F, "YardCrate2", -7.3, 33.6, 0, 1.0, -8)
    crate(F, "YardCrate3", -8.0, 33.4, 1.2, 0.9, 30)
    barrel(F, "YardBarrel1", 7.6, 34.6, 0, accent)
    barrel(F, "YardBarrel2", 8.4, 34.0, 0, "dark")
    palm(F, "YardPalm1", -11.0, 34.5, 0, 7.0, 10)
    palm(F, "YardPalm2", 10.4, 27.0, 0, 6.2, 12)
    planter(F, "YardPlanter1", -4.5, 35.2, 2.6, 1.0)
    planter(F, "YardPlanter2", 4.5, 35.2, 2.6, 1.0)
    lamp(F, "YardLamp", -3.2, 26.6)
    sandbags(F, "YardBags", -9.0, 27.0, 2.6, 3, 90)

    # spawns
    pts = [(-6, 33), (-2.5, 34.5), (2.5, 34.5), (6, 33), (-9.5, 29.5), (8.5, 29.5), (-3, 28.5), (3, 28.5)]
    for i, (sx, sy) in enumerate(pts):
        F.marker(f"SP_{spawn_team}_{i:02d}", (sx, sy, 0.05), 0)
    for i, (sx, sy) in enumerate([(-4, 32.5), (4.5, 31.5)]):
        F.marker(f"SP_FFA_Y{i}", (sx, sy, 0.05), 0)

    # ---------------- barracks (NE) with rooftop stairs --------------------------------
    house(F, "Barracks", 19.0, 29.5, 11.0, 10.0, 6.4, wall="plaster", trim="sand_dark", accent=accent, door_faces=("S",), parapet_sides="NSE")
    stairs(F, "BarracksStairs", 12.65, 24.9, 0, 1.7, 6.4, 0, step_h=0.25, step_d=0.31, wall_side=1)
    F.marker("ROAM_barracks_roof", (19, 29.5, 6.5))
    crate(F, "RoofCrate", 22.0, 31.5, 6.4, 1.1, 20)
    sandbags(F, "RoofBags", 17.0, 25.4, 3.0, 2, 0)

    # ---------------- NW: water tower + shed -------------------------------------------
    water_tower(F, "WaterTower", -19.0, 32.0)
    house(F, "Shed", -25.8, 30.8, 6.0, 6.4, 3.6, wall="sand", trim="terra_dark", accent=accent, door_faces=("E",), roof_props=False, parapet_sides="NEW")
    stairs(F, "ShedStairs", -21.6, 26.9, 0, 1.4, 3.6, 90, step_h=0.24, step_d=0.3, wall_side=1)
    crate(F, "TowerCrate", -17.0, 28.6, 0, 1.1, 5)
    barrel(F, "TowerBarrel", -21.8, 35.2, 0, "red")
    barrel(F, "TowerBarrel2", -22.7, 34.6, 0, "red", tipped=True)

    # ---------------- west lane: container yard (X -28..-14, Y 4..23) -------------------
    container(F, "Cont1", -26.6, 17.5, 0, 0, CONTAINER_COLORS[0 if accent == "teal" else 1])
    container(F, "Cont2", -26.6, 11.3, 2.6, 0, "mustard")
    container(F, "Cont3", -19.5, 19.8, 0, 90, "navy")
    container(F, "Cont4", -21.4, 11.2, 0, 0, "terracotta")
    container(F, "Cont5", -16.6, 8.0, 0, 90, CONTAINER_COLORS[1 if accent == "teal" else 0])
    container(F, "Cont6", -26.6, 11.3, 0, 0, "teal_dark")
    ramp(F, "ContRamp", -26.6, 25.6, 0, 1.8, 5.0, 2.6, 180, "wood")
    crate(F, "ContCrate1", -24.0, 15.6, 0, 1.2, 10)
    crate(F, "ContCrate2", -18.0, 13.5, 0, 1.1, -15)
    crate(F, "ContCrate3", -18.0, 13.5, 1.1, 0.9, 25)
    barrel(F, "ContBarrel1", -24.0, 5.2, 0, accent)
    barrel(F, "ContBarrel2", -24.7, 5.6, 0, "dark", tipped=True)
    sandbags(F, "ContBags", -15.0, 15.5, 2.4, 3, 90)
    crate(F, "LaneCrate1", -12.6, 10.4, 0, 1.2, 20)
    crate(F, "LaneCrate2", -11.6, 11.4, 0, 1.0, -12)
    barrel(F, "LaneBarrel1", -14.6, 3.2, 0, "teal")
    sandbags(F, "LaneBags", -10.8, 19.6, 2.8, 3, 0)
    low_wall(F, "LaneWall", -17.5, 23.6, 3.0, 0)
    pad(F, "west", -21.4, 3.0, 180, 12.0, 9.0)   # launches north onto Cont4 roof
    F.marker("PK_HEALTH_cont", (-20.0, 15.6, 0.1))
    F.marker("PK_AMMO_yard", (-9.0, 31.0, 0.1))
    for i, p in enumerate([(-26.6, 18.5, 2.7), (-21.4, 11.2, 2.7), (-19.5, 16.0, 0), (-24.0, 9.0, 0), (-15.5, 11.5, 0)]):
        F.marker(f"ROAM_cont{i}", p)

    # ---------------- east lane: market (X 14..28, Y 5..22) ------------------------------
    house(F, "MarketHouse1", 25.6, 17.8, 6.2, 7.2, 4.6, wall="sand", trim="terra_dark", accent=accent, door_faces=("W",), awning=None)
    house(F, "MarketHouse2", 25.6, 8.6, 6.2, 6.6, 3.5, wall="cream", trim="sand_dark", accent=accent, door_faces=("W",), roof_props=False, parapet_sides="SEW")
    stairs(F, "MarketStairs", 21.0, 12.6, 0, 1.4, 3.5, -90, step_h=0.25, step_d=0.3, wall_side=1)
    # arcade along the west side of the market street (walkable roof)
    arc = []
    G = F.sub(15.6, 14.0)
    for i in range(5):
        py = -6.0 + i * 3.0
        arc.append(G.box("pillar", (0.55, 0.55, 3.2), (-0.9, py, 1.6), "cream", bev=0.06))
        arc.append(G.box("pillar2", (0.55, 0.55, 3.2), (0.9, py, 1.6), "cream", bev=0.06))
    arc.append(G.box("slab", (2.6, 13.0, 0.35), (0, 0, 3.37), "sand_dark", bev=0.05, dense=1.6))
    arc.append(G.box("rail1", (0.2, 13.0, 0.8), (1.2, 0, 3.95), "sand", bev=0.04))
    prop("Arcade", arc)
    stairs(F, "ArcadeStairs", 15.6, 24.42, 0, 1.6, 3.55, 180, step_h=0.25, step_d=0.28)
    stall(F, "Stall1", 20.6, 16.5, 90, cloth)
    stall(F, "Stall2", 20.6, 9.0, 90, (cloth[1], "cream"))
    crate(F, "MarketCrate1", 19.5, 20.5, 0, 1.0, 15)
    crate(F, "MarketCrate2", 18.6, 6.0, 0, 1.2, -10)
    barrel(F, "MarketBarrel", 23.2, 13.0, 0, "teal")
    lamp(F, "MarketLamp", 18.2, 12.5)
    F.marker("PK_AMMO_market", (20.0, 12.7, 0.1))
    for i, p in enumerate([(19.0, 18.0, 0), (19.5, 7.5, 0), (15.6, 14.0, 3.6), (25.6, 8.6, 3.6)]):
        F.marker(f"ROAM_mkt{i}", p)

    # ---------------- main street + mid quadrant buildings -------------------------------
    house(F, "Inn", 9.2, 14.0, 7.6, 7.0, 5.0, wall="plaster", trim="terracotta", accent=accent, door_faces=("W", "S"),
          awning=(cloth[0], "cream"), parapet_sides="NSW")
    stairs(F, "InnStairs", 13.7, 10.6, 0, 1.4, 5.0, 0, step_h=0.25, step_d=0.3)
    # ruined wall + cover on the other side of main street
    wall_seg(F, "Ruin1", (-12.5, 16.5), (-6.0, 16.5), 2.6, 0.7, "sand_mid", "sand_dark")
    wall_seg(F, "Ruin2", (-6.0, 16.5), (-6.0, 12.5), 1.6, 0.7, "sand_mid", "sand_dark")
    sandbags(F, "RuinBags", -9.0, 11.0, 3.0, 3, 0)
    crate(F, "RuinCrate", -11.0, 13.8, 0, 1.2, 35)
    palm(F, "StreetPalm1", -4.7, 20.0, 0, 6.8, 9)
    palm(F, "StreetPalm2", 4.9, 21.5, 0, 6.0, 14)
    planter(F, "StreetPlanter", -4.8, 9.0, 2.2, 1.0, 90)
    lamp(F, "StreetLamp", 4.4, 9.5)
    low_wall(F, "MidCover1", 11.0, 4.8, 3.2, 0)
    low_wall(F, "MidCover2", -12.5, 6.0, 2.6, 90)
    barrel(F, "MidBarrel", 12.7, 4.6, 0, accent)
    bush(F, "MidBush", -12.8, 3.8, 0, 0.9)
    F.marker("PK_HEALTH_mid", (0.0, 3.4, 1.35))
    for i, p in enumerate([(0, 18, 0), (-9, 14, 0), (9, 9.5, 0), (9.2, 14, 5.1), (-12, 3, 0), (6, 4, 0)]):
        F.marker(f"ROAM_mid{i}", p)

    # FFA spawns around this half (outside the base)
    for i, (sx, sy, f) in enumerate([(-26.5, 3.0, 180), (26.5, 3.5, 180), (-14.0, 21.5, 90), (15.5, 3.0, 0), (-7.0, 22.0, 0), (7.0, 19.0, 0), (-22.0, 26.5, 0)]):
        F.marker(f"SP_FFA_{i:02d}", (sx, sy, 0.05), f)


def build_center():
    F = Frame()
    # raised plaza, stairs N/S, ramps E/W
    platform(F, "Plaza", 0, 0, 9.0, 9.0, 1.2, "concrete", "cream")
    stairs(F, "PlazaStairsN", 0, 4.5 + 1.5, 0, 2.6, 1.2, 180, step_h=0.24, step_d=0.3, side=False)
    stairs(F, "PlazaStairsS", 0, -4.5 - 1.5, 0, 2.6, 1.2, 0, step_h=0.24, step_d=0.3, side=False)
    ramp(F, "PlazaRampE", 4.5 + 3.6, 0, 0, 2.2, 3.6, 1.2, 90, "sand_dark")
    ramp(F, "PlazaRampW", -4.5 - 3.6, 0, 0, 2.2, 3.6, 1.2, -90, "sand_dark")
    fountain(F, "PlazaFountain", 0, 0, 1.2, 1.7)
    for (x, y) in ((3.4, -3.4), (-3.4, 3.4)):
        low_wall(F, f"PlazaWall{x:+.0f}", x, y, 2.2, 45, 0.95, "sand", "cream")
    for (x, y) in ((6.4, 6.4), (-6.4, -6.4), (-6.4, 6.4), (6.4, -6.4)):
        palm(F, f"PlazaPalm{x:+.0f}{y:+.0f}", x, y, 0, 6.6, 10)
    F.marker("ROAM_plaza", (0, 2.5, 1.3))
    F.marker("ROAM_plaza2", (0, -2.5, 1.3))


def build_perimeter():
    F = Frame()
    X, Y = 31.0, 37.0
    wall_seg(F, "PerimN", (-X - 0.5, Y), (X + 0.5, Y), 6.5, 1.0, "sand_mid", "sand_dark", crenel=True)
    wall_seg(F, "PerimS", (-X - 0.5, -Y), (X + 0.5, -Y), 6.5, 1.0, "sand_mid", "sand_dark", crenel=True)
    wall_seg(F, "PerimE", (X, -Y), (X, Y), 6.5, 1.0, "sand_mid", "sand_dark", crenel=True)
    wall_seg(F, "PerimW", (-X, -Y), (-X, Y), 6.5, 1.0, "sand_mid", "sand_dark", crenel=True)
    # buttresses
    for i in range(-3, 4):
        for (bx, by, rz) in ((X - 0.8, i * 10.0, 90), (-X + 0.8, i * 10.0, 90), (i * 8.5, Y - 0.8, 0), (i * 8.5, -Y + 0.8, 0)):
            if abs(by) > 34 and rz == 90:
                continue
            G = F.sub(bx, by, 0, rz)
            prop(f"Buttress{bx:+.0f}_{by:+.0f}", [G.box("bt", (0.9, 0.7, 5.2), (0, 0, 2.6), "sand_dark", bev=0.05, taper=(0.6, 0.8))])
    # corner towers
    for (cx, cy) in ((X, Y), (-X, Y), (X, -Y), (-X, -Y)):
        G = F.sub(cx, cy)
        prop(f"CornerTower{cx:+.0f}{cy:+.0f}", [G.box("tower", (3.2, 3.2, 8.2), (0, 0, 4.1), "sand", bev=0.06, dense=1.8),
                                                 G.box("tcap", (3.6, 3.6, 0.3), (0, 0, 8.3), "terracotta", bev=0.05)])


def ground():
    """Play-area floor: 1m grid so baked AO pools against walls; colored per face."""
    size_x, size_y = 66, 78
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=size_x, y_segments=size_y, size=1.0)
    bmesh.ops.scale(bm, vec=Vector((size_x / 2, size_y / 2, 1)), verts=bm.verts)
    ob = _from_bm("Ground", bm, "sand")
    for p in ob.data.polygons:
        p.use_smooth = False
    MESHES.append(ob)
    return ob


def paint_ground(ob):
    me = ob.data
    col = me.color_attributes["Col"]
    sand = hex_rgb(PAL["sand"]); sand2 = hex_rgb(PAL["sand_mid"]); sand3 = hex_rgb(PAL["sand_dark"])
    tileA = hex_rgb(PAL["cream"]); tileB = hex_rgb(PAL["concrete"]); path = hex_rgb("D8C397")
    for p in me.polygons:
        c = p.center
        x, y = c.x, c.y
        n = (math.sin(x * 0.37 + math.cos(y * 0.21) * 2.0) + math.sin(y * 0.29 - x * 0.13)) * 0.25 + 0.5
        n += R.uniform(-0.08, 0.08)
        rgb = [sand[i] * (1 - n) + sand2[i] * n for i in range(3)]
        # plaza tiles
        if abs(x) < 10.5 and abs(y) < 10.5:
            k = (math.floor(x / 1.5) + math.floor(y / 1.5)) % 2
            rgb = list(tileA if k else tileB)
            if abs(x) > 9.5 or abs(y) > 9.5:
                rgb = list(sand3)
        # main streets (paved strips) and yards
        elif abs(x) < 3.5 and abs(y) < 25.5:
            k = (math.floor(y / 1.0)) % 2
            rgb = [path[i] * (0.97 if k else 1.0) for i in range(3)]
        elif abs(y) > 25.5 and abs(x) < 13:
            k = (math.floor(x / 2.0) + math.floor(y / 2.0)) % 2
            rgb = [tileB[i] * (0.96 if k else 1.0) for i in range(3)]
        # random small pebbles / darker patches
        if R.random() < 0.03 and abs(x) > 3.5:
            rgb = list(sand3)
        for li in p.loop_indices:
            col.data[li].color_srgb = (rgb[0], rgb[1], rgb[2], 1.0)


def scenery():
    """Far dunes, mesas and roof silhouettes beyond the walls (render only)."""
    # dunes ring
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=40, y_segments=40, size=1.0)
    bmesh.ops.scale(bm, vec=Vector((260, 260, 1)), verts=bm.verts)
    for v in bm.verts:
        x, y = v.co.x, v.co.y
        d = max(abs(x) / 33.0, abs(y) / 39.0)
        h = 0.0 if d < 1.05 else (math.sin(x * 0.045) * math.cos(y * 0.05) * 3.5 + 2.5) * min(1.0, (d - 1.05) * 1.2)
        v.co.z = h - 0.05
    ob = _from_bm("NC_Dunes", bm, "sand_mid")
    for p in ob.data.polygons:
        p.use_smooth = True
    MESHES.append(ob)
    # mesas
    F = Frame()
    for i in range(14):
        a = i / 14 * math.tau + R.uniform(-0.15, 0.15)
        dist = R.uniform(110, 170)
        x, y = math.cos(a) * dist, math.sin(a) * dist
        G = F.sub(x, y, 0, R.uniform(0, 360))
        w = R.uniform(18, 34)
        h = R.uniform(10, 24)
        parts = [G.blob("m1", w * 0.6, (0, 0, h * 0.25), "terracotta", subdiv=1, jitter=0.15, scale=(1.3, 1.0, 0.45)),
                 G.box("m2", (w, w * 0.7, h * 0.55), (0, 0, h * 0.45), "terra_dark", bev=0.6, taper=(0.82, 0.82)),
                 G.box("m3", (w * 0.8, w * 0.55, h * 0.3), (R.uniform(-2, 2), 0, h * 0.85), "sand_dark", bev=0.5, taper=(0.85, 0.85)),
                 G.box("m4", (w * 0.62, w * 0.42, h * 0.12), (0, 0, h * 1.04), "rock", bev=0.4)]
        prop(f"Mesa{i}", parts, nc=True)
    # palm clusters and rooftops just outside the walls
    for i in range(18):
        a = i / 18 * math.tau
        x, y = math.cos(a) * 44, math.sin(a) * 50
        palm(F, f"NC_FarPalm{i}", x + R.uniform(-3, 3), y + R.uniform(-3, 3), 0, R.uniform(7, 10), 12, nc_crown=True, far=True)
    for i, (x, y, w, d, h) in enumerate([(-20, 44, 8, 6, 9), (6, 46, 10, 7, 11), (24, 43, 7, 6, 8.5), (40, 18, 6, 9, 9), (39, -12, 7, 8, 10),
                                         (20, -44, 8, 6, 9), (-6, -46, 10, 7, 11), (-24, -43, 7, 6, 8.5), (-40, -18, 6, 9, 9), (-39, 12, 7, 8, 10)]):
        G = F.sub(x, y)
        prop(f"FarHouse{i}", [G.box("fh", (w, d, h), (0, 0, h / 2), "sand" if i % 2 else "cream", bev=0.1),
                               G.box("fr", (w + 0.3, d + 0.3, 0.3), (0, 0, h + 0.15), "terracotta" if i % 3 else "sand_dark", bev=0.06)], nc=True)


def cameras_and_showcase():
    F = Frame()

    def cam(name, pos, look):
        d = Vector(look) - Vector(pos)
        # Unity camera forward = local +Z, which maps to Blender local -Y (axis conversion);
        # build a rotation whose -Y points along d with Z up.
        q = (-d).to_track_quat("Y", "Z")
        e = empty(name, pos, (0, 0, 0))
        e.rotation_mode = "QUATERNION"
        e.rotation_quaternion = q
        MARKERS.append(e)

    path = [((-24, -30, 9), (0, 0, 1)), ((-28, 4, 7), (-10, 12, 2)), ((-10, 30, 6), (8, 18, 3)), ((18, 34, 11), (0, 0, 0)),
            ((26, 0, 6), (8, -8, 2)), ((12, -26, 4), (0, -4, 2)), ((-8, -14, 3), (0, 6, 2))]
    for i, (p, l) in enumerate(path):
        cam(f"CAM_{i}", p, l)
    # showcase trooper stands in the north yard facing the gate (south)
    F.marker("SHOWCASE", (-1.6, 29.0, 0.0), 25)
    cam("SHOWCASE_CAM", (-1.2, 25.9, 1.65), (-1.75, 29.0, 1.15))


def main():
    reset()
    north = Frame(tag="_N")
    south = Frame(0, 0, 0, 180, tag="_S")
    build_half(north, "teal", ("teal", "cream"), "BLUE")
    build_half(south, "coral", ("coral", "cream"), "RED")
    build_center()
    build_perimeter()
    g = ground()
    scenery()
    cameras_and_showcase()

    print(f"[salvo] map meshes: {len(MESHES)} tris: {tri_count(MESHES)}")
    for o in MESHES:
        apply_mods(o)
        palette_to_vcolor(o)
    paint_ground(g)
    # AO: everything that sits in the play space (far scenery gets a lighter, local bake)
    play = [o for o in MESHES if not o.name.startswith(("NC_Mesa", "NC_FarHouse", "NC_Dunes", "FarPalm", "NC_FarPalm"))]
    far = [o for o in MESHES if o not in play]
    if not os.environ.get("SALVO_FAST"):
        bake_ao(play, distance=2.4, samples=20, strength=0.6)
        bake_ao(far, distance=6.0, samples=8, strength=0.35)

    root = empty("Outpost")
    for o in MESHES + MARKERS:
        if o.parent is None:
            set_parent(o, root)
    export_fbx([root], "Map/Outpost.fbx")


if __name__ == "__main__":
    main()
