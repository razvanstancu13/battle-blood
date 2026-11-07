using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

namespace BattleBlood
{
    /// <summary>
    /// Automated playthrough, only active with -bbtest (tools/verify_game.py passes it). Clicks Start on the menu, lets the player's unit be run by the AI,
    /// checks the rules of the brief, photographs each unit type up close, forces a player death to test the body switch, and finishes the battle.
    /// Writes "[TEST] ..." lines to the player log, ending with "[TEST] RESULT OK" or "[TEST] RESULT FAIL ...".
    /// </summary>
    [DefaultExecutionOrder(10000)]      // after the battle's late pose: what it checks there is what is drawn
    public class AutoTest : MonoBehaviour
    {
        bool watchShields; float worstShield = 1f; int shieldSamples; string worstShieldAt = "";
        int horseSamples, horseStanding; double spreadSum; int spreadN; float nextSpreadLog;

        // Every living shield, every frame of the battle, as drawn: its top edge must point up, whatever the clip.
        void LateUpdate()
        {
            if (!watchShields || Battle.I == null) return;
            if (Battle.I.Now == Battle.Phase.Fight && Time.unscaledTime > nextSpreadLog)
            {
                nextSpreadLog = Time.unscaledTime + 15f;
                Vector3 c = Battle.I.ClashCentre;
                var sb = new System.Text.StringBuilder($"spread at {Battle.I.BattleTime:F0}s: clash at ({c.x:F0}, {c.z:F0})");
                foreach (int team in new[] { Unit.Blue, Unit.Red })
                    foreach (UnitType t in System.Enum.GetValues(typeof(UnitType)))
                    {
                        float sum = 0f; int n = 0, far = 0;
                        foreach (var u in Battle.I.Army(team)) if (u.Alive && u.Type == t) { float dd = Mathx.Flat(u.Pos - c).magnitude; sum += dd; n++; if (dd > 60f) far++; }
                        if (n > 0) sb.Append($"; {(team == Unit.Blue ? "blue" : "red")} {t} {n} avg {sum / n:F0} m, {far} beyond 60");
                    }
                Log(sb.ToString());
            }
            if (Battle.I.Now == Battle.Phase.Fight && Time.frameCount % 10 == 0)
            {
                Vector3 c = Battle.I.ClashCentre;
                foreach (var u in Battle.I.All)
                    if (u.Alive && u.Type != UnitType.Archer) { spreadSum += Mathx.Flat(u.Pos - c).magnitude; spreadN++; }
            }
            foreach (var u in Battle.I.All)
            {
                // Horsemen left to themselves keep moving once the fight is on.
                if (u.Alive && u.Type == UnitType.Cavalry && !u.IsPlayer && u.Order == Unit.OrderKind.None && Battle.I.Now == Battle.Phase.Fight)
                {
                    horseSamples++;
                    if (u.Speed < 1f) horseStanding++;
                }
                if (!u.Alive || u.ShieldHolder == null) continue;
                float upright = Vector3.Dot(u.ShieldHolder.up, Vector3.up);
                shieldSamples++;
                if (upright < worstShield) { worstShield = upright; worstShieldAt = $"{u.Type} {u.Action} speed {u.Speed:F1} near {u.NearCamera}"; }
            }
        }

        string shotDir;
        float duration = 180f;
        int shots, errors, exceptions, switches;
        float fpsSum, fpsMin = 9999f, msSum; int fpsFrames;
        bool sampling;
        float maxBlood, bloodSampleAt;
        bool quick;       // balance runs: the battle only, no player tests, no pictures
        bool practice;    // -bbpractice: the Practice bout, close-up pictures of each soldier's moves
        int spikes;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Start0()
        {
            var args = System.Environment.GetCommandLineArgs();
            if (!args.Contains("-bbtest")) return;
            var go = new GameObject("AutoTest");
            DontDestroyOnLoad(go);
            var t = go.AddComponent<AutoTest>();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-bbshots") t.shotDir = args[i + 1];
                if (args[i] == "-bbseconds") float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out t.duration);
                if (args[i] == "-bbseed") int.TryParse(args[i + 1], out Boot.ForcedSeed);
                if (args[i] == "-bbweather") int.TryParse(args[i + 1], out Battle.ForcedWeather);
                if (args[i] == "-bbspeed") float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out Battle.SpeedMul);
            }
            t.quick = args.Contains("-bbquick");
            Art.OneTileOnly = args.Contains("-bbonetile");
            t.practice = args.Contains("-bbpractice");
            if (string.IsNullOrEmpty(t.shotDir)) t.shotDir = Application.persistentDataPath;
            Directory.CreateDirectory(t.shotDir);
        }

        static void Log(string s) => Debug.Log("[TEST] " + s);
        void Check(bool ok, string rule)
        {
            Log((ok ? "pass  " : "ERROR ") + rule);
            if (!ok) errors++;
        }

        IEnumerator Shot(string name)
        {
            if (quick) yield break;
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(shotDir, $"{shots++:00}_{name}.png"));
            yield return null;
        }

        IEnumerator WaitReal(float s) { float t = 0; while (t < s) { t += Time.unscaledDeltaTime; yield return null; } }

        void Update()
        {
            if (!sampling) return;
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f) return;
            float fps = 1f / dt;
            fpsSum += fps; fpsFrames++;
            if (fps < fpsMin) fpsMin = fps;
            if (dt > 0.1f && Battle.I != null && spikes < 12) { spikes++; Log($"slow frame {dt * 1000f:F0} ms at battle time {Battle.I.BattleTime:F1}s, phase {Battle.I.Now}, alive {Battle.I.Alive(0) + Battle.I.Alive(1)}"); }
            if (Battle.I != null) msSum += Battle.I.FrameMs;
            if (Battle.I != null && Time.realtimeSinceStartup > bloodSampleAt) { bloodSampleAt = Time.realtimeSinceStartup + 2f; maxBlood = Mathf.Max(maxBlood, Battle.I.Field.BloodShare()); }
        }

        IEnumerator Start()
        {
            Application.runInBackground = true;
            Application.logMessageReceived += (c, st, type) =>
            {
                if (type == LogType.Exception || (type == LogType.Error && !c.Contains("[ANIM]"))) { exceptions++; Log("LOG " + c); }
            };
            Log($"start {Screen.width}x{Screen.height} {SystemInfo.graphicsDeviceName}");
            if (practice) { yield return PracticeShots(); yield break; }
            yield return WaitReal(3f);
            yield return Shot("menu");
            var play = FindObjectsByType<Button>(FindObjectsInactive.Exclude).FirstOrDefault(x => x.name == "Play");
            Check(play != null, "the main menu has a start button");
            if (play == null) { Finish(); yield break; }
            Battle.AutoPlay = this;
            float loadStart = Time.realtimeSinceStartup;
            play.onClick.Invoke();
            yield return Shot("loading");
            Check(!play.gameObject.activeInHierarchy, "the menu's buttons are hidden while the battle loads");
            // A failed build must end the test, not hang it.
            while (Battle.I == null || !Battle.I.Ready)
            {
                if (exceptions > 0 || Time.realtimeSinceStartup - loadStart > 180f)
                {
                    Check(false, $"the battle finished loading (gave up after {Time.realtimeSinceStartup - loadStart:F0}s, {exceptions} errors)");
                    Finish();
                    yield break;
                }
                yield return null;
            }
            var b = Battle.I;
            Log($"battle built in {Time.realtimeSinceStartup - loadStart:F1}s, seed {b.Seed}, mood {b.Mood.Name}, weather {Weather.Kind}, swamp {b.Field.SwampShare:P0}, hills {b.Field.HillShare:P0}");

            // The armies as specified.
            foreach (int team in new[] { Unit.Blue, Unit.Red })
            {
                string n = team == Unit.Blue ? "blue" : "red";
                Check(b.Army(team).Count == Battle.ArmySize, $"{n} has {Battle.ArmySize} troops");
                Check(b.Alive(team, UnitType.Cavalry) == Battle.CavalryPerArmy, $"{n} has {Battle.CavalryPerArmy} cavalry");
                Check(b.Alive(team, UnitType.Archer) == Battle.ArchersPerArmy, $"{n} has {Battle.ArchersPerArmy} archers");
                Check(b.Alive(team, UnitType.Infantry) == Battle.InfantryPerArmy, $"{n} has {Battle.InfantryPerArmy} infantry");
            }
            Check(b.Player != null && b.Player.Team == Unit.Blue, "the player controls a blue unit");
            Check(b.All.Count(u => u.IsPlayer) == 1, "exactly one unit is the player");
            float minY = b.All.Min(u => u.Pos.y), maxY = b.All.Max(u => u.Pos.y);
            Check(b.Field.HillShare > 0f || maxY - minY > 0.5f, $"the ground is not flat (deployment spans {maxY - minY:F1} m)");
            sampling = true;
            QualitySettings.vSyncCount = 0;      // measure what the game can do, not the screen's cap

            yield return Shot("opening");
            yield return TestDeployment(b);
            yield return Shot("deployed");
            if (!quick)
            {
                yield return TestControls(b);
                yield return TestCommand(b);
            }

            // Follow each type up close, for the pictures.
            b.PlayerSwitched += (_, __) => switches++;
            float start = Time.realtimeSinceStartup;
            bool forcedDeath = false, firstMeleeShot = false;
            int nextShot = 0;
            float nextTour = 0f;
            UnitType tour = UnitType.Infantry;
            while (b.Now != Battle.Phase.Over && Time.realtimeSinceStartup - start < duration)
            {
                float t = Time.realtimeSinceStartup - start;
                watchShields = true;
                if (t > nextShot) { nextShot += 12; yield return Shot($"t{t:000}_{b.Player.S.Label}"); }
                if (!firstMeleeShot && b.Now == Battle.Phase.Fight) { firstMeleeShot = true; yield return Shot("melee_joined"); }

                // Show each kind of soldier in action: the camera rides a living one of each in turn.
                if (!quick && t > nextTour && b.Alive(Unit.Blue, tour) > 0 && !forcedDeath)
                {
                    nextTour = t + 18f;
                    var u = b.Army(Unit.Blue).FirstOrDefault(x => x.Alive && x.Type == tour);
                    if (u != null) { b.PlayerCam.SwitchTo(u); Log($"following a {u.S.Label}"); }
                    tour = (UnitType)(((int)tour + 1) % 3);
                }

                // The body switch, once the fight is on: kill the player's unit and see who he becomes.
                if (!quick && !forcedDeath && t > 60f && b.Now == Battle.Phase.Fight && b.Alive(Unit.Blue) > 2)
                {
                    forcedDeath = true;
                    var dead = b.Player;
                    var expected = b.ChooseNextBody(dead);
                    bool fullAvailable = b.Army(Unit.Blue).Any(u => u != dead && u.Alive && u.Hp >= u.S.MaxHp - 0.5f && !b.UnderAttack(u));
                    Log($"forcing the player's death, {dead.Root.name}; a full-health unattacked unit available: {fullAvailable}");
                    dead.TakeDamage(9999f, null, dead.Forward, HitKind.Sword, dead.ChestPos);
                    yield return Shot("player_falls");
                    yield return WaitReal(2.2f);
                    var now = b.Player;
                    Check(now != null && now != dead, "control left the dead unit");
                    Check(now != null && now.Alive && now.Team == Unit.Blue, "control jumped to a living blue unit");
                    Check(b.All.Count(u => u.IsPlayer) == 1, "still exactly one player unit");
                    if (fullAvailable && now != null) Check(now.Hp >= now.S.MaxHp * 0.6f, $"the new body is healthy ({now.Hp:F0}/{now.S.MaxHp:F0})");
                    yield return Shot("new_body");
                }
                yield return null;
            }

            Log($"after {Time.realtimeSinceStartup - start:F0}s: blue {b.Alive(0)}, red {b.Alive(1)}, arrows shot {Unit.ShotsFired}, fire hits {b.Arrows.FireHits}, ignited {Unit.BurnStat}, severed {Gore.SeveredCount}, decals {Gore.DecalsPlaced}, blood on {b.Field.BloodShare():P1} of the field");
            Log($"balance after {b.BattleTime:F0}s of battle, ground fires {b.Fires.Started}, props lit {b.Fires.PropsLit}:");
            foreach (UnitType t in System.Enum.GetValues(typeof(UnitType))) Log("balance " + Tally.Line(t));
            Check(b.Fires.Started > 0, "lit arrows started fires on the ground");
            watchShields = false;
            Log($"archers: {Unit.AiShotsUphill} of {Unit.AiShots} computer arrows shot uphill (target over 1 m higher), {(Unit.AiShots > 0 ? Unit.AiShotsUphill / (float)Unit.AiShots : 0f):P0}");
            Log($"spread: foot and horse stand on average {(spreadN > 0 ? spreadSum / spreadN : 0):F1} m from the clash centre");
            float stillShare = horseSamples > 0 ? horseStanding / (float)horseSamples : 1f;
            Check(horseSamples > 1000 && stillShare < 0.05f, $"cavalry keep moving ({stillShare:P1} of {horseSamples} samples standing)");
            Check(shieldSamples > 1000 && worstShield > 0.99f, $"shields stay upright in every clip ({shieldSamples} samples, worst tilt {Mathf.Acos(Mathf.Clamp(worstShield, -1f, 1f)) * Mathf.Rad2Deg:F1} deg, {worstShieldAt})");
            yield return Shot("before_end");
            int settled = 0, standing = 0;
            foreach (var u in b.All)
                if (!u.Alive && u.DeadTime > 4.5f)
                {
                    settled++;
                    float headUp = u.HeadPos.y - b.Field.Height(u.HeadPos.x, u.HeadPos.z);      // above the ground under the head: a body lying down a slope is still lying
                    if (headUp > 0.95f) { standing++; Log($"still upright: {u.Root.name}, head {headUp:F2} m above the ground, dead {u.DeadTime:F0}s, camera {u.CamDist:F0} m"); }
                }
            Log($"corpses whose death clip had to be put on its last frame when frozen: {Unit.RescuedCorpses}");
            Check(settled > 0 && standing == 0, $"corpses lie down ({standing} of {settled} long-dead still upright)");
            Check(Unit.ShotsFired > 20, "archers shot arrows");
            Check(Unit.BurnStat > 0, "lit arrows set men on fire");
            Check(Gore.SeveredCount > 0, "blows severed limbs");
            Check(Gore.DecalsPlaced > 30, "blood reached the ground");
            Check(maxBlood > 0.001f, $"the blood map soaked up blood (at most {maxBlood:P1} of the field at once)");
            Check(b.Field.BloodShare() < maxBlood * 0.8f || maxBlood <= 0.001f, $"blood dries off the ground after a while ({b.Field.BloodShare():P1} left of a peak of {maxBlood:P1})");
            Check(b.All.Where(u => u.DeadTime > 20f).All(u => u.BloodLevel < 0.05f), "blood on long-dead bodies has faded");
            Check(switches >= 1 || !forcedDeath, "the body switch event fired");
            Log($"outcome before any ending by the test: blue {b.Alive(Unit.Blue)}, red {b.Alive(Unit.Red)}, battle time {b.BattleTime:F0}s, over {b.Now == Battle.Phase.Over}");

            // The end of the battle: if time ran out, finish the losing side off and check the verdict.
            if (b.Now != Battle.Phase.Over)
            {
                int side = b.Alive(Unit.Red) <= b.Alive(Unit.Blue) ? Unit.Red : Unit.Blue;
                Log($"time is up: ending by killing the rest of {(side == Unit.Red ? "red" : "blue")}");
                foreach (var u in b.Army(side).ToArray()) if (u.Alive) u.TakeDamage(9999f, null, Vector3.forward, HitKind.Sword, u.ChestPos);
            }
            yield return WaitReal(1.5f);
            Check(b.Now == Battle.Phase.Over, "the battle ends when one army is gone");
            Check(b.Winner >= 0 && b.Alive(b.Winner) > 0 && b.Alive(1 - b.Winner) == 0, "the winner is the army still standing");
            yield return WaitReal(4.5f);
            yield return Shot("result");
            var again = FindObjectsByType<Button>(FindObjectsInactive.Exclude).FirstOrDefault(x => x.name == "Again");
            Check(again != null, "the result screen offers another battle");
            sampling = false;
            float avg = fpsFrames > 0 ? fpsSum / fpsFrames : 0f;
            Log($"performance: average {avg:F0} fps, worst frame {fpsMin:F0} fps, battle update {(fpsFrames > 0 ? msSum / fpsFrames : 0f):F2} ms");
            Finish();
        }

        /// <summary>Real input events, not the AI: W must walk the player's unit and the left button must start his attack.</summary>
        IEnumerator TestControls(Battle b)
        {
            var kb = Keyboard.current; var mouse = Mouse.current;
            if (kb == null || mouse == null) { Check(false, "a keyboard and mouse exist for the controls test"); yield break; }
            var p = b.Player;
            Battle.AutoPlay = null;
            Vector3 from = p.Pos;
            bool acted = false;
            float t = 0f;
            while (t < 3f)
            {
                InputSystem.QueueStateEvent(kb, new KeyboardState(Key.W));
                InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Left, Time.frameCount % 24 < 12));
                if (p.Action == Act.Swing || p.Action == Act.Nock || p.Action == Act.Draw) acted = true;
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            InputSystem.QueueStateEvent(mouse, new MouseState());
            yield return null;
            float moved = Mathx.Flat(p.Pos - from).magnitude;
            Check(moved > 2f, $"W moves the player's {p.S.Label} ({moved:F1} m in 3 s)");
            Check(acted, $"the left mouse button makes the {p.S.Label} attack");
            // V: through his own eyes, and back.
            for (int tries = 0; tries < 3 && !b.PlayerCam.FirstPerson; tries++)
            {
                Press(Key.V); yield return null;
                InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState()); yield return null; yield return null;
            }
            Check(b.PlayerCam.FirstPerson, "V switches to first person");
            yield return WaitReal(0.6f);
            float eyeGap = p.HeadBone != null ? Vector3.Distance(b.PlayerCam.transform.position, p.HeadBone.position) : 0f;
            Check(eyeGap < 0.5f, $"the first person view is at his head ({eyeGap:F2} m away)");
            yield return Shot("first_person");
            for (int tries = 0; tries < 3 && b.PlayerCam.FirstPerson; tries++)
            {
                Press(Key.V); yield return null;
                InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState()); yield return null; yield return null;
            }
            Check(!b.PlayerCam.FirstPerson, "V switches back to third person");
            Battle.AutoPlay = this;
        }

        /// <summary>The battle opens in command mode with both armies waiting; orders before the start stay behind the line; Start sounds the horn.</summary>
        IEnumerator TestDeployment(Battle b)
        {
            Check(Rts.Active, "the battle opens in command mode");
            Check(b.Now == Battle.Phase.Deploy, "the battle waits for Start");
            if (quick)
            {
                // Balance runs: no orders of ours, so only the red commander's plan differs; let him set out, then start.
                yield return WaitReal(8f);
                b.StartFight();
                if (Rts.Active) Rts.I.Toggle();
                yield break;
            }
            var before = b.Army(Unit.Blue).Select(u => u.Pos).ToList();
            var redBefore = b.Army(Unit.Red).Select(u => u.Pos).ToList();
            yield return WaitReal(3f);
            float still = b.Army(Unit.Blue).Select((u, i) => Mathx.Flat(u.Pos - before[i]).magnitude).Max();
            Check(still < 0.5f, $"blue troops stand still awaiting orders (furthest moved {still:F2} m)");
            // Send the horse far forward: their places must stay on our side of the line.
            Rts.I.ClearSelection();
            Rts.I.SelectType(UnitType.Cavalry, false);
            Rts.I.Command(Unit.OrderKind.Move, b.Field.Ground(new Vector3(-30f, 0, 10f)), null);
            var horse = b.Army(Unit.Blue).Where(u => u.Type == UnitType.Cavalry).ToList();
            Check(horse.All(u => u.Order == Unit.OrderKind.Move && u.OrderPos.z <= -Battle.DeployLine + 0.01f), "orders before the start stay behind the deployment line");
            Rts.I.ClearSelection();
            Rts.I.SelectType(UnitType.Archer, false);
            Rts.I.Command(Unit.OrderKind.Attack, Vector3.zero, b.Army(Unit.Red)[0]);
            Check(b.Army(Unit.Blue).Where(u => u.Type == UnitType.Archer).All(u => u.Order != Unit.OrderKind.Attack), "no attack orders before the start");
            Rts.I.ClearSelection();
            yield return WaitReal(6f);
            float moved = horse.Average(u => Mathx.Flat(u.Pos - before[b.Army(Unit.Blue).IndexOf(u)]).magnitude);
            Check(moved > 4f, $"the horse moves to its new place before the start ({moved:F1} m on average)");
            float redMoved = b.Army(Unit.Red).Select((u, i) => Mathx.Flat(u.Pos - redBefore[i]).magnitude).Max();
            Check(b.RedPlan >= 0 && redMoved > 3f, $"the red commander sets out his army (plan {b.RedPlan}, furthest moved {redMoved:F1} m)");
            Check(b.All.All(u => u.Team == Unit.Blue ? u.Pos.z <= -Battle.DeployLine + 1.5f : u.Pos.z >= Battle.DeployLine - 1.5f), "both armies keep to their own side before the start");
            Check(b.Now == Battle.Phase.Deploy, "nothing starts without the Start button");
            yield return Shot("deploying");
            var start = FindObjectsByType<Button>(FindObjectsInactive.Exclude).FirstOrDefault(x => x.name == "Start");
            Check(start != null, "command mode shows a Start button during the deployment");
            if (start != null) start.onClick.Invoke(); else b.StartFight();
            yield return null;
            Check(b.Now == Battle.Phase.March, "Start begins the battle");
            Rts.I.Toggle();      // the rest of the test fights from the ground
            yield return null;
            Check(!Rts.Active, "Tab (toggle) leaves command mode");
        }

        /// <summary>
        /// The Practice bout from the menu, then for each soldier type: he fights a dummy that stands still (and one that blocks), photographed
        /// from the front, the side and behind every few tenths of a second, at the speed of the game slowed down. Checks nobody is hurt.
        /// </summary>
        IEnumerator PracticeShots()
        {
            yield return WaitReal(2f);
            var practiceButton = FindObjectsByType<Button>(FindObjectsInactive.Exclude).FirstOrDefault(x => x.name == "Practice");
            Check(practiceButton != null, "the main menu has a Practice button");
            if (practiceButton == null) { Finish(); yield break; }
            practiceButton.onClick.Invoke();
            yield return WaitReal(0.5f);
            yield return Shot("practice_menu");
            var begin = FindObjectsByType<Button>(FindObjectsInactive.Exclude).FirstOrDefault(x => x.name == "Begin");
            Check(begin != null, "the practice menu has a Begin button");
            if (begin == null) { Finish(); yield break; }
            begin.onClick.Invoke();
            float t0 = Time.realtimeSinceStartup;
            while (Battle.I == null || !Battle.I.Ready) { if (Time.realtimeSinceStartup - t0 > 120f || exceptions > 0) { Check(false, "practice loads"); Finish(); yield break; } yield return null; }
            var b = Battle.I;
            Check(Battle.PracticeMode != null && b.All.Count == 2, $"practice is one soldier against one ({b.All.Count})");
            Battle.AutoPlay = this;           // the player's soldier fights by himself, for the pictures
            var cam = b.PlayerCam.Cam;
            b.PlayerCam.enabled = false;
            Battle.SpeedMul = 0.5f;
            foreach (var mine in new[] { UnitType.Infantry, UnitType.Archer, UnitType.Cavalry })
                foreach (int opp in new[] { 1, 2 })
                {
                    if (opp == 2 && mine != UnitType.Infantry) continue;
                    Battle.PracticeMode.Mine = mine; Battle.PracticeMode.Theirs = UnitType.Infantry; Battle.PracticeMode.Opponent = opp;
                    b.SpawnPractice();
                    foreach (var u in b.Army(Unit.Red)) u.Passive = opp;
                    var me = b.Player;
                    float hp0 = b.Army(Unit.Red)[0].Hp;
                    float t = 0f; int n = 0;
                    while (t < (mine == UnitType.Cavalry ? 13f : 7f))
                    {
                        // Front, side, behind, turning with him; further back for a horseman.
                        float dist = mine == UnitType.Cavalry ? 3.0f : 2.6f, up = mine == UnitType.Cavalry ? 2.45f : 1.35f;
                        float ang = new[] { 0f, 90f, 180f, 270f }[n % 4];
                        Vector3 look = me.Pos + Vector3.up * up;
                        Vector3 dir = Quaternion.Euler(0, me.Yaw + ang, 0) * Vector3.forward;
                        cam.transform.SetPositionAndRotation(look + dir * dist + Vector3.up * 0.3f, Quaternion.LookRotation(-dir - Vector3.up * 0.1f));
                        cam.fieldOfView = 50f;
                        if (t > 1.2f) yield return Shot($"practice_{mine}_{(opp == 2 ? "block" : "dummy")}_{n:00}");
                        if (n % 4 == 0) Log($"practice {mine} t {t:F1}: pos {me.Pos}, speed {me.Speed:F1}, wish {me.Wish}, target {(me.Target != null ? me.Target.Id : -1)} at {(me.Target != null ? Vector3.Distance(me.Pos, me.Target.Pos) : 0f):F1} m, action {me.Action}");
                        n++;
                        float w = 0f; while (w < 0.32f) { w += Time.unscaledDeltaTime; t += Time.unscaledDeltaTime; yield return null; }
                    }
                    var foe = b.Army(Unit.Red)[0];
                    Check(foe.Alive && foe.Hp >= hp0, $"practice {mine}: no damage taken by the opponent ({foe.Hp:F0} of {hp0:F0})");
                    Log($"practice {mine} against a {(opp == 2 ? "blocking" : "still")} dummy: swings {Tally.Attempts[(int)mine]}");
                }
            // The trees close up: the nearest pines and oak, from the side at eye height and from a little above.
            {
                var me = b.Player;
                var trees = FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                    .Where(x => x.name == "pine" || x.name == "oak")
                    .OrderBy(x => (x.position - me.Pos).sqrMagnitude).ToList();
                var picks = trees.Where(x => x.name == "pine").Take(2).Concat(trees.Where(x => x.name == "oak").Take(1)).ToList();
                Check(picks.Count == 3, "the field has pines and oaks to look at");
                int k = 0;
                foreach (var tr in picks)
                {
                    float h = tr.name == "pine" ? 13f : 10f;
                    Vector3 mid = tr.position + Vector3.up * h * 0.5f;
                    for (int v = 0; v < 2; v++)
                    {
                        Vector3 dir = Quaternion.Euler(v == 0 ? 0f : 25f, 40f + k * 70f, 0) * Vector3.back;
                        cam.transform.SetPositionAndRotation(mid + dir * h * 1.6f, Quaternion.LookRotation(-dir));
                        cam.fieldOfView = 50f;
                        yield return null;
                        yield return Shot($"practice_tree_{tr.name}_{k:00}_{(v == 0 ? "side" : "above")}");
                    }
                    k++;
                }
            }

            // The horse whole, from the side and behind: its gait and its tail.
            {
                Battle.PracticeMode.Mine = UnitType.Cavalry; b.SpawnPractice();
                foreach (var u in b.Army(Unit.Red)) u.Passive = 1;
                var me = b.Player;
                Battle.SpeedMul = 0.5f;
                for (int k = 0; k < 10; k++)
                {
                    float w = 0f; while (w < 0.45f) { w += Time.unscaledDeltaTime; yield return null; }
                    Vector3 dir = Quaternion.Euler(0, me.Yaw + (k % 2 == 0 ? 100f : 160f), 0) * Vector3.forward;
                    Vector3 look = me.Pos + Vector3.up * 1.5f;
                    cam.transform.SetPositionAndRotation(look + dir * 7.5f + Vector3.up * 0.4f, Quaternion.LookRotation(-dir));
                    cam.fieldOfView = 50f;
                    yield return Shot($"practice_horse_{k:00}");
                }
            }

            // The archer from the player's own view, over his right shoulder: a picture at every phase of the shot.
            Battle.PracticeMode.Mine = UnitType.Archer; Battle.PracticeMode.Opponent = 1;
            b.SpawnPractice();
            foreach (var u in b.Army(Unit.Red)) u.Passive = 1;
            {
                var me = b.Player;
                Battle.SpeedMul = 0.25f;
                Act last = Act.Dead; int k = 0; float t = 0f, lastShot = 0f;
                while (t < 14f && k < 42)
                {
                    Vector3 f = Mathx.Flat(me.Aim).sqrMagnitude > 0.01f ? Mathx.Flat(me.Aim).normalized : me.Forward;
                    Vector3 r = Vector3.Cross(Vector3.up, f);
                    Vector3 focus = me.Pos + Vector3.up * 1.4f;
                    // Every phase from three sides in turn: over his right shoulder (the player's view), close on the bow side, close on the string side.
                    int view = k % 3;
                    Vector3 cpos = view == 0 ? focus + r * 0.62f - f * 3.1f + Vector3.up * 0.15f
                                 : view == 1 ? focus + (f * 0.9f - r * 1.2f).normalized * 1.7f + Vector3.up * 0.1f
                                 : focus + (r * 1.3f + f * 0.4f).normalized * 1.6f + Vector3.up * 0.15f;
                    cam.transform.SetPositionAndRotation(cpos, Quaternion.LookRotation(view == 0 ? f : focus - cpos));
                    cam.fieldOfView = view == 0 ? 55f : 45f;
                    bool phase = me.Action != last || (me.Action != Act.None && t - lastShot > 0.25f);
                    if (phase && t > 0.8f)
                    {
                        yield return Shot($"practice_archer_view_{k:00}_{me.Action}_{me.DrawPower:F1}");
                        k++; lastShot = t;
                    }
                    last = me.Action;
                    t += Time.unscaledDeltaTime;
                    yield return null;
                }
            }
            Battle.SpeedMul = 1f;
            Finish();
        }

        static void Press(Key key)
        {
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(key));
        }

        static void Pointer(Vector2 pos, bool left, bool right)
        {
            var s = new MouseState { position = pos };
            if (left) s = s.WithButton(MouseButton.Left);
            if (right) s = s.WithButton(MouseButton.Right);
            InputSystem.QueueStateEvent(UnityEngine.InputSystem.Mouse.current, s);
        }

        /// <summary>Tab into command mode, drag a box round some infantry, send them somewhere, then at an enemy; Tab back.</summary>
        IEnumerator TestCommand(Battle b)
        {
            Battle.AutoPlay = null;
            // A queued key can land in a frame the game skips past (a hit stop, a slow-motion kill): press again, up to three times.
            for (int tries = 0; tries < 3 && !Rts.Active; tries++)
            {
                Press(Key.Tab); yield return null;
                InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState()); yield return null; yield return null;
            }
            Check(Rts.Active, "Tab switches to command mode");
            if (!Rts.Active) { Battle.AutoPlay = this; yield break; }
            yield return WaitReal(1f);
            var cam = Camera.main;
            // A box round the blue soldiers that are on screen nearest the middle.
            var picks = b.Army(Unit.Blue).Where(u => u.Alive && !u.IsPlayer).Select(u => (u, s: cam.WorldToScreenPoint(u.Pos + Vector3.up)))
                .Where(x => x.s.z > 0 && x.s.x > 40 && x.s.x < Screen.width - 40 && x.s.y > 140 && x.s.y < Screen.height - 40)
                .OrderBy(x => ((Vector2)x.s - new Vector2(Screen.width, Screen.height) * 0.5f).sqrMagnitude).Take(8).ToList();
            Check(picks.Count > 0, $"blue soldiers are on screen in command mode ({picks.Count})");
            if (picks.Count == 0) { Press(Key.Tab); yield return null; InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState()); Battle.AutoPlay = this; yield break; }
            Vector2 lo = new(picks.Min(x => x.s.x) - 12, picks.Min(x => x.s.y) - 12), hi = new(picks.Max(x => x.s.x) + 12, picks.Max(x => x.s.y) + 12);
            Pointer(lo, false, false); yield return null;
            Pointer(lo, true, false); yield return null;
            for (int i = 1; i <= 6; i++) { Pointer(Vector2.Lerp(lo, hi, i / 6f), true, false); yield return null; }
            Pointer(hi, false, false); yield return null; yield return null;
            int selected = Rts.I.Selected.Count;
            Check(selected >= Mathf.Min(3, picks.Count), $"a box drag selects soldiers ({selected} of {picks.Count} boxed)");
            yield return Shot("command_selected");

            // Right-click on open ground 25 m ahead of them.
            Vector3 centre = Vector3.zero; foreach (var u in Rts.I.Selected) centre += u.Pos; centre /= Mathf.Max(1, selected);
            Vector3 dest = b.Field.Ground(centre + (b.Player.Team == Unit.Blue ? Vector3.forward : Vector3.back) * 25f);
            Vector2 ds = cam.WorldToScreenPoint(dest);
            Pointer(ds, false, false); yield return null;
            Pointer(ds, false, true); yield return null;
            Pointer(ds, false, false); yield return null;
            var movers = Rts.I.Selected.ToList();
            Check(movers.Count > 0 && movers.All(u => u.Order == Unit.OrderKind.Move || u.Order == Unit.OrderKind.Hold), "right-click on the ground gives a move order");
            yield return Shot("command_move");
            float t = 0f;
            while (t < 14f && movers.Any(u => u.Alive && Mathx.Flat(u.OrderPos - u.Pos).magnitude > 3f)) { t += Time.unscaledDeltaTime; yield return null; }
            float off = movers.Where(u => u.Alive).Select(u => Mathx.Flat(u.OrderPos - u.Pos).magnitude).DefaultIfEmpty(0f).Max();
            Check(off < 4f, $"ordered soldiers reach their places (furthest {off:F1} m off after {t:F0}s)");
            yield return Shot("command_arrived");

            // Right-click an enemy.
            var enemy = b.Army(Unit.Red).Where(u => u.Alive).OrderBy(u => (u.Pos - centre).sqrMagnitude).FirstOrDefault();
            if (enemy != null)
            {
                Rts.I.LookAt(enemy.Pos);
                yield return WaitReal(1.2f);
                // Whoever is now nearest the middle of the view: the one centred on may have died or ridden off meanwhile.
                Vector2 mid = new Vector2(Screen.width, Screen.height) * 0.5f;
                enemy = b.Army(Unit.Red).Where(u => u.Alive && cam.WorldToScreenPoint(u.Pos + Vector3.up).z > 0f)
                    .OrderBy(u => ((Vector2)cam.WorldToScreenPoint(u.Pos + Vector3.up) - mid).sqrMagnitude).FirstOrDefault();
                if (enemy == null) { Log("no enemy left to attack"); goto done; }
                Vector2 es = cam.WorldToScreenPoint(enemy.Pos + Vector3.up);
                bool onScreen = es.x > 0 && es.x < Screen.width && es.y > 0 && es.y < Screen.height;
                if (onScreen)
                {
                    Pointer(es, false, false); yield return null;
                    Pointer(es, false, true); yield return null;
                    Pointer(es, false, false); yield return null;
                    var attackers = Rts.I.Selected.Where(u => u.Alive).ToList();
                    Check(attackers.All(u => (u.Order == Unit.OrderKind.Attack && u.OrderTarget == enemy) || u.Order == Unit.OrderKind.AttackMove), "right-click on an enemy orders the attack");
                }
                else Check(false, "the enemy is on screen after centring the view on him");
                yield return Shot("command_attack");
            }
            done:
            Press(Key.Tab); yield return null;
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState()); yield return null; yield return null;
            Check(!Rts.Active, "Tab switches back to fighting");
            Battle.AutoPlay = this;
        }

        void Finish()
        {
            Check(exceptions == 0, $"no exceptions or errors in the log ({exceptions})");
            Log(errors == 0 ? "RESULT OK" : $"RESULT FAIL {errors} checks failed");
            Application.Quit(errors == 0 ? 0 : 1);
        }
    }
}
