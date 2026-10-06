"""Render a quick look-dev preview of exported FBX files.
usage: blender -b --factory-startup --python preview.py -- out.png file1.fbx [file2.fbx ...] [--az 35 --el 20 --res 900 --spread 1]
"""
import bpy, sys, math, os
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:]
opts = {"--axis": "x", "--az": "35", "--el": "18", "--res": "900", "--spread": "1", "--samples": "24", "--lens": "50"}
files = []
i = 1
out = args[0]
while i < len(args):
    if args[i] in opts:
        opts[args[i]] = args[i + 1]; i += 2
    else:
        files.append(args[i]); i += 1

bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene

mat = bpy.data.materials.new("VC")
mat.use_nodes = True
nt = mat.node_tree
bsdf = nt.nodes["Principled BSDF"]
attr = nt.nodes.new("ShaderNodeVertexColor"); attr.layer_name = "Col"
mul = nt.nodes.new("ShaderNodeMix"); mul.data_type = "RGBA"; mul.blend_type = "MULTIPLY"; mul.inputs[0].default_value = 1.0
nt.links.new(attr.outputs["Color"], mul.inputs[6])
nt.links.new(attr.outputs["Alpha"], mul.inputs[7])
nt.links.new(mul.outputs[2], bsdf.inputs["Base Color"])
bsdf.inputs["Roughness"].default_value = 0.8

def flat(name, rgb):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (*rgb, 1)
    m.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.6
    return m
special = {"SKIN": flat("skin", (0.35, 0.32, 0.22)), "TEAM": flat("team", (0.6, 0.12, 0.08)), "GLASS": flat("glass", (0.25, 0.5, 0.6))}
offset = 0.0
spread = float(opts["--spread"])
for f in files:
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=f)
    new = [o for o in bpy.data.objects if o not in before]
    roots = [o for o in new if o.parent is None]
    ax = "xyz".index(opts["--axis"])
    for r in roots:
        r.location[ax] += offset
    for o in new:
        if o.type == "MESH":
            for si, slot in enumerate(o.material_slots):
                n = slot.material.name.split(".")[0] if slot.material else ""
                o.material_slots[si].material = special.get(n, mat)
    bpy.context.view_layer.update()
    xs = [ (o.matrix_world @ Vector(c))[ax] for o in new if o.type == "MESH" for c in o.bound_box]
    if xs:
        offset -= (max(xs) - min(xs)) * 1.15 * spread if ax == 2 else -(max(xs) - min(xs)) * 1.15 * spread

# frame all meshes
pts = [o.matrix_world @ Vector(c) for o in bpy.data.objects if o.type == "MESH" for c in o.bound_box]
mn = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
mx = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
ctr = (mn + mx) / 2
rad = (mx - mn).length / 2

cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
sc.collection.objects.link(cam)
cam.data.lens = float(opts["--lens"])
cam.data.sensor_fit = "VERTICAL"
az, el = math.radians(float(opts["--az"])), math.radians(float(opts["--el"]))
dist = rad / math.tan(cam.data.angle / 2) * 1.05
d = Vector((math.sin(az) * math.cos(el), -math.cos(az) * math.cos(el), math.sin(el)))
cam.location = ctr + d * dist
cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
cam.data.clip_end = dist * 4
sc.camera = cam

sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
sun.data.energy = 3.5
sun.data.color = (1.0, 0.93, 0.82)
sun.data.angle = math.radians(3)
sun.rotation_euler = (math.radians(50), math.radians(10), math.radians(35))
sc.collection.objects.link(sun)

w = bpy.data.worlds.new("w"); sc.world = w
w.use_nodes = True
w.node_tree.nodes["Background"].inputs[0].default_value = (0.55, 0.68, 0.82, 1)
w.node_tree.nodes["Background"].inputs[1].default_value = 0.9

sc.render.engine = "CYCLES"
sc.cycles.samples = int(opts["--samples"])
sc.cycles.use_denoising = True
sc.view_settings.view_transform = "AgX"
res = int(opts["--res"])
sc.render.resolution_x = res
sc.render.resolution_y = int(res * 0.62)
sc.render.filepath = out
bpy.ops.render.render(write_still=True)
print("[preview] wrote", out)
