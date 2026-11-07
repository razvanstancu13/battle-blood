#!/usr/bin/env python3
"""Menu background video: Wan 2.2 from the FLUX keyframe via LAGP, cut before any jerky join, then forward + reversed so it loops with no jump.
Usage: python tools/gen_video.py [--key C:/.../menu_key_01.png] [--seconds 7]
Stops ComfyUI first and after (the LAGP README warns against reusing a server for HD video). Skips generation if the raw video exists.
Ends with OK: / FAIL:   Output: C:/AI/outputs/battle_blood/video/menu_loop.mp4"""
import argparse, glob, os, shutil, subprocess, sys, tempfile, time
import numpy as np
from PIL import Image
AI = r"C:\AI\ai.cmd"; OUT = r"C:\AI\outputs\battle_blood\video"
FFMPEG = shutil.which("ffmpeg") or next((os.path.join(d, "ffmpeg.exe") for d, _, fs in os.walk(r"C:\AI\tools") if "ffmpeg.exe" in fs), None)
a = argparse.ArgumentParser(); a.add_argument("--key", default=r"C:\AI\outputs\battle_blood\textures\menu_key\menu_key_01.png")
a.add_argument("--seconds", type=int, default=7); a.add_argument("--max-frames", type=int, default=68, help="the look drifts over a chain: keep the start"); args = a.parse_args()
raw = os.path.join(OUT, "menu.mp4"); loop = os.path.join(OUT, "menu_loop.mp4")
t0 = time.time()
if not (os.path.exists(raw) and os.path.getsize(raw) > 100000):
    prompt = "very slow camera push forward across a misty valley towards two armies, banners ripple in the wind, smoke and fog drift slowly, clouds moving"
    subprocess.run(f'"{AI}" server stop', shell=True, capture_output=True)
    r = subprocess.run(f'"{AI}" video "{prompt}" --image "{args.key}" --seconds {args.seconds} --w 1280 --h 704 --seed 900 --name menu --out "{OUT}"', shell=True, capture_output=True, text=True, errors="replace")
    print((r.stdout + r.stderr)[-500:])
    subprocess.run(f'"{AI}" server stop', shell=True, capture_output=True)
if not os.path.exists(raw) or os.path.getsize(raw) < 100000:
    print("FAIL: no raw video " + raw); sys.exit(1)
# A chained video can jerk at a join: keep only what comes before the first jump far above the clip's own motion.
tmp = tempfile.mkdtemp()
subprocess.run([FFMPEG, "-hide_banner", "-loglevel", "error", "-y", "-i", raw, "-vf", "scale=160:88", os.path.join(tmp, "%04d.png")])
frames = [np.asarray(Image.open(f).convert("RGB"), dtype=np.float32) for f in sorted(glob.glob(os.path.join(tmp, "*.png")))]
shutil.rmtree(tmp, ignore_errors=True)
steps = [float(np.abs(frames[i + 1] - frames[i]).mean()) for i in range(len(frames) - 1)]
median = float(np.median(steps))
jumps = [i for i, d in enumerate(steps) if d > 6 * median]
keep = min((jumps[0] - 1) if jumps else len(frames), args.max_frames)
print(f"{len(frames)} frames, median step {median:.2f}, jumps at {[(i, round(steps[i], 1)) for i in jumps]}, keeping {keep} frames")
if keep < 48: print("FAIL: under 2 s of clean video"); sys.exit(1)
r = subprocess.run([FFMPEG, "-hide_banner", "-loglevel", "error", "-y", "-i", raw, "-filter_complex", f"[0:v]trim=end_frame={keep},setpts=2.2*(PTS-STARTPTS),minterpolate=fps=24:mi_mode=blend,split[a][b];[b]reverse[r];[a][r]concat=n=2:v=1[v]",
                    "-map", "[v]", "-an", "-c:v", "libx264", "-profile:v", "baseline", "-bf", "0", "-pix_fmt", "yuv420p", "-crf", "20", loop], capture_output=True, text=True, errors="replace")
ok = r.returncode == 0 and os.path.exists(loop) and os.path.getmtime(loop) > t0 and os.path.getsize(loop) > 100000
if not ok: print(r.stderr[-400:])
print(f"OK: {loop} {os.path.getsize(loop) / 1e6:.1f} MB in {time.time() - t0:.0f}s" if ok else "FAIL: loop not written")
sys.exit(0 if ok else 1)
