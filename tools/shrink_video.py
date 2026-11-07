#!/usr/bin/env python3
"""Make a smaller copy of a video that fits a size limit, for sending (two-pass x264, so the size is what was asked for).
Usage: python tools/shrink_video.py Build/gameplay.mp4 [--mb 28] [--height 720] [--fps 30] [--out Build/gameplay_small.mp4]
Ends with OK: / FAIL:"""
import argparse, glob, json, os, subprocess, sys, tempfile
a = argparse.ArgumentParser()
a.add_argument("video"); a.add_argument("--mb", type=float, default=28); a.add_argument("--height", type=int, default=720)
a.add_argument("--fps", type=int, default=30); a.add_argument("--out"); a.add_argument("--audio_kbps", type=int, default=96)
args = a.parse_args()
tools = next(os.path.dirname(p) for p in glob.glob(r"C:\AI\tools\ffmpeg\**\ffmpeg.exe", recursive=True))
FFMPEG, FFPROBE = os.path.join(tools, "ffmpeg.exe"), os.path.join(tools, "ffprobe.exe")
out = args.out or os.path.splitext(args.video)[0] + "_small.mp4"
def length(path):
    return float(json.loads(subprocess.run([FFPROBE, "-v", "error", "-show_entries", "format=duration", "-of", "json", path], capture_output=True, text=True).stdout)["format"]["duration"])
seconds = length(args.video)
video_kbps = int(args.mb * 1024 * 1024 * 8 / 1000 / seconds * 0.97) - args.audio_kbps   # 3 % for the container
if video_kbps < 200: print(f"FAIL: {args.mb} MB leaves only {video_kbps} kbps of picture for {seconds:.0f} s"); sys.exit(1)
common = ["-vf", f"scale=-2:{args.height},fps={args.fps}", "-c:v", "libx264", "-preset", "slow", "-b:v", f"{video_kbps}k", "-pix_fmt", "yuv420p"]
with tempfile.TemporaryDirectory() as tmp:
    stats = os.path.join(tmp, "pass")
    for cmd in ([FFMPEG, "-hide_banner", "-loglevel", "error", "-y", "-i", args.video, *common, "-pass", "1", "-passlogfile", stats, "-an", "-f", "null", os.devnull],
                [FFMPEG, "-hide_banner", "-loglevel", "error", "-y", "-i", args.video, *common, "-pass", "2", "-passlogfile", stats, "-c:a", "aac", "-b:a", f"{args.audio_kbps}k", "-movflags", "+faststart", out]):
        r = subprocess.run(cmd, capture_output=True, text=True, errors="replace")
        if r.returncode != 0: print("FAIL: " + r.stderr[:400]); sys.exit(1)
size = os.path.getsize(out) / 1024 / 1024
if size > args.mb * 1.05 or abs(length(out) - seconds) > 0.5: print(f"FAIL: {out} is {size:.1f} MB, {length(out):.1f} s"); sys.exit(1)
print(f"OK: {out} {size:.1f} MB, {length(out):.0f} s, {video_kbps} kbps picture")
