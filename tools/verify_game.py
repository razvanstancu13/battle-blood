#!/usr/bin/env python3
"""Play the built game automatically: the rules of the brief, a forced player death, screenshots (see Assets/Game/Scripts/Runtime/AutoTest.cs).
Usage: python tools/verify_game.py [--seconds 180] [--seed 1234] [--exe Build/BattleBlood.exe] [--width 1280 --height 720] [--practice] [--weather 0-3]
--practice: the Practice bout instead, close-up pictures of each soldier's moves, in Build/practice/.
Screenshots go to Build/verify/. Ends with OK: / FAIL: from the game's own verdict, plus checks of the screenshots."""
import argparse, glob, os, re, shutil, subprocess, sys, time
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
a = argparse.ArgumentParser(); a.add_argument("--seconds", type=int, default=180); a.add_argument("--exe", default="Build/BattleBlood.exe"); a.add_argument("--seed", type=int, default=-1)
a.add_argument("--width", type=int, default=1280); a.add_argument("--height", type=int, default=720); a.add_argument("--practice", action="store_true"); a.add_argument("--weather", type=int, default=-1, help="0 clear, 1 fog, 2 rain, 3 storm"); a.add_argument("--onetile", action="store_true", help="the ground without its variant tiles, to compare")
args = a.parse_args()
exe = os.path.join(ROOT, args.exe)
shots = os.path.join(ROOT, "Build", "practice" if args.practice else "verify")
shutil.rmtree(shots, ignore_errors=True); os.makedirs(shots, exist_ok=True)
log = os.path.join(shots, "player.log")
t0 = time.time()
cmd = [exe, "-bbtest", "-bbshots", shots, "-bbseconds", str(args.seconds), "-logFile", log, "-screen-fullscreen", "0",
       "-screen-width", str(args.width), "-screen-height", str(args.height)]
if args.seed >= 0: cmd += ["-bbseed", str(args.seed)]
if args.practice: cmd += ["-bbpractice"]
if args.weather >= 0: cmd += ["-bbweather", str(args.weather)]
if args.onetile: cmd += ["-bbonetile"]
try:
    rc = subprocess.run(cmd, timeout=args.seconds + 300).returncode
except subprocess.TimeoutExpired:
    rc = -1
text = open(log, encoding="utf-8", errors="replace").read() if os.path.exists(log) else ""
tests = [l for l in text.splitlines() if l.startswith("[TEST]") or l.startswith("[BATTLE] seed") or l.startswith("[UNIT]")]
for l in tests: print(l[:230])
exc = [l for l in text.splitlines() if re.search(r"(NullReference|Exception:|MissingReference|Shader error|InternalErrorShader)", l)]
for e in sorted(set(exc))[:15]: print("  EXC", e[:220])
png = glob.glob(os.path.join(shots, "*.png"))
fresh = [p for p in png if os.path.getmtime(p) > t0 and os.path.getsize(p) > 20000]
result = [l for l in tests if "RESULT" in l]
ok = rc == 0 and result and "RESULT OK" in result[-1] and not exc and len(fresh) >= 8
print(f"{len(fresh)}/{len(png)} screenshots in {shots}, {len(exc)} exception lines, rc={rc}")
print(f"OK: game verified in {time.time()-t0:.0f}s" if ok else f"FAIL: {result[-1] if result else 'no RESULT line'}")
sys.exit(0 if ok else 1)
