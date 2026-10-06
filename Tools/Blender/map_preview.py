"""Map look-dev preview: blender -b --factory-startup --python map_preview.py -- <map.fbx> <out.png> [top|persp] [ortho_scale]"""
import bpy
import sys
import math

args = sys.argv[sys.argv.index("--") + 1:]
fbx, out = args[0], args[1]
mode = args[2] if len(args) > 2 else "top"
scale = float(args[3]) if len(args) > 3 else 80.0

bpy.ops.import_scene.fbx(filepath=fbx)
for o in list(bpy.data.objects):
    if o.type == "MESH" and mode == "top" and o.name.startswith(("NC_Mesa", "NC_Dunes", "NC_Far", "NC_Hill", "NC_Island", "NC_Light", "NC_Sail")):
        bpy.data.objects.remove(o)
mat = bpy.data.materials.new("vc")
mat.use_nodes = True
nt = mat.node_tree
a = nt.nodes.new("ShaderNodeVertexColor"); a.layer_name = "Col"
mix = nt.nodes.new("ShaderNodeMix"); mix.data_type = "RGBA"; mix.blend_type = "MULTIPLY"; mix.inputs[0].default_value = 1.0
nt.links.new(a.outputs["Color"], mix.inputs[6]); nt.links.new(a.outputs["Alpha"], mix.inputs[7])
nt.links.new(mix.outputs[2], nt.nodes["Principled BSDF"].inputs["Base Color"])
nt.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.85
for o in bpy.data.objects:
    if o.type == "MESH":
        for s in o.material_slots:
            s.material = mat
sc = bpy.context.scene
cam = bpy.data.objects.new("c", bpy.data.cameras.new("c"))
sc.collection.objects.link(cam)
cam.data.clip_end = 1500
if mode == "top":
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = scale
    cam.location = (0, 0, 150)
    res = (900, 900)
else:
    cam.data.lens = 24
    cam.location = (-34, -40, 22)
    cam.rotation_euler = (math.radians(66), 0, math.radians(-40))
    res = (1280, 720)
sc.camera = cam
sun = bpy.data.objects.new("s", bpy.data.lights.new("s", "SUN"))
sun.data.energy = 3.2
sun.data.color = (1.0, 0.94, 0.85)
sun.rotation_euler = (math.radians(42), 0, math.radians(35))
sc.collection.objects.link(sun)
w = bpy.data.worlds.new("w"); sc.world = w
w.use_nodes = True
w.node_tree.nodes["Background"].inputs[0].default_value = (0.55, 0.72, 0.9, 1)
w.node_tree.nodes["Background"].inputs[1].default_value = 0.7
sc.render.engine = "BLENDER_EEVEE"
sc.view_settings.view_transform = "Standard"
sc.render.resolution_x, sc.render.resolution_y = res
sc.render.filepath = out
bpy.ops.render.render(write_still=True)
print("[preview] done", out)
