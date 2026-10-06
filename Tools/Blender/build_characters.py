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

TEAM slot = combat shirt + sleeves, helmet band -> recolored per team / FFA color.
Hands (HandR_* / HandL_* variants, each with a "<name>_W" wrist marker) are posed for the
weapon grips; Unity snaps the active variant onto the weapon and IK-solves the arm to its wrist.

FP_Arms: Arm_R / Arm_R_<Weapon> / Arm_L_<Weapon>, authored in the frame of the grip marker
they attach to (see salvo_hands.py).
"""
import os
import sys
sys.path.append(os.path.dirname(__file__))
from salvo_lib import *  # noqa


def limb(name, r1, r2, length, top, color, verts=12):
    """Rounded tapered limb hanging down from `top` (smooth capsule, pivot set by the caller)."""
    from salvo_hands import capsule
    top = Vector(top)
    return capsule(name, top, top - Vector((0, 0, length)), r1, r2, color, verts=verts)


def joint(name, r, loc, color, scale=(1, 1, 1)):
    """Sphere that fills the gap at a bending joint (elbow, knee, shoulder)."""
    return sphere(name, r, loc, color, segs=12, rings=8, scale=scale)


# Third-person hand variants (same poses as first person, lighter mesh). Each hand's origin is
# its grip point; Unity snaps it onto the weapon marker and IK-solves the arm to its wrist.
TP_HANDS = {
    "R": ("HandR_Grip", "HandR_Fist"),
    "L": ("HandL_Guard", "HandL_Vert", "HandL_Pistol"),
}


def tp_hand_specs():
    from salvo_hands import (pose_pistol_grip, pose_fist, pose_support_guard,
                             pose_support_vertical, pose_support_pistol)
    return {"HandR_Grip": pose_pistol_grip(), "HandR_Fist": pose_fist(),
            "HandL_Guard": pose_support_guard(0.027, 0.027), "HandL_Vert": pose_support_vertical(),
            "HandL_Pistol": pose_support_pistol()}


def trooper():
    from salvo_hands import build_hand
    reset()
    parts = {}

    # ---------------- hips / legs (cargo pants, knee pads, boots)
    hips = [box("pelvis", (0.33, 0.22, 0.18), (0, 0, 0.93), "olive", bev=0.07),
            box("belt", (0.355, 0.245, 0.05), (0, 0, 1.0), "dark", bev=0.02),
            box("buckle", (0.06, 0.02, 0.04), (0, 0.126, 1.0), "brass", bev=0.008),
            box("pouch_l", (0.07, 0.07, 0.09), (-0.16, 0.06, 0.96), "khaki", bev=0.022),
            box("pouch_r", (0.07, 0.07, 0.09), (0.16, 0.06, 0.96), "khaki", bev=0.022),
            box("dump", (0.14, 0.07, 0.08), (0.0, -0.13, 0.95), "olive_dark", bev=0.025)]
    parts["Hips"] = (hips, (0, 0, 0.95))

    for side, sx in (("L", -1), ("R", 1)):
        x = 0.105 * sx
        thigh = [limb(f"thigh_{side}", 0.092, 0.072, 0.4, (x, 0, 0.95), "olive"),
                 box(f"cargo_{side}", (0.05, 0.11, 0.12), (x + 0.075 * sx, 0.0, 0.73), "olive_dark", bev=0.022),
                 box(f"flap_{side}", (0.054, 0.115, 0.03), (x + 0.077 * sx, 0.0, 0.79), "olive_dark", bev=0.01)]
        if side == "R":
            thigh += [box("holster", (0.05, 0.1, 0.15), (x + 0.085, -0.01, 0.8), "dark", bev=0.02),
                      box("holster_strap", (0.16, 0.16, 0.025), (x, 0, 0.74), "dark", bev=0.008)]
        parts[f"Thigh_{side}"] = (thigh, (x, 0, 0.93))
        shin = [joint(f"knee_{side}", 0.075, (x, 0.0, 0.52), "olive"),
                box(f"kneepad_{side}", (0.11, 0.07, 0.12), (x, 0.06, 0.5), "dark", bev=0.032),
                limb(f"shin_{side}", 0.07, 0.06, 0.32, (x, 0, 0.5), "olive"),
                cyl(f"bootcuff_{side}", 0.072, 0.13, (x, 0.0, 0.15), "boot", verts=12, bev=0.02),
                box(f"foot_{side}", (0.115, 0.2, 0.085), (x, 0.035, 0.062), "boot", bev=0.04, taper=(0.95, 0.85)),
                sphere(f"toe_{side}", 0.058, (x, 0.12, 0.05), "boot", segs=12, rings=6, scale=(1.0, 0.95, 0.72)),
                box(f"sole_{side}", (0.118, 0.25, 0.03), (x, 0.045, 0.016), "rubber", bev=0.013),
                box(f"laces_{side}", (0.05, 0.08, 0.02), (x, 0.08, 0.1), "glove_pad", rot=(-25, 0, 0), bev=0.006)]
        parts[f"Shin_{side}"] = (shin, (x, 0, 0.5))

    # ---------------- torso: team-colored combat shirt under a plate carrier
    torso = [box("chest", (0.34, 0.21, 0.36), (0, 0, 1.22), "TEAM", bev=0.07, taper=(1.14, 1.05)),
             box("plate_f", (0.32, 0.075, 0.27), (0, 0.095, 1.24), "khaki", bev=0.035, taper=(1.04, 1.0)),
             box("plate_b", (0.32, 0.075, 0.29), (0, -0.095, 1.24), "khaki", bev=0.035, taper=(1.04, 1.0)),
             box("cummer", (0.38, 0.23, 0.09), (0, 0, 1.1), "khaki", bev=0.035),
             box("strap_l", (0.065, 0.23, 0.04), (-0.11, 0, 1.39), "khaki", bev=0.016),
             box("strap_r", (0.065, 0.23, 0.04), (0.11, 0, 1.39), "khaki", bev=0.016),
             box("mag_pouch1", (0.075, 0.05, 0.1), (-0.09, 0.15, 1.15), "olive_dark", bev=0.02),
             box("mag_pouch2", (0.075, 0.05, 0.1), (0.0, 0.15, 1.15), "olive_dark", bev=0.02),
             box("mag_pouch3", (0.075, 0.05, 0.1), (0.09, 0.15, 1.15), "olive_dark", bev=0.02),
             box("admin", (0.15, 0.04, 0.07), (0, 0.145, 1.29), "olive_dark", bev=0.016),
             box("patch", (0.06, 0.01, 0.035), (0.05, 0.168, 1.3), "mustard", bev=0.005),
             box("radio", (0.065, 0.05, 0.11), (-0.12, 0.13, 1.33), "dark", bev=0.015),
             cyl("antenna", 0.006, 0.22, (-0.14, -0.13, 1.52), "dark", verts=6, bev=0.0),
             box("pack", (0.27, 0.12, 0.28), (0, -0.18, 1.2), "olive_dark", bev=0.05),
             box("pack_lid", (0.27, 0.13, 0.06), (0, -0.18, 1.35), "khaki", bev=0.025),
             box("pack_pocket", (0.16, 0.05, 0.11), (0, -0.25, 1.15), "khaki", bev=0.02),
             cyl("neck", 0.058, 0.1, (0, 0, 1.45), "skin", verts=10, bev=0.0),
             cyl("collar", 0.078, 0.05, (0, 0.0, 1.425), "TEAM", verts=14, bev=0.02)]
    parts["Torso"] = (torso, (0, 0, 1.02))

    # ---------------- head: helmet, goggles, shemagh
    head = [sphere("skull", 0.112, (0, 0.005, 1.6), "skin", segs=14, rings=10, scale=(1.0, 1.05, 1.08)),
            sphere("helmet", 0.142, (0, -0.008, 1.655), "olive", segs=18, rings=10, scale=(1.05, 1.12, 0.84)),
            cyl("helmet_rim", 0.148, 0.03, (0, -0.008, 1.606), "olive_dark", verts=18, bev=0.012),
            cyl("helmet_band", 0.1505, 0.034, (0, -0.008, 1.645), "TEAM", verts=18, bev=0.008),
            box("nvg_mount", (0.05, 0.03, 0.045), (0, 0.142, 1.685), "dark", bev=0.01),
            box("rail_l", (0.02, 0.12, 0.03), (-0.147, -0.01, 1.64), "dark", bev=0.008),
            box("rail_r", (0.02, 0.12, 0.03), (0.147, -0.01, 1.64), "dark", bev=0.008),
            box("goggles", (0.2, 0.05, 0.062), (0, 0.112, 1.618), "dark", bev=0.022),
            box("lens_l", (0.072, 0.02, 0.046), (-0.046, 0.136, 1.618), "GLASS", bev=0.014),
            box("lens_r", (0.072, 0.02, 0.046), (0.046, 0.136, 1.618), "GLASS", bev=0.014),
            sphere("gaiter", 0.104, (0, 0.022, 1.53), "olive_dark", segs=14, rings=8, scale=(1.04, 1.0, 0.62)),
            box("cup_l", (0.035, 0.07, 0.075), (-0.122, 0.0, 1.585), "dark", bev=0.016),
            box("cup_r", (0.035, 0.07, 0.075), (0.122, 0.0, 1.585), "dark", bev=0.016)]
    parts["Head"] = (head, (0, 0, 1.5))

    # ---------------- arms: team sleeves, elbow joints, rolled cuffs, glove cuffs
    for side, sx in (("L", -1), ("R", 1)):
        x = 0.24 * sx
        upper = [joint(f"shoulder_{side}", 0.062, (x - 0.012 * sx, 0, 1.37), "TEAM", scale=(1.1, 1.0, 0.9)),
                 limb(f"upper_{side}", 0.05, 0.044, 0.25, (x, 0, 1.36), "TEAM"),
                 box(f"pad_{side}", (0.026, 0.08, 0.08), (x + 0.044 * sx, 0, 1.31), "khaki", bev=0.012),
                 cyl(f"flag_{side}", 0.028, 0.01, (x + 0.047 * sx, 0, 1.235), "mustard", rot=(0, 90, 0), verts=10, bev=0.004)]
        parts[f"UpperArm_{side}"] = (upper, (x, 0, 1.38))
        lower = [joint(f"elbow_{side}", 0.047, (x, 0, 1.1), "TEAM"),
                 limb(f"fore_{side}", 0.045, 0.038, 0.16, (x, 0, 1.1), "TEAM"),
                 cyl(f"roll_{side}", 0.045, 0.034, (x, 0, 0.93), "olive_dark", verts=12, bev=0.012),
                 cyl(f"gcuff_{side}", 0.034, 0.05, (x, 0, 0.885), "glove", verts=12, bev=0.012)]
        parts[f"LowerArm_{side}"] = (lower, (x, 0, 1.1))

    built = {}
    for name, (objs, pivot) in parts.items():
        ob = join(objs, name)
        set_origin(ob, pivot)
        built[name] = ob

    # hands: built in their grip frame, then hung at the wrist in a relaxed rest pose
    hands = {}
    specs = tp_hand_specs()
    for side, names in TP_HANDS.items():
        sx = -1 if side == "L" else 1
        wrist_world = Vector((0.24 * sx, 0, 0.86))
        for hn in names:
            spec = specs[hn]
            h = build_hand(hn, spec, detail=0.6, forearm=False)
            set_origin(h, (0, 0, 0))
            d = Vector(spec["dir"]).normalized()
            rot = d.rotation_difference(Vector((0, 0, 1))).to_matrix().to_4x4()
            h.matrix_world = Matrix.Translation(wrist_world - (rot.to_3x3() @ Vector(spec["wrist"]))) @ rot
            hands[hn] = (h, side, wrist_world)

    allm = list(built.values()) + [h for h, _, _ in hands.values()]
    # AO: hands baked away from the body so they don't pick up its occlusion
    for i, (h, side, _) in enumerate(hands.values()):
        h.location.x += 3.0 + i * 0.5
    finalize(allm, ao=True, ao_distance=0.18, ground=0.0, ao_strength=0.5)
    for i, (h, side, _) in enumerate(hands.values()):
        h.location.x -= 3.0 + i * 0.5

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
    for side in ("L", "R"):
        sx = -1 if side == "L" else 1
        empty(f"Wrist_{side}", (0.24 * sx, 0, 0.86), parent=built[f"LowerArm_{side}"])
    for hn, (h, side, wrist_world) in hands.items():
        set_parent(h, built[f"LowerArm_{side}"])
        empty(f"{hn}_W", tuple(wrist_world), parent=h)
    print(f"[salvo] Trooper: body {tri_count(built.values())} tris, hands "
          f"{tri_count([hands['HandR_Grip'][0], hands['HandL_Guard'][0]])} tris (2 visible)")
    export_fbx([root], "Characters/Trooper.fbx")


# Hand poses per weapon: (right pose, left pose, marker the left hand attaches to).
# Left hands on pistols cup the shooting hand, so they live in the GripR frame.
def weapon_poses():
    from salvo_hands import (pose_pistol_grip, pose_fist, pose_support_guard,
                             pose_support_vertical, pose_support_pistol)
    return {
        "Viper": (pose_pistol_grip(), pose_support_pistol(), "GripR"),
        "Hornet": (pose_pistol_grip(), pose_support_vertical(), "GripL"),
        "Ranger": (pose_pistol_grip(), pose_support_guard(0.028, 0.029), "GripL"),
        "Bulldog": (pose_pistol_grip(), pose_support_guard(0.027, 0.027), "GripL"),
        "Longbow": (pose_pistol_grip(), pose_support_guard(0.025, 0.024), "GripL"),
        "Fang": (pose_fist(), None, None),
    }


def fp_arms():
    """First-person arms. Every arm's origin is its grip marker and it is authored in that
    marker's frame, so Unity parents it to GripR / GripL with an identity transform.
      Arm_R            standard pistol-grip hand, index finger on the trigger
      Arm_R_<Weapon>   per-weapon right hand when it differs (knife fist)
      Arm_L_<Weapon>   per-weapon support hand, wrapped around that weapon's handguard/foregrip
    """
    from salvo_hands import build_hand, pose_pistol_grip
    reset()
    arms = [build_hand("Arm_R", pose_pistol_grip())]
    for wname, (right, left, _) in weapon_poses().items():
        if wname == "Fang":
            arms.append(build_hand(f"Arm_R_{wname}", right))
        if left is not None:
            arms.append(build_hand(f"Arm_L_{wname}", left))
    for a in arms:
        set_origin(a, (0, 0, 0))
    # spread apart while baking AO so the hands don't occlude each other
    for i, a in enumerate(arms):
        a.location.x += i * 0.8
    finalize(arms, ao=True, ao_distance=0.04, ao_strength=0.55)
    for i, a in enumerate(arms):
        a.location.x -= i * 0.8
    root = empty("FP_Arms")
    parent_all(arms, root)
    print(f"[salvo] FP_Arms: {len(arms)} arms, {tri_count(arms)} tris")
    export_fbx([root], "Characters/FP_Arms.fbx")


if __name__ == "__main__":
    which = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    for k, fn in {"trooper": trooper, "arms": fp_arms}.items():
        if not which or k in which:
            fn()
