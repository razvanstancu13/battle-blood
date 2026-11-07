#!/usr/bin/env python3
"""Tileable textures, skies, logo, menu keyframe, UI icons and effect sprites of Battle Blood (FLUX via LAGP).
Usage: python tools/gen_textures.py [--only name,name] [--n 3] [--budget 450]
       python tools/gen_textures.py --variants 7 [--only grass,dirt]   more tiles of each ground type, in textures/<name>_variants/
         (the terrain blends between all of them, so the ground does not repeat)
Skips what exists. Ends with OK: / MORE: / FAIL:  Output: C:/AI/outputs/battle_blood/textures/<name>/"""
import argparse, glob, os, subprocess, sys, time
T0 = time.time()
AI = r"C:\AI\ai.cmd"; ROOT = r"C:\AI\outputs\battle_blood\textures"
TOP = "top-down, flat light, photographic, no objects"
T = {
 # Ground: six tiles blended by the terrain shader; each also gives a derived normal map.
 "grass":      (f"seamless natural wild meadow grass ground texture, dense short green blades with dry patches, {TOP}", "--tile"),
 "grass_dry":  (f"seamless dry yellow autumn field grass ground texture, trampled straw, {TOP}", "--tile"),
 "dirt":       (f"seamless packed brown earth battlefield dirt ground texture, small stones, hoof marks, {TOP}", "--tile"),
 "mud":        (f"seamless wet dark swamp mud ground texture, glossy puddles, rotting leaves, {TOP}", "--tile"),
 "rock":       ("seamless weathered grey granite cliff rock texture, cracks and lichen, straight-on, flat light, photographic", "--tile"),
 "moss":       (f"seamless dark green bog moss ground texture, wet peat, {TOP}", "--tile"),
 # Material tiles reused on the procedural meshes and detail.
 "water_n":    ("seamless small gentle water ripples, grey monochrome height map style, top-down, flat light", "--tile"),
 "wood":       ("seamless dark weathered oak wood grain texture, straight long grain, straight-on, flat light, photographic", "--tile"),
 "cloth":      ("seamless plain woven white linen canvas fabric texture, straight-on, flat light, photographic", "--tile"),
 "blood_tile": ("seamless wet dark red blood smears and splatter texture on a white background, drips and streaks, top-down, flat light", "--tile"),
 # Skies: one per mood, chosen by the battle's seed.
 "sky_overcast": ("panoramic 360 degree heavy overcast sky over a battlefield, layered grey storm clouds, thin pale light near the horizon, no land, no birds, no text", "--tile x --w 1536 --h 768"),
 "sky_dawn":     ("panoramic 360 degree sky at dawn, low golden sun haze near the horizon, long soft pink and amber clouds, deep blue above, no land, no birds, no text", "--tile x --w 1536 --h 768"),
 "sky_dusk":     ("panoramic 360 degree sky at blood red sunset, dramatic crimson and orange clouds, deep violet above, no land, no birds, no text", "--tile x --w 1536 --h 768"),
 "sky_fair":     ("panoramic 360 degree sky at midday, scattered white cumulus clouds, pale blue, soft haze at the horizon, no land, no birds, no text", "--tile x --w 1536 --h 768"),
 # Cards: scattered thousands of times by the instanced foliage.
 "grass_card": ("a dense clump of tall wild meadow grass blades growing from the bottom edge, seen from the side, photographic, soft even light, isolated on a plain white background", "--nobg --w 768 --h 768"),
 "flower_card":("a clump of wild meadow grass with small yellow and white wildflowers, seen from the side, growing from the bottom edge, photographic, isolated on a plain white background", "--nobg --w 768 --h 768"),
 "reed_card":  ("a dense clump of tall marsh reeds and cattails, seen from the side, growing from the bottom edge, photographic, isolated on a plain white background", "--nobg --w 768 --h 1024"),
 "feather":    ("a single white goose feather arrow fletching, side view, isolated on a plain white background", "--nobg --w 512 --h 512"),
 "banner":     ("a long hanging medieval war banner, plain white cloth with a large simple heraldic lion rampant in light grey, swallowtail bottom edge, flat front view, filling the frame, isolated on a plain white background", "--nobg --w 512 --h 1024"),
 # Blood and effects.
 "blood_a":    ("a splatter of dark red blood seen from above, irregular drips and droplets, isolated on a plain white background", "--nobg --w 512 --h 512"),
 "blood_b":    ("a long directional spray of dark red blood seen from above, droplets flung in one direction, isolated on a plain white background", "--nobg --w 512 --h 512"),
 "blood_c":    ("many scattered drops and small splashes of bright red blood seen from above, isolated on a plain white background", "--nobg --w 512 --h 512"),
 "blood_pool": ("a large smooth pool of dark red blood seen from above, rounded irregular outline, isolated on a plain white background", "--nobg --w 512 --h 512"),
 "blood_spray":("a burst of red blood mist and flying droplets in mid air, high speed photograph, isolated on a plain white background", "--nobg --w 512 --h 512"),
 "blood_lens": ("thick red blood splattered on a pane of glass, running drips, seen straight-on, isolated on a plain white background", "--nobg --w 1024 --h 1024"),
 "wound":      ("a dark red raw flesh cut surface with a pale bone centre, round cross section, straight-on, isolated on a plain white background", "--nobg --w 512 --h 512"),
 "smoke":      ("soft grey puff of smoke, isolated on a plain white background", "--nobg --w 512 --h 512"),
 "dust":       ("soft pale brown cloud of kicked up dust, isolated on a plain white background", "--nobg --w 512 --h 512"),
 "fire":       ("a single tongue of bright orange fire flames, on a plain pure black background, photographic", "--w 512 --h 512"),
 "spark":      ("a burst of bright orange metal sparks flying from a sword strike, on a plain pure black background, photographic", "--w 512 --h 512"),
 # UI.
 "logo":       ("Bold medieval engraved title logo with the exact words 'BATTLE BLOOD', heavy blackletter capital letters, battered dark steel dripping with fresh red blood, centered on a plain white background, no other text", "--model flux3 --nobg --w 1536 --h 640"),
 "menu_key":   ("cinematic film still, two medieval armies facing each other across a misty valley at dawn, red and blue banners, hundreds of soldiers small in the distance, low sun, rolling hills, drifting smoke, no text, no interface", "--w 1280 --h 704"),
 "icon_infantry": ("a medieval sword crossed over a heater shield, emblem, engraved white silhouette, on a plain black background, centred, no text", "--w 512 --h 512"),
 "icon_archer":   ("a medieval longbow with a nocked arrow, emblem, engraved white silhouette, on a plain black background, centred, no text", "--w 512 --h 512"),
 "icon_cavalry":  ("the head of a charging war horse, emblem, engraved white silhouette, side view, on a plain black background, centred, no text", "--w 512 --h 512"),
 "parchment":  ("seamless aged dark parchment leather texture, worn, straight-on, flat light", "--tile"),
 # Bushes are clumps of these cards, not 3D models: a leafy model costs 2 million triangles to remesh and shatters into hundreds of pieces.
 # Card trees: a real trunk and branches, the crown made of these (the grass's style).
 "oak_card":   ("a large dense spray of green summer oak leaves on a thin twig, many overlapping lobed oak leaves filling the frame in a rounded clump, photographic, soft even light, isolated on a plain white background", "--nobg --w 768 --h 768"),
 "pine_card":  ("a single scots pine branch tip seen from above, dense dark green needles spreading from a thin twig, filling the frame, photographic, soft even light, isolated on a plain white background", "--nobg --w 768 --h 768"),
 "pine_bark":  ("seamless scots pine tree bark texture, reddish brown flaking plates and dark grey furrows, straight-on, flat light, photographic", "--tile"),
 "leaf_card":  ("a large dense spray of wild hazel shrub leaves on thin twigs, many overlapping green leaves filling the frame in a rounded clump, photographic, soft even light, isolated on a plain white background", "--nobg --w 768 --h 768"),
}
GROUND = ["grass", "grass_dry", "dirt", "mud", "rock", "moss"]
a = argparse.ArgumentParser(); a.add_argument("--only", default=""); a.add_argument("--n", type=int, default=3); a.add_argument("--variants", type=int, default=0); a.add_argument("--budget", type=int, default=450, help="seconds; no new job starts after this, rerun to continue (prints MORE)")
args = a.parse_args(); only = set(filter(None, args.only.split(",")))
fails = []
if args.variants:
    for k, name in enumerate(GROUND):
        if only and name not in only: continue
        prompt, extra = T[name]
        out = os.path.join(ROOT, name + "_variants")
        if len(glob.glob(os.path.join(out, f"{name}_*_tile.png"))) >= args.variants: continue
        if time.time() - T0 > args.budget:
            subprocess.run(f'"{AI}" server stop', shell=True, capture_output=True); print("MORE: time budget used, rerun to continue"); sys.exit(0)
        r = subprocess.run(f'"{AI}" image "{prompt}" --n {args.variants} --seed {4100 + k * 17} --name {name} --out "{out}" {extra}', shell=True, capture_output=True, text=True, errors="replace")
        n = len(glob.glob(os.path.join(out, f"{name}_*_tile.png"))); print(name, "variants", n, flush=True)
        if r.returncode or n < args.variants: fails.append(name); print(r.stderr[-600:])
    subprocess.run(f'"{AI}" server stop', shell=True, capture_output=True)
    print(f"FAIL: {fails}" if fails else f"OK: {args.variants} variants of each ground tile in {ROOT}"); sys.exit(1 if fails else 0)
for seed, (name, (prompt, extra)) in enumerate(T.items(), 3000):
    if only and name not in only: continue
    out = os.path.join(ROOT, name)
    if len(glob.glob(os.path.join(out, f"{name}_*.png"))) >= args.n: continue
    if time.time() - T0 > args.budget:
        subprocess.run(f'"{AI}" server stop', shell=True, capture_output=True); print("MORE: time budget used, rerun to continue"); sys.exit(0)
    r = subprocess.run(f'"{AI}" image "{prompt}" --n {args.n} --seed {seed} --name {name} --out "{out}" {extra}', shell=True, capture_output=True, text=True, errors="replace")
    n = len(glob.glob(os.path.join(out, f"{name}_*.png"))); print(name, n, flush=True)
    if r.returncode or n < args.n: fails.append(name); print(r.stderr[-600:])
subprocess.run(f'"{AI}" server stop', shell=True, capture_output=True)
print(f"FAIL: {fails}" if fails else "OK: textures in " + ROOT); sys.exit(1 if fails else 0)
