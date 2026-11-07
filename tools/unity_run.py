#!/usr/bin/env python3
"""Helper for scripts that need Unity. A project open in an Editor cannot also run in batch mode, so they run their C# inside that Editor.
Usage: from unity_run import editor_open, eval_in_editor
Self-check: python tools/unity_run.py   ends with OK: / FAIL:"""
import json, os, shutil, subprocess, sys, time

def _unity():
    return shutil.which("unity") or os.path.expandvars(r"%LOCALAPPDATA%\Unity\bin\unity.exe")

def editor_open(root):
    """True when a connected Editor has this project open."""
    try:
        r = subprocess.run([_unity(), "status", "--format", "json", "--no-banner"], capture_output=True, text=True, timeout=60)
        for i in json.loads(r.stdout)["data"]["instances"]:
            if os.path.normcase(os.path.abspath(i["projectPath"])) == os.path.normcase(os.path.abspath(root)): return True
    except Exception:
        pass
    return False

def eval_in_editor(root, code, timeout=1800, retry=True):
    """Runs C# inside the Editor. Returns 0 when it ran without error, else 1 (the error is printed)."""
    # A recompile reloads the Editor for a moment: wait until it answers.
    subprocess.run([_unity(), "status", "--until-ready", "--project-path", root], capture_output=True, timeout=300)
    try:
        r = subprocess.run([_unity(), "command", "script", "eval", code, "--project-path", root, "--timeout", str(timeout), "--format", "json"],
                           capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=timeout + 60)
    except subprocess.TimeoutExpired:
        print("timed out waiting for the Editor"); return 1
    if "EDITOR_NOT_RUNNING" in r.stdout and retry:      # still reloading after a recompile
        time.sleep(15)
        return eval_in_editor(root, code, timeout, False)
    try:
        d = json.loads(r.stdout)
        res = d["data"]["result"]
        if d.get("success") and res.get("success"): return 0
        print(str(res)[:1500])
    except Exception:
        print((r.stdout + r.stderr)[:1500])
    return 1

if __name__ == "__main__":
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    if not editor_open(root): print("OK: no Editor has this project open, batch mode will be used"); sys.exit(0)
    rc = eval_in_editor(root, 'return "alive";', 60)
    print("OK: Editor answers" if rc == 0 else "FAIL: Editor is open but does not answer")
    sys.exit(rc)
