#!/usr/bin/env python3
"""Apply the import rules and settings, make the scene, and build the Windows player in Unity.
Usage: python tools/build_game.py [--skip-content] [--output Build/BattleBlood.exe]
If the project is open in an Editor the build runs inside it, otherwise in batch mode. Checks the player's files are newer than the start and the log has the success line.
Ends with OK: / FAIL:"""
import argparse, os, re, subprocess, sys, time
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
a = argparse.ArgumentParser(); a.add_argument("--skip-content", action="store_true")
a.add_argument("--output", default="Build/BattleBlood.exe"); a.add_argument("--timeout", type=int, default=3000)
args = a.parse_args()
log = os.path.join(ROOT, "Logs", "build_game.log")
os.makedirs(os.path.dirname(log), exist_ok=True)
if os.path.exists(log): os.remove(log)    # an old log must not pass for this build
t0 = time.time()
exe = os.path.join(ROOT, args.output)
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from unity_run import editor_open, eval_in_editor
if editor_open(ROOT):
    print("project is open in an Editor: building inside it")
    # The Editor only recompiles changed scripts when it gets focus; a build from stale scripts fails ("class layout is incompatible").
    eval_in_editor(ROOT, 'UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceSynchronousImport); UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation(); return "compiling";', 600)
    time.sleep(5)
    # Compiling can outlast the wait: Unity then refuses ("scripts are compiling"), so wait and try again, up to about 5 minutes.
    for attempt in range(30):
        rc = eval_in_editor(ROOT, f'return BattleBlood.EditorTools.Builder.BuildInEditor("{args.output}", {"true" if args.skip_content else "false"}, "Logs/build_game.log");', args.timeout)
        busy = os.path.exists(log) and "scripts are compiling" in open(log, encoding="utf-8", errors="replace").read()
        if not busy: break
        print("the Editor is still compiling: trying again in 10 s")
        os.remove(log); time.sleep(10)
else:
    cmd = ["C:/Program Files/Unity/Hub/Editor/6000.5.10f1/Editor/Unity.exe", "-batchmode", "-quit", "-projectPath", ROOT,
           "-executeMethod", "BattleBlood.EditorTools.Builder.BuildPlayerBatch", "-logFile", log, "-bboutput", args.output]
    if args.skip_content: cmd.append("-bbskipcontent")
    try:
        rc = subprocess.run(cmd, timeout=args.timeout).returncode
    except subprocess.TimeoutExpired:
        rc = -1
text = open(log, encoding="utf-8", errors="replace").read() if os.path.exists(log) else ""
if "compile errors" in text:
    # The open Editor writes its compile errors to its own log: show the latest ones (the tail, so old fixed ones do not show).
    ed = os.path.join(ROOT, "Logs", "Editor.log")
    if os.path.exists(ed):
        tail = open(ed, encoding="utf-8", errors="replace").read()[-400000:]
        text += "\n" + "\n".join(sorted(set(l for l in tail.splitlines() if re.search(r"error CS\d+", l))))
for line in text.splitlines():
    if line.startswith("[BUILD]"): print(line[:170])
errors = [l for l in text.splitlines() if re.search(r"error CS\d+|\[BUILD\].*(missing|no model)|Shader error", l)]
for e in sorted(set(errors))[:25]: print("  ", e[:220])
data = os.path.splitext(exe)[0] + "_Data"   # the exe itself is not rewritten by incremental builds
newest = max((os.path.getmtime(os.path.join(d, f)) for d, _, fs in os.walk(data) for f in fs), default=0)
ok = rc == 0 and os.path.exists(exe) and newest > t0 and "[BUILD] player Succeeded" in text and not errors
if ok:
    size = sum(os.path.getsize(os.path.join(d, f)) for d, _, fs in os.walk(os.path.dirname(exe)) for f in fs) / 1e6
    print(f"OK: {args.output} built in {time.time()-t0:.0f}s, folder {size:.0f} MB")
else:
    print(f"FAIL: rc={rc}, {len(errors)} errors, log {log}")
sys.exit(0 if ok else 1)
