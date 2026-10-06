"""
Builds every SALVO weapon as an FBX with this hierarchy (Blender axes: X right, Y forward, Z up):

  W_<Name>            root, origin = right-hand grip point
    Body              static mesh (SKIN slot = paintable panels)
    Mag / Slide / Pump / Bolt   animated child meshes (pivots placed for procedural anims)
    Muzzle            empty - tracer/flash origin
    Sight             empty - eye point for ADS alignment
    GripR / GripL     empties - hand placement for first-person arms & third-person rigs
    Eject             empty - shell ejection port
"""
import os
import sys
sys.path.append(os.path.dirname(__file__))
from salvo_lib import *  # noqa


def grip(name, loc, h=0.10, w=0.032, d=0.046, tilt=-14):
    return box(name, (w, d, h), loc, "rubber", rot=(tilt, 0, 0), bev=0.008, segs=2, taper=(0.95, 0.9))


# every pistol grip shares one shape/tilt so the posed right hand fits all guns; the trigger sits
# where that hand's index finger lands: GripR + (0, 0.05, 0.042)
TRIGGER = (0, 0.05, 0.042)


def trigger(origin=(0, 0, 0)):
    o = origin
    return [box("tg", (0.012, 0.064, 0.007), (o[0], o[1] + 0.046, o[2] + 0.018), "dark", bev=0.003),
            box("tg_front", (0.012, 0.007, 0.03), (o[0], o[1] + 0.077, o[2] + 0.032), "dark", bev=0.003),
            box("trigger", (0.006, 0.008, 0.022), (o[0] + TRIGGER[0], o[1] + TRIGGER[1], o[2] + TRIGGER[2]), "steel", rot=(15, 0, 0), bev=0.002)]


def finish(root_name, body_parts, movers, markers, out):
    """movers: dict name -> (parts, pivot)"""
    body = join(body_parts, "Body")
    objs = [body]
    mover_objs = []
    for mname, (parts, pivot) in movers.items():
        m = join(parts, mname)
        set_origin(m, pivot)
        mover_objs.append(m)
    objs += mover_objs
    finalize(objs, ao=True, ao_distance=0.06, ao_strength=0.55)
    root = empty(root_name)
    parent_all(objs, root)
    for mname, loc, rot in markers:
        empty(mname, loc, rot, parent=root)
    print(f"[salvo] {root_name}: {tri_count(objs)} tris")
    export_fbx([root], out)


# --------------------------------------------------------------------------------------
def viper():
    reset()
    P = []
    P.append(grip("grip", (0, 0, 0)))
    P.append(box("frame", (0.03, 0.17, 0.03), (0, 0.035, 0.062), "dark", bev=0.006))
    P.append(box("slide", (0.034, 0.19, 0.036), (0, 0.04, 0.094), "SKIN", bev=0.007))
    P.append(box("slide_top", (0.024, 0.15, 0.006), (0, 0.045, 0.114), "gunmetal", bev=0.002))
    for i in range(4):
        P.append(box(f"serr{i}", (0.036, 0.004, 0.024), (0, -0.04 + i * 0.009, 0.094), "gunmetal", bev=0.001))
    P.append(cyl("barrel", 0.009, 0.03, (0, 0.14, 0.094), "dark", rot=(90, 0, 0), verts=10, bev=0.002))
    P.append(box("fsight", (0.006, 0.01, 0.012), (0, 0.125, 0.118), "dark", bev=0.002))
    P.append(box("rsight_l", (0.007, 0.008, 0.012), (-0.009, -0.045, 0.118), "dark", bev=0.002))
    P.append(box("rsight_r", (0.007, 0.008, 0.012), (0.009, -0.045, 0.118), "dark", bev=0.002))
    P.append(box("dot", (0.004, 0.003, 0.004), (0, 0.128, 0.121), "mustard", bev=0.0))
    # trigger guard (U shape)
    P += trigger()
    P.append(box("beaver", (0.03, 0.03, 0.012), (0, -0.04, 0.07), "dark", bev=0.004))
    P.append(box("rail", (0.022, 0.05, 0.01), (0, 0.095, 0.042), "dark", bev=0.003))
    mag = [box("mag_base", (0.036, 0.05, 0.012), (0, -0.012, -0.056), "dark", rot=(-14, 0, 0), bev=0.004),
           box("mag_body", (0.024, 0.036, 0.09), (0, -0.003, -0.01), "steel", rot=(-14, 0, 0), bev=0.003)]
    slide = []
    finish("W_Viper", P, {"Mag": (mag, (0, 0.006, 0.03))}, [
        ("Muzzle", (0, 0.158, 0.094), (0, 0, 0)),
        ("Sight", (0, -0.045, 0.121), (0, 0, 0)),
        ("GripR", (0, 0, 0), (-14, 0, 0)),
        ("GripL", (-0.012, 0.005, -0.01), (-14, 0, 0)),
        ("Eject", (0.02, 0.04, 0.1), (0, 0, 0)),
    ], "Weapons/W_Viper.fbx")


# --------------------------------------------------------------------------------------
def hornet():
    reset()
    P = []
    P.append(grip("grip", (0, 0, 0), h=0.105))
    P.append(box("recv", (0.05, 0.30, 0.068), (0, 0.07, 0.075), "SKIN", bev=0.01))
    P.append(box("recv_side", (0.054, 0.18, 0.02), (0, 0.09, 0.058), "gunmetal", bev=0.004))
    P.append(box("rail", (0.026, 0.24, 0.012), (0, 0.07, 0.115), "dark", bev=0.003))
    for i in range(6):
        P.append(box(f"railn{i}", (0.03, 0.008, 0.006), (0, -0.02 + i * 0.035, 0.12), "dark", bev=0.001))
    P.append(cyl("shroud", 0.024, 0.11, (0, 0.27, 0.075), "dark", rot=(90, 0, 0), verts=12, bev=0.004))
    for i in range(3):
        P.append(cyl(f"shroud_ring{i}", 0.026, 0.008, (0, 0.235 + i * 0.03, 0.075), "gunmetal", rot=(90, 0, 0), verts=12, bev=0.002))
    P.append(cyl("barrel", 0.011, 0.06, (0, 0.35, 0.075), "gunmetal", rot=(90, 0, 0), verts=10, bev=0.002))
    P.append(cyl("comp", 0.016, 0.035, (0, 0.385, 0.075), "dark", rot=(90, 0, 0), verts=8, bev=0.003))
    P.append(cyl("fgrip", 0.016, 0.09, (0, 0.2, -0.004), "rubber", rot=(8, 0, 0), verts=12, bev=0.006))
    P.append(cyl("fgrip_cap", 0.019, 0.01, (0, 0.2 - 0.0063, -0.049), "dark", rot=(8, 0, 0), verts=12, bev=0.003))
    P += trigger()
    # stock: two rods + butt pad
    P.append(cyl("rod_l", 0.006, 0.2, (-0.018, -0.15, 0.085), "steel", rot=(90, 0, 0), verts=8, bev=0.0))
    P.append(cyl("rod_r", 0.006, 0.2, (0.018, -0.15, 0.085), "steel", rot=(90, 0, 0), verts=8, bev=0.0))
    P.append(box("butt", (0.05, 0.022, 0.09), (0, -0.255, 0.07), "rubber", bev=0.008))
    # red dot
    P.append(box("rd_base", (0.03, 0.05, 0.012), (0, 0.04, 0.127), "dark", bev=0.003))
    P.append(box("rd_hood_l", (0.006, 0.04, 0.034), (-0.017, 0.04, 0.148), "SKIN", bev=0.002))
    P.append(box("rd_hood_r", (0.006, 0.04, 0.034), (0.017, 0.04, 0.148), "SKIN", bev=0.002))
    P.append(box("rd_hood_t", (0.04, 0.04, 0.006), (0, 0.04, 0.167), "SKIN", bev=0.002))
    P.append(box("rd_glass", (0.028, 0.003, 0.028), (0, 0.055, 0.149), "GLASS", bev=0.0))
    P.append(box("charge", (0.012, 0.03, 0.012), (0.03, 0.15, 0.09), "dark", bev=0.003))
    mag = [box("mag", (0.028, 0.04, 0.17), (0, 0.105, -0.035), "gunmetal", rot=(6, 0, 0), bev=0.004),
           box("mag_base", (0.034, 0.046, 0.012), (0, 0.111, -0.12), "dark", rot=(6, 0, 0), bev=0.004)]
    finish("W_Hornet", P, {"Mag": (mag, (0, 0.1, 0.04))}, [
        ("Muzzle", (0, 0.405, 0.075), (0, 0, 0)),
        ("Sight", (0, 0.0, 0.149), (0, 0, 0)),
        ("GripR", (0, 0, 0), (-14, 0, 0)),
        ("GripL", (0, 0.2, -0.004), (8, 0, 0)),
        ("Eject", (0.03, 0.1, 0.1), (0, 0, 0)),
    ], "Weapons/W_Hornet.fbx")


# --------------------------------------------------------------------------------------
def ranger():
    reset()
    P = []
    P.append(grip("grip", (0, 0, 0), h=0.105))
    P.append(box("lower", (0.05, 0.25, 0.06), (0, 0.06, 0.06), "SKIN", bev=0.009))
    P.append(box("upper", (0.048, 0.32, 0.046), (0, 0.1, 0.11), "SKIN", bev=0.009))
    P.append(box("upper_trim", (0.052, 0.2, 0.012), (0, 0.06, 0.09), "gunmetal", bev=0.003))
    P.append(box("rail", (0.024, 0.28, 0.012), (0, 0.1, 0.139), "dark", bev=0.003))
    P.append(box("hguard", (0.056, 0.27, 0.058), (0, 0.38, 0.1), "dark", bev=0.012))
    for i in range(4):
        P.append(box(f"vent_l{i}", (0.06, 0.03, 0.012), (0, 0.29 + i * 0.055, 0.1), "gunmetal", bev=0.004))
    P.append(cyl("barrel", 0.011, 0.2, (0, 0.6, 0.1), "gunmetal", rot=(90, 0, 0), verts=10, bev=0.002))
    P.append(cyl("gas", 0.017, 0.03, (0, 0.55, 0.1), "dark", rot=(90, 0, 0), verts=10, bev=0.004))
    P.append(box("brake", (0.034, 0.06, 0.03), (0, 0.72, 0.1), "dark", bev=0.008))
    for i in range(2):
        P.append(box(f"brake_slot{i}", (0.036, 0.008, 0.018), (0, 0.71 + i * 0.02, 0.1), "gunmetal", bev=0.0))
    # stock (profile in YZ)
    P.append(prism("stock", [(-0.06, 0.13), (-0.06, 0.05), (-0.2, 0.02), (-0.31, -0.03), (-0.33, -0.03), (-0.33, 0.13)], 0.044, color="SKIN", bev=0.01))
    P.append(box("buttpad", (0.048, 0.02, 0.17), (0, -0.335, 0.05), "rubber", bev=0.008))
    P += trigger()
    P.append(box("charge", (0.03, 0.02, 0.012), (0, -0.05, 0.13), "dark", bev=0.003))
    # holo sight
    P.append(box("holo_base", (0.036, 0.07, 0.016), (0, 0.06, 0.153), "dark", bev=0.004))
    P.append(box("holo_l", (0.006, 0.05, 0.04), (-0.021, 0.06, 0.18), "gunmetal", bev=0.002))
    P.append(box("holo_r", (0.006, 0.05, 0.04), (0.021, 0.06, 0.18), "gunmetal", bev=0.002))
    P.append(box("holo_t", (0.048, 0.05, 0.007), (0, 0.06, 0.202), "gunmetal", bev=0.002))
    P.append(box("holo_glass", (0.036, 0.003, 0.034), (0, 0.075, 0.18), "GLASS", bev=0.0))
    P.append(box("fsight", (0.008, 0.012, 0.03), (0, 0.5, 0.144), "dark", bev=0.002))
    # curved mag: 3 rotated segments
    mag = []
    for i, (y, z, a) in enumerate([(0.12, 0.005, 6), (0.128, -0.055, 14), (0.143, -0.11, 22)]):
        mag.append(box(f"mag{i}", (0.03, 0.05, 0.065), (0, y, z), "SKIN" if i == 1 else "gunmetal", rot=(a, 0, 0), bev=0.005))
    mag.append(box("mag_base", (0.034, 0.056, 0.012), (0, 0.157, -0.145), "dark", rot=(24, 0, 0), bev=0.004))
    finish("W_Ranger", P, {"Mag": (mag, (0, 0.118, 0.035))}, [
        ("Muzzle", (0, 0.755, 0.1), (0, 0, 0)),
        ("Sight", (0, 0.0, 0.18), (0, 0, 0)),
        ("GripR", (0, 0, 0), (-14, 0, 0)),
        ("GripL", (0, 0.36, 0.1), (0, 0, 0)),
        ("Eject", (0.03, 0.1, 0.11), (0, 0, 0)),
    ], "Weapons/W_Ranger.fbx")


# --------------------------------------------------------------------------------------
def bulldog():
    reset()
    P = []
    P.append(box("recv", (0.052, 0.22, 0.075), (0, 0.07, 0.075), "SKIN", bev=0.012))
    P.append(box("recv_top", (0.03, 0.2, 0.01), (0, 0.07, 0.115), "gunmetal", bev=0.003))
    P.append(cyl("barrel", 0.017, 0.5, (0, 0.43, 0.095), "gunmetal", rot=(90, 0, 0), verts=12, bev=0.003))
    P.append(cyl("tube", 0.015, 0.42, (0, 0.39, 0.058), "dark", rot=(90, 0, 0), verts=12, bev=0.003))
    P.append(cyl("tube_cap", 0.018, 0.02, (0, 0.6, 0.058), "gunmetal", rot=(90, 0, 0), verts=12, bev=0.004))
    P.append(box("band", (0.04, 0.02, 0.06), (0, 0.55, 0.077), "gunmetal", bev=0.006))
    P.append(cyl("bead", 0.004, 0.01, (0, 0.67, 0.115), "brass", verts=8, bev=0.0))
    # pistol grip + wooden stock
    P.append(grip("grip", (0, 0, 0)))
    P.append(prism("stock", [(-0.03, 0.11), (-0.03, 0.05), (-0.38, 0.0), (-0.4, 0.0),
                             (-0.4, 0.13), (-0.2, 0.105), (-0.06, 0.112)], 0.046, color="wood", bev=0.012))
    P.append(box("buttpad", (0.05, 0.02, 0.14), (0, -0.405, 0.065), "rubber", bev=0.008))
    P += trigger()
    # side shell holder
    P.append(box("shell_holder", (0.008, 0.08, 0.03), (-0.03, 0.06, 0.075), "dark", bev=0.003))
    for i in range(4):
        P.append(cyl(f"shell{i}", 0.006, 0.026, (-0.036, 0.03 + i * 0.02, 0.075), "red", verts=8, bev=0.0))
    pump = [cyl("pump", 0.026, 0.15, (0, 0.33, 0.058), "wood_dark", rot=(90, 0, 0), verts=12, bev=0.006)]
    for i in range(4):
        pump.append(cyl(f"pump_g{i}", 0.0275, 0.008, (0, 0.28 + i * 0.033, 0.058), "wood", rot=(90, 0, 0), verts=12, bev=0.002))
    finish("W_Bulldog", P, {"Pump": (pump, (0, 0.33, 0.058))}, [
        ("Muzzle", (0, 0.685, 0.095), (0, 0, 0)),
        ("Sight", (0, -0.05, 0.12), (0, 0, 0)),
        ("GripR", (0, 0, 0), (-14, 0, 0)),
        ("GripL", (0, 0.33, 0.058), (0, 0, 0)),
        ("Eject", (0.03, 0.08, 0.09), (0, 0, 0)),
    ], "Weapons/W_Bulldog.fbx")


# --------------------------------------------------------------------------------------
def longbow():
    reset()
    P = []
    # chassis / stock (profile)
    P.append(prism("chassis", [(-0.42, 0.11), (-0.42, -0.03), (-0.3, -0.02), (-0.16, 0.03), (-0.1, 0.0), (-0.04, 0.005),
                               (0.08, 0.03), (0.42, 0.05), (0.44, 0.075), (0.42, 0.09), (-0.02, 0.09),
                               (-0.12, 0.12), (-0.3, 0.12)], 0.05, color="SKIN", bev=0.012))
    P.append(box("cheek", (0.046, 0.14, 0.03), (0, -0.25, 0.13), "SKIN", bev=0.01))
    P.append(box("buttpad", (0.052, 0.02, 0.16), (0, -0.425, 0.045), "rubber", bev=0.008))
    P.append(cyl("recv", 0.025, 0.26, (0, 0.08, 0.098), "gunmetal", rot=(90, 0, 0), verts=12, bev=0.004))
    P.append(cyl("barrel", 0.014, 0.6, (0, 0.5, 0.098), "dark", rot=(90, 0, 0), verts=12, bev=0.002, r2=0.011))
    P.append(box("brake", (0.04, 0.07, 0.032), (0, 0.83, 0.098), "dark", bev=0.008))
    for i in range(3):
        P.append(box(f"brake_s{i}", (0.042, 0.008, 0.02), (0, 0.81 + i * 0.02, 0.098), "gunmetal", bev=0.0))
    P.append(grip("grip", (0, -0.01, -0.035)))
    P += trigger((0, -0.01, -0.035))
    P.append(box("bipod", (0.03, 0.08, 0.02), (0, 0.44, 0.04), "gunmetal", bev=0.006))
    # scope
    P.append(cyl("scope_tube", 0.019, 0.3, (0, 0.08, 0.165), "dark", rot=(90, 0, 0), verts=14, bev=0.003))
    P.append(cyl("scope_obj", 0.032, 0.08, (0, 0.26, 0.165), "dark", rot=(90, 0, 0), verts=14, bev=0.005, r2=0.02))
    P.append(cyl("scope_eye", 0.027, 0.06, (0, -0.1, 0.165), "dark", rot=(-90, 0, 0), verts=14, bev=0.005, r2=0.019))
    P.append(cyl("scope_ring", 0.034, 0.01, (0, 0.3, 0.165), "SKIN", rot=(90, 0, 0), verts=14, bev=0.003))
    P.append(cyl("turret_t", 0.012, 0.03, (0, 0.08, 0.195), "gunmetal", verts=10, bev=0.003))
    P.append(cyl("turret_r", 0.012, 0.03, (0.03, 0.08, 0.165), "gunmetal", rot=(0, 90, 0), verts=10, bev=0.003))
    P.append(box("mount_f", (0.02, 0.02, 0.05), (0, 0.16, 0.13), "gunmetal", bev=0.004))
    P.append(box("mount_r", (0.02, 0.02, 0.05), (0, 0.0, 0.13), "gunmetal", bev=0.004))
    P.append(cyl("lens_f", 0.026, 0.004, (0, 0.302, 0.165), "GLASS", rot=(90, 0, 0), verts=14, bev=0.0))
    P.append(cyl("lens_r", 0.02, 0.004, (0, -0.131, 0.165), "GLASS", rot=(90, 0, 0), verts=14, bev=0.0))
    mag = [box("mag", (0.034, 0.07, 0.05), (0, 0.11, 0.03), "gunmetal", bev=0.005)]
    bolt = [cyl("bolt_arm", 0.006, 0.05, (0.035, -0.02, 0.1), "steel", rot=(0, 90, 0), verts=8, bev=0.0),
            sphere("bolt_knob", 0.011, (0.062, -0.02, 0.1), "dark", segs=8, rings=6)]
    finish("W_Longbow", P, {"Mag": (mag, (0, 0.11, 0.05)), "Bolt": (bolt, (0.0, -0.02, 0.1))}, [
        ("Muzzle", (0, 0.87, 0.098), (0, 0, 0)),
        ("Sight", (0, -0.14, 0.165), (0, 0, 0)),
        ("GripR", (0, -0.01, -0.035), (-14, 0, 0)),
        ("GripL", (0, 0.3, 0.066), (0, 0, 0)),
        ("Eject", (0.03, 0.06, 0.11), (0, 0, 0)),
    ], "Weapons/W_Longbow.fbx")


# --------------------------------------------------------------------------------------
def fang():
    reset()
    P = []
    P.append(box("handle", (0.028, 0.11, 0.032), (0, 0.0, 0.0), "rubber", bev=0.01, taper=(0.9, 1.0)))
    for i in range(3):
        P.append(box(f"wrap{i}", (0.031, 0.008, 0.035), (0, -0.03 + i * 0.03, 0.0), "olive", bev=0.003))
    P.append(box("guard", (0.03, 0.012, 0.06), (0, 0.06, 0.006), "gunmetal", bev=0.004))
    P.append(box("pommel", (0.03, 0.016, 0.034), (0, -0.062, 0.0), "gunmetal", bev=0.006))
    P.append(prism("blade", [(0.065, 0.016), (0.24, 0.016), (0.27, 0.0), (0.21, -0.018), (0.065, -0.018)], 0.006, color="steel", bev=0.002))
    P.append(prism("spine", [(0.065, 0.016), (0.24, 0.016), (0.245, 0.012), (0.065, 0.01)], 0.0075, color="gunmetal", bev=0.0))
    finish("W_Fang", P, {}, [
        ("Muzzle", (0, 0.27, 0.0), (0, 0, 0)),
        ("Sight", (0, 0, 0.05), (0, 0, 0)),
        ("GripR", (0, 0, 0), (-90, 0, 0)),
        ("GripL", (0, 0, 0), (-90, 0, 0)),
        ("Eject", (0, 0, 0), (0, 0, 0)),
    ], "Weapons/W_Fang.fbx")


if __name__ == "__main__":
    which = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    builders = {"viper": viper, "hornet": hornet, "ranger": ranger, "bulldog": bulldog, "longbow": longbow, "fang": fang}
    for k, fn in builders.items():
        if not which or k in which:
            fn()
