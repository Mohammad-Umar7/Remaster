"""
REMASTER - prop generator (Blender 5.x, bpy + bmesh only).

Every prop is built from code, so the art is original and reproducible.  Nothing is
downloaded, imported or taken from an asset library.

Run it one of these ways:
    blender --python tools/gen_props_blender.py          interactive: builds, exports, renders previews
    blender -b --python tools/gen_props_blender.py       headless:   same, without a window
    (from Blender's Python console)  import runpy; runpy.run_path(r"<path>", run_name="__main__")

What it does:
  * creates / refreshes a scene called REMASTER_PROPS (other scenes are never touched)
  * builds each prop as ONE mesh object: flat shaded, triangulated, real-world metres,
    transforms applied, origin as documented per prop
  * faces are assigned to material slots whose NAMES are the palette keys the game reads
    (stone, stone_dark, wood, wood_dark, metal, teal, yellow, red, purple, paper, screen,
    flame, cloth, green, ink, white).  The colours here are only for the Blender preview.
  * exports <name>.fbx to Assets/Resources/Models (Unity: 1 unit = 1 m, +Y up, no rotation)

Hand-made look.  Parts are made slightly uneven in two ways that both keep every face
perfectly FLAT (so flat shading and ink-outline edge detection never see a false crease):
  * "rough" boxes: each of the six face planes is nudged and tilted a little, and the corners
    are recomputed where three planes meet
  * "wonk": a small random shear/scale of a whole part

Axis convention.  Blender (x, y, z)  ->  Unity (-x, z, -y).
  Blender +Z is up, Blender -Y is the "front" of a prop, so a front face ends up facing Unity +Z.
"""
import bpy
import bmesh
import math
import os
import random
import tempfile
from mathutils import Vector, Matrix, Euler

SCENE_NAME = "REMASTER_PROPS"
HERE = os.path.dirname(os.path.abspath(__file__)) if "__file__" in globals() else os.getcwd()
OUT_DIR = os.path.normpath(os.path.join(HERE, "..", "Assets", "Resources", "Models"))
DEFAULT_PREVIEW_DIR = os.path.join(tempfile.gettempdir(), "remaster_props_preview")

# sRGB preview colours (the game ignores these and colours by slot NAME)
PALETTE = {
    "stone": (0.725, 0.682, 0.878),
    "stone_dark": (0.561, 0.514, 0.788),
    "wood": (0.800, 0.588, 0.376),
    "wood_dark": (0.510, 0.345, 0.243),
    "metal": (0.455, 0.478, 0.565),
    "teal": (0.184, 0.710, 0.659),
    "yellow": (1.000, 0.824, 0.290),
    "red": (0.937, 0.365, 0.365),
    "purple": (0.478, 0.310, 0.839),
    "paper": (0.953, 0.918, 0.843),
    "screen": (0.105, 0.125, 0.190),
    "flame": (1.000, 0.580, 0.180),
    "cloth": (0.640, 0.500, 0.900),
    "green": (0.482, 0.839, 0.353),
    "ink": (0.169, 0.145, 0.251),
    "white": (0.930, 0.930, 0.950),
}


def R(deg):
    return math.radians(deg)


def srgb_to_lin(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def get_mat(name):
    mat = bpy.data.materials.get(name)
    if mat is None:
        mat = bpy.data.materials.new(name)
    col = tuple(srgb_to_lin(c) for c in PALETTE[name]) + (1.0,)
    mat.diffuse_color = col
    try:
        mat.use_nodes = True
    except Exception:
        pass
    tree = getattr(mat, "node_tree", None)
    if tree is not None:
        for node in tree.nodes:
            if node.type == "BSDF_PRINCIPLED":
                node.inputs["Base Color"].default_value = col
                node.inputs["Roughness"].default_value = 0.9
    mat.roughness = 0.9
    return mat


# ------------------------------------------------------------------------------------------
# geometry helpers.  Every P_* function returns a fresh bmesh in its own local space.
# ------------------------------------------------------------------------------------------
def signed_volume(bm):
    vol = 0.0
    for f in bm.faces:
        vs = [l.vert.co for l in f.loops]
        for i in range(1, len(vs) - 1):
            vol += vs[0].dot(vs[i].cross(vs[i + 1]))
    return vol / 6.0


def wonk(bm, amount, rng, lock_bottom=False):
    """Small random shear/scale of a whole part.  Affine, so flat faces stay flat.
    amount is roughly how far (in metres) the outermost vertices move."""
    if amount <= 0.0 or not bm.verts:
        return
    lo = Vector((min(v.co.x for v in bm.verts), min(v.co.y for v in bm.verts), min(v.co.z for v in bm.verts)))
    hi = Vector((max(v.co.x for v in bm.verts), max(v.co.y for v in bm.verts), max(v.co.z for v in bm.verts)))
    centre = (lo + hi) / 2.0
    k = amount / max((hi - lo).length / 2.0, 1e-6)
    m = Matrix.Identity(3)
    for i in range(3):
        for j in range(3):
            m[i][j] += rng.uniform(-k, k)
    if lock_bottom:
        m[2][0] = 0.0
        m[2][1] = 0.0
        centre.z = lo.z
    for v in bm.verts:
        v.co = centre + m @ (v.co - centre)


def _bevel(bm, bevel):
    bmesh.ops.bevel(bm, geom=bm.edges[:], offset=bevel, offset_type="OFFSET", segments=1,
                    profile=0.5, affect="EDGES", clamp_overlap=True)


def P_box(sx, sy, sz, bevel=0.0):
    """Exact box centred on the origin.  bevel > 0 gives chunky chamfered edges."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co.x *= sx
        v.co.y *= sy
        v.co.z *= sz
    if bevel > 0.0:
        _bevel(bm, bevel)
    return bm


def P_block(sx, sy, sz, rough, rng, bevel=0.0, flat_bottom=False):
    """Hand-cut block: a box whose six face planes are each nudged and tilted a little.
    Corners are where three planes meet, so every face stays perfectly flat while the
    edges go slightly off-parallel.  rough ~0.02 is subtle, ~0.12 is a lumpy rock."""
    half = (sx / 2.0, sy / 2.0, sz / 2.0)
    cap = max(0.15, 1.5 * rough)          # never slant a face by more than ~9 degrees (more for rocks)
    planes = {}
    for axis in range(3):
        for sign in (-1, 1):
            # plane:  n . v = d  with n[axis] = +-1 exactly, so the face stays at +-d on the
            # part's centre line and only leans by up to rough * half[axis] at its corners
            n = Vector((0.0, 0.0, 0.0))
            n[axis] = float(sign)
            d = half[axis]
            if not (flat_bottom and axis == 2 and sign == -1):
                for other in range(3):
                    if other != axis:
                        lean = min(rough * half[axis] / half[other], cap)
                        n[other] = rng.uniform(-1.0, 1.0) * lean
                d = half[axis] * (1.0 + rng.uniform(-rough, rough))
            planes[(axis, sign)] = (n, d)
    bm = bmesh.new()
    verts = {}
    for kx in (-1, 1):
        for ky in (-1, 1):
            for kz in (-1, 1):
                (n0, d0), (n1, d1), (n2, d2) = planes[(0, kx)], planes[(1, ky)], planes[(2, kz)]
                m = Matrix((n0, n1, n2))
                verts[(kx, ky, kz)] = bm.verts.new(m.inverted() @ Vector((d0, d1, d2)))
    for axis in range(3):
        o1, o2 = [a for a in range(3) if a != axis]
        for sign in (-1, 1):
            quad = []
            for (a, b) in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
                key = [0, 0, 0]
                key[axis] = sign
                key[o1] = a
                key[o2] = b
                quad.append(verts[tuple(key)])
            bm.faces.new(quad)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    if bevel > 0.0:
        _bevel(bm, bevel)
    return bm


def P_lathe(profile, n, seg_mats=None, phase=0.0, cap_start=True, cap_end=True):
    """Revolve a profile of (radius, z) points around local Z with n sides.

    Trace the profile counter-clockwise in the (r, z) half plane (start at the bottom
    centre, go out, up, then back in) and every face points outward.  A point with
    radius 0 becomes a single apex vertex.  seg_mats gives a local material index per
    profile segment."""
    bm = bmesh.new()
    rings = []
    for (r, z) in profile:
        if r <= 1e-9:
            rings.append([bm.verts.new((0.0, 0.0, z))])
        else:
            rings.append([bm.verts.new((r * math.cos(phase + 2.0 * math.pi * j / n),
                                        r * math.sin(phase + 2.0 * math.pi * j / n), z)) for j in range(n)])
    for i in range(len(profile) - 1):
        a, b = rings[i], rings[i + 1]
        mi = seg_mats[i] if seg_mats else 0
        if len(a) == 1 and len(b) == 1:
            continue
        for j in range(n):
            j2 = (j + 1) % n
            if len(a) == 1:
                f = bm.faces.new((a[0], b[j2], b[j]))
            elif len(b) == 1:
                f = bm.faces.new((a[j], a[j2], b[0]))
            else:
                f = bm.faces.new((a[j], a[j2], b[j2], b[j]))
            f.material_index = mi
    if cap_start and len(rings[0]) > 1:
        f = bm.faces.new(list(reversed(rings[0])))
        f.material_index = seg_mats[0] if seg_mats else 0
    if cap_end and len(rings[-1]) > 1:
        f = bm.faces.new(rings[-1])
        f.material_index = seg_mats[-1] if seg_mats else 0
    return bm


def P_prism(points, depth, front=True, back=True):
    """Extrude a 2D outline given as (x, z) points along Y (front at -depth/2).
    The outline may be concave.  Front face points to -Y, back face to +Y."""
    pts = list(points)
    n = len(pts)
    area = sum(pts[i][0] * pts[(i + 1) % n][1] - pts[(i + 1) % n][0] * pts[i][1] for i in range(n)) / 2.0
    if area < 0.0:
        pts.reverse()
    bm = bmesh.new()
    fr = [bm.verts.new((x, -depth / 2.0, z)) for x, z in pts]
    bk = [bm.verts.new((x, +depth / 2.0, z)) for x, z in pts]
    if front:
        bm.faces.new(fr)
    if back:
        bm.faces.new(list(reversed(bk)))
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((fr[i], bk[i], bk[j], fr[j]))
    return bm


def P_cap(w, d, h, taper=0.82):
    """Key cap: tapered box with no bottom face."""
    bm = bmesh.new()
    corners = ((-1, -1), (1, -1), (1, 1), (-1, 1))
    lo = [bm.verts.new((sx * w / 2.0, sy * d / 2.0, 0.0)) for sx, sy in corners]
    hi = [bm.verts.new((sx * (w / 2.0 - (1.0 - taper) * d / 2.0), sy * d / 2.0 * taper, h)) for sx, sy in corners]
    bm.faces.new(hi)
    for i in range(4):
        j = (i + 1) % 4
        bm.faces.new((lo[i], lo[j], hi[j], hi[i]))
    return bm


def P_quad_uv(corners):
    """One flat quad with 0-1 UVs.  corners = bottom-left, bottom-right, top-right, top-left
    as seen from the side the face points to."""
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    verts = [bm.verts.new(c) for c in corners]
    face = bm.faces.new(verts)
    for loop, t in zip(face.loops, ((0.0, 0.0), (1.0, 0.0), (1.0, 1.0), (0.0, 1.0))):
        loop[uv].uv = t
    return bm


def P_leaf(length, width, thick, droop=0.15):
    """Chunky double-sided leaf growing along +Z from the origin, flat side facing -Y / +Y."""
    bm = bmesh.new()
    base = bm.verts.new((0.0, 0.0, 0.0))
    left = bm.verts.new((-width, -droop * length * 0.35, length * 0.47))
    right = bm.verts.new((width, -droop * length * 0.35, length * 0.47))
    tip = bm.verts.new((0.0, -droop * length, length))
    fr = bm.verts.new((0.0, -thick - droop * length * 0.35, length * 0.43))
    bk = bm.verts.new((0.0, +thick - droop * length * 0.35, length * 0.43))
    for tri in ((base, right, fr), (right, tip, fr), (tip, left, fr), (left, base, fr),
                (right, base, bk), (tip, right, bk), (left, tip, bk), (base, left, bk)):
        bm.faces.new(tri)
    return bm


def bar_matrix(a, b):
    """Matrix that places a part built along local Z between points a and b.  Returns (matrix, length)."""
    a = Vector(a)
    b = Vector(b)
    d = b - a
    q = Vector((0.0, 0.0, 1.0)).rotation_difference(d.normalized())
    return Matrix.Translation((a + b) / 2.0) @ q.to_matrix().to_4x4(), d.length


def axis_matrix(origin, direction):
    """Matrix that maps local +Z onto direction, with the local origin at origin."""
    q = Vector((0.0, 0.0, 1.0)).rotation_difference(Vector(direction).normalized())
    return Matrix.Translation(Vector(origin)) @ q.to_matrix().to_4x4()


class Prop:
    """Collects parts into one mesh with named material slots."""

    def __init__(self, name, seed, rough=0.02):
        self.name = name
        self.bm = bmesh.new()
        self.bm.loops.layers.uv.new("UVMap")
        self.slots = []
        self.rng = random.Random(seed)
        self.rough = rough
        self.parent = Matrix.Identity(4)
        self.post = None

    def slot(self, mat):
        if mat not in PALETTE:
            raise ValueError("unknown slot " + mat)
        if mat not in self.slots:
            self.slots.append(mat)
        return self.slots.index(mat)

    def add(self, part, mat, loc=(0.0, 0.0, 0.0), rot=(0.0, 0.0, 0.0), wob=0.0, lock_bottom=False,
            closed=True, mat4=None, drop=False):
        """Merge a part.  mat is a slot name, or a list of slot names indexed by the part's
        own material indices.  closed=True re-orients the shell so normals point outward.
        wob is the wonk amount in metres (see wonk())."""
        if isinstance(mat, str):
            idx = self.slot(mat)
            for f in part.faces:
                f.material_index = idx
        else:
            remap = [self.slot(m) for m in mat]
            for f in part.faces:
                f.material_index = remap[f.material_index]
        if closed:
            bmesh.ops.recalc_face_normals(part, faces=part.faces[:])
            if signed_volume(part) < 0.0:
                bmesh.ops.reverse_faces(part, faces=part.faces[:])
        wonk(part, wob, self.rng, lock_bottom)
        if mat4 is None:
            mat4 = Matrix.Translation(Vector(loc)) @ Euler(rot, "XYZ").to_matrix().to_4x4()
        bmesh.ops.transform(part, matrix=self.parent @ mat4, verts=part.verts[:])
        if drop:
            zmin = min(v.co.z for v in part.verts)
            target = (self.parent @ Vector(loc)).z
            for v in part.verts:
                v.co.z += target - zmin
        tmp = bpy.data.meshes.new("_rm_tmp")
        part.to_mesh(tmp)
        part.free()
        self.bm.from_mesh(tmp)
        bpy.data.meshes.remove(tmp)

    def box(self, size, mat, loc=(0.0, 0.0, 0.0), rot=(0.0, 0.0, 0.0), rough=None, bevel=0.0,
            flat_bottom=False, **kw):
        """Add a box.  rough=None uses the prop's default roughness, rough=0 gives an exact box."""
        rough = self.rough if rough is None else rough
        if rough > 0.0:
            part = P_block(size[0], size[1], size[2], rough, self.rng, bevel=bevel, flat_bottom=flat_bottom)
        else:
            part = P_box(size[0], size[1], size[2], bevel=bevel)
        self.add(part, mat, loc=loc, rot=rot, **kw)

    def build(self, coll, ground=True):
        bm = self.bm
        if self.post is not None:
            self.post(bm)
        if ground:
            for v in bm.verts:
                if v.co.z < 0.0:
                    v.co.z = 0.0
        bmesh.ops.triangulate(bm, faces=bm.faces[:], quad_method="BEAUTY", ngon_method="BEAUTY")
        bm.normal_update()
        old = bpy.data.meshes.get(self.name)
        if old is not None and old.users == 0:
            bpy.data.meshes.remove(old)
        me = bpy.data.meshes.new(self.name)
        bm.to_mesh(me)
        bm.free()
        me["remaster_prop"] = 1
        for s in self.slots:
            me.materials.append(get_mat(s))
        me.polygons.foreach_set("use_smooth", [False] * len(me.polygons))
        me.update()
        obj = bpy.data.objects.new(self.name, me)
        coll.objects.link(obj)
        return obj


# ------------------------------------------------------------------------------------------
# dungeon props (the "finished" parts of the remake)
# ------------------------------------------------------------------------------------------
def make_torch():
    """Wall sconce.  Origin = centre of the back plate where it touches the wall.
    The wall is on the +Y side, the torch sticks out toward -Y, the flame points up."""
    p = Prop("torch", 11, rough=0.05)
    p.box((0.15, 0.03, 0.27), "metal", loc=(0, -0.015, 0.0))
    p.box((0.045, 0.19, 0.040), "metal", loc=(0, -0.120, -0.010), rot=(R(-17), 0, 0))
    p.box((0.030, 0.142, 0.030), "metal", loc=(0, -0.085, -0.068), rot=(R(-39.3), 0, 0))
    lean = R(8)
    base = Vector((0.0, -0.190, -0.150))
    axis = Vector((0.0, -math.sin(lean), math.cos(lean)))

    def at(s):
        return tuple(base + axis * s)

    p.add(P_lathe([(0, 0), (0.020, 0), (0.030, 0.30), (0, 0.30)], 6), "wood", loc=at(0.0), rot=(lean, 0, 0), wob=0.003)
    p.add(P_lathe([(0, 0), (0.050, 0), (0.068, 0.07), (0, 0.07)], 6, phase=0.5), "metal", loc=at(0.14),
          rot=(lean, 0, 0), wob=0.004)
    p.add(P_lathe([(0, 0), (0.046, 0), (0.058, 0.09), (0, 0.09)], 6), "wood_dark", loc=at(0.26),
          rot=(lean, 0, 0), wob=0.004)
    fb = base + axis * 0.345
    p.add(P_lathe([(0, 0), (0.050, 0.0), (0.090, 0.075), (0.062, 0.155), (0, 0.275)], 5), "flame",
          loc=tuple(fb), rot=(R(3), R(-4), R(20)), wob=0.008)
    p.add(P_lathe([(0, 0), (0.034, 0.0), (0.048, 0.05), (0, 0.17)], 4), "flame",
          loc=(fb.x + 0.047, fb.y + 0.004, fb.z - 0.004), rot=(R(6), R(27), R(10)), wob=0.005)
    p.add(P_lathe([(0, 0), (0.030, 0.0), (0.044, 0.045), (0, 0.15)], 4), "flame",
          loc=(fb.x - 0.044, fb.y + 0.012, fb.z - 0.008), rot=(R(-14), R(-26), R(50)), wob=0.005)
    return p, dict(ground=False)


def make_pillar():
    p = Prop("pillar", 12, rough=0.035)
    p.box((0.70, 0.70, 0.16), "stone_dark", loc=(0, 0, 0.08), flat_bottom=True)
    p.box((0.59, 0.59, 0.11), "stone", loc=(0, 0, 0.205), rot=(0, 0, R(2.5)))
    z = 0.26
    drums = [(0.84, 0.250, 0.240, 5.0, 0.006, -0.004), (0.80, 0.240, 0.226, -11.0, -0.008, 0.005),
             (0.80, 0.226, 0.214, 17.0, 0.004, 0.007)]
    for (h, r1, r2, ang, dx, dy) in drums:
        p.add(P_lathe([(0, 0), (r1, 0), (r2, h), (0, h)], 8), "stone", loc=(dx, dy, z), rot=(0, 0, R(ang)), wob=0.012)
        z += h
    p.add(P_lathe([(0, 0), (0.290, 0), (0.290, 0.07), (0, 0.07)], 8, phase=R(22.5)), "stone_dark",
          loc=(0, 0, 0.262), wob=0.006)
    p.add(P_lathe([(0, 0), (0.262, 0), (0.262, 0.07), (0, 0.07)], 8, phase=R(22.5)), "stone_dark",
          loc=(0, 0, 2.622), wob=0.006)
    p.box((0.59, 0.59, 0.11), "stone", loc=(0, 0, 2.755), rot=(0, 0, R(-2.0)))
    p.box((0.70, 0.70, 0.19), "stone_dark", loc=(0, 0, 2.905))
    return p, dict(ground=True)


def arch_curve(half_w, spring, apex, segs):
    """Right half of a slightly pointed arch, from the spring line up to the apex."""
    rise = apex - spring
    c = (rise * rise - half_w * half_w) / (2.0 * half_w)
    rad = half_w + c
    th_max = math.acos(max(-1.0, min(1.0, c / rad)))
    return [(-c + rad * math.cos(th_max * i / segs), spring + rad * math.sin(th_max * i / segs))
            for i in range(segs + 1)]


def make_door_arch():
    """Stone archway frame, 2.0 wide x 3.0 tall x 0.5 deep.  Opening 1.4 x 2.4 with a pointed top."""
    p = Prop("door_arch", 13, rough=0.035)
    rng = p.rng
    right = arch_curve(0.70, 1.60, 2.40, 6)

    def nudge(pt, amt=0.010):
        return (pt[0] + rng.uniform(-amt, amt), pt[1] + rng.uniform(-amt, amt))

    left_side = [right[0]] + [nudge(q) for q in right[1:-1]] + [right[-1]]
    left_side = [(-x, z) for (x, z) in left_side]                    # spring line -> apex
    right_inner = [right[0]] + [nudge(q) for q in right[1:-1]]
    right_side = list(reversed(right_inner))                         # just below apex -> spring line
    outline = [(-1.0, 0.0), (-0.70, 0.0)] + left_side + right_side + [(0.70, 0.0), (1.0, 0.0), (1.0, 3.0), (-1.0, 3.0)]
    p.add(P_prism(outline, 0.50), "stone", loc=(0, 0, 0))
    p.add(P_prism([(-0.105, 2.345), (0.105, 2.345), (0.175, 2.865), (-0.175, 2.865)], 0.58), "stone_dark",
          loc=(0, 0, 0), wob=0.006)
    for sx in (-1.0, 1.0):
        p.box((0.31, 0.58, 0.14), "stone_dark", loc=(sx * 0.825, 0, 1.60), rot=(0, 0, R(1.2 * sx)))
        p.box((0.31, 0.58, 0.27), "stone_dark", loc=(sx * 0.825, 0, 0.135), flat_bottom=True)
        p.box((0.29, 0.54, 0.05), "stone", loc=(sx * 0.830, 0, 0.82), rot=(0, R(1.2 * sx), 0))
    return p, dict(ground=True)


def make_gate():
    """Iron portcullis filling a 2.0 x 3.0 opening, 0.12 thick, centred on the origin plane."""
    p = Prop("gate", 14, rough=0.0)
    rng = p.rng
    xs = [-0.84, -0.504, -0.168, 0.168, 0.504, 0.84]
    zs = [0.52, 1.21, 1.90, 2.58]
    half = 0.07 / 2.0
    for x in xs:
        xj = x + rng.uniform(-0.006, 0.006)
        yaw = R(rng.uniform(-5.0, 5.0))
        lean = R(rng.uniform(-0.25, 0.25))
        m = Matrix.Translation((xj, 0.0, 0.0)) @ Euler((0.0, lean, yaw), "XYZ").to_matrix().to_4x4()
        p.parent = m
        p.box((0.07, 0.07, 2.75), "metal", loc=(0, 0, 1.575))
        p.add(P_lathe([(0, 0), (half * math.sqrt(2.0), 0.20)], 4, phase=math.pi / 4.0, cap_end=False), "metal",
              closed=False)
        p.parent = Matrix.Identity(4)
    for i, z in enumerate(zs):
        p.box((2.0, 0.10, 0.08), "metal", loc=(0, 0, z + rng.uniform(-0.008, 0.008)),
              rot=(0, R(rng.uniform(-0.35, 0.35)), 0))
    p.box((2.0, 0.12, 0.14), "metal", loc=(0, 0, 2.93))
    for x in xs:
        for z in zs:
            for side in (-1.0, 1.0):
                p.add(P_lathe([(0.028, 0), (0, 0.010)], 4, phase=math.pi / 4.0, cap_start=False), "ink",
                      loc=(x, side * 0.050, z), rot=(R(-90.0 * side), 0, 0), closed=False)
    return p, dict(ground=True)


def make_crate():
    p = Prop("crate", 15, rough=0.02)
    s = 0.80
    t = 0.080
    post = t + 0.014
    hp = s / 2.0 - post / 2.0
    p.box((s - 0.06, s - 0.06, s - 0.05), "wood", loc=(0, 0, (s - 0.05) / 2.0 + 0.02), rough=0.01)
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.box((post, post, s + 0.004), "wood_dark", loc=(sx * hp, sy * hp, (s + 0.004) / 2.0), flat_bottom=True)
    span = s - 2.0 * post + 0.03
    hb = s / 2.0 - t / 2.0 - 0.007
    for zc in (t / 2.0 + 0.001, s - t / 2.0 - 0.006):
        for sgn in (-1, 1):
            p.box((span, t, t), "wood_dark", loc=(0, sgn * hb, zc))
            p.box((t, span, t), "wood_dark", loc=(sgn * hb, 0, zc))
    diag = (s - 2.0 * t) * 1.36
    off = (s - 0.06) / 2.0 + 0.006
    p.box((0.075, 0.020, diag), "wood_dark", loc=(0, -off, s / 2.0), rot=(0, R(45), 0))
    p.box((0.075, 0.020, diag), "wood_dark", loc=(0, off, s / 2.0), rot=(0, R(-45), 0))
    p.box((0.020, 0.075, diag), "wood_dark", loc=(-off, 0, s / 2.0), rot=(R(45), 0, 0))
    p.box((0.020, 0.075, diag), "wood_dark", loc=(off, 0, s / 2.0), rot=(R(-45), 0, 0))
    return p, dict(ground=True)


def make_barrel():
    p = Prop("barrel", 16)
    profile = [(0, 0), (0.270, 0), (0.330, 0.25), (0.350, 0.50), (0.330, 0.75), (0.280, 1.00),
               (0.240, 1.00), (0.240, 0.962), (0, 0.962)]
    p.add(P_lathe(profile, 10, seg_mats=[1, 0, 0, 0, 0, 0, 0, 1]), ["wood", "wood_dark"])

    def rad(z):
        pts = profile[1:6]
        for (r0, z0), (r1, z1) in zip(pts, pts[1:]):
            if z0 <= z <= z1:
                return r0 + (r1 - r0) * (z - z0) / (z1 - z0)
        return pts[-1][0]

    for (za, zb) in ((0.19, 0.27), (0.73, 0.81)):
        ra, rb = rad(za), rad(zb)
        hoop = [(ra - 0.004, za), (ra + 0.016, za), (rb + 0.016, zb), (rb - 0.004, zb)]
        p.add(P_lathe(hoop, 10, cap_start=False, cap_end=False), "metal", closed=False)

    def lean(bm):
        # one shared shear for staves and hoops, so the whole barrel sags a little to one side
        wonk(bm, 0.022, random.Random(161), lock_bottom=True)

    p.post = lean
    return p, dict(ground=True)


def banner_cloth(rng, w=0.90, top=-0.03, bottom=-1.80, notch=-1.45, thick=0.02):
    """Thin cloth slab with a swallow tail.  Folds run along rows only, so every quad stays flat."""
    bm = bmesh.new()
    cols = [-w / 2.0, 0.0, w / 2.0]
    rows = [top, -0.48, -0.92, -1.36]
    waves = [0.0, 0.010, -0.008, 0.007, 0.007]
    front, back = [], []
    for ri in range(5):
        fr, bk = [], []
        for ci, x in enumerate(cols):
            z = rows[ri] if ri < 4 else (notch if ci == 1 else bottom)
            xx = x * (1.0 - 0.010 * ri) + (rng.uniform(-0.006, 0.006) if ri > 0 else 0.0)
            fr.append(bm.verts.new((xx, -thick / 2.0 + waves[ri], z)))
            bk.append(bm.verts.new((xx, +thick / 2.0 + waves[ri], z)))
        front.append(fr)
        back.append(bk)
    for ri in range(4):
        for ci in range(2):
            a, b, c, d = front[ri][ci], front[ri][ci + 1], front[ri + 1][ci + 1], front[ri + 1][ci]
            bm.faces.new((d, c, b, a))
            a2, b2, c2, d2 = back[ri][ci], back[ri][ci + 1], back[ri + 1][ci + 1], back[ri + 1][ci]
            bm.faces.new((a2, b2, c2, d2))
    edges = []
    for ci in range(2):
        edges.append(((0, ci), (0, ci + 1)))
        edges.append(((4, ci), (4, ci + 1)))
    for ri in range(4):
        edges.append(((ri, 0), (ri + 1, 0)))
        edges.append(((ri, 2), (ri + 1, 2)))
    for (r0, c0), (r1, c1) in edges:
        bm.faces.new((front[r0][c0], front[r1][c1], back[r1][c1], back[r0][c0]))
    return bm


def make_banner():
    """Hanging banner, cloth 0.9 x 1.8.  Origin = top centre at the wall.  Wall on +Y, front faces -Y."""
    p = Prop("banner", 17)
    yc = -0.035
    rod, rod_len = bar_matrix((-0.53, yc, -0.045), (0.53, yc, -0.045))
    p.add(P_lathe([(0, -rod_len / 2), (0.022, -rod_len / 2), (0.022, rod_len / 2), (0, rod_len / 2)], 6), "wood",
          mat4=rod, wob=0.003)
    for sx in (-1.0, 1.0):
        knob, klen = bar_matrix((sx * 0.515, yc, -0.045), (sx * 0.600, yc, -0.045))
        p.add(P_lathe([(0, -klen / 2), (0.040, 0.0), (0, klen / 2)], 4, phase=math.pi / 4.0), "wood_dark",
              mat4=knob, wob=0.003)
    p.box((0.93, 0.034, 0.075), "cloth", loc=(0, yc, -0.050), rough=0.03)
    p.add(banner_cloth(p.rng), "purple", loc=(0, yc, 0))
    tower = [(-0.130, 0.00), (0.130, 0.00), (0.130, 0.30), (0.165, 0.30), (0.165, 0.43), (0.095, 0.43),
             (0.095, 0.365), (0.035, 0.365), (0.035, 0.43), (-0.035, 0.43), (-0.035, 0.365), (-0.095, 0.365),
             (-0.095, 0.43), (-0.165, 0.43), (-0.165, 0.30), (-0.130, 0.30)]
    tower = [(x + p.rng.uniform(-0.004, 0.004), z + p.rng.uniform(-0.004, 0.004)) for (x, z) in tower]
    p.add(P_prism(tower, 0.014), "yellow", loc=(0, yc - 0.030, -0.98))
    p.add(P_prism([(-0.035, 0.0), (0.035, 0.0), (0.035, 0.095), (0.0, 0.130), (-0.035, 0.095)], 0.016), "purple",
          loc=(0, yc - 0.033, -0.98))
    return p, dict(ground=False)


SIGN_ROLL_DEG = 3.0
SIGN_BOARD_CENTRE = (0.015, 0.070, 1.02)
SIGN_PAPER_HALF = (0.40, 0.25)


def make_signpost():
    """Wooden post with a slightly crooked notice board.  The paper face points to +Y."""
    p = Prop("signpost", 18, rough=0.03)
    roll = R(SIGN_ROLL_DEG)
    p.box((0.09, 0.09, 1.42), "wood", loc=(0.0, 0.0, 0.71), rot=(0, R(-2.0), 0))
    board_c = Vector(SIGN_BOARD_CENTRE)
    p.box((0.90, 0.05, 0.60), "wood_dark", loc=tuple(board_c), rot=(0, roll, 0), rough=0.015)
    hw, hh = SIGN_PAPER_HALF
    quad = P_quad_uv([(hw, 0, -hh), (-hw, 0, -hh), (-hw, 0, hh), (hw, 0, hh)])
    p.add(quad, "paper", loc=(board_c.x, board_c.y + 0.025 + 0.006, board_c.z), rot=(0, roll, 0), closed=False)
    for (sx, sy, sz, lx, ly, yaw) in ((0.20, 0.16, 0.12, -0.10, 0.03, 20), (0.16, 0.15, 0.10, 0.10, -0.05, -35),
                                      (0.13, 0.12, 0.09, 0.04, 0.11, 60)):
        p.box((sx, sy, sz), "stone_dark", loc=(lx, ly, sz / 2.0), rot=(0, 0, R(yaw)), rough=0.14, flat_bottom=True)
    return p, dict(ground=True)


def make_rubble():
    p = Prop("rubble", 19, rough=0.15)
    ground_blocks = [
        ((0.42, 0.34, 0.28), (-0.22, 0.05), 15, "stone"),
        ((0.36, 0.30, 0.24), (0.20, -0.10), -25, "stone_dark"),
        ((0.30, 0.26, 0.22), (0.08, 0.27), 40, "stone"),
        ((0.24, 0.22, 0.18), (-0.30, -0.27), -10, "stone_dark"),
        ((0.26, 0.20, 0.16), (0.39, 0.20), 70, "stone"),
        ((0.16, 0.14, 0.12), (0.03, -0.37), 5, "stone_dark"),
    ]
    for (size, xy, yaw, mat) in ground_blocks:
        p.box(size, mat, loc=(xy[0], xy[1], size[2] / 2.0), rot=(0, 0, R(yaw)), flat_bottom=True)
    p.box((0.30, 0.24, 0.20), "stone", loc=(-0.03, 0.03, 0.335), rot=(R(19), R(-13), R(30)))
    return p, dict(ground=True)


def make_placeholder_cube():
    """Plain 1 m grey-box cube (exact on purpose, so it scales and tiles predictably)."""
    p = Prop("placeholder_cube", 20, rough=0.0)
    p.box((1.0, 1.0, 1.0), "white", loc=(0, 0, 0.5), bevel=0.04)
    return p, dict(ground=True)


# ------------------------------------------------------------------------------------------
# developer's desk props.  The user sits on the -Y side looking toward +Y.
# ------------------------------------------------------------------------------------------
def make_desk():
    p = Prop("desk", 21, rough=0.02)
    p.box((1.60, 0.75, 0.045), "wood", loc=(0, 0, 0.7175))
    for sx in (-1.0, 1.0):
        p.box((0.045, 0.68, 0.70), "wood_dark", loc=(sx * 0.76, 0, 0.35), flat_bottom=True)
    p.box((1.48, 0.025, 0.36), "wood_dark", loc=(0, 0.30, 0.50))
    p.box((0.44, 0.62, 0.505), "wood", loc=(0.50, 0.0, 0.4475))
    for i in range(3):
        zc = 0.695 - 0.10 - i * 0.155
        p.box((0.38, 0.020, 0.135), "wood_dark", loc=(0.50, -0.318, zc), rot=(0, R(0.8 * (i - 1)), 0))
        p.box((0.11, 0.022, 0.024), "metal", loc=(0.50, -0.338, zc), rough=0.05)
    return p, dict(ground=True)


MONITOR_SCREEN = dict(half_w=0.31, z0=0.205, z1=0.575, y=-0.022)


def make_monitor():
    """Chunky monitor.  Origin = bottom centre of the stand.  The screen is ONE quad
    (slot "screen", 0.62 x 0.37, UVs 0-1) on the plane y = -0.022 facing -Y."""
    p = Prop("monitor", 22, rough=0.0)
    p.box((0.27, 0.19, 0.020), "metal", loc=(0, 0.030, 0.010), bevel=0.006, rough=0.03, flat_bottom=True)
    neck, nlen = bar_matrix((0, 0.029, 0.016), (0, 0.071, 0.314))
    p.add(P_block(0.055, 0.030, nlen, 0.03, p.rng), "metal", mat4=neck)
    p.box((0.68, 0.040, 0.43), "ink", loc=(0, 0, 0.385))
    p.box((0.42, 0.050, 0.26), "ink", loc=(0, 0.040, 0.38), bevel=0.02, rough=0.03)
    p.box((0.68, 0.012, 0.025), "ink", loc=(0, -0.026, 0.5875))
    p.box((0.68, 0.012, 0.035), "ink", loc=(0, -0.026, 0.1875))
    p.box((0.03, 0.012, 0.37), "ink", loc=(-0.325, -0.026, 0.39))
    p.box((0.03, 0.012, 0.37), "ink", loc=(0.325, -0.026, 0.39))
    s = MONITOR_SCREEN
    quad = P_quad_uv([(-s["half_w"], s["y"], s["z0"]), (s["half_w"], s["y"], s["z0"]),
                      (s["half_w"], s["y"], s["z1"]), (-s["half_w"], s["y"], s["z1"])])
    p.add(quad, "screen", closed=False)
    p.box((0.016, 0.006, 0.009), "teal", loc=(0.285, -0.0335, 0.187))
    return p, dict(ground=True)


def make_keyboard():
    """0.44 x 0.14 keyboard.  Front edge (nearest the user) is at -Y, back edge is raised."""
    p = Prop("keyboard", 23, rough=0.0)
    rng = p.rng
    width, depth = 0.44, 0.14
    p.box((width, depth, 0.016), "ink", loc=(0, 0, 0.008), rough=0.012, flat_bottom=True)
    rows_y = [0.047, 0.016, -0.015, -0.046]
    pitch, kw, kd, kh = 0.0405, 0.034, 0.025, 0.012

    def key(x, y, w, mat):
        p.add(P_cap(w, kd, kh + rng.uniform(-0.001, 0.001)), mat, loc=(x, y, 0.0150),
              rot=(0, 0, R(rng.uniform(-4.0, 4.0))), closed=False)

    for r in range(3):
        x0 = -(10 - 1) * pitch / 2.0 + (r - 1) * 0.004
        for i in range(10):
            mat = "white"
            if r == 0 and i == 0:
                mat = "red"
            if r == 1 and i == 9:
                mat = "teal"
            key(x0 + i * pitch, rows_y[r], kw, mat)
    for x in (-0.182, -0.1415):
        key(x, rows_y[3], kw, "white")
    p.add(P_cap(0.198, kd, kh), "white", loc=(-0.020, rows_y[3], 0.0150), rot=(0, 0, R(1.2)), closed=False)
    for x in (0.1025, 0.143, 0.1835):
        key(x, rows_y[3], kw, "white")

    def shear(bm):
        for v in bm.verts:
            if v.co.z > 0.001:
                v.co.z += (v.co.y + depth / 2.0) * 0.085

    p.post = shear
    return p, dict(ground=True)


def make_mouse():
    """Chunky mouse ~0.11 long.  Buttons end at +Y, palm end toward the user (-Y)."""
    p = Prop("mouse", 24, rough=0.0)
    body = P_box(0.062, 0.108, 0.032, bevel=0.011)
    for v in body.verts:
        if v.co.z > 0.0:
            v.co.x *= 0.82
            v.co.y = v.co.y * 0.90 - 0.004
            if v.co.y > 0.010:
                v.co.z -= 0.006
    p.add(body, "white", loc=(0, 0, 0.016))
    slope = -math.degrees(math.atan2(0.006, 0.0774))
    p.box((0.0026, 0.040, 0.003), "ink", loc=(0, 0.014, 0.0274), rot=(R(slope), 0, 0))
    p.box((0.008, 0.016, 0.008), "teal", loc=(0, 0.014, 0.0275), rot=(R(slope), 0, 0))
    return p, dict(ground=True)


def make_mug():
    """Coffee mug, 0.09 wide x 0.10 tall, handle on +X."""
    p = Prop("mug", 25, rough=0.04)
    profile = [(0, 0), (0.036, 0), (0.045, 0.100), (0.038, 0.100), (0.038, 0.086), (0, 0.086)]
    p.add(P_lathe(profile, 8, seg_mats=[0, 0, 0, 0, 1]), ["teal", "wood_dark"], wob=0.002, lock_bottom=True)
    p.box((0.034, 0.014, 0.012), "teal", loc=(0.058, 0, 0.078))
    p.box((0.034, 0.014, 0.012), "teal", loc=(0.056, 0, 0.030))
    p.box((0.012, 0.016, 0.066), "teal", loc=(0.076, 0, 0.054))
    return p, dict(ground=True)


def make_lamp():
    """Desk lamp ~0.5 tall.  The shade points down and toward -Y."""
    p = Prop("lamp", 26, rough=0.03)
    p.add(P_lathe([(0, 0), (0.085, 0), (0.085, 0.016), (0.060, 0.028), (0, 0.028)], 8), "metal",
          wob=0.003, lock_bottom=True)
    j0 = Vector((0.0, 0.0, 0.026))
    j1 = Vector((0.0, 0.115, 0.275))
    j2 = Vector((0.0, -0.065, 0.455))
    for (a, b) in ((j0, j1), (j1, j2)):
        m, length = bar_matrix(a, b)
        p.add(P_block(0.017, 0.017, length, 0.03, p.rng), "metal", mat4=m)
    for j in (j1, j2):
        m, length = bar_matrix(j + Vector((-0.02, 0, 0)), j + Vector((0.02, 0, 0)))
        p.add(P_lathe([(0, -length / 2), (0.021, -length / 2), (0.021, length / 2), (0, length / 2)], 6), "ink",
              mat4=m, wob=0.001)
    d = Vector((0.0, -0.42, -0.91)).normalized()
    shade = [(0, 0), (0.035, 0), (0.100, 0.150), (0.091, 0.150), (0.028, 0.020), (0, 0.020)]
    p.add(P_lathe(shade, 8, seg_mats=[0, 0, 0, 1, 1]), ["metal", "yellow"], mat4=axis_matrix(j2 + d * 0.012, d),
          wob=0.004)
    p.add(P_lathe([(0, 0), (0.026, 0.028), (0, 0.058)], 6), "yellow", mat4=axis_matrix(j2 + d * 0.050, d), wob=0.001)
    return p, dict(ground=True)


CLOCK_SCREEN = dict(half_w=0.0625, z0=0.0235, z1=0.0785, y=-0.037)


def make_clock():
    """Digital alarm clock 0.16 x 0.09 x 0.07.  The digits quad (slot "screen", UVs 0-1) faces -Y."""
    p = Prop("clock", 27, rough=0.0)
    p.box((0.16, 0.07, 0.09), "ink", loc=(0, 0, 0.051), bevel=0.008)
    for sx in (-1.0, 1.0):
        p.box((0.022, 0.050, 0.008), "ink", loc=(sx * 0.055, 0, 0.004))
    s = CLOCK_SCREEN
    quad = P_quad_uv([(-s["half_w"], s["y"], s["z0"]), (s["half_w"], s["y"], s["z0"]),
                      (s["half_w"], s["y"], s["z1"]), (-s["half_w"], s["y"], s["z1"])])
    p.add(quad, "screen", closed=False)
    p.box((0.070, 0.030, 0.009), "red", loc=(0, 0.002, 0.0995), rough=0.04)
    return p, dict(ground=True)


def make_plant():
    p = Prop("plant", 28)
    rng = p.rng
    pot = [(0, 0), (0.058, 0), (0.082, 0.100), (0.092, 0.100), (0.092, 0.125), (0.072, 0.125), (0.072, 0.112),
           (0, 0.112)]
    p.add(P_lathe(pot, 8, seg_mats=[0, 0, 0, 0, 0, 0, 1]), ["wood", "wood_dark"], wob=0.004, lock_bottom=True)
    for i in range(6):
        az = i * 60.0 + rng.uniform(-14.0, 14.0)
        tilt = rng.uniform(34.0, 56.0)
        length = rng.uniform(0.165, 0.225)
        rad = 0.022
        loc = (rad * math.sin(R(az)), -rad * math.cos(R(az)), 0.108)
        p.add(P_leaf(length, length * 0.24, 0.007), "green", loc=loc, rot=(R(tilt), 0, R(az)))
    p.add(P_leaf(0.24, 0.052, 0.008, droop=0.06), "green", loc=(0.004, 0.0, 0.108), rot=(R(5), R(3), R(35)))
    return p, dict(ground=True)


def make_books():
    """A skewed pile of four books lying flat."""
    p = Prop("books", 29, rough=0.012)
    specs = [(0.170, 0.240, 0.036, "purple", 0.0, (0.0, 0.0)), (0.150, 0.215, 0.030, "teal", 9.0, (0.012, -0.006)),
             (0.160, 0.225, 0.034, "red", -7.0, (-0.010, 0.008)), (0.130, 0.190, 0.026, "yellow", 16.0, (0.018, 0.004))]
    z = 0.0
    c = 0.004
    for i, (w, d, h, mat, yaw, off) in enumerate(specs):
        p.parent = Matrix.Translation(Vector((off[0], off[1], z))) @ Matrix.Rotation(R(yaw), 4, "Z")
        p.box((w - c, d, c), mat, loc=(c / 2.0, 0, c / 2.0), rough=0.0)
        p.box((w - c, d, c), mat, loc=(c / 2.0, 0, h - c / 2.0), rough=0.0)
        p.box((c, d, h), mat, loc=(-w / 2.0 + c / 2.0, 0, h / 2.0), rough=0.0)
        p.box((w - 0.008, d - 0.016, h - 2.0 * c + 0.001), "paper", loc=(0.001, 0, h / 2.0))
        z += h + 0.0005
    p.parent = Matrix.Identity(4)
    return p, dict(ground=True)


# ------------------------------------------------------------------------------------------
# things that are NEW in the remake: the old game has never heard of them
# ------------------------------------------------------------------------------------------
def make_guard():
    """Keep guard: a stubby toy knight with a spear and a shield.  1.38 to the top of the helmet,
    about 2.0 with the spear.  Origin on the ground between the feet, face toward -Y."""
    p = Prop("guard", 31, rough=0.04)
    for sx in (-1.0, 1.0):
        p.box((0.26, 0.38, 0.16), "ink", loc=(sx * 0.20, -0.03, 0.08), bevel=0.03, flat_bottom=True)
    # body, belt, and a red tabard with a yellow diamond so the front reads from far away
    p.box((0.78, 0.58, 0.74), "metal", loc=(0, 0, 0.53), bevel=0.07)
    p.box((0.81, 0.61, 0.09), "ink", loc=(0, 0, 0.36), rough=0.02)
    p.box((0.36, 0.05, 0.46), "red", loc=(0, -0.295, 0.62), rough=0.03)
    p.box((0.15, 0.04, 0.15), "yellow", loc=(0, -0.325, 0.68), rot=(0, R(45), 0), rough=0.0)
    for sx in (-1.0, 1.0):
        p.box((0.24, 0.42, 0.20), "metal", loc=(sx * 0.46, 0, 0.84), rot=(0, R(11 * sx), 0), bevel=0.04)
    # helmet: one dark slit, two bright eyes, a red crest front to back
    p.box((0.50, 0.46, 0.09), "ink", loc=(0, 0, 0.93), rough=0.02)
    p.box((0.58, 0.54, 0.44), "metal", loc=(0, -0.01, 1.16), bevel=0.08)
    p.box((0.44, 0.05, 0.14), "ink", loc=(0, -0.265, 1.17), rough=0.0)
    for sx in (-1.0, 1.0):
        p.box((0.095, 0.03, 0.095), "flame", loc=(sx * 0.105, -0.292, 1.17), rough=0.0)
    p.add(P_prism([(-0.29, 1.35), (-0.25, 1.53), (-0.04, 1.61), (0.19, 1.56), (0.30, 1.35)], 0.09), "red",
          rot=(0, 0, R(90)), wob=0.006)
    # spear in the right hand
    p.box((0.055, 0.055, 1.74), "wood_dark", loc=(0.57, -0.12, 0.89), rot=(R(-2.0), R(1.5), 0), rough=0.03)
    p.box((0.17, 0.17, 0.15), "ink", loc=(0.55, -0.12, 0.70), bevel=0.03)
    p.box((0.11, 0.11, 0.06), "yellow", loc=(0.575, -0.15, 1.73), rough=0.0)
    p.add(P_lathe([(0, 0), (0.085, 0.0), (0.095, 0.06), (0, 0.33)], 4, phase=math.pi / 4.0), "white",
          loc=(0.578, -0.155, 1.76), rot=(R(-2.0), R(1.5), 0), wob=0.004)
    # shield on the left arm, turned a little outward
    p.parent = Matrix.Translation((-0.57, -0.25, 0.62)) @ Euler((0, 0, R(-24.0)), "XYZ").to_matrix().to_4x4()
    p.box((0.56, 0.05, 0.68), "ink", loc=(0, 0.02, 0), rough=0.02)
    p.box((0.48, 0.07, 0.60), "red", loc=(0, -0.01, 0), rough=0.03)
    p.box((0.16, 0.05, 0.16), "yellow", loc=(0, -0.055, 0.02), rot=(0, R(45), 0), rough=0.0)
    p.parent = Matrix.Identity(4)
    p.box((0.17, 0.17, 0.15), "ink", loc=(-0.50, -0.10, 0.62), bevel=0.03)
    return p, dict(ground=True)


def make_key():
    """Big cartoon key, 0.73 tall: bow on top, bit pointing +X.  Flat in the XZ plane so it
    flashes as it spins.  Origin at its centre."""
    p = Prop("key", 33, rough=0.03)
    bow_r, bow_z, n = 0.135, 0.215, 8
    seg = 2.0 * bow_r * math.tan(math.pi / n) + 0.06
    for i in range(n):
        a = 2.0 * math.pi * i / n
        p.box((seg, 0.085, 0.078), "yellow", loc=(bow_r * math.sin(a), 0, bow_z + bow_r * math.cos(a)),
              rot=(0, a, 0), rough=0.03)
    p.box((0.078, 0.078, 0.46), "yellow", loc=(0, 0, -0.125), rough=0.03)
    p.box((0.14, 0.105, 0.055), "yellow", loc=(0, 0, 0.055), rough=0.02)
    p.box((0.16, 0.072, 0.075), "yellow", loc=(0.105, 0, -0.315), rough=0.03)
    p.box((0.115, 0.072, 0.062), "yellow", loc=(0.085, 0, -0.195), rough=0.03)
    return p, dict(ground=False)


DUNGEON = [make_torch, make_pillar, make_door_arch, make_gate, make_crate, make_barrel, make_banner,
           make_signpost, make_rubble, make_placeholder_cube]
DESK = [make_desk, make_monitor, make_keyboard, make_mouse, make_mug, make_lamp, make_clock, make_plant, make_books]
DUNGEON_NAMES = ["torch", "pillar", "door_arch", "gate", "crate", "barrel", "banner", "signpost", "rubble",
                 "placeholder_cube"]
DESK_NAMES = ["desk", "monitor", "keyboard", "mouse", "mug", "lamp", "clock", "plant", "books"]

# preview height offsets for wall-mounted props, and a suggested arrangement on the desk
# (Blender coordinates relative to the desk origin; yaw in degrees about +Z)
ROW_Z = {"torch": 1.30, "banner": 2.40}
DESK_TOP = 0.74
ASSEMBLY = {
    "desk": ((0.0, 0.0, 0.0), 0.0),
    "monitor": ((0.0, 0.14, DESK_TOP), 0.0),
    "keyboard": ((0.0, -0.17, DESK_TOP), 0.0),
    "mouse": ((0.33, -0.17, DESK_TOP), -6.0),
    "mug": ((-0.36, -0.12, DESK_TOP), 30.0),
    "lamp": ((-0.60, 0.20, DESK_TOP), 40.0),
    "clock": ((0.47, 0.17, DESK_TOP), -18.0),
    "plant": ((0.68, 0.24, DESK_TOP), 0.0),
    "books": ((0.61, -0.11, DESK_TOP), 15.0),
}
ASSEMBLY_ORIGIN = Vector((40.0, 14.0, 0.0))


# ------------------------------------------------------------------------------------------
# scene, export, preview
# ------------------------------------------------------------------------------------------
def get_scene():
    sc = bpy.data.scenes.get(SCENE_NAME)
    if sc is None:
        sc = bpy.data.scenes.new(SCENE_NAME)
    for obj in list(sc.collection.all_objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for coll in list(sc.collection.children):
        sc.collection.children.unlink(coll)
        if coll.users == 0:
            bpy.data.collections.remove(coll)
    for me in list(bpy.data.meshes):
        if me.users == 0 and me.get("remaster_prop"):
            bpy.data.meshes.remove(me)
    sc.unit_settings.system = "METRIC"
    sc.unit_settings.scale_length = 1.0
    wm = bpy.context.window_manager
    if wm is not None and len(wm.windows) > 0:
        wm.windows[0].scene = sc
    return sc


def new_collection(sc, name):
    coll = bpy.data.collections.get(name)
    if coll is None:
        coll = bpy.data.collections.new(name)
    if coll.name not in sc.collection.children:
        sc.collection.children.link(coll)
    return coll


def to_unity(v):
    """Blender (x, y, z) -> Unity (-x, z, -y)."""
    return [round(-v[0], 4), round(v[2], 4), round(-v[1], 4)]


def stats_of(obj):
    me = obj.data
    xs = [v.co.x for v in me.vertices]
    ys = [v.co.y for v in me.vertices]
    zs = [v.co.z for v in me.vertices]
    flat = True
    out = {
        "tris": len(me.polygons),
        "verts": len(me.vertices),
        "size_unity_xyz": [round(max(xs) - min(xs), 3), round(max(zs) - min(zs), 3), round(max(ys) - min(ys), 3)],
        "min_unity": [round(-max(xs), 3), round(min(zs), 3), round(-max(ys), 3)],
        "max_unity": [round(-min(xs), 3), round(max(zs), 3), round(-min(ys), 3)],
        "slots": [m.name for m in me.materials],
        "non_tri_faces": sum(1 for p in me.polygons if len(p.vertices) != 3),
        "degenerate_tris": sum(1 for p in me.polygons if p.area < 1e-9),
    }
    quads = {}
    for p in me.polygons:
        name = me.materials[p.material_index].name
        if name in ("screen", "paper") and obj.name in ("monitor", "clock", "signpost"):
            for vi in p.vertices:
                quads.setdefault(name, set()).add(tuple(round(c, 4) for c in me.vertices[vi].co))
    for name, pts in quads.items():
        out[name + "_quad_unity"] = sorted(to_unity(q) for q in pts)
    return out


def build_all(sc):
    props = new_collection(sc, "RM_Props")
    asm = new_collection(sc, "RM_DeskAssembly")
    objs = {}
    for row_y, row_x, makers, gap in ((0.0, 0.0, DUNGEON, 0.55), (-6.0, -40.0, DESK, 0.16)):
        x = row_x
        for maker in makers:
            prop, opts = maker()
            obj = prop.build(props, ground=opts.get("ground", True))
            xs = [v.co.x for v in obj.data.vertices]
            x -= min(xs)
            obj.location = (x, row_y, ROW_Z.get(obj.name, 0.0))
            x += max(xs) + gap
            objs[obj.name] = obj
    for name, (loc, yaw) in ASSEMBLY.items():
        dup = bpy.data.objects.new(name + "_on_desk", objs[name].data)
        dup.location = ASSEMBLY_ORIGIN + Vector(loc)
        dup.rotation_euler = (0.0, 0.0, R(yaw))
        asm.objects.link(dup)
    return objs


def export_all(sc, objs, out_dir=OUT_DIR):
    os.makedirs(out_dir, exist_ok=True)
    vl = sc.view_layers[0]
    wm = bpy.context.window_manager
    win = wm.windows[0] if (wm is not None and len(wm.windows) > 0) else None
    written = []
    for name, obj in objs.items():
        keep = obj.location.copy()
        obj.location = (0.0, 0.0, 0.0)
        for o in sc.collection.all_objects:
            o.select_set(False, view_layer=vl)
        obj.select_set(True, view_layer=vl)
        vl.objects.active = obj
        vl.update()
        path = os.path.join(out_dir, name + ".fbx")
        kwargs = dict(filepath=path, use_selection=True, object_types={"MESH"}, use_mesh_modifiers=True,
                      mesh_smooth_type="FACE", axis_forward="-Z", axis_up="Y", apply_unit_scale=True,
                      apply_scale_options="FBX_SCALE_UNITS", bake_space_transform=True, bake_anim=False,
                      add_leaf_bones=False, use_custom_props=False, embed_textures=False, path_mode="AUTO")
        if win is not None:
            with bpy.context.temp_override(window=win, scene=sc, view_layer=vl):
                bpy.ops.export_scene.fbx(**kwargs)
        else:
            with bpy.context.temp_override(scene=sc, view_layer=vl):
                bpy.ops.export_scene.fbx(**kwargs)
        obj.location = keep
        written.append(path)
    vl.update()
    return written


def look_at(obj, target):
    direction = Vector(target) - obj.location
    obj.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()


def setup_preview(sc):
    extra = new_collection(sc, "RM_Preview")
    cam_data = bpy.data.cameras.get("RM_Camera") or bpy.data.cameras.new("RM_Camera")
    cam = bpy.data.objects.get("RM_Camera")
    if cam is None:
        cam = bpy.data.objects.new("RM_Camera", cam_data)
    if cam.name not in extra.objects:
        extra.objects.link(cam)
    sc.camera = cam
    world = bpy.data.worlds.get("RM_World") or bpy.data.worlds.new("RM_World")
    world.color = tuple(srgb_to_lin(c) for c in (0.953, 0.918, 0.843))
    sc.world = world
    try:
        sc.render.engine = "BLENDER_WORKBENCH"
    except TypeError:
        pass
    shading = sc.display.shading
    shading.light = "STUDIO"
    shading.color_type = "MATERIAL"
    shading.show_object_outline = True
    shading.object_outline_color = (0.10, 0.08, 0.16)
    shading.show_cavity = True
    try:
        shading.cavity_type = "BOTH"
    except TypeError:
        pass
    shading.show_shadows = False
    sc.render.resolution_percentage = 100
    sc.render.image_settings.file_format = "PNG"
    sc.render.film_transparent = False
    cam_data.clip_start = 0.01
    cam_data.clip_end = 300.0
    return cam


def render_shot(sc, cam, path, loc, target, lens, size=(1500, 800)):
    cam.location = loc
    cam.data.lens = lens
    look_at(cam, target)
    sc.render.resolution_x, sc.render.resolution_y = size
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True, scene=sc.name)
    return path


def render_previews(sc, objs, preview_dir):
    os.makedirs(preview_dir, exist_ok=True)
    cam = setup_preview(sc)

    def row_span(names):
        los, his = [], []
        for n in names:
            o = objs[n]
            xs = [v.co.x for v in o.data.vertices]
            los.append(o.location.x + min(xs))
            his.append(o.location.x + max(xs))
        return min(los), max(his)

    shots = []
    lo, hi = row_span(DUNGEON_NAMES)
    mid = (lo + hi) / 2.0
    shots.append(("props_dungeon_front.png", (mid - 1.5, -15.5, 3.4), (mid, 0.0, 1.35), 30.0))
    shots.append(("props_dungeon_back.png", (mid + 2.0, 15.0, 4.0), (mid, 0.0, 1.35), 30.0))
    lo, hi = row_span(DESK_NAMES)
    mid = (lo + hi) / 2.0
    shots.append(("props_desk_row.png", (mid - 0.4, -10.4, 1.9), (mid, -6.0, 0.30), 32.0))
    ao = ASSEMBLY_ORIGIN
    shots.append(("props_desk_assembled.png", (ao.x - 0.9, ao.y - 1.9, 1.75), (ao.x, ao.y + 0.05, 0.86), 38.0))
    shots.append(("props_desk_first_person.png", (ao.x, ao.y - 0.72, 1.22), (ao.x, ao.y + 0.14, 1.02), 24.0))
    return [render_shot(sc, cam, os.path.join(preview_dir, f), loc, target, lens) for (f, loc, target, lens) in shots]


def main(export=True, preview=True, preview_dir=None, out_dir=OUT_DIR):
    sc = get_scene()
    objs = build_all(sc)
    out = {"props": {name: stats_of(obj) for name, obj in objs.items()}}
    if export:
        out["fbx"] = export_all(sc, objs, out_dir)
    if preview:
        out["previews"] = render_previews(sc, objs, preview_dir or DEFAULT_PREVIEW_DIR)
    return out


if __name__ == "__main__":
    print(main())
