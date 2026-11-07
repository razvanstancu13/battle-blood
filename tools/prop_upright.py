#!/usr/bin/env python3
"""Stand leaning props upright. A model generated from a three-quarter concept can come out turned by the concept camera's pitch
(the oak's trunk and the tent leaned in the game). The correction for each prop is set by eye in tools/assets.json as
"upright": [x_deg, y_deg] (Blender world axes, turned about the model's foot, then put back on the ground), and checked on the sheet.
Usage:
  python tools/prop_upright.py [--models oak,pine,tent,dead_tree] [--sheet out.png]     front and side renders, as they are now
  python tools/prop_upright.py --fix [--models oak,pine,tent,dead_tree] [--sheet out.png]
      writes the turned model over models/<name>/final/*.fbx (and lod/*.fbx for <name>_lod), the generated one kept in
      as_generated/ next to it (the fix always starts from that), copies it into Assets/Game/Resources/Art/Models/, renders the result.
Ends with OK: / FAIL:"""
import argparse, glob, json, os, re, shutil, subprocess, sys, tempfile
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MODELS = os.path.join(ROOT, "Assets", "Game", "Resources", "Art", "Models")
BASE = r"C:\AI\outputs\battle_blood\models"
BLENDER = r"C:\AI\blender\blender.exe"

BLENDER_SCRIPT = r'''
import bpy, sys, math
from mathutils import Vector, Matrix, Euler
args = sys.argv[sys.argv.index("--") + 1:]
src, png, rx, ry, out = args[0], args[1], float(args[2]), float(args[3]), args[4]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
pts = [o.matrix_world @ v.co for o in meshes for v in o.data.vertices]
z0 = min(p.z for p in pts); h = max(p.z for p in pts) - z0
foot = [p for p in pts if p.z < z0 + 0.06 * h]
f = sum(foot, Vector()) / len(foot); f.z = z0
if rx or ry:
    turn = Matrix.Translation(f) @ Euler((math.radians(rx), math.radians(ry), 0)).to_matrix().to_4x4() @ Matrix.Translation(-f)
    for o in meshes:
        m = o.matrix_world; mi = m.inverted()
        o.data.transform(mi @ turn @ m)
        o.data.update()
    pts = [o.matrix_world @ v.co for o in meshes for v in o.data.vertices]
    drop = min(p.z for p in pts) - z0
    for o in meshes:
        mi = o.matrix_world.inverted()
        o.data.transform(mi @ Matrix.Translation((0, 0, -drop)) @ o.matrix_world)
    pts = [o.matrix_world @ v.co for o in meshes for v in o.data.vertices]
# The trunk's lean, measured: a line fitted through the middle of the stem in thin slices from 6 to 32 % of the height
# (the cluster near each slice's median, so low branches do not pull it). Read by eye a gnarled trunk is easy to misjudge.
z0 = min(p.z for p in pts); H = max(p.z for p in pts) - z0
rows = []
for i in range(14):
    q = 0.06 + 0.02 * i
    sl = [p for p in pts if abs((p.z - z0) / H - q) < 0.02]
    if len(sl) < 4: continue
    mx_ = sorted(p.x for p in sl)[len(sl) // 2]; my_ = sorted(p.y for p in sl)[len(sl) // 2]
    sl = [p for p in sl if (p.x - mx_) ** 2 + (p.y - my_) ** 2 < (0.12 * H) ** 2]
    rows.append((q * H, sum(p.x for p in sl) / len(sl), sum(p.y for p in sl) / len(sl)))
if len(rows) >= 4:
    mh = sum(r[0] for r in rows) / len(rows)
    def slope(k):
        m = sum(r[k] for r in rows) / len(rows)
        return sum((r[0] - mh) * (r[k] - m) for r in rows) / sum((r[0] - mh) ** 2 for r in rows)
    # leaning towards +y is undone by turning about x (+), leaning towards -x by turning about y (+)
    print(f"LEAN {math.degrees(math.atan(slope(2))):+.1f} {-math.degrees(math.atan(slope(1))):+.1f}")
if out != "-":
    bpy.ops.export_scene.fbx(filepath=out, use_selection=False, object_types={"MESH", "EMPTY"}, mesh_smooth_type="FACE", add_leaf_bones=False, bake_anim=False)
scene = bpy.context.scene
scene.render.engine = "BLENDER_WORKBENCH"
scene.display.shading.light = "STUDIO"; scene.display.shading.color_type = "TEXTURE"
scene.render.resolution_x = 360; scene.render.resolution_y = 480
mn = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts))); mx = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
c = (mn + mx) / 2; size = max(mx - mn) * 1.15
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); scene.collection.objects.link(cam)
cam.data.type = "ORTHO"; cam.data.ortho_scale = size; scene.camera = cam
for i, (loc, rot) in enumerate([((c.x, c.y - size * 3, c.z), (math.pi / 2, 0, 0)), ((c.x + size * 3, c.y, c.z), (math.pi / 2, 0, math.pi / 2))]):
    cam.location = loc; cam.rotation_euler = rot
    scene.render.filepath = png.replace(".png", f"_{i}.png")
    bpy.ops.render.render(write_still=True)
print("DONE")
'''

a = argparse.ArgumentParser()
a.add_argument("--models", default="oak,oak_lod,pine,pine_lod,tent,dead_tree")
a.add_argument("--sheet", default=os.path.join(tempfile.gettempdir(), "prop_upright.png"))
a.add_argument("--fix", action="store_true")
args = a.parse_args()
manifest = json.load(open(os.path.join(ROOT, "tools", "assets.json"), encoding="utf-8"))["props"]
tmp = tempfile.mkdtemp(prefix="upright_")
script = os.path.join(tmp, "upright.py"); open(script, "w").write(BLENDER_SCRIPT)
problems, images = [], []
for name in [m for m in args.models.split(",") if m]:
    base = name[:-4] if name.endswith("_lod") else name
    folder = os.path.join(BASE, base, "lod" if name.endswith("_lod") else "final")
    fbxs = sorted(glob.glob(os.path.join(folder, "*.fbx")))
    if not fbxs: problems.append(f"{name}: no fbx in {folder}"); continue
    target = fbxs[0]
    keep = os.path.join(folder, "as_generated", os.path.basename(target))
    rx, ry = manifest.get(base, {}).get("upright", [0, 0])
    png = os.path.join(tmp, name + ".png")
    if args.fix:
        if not os.path.exists(keep): os.makedirs(os.path.dirname(keep), exist_ok=True); shutil.copy2(target, keep)
        src, out = keep, target
    else:
        src, out, rx, ry = target, "-", 0, 0
    r = subprocess.run([BLENDER, "-b", "-P", script, "--", src, png, str(rx), str(ry), out], capture_output=True, text=True, errors="replace")
    if "DONE" not in r.stdout: problems.append(f"{name}: blender failed {r.stderr[-300:]}"); continue
    if args.fix:
        shutil.copy2(target, os.path.join(MODELS, name + ".fbx"))
    lean = re.search(r"LEAN (\S+) (\S+)", r.stdout)
    lean = f", trunk still leans: turn x {lean.group(1)} y {lean.group(2)} more to stand it straight" if lean else ""
    print(f"{name:<9} turned x {rx:+.1f} y {ry:+.1f} deg{' -> ' + target if args.fix else ' (as it is)'}{lean}")
    images.append((name, rx, ry, png.replace(".png", "_0.png"), png.replace(".png", "_1.png")))
from PIL import Image, ImageDraw
tiles = []
for name, rx, ry, f0, f1 in images:
    if not (os.path.exists(f0) and os.path.exists(f1)): problems.append(f"{name}: no render"); continue
    t = Image.new("RGB", (720, 500), "white")
    for k, f in enumerate((f0, f1)):
        im = Image.open(f).convert("RGBA"); bg = Image.new("RGBA", im.size, (235, 235, 235, 255)); bg.alpha_composite(im)
        t.paste(bg.convert("RGB"), (k * 360, 20))
    d = ImageDraw.Draw(t); d.text((6, 4), f"{name}  front | side   turned x {rx:+.1f} y {ry:+.1f}", fill="black")
    for x in (180, 540): d.line([(x, 20), (x, 500)], fill=(255, 0, 0))      # a plumb line to judge by
    tiles.append(t)
if tiles:
    rows = (len(tiles) + 1) // 2
    sheet = Image.new("RGB", (1440, 500 * rows), "white")
    for i, t in enumerate(tiles): sheet.paste(t, ((i % 2) * 720, (i // 2) * 500))
    sheet.save(args.sheet); print("sheet:", args.sheet)
print(f"OK: {len(images)} props {'stood up and copied into the game' if args.fix else 'rendered'}" if not problems else f"FAIL: {problems}")
sys.exit(0 if not problems else 1)
