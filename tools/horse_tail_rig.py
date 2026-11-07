#!/usr/bin/env python3
"""Give the generated horse's long tail bones of its own. LAGP's four-legged rig has its tail bones running straight back from the rump,
so a long hanging tail gets weighted to the nearest bones - the hind legs - and flaps with them. This finds the tail (the black hair
connected to the dock, kept apart from the black socks by the gap the concept leaves between tail and leg), puts a chain of bones
down its middle (tail_hang1..N, under the rig's first tail bone) and weights the tail to those alone. The game swings that chain
with spring physics (TailBones in the game's code). It also adds a "seat" bone on the saddle (found by its white blanket) where the game puts the rider. Everything else - mesh, other weights, the clips - is kept.
Usage: python tools/horse_tail_rig.py [--src C:/AI/outputs/battle_blood/models/horse/final/horse.glb] [--bones 4] [--dark 0.16]
Writes final/horse.fbx and final/horse.glb (the rig as LAGP made it kept in final/as_rigged/), renders a check picture
(tail in red) to Build/clip_sheets/horse_tail_weights.png. Ends with OK: / FAIL:"""
import argparse, os, shutil, subprocess, sys, tempfile, time
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BLENDER = r"C:\AI\blender\blender.exe"
FINAL = r"C:\AI\outputs\battle_blood\models\horse\final"

SCRIPT = r'''
import bpy, bmesh, sys, json, math
import numpy as np
from mathutils import Vector
args = sys.argv[sys.argv.index("--") + 1:]
src, out, png, nb, dark = args[0], args[1], args[2], int(args[3]), float(args[4])
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
arm = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
mesh = next(o for o in bpy.context.scene.objects if o.type == "MESH")
me = mesh.data
mw = mesh.matrix_world
bones = arm.data.bones
def head(n): return arm.matrix_world @ bones[n].head_local
hips, headb = head("hips"), head("head")
fwd = headb - hips; fwd.z = 0; fwd.normalize()
root_name = "tail1" if "tail1" in bones else "hips"
dock = head(root_name)

# Texture colour at each vertex (the tail hair is black).
img = None
for slot in mesh.material_slots:
    if slot.material and slot.material.use_nodes:
        for nd in slot.material.node_tree.nodes:
            if nd.type == "TEX_IMAGE" and nd.image: img = nd.image; break
if img is None: print("FAIL: no texture on the horse"); sys.exit(1)
w, h = img.size
px = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)
uvl = me.uv_layers.active.data
nv = len(me.vertices)
lum = np.ones(nv); cnt = np.zeros(nv); acc = np.zeros(nv)
for poly in me.polygons:
    for li in poly.loop_indices:
        vi = me.loops[li].vertex_index
        u, v = uvl[li].uv
        x = min(w - 1, max(0, int(u * w))); y = min(h - 1, max(0, int(v * h)))
        c = px[y, x]
        acc[vi] += 0.3 * c[0] + 0.59 * c[1] + 0.11 * c[2]; cnt[vi] += 1
lum = np.where(cnt > 0, acc / np.maximum(cnt, 1), 1)
pos = [mw @ v.co for v in me.vertices]
zs = [p.z for p in pos]; z0, z1 = min(zs), max(zs)
back = [(p - hips).dot(-fwd) for p in pos]
hip_back = 0.0

# Colour smoothed over each vertex's neighbours: hair strands are noisy.
adj = [[] for _ in range(nv)]
for e in me.edges:
    a_, b_ = e.vertices; adj[a_].append(b_); adj[b_].append(a_)
slum = lum.copy()
for _ in range(3):
    slum = np.array([(slum[i] + sum(slum[j] for j in adj[i])) / (1 + len(adj[i])) for i in range(nv)])
H = z1 - z0
# Where the hind legs are, at each height: the furthest-back point of their bone axes.
segs = []
for n in ("legBL_upper", "legBL_lower", "legBR_upper", "legBR_lower"):
    if n in bones: segs.append((arm.matrix_world @ bones[n].head_local, arm.matrix_world @ bones[n].tail_local))
def leg_back(z):
    best = -9.0
    for p, q in segs:
        lo, hi = min(p.z, q.z), max(p.z, q.z)
        if lo <= z <= hi and hi - lo > 1e-4:
            t = (z - p.z) / (q.z - p.z); x = p.lerp(q, t)
            best = max(best, (x - hips).dot(-fwd))
    if best < -8.0: best = max((p - hips).dot(-fwd) for p, q in segs)
    return best
cand = [i for i in range(nv) if slum[i] < dark and back[i] > leg_back(pos[i].z) + 0.12 and pos[i].z > z0 + 0.12 * H]
if len(cand) < 30: print("FAIL: found no tail behind the hind legs (%d vertices)" % len(cand)); sys.exit(1)
# Its middle line, top to bottom, from slices of those.
czs = sorted(pos[i].z for i in cand)
top, bot = czs[-1], czs[0]
line = []
for k in range(9):
    zc = top - (top - bot) * k / 8
    sl = [pos[i] for i in cand if abs(pos[i].z - zc) < (top - bot) / 12]
    if sl: line.append(sum(sl, Vector()) / len(sl))
dock = line[0]
def near_line(p):
    best = 9.0
    for k in range(len(line) - 1):
        a_, b_ = line[k], line[k + 1]; ab = b_ - a_
        t = max(0.0, min(1.0, (p - a_).dot(ab) / max(1e-6, ab.length_squared)))
        best = min(best, (a_ + ab * t - p).length)
    return best
tail = [i for i in range(nv) if back[i] > leg_back(pos[i].z) + 0.05 and pos[i].z < top + 0.04
        and (near_line(pos[i]) < 0.09 or (near_line(pos[i]) < 0.26 and slum[i] < dark * 1.2))]
# Between the hind legs, behind the hips: the tail hangs there too, pressed against their inner sides.
side_axis = fwd.cross(Vector((0, 0, 1))).normalized()
legx = [abs((p - hips).dot(side_axis)) for p, q in segs] + [abs((q - hips).dot(side_axis)) for p, q in segs]
gap = max(0.05, min(legx) * 0.55) if legx else 0.08
ts = set(tail)
for i in range(nv):
    if i in ts: continue
    lat = abs((pos[i] - hips).dot(side_axis))
    if lat < gap and back[i] > 0.08 and z0 + 0.12 * H < pos[i].z < top - 0.05 and slum[i] < dark * 1.25: ts.add(i)
tail = sorted(ts)
print("GAP between the hind legs %.2f m, legs at %s" % (gap, [round(x, 2) for x in legx]))
tb = [back[i] for i in tail]
print("TAIL", len(tail), "vertices, behind the hips from %.2f to %.2f m, height %.2f to %.2f" % (min(tb), max(tb), min(pos[i].z for i in tail), max(pos[i].z for i in tail)))
if len(tail) < 30: print("FAIL: tail too small"); sys.exit(1)

# Cut the faces that join tail to leg: their skin would be pulled into a sheet as the leg moves. The gaps are inside the hair.
legs = {g.index for g in mesh.vertex_groups if g.name.startswith("legB")}
def leg_owned(i):
    gs = sorted(me.vertices[i].groups, key=lambda g: -g.weight)
    return bool(gs) and gs[0].group in legs
tset = set(tail)
bm = bmesh.new(); bm.from_mesh(me); bm.verts.ensure_lookup_table()
cut = [f for f in bm.faces if any(v.index in tset for v in f.verts) and any((v.index not in tset) and leg_owned(v.index) for v in f.verts)]
bmesh.ops.delete(bm, geom=cut, context="FACES_ONLY")
bm.to_mesh(me); bm.free(); me.update()
print("CUT", len(cut), "faces between tail and hind legs")

# The tail's middle line: from the dock to its tip, in bands of distance.
tip = max(tail, key=lambda i: (pos[i] - dock).length)
L = (pos[tip] - dock).length
d = {i: (pos[i] - dock).length / L for i in tail}
pts = [dock]
for k in range(1, nb + 1):
    lo, hi = (k - 0.5) / nb, min(1.0, (k + 0.5) / nb)
    band = [pos[i] for i in tail if lo <= d[i] <= hi] or [pos[tip]]
    pts.append(sum(band, Vector()) / len(band))
pts[-1] = pts[-1] + (pts[-1] - pts[-2]) * 0.15

# The saddle: the white blanket under it, on the back; its middle along the horse is the middle of the saddle.
# The seat is the top of the saddle there, along the spine line.
sat = np.zeros(nv)
blanket = [i for i in range(nv) if lum[i] > 0.62 and pos[i].z > z0 + 0.65 * H]
if len(blanket) < 20: print("FAIL: no saddle blanket found"); sys.exit(1)
bc = sum((pos[i] for i in blanket), Vector()) / len(blanket)
# On the horse's midline (the spine bones' line), the saddle's outline along the back: the highest point of each thin slice.
# Its front and back peaks (pommel, cantle) are the two highest on either side of the blanket's middle; the seat is the lowest between.
lat_axis = fwd.cross(Vector((0, 0, 1))).normalized()
mid_lat = (head("spine") - hips).dot(lat_axis) if "spine" in bones else 0.0
centre = (bc - hips).dot(fwd)
bins = 24; span = 0.42
topz = [None] * bins
for i in range(nv):
    dv = pos[i] - hips
    if abs(dv.dot(lat_axis) - mid_lat) > 0.06: continue
    a_ = dv.dot(fwd) - centre
    if abs(a_) >= span: continue
    b_ = int((a_ + span) / (2 * span) * bins)
    if topz[b_] is None or pos[i].z > topz[b_]: topz[b_] = pos[i].z
def peak(lo, hi):
    best = None
    for b_ in range(lo, hi):
        if topz[b_] is not None and (best is None or topz[b_] > topz[best]): best = b_
    return best
back_pk, front_pk = peak(0, bins // 2), peak(bins // 2, bins)
if back_pk is None or front_pk is None: print("FAIL: no saddle outline on the midline"); sys.exit(1)
low = min((b_ for b_ in range(back_pk, front_pk + 1) if topz[b_] is not None), key=lambda b_: topz[b_])
along = centre - span + (low + 0.5) * 2 * span / bins
seat_pt = hips + fwd * along + lat_axis * mid_lat
seat_pt.z = topz[low]
# Centred across the horse: halfway between its two sides in the slice under the seat (the spine bones need not run down its middle).
side_vals = [(pos[i] - hips).dot(lat_axis) for i in range(nv)
             if abs((pos[i] - seat_pt).dot(fwd)) < 0.06 and seat_pt.z - 0.45 < pos[i].z < seat_pt.z - 0.15]
if len(side_vals) > 10:
    centre_lat = (min(side_vals) + max(side_vals)) / 2
    seat_pt = seat_pt + lat_axis * (centre_lat - mid_lat)
    print("SEAT across: the spine line is %.3f m from the body's middle" % (mid_lat - centre_lat))
print("PROFILE", " ".join("%.2f" % (z - z0) if z is not None else "-" for z in topz), "back peak", back_pk, "front peak", front_pk, "seat", low)
print("SEAT %.2f m up, %.2f m ahead of the hips bone (blanket of %d vertices)" % (seat_pt.z - z0, (seat_pt - hips).dot(fwd), len(blanket)))

# Bones down that line, under the first tail bone.
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="EDIT")
eb = arm.data.edit_bones
inv = arm.matrix_world.inverted()
parent = eb[root_name]
names = []
for k in range(nb):
    b = eb.new("tail_hang%d" % (k + 1))
    b.head = inv @ pts[k]; b.tail = inv @ pts[k + 1]
    b.parent = parent; b.use_connect = k > 0
    parent = b; names.append(b.name)
sb = eb.new("seat")
sb.head = inv @ seat_pt; sb.tail = inv @ (seat_pt + Vector((0, 0, 0.12)))
sb.parent = eb["spine"] if "spine" in eb else eb["hips"]; sb.use_connect = False
bpy.ops.object.mode_set(mode="OBJECT")

# The tail's weights: only the new chain, blended along it; the first band shares with the old tail bone so the dock bends softly.
groups = {g.name: g for g in mesh.vertex_groups}
for n in names:
    if n not in groups: groups[n] = mesh.vertex_groups.new(name=n)
for g in list(mesh.vertex_groups):
    if g.name not in names: g.remove(tail)
for i in tail:
    s = d[i] * nb
    k = min(nb - 1, int(s)); f = s - k
    a, b = names[k], names[min(nb - 1, k + 1)]
    wa, wb = (1 - f * 0.5), f * 0.5
    if k == 0 and d[i] < 0.12 / nb * nb * 0.12 + 0.08:
        groups[root_name].add([i], 0.4, "REPLACE"); wa *= 0.6; wb *= 0.6
    groups[a].add([i], wa, "REPLACE")
    if b != a: groups[b].add([i], wb, "ADD")
print("BONES", " ".join(names), "length %.2f m" % L)

# A picture to check: the tail in red, from the side.
for poly in me.polygons: pass
mat = bpy.data.materials.new("check"); mat.use_nodes = False
col = me.color_attributes.new("check", "FLOAT_COLOR", "POINT")
ts = set(tail)
for i in range(nv): col.data[i].color = (0.9, 0.1, 0.1, 1) if i in ts else (0.7, 0.7, 0.7, 1)
sc = bpy.context.scene
sc.render.engine = "BLENDER_WORKBENCH"; sc.display.shading.color_type = "VERTEX"; sc.display.shading.light = "STUDIO"
sc.render.resolution_x = 900; sc.render.resolution_y = 600
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam)
side = fwd.cross(Vector((0, 0, 1))).normalized()
c = (hips + headb) / 2
cam.location = c + side * 6 + Vector((0, 0, 0.2)); cam.data.type = "ORTHO"; cam.data.ortho_scale = 3.2
cam.rotation_euler = (c - cam.location).to_track_quat("-Z", "Y").to_euler(); sc.camera = cam
sc.render.filepath = png
bpy.ops.render.render(write_still=True)
me.color_attributes.remove(col)

# Only the horse and its armature go out (LAGP's file also holds an icosphere used to draw bones).
for o in list(bpy.context.scene.objects):
    if o.type == "MESH" and o != mesh: bpy.data.objects.remove(o, do_unlink=True)
# Export as the pipeline's rigs are: one NLA track per clip.
ad = arm.animation_data
acts = [a for a in bpy.data.actions]
for t in list(ad.nla_tracks): ad.nla_tracks.remove(t)
for a in acts:
    nm = a.name.split("|")[-1]
    a.name = nm
    tr = ad.nla_tracks.new(); tr.name = nm
    st = tr.strips.new(nm, int(a.frame_range[0]), a); st.name = nm
ad.action = None
sc.render.fps = 30
bpy.ops.export_scene.fbx(filepath=out, object_types={"ARMATURE", "MESH"}, bake_anim=True, bake_anim_use_all_actions=False,
                         bake_anim_use_nla_strips=True, bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0,
                         add_leaf_bones=False, path_mode="COPY", embed_textures=True, apply_scale_options="FBX_SCALE_ALL",
                         axis_forward="-Z", axis_up="Y")
bpy.ops.export_scene.gltf(filepath=out[:-4] + ".glb", export_format="GLB", export_animations=True, export_animation_mode="NLA_TRACKS")
print("CLIPS", ", ".join(a.name for a in acts))
print("DONE")
'''

a = argparse.ArgumentParser()
a.add_argument("--src", default=os.path.join(FINAL, "horse.glb"))
a.add_argument("--bones", type=int, default=4)
a.add_argument("--dark", type=float, default=0.16, help="texture brightness under which a texel is tail hair")
args = a.parse_args()
keep = os.path.join(FINAL, "as_rigged")
os.makedirs(keep, exist_ok=True)
for f in ("horse.fbx", "horse.glb"):
    if not os.path.exists(os.path.join(keep, f)):
        if not os.path.exists(os.path.join(FINAL, f)): print("FAIL: no " + os.path.join(FINAL, f)); sys.exit(1)
        shutil.copy2(os.path.join(FINAL, f), os.path.join(keep, f))
src = args.src if args.src != os.path.join(FINAL, "horse.glb") else os.path.join(keep, "horse.glb")
tmp = tempfile.mkdtemp(prefix="tailrig_")
script = os.path.join(tmp, "tail.py"); open(script, "w").write(SCRIPT)
out = os.path.join(tmp, "horse.fbx")
png = os.path.join(ROOT, "Build", "clip_sheets", "horse_tail_weights.png")
os.makedirs(os.path.dirname(png), exist_ok=True)
t0 = time.time()
r = subprocess.run([BLENDER, "-b", "--factory-startup", "-P", script, "--", src, out, png, str(args.bones), str(args.dark)], capture_output=True, text=True, errors="replace")
for l in r.stdout.splitlines():
    if l.startswith(("TAIL", "BONES", "CLIPS", "FAIL", "DONE", "CUT", "GAP", "SEAT", "PROFILE")): print(l)
if "DONE" not in r.stdout:
    print(r.stderr[-1500:]); print("FAIL: " + next((l for l in r.stdout.splitlines() if l.startswith("FAIL")), "blender did not finish")); sys.exit(1)
shutil.copy2(out, os.path.join(FINAL, "horse.fbx")); shutil.copy2(out[:-4] + ".glb", os.path.join(FINAL, "horse.glb"))
ok = os.path.exists(png) and os.path.getmtime(png) > t0
print(f"OK: tail rigged, written to {FINAL}, check picture {png}" if ok else "FAIL: no check picture")
sys.exit(0 if ok else 1)
