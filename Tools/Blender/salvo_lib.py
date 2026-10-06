"""
SALVO - shared Blender helpers for procedural, stylized low-poly assets.

Art pipeline contract (read by Unity's SalvoModelPostprocessor + Salvo/StylizedLit shader):
  * Vertex color RGB  = albedo (sRGB, from the named palette below)
  * Vertex color A    = baked ambient occlusion (linear, 0..1)
  * Material slots named TEAM / SKIN / GLASS stay as separate submeshes so Unity can
    swap them at runtime (team colors, weapon skins, lenses). Everything else is merged
    into a single "Salvo_Main" slot -> one material for the whole world = cheap rendering.
  * Empties are exported as plain transforms (muzzles, sights, spawn points, ...).

Run headless:  blender -b --factory-startup --python Tools/Blender/build_xxx.py
"""
import bpy
import bmesh
import math
import os
import random
from mathutils import Vector, Matrix, Euler

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
ART = os.path.join(ROOT, "Assets", "_Salvo", "Art", "Models")

# --------------------------------------------------------------------------------------
# Palette - warm, sun-baked Mediterranean outpost. Kept slightly desaturated so the
# stylized lighting (warm sun / cool shadow) does the color work instead of neon albedo.
# --------------------------------------------------------------------------------------
PAL = {
    "sand":        "E9D8B4",
    "sand_mid":    "DCC59A",
    "sand_dark":   "C9AE80",
    "plaster":     "F1E6CF",
    "terracotta":  "C66B3D",
    "terra_dark":  "A3502B",
    "teal":        "2E8C86",
    "teal_dark":   "1F6A66",
    "teal_light":  "5FB3A8",
    "mustard":     "E3B448",
    "mustard_dark":"C4952F",
    "coral":       "E8735A",
    "coral_dark":  "C4553F",
    "navy":        "2B3A55",
    "sky":         "8FC1D6",
    "slate":       "5B6B7A",
    "gunmetal":    "3A3F47",
    "steel":       "8A929B",
    "dark":        "26292E",
    "rubber":      "2C2D30",
    "wood":        "B07A4A",
    "wood_light":  "C99863",
    "wood_dark":   "84562F",
    "leaf":        "6FA34A",
    "leaf_light":  "8DBB5C",
    "leaf_dark":   "4C7C36",
    "trunk":       "8C6A4A",
    "rock":        "B3A897",
    "rock_dark":   "8E8474",
    "concrete":    "C2BDB2",
    "concrete_dk": "9D978B",
    "white":       "F4F1EA",
    "cream":       "EFE3C6",
    "olive":       "6E7444",
    "olive_dark":  "545A33",
    "khaki":       "B9A57A",
    "skin":        "D9A27F",
    "skin_dark":   "B98262",
    "glass":       "9CCBDA",
    "red":         "D2483C",
    "brass":       "C9A04A",
    "water":       "6FB4C2",
    "cloth_red":   "C9503F",
    "cloth_cream": "F0E2C2",
    # special slots (vertex color forced to white; Unity assigns the real material)
    "TEAM":  "FFFFFF",
    "SKIN":  "FFFFFF",
    "GLASS": "FFFFFF",
}
SPECIAL_SLOTS = ("TEAM", "SKIN", "GLASS")


def hex_rgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4))


def srgb_to_lin(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


# --------------------------------------------------------------------------------------
# Scene
# --------------------------------------------------------------------------------------
def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    random.seed(1337)


def get_mat(name):
    m = bpy.data.materials.get(name)
    if m is None:
        m = bpy.data.materials.new(name)
        if name in PAL:
            r, g, b = hex_rgb(PAL[name])
            m.diffuse_color = (srgb_to_lin(r), srgb_to_lin(g), srgb_to_lin(b), 1.0)
    return m


def _link(ob, collection=None):
    (collection or bpy.context.scene.collection).objects.link(ob)
    return ob


def _from_bm(name, bm, color):
    me = bpy.data.meshes.new(name)
    bm.normal_update()
    bm.to_mesh(me)
    bm.free()
    for p in me.polygons:
        p.use_smooth = True
    ob = bpy.data.objects.new(name, me)
    _link(ob)
    me.materials.append(get_mat(color))
    return ob


def _place(ob, loc, rot, scale=None):
    ob.location = Vector(loc)
    ob.rotation_euler = Euler([math.radians(a) for a in rot], "XYZ")
    if scale is not None:
        ob.scale = Vector(scale)
    return ob


def bevel(ob, width=0.02, segments=2, angle=35.0):
    """Rounded edges with hardened normals: flat faces stay flat, edges catch light."""
    if width <= 0:
        return ob
    m = ob.modifiers.new("Bevel", "BEVEL")
    m.width = width
    m.segments = segments
    m.limit_method = "ANGLE"
    m.angle_limit = math.radians(angle)
    m.harden_normals = True
    m.use_clamp_overlap = True
    m.miter_outer = "MITER_ARC"
    return ob


# --------------------------------------------------------------------------------------
# Primitives (all origin-centred unless noted, sizes in metres, rotations in degrees)
# --------------------------------------------------------------------------------------
def box(name, size, loc=(0, 0, 0), color="sand", rot=(0, 0, 0), bev=0.02, segs=2, taper=None):
    """taper=(sx, sy) scales the +Z face, handy for tapered stocks, roofs, legs."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    if taper:
        for v in bm.verts:
            if v.co.z > 0:
                v.co.x *= taper[0]
                v.co.y *= taper[1]
    bmesh.ops.scale(bm, vec=Vector(size), verts=bm.verts)
    ob = _from_bm(name, bm, color)
    _place(ob, loc, rot)
    bevel(ob, min(bev, min(size) * 0.45), segs)
    return ob


def cyl(name, r, depth, loc=(0, 0, 0), color="steel", rot=(0, 0, 0), verts=12, bev=0.01, r2=None, segs=2):
    """Cylinder along local Z. r2 = top radius (cone / taper)."""
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=verts,
                          radius1=r, radius2=r if r2 is None else r2, depth=depth)
    ob = _from_bm(name, bm, color)
    _place(ob, loc, rot)
    if bev > 0:
        bevel(ob, min(bev, r * 0.4, depth * 0.4), segs, angle=40.0)
    return ob


def sphere(name, r, loc=(0, 0, 0), color="leaf", segs=10, rings=7, scale=(1, 1, 1), rot=(0, 0, 0)):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=segs, v_segments=rings, radius=r)
    ob = _from_bm(name, bm, color)
    _place(ob, loc, rot, scale)
    return ob


def blob(name, r, loc=(0, 0, 0), color="leaf", subdiv=1, jitter=0.18, scale=(1, 1, 1), rot=(0, 0, 0), seed=None, flat=True):
    """Organic icosphere blob - foliage, rocks, clouds."""
    rng = random.Random(seed if seed is not None else random.random())
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=r)
    for v in bm.verts:
        v.co *= 1.0 + rng.uniform(-jitter, jitter)
    ob = _from_bm(name, bm, color)
    if flat:
        for p in ob.data.polygons:
            p.use_smooth = False
    _place(ob, loc, rot, scale)
    return ob


def wedge(name, size, loc=(0, 0, 0), color="sand", rot=(0, 0, 0), bev=0.02):
    """Right-angle prism: full height at -Y, zero height at +Y (ramps, roof halves)."""
    sx, sy, sz = size
    bm = bmesh.new()
    x, y, z = sx / 2, sy / 2, sz / 2
    v = [bm.verts.new(c) for c in (
        (-x, -y, -z), (x, -y, -z), (x, y, -z), (-x, y, -z),
        (-x, -y, z), (x, -y, z))]
    bm.faces.new((v[0], v[3], v[2], v[1]))  # bottom
    bm.faces.new((v[0], v[1], v[5], v[4]))  # back (tall)
    bm.faces.new((v[4], v[5], v[2], v[3]))  # slope
    bm.faces.new((v[0], v[4], v[3]))        # side
    bm.faces.new((v[1], v[2], v[5]))        # side
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = _from_bm(name, bm, color)
    _place(ob, loc, rot)
    bevel(ob, bev, 2)
    return ob


def prism(name, points2d, depth, loc=(0, 0, 0), color="sand", rot=(0, 0, 0), bev=0.015, axis="X"):
    """Extrude a 2D polygon (in the YZ plane by default) along X. Great for gun silhouettes."""
    bm = bmesh.new()
    h = depth / 2
    front, back = [], []
    for (a, b) in points2d:
        if axis == "X":
            front.append(bm.verts.new((-h, a, b)))
            back.append(bm.verts.new((h, a, b)))
        else:  # Z extrusion, points are XY
            front.append(bm.verts.new((a, b, -h)))
            back.append(bm.verts.new((a, b, h)))
    bm.faces.new(front)
    bm.faces.new(list(reversed(back)))
    n = len(points2d)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((front[i], front[j], back[j], back[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = _from_bm(name, bm, color)
    _place(ob, loc, rot)
    bevel(ob, bev, 2)
    return ob


def empty(name, loc=(0, 0, 0), rot=(0, 0, 0), parent=None):
    ob = bpy.data.objects.new(name, None)
    ob.empty_display_size = 0.05
    _link(ob)
    _place(ob, loc, rot)
    if parent is not None:
        set_parent(ob, parent)
    return ob


def _sync():
    bpy.context.view_layer.update()


def set_parent(child, parent):
    _sync()
    mw = child.matrix_world.copy()
    child.parent = parent
    child.matrix_world = mw


# --------------------------------------------------------------------------------------
# Mesh processing
# --------------------------------------------------------------------------------------
def apply_mods(ob):
    """Bake modifier stack + transform into the mesh data without bpy.ops (background safe)."""
    if ob.type != "MESH":
        return ob
    dg = bpy.context.evaluated_depsgraph_get()
    ev = ob.evaluated_get(dg)
    me = bpy.data.meshes.new_from_object(ev, preserve_all_data_layers=True, depsgraph=dg)
    old = ob.data
    ob.modifiers.clear()
    ob.data = me
    if old.users == 0:
        bpy.data.meshes.remove(old)
    return ob


def apply_transform(ob):
    """Bake scale/rotation (keep location so pivots survive)."""
    me = ob.data
    m = Matrix.LocRotScale(None, ob.rotation_euler, ob.scale)
    me.transform(m)
    ob.rotation_euler = (0, 0, 0)
    ob.scale = (1, 1, 1)
    me.update()
    _sync()


def join(objs, name):
    """Join meshes into one object whose origin is the first object's origin."""
    objs = [o for o in objs if o is not None]
    for o in objs:
        apply_mods(o)
    base = objs[0]
    if len(objs) > 1:
        bpy.ops.object.select_all(action="DESELECT")
        for o in objs:
            o.select_set(True)
        bpy.context.view_layer.objects.active = base
        bpy.ops.object.join()
    base.name = name
    base.data.name = name
    apply_transform(base)
    return base


def set_origin(ob, world_point):
    """Move the object's pivot to world_point without moving the geometry."""
    _sync()
    p = Vector(world_point)
    local = ob.matrix_world.inverted() @ p
    ob.data.transform(Matrix.Translation(-local))
    if ob.parent is None:
        ob.location = p
    else:
        ob.matrix_world.translation = p
    _sync()
    return ob


def parent_all(children, parent):
    for c in children:
        if c is not None:
            set_parent(c, parent)


def palette_to_vcolor(ob):
    """Material slot colors -> face-corner vertex colors. Collapse non-special slots."""
    me = ob.data
    attr = me.color_attributes.get("Col") or me.color_attributes.new("Col", "BYTE_COLOR", "CORNER")
    names = [m.name if m else "sand" for m in me.materials]
    cols = [hex_rgb(PAL.get(n.split(".")[0], "FF00FF")) for n in names]
    for p in me.polygons:
        c = cols[p.material_index] if cols else (1, 0, 1)
        for li in p.loop_indices:
            attr.data[li].color_srgb = (c[0], c[1], c[2], 1.0)
    me.color_attributes.active_color = attr
    me.color_attributes.render_color_index = me.color_attributes.find("Col")

    # collapse slots: Salvo_Main + specials (keep stable order: Main, TEAM, SKIN, GLASS)
    keep = ["Salvo_Main"] + [s for s in SPECIAL_SLOTS if s in [n.split(".")[0] for n in names]]
    idx_map = []
    for n in names:
        base = n.split(".")[0]
        idx_map.append(keep.index(base) if base in SPECIAL_SLOTS else 0)
    for p in me.polygons:
        p.material_index = idx_map[p.material_index] if idx_map else 0
    me.materials.clear()
    for k in keep:
        me.materials.append(get_mat(k))
    return ob


def bake_ao(objs, distance=1.0, samples=24, ground=None, strength=0.62):
    """Bake AO into the alpha channel of the "Col" attribute for every object in objs.
    ground: optional z height for a temporary occluding floor plane (props sitting on ground)."""
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = samples
    if scene.world is None:
        scene.world = bpy.data.worlds.new("World")
    scene.world.light_settings.distance = distance

    plane = None
    if ground is not None:
        bm = bmesh.new()
        bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=200)
        plane = _from_bm("__bake_ground", bm, "sand")
        plane.location.z = ground

    meshes = [o for o in objs if o.type == "MESH"]
    for o in meshes:
        me = o.data
        ao = me.color_attributes.get("AO") or me.color_attributes.new("AO", "FLOAT_COLOR", "CORNER")
        me.color_attributes.active_color = ao

    bpy.ops.object.select_all(action="DESELECT")
    for o in meshes:
        o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    bpy.ops.object.bake(type="AO", target="VERTEX_COLORS", use_clear=True, margin=0)

    for o in meshes:
        me = o.data
        ao = me.color_attributes["AO"]
        col = me.color_attributes["Col"]
        for i in range(len(me.loops)):
            a = ao.data[i].color[0]
            a = 1.0 - strength * (1.0 - a)  # soften: stylized, never pitch black
            c = col.data[i].color_srgb
            col.data[i].color_srgb = (c[0], c[1], c[2], max(0.0, min(1.0, a)))
        me.color_attributes.remove(ao)
        col = me.color_attributes["Col"]
        me.color_attributes.active_color = col
        me.color_attributes.render_color_index = me.color_attributes.find("Col")

    if plane is not None:
        bpy.data.objects.remove(plane, do_unlink=True)


def finalize(objs, ao=True, ao_distance=0.6, ground=None, ao_strength=0.62):
    """Apply modifiers, convert palette to vertex color, bake AO."""
    meshes = [o for o in objs if o.type == "MESH"]
    for o in meshes:
        apply_mods(o)
        palette_to_vcolor(o)
    if ao and meshes:
        bake_ao(meshes, distance=ao_distance, ground=ground, strength=ao_strength)
    return objs


def descendants(ob):
    out = [ob]
    for c in ob.children:
        out.extend(descendants(c))
    return out


def export_fbx(roots, rel_path):
    """Export the given root objects (with their children) to Assets/_Salvo/Art/Models/<rel_path>."""
    path = os.path.join(ART, rel_path)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    for r in roots:
        for o in descendants(r):
            o.select_set(True)
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        object_types={"MESH", "EMPTY"},
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z",
        axis_up="Y",
        bake_space_transform=False,
        use_mesh_modifiers=True,
        mesh_smooth_type="OFF",
        colors_type="SRGB",
        prioritize_active_color=True,
        use_tspace=False,
        use_custom_props=False,
        add_leaf_bones=False,
        bake_anim=False,
        use_metadata=False,
    )
    print("[salvo] exported", path)
    return path


def tri_count(objs):
    n = 0
    for o in objs:
        if o.type == "MESH":
            n += sum(len(p.vertices) - 2 for p in o.data.polygons)
    return n
