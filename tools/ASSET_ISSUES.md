# Things to revisit after the first playable build
Nothing here is being regenerated until then. Each line is what was seen and where the alternative is kept.

- horse: a thin spike joins the front hooves in Walk and Trot. `Unit.NoLegBridges` removes those triangles at load; confirm in the game that it worked.
- horse: the concept has visible genitals, which the mesh keeps. Only worth fixing if it shows in play.
- horse (pipeline): the four-legged rig's tail bones run straight back, so a long hanging tail follows the hind leg. Worked round by painting the tail short (`tools/dock_horse_tail.py`) and adding a simulated full tail in the game (`HorseTail.cs`, 2026-11-06). A general fix would be a change to LAGP, Razvan's decision.
- infantry: blotchy grey patches on the upper tabard and sleeves in small renders; close-up still to do.
- infantry: the first version (seed 1101) shattered into 447 pieces while LAGP's check said ok; kept in `models/infantry_rejected_shattered`. The check not failing on piece count is worth raising for LAGP.
- oak: the kept version has 15 pieces, which is fine for a tree. Razvan liked it. Retry kept in `models/oak/stopped_retry_seed3010`.
- skies: generated panoramas have a soft line down the middle, hidden by a cross-fade in `BBSky.shader`; check it holds up in play.
- bush: now instanced leaf cards (`Dressing.BuildBushes`), no 3D model, at Razvan's request. They cast no shadow yet; judge the look in the game. The 3D version is kept in `models/_set_aside/bush_314_pieces`.

## Seen in the first builds
- menu video: not generated yet; the menu shows the still picture.
- audio: nobody has listened to it yet except Razvan's own playthrough.
- balance: at 200 a side test battles took 94 to 157 s: quick. Kills vary with the ground: seed 9001 (more hills) infantry 146, archers 92, cavalry 69; seed 555 (open) infantry 90, archers 199, cavalry 42. Archers still dominate on open ground.
- performance: the one slow frame is at load (about 350 ms); at 400 men the battle averages over 160 fps uncapped on the RTX 4070 laptop.
- command mode: blue soldiers have no formation memory: after a fight a group does not re-form unless ordered again. Groups (Ctrl+number) and a minimap click to move the view are not in yet.
- grass: until the instancing fix on 2026-11-05 no grass cards, bushes or blood splats were drawn in any build (the instanced variants were stripped). They are drawn now; grass is drawn in every view; from above the root darkening is lifted and it fades by distance across the ground, so it does not read as dark stars or end in a ring (2026-11-06).
- trees: oaks and pines are card trees now (2026-11-06); the oak and pine models are still imported but no longer placed. The pine card photo is dark (about 40/255), so its tint is raised 1.6x in `CardTrees.cs`; a brighter regeneration would be cleaner.
- balance: blue bias fixed on 2026-11-05 (units act in a random order each frame; red no longer hunts the player soldier when the AI runs it). 14 battles with `balance_runs.py`: blue 9, red 5; before, blue won 6 of 6. Results swing hard with the ground (survivors 14 to 130).
- balance: over those 14 battles archers average about 150 kills, infantry 115, cavalry 45 (60 riders a side): archers still the strongest, cavalry the weakest.

## 2026-11-06
- horse clips: now Quaternius' hand-animated horse (CC0, poly.pizza/m/qvTrSG9pZF) retargeted with `retarget_quad.py` (`tools/horse_clips.py`). It has no trot: Trot and Run both use its Gallop. The procedural rig is kept as `models/horse/final/horse_procedural.fbx`.
- cavalry: redone again. The dark steel knight textured as black lumps; the new one (seed 1322, concept 2, shape seed 1331) is bright mail and plate under a white surcoat. The first shape of it (seed 1322) shattered the surcoat (310 pieces). Set aside in `models/_set_aside/cavalry_darksteel` and `cavalry_shattered_1322`.
- oak, pine, tent: generated leaning (the concept camera's pitch). Turned upright by eye with `prop_upright.py --fix` (oak 18, pine 7, tent 20 degrees about x); the generated ones kept in `final/as_generated/`. The tent's centre pole reaches 0.35 m below its walls, so tents are sunk that much.
- oak LOD: the 3000-triangle far mesh lost the trunk and crown shape; oaks now use the full mesh at every distance.
- archers: the player's archer nocks and draws normal arrows twice as fast (0.16 s, 0.52 s) and aiming slows time to half; computer archers are unchanged.
- cavalry tint: the new knight's polished steel is as pale as his surcoat, so it took the team tint in spots. His material tints only warm whites (`_TintWarm` in BBLit); steel is neutral or cool grey.
- balance (8 battles, `balance_runs.py`, seeds 11 to 88): blue 5, red 3. Kills per battle: infantry about 173, archers 81, cavalry 53. Infantry is now the strongest type.
- sounds: rain loop and thunder are new (`gen_audio.py --only rain,thunder`); nobody has listened to them yet.
- horse (2026-11-06, again): regenerated with a long tail (seed 1411, concept 3). LAGP's rig weighted the lower tail to the hind legs; `horse_tail_rig.py` gives it a bone chain of its own and cuts the faces joining it to the legs. A few dark bits by the hocks still follow the legs. The game's simulated hair tail (HorseTail) is gone. The docked horse is in `models/_set_aside/horse_docked_tail`.
- horse clips: the Quaternius retarget looked worse than LAGP's own procedural clips, so those are back. A better source is the Truebones Zoo pack (29 horse clips, free from Gumroad, needs a person to check out at $0); retargeting it needs a Truebones profile in LAGP's `retarget_quad.py` (a pipeline change, Razvan's decision).
- melee: the overhead cut is gone (it looked bad). Computer soldiers swing, block and bash at half the player's speed.
- horse seat: the rider sat behind the saddle (seat guessed from the back's outline). `horse_tail_rig.py` now also adds a `seat` bone over the middle of the saddle, found by its white blanket; the rider's hips go 13 cm above it.
- archer: stands side-on while shooting; the string elbow starts out and forward and swings back during the draw; both forearms take part of the hand's twist (`Limb.ShareTwist`) so the wrists are not wrung.
- arm IK (all units): the upper arm (and thigh) was never rolled about its own length, so a raised elbow bent with its hinge twisted to the wrong side. `Limb.Solve` now rolls it so the joint folds towards the hand.
- archer: stance eased to half side-on (feet 30 degrees, spine 20 more, head turned back); fully side-on put the right shoulder behind the head, and the reach to the string went through it. Elbow and quiver reach are worked out from his own turned body; the hand goes to the real quiver.
- horse seat: centred across the horse's body (its spine bones run 7 cm off its middle) and between the saddle's front and back peaks.
- ground (2026-11-06): 7 more tiles per ground type (42, `gen_textures.py --variants 7`), as texture arrays in BBTerrain. Two bugs on the way: the arrays were made linear (washed out white), and sampling inside per-pixel branches lost the mip level (pale, cross-hatched); fixed with sRGB arrays and SampleGrad. Each tile's baked light-dark across it showed as bands every 3.6 m: the import flattens it, and the shader gives every few metres its own tile and random offset.
- archer: searched for ready-made bow animations (2026-11-06). No CC0 pack has them (Quaternius UAL1 and UAL2 have none). Candidates: Mixamo's free archery set (needs Razvan's Adobe login to download), GameDevHero's Archery Combat Animation (paid/bundled), Archer AnimSet Pro (paid, Unreal).
- Mixamo clips (2026-11-06, downloaded by Razvan into C:/AI/tools/anim_library/mixamo_*): archer BowDraw / BowAim / BowRelease replace the hand-made arm code; infantry SSIdle, SSRun, SSSlash1 (diagonal) and SSSlash3 (wide horizontal) as the two cuts, SSBlockIdle held while blocking, SSBlock on a caught blow, SSImpact, SSDeath / SSDeath2; cavalry riders use the two cuts on the upper body. Not used: the spinning, jumping and kneeling attacks (slash 2, 4, 5, attack 1, 2), kick, crouch, casting. The shield bash stays hand-made (no clip for it).
- build in an open Editor: `build_game.py` now asks the Editor to refresh and recompile first (it only recompiles on focus, and building from stale scripts fails).
- facing (2026-11-06): re-exported soldier models open in the first frame of a clip (the side-on bow aim), and the game measured each soldier's facing and arm T-pose from that: everyone stood a quarter turn left. Units now put every bone in the mesh's bind pose before measuring (`Unit.ToBindPose`).
- `mixamo_clips.py --only` writes a model with only those clips: run it without --only before building (README's rebuild order does).
- dead_tree: leaned about 13 degrees (also the frame of every oak now); stood up by `upright: [9, 0]`, set from the measured trunk lean, since 12 by eye overshot the other way (2026-11-06).
