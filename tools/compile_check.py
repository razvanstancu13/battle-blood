#!/usr/bin/env python3
"""Compile the project's scripts in Unity batch mode and list the errors.
Usage: python tools/compile_check.py   (no Editor may have the project open). Ends with OK: / FAIL:"""
import os, re, subprocess, sys
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
log = os.path.join(ROOT, "Logs", "compile.log")
r = subprocess.run(["C:/Program Files/Unity/Hub/Editor/6000.5.10f1/Editor/Unity.exe", "-batchmode", "-quit", "-projectPath", ROOT, "-logFile", log], timeout=3000)
text = open(log, encoding="utf-8", errors="replace").read()
errs = sorted(set(re.findall(r"^.*error CS\d+.*$", text, re.M)))
shader = sorted(set(re.findall(r"^.*Shader error.*$", text, re.M)))
for e in errs + shader: print(e[:240])
ok = r.returncode == 0 and not errs and not shader
print(f"OK: scripts compile" if ok else f"FAIL: rc={r.returncode}, {len(errs)} compile errors, {len(shader)} shader errors, log {log}")
sys.exit(0 if ok else 1)
