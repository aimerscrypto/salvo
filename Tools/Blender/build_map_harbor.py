"""
HARBOR - a whitewashed Aegean fishing town on a stone quay between two sea inlets.
Point-symmetric (180deg about the origin) for fair Team Deathmatch. Falling into the sea is lethal
(MapInfo.killHeight). Reuses the Outpost kit; same naming contract (NC_, SP_, ROAM_, CAM_, PAD_, PK_).

Blender axes: X east, Y north, Z up.  North half: west inlet X[-31,-19] Y[4,31].
"""
import os
import sys
import math
sys.path.append(os.path.dirname(__file__))
import build_map_outpost as K
from build_map_outpost import Frame, prop, crate, barrel, palm, bush, planter, wall_seg, house, stairs, stall, lamp, fountain, platform, low_wall, pad, densify, awning_parts
from salvo_lib import *  # noqa
from salvo_lib import _from_bm
import bmesh
from mathutils import Vector

R = K.R
INLET_X = -19.0


# =====================================================================================
# kit
# =====================================================================================
def boat(F, name, x, y, rz, hull="hull_red"):
    G = F.sub(x, y, 0, rz)
    L, W = 7.2, 2.6
    plan = [(-W / 2, -L / 2), (W / 2, -L / 2), (W / 2, L / 2 - 1.6), (0.0, L / 2), (-W / 2, L / 2 - 1.6)]
    parts = [prism("hull", plan, 1.5, G.p(0, 0, -0.3), "white", rot=G.r((0, 0, 0)), bev=0.08, axis="Z"),
             prism("band", [(px * 1.02, py * 1.01) for px, py in plan], 0.35, G.p(0, 0, -0.75), hull, rot=G.r((0, 0, 0)), bev=0.03, axis="Z"),
             G.box("deck", (W - 0.3, L - 1.8, 0.1), (0, -0.4, 0.42), "wood_light", bev=0.02),
             G.box("cabin", (1.9, 2.0, 1.5), (0, -1.9, 1.2), "white", bev=0.08),
             G.box("cabin_roof", (2.1, 2.3, 0.15), (0, -1.9, 2.0), "aegean", bev=0.04),
             G.box("window", (1.95, 0.08, 0.45), (0, -0.88, 1.45), "navy", bev=0.02),
             G.cyl("mast", 0.07, 4.2, (0, 0.6, 2.5), "wood_dark", verts=8, bev=0.0)]
    for i in range(4):
        parts.append(G.cyl(f"fender{i}", 0.16, 0.5, (W / 2 + 0.12, -2.4 + i * 1.4, 0.15), "white", verts=8, bev=0.04))
    body = prop(name, parts)
    deco = [G.box("crate", (0.6, 0.5, 0.4), (0.5, 1.4, 0.67), "aegean", bev=0.04),
            G.box("net", (0.9, 0.7, 0.25), (-0.5, 1.8, 0.6), "net", bev=0.1),
            G.cyl("buoy", 0.18, 0.3, (-0.8, -0.2, 0.62), "coral", verts=8, bev=0.05)]
    prop(name + "_deco", deco, nc=True)
    return body


def pier(F, name, x0, y, length, width, rz=0.0):
    """Wooden pier starting at the quay (x0) running along local -X."""
    G = F.sub(x0, y, 0, rz)
    parts = [G.box("deck", (length, width, 0.16), (-length / 2, 0, -0.08), "wood", bev=0.03)]
    n = int(length / 0.5)
    for i in range(n):
        parts.append(G.box("plank", (0.04, width + 0.04, 0.03), (-0.25 - i * 0.5, 0, 0.01), "wood_dark", bev=0.0))
    for i in range(int(length / 2.5) + 1):
        for sy in (-1, 1):
            parts.append(G.cyl("post", 0.12, 2.6, (-0.3 - i * 2.5, sy * (width / 2 - 0.05), -1.25), "wood_dark", verts=8, bev=0.02))
    body = prop(name, parts)
    G2 = G
    for i in range(int(length / 3.5)):
        prop(f"{name}_bollard{i}", [G2.cyl("b", 0.14, 0.45, (-1.5 - i * 3.5, width / 2 - 0.25, 0.22), "dark", verts=10, bev=0.04),
                                    G2.cyl("bt", 0.19, 0.08, (-1.5 - i * 3.5, width / 2 - 0.25, 0.46), "dark", verts=10, bev=0.03)])
    return body


def quay_edge(F, name, a, b):
    """Stone quay wall dropping into the water along segment a->b."""
    ax, ay = a
    bx, by = b
    L = math.hypot(bx - ax, by - ay)
    ang = math.degrees(math.atan2(by - ay, bx - ax))
    G = F.sub((ax + bx) / 2, (ay + by) / 2, 0, ang)
    parts = [G.box("wall", (L, 1.0, 2.6), (0, 0, -1.3), "stone_dark", bev=0.06, dense=1.8),
             G.box("lip", (L + 0.05, 1.15, 0.22), (0, 0.0, 0.0), "stone", bev=0.05)]
    body = prop(name, parts)
    n = int(L / 4.5)
    for i in range(n):
        px = -L / 2 + 2.25 + i * 4.5
        prop(f"{name}_bol{i}", [G.cyl("b", 0.16, 0.5, (px, 0.1, 0.35), "dark", verts=10, bev=0.04),
                                G.cyl("bt", 0.22, 0.08, (px, 0.1, 0.62), "dark", verts=10, bev=0.03)])
    return body


def dome_chapel(F, name, x, y, z=0.0):
    G = F.sub(x, y, z)
    parts = [G.box("body", (4.2, 4.2, 3.6), (0, 0, 1.8), "whitewash", bev=0.08, dense=1.6),
             G.box("drum", (3.0, 3.0, 0.6), (0, 0, 3.9), "whitewash", bev=0.06),
             G.box("door", (1.1, 0.12, 2.0), (0, -2.12, 1.0), "aegean", bev=0.03),
             G.box("bell_arch", (1.6, 0.5, 1.8), (1.0, 2.0, 4.5), "whitewash", bev=0.06)]
    body = prop(name, parts)
    dome = [G.sphere("dome", 1.55, (0, 0, 4.2), "aegean", segs=14, rings=9, scale=(1, 1, 0.85)),
            G.cyl("cross_v", 0.05, 0.7, (0, 0, 5.75), "white", verts=6, bev=0.0),
            G.box("cross_h", (0.4, 0.06, 0.06), (0, 0, 5.85), "white", bev=0.0),
            G.sphere("bell", 0.25, (1.0, 2.0, 4.6), "brass", segs=8, rings=6)]
    prop(name + "_dome", dome, nc=True)
    return body


def town_house(F, name, cx, cy, w, d, h, accent="aegean", rz=0.0, door_faces=("S",), parapet_sides="NSEW", roof=True, z=0.0):
    G = F.sub(0, 0, z)
    return house(G, name, cx, cy, w, d, h, wall="whitewash", trim="stone", accent=accent, rz=rz,
                 door_faces=door_faces, parapet_sides=parapet_sides, roof_props=roof)


def trap_stack(F, name, x, y, rz=0.0):
    G = F.sub(x, y, 0, rz)
    parts = []
    for i, (px, py, pz) in enumerate([(0, 0, 0), (0.75, 0.05, 0), (0.35, 0.0, 0.5)]):
        parts.append(G.box("trap", (0.7, 0.55, 0.5), (px, py, pz + 0.25), "net" if i % 2 else "wood_light", bev=0.04))
        parts.append(G.box("trapband", (0.72, 0.57, 0.06), (px, py, pz + 0.25), "rope", bev=0.0))
    return prop(name, parts)


# =====================================================================================
# layout
# =====================================================================================
def build_half(F, accent, cloth, team):
    # ---------------- perimeter town rows ------------------------------------------------
    for i, (cx, cy, w, d, h, acc) in enumerate([(-14, 34.5, 8, 6, 6.5, accent), (-5, 35.0, 9, 7, 7.5, "teal"), (5, 34.5, 8, 6, 6.0, accent),
                                                (14, 35.0, 9, 7, 8.0, "coral"), (24, 34.5, 10, 6, 6.5, accent)]):
        town_house(F, f"RowN{i}", cx, cy, w, d, h, acc, door_faces=("S",), roof=i % 2 == 0)
    for i, (cy, d, h) in enumerate([(4.5, 9, 6.0), (13.5, 8, 7.0), (22.5, 9, 6.5), (30.0, 5, 7.5)]):
        town_house(F, f"RowE{i}", 34.5, cy, 6, d, h, accent if i % 2 else "teal", door_faces=("W",), roof=i % 2 == 1)

    # ---------------- spawn plaza -------------------------------------------------------
    fountain(F, "PlazaFountain", 0, 25.5, 0, 1.5)
    for (x, y) in ((-7, 29.5), (7, 29.5)):
        planter(F, f"PlazaPlanter{x:+.0f}", x, y, 2.4, 1.0)
    palm(F, "PlazaPalm1", -16.5, 30.0, 0, 6.5, 9)
    palm(F, "PlazaPalm2", 11.0, 23.0, 0, 6.0, 11)
    lamp(F, "PlazaLamp1", -4.0, 21.0)
    lamp(F, "PlazaLamp2", 4.5, 29.8)
    crate(F, "PlazaCrate", 9.0, 30.0, 0, 1.1, 15)
    for i, (sx, sy) in enumerate([(-6, 27.5), (-2.5, 29.5), (2.5, 29.5), (6, 27.5), (-9.5, 24), (9.5, 26.5), (-3, 22.5), (3, 22.5)]):
        F.marker(f"SP_{team}_{i:02d}", (sx, sy, 0.05), 0)

    # ---------------- west quay + piers + boat -------------------------------------------
    quay_edge(F, "QuayW", (INLET_X, 4.0), (INLET_X, 31.5))
    quay_edge(F, "QuayS", (-31.5, 4.0), (INLET_X, 4.0))
    pier(F, "Pier1", INLET_X - 0.4, 11.0, 10.0, 3.0)
    pier(F, "Pier2", INLET_X - 0.4, 24.0, 8.0, 2.6)
    boat(F, "Boat", -25.5, 21.2, 90)
    trap_stack(F, "Traps1", -16.5, 19.0, 10)
    trap_stack(F, "Traps2", -16.8, 8.0, -20)
    crate(F, "QuayCrate1", -15.0, 13.5, 0, 1.2, 8, "aegean")
    crate(F, "QuayCrate2", -15.2, 14.9, 0, 1.0, -12, "wood")
    crate(F, "QuayCrate3", -15.0, 14.1, 1.2, 0.9, 30, "aegean")
    barrel(F, "QuayBarrel1", -16.8, 27.5, 0, "aegean")
    barrel(F, "QuayBarrel2", -16.0, 28.1, 0, "white")
    low_wall(F, "QuayWall", -13.0, 22.0, 3.0, 90, 1.05, "whitewash", "stone")
    town_house(F, "Boathouse", -12.0, 28.0, 6.0, 4.5, 3.8, accent, door_faces=("W",), roof=False, parapet_sides="NSW")
    stairs(F, "BoathouseStairs", -4.5, 26.6, 0, 1.3, 3.8, 90, step_h=0.25, step_d=0.3)
    F.marker("PK_HEALTH_boat", (-26.0, 21.2, 0.55))
    for i, p in enumerate([(-24.0, 11.0, 0.1), (-26.5, 21.2, 0.5), (-16.0, 17.0, 0), (-12.0, 28.0, 3.9), (-15.0, 6.0, 0)]):
        F.marker(f"ROAM_quay{i}", p)

    # ---------------- market street ------------------------------------------------------
    stall(F, "Stall1", -6.0, 15.5, 0, cloth)
    stall(F, "Stall2", 6.5, 13.0, 180, (cloth[1], "aegean"))
    barrel(F, "MarketBarrel1", -2.2, 17.8, 0, "aegean")
    barrel(F, "MarketBarrel2", 2.6, 10.8, 0, "white", tipped=True)
    crate(F, "MarketCrate1", 10.5, 17.5, 0, 1.2, 20)
    crate(F, "MarketCrate2", -10.5, 10.5, 0, 1.1, -10)
    planter(F, "MarketPlanter", 12.5, 9.0, 2.2, 1.0, 90)
    low_wall(F, "MarketWall", -12.5, 15.0, 3.2, 0, 1.05, "whitewash", "stone")
    # extra cover to break long sightlines
    town_house(F, "Kiosk", -12.0, 7.5, 3.2, 3.0, 2.8, accent, door_faces=("E",), roof=False)
    G = F.sub(-3.5, 9.0, 0, 15)
    prop("Cart", [G.box("bed", (1.6, 2.6, 0.5), (0, 0, 0.75), "wood", bev=0.04),
                  G.box("side1", (0.1, 2.6, 0.45), (0.75, 0, 1.2), "wood_dark", bev=0.02),
                  G.box("side2", (0.1, 2.6, 0.45), (-0.75, 0, 1.2), "wood_dark", bev=0.02),
                  G.cyl("wheel1", 0.45, 0.12, (0.86, 0.3, 0.45), "wood_dark", rot=(0, 90, 0), verts=12, bev=0.02),
                  G.cyl("wheel2", 0.45, 0.12, (-0.86, 0.3, 0.45), "wood_dark", rot=(0, 90, 0), verts=12, bev=0.02),
                  G.box("shaft", (1.0, 1.8, 0.08), (0, -2.0, 0.7), "wood_dark", bev=0.02)])
    prop("CartLoad", [G.box("fish", (1.3, 2.2, 0.35), (0, 0, 1.15), "aegean", bev=0.1)], nc=True)
    trap_stack(F, "Traps3", 5.0, 21.0, 30)
    crate(F, "CoverCrate1", 12.5, 22.5, 0, 1.2, 10)
    crate(F, "CoverCrate2", 13.6, 23.2, 0, 1.0, -25)
    barrel(F, "CoverBarrel", 12.2, 23.9, 0, "aegean")
    low_wall(F, "CoverWall", 9.0, 4.0, 2.8, 0, 1.05, "whitewash", "stone")
    F.marker("PK_AMMO_market", (0.0, 14.0, 0.1))
    for i, p in enumerate([(0, 18, 0), (-8, 12, 0), (8, 16, 0), (12, 6, 0)]):
        F.marker(f"ROAM_mkt{i}", p)

    # ---------------- east terrace -------------------------------------------------------
    T = F.sub(0, 0, 2.6)
    platform(F, "Terrace", 26.0, 13.0, 10.0, 14.0, 2.6, "stone", "whitewash")
    stairs(F, "TerraceStairs", 18.0, 9.0, 0, 1.5, 2.6, -90, step_h=0.26, step_d=0.3)
    low_wall(T, "TerraceRail1", 21.2, 15.5, 9.0, 90, 0.95, "whitewash", "stone")
    low_wall(T, "TerraceRail2", 21.2, 7.0, 2.0, 90, 0.95, "whitewash", "stone")
    dome_chapel(F, "Chapel", 27.5, 9.5, 2.6)
    town_house(F, "TerraceHouse", 27.5, 17.5, 5.0, 5.0, 3.6, accent, door_faces=("W",), z=2.6, parapet_sides="NSE")
    stairs(F, "TerraceHouseStairs", 24.25, 15.2, 2.6, 1.3, 3.6, 0, step_h=0.24, step_d=0.3)
    bush(T, "TerraceBush", 23.5, 19.0, 0, 0.8)
    pad(F, "terrace", 13.5, 15.5, 90, 14.0, 9.0)
    F.marker("PK_AMMO_terrace", (24.0, 11.0, 2.7))
    for i, p in enumerate([(25.0, 13.0, 2.7), (27.5, 17.5, 6.3), (16.5, 21.0, 0), (19.0, 3.0, 0)]):
        F.marker(f"ROAM_ter{i}", p)

    # FFA spawns (outside the plaza)
    for i, (sx, sy, f) in enumerate([(-17.0, 6.0, 180), (16.0, 4.0, 0), (-10.0, 20.0, 0), (17.0, 22.0, 90), (24.0, 13.0, 90), (-6.0, 9.0, 0), (12.0, 28.5, 0)]):
        F.marker(f"SP_FFA_{i:02d}", (sx, sy, 0.05 if f != 90 or sx < 20 else 2.65), f)


def build_center():
    F = Frame()
    # fish-market hall: open sides, pillars, gable roof, central catwalk
    parts = []
    for sx in (-6.5, 6.5):
        for i in range(5):
            py = -6.0 + i * 3.0
            parts.append(F.box("pillar", (0.5, 0.5, 5.2), (sx, py, 2.6), "whitewash", bev=0.06))
    parts.append(F.box("beamE", (0.6, 12.6, 0.5), (6.5, 0, 5.35), "wood_dark", bev=0.04))
    parts.append(F.box("beamW", (0.6, 12.6, 0.5), (-6.5, 0, 5.35), "wood_dark", bev=0.04))
    prop("Hall", parts)
    roof = [F.wedge("roofE", (13.0, 7.6, 1.6), (3.5, 0, 6.4), "terracotta", rot=(0, 0, -90), bev=0.06),
            F.wedge("roofW", (13.0, 7.6, 1.6), (-3.5, 0, 6.4), "terracotta", rot=(0, 0, 90), bev=0.06),
            F.box("ridge", (0.4, 13.2, 0.25), (0, 0, 7.25), "terra_dark", bev=0.04)]
    prop("HallRoof", roof)
    cat = [F.box("catwalk", (1.8, 11.4, 0.2), (0, 0, 3.0), "wood", bev=0.04)]
    for sx in (-1, 1):
        cat.append(F.box("rail", (0.08, 11.4, 0.08), (sx * 0.86, 0, 4.0), "dark", bev=0.0))
        for i in range(7):
            cat.append(F.box("rpost", (0.08, 0.08, 1.0), (sx * 0.86, -5.4 + i * 1.8, 3.5), "dark", bev=0.0))
    for py in (-3.0, 3.0):
        cat.append(F.box("hang", (0.12, 0.12, 2.2), (0, py, 4.2), "dark", bev=0.0))
    prop("Catwalk", cat)
    stairs(F, "CatStairsN", 0, 9.3, 0, 1.4, 3.1, 180, step_h=0.26, step_d=0.32)
    stairs(F, "CatStairsS", 0, -9.3, 0, 1.4, 3.1, 0, step_h=0.26, step_d=0.32)
    for (x, y, rz) in ((-3.5, 2.5, 0), (3.5, -2.5, 0), (-3.5, -3.0, 90), (3.5, 3.0, 90)):
        G = F.sub(x, y, 0, rz)
        prop(f"FishTable{x:+.0f}{y:+.0f}", [G.box("t", (2.6, 1.1, 0.95), (0, 0, 0.475), "stone", bev=0.05),
                                             G.box("ice", (2.3, 0.9, 0.08), (0, 0, 0.98), "glass", bev=0.02)])
    F.marker("PK_HEALTH_hall", (0.0, 0.0, 3.15))
    F.marker("ROAM_hall", (0, 0, 3.15))
    F.marker("ROAM_hall2", (-3, 0, 0))
    F.marker("ROAM_hall3", (3, 0, 0))
    for (x, y) in ((10.0, 1.5), (-10.0, -1.5)):
        barrel(F, f"MidBarrel{x:+.0f}", x, y, 0, "aegean")
    for (x, y) in ((11.5, -3.0), (-11.5, 3.0)):
        crate(F, f"MidCrate{x:+.0f}", x, y, 0, 1.2, 25)


def ground():
    sx, sy = 64, 64
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=sx, y_segments=sy, size=1.0)
    bmesh.ops.scale(bm, vec=Vector((sx / 2, sy / 2, 1)), verts=bm.verts)
    dead = []
    for f in bm.faces:
        c = f.calc_center_median()
        if (c.x < INLET_X - 0.5 and 4.0 < c.y < 33) or (c.x > -INLET_X + 0.5 and -33 < c.y < -4.0):
            dead.append(f)
    bmesh.ops.delete(bm, geom=dead, context="FACES")
    ob = _from_bm("Ground", bm, "stone")
    for p in ob.data.polygons:
        p.use_smooth = False
    K.MESHES.append(ob)
    return ob


def paint_ground(ob):
    me = ob.data
    col = me.color_attributes["Col"]
    a = hex_rgb(PAL["stone"]); b = hex_rgb(PAL["whitewash"]); c2 = hex_rgb(PAL["stone_dark"]); s = hex_rgb(PAL["sand_mid"])
    for p in me.polygons:
        x, y = p.center.x, p.center.y
        k = (math.floor(x / 1.0) + math.floor(y / 1.0)) % 2
        rgb = [a[i] * (0.97 if k else 1.0) for i in range(3)]
        if abs(y) > 20 and abs(x) < 16:               # plazas: big pale slabs
            k2 = (math.floor(x / 2.0) + math.floor(y / 2.0)) % 2
            rgb = list(b) if k2 else [b[i] * 0.95 for i in range(3)]
        elif abs(x) < 7.5 and abs(y) < 7.5:            # hall floor
            rgb = [c2[i] * (0.98 if k else 1.0) for i in range(3)]
        elif abs(x) < 3 and 7.5 <= abs(y) <= 20:      # pale path to the hall
            rgb = list(b)
        if R.random() < 0.025:
            rgb = list(s)
        for li in p.loop_indices:
            col.data[li].color_srgb = (rgb[0], rgb[1], rgb[2], 1.0)


def scenery():
    F = Frame()
    # the sea
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=2, y_segments=2, size=1.0)
    bmesh.ops.scale(bm, vec=Vector((420, 420, 1)), verts=bm.verts)
    sea = _from_bm("NC_Sea", bm, "sea")
    sea.location.z = -1.0
    K.MESHES.append(sea)
    # hillside towns rising behind the east and west rows (Santorini skyline)
    for side in (1, -1):
        for i in range(14):
            x = side * (40 + (i % 5) * 7 + R.uniform(-1, 1))
            y = side * (-30 + (i // 5) * 22 + R.uniform(-4, 4)) + side * 10
            z = (abs(x) - 38) * 0.55
            G = F.sub(x, y, z)
            w, d, h = R.uniform(5, 8), R.uniform(5, 8), R.uniform(4, 7)
            prop(f"FarHouse{side}_{i}", [G.box("fh", (w, d, h + z), (0, 0, (h - z) / 2), "whitewash", bev=0.12),
                                         G.box("fr", (w + 0.3, d + 0.3, 0.3), (0, 0, h + 0.15), "stone", bev=0.06)], nc=True)
            if i % 4 == 0:
                prop(f"FarDome{side}_{i}", [G.sphere("d", min(w, d) * 0.3, (0, 0, h + 0.3), "aegean", segs=12, rings=8, scale=(1, 1, 0.85))], nc=True)
        # far enough out that no part of it rises inside the playable area or over the inlets
        # (players read it as walkable land and fell through into the water)
        G = F.sub(side * 66, side * 5)
        prop(f"Hill{side}", [G.blob("h", 30, (0, 0, -6), "rock", subdiv=2, jitter=0.12, scale=(1.0, 1.6, 0.45)),
                             G.blob("h2", 22, (side * 8, 12, -2), "sand_dark", subdiv=2, jitter=0.12, scale=(1.0, 1.4, 0.5))], nc=True)
    # lighthouse islet + distant islands
    G = F.sub(-70, 46)
    prop("Lighthouse", [G.blob("rock", 7, (0, 0, -1.5), "rock_dark", subdiv=2, jitter=0.18, scale=(1.3, 1.0, 0.5)),
                        G.cyl("tower", 1.6, 12, (0, 0, 7), "white", verts=12, bev=0.05, r2=1.2),
                        G.cyl("band1", 1.56, 1.2, (0, 0, 5), "red", verts=12, bev=0.0),
                        G.cyl("band2", 1.4, 1.2, (0, 0, 9.5), "red", verts=12, bev=0.0),
                        G.cyl("lamp", 1.3, 1.4, (0, 0, 13.7), "glass", verts=12, bev=0.04),
                        G.cyl("cap", 1.6, 1.0, (0, 0, 15), "aegean", verts=12, bev=0.05, r2=0.3)], nc=True)
    G = F.sub(70, -46)
    prop("Islet", [G.blob("rock", 9, (0, 0, -2), "rock", subdiv=2, jitter=0.18, scale=(1.4, 1.0, 0.5)),
                   G.blob("green", 6, (2, 1, 1), "leaf_dark", subdiv=1, jitter=0.2, scale=(1.2, 1.0, 0.5))], nc=True)
    for i in range(8):
        a = i / 8 * math.tau + 0.3
        G = F.sub(math.cos(a) * 190, math.sin(a) * 190, 0, R.uniform(0, 360))
        prop(f"FarIsland{i}", [G.blob("i", R.uniform(18, 30), (0, 0, -6), "rock" if i % 2 else "sand_dark", subdiv=2, jitter=0.15, scale=(1.6, 1.0, 0.4))], nc=True)
    # sailboats
    for i, (x, y, rz) in enumerate([(-55, 20, 30), (-48, 60, -20), (60, -25, 200), (52, -62, 170)]):
        G = F.sub(x, y, -0.8, rz)
        prop(f"Sailboat{i}", [G.box("hull", (1.6, 5.0, 0.8), (0, 0, 0), "white", bev=0.2, taper=(1.0, 1.0)),
                              G.cyl("mast", 0.06, 6.0, (0, 0.4, 3.2), "wood_dark", verts=6, bev=0.0),
                              G.wedge("sail", (0.05, 2.6, 5.0), (0, -0.9, 3.4), "cream", rot=(0, 0, 180), bev=0.0)], nc=True)


def cameras_and_showcase():
    F = Frame()

    def cam(name, pos, look):
        d = Vector(look) - Vector(pos)
        q = (-d).to_track_quat("Y", "Z")
        e = empty(name, pos, (0, 0, 0))
        e.rotation_mode = "QUATERNION"
        e.rotation_quaternion = q
        K.MARKERS.append(e)

    path = [((-30, 30, 9), (-12, 14, 1)), ((-22, 2, 3.5), (-6, 4, 2)), ((-4, 12, 2.6), (2, -2, 3)), ((14, 26, 7), (22, 12, 3)),
            ((28, -2, 6), (8, -8, 2)), ((8, -26, 4), (0, -14, 2))]
    for i, (p, l) in enumerate(path):
        cam(f"CAM_{i}", p, l)
    F.marker("SHOWCASE", (-1.6, 23.0, 0.0), 25)
    cam("SHOWCASE_CAM", (-1.2, 19.9, 1.65), (-1.75, 23.0, 1.15))


def main():
    reset()
    north = Frame(tag="_N")
    south = Frame(0, 0, 0, 180, tag="_S")
    build_half(north, "aegean", ("aegean", "white"), "BLUE")
    build_half(south, "teal", ("coral", "white"), "RED")
    build_center()
    g = ground()
    scenery()
    cameras_and_showcase()
    meshes = K.MESHES
    print(f"[salvo] harbor meshes: {len(meshes)} tris: {tri_count(meshes)}")
    for o in meshes:
        apply_mods(o)
        palette_to_vcolor(o)
    paint_ground(g)
    far_prefix = ("NC_Sea", "NC_FarHouse", "NC_FarDome", "NC_Hill", "NC_Lighthouse", "NC_Islet", "NC_FarIsland", "NC_Sailboat")
    play = [o for o in meshes if not o.name.startswith(far_prefix)]
    far = [o for o in meshes if o not in play and o.name != "NC_Sea"]
    if not os.environ.get("SALVO_FAST"):
        bake_ao(play, distance=2.4, samples=20, strength=0.6)
        bake_ao(far, distance=6.0, samples=8, strength=0.35)
    root = empty("Harbor")
    for o in meshes + K.MARKERS:
        if o.parent is None:
            set_parent(o, root)
    export_fbx([root], "Map/Harbor.fbx")


if __name__ == "__main__":
    main()
