#!/usr/bin/env python3
"""Concept images for the 3D assets of Battle Blood (FLUX.2 via LAGP).
Usage: python tools/gen_concepts.py [--only name,name] [--n 4]
Skips assets whose concepts already exist. Ends with OK: / FAIL:"""
import argparse, glob, json, os, subprocess, sys
AI = r"C:\AI\ai.cmd"
ROOT = r"C:\AI\outputs\battle_blood\concepts"
HERE = os.path.dirname(os.path.abspath(__file__))
a = argparse.ArgumentParser(); a.add_argument("--only", default=""); a.add_argument("--n", type=int, default=4)
args = a.parse_args()
m = json.load(open(os.path.join(HERE, "assets.json")))
only = set(filter(None, args.only.split(",")))
jobs = []
for name, c in m["characters"].items():
    jobs.append((name, c["seed"], f'Full body photorealistic adult human, {c["prompt"]}, front view, symmetrical T-pose, arms straight out horizontally to the sides at shoulder height, palms down, legs apart, plain white background. {m["light"]}'))
for name, c in m["animals"].items():
    jobs.append((name, c["seed"], f'{c["prompt"]}, strict side view, standing, all four legs visible and apart, plain white background. {m["light"]}'))
for name, c in m["props"].items():
    jobs.append((name, c["seed"], f'{c["prompt"]}, single object, realistic photographic 3D scan, {c.get("view", "three-quarter front view")}, whole object visible, plain white background. {m["light"]}'))
fails = []
for name, seed, prompt in jobs:
    if only and name not in only: continue
    out = os.path.join(ROOT, name)
    if len(glob.glob(os.path.join(out, "*.png"))) >= args.n: continue
    r = subprocess.run(f'"{AI}" image "{prompt}" --n {args.n} --seed {seed} --name {name} --out "{out}"', shell=True, capture_output=True, text=True, errors="replace")
    n = len(glob.glob(os.path.join(out, "*.png")))
    print(name, n, flush=True)
    if r.returncode or n < args.n: fails.append(name); print(r.stderr[-400:])
subprocess.run(f'"{AI}" server stop', shell=True, capture_output=True)
print(f"FAIL: {fails}" if fails else "OK: concepts ready in " + ROOT)
sys.exit(1 if fails else 0)
