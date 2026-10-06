"""Render weapon card icons (transparent PNG, side view) into Assets/_Salvo/Art/Icons."""
import bpy
import os
import sys
import math
from mathutils import Vector

sys.path.append(os.path.dirname(__file__))
from salvo_lib import ROOT, ART, hex_rgb  # noqa

OUT = os.path.join(ROOT, "Assets", "_Salvo", "Art", "Icons")
os.makedirs(OUT, exist_ok=True)
SLOT_COLORS = {"SKIN": "4A515C", "GLASS": "5E9FB2", "TEAM": "E0614F"}
WEAPONS = ["Viper", "Hornet", "Ranger", "Bulldog", "Longbow", "Fang"]

for name in WEAPONS:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=os.path.join(ART, "Weapons", f"W_{name}.fbx"))
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    for o in meshes:
        me = o.data
        col = me.color_attributes.get("Col") or me.color_attributes[0]
        slots = [m.name.split(".")[0] if m else "" for m in me.materials]
        for p in me.polygons:
            s = slots[p.material_index] if p.material_index < len(slots) else ""
            if s in SLOT_COLORS:
                c = hex_rgb(SLOT_COLORS[s])
                for li in p.loop_indices:
                    a = col.data[li].color_srgb[3]
                    col.data[li].color_srgb = (c[0], c[1], c[2], a)
        me.color_attributes.active_color = col
    sc = bpy.context.scene
    bpy.context.view_layer.update()
    pts = [o.matrix_world @ Vector(c) for o in meshes for c in o.bound_box]
    mn = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    mx = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    ctr = (mn + mx) / 2
    length = max(mx.y - mn.y, (mx.z - mn.z) * 2.8)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    sc.collection.objects.link(cam)
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = length * 1.08
    # right side view, weapon pointing right in the image (+Y -> image right)
    cam.location = ctr + Vector((3.0, 0, 0))
    cam.rotation_euler = (math.radians(90), 0, math.radians(90))
    sc.camera = cam
    sc.render.engine = "BLENDER_WORKBENCH"
    sh = sc.display.shading
    sh.light = "STUDIO"
    sh.color_type = "VERTEX"
    sh.show_cavity = True
    sh.cavity_type = "BOTH"
    sh.cavity_ridge_factor = 1.2
    sh.cavity_valley_factor = 1.0
    sh.show_object_outline = True
    sh.object_outline_color = (0.06, 0.07, 0.09)
    sh.show_specular_highlight = True
    sc.render.film_transparent = True
    sc.view_settings.view_transform = "Standard"
    sc.render.resolution_x = 640
    sc.render.resolution_y = 240
    sc.render.filepath = os.path.join(OUT, f"Icon_{name}.png")
    bpy.ops.render.render(write_still=True)
    print("[icons]", name)
