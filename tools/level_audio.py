#!/usr/bin/env python3
"""Bring every sound of the game to the same loudness, and prove it by measuring the results.
Usage: python tools/level_audio.py [--out Assets/Game/Resources/Art/Audio] [--target -21] [--check-only]
Picks come from tools/asset_picks.json, sources from C:/AI/outputs/battle_blood/audio.

Metric (EBU R128, ffmpeg ebur128): one-shots = loudest 400 ms window (momentary max), with silence padded on so that sounds
shorter than 400 ms can be measured at all. Loops (music, ambience) = integrated loudness. Gain is applied, then a limiter only
if the peak would pass -1 dBTP; repeated until on target. The mix between kinds of sound is set in the game, not in these files.
Fails if any file is off target by more than --tolerance LU, peaks above -0.9 dBTP, or a loop changed length.
Ends with OK: / FAIL:"""
import argparse, json, os, re, shutil, subprocess, sys, tempfile
HERE = os.path.dirname(os.path.abspath(__file__))
FFMPEG = r"C:\AI\tools\ffmpeg\ffmpeg.exe"
if not os.path.exists(FFMPEG):
    FFMPEG = shutil.which("ffmpeg") or next((os.path.join(d, "ffmpeg.exe") for d, _, fs in os.walk(r"C:\AI\tools") if "ffmpeg.exe" in fs), None)
SRC = r"C:\AI\outputs\battle_blood\audio"
LOOPS = ("wind", "swamp", "melee", "gallop", "fire", "music_menu", "music_battle", "music_march")
a = argparse.ArgumentParser()
a.add_argument("--out", default=os.path.join(os.path.dirname(HERE), "Assets", "Game", "Resources", "Art", "Audio"))
a.add_argument("--target", type=float, default=-21.0); a.add_argument("--tolerance", type=float, default=0.5)
a.add_argument("--check-only", action="store_true")
args = a.parse_args()

def measure(path, loop):
    """(loudness, true peak dBTP, seconds)"""
    pad = "" if loop else "apad=pad_dur=1,"
    r = subprocess.run([FFMPEG, "-hide_banner", "-nostats", "-i", path, "-af", f"{pad}ebur128=framelog=info:peak=true", "-f", "null", "-"],
                       capture_output=True, text=True, errors="replace")
    t = r.stderr
    dur = re.search(r"Duration: (\d+):(\d+):([\d.]+)", t)
    secs = int(dur[1]) * 3600 + int(dur[2]) * 60 + float(dur[3]) if dur else 0.0
    peak = re.findall(r"Peak:\s*(-?[\d.]+|-inf) dBFS", t)
    p = float(peak[-1]) if peak and peak[-1] != "-inf" else -99.0
    if loop:
        m = re.findall(r"I:\s*(-?[\d.]+) LUFS", t)
        return float(m[-1]) if m else -99.0, p, secs
    vals = [float(x) for x in re.findall(r"M:\s*(-?[\d.]+)", t)]
    return (max(vals) if vals else -99.0), p, secs

def apply(src, dst, gain):
    f = f"volume={gain:.3f}dB,alimiter=limit=0.72:attack=1:release=30:level=disabled"
    r = subprocess.run([FFMPEG, "-hide_banner", "-loglevel", "error", "-y", "-i", src, "-af", f, "-c:a", "libvorbis", "-q:a", "6", dst],
                       capture_output=True, text=True, errors="replace")
    if r.returncode: raise RuntimeError(r.stderr[-300:])

picks = json.load(open(os.path.join(HERE, "asset_picks.json")))["audio"]
os.makedirs(args.out, exist_ok=True)
problems, rows = [], []
tmp = tempfile.mkdtemp(dir=os.environ.get("TEMP"))
for group, files in picks.items():
    loop = group in LOOPS
    for i, name in enumerate(files):
        src = os.path.join(SRC, group, name)
        dst = os.path.join(args.out, f"{group}_{i}.ogg")
        if not os.path.exists(src): problems.append("missing " + src); continue
        before, _, src_secs = measure(src, loop)
        if not args.check_only:
            gain = args.target - before
            work = os.path.join(tmp, f"{group}_{i}.ogg")
            for _ in range(6):
                apply(src, work, gain)
                now, _, _ = measure(work, loop)
                if abs(now - args.target) <= 0.2: break
                gain += args.target - now
            shutil.copyfile(work, dst)
        if not os.path.exists(dst): problems.append("not levelled yet " + dst); continue
        after, peak, secs = measure(dst, loop)
        rows.append((f"{group}_{i}", before, after, peak, secs))
        if abs(after - args.target) > args.tolerance: problems.append(f"{group}_{i} at {after:.1f}, target {args.target}")
        if peak > -0.9: problems.append(f"{group}_{i} peak {peak:.1f} dBTP")
        if loop and abs(secs - src_secs) > 0.05: problems.append(f"{group}_{i} length {src_secs:.2f} -> {secs:.2f} s")
shutil.rmtree(tmp, ignore_errors=True)
print(f"{'sound':18} {'before':>7} {'after':>7} {'peak':>6} {'sec':>6}")
for n, b, x, p, s in rows: print(f"{n:18} {b:7.1f} {x:7.1f} {p:6.1f} {s:6.2f}")
if rows:
    v = [r[2] for r in rows]; o = [r[1] for r in rows]
    print(f"spread: before {max(o) - min(o):.1f} LU, after {max(v) - min(v):.1f} LU")
for p in problems: print("  " + p)
print(f"FAIL: {len(problems)} problems" if problems else f"OK: {len(rows)} sounds at {args.target} LUFS +/- {args.tolerance} in {args.out}")
sys.exit(1 if problems else 0)
