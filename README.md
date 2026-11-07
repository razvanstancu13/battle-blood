# Battle Blood

A realistic third person medieval battle in Unity 6.5 (URP), made to show what the Local Asset Generation Pipeline (LAGP v1.2, `C:\AI`) can do. Four hundred against four hundred on new ground every battle. You command the blue army from above, and can drop into any moment as one random soldier of it; fall, and you fight on as another. **Practice** on the menu is one soldier against one, with no damage, to look at the moves.

## Play

Run `Build/BattleBlood.exe`, or open the project in Unity 6000.5.10f1 and press Play in `Assets/Game/Scenes/Main.unity`.

| Control | Infantry | Archer | Cavalry |
|---|---|---|---|
| WASD, mouse | Move, look | Move, look | Steer, look |
| Shift / Ctrl | Sprint / walk | Sprint / walk | Gallop |
| Left mouse | Two-cut combo | Hold to draw, release to shoot | Cut |
| Right mouse | Block with the shield | Aim (zoom, time at half speed) | |
| Q | Shield bash | | Charge |
| Space | Dodge roll (lands facing your aim) | Dodge roll | Charge |
| F | | Lit arrow (8 s reload) | |
| V | First / third person | First / third person | First / third person |
| Esc | Pause | Pause | Pause |

**The battle opens in command mode, during the deployment.** Both armies stand waiting. Move your troops anywhere on your side of the yellow line (the red commander sets out his own meanwhile), then press **Start Battle** (or Enter). Before the start only moves are taken, no attacks; at the start deployment orders end and everyone fights on his own until given new ones.

**Tab** switches between command mode (the whole blue army from above) and fighting as your soldier.

| Command mode | |
|---|---|
| WASD or screen edges, wheel, Q / E or middle drag | Pan, zoom, turn the view |
| LMB, drag, Shift | Select, box select, add to the selection |
| Double-click a soldier; 1 2 3; Ctrl+A | All of his kind on screen; all infantry, archers, cavalry; everyone |
| RMB on the ground / on an enemy | Move there in formation / attack him |
| A then LMB | Attack-move: fight whatever is met on the way |
| H | Hold: fight what comes near, never chase far |

Soldiers without orders fight on their own. Your own soldier fights by himself while you command; taking him back clears his orders.

Riding through men on foot tramples them; at a walk or trot the horse shoulders them aside. A shield bash shoves a man back. Headshots do double damage. Water puts out a burning man. Every kill of yours plays out as a slow-motion kill shot, the camera swung round the man falling. Landed arrows, blood and burnt ground fade after a few seconds; a burnt tree stands on as a dead tree.

**Weather** changes with every battle: clear, fog, rain (fires catch less and burn out sooner) or a thunderstorm. Procedural mountains ring the field, a new range every battle.

**Practice** (main menu): pick your soldier and the opponent. In the bout: 1 2 3 your soldier, 4 5 6 the opponent's type, P the opponent fights / stands / blocks, R start again, T slow motion, Esc menu.

## Rules

| Unit (each army) | Health | Damage |
|---|---|---|
| Infantry (160) | 120 | Sword 25; the shield stops most frontal damage. Guards the archers, otherwise goes for the enemy foot |
| Archer (120) | 70 | Arrow 6 to 14 by draw; lit arrows (slow to nock, 8 s reload) set men, grass and wooden props on fire. Shoots from the high ground near him, rushed (short draw, wild aim) with the enemy close, and not into a press where his own side is fighting |
| Cavalry (120) | 150 | Sword 20, up to 30% more at speed; trample 6 to 16; rides round the flank (archers first), charges, fights on the move, and rides out after 2 or 3 cuts that landed or when hemmed in by foot or horse, then wheels and charges again; never stands still |

All the tuning for chance to hit is in `Balance` (`Combat.cs`). Every soldier keeps one target until it is down, except archers, who spread their shots. Nobody can hurt his own side, fire included. The game ends when an army has no men left.

## How the pipeline is reused

- **One model, two armies:** near-white cloth takes the team tint in the shader (`BBLit`), so 3 soldiers and a horse serve both sides. The same shader derives metal from greys, soaks in blood and chars with fire.
- **Placed many times:** each rock and prop is one model, scaled, turned and tinted. Bushes, grass, flowers and reeds are instanced cards.
- **Card trees** (`CardTrees.cs`, `BBLeaves` shader): oaks are the dead-tree model with crowns of photographic leaf-card clumps on its branches; pines are a tapered bark trunk with tiers of flat needle clumps. The clumps are instanced, sway, cast shadows, and are lit as rounded masses. A burnt tree loses its clumps and stands bare. Card photos: `oak_card`, `pine_card`, `pine_bark` in `gen_textures.py`.
- **Tiled ground:** six kinds of ground, each a stack of 8 generated tiles (`gen_textures.py --variants 7`) with derived normal maps; the shader picks and blends tiles with random offsets across the land so the tiling does not show, and the import flattens each tile's broad shading. Blood and burnt ground go in one map that the ground, grass and water read.
- **Severed parts:** cut at runtime from the unit's own mesh by bone weights.
- **Motion capture:** the archer's draw, aim and release and the foot soldiers' idle, run, cuts, block, hits and deaths are Mixamo clips, retargeted onto the generated soldiers (`mixamo_clips.py`); the game only lines them up (the shot's direction, the shield on the arm, grips).
- **The horse's tail:** generated with the horse; `horse_tail_rig.py` gives it bones of its own, and the game swings them with spring physics (`TailBones`).
- **Built from a seed:** terrain, mood, sky and scenery are all made when a battle starts. There is no Animator Controller: clips run through one small Playables graph (`AnimRig`).

## Scripts

Everything repeatable is in `tools/` and ends in one `OK:` or `FAIL:` line. Run them with LAGP's Python: `C:\AI\ComfyUI\python_embeded\python.exe -s tools\<script>`.

| Script | What it does | Example |
|---|---|---|
| `gen_concepts.py` | Concept images for the 3D assets in `assets.json` | `gen_concepts.py --only horse --n 6` |
| `mixamo_clips.py` | Retargets Mixamo clips (archery, sword and shield; list in `mixamo_clips.json`) onto the soldiers' rigs by bone direction | `mixamo_clips.py --models archer --only BowDraw` |
| `horse_tail_rig.py` | Gives the generated horse's long tail its own bones (the rig weights it to the hind legs otherwise) and cuts it free of them; check picture in `Build/clip_sheets/` | `horse_tail_rig.py --dark 0.24` |
| `dock_horse_tail.py` | (old) Painted the tail out of an earlier horse concept | `dock_horse_tail.py` |
| `horse_clips.py` | (tried, not used) Retargets Quaternius' CC0 horse clips onto our horse | `horse_clips.py --map "Run=Gallop,Walk=Walk"` |
| `prop_upright.py` | Front and side renders of props; `--fix` turns leaning ones upright by the angles in `assets.json`, and prints how far the trunk still leans (fitted through the stem) | `prop_upright.py --fix --models dead_tree` |
| `gen_models.py` | Concept to shape, clean, texture, then rig or FBX; resumable | `gen_models.py --only oak --budget 300` |
| `gen_textures.py` | Tiles, skies, cards, effects, icons, logo, menu picture; `--variants N` more tiles of each ground type | `gen_textures.py --variants 7` |
| `gen_audio.py` | Sound effects, ambience and music loops | `gen_audio.py --only horn` |
| `gen_video.py` | Menu background video (not generated yet; the menu shows the still) | `gen_video.py --seconds 7` |
| `contact_sheet.py` | Sheet of generated images for picking, optionally over a colour | `contact_sheet.py <folder> --bg "#667"` |
| `clip_sheet.py` | Frames of a rigged model's clips, to spot skinning artifacts | `clip_sheet.py horse --clips Walk,Run --quad [--glb other.glb]` |
| `import_assets.py` | Copies the picks (`asset_picks.json`) into `Resources/Art`, makes normal maps, levels the audio | `import_assets.py` |
| `level_audio.py` | Brings every sound to the same loudness and checks it | `level_audio.py --check-only` |
| `tile_preview.py` | Shows textures repeated 2 x 2 to check seams | `tile_preview.py out.jpg a.png` |
| `compile_check.py` | Compiles the scripts in batch mode and lists errors | `compile_check.py` |
| `build_game.py` | Applies import rules and settings, builds the Windows player | `build_game.py --skip-content` |
| `verify_game.py` | Plays the build (`--weather 0-3` forces clear, fog, rain or storm; `--onetile` the ground without its variant tiles, to compare): deployment and Start, first person, rules, real input, a forced death, corpses, blood drying, balance table, screenshots in `Build/verify/`; `--practice` photographs each soldier's moves in Practice (`Build/practice/`) | `verify_game.py --seconds 200 --seed 777` |
| `balance_runs.py` | Plays whole battles on several seeds at speed: wins per side, survivors, kills per unit type | `balance_runs.py --seeds 11,22,33,44 --speed 2` |
| `unity_run.py` | Helper: runs C# inside an open Editor | `from unity_run import eval_in_editor` |
| `shrink_video.py` | Smaller copy of a video for sending | `shrink_video.py x.mp4 --mb 28` |

Full rebuild from generated assets: `mixamo_clips.py`, `horse_tail_rig.py`, `prop_upright.py --fix`, `import_assets.py`, then `build_game.py`, then `verify_game.py`.

Things to revisit are listed in `tools/ASSET_ISSUES.md`.

## License
Free for non-commercial use. The code (C# scripts, shaders, `tools/`) is under the [PolyForm Noncommercial License 1.0.0](LICENSE-CODE.md). The assets (models, textures, sounds, music, video) are under [CC BY-NC 4.0](LICENSE-ASSETS.txt). The Mixamo animations stay under Adobe's terms. See [LICENSE.md](LICENSE.md) for the details.
