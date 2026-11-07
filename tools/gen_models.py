#!/usr/bin/env python3
"""Concept -> shape -> clean -> texture -> rig (characters, horse) or scaled FBX (props), via LAGP.
Usage: python tools/gen_models.py [--only name,name] [--budget 500]
A model whose clean step shatters it is kept, with a line added to tools/ASSET_ISSUES.md for review after the build.
Resumable: a step whose output exists is skipped, and no new step starts after --budget seconds (rerun to continue; prints MORE).
Ends with OK: / MORE: / FAIL:  Results: C:/AI/outputs/battle_blood/models/<name>/final/"""
import argparse, glob, json, os, re, subprocess, sys, time
AI = r"C:\AI\ai.cmd"
BASE = r"C:\AI\outputs\battle_blood"
HERE = os.path.dirname(os.path.abspath(__file__))
# One clip list for all three soldiers, so they share one Animator controller.
HUMAN = "Run,Jog,Sprint,SwordIdle,SwordAttack,PunchJab,PunchCross,HitChest,HitHead,Death,Roll,JumpStart,JumpLoop,JumpLand,Driving,Roar,SpearThrust,Dizzy,PistolIdle,PistolAimNeutral"
HORSE = "Trot,Run,Jump,Attack,Hit,Death,LookAround,Eat"
a = argparse.ArgumentParser(); a.add_argument("--only", default=""); a.add_argument("--budget", type=int, default=500, help="seconds; no new step starts after this, rerun to continue"); args = a.parse_args()
T0 = time.time()
MAX_PARTS = 8          # a soldier or prop is one or a few pieces; hundreds means the remesh shattered it (per asset: "max_parts")
ISSUES = os.path.join(HERE, "ASSET_ISSUES.md")

def note(name, text):
    """Adds a line to the list of things to revisit, once."""
    line = f"- {name}: {text}" + chr(10)
    old = open(ISSUES, encoding="utf-8").read() if os.path.exists(ISSUES) else ""
    if line not in old:
        with open(ISSUES, "a", encoding="utf-8") as f: f.write(line)
    print("  NOTE " + line.strip(), flush=True)

def clean_parts(folder):
    """Pieces in the cleaned mesh, from the check line the clean step wrote to its job.json."""
    info = json.load(open(os.path.join(folder, "job.json")))[-1].get("info", [])
    for line in info:
        m = re.search(r'"parts": (\d+)', line)
        if m: return int(m.group(1))
    return 10**6       # no check line: treat as failed rather than pass it unseen
class Budget(Exception): pass
m = json.load(open(os.path.join(HERE, "assets.json")))
only = set(filter(None, args.only.split(",")))

def run(step, cmd, out_glob):
    hits = glob.glob(out_glob)
    if hits: return hits[0]
    if time.time() - T0 > args.budget: raise Budget()
    t = time.time()
    r = subprocess.run(f'"{AI}" {cmd}', shell=True, capture_output=True, text=True, errors="replace")
    hits = glob.glob(out_glob)
    print(f"  {step}: {time.time()-t:.0f}s rc={r.returncode} {os.path.basename(hits[0]) if hits else 'NO OUTPUT'}", flush=True)
    if not hits:
        print((r.stdout + r.stderr)[-1500:]); raise RuntimeError(step)
    return hits[0]

fails = []
for kind in ("characters", "animals", "props"):
    for name, c in m[kind].items():
        if only and name not in only: continue
        print(name, flush=True)
        try:
            d = os.path.join(BASE, "models", name)
            concept = glob.glob(os.path.join(BASE, "concepts", name, f"*_{c['pick']:02d}.png"))[0]
            big = kind != "props"
            res = c.get("res", 1536 if big else 1024)
            faces = c.get("faces", 200000 if big else 80000)
            tex = c.get("tex", 2048)
            tris = c.get("tris", 9000)
            sd = os.path.join(d, "shape")
            shape = run("shape", f'shape "{concept}" --res {res} --faces {faces} --name {name} --out "{sd}" --seed {c.get("shape_seed", c["seed"])}', os.path.join(sd, "*_shape.glb"))
            sj = glob.glob(os.path.join(sd, "*_shape.json"))[0]
            cd = os.path.join(d, "clean")
            clean = run("clean", f'clean "{shape}" --error 0.0005 --tris {tris} --name {name} --out "{cd}"', os.path.join(cd, "*_clean.glb"))
            # A shape can shatter in the remesh (seen: 447 pieces, while LAGP's check still says ok). Nothing is regenerated: it is noted for review after the build.
            parts = clean_parts(cd)
            print(f"  clean: {parts} pieces (limit {c.get('max_parts', MAX_PARTS)})", flush=True)
            if parts > c.get("max_parts", MAX_PARTS): note(name, f"clean step left {parts} pieces, limit {c.get('max_parts', MAX_PARTS)}: look at it")
            td = os.path.join(d, "texture")
            tx = run("texture", f'texture "{clean}" --shape_json "{sj}" --tex {tex} --name {name} --out "{td}" --seed {c["seed"]}', os.path.join(td, "*_01.glb"))
            fd = os.path.join(d, "final")
            if kind == "characters":
                run("rig", f'rig "{tx}" --type humanoid --image "{concept}" --height {c.get("height", 1.78)} --anims {HUMAN} --name {name} --out "{fd}"', os.path.join(fd, "*.fbx"))
            elif kind == "animals":
                run("rig", f'rig "{tx}" --height {c["height"]} --anims {HORSE} --name {name} --out "{fd}"', os.path.join(fd, "*.fbx"))
            else:
                lvl = c.get("level", "")
                run("final", f'lowpoly "{tx}" --height {c["height"]} --tris {tris} {lvl} --drop_floating --fbx --name {name} --out "{fd}"', os.path.join(fd, "*.fbx"))
                if c.get("lod"):   # a cheap version for distance
                    ld = os.path.join(d, "lod")
                    run("lod", f'lowpoly "{tx}" --height {c["height"]} --tris {c["lod"]} {lvl} --drop_floating --fbx --name {name}_lod --out "{ld}"', os.path.join(ld, "*.fbx"))
        except Budget:
            subprocess.run(f'"{AI}" server stop', shell=True, capture_output=True)
            print("MORE: time budget used, rerun to continue"); sys.exit(0)
        except Exception as e:
            print("  ERROR", e); fails.append(name)
subprocess.run(f'"{AI}" server stop', shell=True, capture_output=True)
print(f"FAIL: {fails}" if fails else "OK: models ready in " + os.path.join(BASE, "models"))
sys.exit(1 if fails else 0)
