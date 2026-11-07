#!/usr/bin/env python3
"""Render a rigged LAGP model's clips and lay frames out in one sheet per clip, to look for skinning artifacts.
Usage: python tools/clip_sheet.py infantry --clips Walk,Run [--frames 6] [--quad] [--out Build/clip_sheets]
Reads C:/AI/outputs/battle_blood/models/<name>/final/<name>.glb. Writes <out>/<name>_<clip>_<view>.jpg.
Checks every clip rendered and each sheet is newer than the start. Ends with OK: / FAIL:"""
import argparse, glob, os, shutil, subprocess, sys, tempfile, time
from PIL import Image
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BLENDER = r"C:\AI\blender\blender.exe"; RENDER = r"C:\AI\assetctl\checks\cliprender.py"
a = argparse.ArgumentParser(); a.add_argument("name"); a.add_argument("--clips", default="Walk,Run"); a.add_argument("--frames", type=int, default=6)
a.add_argument("--quad", action="store_true", help="four-legged: side and three-quarter views"); a.add_argument("--out", default=os.path.join(ROOT, "Build", "clip_sheets")); a.add_argument("--glb", help="another file than models/<name>/final/<name>.glb")
args = a.parse_args()
glb = args.glb or os.path.join(r"C:\AI\outputs\battle_blood\models", args.name, "final", args.name + ".glb")
if not os.path.exists(glb): print("FAIL: no model " + glb); sys.exit(1)
os.makedirs(args.out, exist_ok=True)
t0 = time.time()
tmp = tempfile.mkdtemp()
cmd = [BLENDER, "-b", "--factory-startup", "-P", RENDER, "--", glb, tmp, args.clips] + (["quad"] if args.quad else [])
r = subprocess.run(cmd, capture_output=True, text=True, errors="replace")
for line in r.stdout.splitlines():
    if line.startswith("CLIP"): print(line[:120])
missing, written = [], []
for clip in args.clips.split(","):
    for view in ("front", "high"):
        files = sorted(glob.glob(os.path.join(tmp, f"{clip}_{view}_*.png")))
        if not files: missing.append(f"{clip}/{view}"); continue
        picked = [files[int(i * len(files) / args.frames)] for i in range(args.frames)]
        w, h = Image.open(picked[0]).size
        sheet = Image.new("RGB", (w * len(picked), h))
        for i, p in enumerate(picked): sheet.paste(Image.open(p).convert("RGB"), (i * w, 0))
        path = os.path.join(args.out, f"{args.name}_{clip}_{view}.jpg")
        sheet.save(path, quality=88); written.append(path); print(path)
shutil.rmtree(tmp, ignore_errors=True)
ok = not missing and all(os.path.getmtime(p) > t0 for p in written)
if not ok: print(r.stderr[-400:])
print(f"OK: {len(written)} sheets for {args.name}" if ok else f"FAIL: nothing rendered for {missing}")
sys.exit(0 if ok else 1)
