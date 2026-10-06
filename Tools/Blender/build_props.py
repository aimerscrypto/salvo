"""Gameplay props placed by Unity: jump pad, health pack, ammo box. (Blender: Y forward, Z up)"""
import os
import sys
sys.path.append(os.path.dirname(__file__))
from salvo_lib import *  # noqa


def jump_pad():
    reset()
    base = [cyl("base", 1.15, 0.22, (0, 0, 0.11), "concrete_dk", verts=16, bev=0.05),
            cyl("rim", 1.2, 0.08, (0, 0, 0.2), "dark", verts=16, bev=0.02),
            cyl("plate", 0.95, 0.06, (0, 0, 0.24), "mustard", verts=16, bev=0.02)]
    for i in range(3):  # chevrons pointing along +Y (launch direction)
        y = -0.35 + i * 0.3
        for sx in (-1, 1):
            base.append(box("chev", (0.42, 0.12, 0.03), (sx * 0.16, y, 0.28), "dark", rot=(0, 0, sx * 35), bev=0.0))
    body = join(base, "Base")
    ring = join([cyl("ring", 0.75, 0.07, (0, 0, 0.0), "teal", verts=16, bev=0.02),
                 cyl("ringin", 0.6, 0.075, (0, 0, 0.0), "teal_dark", verts=16, bev=0.02)], "Ring")
    set_origin(ring, (0, 0, 0))
    ring.location = (0, 0, 0.3)
    finalize([body, ring], ao=True, ao_distance=0.3, ground=0.0)
    root = empty("JumpPad")
    parent_all([body, ring], root)
    export_fbx([root], "Props/JumpPad.fbx")


def health_pack():
    reset()
    parts = [box("case", (0.62, 0.42, 0.34), (0, 0, 0.17), "cream", bev=0.06),
             box("lid", (0.64, 0.44, 0.06), (0, 0, 0.35), "white", bev=0.025),
             box("crossA", (0.34, 0.012, 0.1), (0, -0.215, 0.18), "coral", bev=0.0),
             box("crossB", (0.1, 0.012, 0.28), (0, -0.215, 0.18), "coral", bev=0.0),
             box("crossC", (0.24, 0.012, 0.08), (0, 0.0, 0.385), "coral", rot=(90, 0, 0), bev=0.0),
             box("crossD", (0.08, 0.24, 0.012), (0, 0.0, 0.385), "coral", bev=0.0),
             box("handle", (0.2, 0.05, 0.06), (0, 0, 0.41), "dark", bev=0.02),
             box("latchL", (0.05, 0.03, 0.08), (-0.2, -0.215, 0.3), "steel", bev=0.01),
             box("latchR", (0.05, 0.03, 0.08), (0.2, -0.215, 0.3), "steel", bev=0.01)]
    ob = join(parts, "Model")
    set_origin(ob, (0, 0, 0))
    finalize([ob], ao=True, ao_distance=0.15)
    root = empty("HealthPack")
    parent_all([ob], root)
    export_fbx([root], "Props/HealthPack.fbx")


def ammo_box():
    reset()
    parts = [box("case", (0.6, 0.36, 0.3), (0, 0, 0.15), "olive", bev=0.04),
             box("lid", (0.62, 0.38, 0.07), (0, 0, 0.31), "olive_dark", bev=0.02),
             box("stripe", (0.61, 0.37, 0.05), (0, 0, 0.2), "mustard", bev=0.0),
             box("handle", (0.24, 0.05, 0.05), (0, 0, 0.37), "dark", bev=0.02)]
    for i in range(5):
        parts.append(cyl("bullet", 0.025, 0.12, (-0.16 + i * 0.08, 0.05, 0.4), "brass", verts=8, bev=0.0, r2=0.012))
    ob = join(parts, "Model")
    set_origin(ob, (0, 0, 0))
    finalize([ob], ao=True, ao_distance=0.15)
    root = empty("AmmoBox")
    parent_all([ob], root)
    export_fbx([root], "Props/AmmoBox.fbx")


if __name__ == "__main__":
    jump_pad()
    health_pack()
    ammo_box()
