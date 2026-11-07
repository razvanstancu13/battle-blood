using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace BattleBlood
{
    /// <summary>One battle from the first horn to the last man: builds the world from a seed, updates every unit, decides who the player is.</summary>
    public class Battle : MonoBehaviour
    {
        public enum Phase { Deploy, March, Fight, Over }
        public const float DeployLine = 40f;     // before the start each army stays on its own side of this: blue z <= -40, red z >= 40

        /// <summary>Clamped into a team's deployment ground.</summary>
        public static Vector3 InZone(int team, Vector3 p)
        {
            p.z = team == Unit.Blue ? Mathf.Min(p.z, -DeployLine) : Mathf.Max(p.z, DeployLine);
            p.x = Mathf.Clamp(p.x, -Field.Limit, Field.Limit);
            p.z = Mathf.Clamp(p.z, -Field.Limit, Field.Limit);
            return p;
        }
        public const int InfantryPerArmy = 160, ArchersPerArmy = 120, CavalryPerArmy = 120;
        public const int ArmySize = InfantryPerArmy + ArchersPerArmy + CavalryPerArmy;

        public static Battle I { get; private set; }
        public Field Field { get; private set; }
        public Arrows Arrows { get; private set; }
        public Fires Fires { get; private set; }
        public Hud Hud { get; private set; }
        public PlayerCam PlayerCam { get; private set; }
        public Dressing Dressing { get; private set; }
        public Mood Mood { get; private set; }
        public int Seed { get; private set; }
        public Phase Now { get; private set; }
        public float BattleTime { get; private set; }
        public Unit Player { get; private set; }
        public int Winner { get; private set; } = -1;
        public int PlayerKills { get; private set; }
        public int PlayerLives { get; private set; } = 1;
        public float FrameMs { get; private set; }

        readonly List<Unit> all = new();
        readonly List<Unit> order = new();

        /// <summary>Units act in a fresh random order each frame, so neither army gets the first blow by being listed first.</summary>
        void Shuffle()
        {
            if (order.Count != all.Count) { order.Clear(); order.AddRange(all); }
            for (int i = order.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }
        }

        /// <summary>A person is steering the player's soldier right now (not the test, not command mode).</summary>
        public static bool HumanControlled => AutoPlay == null && !Rts.Active;

        /// <summary>Game speed multiplier, for the balance runs (-bbspeed).</summary>
        public static float SpeedMul = 1f;
        public static int ForcedWeather = -1;        // -bbweather 0..3 for the test

        /// <summary>Set by the player's bow while he aims (1 = normal time, 0.5 = half). Only counts while a person is steering him.</summary>
        public static float AimSlow = 1f;
        float Aim()
        {
            if (!HumanControlled || Player == null || !Player.Alive || Player.Type != UnitType.Archer) AimSlow = 1f;
            return AimSlow;
        }
        readonly List<Unit>[] armies = { new(), new() };
        readonly Dictionary<long, float> trampled = new();
        float hitStopLeft, overTime;
        float slowMo = 1f;
        bool switching;

        public IReadOnlyList<Unit> All => all;
        public List<Unit> Army(int team) => armies[team];
        public int Alive(int team) { int n = 0; foreach (var u in armies[team]) if (u.Alive) n++; return n; }
        public int Alive(int team, UnitType t) { int n = 0; foreach (var u in armies[team]) if (u.Alive && u.Type == t) n++; return n; }

        public event System.Action<Unit, Unit> PlayerSwitched;     // old, new
        public event System.Action BattleOver;

        // Spatial hash: every unit in a cell, rebuilt each frame.
        const float HashCell = 5f;
        const int HashSize = 128;
        readonly int[] head = new int[HashSize * HashSize];
        int[] next;

        /// <summary>Set for a practice bout (one against one, no damage), null for a battle.</summary>
        public static PracticeSetup PracticeMode;
        public static bool NoDamage => PracticeMode != null;

        public static Battle Begin(int seed)
        {
            var go = new GameObject("Battle");
            var b = go.AddComponent<Battle>();
            b.StartCoroutine(b.Build(seed));
            return b;
        }

        public bool Ready { get; private set; }
        public float BuildProgress { get; private set; }
        public static System.Action<float, string> OnProgress;

        void Report(float p, string what) { BuildProgress = p; OnProgress?.Invoke(p, what); Debug.Log($"[BATTLE] {what}"); }

        System.Collections.IEnumerator Build(int seed)
        {
            I = this; Seed = seed;
            Random.InitState(seed);
            Report(0.05f, "Raising the land");
            yield return null;
            Field = new Field(seed);
            var mood = Mood.Pick(seed);
            Weather.Pick(seed, ref mood);
            Mood = mood.Copy();
            Weather.Shape(Mood);
            Mood.Apply(Field);
            gameObject.AddComponent<Weather>().Build(Mood);
            Mountains.Build(seed, Mood);
            Field.Build();
            if (Field.WaterMaterial != null) Field.WaterMaterial.SetColor("_SkyCol", Mood.Fog);
            Report(0.25f, "Growing the trees");
            yield return null;
            Fx.CreateShared();
            Fx.CreateSystems();
            Gore.Create();
            Arrows = new Arrows();
            Fires = new Fires();
            for (int i = 0; i < 3; i++) { Tally.Attempts[i] = Tally.Hits[i] = Tally.Kills[i] = Tally.Deaths[i] = 0; Tally.Damage[i] = 0f; }
            Dressing = new Dressing(Field, seed);
            Dressing.BuildProps();
            Report(0.45f, "Pitching the camps");
            yield return null;
            var bannerTex = Art.Tex("banner");
            for (int t = 0; t < 2; t++)
            {
                var m = new Material(Art.Shader("BBLit")) { name = "m_banner" };
                m.SetTexture("_BaseMap", bannerTex);
                m.SetFloat("_TintAmount", 1f);
                m.SetColor("_TintColor", Unit.TeamColor[t]);
                m.SetFloat("_Cull", 0f);
                m.SetFloat("_Cutoff", 0.4f);
                float z = t == Unit.Blue ? -128f : 128f;
                Dressing.BuildCamp(t, Unit.TeamColor[t], new Vector3(0, 0, z), t == Unit.Blue ? Vector3.forward : Vector3.back, m);
            }
            Dressing.BuildCards();
            SmokeColumns(seed);
            Report(0.6f, "Mustering the armies");
            yield return null;
            nextId = 0;
            if (PracticeMode != null) SpawnPractice();
            else
            {
                for (int t = 0; t < 2; t++)
                {
                    Deploy(t);
                    Report(0.6f + 0.15f * (t + 1), "Mustering the armies");
                    yield return null;
                }
                next = new int[all.Count];

                // The player: one random blue soldier.
                var blue = armies[Unit.Blue];
                Player = blue[Random.Range(0, blue.Count)];
                Player.IsPlayer = true;
            }

            var camGo = new GameObject("Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.farClipPlane = 4200f; cam.nearClipPlane = 0.12f; cam.fieldOfView = 60f;
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camGo.AddComponent<AudioListener>();
            PlayerCam = camGo.AddComponent<PlayerCam>();
            PlayerCam.Init(cam, Player);

            Hud = gameObject.AddComponent<Hud>();
            Hud.Build(this);
            gameObject.AddComponent<PlayerControl>();
            if (PracticeMode == null)
            {
                var rts = gameObject.AddComponent<Rts>();
                rts.Init(this, cam, PlayerCam);
                GameAudio.Instance.StartBattle(Field.SwampShare > 0.08f);
                Hud.Message("SET OUT YOUR ARMY ON YOUR SIDE OF THE FIELD, THEN START THE BATTLE", 6f);
                rts.Open();
                StartCoroutine(RedCommander(seed));
            }
            else
            {
                gameObject.AddComponent<Practice>().Init(this);
                foreach (var u in armies[Unit.Red]) u.Passive = PracticeMode.Opponent;
                GameAudio.Instance.StartBattle(false);
            }

            Report(1f, "Ready");
            Ready = true;
            Now = PracticeMode != null ? Phase.Fight : Phase.Deploy;
            Debug.Log($"[BATTLE] seed {seed}, mood {Mood.Name}, weather {Weather.Kind}, swamp {Field.SwampShare:P0}, hills {Field.HillShare:P0}, props {Dressing.PropCount}, cards {Dressing.CardCount}");
        }

        /// <summary>Ranks: infantry in front, archers behind, cavalry out on both flanks.</summary>
        void Deploy(int team)
        {
            float dir = team == Unit.Blue ? 1f : -1f;
            float line = -72f * dir;
            float yaw = team == Unit.Blue ? 0f : 180f;
            void Rank(UnitType type, int count, float z, float spacing, float xCentre, int perRow)
            {
                for (int i = 0; i < count; i++)
                {
                    int row = i / perRow, col = i % perRow;
                    int inRow = Mathf.Min(perRow, count - row * perRow);
                    float x = xCentre + (col - (inRow - 1) * 0.5f) * spacing + Random.Range(-0.5f, 0.5f);
                    float zz = z - dir * row * spacing + Random.Range(-0.5f, 0.5f);
                    var p = new Vector3(x, 0, zz);
                    Field.PushOut(ref p, 1.2f);
                    p.y = Field.Height(p.x, p.z);
                    var u = new Unit(nextId++, team, type, p, yaw);
                    u.PushRoot();
                    all.Add(u); armies[team].Add(u);
                }
            }
            // Wide rather than deep: the foot six ranks (under the 12 m to the archers), the horse out on both flanks past the foot's ends.
            Rank(UnitType.Infantry, InfantryPerArmy, line, 1.9f, 0f, 27);
            Rank(UnitType.Archer, ArchersPerArmy, line - dir * 12f, 2f, 0f, 24);
            Rank(UnitType.Cavalry, CavalryPerArmy / 2, line - dir * 2f, 3.4f, -44f, 9);
            Rank(UnitType.Cavalry, CavalryPerArmy - CavalryPerArmy / 2, line - dir * 2f, 3.4f, 44f, 9);
        }

        Vector3 practiceSpot; bool practiceSpotFound;

        /// <summary>Practice: the two soldiers, facing each other on a flat, dry, open patch near the middle. Called again to start over.</summary>
        public void SpawnPractice()
        {
            foreach (var u in all) if (u.Root != null) Destroy(u.Root.gameObject);
            all.Clear(); order.Clear(); armies[0].Clear(); armies[1].Clear();
            Arrows?.Clear();
            nextId = 0;
            if (!practiceSpotFound)
            {
                practiceSpot = Field.Ground(Vector3.zero);
                for (float r = 0f; r < 170f && !practiceSpotFound; r += 6f)
                    for (int k = 0; k < 12 && !practiceSpotFound; k++)
                    {
                        float a = k * Mathf.PI / 6f;
                        var p = Field.Ground(new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r));
                        bool ok = p.y > Field.WaterLevel + 0.8f && Field.FreeOfObstacles(p, 16f);
                        for (int sx = -2; sx <= 2 && ok; sx++)
                            for (int t = -2; t <= 2 && ok; t++)
                            {
                                var q = p + new Vector3(sx * 6f, 0, t * 6f);
                                ok = Field.Normal(q.x, q.z).y > 0.96f && Field.Height(q.x, q.z) > Field.WaterLevel + 0.6f;
                            }
                        if (ok) { practiceSpot = p; practiceSpotFound = true; }
                    }
                Debug.Log($"[BATTLE] practice ground at {practiceSpot}, found {practiceSpotFound}");
            }
            var s = PracticeMode;
            float gap = s.Mine == UnitType.Cavalry || s.Theirs == UnitType.Cavalry ? 14f : s.Mine == UnitType.Archer || s.Theirs == UnitType.Archer ? 12f : 7f;
            Unit Make(int team, UnitType type, Vector3 p, float yaw)
            {
                p = Field.Ground(p);
                var u = new Unit(nextId++, team, type, p, yaw);
                u.PushRoot();
                all.Add(u); armies[team].Add(u);
                return u;
            }
            var me = Make(Unit.Blue, s.Mine, practiceSpot - Vector3.forward * gap * 0.5f, 0f);
            Make(Unit.Red, s.Theirs, practiceSpot + Vector3.forward * gap * 0.5f, 180f);
            next = new int[all.Count];
            Player = me;
            me.IsPlayer = true;
            if (PlayerCam != null) { PlayerCam.SwitchTo(me); Hud.OnBodyChanged(me); Hud.Counts(1, 1); }
        }

        /// <summary>The Start button: the horn sounds and both armies go at each other. Deployment orders end here; men fight on their own until given new ones.</summary>
        public void StartFight()
        {
            if (Now != Phase.Deploy || !Ready) return;
            Now = Phase.March;
            BattleTime = 0f;
            foreach (var u in all) u.Order = Unit.OrderKind.None;
            GameAudio.Play2D("horn", 1f);
            GameAudio.Instance.BattleJoined(false);
            foreach (var u in all) if (Random.value < 0.15f) GameAudio.Play("cry", u.Pos, 0.7f);
            Hud.Message("THE HORN SOUNDS  ·  TAB TO FIGHT AS " + Player.S.Label.ToUpper(), 4f);
            Debug.Log("[BATTLE] started by the player");
        }

        /// <summary>
        /// The red army's commander sets out his men during the deployment, once, a few seconds in: his archers on the highest ground he holds,
        /// the foot in a line before them, the horse on one flank or both. A different plan each battle.
        /// </summary>
        System.Collections.IEnumerator RedCommander(int seed)
        {
            var r = new System.Random(seed ^ 0x5ed);
            float wait = 2.5f + (float)r.NextDouble() * 3f, t = 0f;
            while (t < wait && Now == Phase.Deploy) { t += Time.deltaTime; yield return null; }
            if (Now != Phase.Deploy) yield break;
            var red = armies[Unit.Red];
            List<Unit> Of(UnitType ty) => red.FindAll(u => u.Alive && u.Type == ty);
            Vector3 back = Vector3.back;      // red faces -z
            // Archers: the highest ground in the middle of his side.
            Vector3 hill = new(0, 0, 80f); float best = float.MinValue;
            for (float x = -50f; x <= 50f; x += 5f)
                for (float z = DeployLine + 14f; z <= 100f; z += 5f)
                {
                    float h = Field.Height(x, z) - Mathf.Abs(x) * 0.03f;
                    if (Field.WaterDepth(x, z) > 0.05f || !Field.FreeOfObstacles(new Vector3(x, 0, z), 3f)) continue;
                    if (h > best) { best = h; hill = new Vector3(x, 0, z); }
                }
            float lineZ = Mathf.Max(DeployLine + 2f, Mathf.Min(hill.z - 12f, DeployLine + 4f + (float)r.NextDouble() * 14f));
            float lineX = Mathf.Clamp(hill.x * 0.5f + ((float)r.NextDouble() - 0.5f) * 20f, -30f, 30f);
            int plan = r.Next(3);       // 0 horse on both flanks, 1 all on the left, 2 all on the right
            Rts.Formation(Of(UnitType.Archer), Unit.OrderKind.Move, InZone(Unit.Red, Field.Ground(hill)), back, Field);
            Rts.Formation(Of(UnitType.Infantry), Unit.OrderKind.Move, InZone(Unit.Red, Field.Ground(new Vector3(lineX, 0, lineZ))), back, Field);
            var horse = Of(UnitType.Cavalry);
            float wing = 34f + (float)r.NextDouble() * 18f;
            if (plan == 0)
            {
                Rts.Formation(horse.GetRange(0, horse.Count / 2), Unit.OrderKind.Move, InZone(Unit.Red, Field.Ground(new Vector3(lineX - wing, 0, lineZ + 6f))), back, Field);
                Rts.Formation(horse.GetRange(horse.Count / 2, horse.Count - horse.Count / 2), Unit.OrderKind.Move, InZone(Unit.Red, Field.Ground(new Vector3(lineX + wing, 0, lineZ + 6f))), back, Field);
            }
            else Rts.Formation(horse, Unit.OrderKind.Move, InZone(Unit.Red, Field.Ground(new Vector3(lineX + (plan == 1 ? wing : -wing), 0, lineZ + 8f))), back, Field);
            RedPlan = plan;
            Debug.Log($"[BATTLE] red commander: archers on the height at {hill}, line at z {lineZ:F0}, horse {(plan == 0 ? "both flanks" : plan == 1 ? "on his left" : "on his right")}");
        }
        public int RedPlan { get; private set; } = -1;

        void SmokeColumns(int seed)
        {
            var r = new System.Random(seed ^ 77);
            for (int i = 0; i < 5; i++)
            {
                float x = (float)r.NextDouble() * 320f - 160f, z = (float)r.NextDouble() * 320f - 160f;
                if (Mathf.Abs(z) < 90f && Mathf.Abs(x) < 70f) continue;
                var p = Field.Ground(new Vector3(x, 0, z));
                if (p.y < 0.5f) continue;
                var go = new GameObject("burning wreck");
                go.transform.position = p;
                go.AddComponent<FieldFire>();
            }
        }

        // ---- spatial queries ----

        static int Cell(float v) => Mathf.Clamp(Mathf.FloorToInt((v + 320f) / HashCell), 0, HashSize - 1);

        void RebuildHash()
        {
            System.Array.Fill(head, -1);
            for (int i = 0; i < all.Count; i++)
            {
                var u = all[i];
                if (!u.Alive) continue;
                int h = Cell(u.Pos.x) + Cell(u.Pos.z) * HashSize;
                next[i] = head[h];
                head[h] = i;
            }
        }

        public void Query(Vector3 p, float radius, List<Unit> results)
        {
            results.Clear();
            if (next == null) return;
            int x0 = Cell(p.x - radius), x1 = Cell(p.x + radius), z0 = Cell(p.z - radius), z1 = Cell(p.z + radius);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    for (int i = head[x + z * HashSize]; i >= 0; i = next[i])
                        results.Add(all[i]);
        }

        int nextId;
        readonly List<Unit> nearList = new(32);

        void Separate(float dt)
        {
            for (int i = 0; i < all.Count; i++)
            {
                var a = all[i];
                if (!a.Alive) continue;
                for (int z = Cell(a.Pos.z - 2.4f); z <= Cell(a.Pos.z + 2.4f); z++)
                    for (int x = Cell(a.Pos.x - 2.4f); x <= Cell(a.Pos.x + 2.4f); x++)
                        for (int j = head[x + z * HashSize]; j >= 0; j = next[j])
                        {
                            if (j <= i) continue;
                            var b = all[j];
                            Vector3 d = b.Pos - a.Pos; d.y = 0;
                            float min = a.S.Radius + b.S.Radius;
                            float m2 = d.sqrMagnitude;
                            if (m2 >= min * min || m2 < 1e-5f) continue;
                            float m = Mathf.Sqrt(m2);
                            Vector3 push = d / m * (min - m) * Mathf.Min(1f, dt * 12f);
                            float wa = a.Type == UnitType.Cavalry ? 0.15f : b.Type == UnitType.Cavalry ? 0.85f : 0.5f;
                            a.Pos -= push * wa;
                            b.Pos += push * (1f - wa);
                        }
            }
        }

        // ---- the frame ----

        void Update()
        {
            if (!Ready) return;
            float started = Time.realtimeSinceStartup;
            float dt = Time.deltaTime;
            if (hitStopLeft > 0f)
            {
                hitStopLeft -= Time.unscaledDeltaTime;
                Time.timeScale = (hitStopLeft > 0f ? 0.06f : slowMo) * SpeedMul * Aim();
            }
            else Time.timeScale = slowMo * SpeedMul * Aim();

            if (cinematicLeft > 0f)
            {
                cinematicLeft -= Time.unscaledDeltaTime;
                slowMo = cinematicLeft > 0f ? Mathf.MoveTowards(slowMo, 0.15f, Time.unscaledDeltaTime * 8f) : 1f;
            }
            UpdatePhase(dt);
            Vector3 camPos = PlayerCam.transform.position;
            float now = Time.time;
            RebuildHash();
            Shuffle();
            foreach (var u in order)
            {
                u.CamDist = Vector3.Distance(camPos, u.Pos);
                u.NearCamera = u.CamDist < 34f;       // arms placed by IK only this close: further off nobody can tell
                if (u.IsPlayer && Battle.AutoPlay == null && !Rts.Active) { /* PlayerControl sets the intents */ }
                else u.Brain(dt, now, Now);
            }
            foreach (var u in order) u.Tick(dt);
            Separate(dt);
            foreach (var u in order)
            {
                if (u.Type == UnitType.Cavalry && u.Alive) Combat.Trample(u, trampled);
                if (u.Alive) u.PushRoot();      // a corpse places itself, along the slope
            }
            Arrows.Tick(dt);
            Fires.Tick(dt);
            Gore.Tick(dt);
            Fx.Tick(dt);
            Field.Tick(dt);
            Dressing.DrawCards(PlayerCam.Cam);
            Arrows.Draw();
            Gore.Draw();
            FrameMs = (Time.realtimeSinceStartup - started) * 1000f;
        }

        void LateUpdate()
        {
            if (!Ready) return;
            float dt = Time.deltaTime;
            foreach (var u in all)
                if (u.NearCamera || u.Type == UnitType.Cavalry || u.Alive) u.LatePose(dt);
        }

        void UpdatePhase(float dt)
        {
            if (PracticeMode != null) { Now = Phase.Fight; BattleTime += dt; return; }
            if (Now == Phase.Over)
            {
                overTime += Time.unscaledDeltaTime;
                slowMo = Mathf.MoveTowards(slowMo, overTime < 3.2f ? 0.22f : 1f, Time.unscaledDeltaTime * 0.6f);
                return;
            }
            if (Now == Phase.Deploy) return;       // until the player presses Start
            BattleTime += dt;
            if (Now == Phase.March && (BattleTime > 28f || MeleeHasBegun()))
            {
                Now = Phase.Fight;
                GameAudio.Instance.BattleJoined(true);
            }
        }

        bool MeleeHasBegun()
        {
            foreach (var a in armies[0])
            {
                if (!a.Alive || a.Target == null) continue;
                if ((a.Pos - a.Target.Pos).sqrMagnitude < 25f) return true;
            }
            return false;
        }

        // ---- deaths and the player ----

        public void OnUnitDied(Unit u, Unit killer, HitKind kind)
        {
            if (killer != null && killer.IsPlayer)
            {
                PlayerKills++;
                Hud.KillFeed(killer, u, kind);
                // Every kill of yours is a cinematic one: slow motion, the camera swung round to watch him fall.
                if (HumanControlled && PracticeMode == null) Cinematic(u, 1.6f);
            }
            else if (u.IsPlayer) Hud.KillFeed(killer, u, kind);
            else if (Random.value < 0.06f) Hud.KillFeed(killer, u, kind);

            int blue = Alive(Unit.Blue), red = Alive(Unit.Red);
            Hud.Counts(blue, red);
            if (blue == 0 || red == 0) { EndBattle(blue == 0 ? Unit.Red : Unit.Blue); return; }
            if (u.IsPlayer) StartCoroutine(SwitchPlayer(u));
        }

        /// <summary>Control passes to a living blue soldier: at full health and not being attacked if there is one, else the healthiest nearby.</summary>
        public Unit ChooseNextBody(Unit from)
        {
            Unit best = null;
            float bestScore = float.MinValue;
            foreach (var u in armies[Unit.Blue])
            {
                if (!u.Alive || u == from) continue;
                bool full = u.Hp >= u.S.MaxHp - 0.5f;
                bool attacked = UnderAttack(u);
                float score = (full ? 1000f : 0f) + (attacked ? 0f : 500f) + u.Hp / u.S.MaxHp * 100f - Vector3.Distance(u.Pos, from.Pos) * 0.3f;
                if (u.Burning) score -= 800f;
                if (score > bestScore) { bestScore = score; best = u; }
            }
            return best;
        }

        public bool UnderAttack(Unit u)
        {
            if (Time.time - u.LastHitTime < 4f) return true;
            Query(u.Pos, 7f, nearList);
            foreach (var e in nearList)
                if (e.Alive && e.Team != u.Team && (e.Pos - u.Pos).sqrMagnitude < 36f) return true;
            return false;
        }

        System.Collections.IEnumerator SwitchPlayer(Unit dead)
        {
            if (switching) yield break;
            switching = true;
            dead.IsPlayer = false;
            // Let the death be seen, slowly.
            slowMo = 0.3f;
            Hud.Message("YOU HAVE FALLEN", 1.8f);
            GameAudio.Instance.Muffle(true);
            float t = 0f;
            while (t < 1.5f) { t += Time.unscaledDeltaTime; yield return null; }
            slowMo = 1f;
            GameAudio.Instance.Muffle(false);
            if (Now == Phase.Over) { switching = false; yield break; }

            var nextUnit = ChooseNextBody(dead);
            if (nextUnit == null) { switching = false; yield break; }
            bool wasFull = nextUnit.Hp >= nextUnit.S.MaxHp - 0.5f;
            bool wasSafe = !UnderAttack(nextUnit);
            Debug.Log($"[BATTLE] control jumps from {dead.Root.name} to {nextUnit.Root.name}, full health {wasFull}, not attacked {wasSafe}");
            Player = nextUnit;
            nextUnit.IsPlayer = true;
            PlayerLives++;
            PlayerCam.SwitchTo(nextUnit);
            Hud.OnBodyChanged(nextUnit);
            Hud.Message("YOU FIGHT ON AS " + nextUnit.S.Label.ToUpper(), 2.4f);
            PlayerSwitched?.Invoke(dead, nextUnit);
            switching = false;
        }

        public void SpreadFire(Unit from)
        {
            Query(from.Pos, 2.2f, nearList);
            foreach (var n in nearList)
                if (n != from && n.Alive && !n.Burning && (n.Pos - from.Pos).sqrMagnitude < 2.2f && (from.LastBurner == null || n.Team != from.LastBurner.Team))
                {
                    n.Ignite(from.LastBurner);
                    return;
                }
        }

        void EndBattle(int winner)
        {
            if (Now == Phase.Over) return;
            Winner = winner;
            Now = Phase.Over;
            overTime = 0f;
            slowMo = 0.22f;
            bool won = winner == Unit.Blue;
            GameAudio.Instance.BattleEnded(won);
            foreach (var u in armies[winner]) if (u.Alive && Random.value < 0.8f) u.Cheer();
            Hud.ShowResult(won);
            BattleOver?.Invoke();
            Debug.Log($"[BATTLE] over, winner {(won ? "blue" : "red")}, blue left {Alive(0)}, red left {Alive(1)}, time {BattleTime:F0}s");
        }

        readonly Vector3[] infCentre = new Vector3[2]; readonly float[] infHalf = new float[2]; float infAge = -1f;
        /// <summary>The middle of a team's living infantry and half the width of its line: cavalry ride round its ends. Refreshed once a second.</summary>
        /// <summary>Where the fighting is: the middle of everyone with an enemy within 6 m, or before the lines meet, between the two bodies of foot.
        /// Soldiers lean towards it, so the battle stays a crush rather than spreading over the field.</summary>
        public Vector3 ClashCentre
        {
            get
            {
                if (Time.time - clashAge > 0.5f || clashAge < 0f)
                {
                    clashAge = Time.time;
                    Vector3 c = Vector3.zero; int n = 0;
                    foreach (var u in all)
                        if (u.Alive && u.Target != null && u.Target.Alive && Mathx.Flat(u.Target.Pos - u.Pos).sqrMagnitude < 36f) { c += u.Pos; n++; }
                    if (n >= 4) clash = c / n;
                    else clash = (InfantryCentre(0, out _) + InfantryCentre(1, out _)) * 0.5f;
                    clash.y = 0f;
                }
                return clash;
            }
        }
        Vector3 clash; float clashAge = -1f;

        public Vector3 InfantryCentre(int team, out float halfWidth)
        {
            if (Time.time - infAge > 1f || infAge < 0f)
            {
                infAge = Time.time;
                for (int t = 0; t < 2; t++)
                {
                    Vector3 c = Vector3.zero; int n = 0;
                    foreach (var u in armies[t]) if (u.Alive && u.Type == UnitType.Infantry) { c += u.Pos; n++; }
                    if (n == 0) { infHalf[t] = 0f; continue; }
                    c /= n;
                    float w = 0f; foreach (var u in armies[t]) if (u.Alive && u.Type == UnitType.Infantry) w += Mathf.Abs(u.Pos.x - c.x);
                    infCentre[t] = c; infHalf[t] = w / n * 2f;      // mean distance from the middle, doubled: about the half-width
                }
            }
            halfWidth = infHalf[team];
            return infCentre[team];
        }

        public void HitStop(float seconds) { hitStopLeft = Mathf.Max(hitStopLeft, seconds); }

        float cinematicLeft;
        public int CinematicKills { get; private set; }
        /// <summary>Slow motion for this many real seconds, then back to full speed.</summary>
        public void Cinematic(Unit victim, float seconds)
        {
            if (switching || Now == Phase.Over || cinematicLeft > 0f) return;
            cinematicLeft = seconds; CinematicKills++;
            PlayerCam.KillShot(victim, seconds);
            Hud.Letterbox(seconds);
        }

        public static object AutoPlay;     // set by the test: the player is run by the same brain as everyone else

        void OnDestroy()
        {
            Time.timeScale = 1f;
        }
    }
}
