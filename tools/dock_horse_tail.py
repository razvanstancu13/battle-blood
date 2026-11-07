#!/usr/bin/env python3
"""Paint the lower tail out of the horse concept, leaving a short docked tail. LAGP's four-legged rig has its tail bones running back from the
rump, so a long hanging tail has no bone in its lower half and gets dragged by the hind leg (a flapping sheet in Walk and Run).
Usage: python tools/dock_horse_tail.py [--src horse_05.png] [--dst horse_07.png] [--cut-y 505]
Only the area left of the hind leg is painted white, so the leg is untouched. Checks the tail below the cut is gone and the leg is not.
Ends with OK: / FAIL:   Output: C:/AI/outputs/battle_blood/concepts/horse/<dst>"""
import argparse, os, sys
import numpy as np
from PIL import Image
ROOT = r"C:\AI\outputs\battle_blood\concepts\horse"
a = argparse.ArgumentParser(); a.add_argument("--src", default="horse_05.png"); a.add_argument("--dst", default="horse_07.png")
a.add_argument("--cut-y", type=int, default=505); args = a.parse_args()
im = np.asarray(Image.open(os.path.join(ROOT, args.src)).convert("RGB")).copy()
h, w, _ = im.shape
lum = im.mean(2)
before_leg = lum[700:900, 212:280].copy()
# Everything left of the hind leg below the cut goes: the hair, its fringe, the strip touching the leg. The leg's edge runs slightly right going down.
for y in range(args.cut_y, h):
    limit = int(np.interp(y, [505, 560, 600, 650, 700, 830, 900], [172, 188, 200, 200, 189, 204, 204]))
    im[y, :limit] = (255, 255, 255)
    if y < 690:      # a sliver of hair lies along the thigh: grey or black pixels just past the edge; the coat beside it is reddish
        for x in range(limit, limit + 22):
            if int(im[y, x].max()) - int(im[y, x].min()) < 34: im[y, x] = (255, 255, 255)
# The stub ends in a rounded tip, not a square cut: its left edge moves in over the last rows.
for y in range(args.cut_y - 60, args.cut_y):
    k = (y - (args.cut_y - 60)) / 60.0
    im[y, :int(120 + 58 * k * k)] = (255, 255, 255)
out = os.path.join(ROOT, args.dst)
Image.fromarray(im).save(out)
lum2 = im.mean(2)
tail_left = int((lum2[args.cut_y + 30:830, :186] < 120).sum())
leg_changed = float(np.abs(lum2[700:900, 212:280] - before_leg).mean())
ok = tail_left < 400 and leg_changed < 0.5
print(f"dark tail pixels left below the cut: {tail_left}, leg change: {leg_changed:.2f}")
print(f"OK: {out}" if ok else "FAIL: the tail was not cleanly removed")
sys.exit(0 if ok else 1)
