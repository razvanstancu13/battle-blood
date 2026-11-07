#!/usr/bin/env python3
"""Sound effects, ambience and music of Battle Blood (Stable Audio / ACE-Step via LAGP).
Usage: python tools/gen_audio.py [--only name,name] [--budget 450]
Skips what exists. Ends with OK: / MORE: / FAIL:  Output: C:/AI/outputs/battle_blood/audio/<name>/"""
import argparse, glob, os, subprocess, sys, time
T0 = time.time()
AI = r"C:\AI\ai.cmd"; ROOT = r"C:\AI\outputs\battle_blood\audio"
S = {  # name: (prompt, seconds, n)
 "swing": ("a sword swung fast through the air, short sharp whoosh, no impact, no voice", 1.0, 3),
 "clash": ("steel sword striking a steel sword, one sharp metallic clang with ringing", 1.0, 3),
 "block": ("a sword hitting a wooden shield, heavy dull wooden thud with a metal rattle", 1.0, 3),
 "flesh": ("a blade cutting into a body, wet heavy thump, no voice", 1.0, 4),
 "gore": ("wet flesh splatter, heavy blood squelch", 1.0, 4),
 "sever": ("a heavy blade chopping through flesh and bone, wet crunch and crack", 1.0, 4),
 "spurt": ("blood spurting and gushing in wet pulses, liquid splatter", 1.5, 3),
 "bash": ("a heavy wooden shield slamming into a body, deep padded thump", 1.0, 2),
 "bow_draw": ("a wooden longbow being drawn, creaking wood and tightening string", 1.0, 2),
 "bow_release": ("a longbow string releasing an arrow, sharp twang and quick swish", 1.0, 3),
 "arrow_body": ("an arrow striking a body, short sharp wet thock", 1.0, 3),
 "arrow_wood": ("an arrow hitting a wooden shield, sharp woody thock with a quiver", 1.0, 2),
 "arrow_ground": ("an arrow sticking into soft earth, short dull thud", 1.0, 2),
 "arrow_light": ("a cloth wrapped arrow tip catching fire, short soft flame whoosh and crackle", 1.0, 2),
 "fire_fly": ("a burning arrow flying past, fluttering flame whoosh", 1.0, 2),
 "ignite": ("a body bursting into flames, sudden fiery whoomp and crackle", 1.2, 3),
 "burn_scream": ("a man burning alive, desperate agonised male scream, no words", 2.5, 3),
 "step": ("a single boot footstep on grass and earth", 1.0, 4),
 "step_mud": ("a single squelching footstep in wet mud and shallow water", 1.0, 3),
 "hoof": ("a single horse hoof striking hard earth, deep thud", 1.0, 3),
 "neigh": ("a war horse neighing loudly", 2.0, 2),
 "horse_scream": ("a wounded war horse screaming in pain", 2.0, 2),
 "snort": ("a horse snorting and blowing through its nose", 1.2, 2),
 "trample": ("a heavy horse crashing into a man, deep body thump and armour rattle", 1.2, 3),
 "horn": ("a medieval war horn, one long low mournful blast, outdoor echo", 4.0, 2),
 "pain": ("a man grunting in pain, short male grunt, no words", 1.0, 4),
 "scream": ("a man screaming in agony on a battlefield, male scream, no words", 2.0, 4),
 "death": ("a man crying out and dying, short male death groan, no words", 1.5, 4),
 "cry": ("one soldier shouting a fierce battle cry, male shout, no words", 1.5, 3),
 "armour": ("chainmail and armour rattling as someone falls to the ground, metallic shuffle and a thud", 1.2, 2),
 "ui_click": ("soft button press, a firm low wooden knock", 1.0, 2),
 "victory": ("triumphant medieval fanfare of brass horns with drums, short victorious sting", 5.0, 2),
 "defeat": ("sad low war horn and slow funeral drum, defeat sting", 5.0, 2),
 "thunder": ("a loud thunderclap followed by deep rolling thunder fading away, no rain", 5.0, 3),
}
LOOPS = {
 "wind": ("open windy plains ambience, steady wind over grass, a few distant birds, no voices", 10, -24),
 "swamp": ("marsh swamp ambience at dusk, frogs, insects, gentle water, faint wind, no voices", 10, -24),
 "melee": ("distant large medieval battle, many men shouting, swords clashing, horses, steady crowd roar", 10, -22),
 "gallop": ("a single horse galloping steadily on earth, rhythmic hooves, no voices", 6, -18),
 "fire": ("a large fire burning steadily, crackling wood", 8, -22),
 "rain": ("steady heavy rain falling on grass and earth, constant downpour, no thunder, no voices", 10, -20),
}
MUSIC = {
 "music_menu": ("dark medieval epic orchestral, slow war drums, low male choir, solemn horns, hurdy gurdy, tension, instrumental", 60, 80, "D minor"),
 "music_battle": ("relentless epic medieval battle orchestral, fast pounding taiko and war drums, driving low strings, huge brass stabs, male choir, instrumental", 66, 150, "D minor"),
 "music_march": ("tense medieval march, snare and low drum cadence, low strings ostinato, distant horns, building anticipation, instrumental", 60, 100, "A minor"),
}
a = argparse.ArgumentParser(); a.add_argument("--only", default=""); a.add_argument("--budget", type=int, default=450, help="seconds; no new job starts after this, rerun to continue (prints MORE)"); args = a.parse_args()
only = set(filter(None, args.only.split(",")))
fails = []
def want(n): return not only or n in only
def run(cmd, name, pattern):
    if time.time() - T0 > args.budget:
        subprocess.run(f'"{AI}" server stop', shell=True, capture_output=True); print("MORE: time budget used, rerun to continue"); sys.exit(0)
    r = subprocess.run(f'"{AI}" {cmd}', shell=True, capture_output=True, text=True, errors="replace")
    hits = glob.glob(pattern); print(name, len(hits), (r.stdout.strip().splitlines() or [""])[-1][:150], flush=True)
    if r.returncode or not hits: fails.append(name); print(r.stderr[-500:])
    return hits
for seed, (n, (p, sec, cnt)) in enumerate(S.items(), 4000):
    out = os.path.join(ROOT, n)
    if want(n) and len(glob.glob(os.path.join(out, "*.ogg"))) < cnt:
        run(f'sfx "{p}" --seconds {sec} --n {cnt} --mono --seed {seed} --name {n} --out "{out}"', n, os.path.join(out, "*.ogg"))
for seed, (n, (p, sec, lufs)) in enumerate(LOOPS.items(), 4100):
    out = os.path.join(ROOT, n)
    if want(n) and not glob.glob(os.path.join(out, "*_loop.ogg")):
        hits = run(f'sfx "{p}" --seconds {sec} --n 1 --lufs {lufs} --seed {seed} --name {n} --out "{out}"', n, os.path.join(out, "*.flac"))
        if hits: run(f'loop "{sorted(hits)[0]}" --seconds {max(4, sec-3)} --name {n} --out "{out}"', n + "_loop", os.path.join(out, "*_loop.ogg"))
for seed, (n, (p, sec, bpm, key)) in enumerate(MUSIC.items(), 4200):
    out = os.path.join(ROOT, n)
    if want(n) and not glob.glob(os.path.join(out, "*_loop.ogg")):
        hits = run(f'music "{p}" --seconds {sec} --bpm {bpm} --key "{key}" --no_plan --seed {seed} --name {n} --out "{out}"', n, os.path.join(out, "*.flac"))
        if hits: run(f'loop "{sorted(hits)[0]}" --bpm {bpm} --bars {32 if bpm > 140 else 16} --name {n} --out "{out}"', n + "_loop", os.path.join(out, "*_loop.ogg"))
subprocess.run(f'"{AI}" server stop', shell=True, capture_output=True)
print(f"FAIL: {fails}" if fails else "OK: audio in " + ROOT); sys.exit(1 if fails else 0)
