#!/usr/bin/env python3
"""Show tileable textures repeated 2 x 2, side by side, to check their seams; alpha images are shown on grey.
Usage: python tools/tile_preview.py out.jpg a.png b.png ... [--size 420]. Ends with OK: / FAIL:"""
import argparse, sys
from PIL import Image
a = argparse.ArgumentParser(); a.add_argument("out"); a.add_argument("files", nargs="+"); a.add_argument("--size", type=int, default=420); a.add_argument("--cols", type=int, default=3)
args = a.parse_args()
S = args.size; rows = (len(args.files) + args.cols - 1) // args.cols
sheet = Image.new("RGB", (S * args.cols, S * rows), (128, 128, 128))
for i, f in enumerate(args.files):
    im = Image.open(f).convert("RGBA").resize((S // 2, S // 2))
    for x in (0, S // 2):
        for y in (0, S // 2):
            sheet.paste(im, (i % args.cols * S + x, i // args.cols * S + y), im)
sheet.save(args.out, quality=88)
print(f"OK: {args.out} with {len(args.files)} textures")
