"""
Look-dev check for the posed hands: builds a weapon and its hands in one scene (nothing is
exported), attaches the hands to the grip markers exactly like Unity does, and renders views.
usage: blender -b --factory-startup --python hands_preview.py -- <out_dir> [weapon ...] [--views fp,right,left,under]
"""
import os
import sys
import math
import bpy
from mathutils import Vector

sys.path.append(os.path.dirname(__file__))
import salvo_lib as L  # noqa: E402
import build_weapons as bw  # noqa: E402
import build_characters as bc  # noqa: E402
from salvo_hands import build_hand  # noqa: E402

args = sys.argv[sys.argv.index("--") + 1:]
out_dir = args[0]
views = ["fp", "right", "left"]
weapons = []
i = 1
while i < len(args):
    if args[i] == "--views":
        views = args[i + 1].split(","); i += 2
    else:
        weapons.append(args[i]); i += 1
weapons = weapons or ["viper", "hornet", "ranger", "bulldog", "longbow", "fang"]


def fast_finalize(objs, **_):
    for o in objs:
        if o.type == "MESH":
            L.apply_mods(o)
            L.palette_to_vcolor(o)
    return objs


bw.export_fbx = lambda *a, **k: None
bw.finalize = fast_finalize


def material():
    mat = bpy.data.materials.new("VC")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    attr = nt.nodes.new("ShaderNodeVertexColor"); attr.layer_name = "Col"
    nt.links.new(attr.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.75
    return mat


def flat(name, rgb):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (*rgb, 1)
    return m


def render(path, eye, target, lens=40):
    sc = bpy.context.scene
    cam = bpy.data.objects.get("cam")
    if cam is None:
        cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
        sc.collection.objects.link(cam)
        sc.camera = cam
    cam.data.lens = lens
    cam.data.clip_start = 0.01
    cam.location = Vector(eye)
    d = Vector(target) - Vector(eye)
    cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)


def setup_scene():
    sc = bpy.context.scene
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.data.energy = 3.2
    sun.data.color = (1.0, 0.93, 0.82)
    sun.rotation_euler = (math.radians(40), math.radians(15), math.radians(30))
    sc.collection.objects.link(sun)
    w = bpy.data.worlds.new("w"); sc.world = w
    w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0.55, 0.68, 0.82, 1)
    w.node_tree.nodes["Background"].inputs[1].default_value = 0.8
    sc.render.engine = "CYCLES"
    sc.cycles.samples = 16
    sc.cycles.use_denoising = True
    sc.view_settings.view_transform = "AgX"
    sc.render.resolution_x = 900
    sc.render.resolution_y = 600


poses = bc.weapon_poses()
for wn in weapons:
    getattr(bw, wn)()  # resets the scene and builds W_<Name> with its markers
    name = wn.capitalize()
    root = bpy.data.objects["W_" + name]
    right, left, left_marker = poses[name]
    hands = [(build_hand("HR", right), "GripR")]
    if left is not None:
        hands.append((build_hand("HL", left), left_marker))
    fast_finalize([h for h, _ in hands])
    bpy.context.view_layer.update()
    for h, marker in hands:
        L.set_origin(h, (0, 0, 0))
        m = bpy.data.objects[marker]
        h.parent = m
        h.matrix_parent_inverse.identity()
        h.location = (0, 0, 0)
        h.rotation_euler = (0, 0, 0)
    vc = material()
    special = {"TEAM": flat("team", (0.6, 0.12, 0.08)), "SKIN": flat("skin", (0.3, 0.32, 0.36)), "GLASS": flat("glass", (0.25, 0.5, 0.6))}
    for o in bpy.data.objects:
        if o.type == "MESH":
            for si, slot in enumerate(o.material_slots):
                n = slot.material.name.split(".")[0] if slot.material else ""
                o.material_slots[si].material = special.get(n, vc)
    setup_scene()
    bpy.context.view_layer.update()
    gr = bpy.data.objects["GripR"].matrix_world.translation
    gl = bpy.data.objects["GripL"].matrix_world.translation
    mid = (gr + gl) / 2 if left_marker == "GripL" else gr
    span = max(0.12, (gl - gr).length)
    for v in views:
        p = os.path.join(out_dir, f"hands_{wn}_{v}.png")
        if v == "fp":
            render(p, mid + Vector((-0.16, -0.32 - span * 0.6, 0.2)), mid + Vector((0.02, 0.05, 0.0)), 30)
        elif v == "right":
            render(p, mid + Vector((0.45 + span * 0.5, 0.0, 0.04)), mid, 35)
        elif v == "left":
            render(p, mid + Vector((-0.45 - span * 0.5, 0.0, 0.0)), mid, 35)
        elif v == "under":
            render(p, mid + Vector((-0.2, 0.05, -0.4 - span * 0.4)), mid, 35)
        elif v == "front":
            render(p, mid + Vector((0.12, 0.5 + span, 0.06)), mid, 35)
    print("[hands_preview] done", wn)
