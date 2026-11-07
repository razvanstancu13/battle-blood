#!/usr/bin/env python3
"""Copy the chosen LAGP outputs into Assets/Game/Resources/Art, derive normal maps from the ground tiles, level the audio.
Usage: python tools/import_assets.py [--skip-audio]   (run with C:/AI/ComfyUI/python_embeded/python.exe -s)
Which variant of each picture is in tools/asset_picks.json; the ground's extra tiles (gen_textures.py --variants) all go in as <name>_v1..; every 3D model in assets.json is taken. Checks every copy exists and is not empty.
Ends with OK: / FAIL:"""
import argparse, glob, json, os, shutil, subprocess, sys
import numpy as np
from PIL import Image

BASE = r"C:\AI\outputs\battle_blood"
HERE = os.path.dirname(os.path.abspath(__file__))
a = argparse.ArgumentParser(); a.add_argument("--project", default=os.path.dirname(HERE)); a.add_argument("--skip-audio", action="store_true"); args = a.parse_args()
ART = os.path.join(args.project, "Assets", "Game", "Resources", "Art")
m = json.load(open(os.path.join(HERE, "assets.json")))
picks = json.load(open(os.path.join(HERE, "asset_picks.json")))
errors, copied = [], 0

def copy(src, dst):
    global copied
    if not os.path.exists(src): errors.append("missing " + src); return
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    if not (os.path.exists(dst) and os.path.getsize(dst) == os.path.getsize(src) and os.path.getmtime(dst) >= os.path.getmtime(src)):
        shutil.copyfile(src, dst)
    copied += 1
    if os.path.getsize(dst) == 0: errors.append("empty " + dst)

def normal_map(src, dst, strength):
    if os.path.exists(dst) and os.path.getmtime(dst) >= os.path.getmtime(src): return
    im = np.asarray(Image.open(src).convert("L"), dtype=np.float32) / 255
    dx = (np.roll(im, -1, 1) - np.roll(im, 1, 1)) * strength
    dy = (np.roll(im, -1, 0) - np.roll(im, 1, 0)) * strength
    n = np.dstack([-dx, dy, np.ones_like(im)])
    n /= np.linalg.norm(n, axis=2, keepdims=True)
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    Image.fromarray(((n * 0.5 + 0.5) * 255).astype(np.uint8)).save(dst)

for kind in ("characters", "animals", "props"):
    for name, c in m[kind].items():
        final = os.path.join(BASE, "models", name, "final")
        fbx = sorted(glob.glob(os.path.join(final, "*.fbx")))
        if fbx: copy(fbx[0], os.path.join(ART, "Models", name + ".fbx"))
        else: errors.append("no fbx for " + name)
        lod = sorted(glob.glob(os.path.join(BASE, "models", name, "lod", "*.fbx")))
        if lod: copy(lod[0], os.path.join(ART, "Models", name + "_lod.fbx"))
        tex = [t for t in sorted(glob.glob(os.path.join(final, "*.png"))) if not any(s in t for s in ("_pose", "_walk", "preview"))]
        if not tex: tex = sorted(glob.glob(os.path.join(BASE, "models", name, "texture", "*.png")))
        if tex:
            copy(tex[0], os.path.join(ART, "Models", name + ".png"))
            if lod: copy(tex[0], os.path.join(ART, "Models", name + "_lod.png"))
        else: errors.append("no texture for " + name)

def crop_to_alpha(path):
    """Cards must start at the image's bottom edge, or they float over the ground."""
    im = Image.open(path).convert("RGBA")
    box = im.getchannel("A").point(lambda v: 255 if v > 40 else 0).getbbox()
    if box and box != (0, 0) + im.size: im.crop(box).save(path)

for name, pick in picks["textures"].items():
    src = os.path.join(BASE, "textures", name, pick)
    if name in picks.get("height_to_normal", {}):      # a height picture: only its normal map goes in
        if os.path.exists(src): normal_map(src, os.path.join(ART, "Textures", name + ".png"), picks["height_to_normal"][name]); copied += 1
        else: errors.append("missing " + src)
        continue
    dst = os.path.join(ART, "Textures", name + ".png")
    cropped = name in picks.get("crop", [])
    if cropped and os.path.exists(dst) and os.path.getmtime(dst) >= os.path.getmtime(src): copied += 1; continue   # already copied and cropped
    copy(src, dst)
    if cropped and os.path.exists(dst): crop_to_alpha(dst)
    if os.path.exists(dst) and name in picks["normals"]:
        normal_map(dst, os.path.join(ART, "Textures", name + "_n.png"), picks["normals"][name])

def flatten_tile(path, radius=0.09):
    """A generated ground tile carries soft light and dark across it; repeated every few metres that shows as bands. Divide its
    colour by a heavily blurred copy of itself (wrapping at the edges, as the tile does) and restore the average: the fine detail
    stays, the broad shading goes."""
    from PIL import ImageFilter
    im = Image.open(path).convert("RGB"); w, h = im.size
    big = Image.new("RGB", (w * 3, h * 3))
    for x in range(3):
        for y in range(3): big.paste(im, (x * w, y * h))
    blur = np.asarray(big.filter(ImageFilter.GaussianBlur(w * radius)).crop((w, h, 2 * w, 2 * h))).astype(np.float32)
    a = np.asarray(im).astype(np.float32)
    lum = lambda c: c[..., 0] * 0.3 + c[..., 1] * 0.59 + c[..., 2] * 0.11
    k = (lum(a).mean() / np.maximum(lum(blur), 4.0))[..., None]
    Image.fromarray(np.clip(a * k, 0, 255).astype(np.uint8)).save(path)

GROUND = ("grass", "grass_dry", "dirt", "mud", "rock", "moss")
for name in GROUND:
    p_ = os.path.join(ART, "Textures", name + ".png")
    if os.path.exists(p_): flatten_tile(p_)
        
# Ground variants: every tile in textures/<name>_variants becomes <name>_v1.._vN (with its normal map); the terrain blends between them.
for name in ("grass", "grass_dry", "dirt", "mud", "rock", "moss"):
    for k, src in enumerate(sorted(glob.glob(os.path.join(BASE, "textures", name + "_variants", f"{name}_*_tile.png"))), 1):
        dst = os.path.join(ART, "Textures", f"{name}_v{k}.png")
        copy(src, dst)
        if os.path.exists(dst): flatten_tile(dst)
        if os.path.exists(dst) and name in picks["normals"]:
            normal_map(dst, os.path.join(ART, "Textures", f"{name}_v{k}_n.png"), picks["normals"][name])

for name, pick in picks["ui"].items():
    copy(os.path.join(BASE, "textures", name, pick), os.path.join(ART, "UI", name + ".png"))

if not args.skip_audio:
    if subprocess.run([sys.executable, "-s", os.path.join(HERE, "level_audio.py"), "--out", os.path.join(ART, "Audio")]).returncode:
        errors.append("level_audio failed")

if picks.get("video"):
    copy(os.path.join(BASE, "video", picks["video"]), os.path.join(ART, "Video", "menu.mp4"))

print(f"FAIL: {errors}" if errors else f"OK: {copied} files in {ART}")
sys.exit(1 if errors else 0)
