#!/usr/bin/env python3
"""Replace the horse's procedural clips with Quaternius' hand-animated horse (CC0, "Ultimate Animated Animals"),
retargeted onto our generated horse with LAGP's tools/anim/retarget_quad.py (our mesh and skin stay as they are).
Usage: python tools/horse_clips.py [--map "Idle=Idle,Walk=Walk,Trot=Gallop,Run=Gallop,Death=Death,Hit=Idle_HitReact_Left,Attack=Attack_Kick,LookAround=Idle_2"]
       [--source C:/AI/tools/anim_library/quaternius_horses/qvTrSG9pZF.glb] [--no_install]
Writes C:/AI/outputs/battle_blood/models/horse/final/horse_q.fbx + .glb, then (unless --no_install) copies it over final/horse.fbx/.glb and to
Assets/Game/Resources/Art/Models/horse.fbx (the old one is kept as horse_procedural.fbx next to the final outputs).
Source download: https://poly.pizza/m/qvTrSG9pZF (Quaternius, CC0 1.0).
Ends with OK: / FAIL:"""
import argparse, os, re, shutil, subprocess, sys, time
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BLENDER = r"C:\AI\blender\blender.exe"
RETARGET = r"C:\AI\tools\anim\retarget_quad.py"
FINAL = r"C:\AI\outputs\battle_blood\models\horse\final"
a = argparse.ArgumentParser()
a.add_argument("--source", default=r"C:\AI\tools\anim_library\quaternius_horses\qvTrSG9pZF.glb")
a.add_argument("--map", default="Idle=Idle,Walk=Walk,Trot=Gallop,Run=Gallop,Death=Death,Hit=Idle_HitReact_Left,Attack=Attack_Kick,LookAround=Idle_2")
a.add_argument("--no_install", action="store_true")
args = a.parse_args()
target = os.path.join(FINAL, "horse.fbx")
backup = os.path.join(FINAL, "horse_procedural.fbx")
if not os.path.exists(args.source): print("FAIL: no source " + args.source); sys.exit(1)
if not os.path.exists(backup):
    if not os.path.exists(target): print("FAIL: no target " + target); sys.exit(1)
    shutil.copy2(target, backup)
out = os.path.join(FINAL, "horse_q.fbx")
t0 = time.time()
r = subprocess.run([BLENDER, "-b", "--factory-startup", "-P", RETARGET, "--", args.source, backup, out, args.map, "--loop", "Idle,Walk,Trot,Run,LookAround"],
                   capture_output=True, text=True, errors="replace")
lines = [l for l in r.stdout.splitlines() if re.match(r"\s*(clip|CLIP|OK|FAIL|\w+:)", l)]
print("\n".join(lines[-30:]))
if not os.path.exists(out) or os.path.getmtime(out) < t0:
    print(r.stderr[-1500:]); print("FAIL: retarget wrote no " + out); sys.exit(1)
if not args.no_install:
    # The result becomes the horse's final output, so tools/import_assets.py copies this one (the procedural rig stays in horse_procedural.fbx).
    shutil.copy2(out, target); shutil.copy2(out[:-4] + ".glb", target[:-4] + ".glb")
    dst = os.path.join(ROOT, "Assets", "Game", "Resources", "Art", "Models", "horse.fbx")
    shutil.copy2(out, dst)
    print("installed", dst)
print(f"OK: horse clips retargeted ({args.map}) in {time.time() - t0:.0f}s")
