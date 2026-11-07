#!/usr/bin/env python3
"""Add Mixamo animation clips to the soldiers' rigs. Our humanoid rigs (LAGP) use Mixamo's bone names, so each bone is matched by
name and retargeted by bone direction, as LAGP does for its own clip library: the bone is first turned to the Mixamo bone's rest
direction, then given the Mixamo bone's rotation from its rest, so any rest pose and proportions work; the hips' movement scales with
hip height. The clips, files and loops are listed in tools/mixamo_clips.json.
Usage: python tools/mixamo_clips.py [--models infantry,archer,cavalry] [--only BowDraw,SSSlash1]
Reads models/<name>/final/as_rigged/<name>.glb (the rig as LAGP made it, kept there on the first run), writes final/<name>.fbx + .glb
with LAGP's clips and the Mixamo ones. Ends with OK: / FAIL:"""
import argparse, json, os, shutil, subprocess, sys, tempfile, time
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BLENDER = r"C:\AI\blender\blender.exe"
BASE = r"C:\AI\outputs\battle_blood\models"
LIB = r"C:\AI\tools\anim_library"

SCRIPT = r'''
import bpy, sys, json, math
from mathutils import Vector, Matrix, Quaternion
args = sys.argv[sys.argv.index("--") + 1:]
src_glb, out, clips_json = args[0], args[1], args[2]
spec = json.load(open(clips_json))
clips, loops = spec["clips"], set(spec["loop"])
bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene
sc.render.fps = 30
bpy.ops.import_scene.gltf(filepath=src_glb)
arm = next(o for o in sc.objects if o.type == "ARMATURE")
keep_acts = list(bpy.data.actions)
for a in keep_acts: a.use_fake_user = True
tb, pb = arm.data.bones, arm.pose.bones
Tw = arm.matrix_world.to_3x3().normalized()
NEXT = {"Hips": "Spine", "Spine": "Spine1", "Spine1": "Spine2", "Spine2": "Neck", "Neck": "Head"}
for S in ("Left", "Right"):
    NEXT.update({S + "Shoulder": S + "Arm", S + "Arm": S + "ForeArm", S + "ForeArm": S + "Hand", S + "Hand": S + "HandMiddle1",
                 S + "UpLeg": S + "Leg", S + "Leg": S + "Foot", S + "Foot": S + "ToeBase", S + "HandMiddle1": S + "HandMiddle2",
                 S + "HandThumb1": S + "HandThumb2"})
order = sorted(tb, key=lambda b: len(b.parent_recursive))
added = []
for name, rel in clips.items():
    path = __import__("os").path.join(spec["_lib"], rel)
    before_o, before_a = set(sc.objects), set(bpy.data.actions)
    bpy.ops.import_scene.fbx(filepath=path, automatic_bone_orientation=False)
    new = [o for o in sc.objects if o not in before_o]
    src = next((o for o in new if o.type == "ARMATURE"), None)
    sact = next((a for a in bpy.data.actions if a not in before_a), None)
    if src is None or sact is None:
        print("SKIP %s: no armature or action in %s" % (name, rel)); continue
    sb = src.data.bones
    pre = "mixamorig:" if ("mixamorig:Hips" in sb) else ("mixamorig1:" if "mixamorig1:Hips" in sb else "")
    Sw = src.matrix_world.to_3x3().normalized()
    def rdir(bones, n, prefix, W):
        b = bones[prefix + n]; nx = NEXT.get(n)
        tip = bones[prefix + nx].head_local if nx and (prefix + nx) in bones else b.tail_local
        return (W @ (tip - b.head_local)).normalized()
    mapped = [b.name for b in tb if (pre + b.name) in sb]
    A, Rt, Rs = {}, {}, {}
    for n in mapped:
        A[n] = rdir(tb, n, "", Tw).rotation_difference(rdir(sb, n, pre, Sw)).to_matrix()   # turns our rest direction onto Mixamo's
        Rt[n] = Tw @ tb[n].matrix_local.to_3x3()
        Rs[n] = Sw @ sb[pre + n].matrix_local.to_3x3()
    sw_full = src.matrix_world
    h_src = (sw_full @ sb[pre + "Hips"].head_local).z - min((sw_full @ b.head_local).z for b in sb)
    h_tgt = (arm.matrix_world @ tb["Hips"].head_local).z - min((arm.matrix_world @ b.head_local).z for b in tb)
    scale = h_tgt / max(1e-4, h_src)
    hip0 = sw_full @ sb[pre + "Hips"].head_local
    hips_inv = tb["Hips"].matrix_local.to_3x3().inverted()
    def assign(obj, a_):
        ad_ = obj.animation_data or obj.animation_data_create()
        for t in ad_.nla_tracks: t.mute = True
        ad_.action = a_
        if hasattr(ad_, "action_slot") and getattr(a_, "slots", None) and len(a_.slots) and ad_.action_slot is None:
            ad_.action_slot = a_.slots[0]
    assign(src, sact)
    act = bpy.data.actions.new(name); act.use_fake_user = True
    assign(arm, act)
    f0, f1 = (int(round(x)) for x in sact.frame_range)
    for f in range(f0, f1 + 1):
        sc.frame_set(f)
        W = {}
        for b in order:
            n = b.name
            if n in A:
                D = (Sw @ src.pose.bones[pre + n].matrix.to_3x3()) @ Rs[n].inverted()
                W[n] = D @ A[n] @ Rt[n]
            elif b.parent:
                W[n] = W[b.parent.name] @ (Tw @ b.parent.matrix_local.to_3x3()).inverted() @ (Tw @ b.matrix_local.to_3x3())
            else:
                W[n] = Tw @ b.matrix_local.to_3x3()
            Wa = Tw.inverted() @ W[n]
            if b.parent:
                Wp = Tw.inverted() @ W[b.parent.name]
                relm = b.parent.matrix_local.to_3x3().inverted() @ b.matrix_local.to_3x3()
                q = (relm.inverted() @ Wp.inverted() @ Wa).to_quaternion()
            else:
                q = (b.matrix_local.to_3x3().inverted() @ Wa).to_quaternion()
            pb[n].rotation_mode = "QUATERNION"; pb[n].rotation_quaternion = q
        if f == f0 + (f1 - f0) // 2:
            print("MOVED %s: hips turned %.1f deg from rest at mid clip" % (name, math.degrees(src.pose.bones[pre + "Hips"].matrix.to_quaternion().rotation_difference(sb[pre + "Hips"].matrix_local.to_quaternion()).angle)))
        delta = (sw_full @ src.pose.bones[pre + "Hips"].head - hip0) * scale
        pb["Hips"].location = hips_inv @ (Tw.inverted() @ delta)
        for p in pb:
            p.keyframe_insert("rotation_quaternion", frame=f - f0)
        pb["Hips"].keyframe_insert("location", frame=f - f0)
    act["loop"] = name in loops
    added.append(act)
    print("CLIP %s <- %s: %d frames, %d bones matched, hips scale %.2f" % (name, rel, f1 - f0 + 1, len(mapped), scale))
    for o in new: bpy.data.objects.remove(o, do_unlink=True)
    for a_ in [a_ for a_ in bpy.data.actions if a_ not in before_a and a_ is not act]: bpy.data.actions.remove(a_)
for p in pb: p.rotation_quaternion = Quaternion(); p.location = Vector()
# Only our mesh and armature go out.
for o in list(sc.objects):
    if o.type == "MESH" and o.parent != arm and not any(m.type == "ARMATURE" for m in o.modifiers): bpy.data.objects.remove(o, do_unlink=True)
ad = arm.animation_data
for t in list(ad.nla_tracks): ad.nla_tracks.remove(t)
acts = [a for a in bpy.data.actions if a.use_fake_user]
names_taken = set()
for a in acts:
    base = a.name.split("|")[-1]
    if len(base) > 4 and base[-4] == "." and base[-3:].isdigit() and base[:-4] not in [x.name for x in acts if x is not a]: base = base[:-4]
    a.name = base
    tr = ad.nla_tracks.new(); tr.name = a.name
    st = tr.strips.new(a.name, int(a.frame_range[0]), a); st.name = a.name
ad.action = None
bpy.ops.export_scene.fbx(filepath=out, object_types={"ARMATURE", "MESH"}, bake_anim=True, bake_anim_use_all_actions=False,
                         bake_anim_use_nla_strips=True, bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0,
                         add_leaf_bones=False, path_mode="COPY", embed_textures=True, apply_scale_options="FBX_SCALE_ALL",
                         axis_forward="-Z", axis_up="Y")
bpy.ops.export_scene.gltf(filepath=out[:-4] + ".glb", export_format="GLB", export_animations=True, export_animation_mode="NLA_TRACKS")
print("CLIPS", ", ".join(a.name for a in acts))
print("DONE", len(added))
'''

a = argparse.ArgumentParser()
a.add_argument("--models", default="")
a.add_argument("--only", default="")
args = a.parse_args()
spec = json.load(open(os.path.join(ROOT, "tools", "mixamo_clips.json"), encoding="utf-8"))
spec["_lib"] = LIB
if args.only:
    spec["clips"] = {k: v for k, v in spec["clips"].items() if k in args.only.split(",")}
missing = [v for v in spec["clips"].values() if not os.path.exists(os.path.join(LIB, v))]
if missing: print("FAIL: missing Mixamo files " + ", ".join(missing)); sys.exit(1)
models = [m for m in (args.models.split(",") if args.models else spec["models"]) if m]
tmp = tempfile.mkdtemp(prefix="mixamo_")
script = os.path.join(tmp, "mx.py"); open(script, "w").write(SCRIPT)
cj = os.path.join(tmp, "clips.json"); json.dump(spec, open(cj, "w"))
problems = []
for m in models:
    final = os.path.join(BASE, m, "final")
    keep = os.path.join(final, "as_rigged")
    os.makedirs(keep, exist_ok=True)
    for f in (m + ".fbx", m + ".glb"):
        if not os.path.exists(os.path.join(keep, f)):
            if not os.path.exists(os.path.join(final, f)): problems.append(f"{m}: no {f}"); break
            shutil.copy2(os.path.join(final, f), os.path.join(keep, f))
    out = os.path.join(tmp, m + ".fbx")
    t0 = time.time()
    r = subprocess.run([BLENDER, "-b", "--factory-startup", "-P", script, "--", os.path.join(keep, m + ".glb"), out, cj],
                       capture_output=True, text=True, errors="replace")
    for l in r.stdout.splitlines():
        if l.startswith(("CLIP ", "SKIP", "DONE", "CLIPS", "MOVED")): print(m, l)
    if "DONE" not in r.stdout or not os.path.exists(out):
        problems.append(f"{m}: blender failed {r.stderr[-600:]}"); continue
    shutil.copy2(out, os.path.join(final, m + ".fbx")); shutil.copy2(out[:-4] + ".glb", os.path.join(final, m + ".glb"))
    print(f"{m}: written in {time.time() - t0:.0f}s")
print(f"OK: Mixamo clips added to {', '.join(models)}" if not problems else "FAIL: " + "; ".join(problems))
sys.exit(1 if problems else 0)
