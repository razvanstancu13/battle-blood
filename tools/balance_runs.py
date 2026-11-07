#!/usr/bin/env python3
"""Play several whole battles at speed and report who wins and how: to find a bias between the armies and to tune the unit types.
Usage: python tools/balance_runs.py [--seeds 11,22,33,44,55,66] [--speed 2] [--seconds 300] [--exe Build/BattleBlood.exe]
Each seed runs the build with -bbtest -bbquick (no player tests, no screenshots). The player's soldier is run by the AI, as everyone else.
Prints one line per battle, then wins per side, average survivors and kills per unit type.
Ends with OK: (every battle ran and finished) / FAIL:"""
import argparse, os, re, subprocess, sys, tempfile, time
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
a = argparse.ArgumentParser(); a.add_argument("--seeds", default="11,22,33,44,55,66"); a.add_argument("--speed", type=float, default=2.0)
a.add_argument("--seconds", type=int, default=300); a.add_argument("--exe", default="Build/BattleBlood.exe")
args = a.parse_args()
exe = os.path.join(ROOT, args.exe)
if not os.path.exists(exe): print("FAIL: no build at " + exe); sys.exit(1)
rows, problems = [], []
kills = {"Infantry": [], "Archer": [], "Cavalry": []}
t0 = time.time()
for seed in [int(s) for s in args.seeds.split(",") if s]:
    log = os.path.join(tempfile.gettempdir(), f"bb_balance_{seed}.log")
    if os.path.exists(log): os.remove(log)
    cmd = [exe, "-bbtest", "-bbquick", "-bbseed", str(seed), "-bbspeed", str(args.speed), "-bbseconds", str(args.seconds),
           "-logFile", log, "-screen-fullscreen", "0", "-screen-width", "960", "-screen-height", "540"]
    try:
        rc = subprocess.run(cmd, timeout=args.seconds + 240).returncode
    except subprocess.TimeoutExpired:
        rc = -1
    text = open(log, encoding="utf-8", errors="replace").read() if os.path.exists(log) else ""
    outcome = re.search(r"outcome before any ending by the test: blue (\d+), red (\d+), battle time (\d+)s, over (\w+)", text)
    mood = re.search(r"mood ([A-Za-z ]+?), swamp (\d+) %, hills (\d+) %", text)
    if not outcome: problems.append(f"seed {seed}: no outcome line (rc={rc})"); print(f"seed {seed}: no outcome, rc={rc}"); continue
    blue, red, secs, over = int(outcome[1]), int(outcome[2]), int(outcome[3]), outcome[4] == "True"
    winner = "blue" if red == 0 and blue > 0 else "red" if blue == 0 and red > 0 else "none"
    for t in kills:
        k = re.search(rf"balance {t}\s+attacks\s+\d+\s+hits\s+\d+ \(\d+%\)\s+damage\s+\d+\s+kills\s+(\d+)", text)
        if k: kills[t].append(int(k[1]))
    rows.append((seed, winner, blue, red, secs, over))
    print(f"seed {seed:>6}: {winner:>4} wins, blue {blue:>3} red {red:>3} left, {secs:>3}s{'' if over else ' (not over)'}"
          + (f", {mood[1]}, swamp {mood[2]}%, hills {mood[3]}%" if mood else ""), flush=True)
    if "RESULT OK" not in text: problems.append(f"seed {seed}: test result not OK")
blue_w = sum(1 for r in rows if r[1] == "blue"); red_w = sum(1 for r in rows if r[1] == "red")
print(f"\nwins: blue {blue_w}, red {red_w}, none {len(rows) - blue_w - red_w} of {len(rows)}")
if rows:
    print(f"average left: blue {sum(r[2] for r in rows) / len(rows):.0f}, red {sum(r[3] for r in rows) / len(rows):.0f}; average length {sum(r[4] for r in rows) / len(rows):.0f}s")
for t, v in kills.items():
    if v: print(f"kills {t:<8} average {sum(v) / len(v):.0f}  ({', '.join(map(str, v))})")
print(f"OK: {len(rows)} battles in {time.time() - t0:.0f}s" if rows and not problems else f"FAIL: {problems}")
sys.exit(0 if rows and not problems else 1)
