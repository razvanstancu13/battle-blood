#!/usr/bin/env python3
"""Contact sheets of generated images: one row per sub-folder.
Usage: python tools/contact_sheet.py <folder> [--rows 8] [--cols 4] [--size 256] [--only a,b] [--bg "#667"]
Writes <folder>/sheet0.jpg, sheet1.jpg... Ends with OK: / FAIL:"""
import argparse, glob, os, sys
from PIL import Image
a = argparse.ArgumentParser(); a.add_argument("folder"); a.add_argument("--rows", type=int, default=8)
a.add_argument("--cols", type=int, default=4); a.add_argument("--size", type=int, default=256)
a.add_argument("--only", default="", help="comma list of sub-folders"); a.add_argument("--bg", default="", help="composite transparency over this colour, e.g. #667")
args = a.parse_args()
names = sorted(n for n in os.listdir(args.folder) if os.path.isdir(os.path.join(args.folder, n)))
if args.only: names = [n for n in names if n in args.only.split(",")]
if not names: print("FAIL: no sub-folders in " + args.folder); sys.exit(1)
S = args.size; written = 0
for k in range(0, len(names), args.rows):
    chunk = names[k:k + args.rows]
    sheet = Image.new("RGB", (args.cols * S, len(chunk) * S), "white")
    for y, n in enumerate(chunk):
        all_png = sorted(glob.glob(os.path.join(args.folder, n, "*.png")))
        tiles = [f for f in all_png if f.endswith("_tile.png")]   # tileable textures: show the seamless version
        files = (tiles or all_png)[:args.cols]
        for x, f in enumerate(files):
            im = Image.open(f)
            if args.bg:
                im = im.convert("RGBA"); back = Image.new("RGBA", im.size, args.bg); back.alpha_composite(im); im = back
            sheet.paste(im.convert("RGB").resize((S, S)), (x * S, y * S))
    p = os.path.join(args.folder, f"sheet{written}.jpg"); sheet.save(p, quality=85); written += 1
    print(p, chunk)
print(f"OK: {written} sheets for {len(names)} folders")
