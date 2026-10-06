"""
SALVO characters (Blender axes: X right, Y forward, Z up -> Unity wraps the model in a 180deg yaw).

Trooper hierarchy (every node's pivot sits on its joint, so Unity can animate procedurally
with plain localRotation - no skinned meshes, no Animator, near-zero CPU cost):

  Trooper
    Hips
      Torso
        Head        (+ "Eye" marker)
        UpperArm_L > LowerArm_L (+ "Hand_L")
        UpperArm_R > LowerArm_R (+ "Hand_R")
      Thigh_L > Shin_L
      Thigh_R > Shin_R

TEAM slot = vest, shoulder pads, helmet band -> recolored per team / FFA color.

FP_Arms: Arm_R / Arm_L, origin = hand grip point, used by the first-person view model.
"""
import os
import sys
sys.path.append(os.path.dirname(__file__))
from salvo_lib import *  # noqa


def limb(name, r1, r2, length, top, color, verts=10, bev=0.02):
    """Tapered cylinder hanging down from `top` (pivot at the top)."""
    c = cyl(name, r1, length, (top[0], top[1], top[2] - length / 2), color, verts=verts, r2=None, bev=bev)
    # taper: scale bottom ring
    me = c.data
    for v in me.vertices:
        if v.co.z < 0:
            v.co.x *= r2 / r1
            v.co.y *= r2 / r1
    return c


def trooper():
    reset()
    parts = {}

    # ---------------- hips / legs
    hips = [box("pelvis", (0.34, 0.22, 0.17), (0, 0, 0.93), "olive", bev=0.05),
            box("belt", (0.36, 0.24, 0.05), (0, 0, 1.0), "dark", bev=0.02),
            box("buckle", (0.06, 0.02, 0.04), (0, 0.125, 1.0), "brass", bev=0.008),
            box("pouch_l", (0.08, 0.06, 0.09), (-0.15, 0.08, 0.95), "khaki", bev=0.02),
            box("pouch_r", (0.08, 0.06, 0.09), (0.15, 0.08, 0.95), "khaki", bev=0.02),
            box("holster", (0.06, 0.1, 0.14), (0.2, -0.01, 0.9), "dark", bev=0.02)]
    parts["Hips"] = (hips, (0, 0, 0.95))

    for side, sx in (("L", -1), ("R", 1)):
        x = 0.105 * sx
        thigh = [limb(f"thigh_{side}", 0.085, 0.07, 0.44, (x, 0, 0.94), "olive"),
                 box(f"cargo_{side}", (0.05, 0.1, 0.12), (x + 0.07 * sx, 0.0, 0.72), "olive_dark", bev=0.02),
                 box(f"kneepad_{side}", (0.11, 0.06, 0.11), (x, 0.06, 0.5), "dark", bev=0.03)]
        parts[f"Thigh_{side}"] = (thigh, (x, 0, 0.93))
        shin = [limb(f"shin_{side}", 0.068, 0.058, 0.38, (x, 0, 0.5), "olive"),
                box(f"boot_{side}", (0.13, 0.25, 0.12), (x, 0.04, 0.065), "wood_dark", bev=0.04),
                box(f"sole_{side}", (0.135, 0.26, 0.03), (x, 0.04, 0.015), "rubber", bev=0.01),
                box(f"cuff_{side}", (0.14, 0.15, 0.06), (x, 0.0, 0.15), "wood_dark", bev=0.03)]
        parts[f"Shin_{side}"] = (shin, (x, 0, 0.5))

    # ---------------- torso
    torso = [box("chest", (0.38, 0.22, 0.38), (0, 0, 1.22), "olive", bev=0.06, taper=(1.08, 1.05)),
             box("vest_f", (0.36, 0.07, 0.3), (0, 0.1, 1.22), "TEAM", bev=0.03, taper=(1.05, 1.0)),
             box("vest_b", (0.36, 0.07, 0.32), (0, -0.1, 1.22), "TEAM", bev=0.03, taper=(1.05, 1.0)),
             box("strap_l", (0.06, 0.24, 0.04), (-0.12, 0, 1.39), "TEAM", bev=0.015),
             box("strap_r", (0.06, 0.24, 0.04), (0.12, 0, 1.39), "TEAM", bev=0.015),
             box("mag_pouch1", (0.08, 0.05, 0.1), (-0.1, 0.15, 1.15), "khaki", bev=0.02),
             box("mag_pouch2", (0.08, 0.05, 0.1), (0.0, 0.15, 1.15), "khaki", bev=0.02),
             box("mag_pouch3", (0.08, 0.05, 0.1), (0.1, 0.15, 1.15), "khaki", bev=0.02),
             box("radio", (0.07, 0.05, 0.12), (0.12, 0.14, 1.3), "dark", bev=0.015),
             cyl("antenna", 0.006, 0.2, (0.14, -0.14, 1.5), "dark", verts=6, bev=0.0),
             box("pack", (0.3, 0.14, 0.32), (0, -0.19, 1.2), "khaki", bev=0.05),
             box("pack_lid", (0.3, 0.15, 0.08), (0, -0.19, 1.37), "olive_dark", bev=0.03),
             box("pack_pocket", (0.18, 0.05, 0.12), (0, -0.27, 1.13), "olive_dark", bev=0.02),
             cyl("bedroll", 0.06, 0.32, (0, -0.19, 1.06), "cream", rot=(0, 90, 0), verts=10, bev=0.015),
             cyl("neck", 0.06, 0.1, (0, 0, 1.45), "skin", verts=10, bev=0.0),
             cyl("scarf", 0.095, 0.07, (0, 0.0, 1.43), "cream", verts=12, bev=0.02)]
    parts["Torso"] = (torso, (0, 0, 1.02))

    # ---------------- head
    head = [sphere("skull", 0.115, (0, 0.005, 1.6), "skin", segs=12, rings=8, scale=(1.0, 1.05, 1.08)),
            sphere("helmet", 0.14, (0, -0.005, 1.65), "olive", segs=14, rings=9, scale=(1.05, 1.12, 0.82)),
            cyl("helmet_rim", 0.15, 0.03, (0, -0.005, 1.6), "olive_dark", verts=14, bev=0.01),
            cyl("helmet_band", 0.147, 0.035, (0, -0.005, 1.64), "TEAM", verts=14, bev=0.008),
            box("goggles", (0.2, 0.05, 0.06), (0, 0.11, 1.62), "dark", bev=0.02),
            box("lens_l", (0.07, 0.02, 0.045), (-0.045, 0.135, 1.62), "GLASS", bev=0.012),
            box("lens_r", (0.07, 0.02, 0.045), (0.045, 0.135, 1.62), "GLASS", bev=0.012),
            sphere("mask", 0.1, (0, 0.04, 1.53), "cream", segs=12, rings=8, scale=(1.05, 1.0, 0.75)),
            box("ear_l", (0.03, 0.05, 0.06), (-0.12, 0.0, 1.58), "dark", bev=0.012),
            box("ear_r", (0.03, 0.05, 0.06), (0.12, 0.0, 1.58), "dark", bev=0.012)]
    parts["Head"] = (head, (0, 0, 1.5))

    # ---------------- arms (rest pose: hanging, slightly out)
    for side, sx in (("L", -1), ("R", 1)):
        x = 0.24 * sx
        upper = [limb(f"upper_{side}", 0.068, 0.058, 0.28, (x, 0, 1.4), "olive"),
                 sphere(f"shoulder_{side}", 0.085, (x, 0, 1.38), "TEAM", segs=10, rings=6, scale=(1.05, 1.05, 0.8)),
                 cyl(f"patch_{side}", 0.06, 0.02, (x + 0.065 * sx, 0, 1.3), "mustard", rot=(0, 90, 0), verts=10, bev=0.005)]
        parts[f"UpperArm_{side}"] = (upper, (x, 0, 1.38))
        lower = [limb(f"fore_{side}", 0.058, 0.048, 0.24, (x, 0, 1.12), "olive"),
                 cyl(f"cuff_{side}", 0.06, 0.05, (x, 0, 0.9), "olive_dark", verts=10, bev=0.01),
                 box(f"glove_{side}", (0.075, 0.1, 0.11), (x, 0.01, 0.82), "rubber", bev=0.03),
                 box(f"thumb_{side}", (0.035, 0.04, 0.06), (x - 0.04 * sx, 0.04, 0.84), "rubber", bev=0.012)]
        parts[f"LowerArm_{side}"] = (lower, (x, 0, 1.1))

    built = {}
    for name, (objs, pivot) in parts.items():
        ob = join(objs, name)
        set_origin(ob, pivot)
        built[name] = ob

    finalize(list(built.values()), ao=True, ao_distance=0.18, ground=0.0, ao_strength=0.5)

    root = empty("Trooper")
    hier = {"Hips": root, "Torso": built["Hips"], "Head": built["Torso"],
            "UpperArm_L": built["Torso"], "UpperArm_R": built["Torso"],
            "LowerArm_L": built["UpperArm_L"], "LowerArm_R": built["UpperArm_R"],
            "Thigh_L": built["Hips"], "Thigh_R": built["Hips"],
            "Shin_L": built["Thigh_L"], "Shin_R": built["Thigh_R"]}
    for name in ["Hips", "Torso", "Head", "UpperArm_L", "UpperArm_R", "LowerArm_L", "LowerArm_R",
                 "Thigh_L", "Thigh_R", "Shin_L", "Shin_R"]:
        set_parent(built[name], hier[name])
    empty("Eye", (0, 0.1, 1.62), parent=built["Head"])
    empty("Hand_L", (-0.24, 0.02, 0.82), parent=built["LowerArm_L"])
    empty("Hand_R", (0.24, 0.02, 0.82), parent=built["LowerArm_R"])
    print(f"[salvo] Trooper: {tri_count(built.values())} tris")
    export_fbx([root], "Characters/Trooper.fbx")


def fp_arms():
    """First-person arms. Each arm's origin is the grip point; the grip axis is local Z for
    the right hand (pistol grip) and local Y for the left hand (support hand under a handguard)."""
    reset()
    # ---- right hand: wraps a vertical grip at the origin; back of hand faces +X
    R = [box("palm", (0.03, 0.075, 0.09), (0.03, -0.005, 0.0), "rubber", bev=0.012),
         box("fingers", (0.07, 0.03, 0.085), (0.0, 0.045, -0.005), "rubber", bev=0.013),
         box("knuckle", (0.026, 0.05, 0.08), (0.037, 0.03, -0.004), "dark", bev=0.01),
         box("thumb", (0.024, 0.06, 0.026), (-0.03, 0.02, 0.04), "rubber", rot=(0, 0, 20), bev=0.01),
         box("index", (0.02, 0.05, 0.022), (-0.005, 0.06, 0.06), "rubber", rot=(0, 0, 0), bev=0.008)]
    # forearm going back/down/right from the wrist
    import math as _m
    wrist = Vector((0.03, -0.05, 0.03))
    d = Vector((0.22, -1.0, -0.45)).normalized()
    L = 0.42
    mid = wrist + d * (L / 2)
    rot = d.to_track_quat("Z", "Y").to_euler()
    deg = tuple(_m.degrees(a) for a in rot)
    R.append(cyl("wrist", 0.038, 0.06, tuple(wrist + d * 0.02), "rubber", rot=deg, verts=12, bev=0.01))
    R.append(cyl("cuff", 0.05, 0.05, tuple(wrist + d * 0.075), "olive_dark", rot=deg, verts=12, bev=0.012))
    R.append(cyl("sleeve", 0.058, L, tuple(mid + d * 0.08), "TEAM", rot=deg, verts=12, bev=0.012))
    R.append(cyl("band", 0.061, 0.035, tuple(wrist + d * 0.16), "olive_dark", rot=deg, verts=12, bev=0.008))
    arm_r = join(R, "Arm_R")
    set_origin(arm_r, (0, 0, 0))

    # ---- left hand: support grip under a horizontal handguard at origin (grip axis = Y)
    Lp = [box("palm", (0.075, 0.09, 0.03), (0.0, 0.0, -0.035), "rubber", bev=0.012),
          box("fingers", (0.03, 0.085, 0.07), (-0.045, 0.005, -0.005), "rubber", bev=0.013),
          box("knuckle", (0.026, 0.08, 0.05), (-0.05, 0.005, -0.02), "dark", bev=0.01),
          box("thumb", (0.024, 0.06, 0.026), (0.045, 0.02, -0.01), "rubber", rot=(0, 0, -15), bev=0.01)]
    wrist = Vector((0.0, -0.05, -0.04))
    d = Vector((-0.45, -1.0, -0.5)).normalized()
    mid = wrist + d * (L / 2)
    rot = d.to_track_quat("Z", "Y").to_euler()
    deg = tuple(_m.degrees(a) for a in rot)
    Lp.append(cyl("wrist", 0.038, 0.06, tuple(wrist + d * 0.02), "rubber", rot=deg, verts=12, bev=0.01))
    Lp.append(cyl("cuff", 0.05, 0.05, tuple(wrist + d * 0.075), "olive_dark", rot=deg, verts=12, bev=0.012))
    Lp.append(cyl("sleeve", 0.058, L, tuple(mid + d * 0.08), "TEAM", rot=deg, verts=12, bev=0.012))
    Lp.append(cyl("band", 0.061, 0.035, tuple(wrist + d * 0.16), "olive_dark", rot=deg, verts=12, bev=0.008))
    arm_l = join(Lp, "Arm_L")
    set_origin(arm_l, (0, 0, 0))
    arm_l.location.x -= 0.5  # keep apart while baking AO

    finalize([arm_r, arm_l], ao=True, ao_distance=0.05, ao_strength=0.5)
    arm_l.location.x += 0.5
    root = empty("FP_Arms")
    parent_all([arm_r, arm_l], root)
    print(f"[salvo] FP_Arms: {tri_count([arm_r, arm_l])} tris")
    export_fbx([root], "Characters/FP_Arms.fbx")


if __name__ == "__main__":
    which = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    for k, fn in {"trooper": trooper, "arms": fp_arms}.items():
        if not which or k in which:
            fn()
