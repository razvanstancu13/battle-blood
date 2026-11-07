using System.Collections.Generic;
using UnityEngine;

namespace BattleBlood
{
    /// <summary>The numbers to tune the armies by, in one place. tools/verify_game.py prints what they led to.</summary>
    public static class Balance
    {
        public const float InfantryHit = 0.62f, CavalryHit = 0.42f;
        public const float ArcherSpreadBase = 1.3f, ArcherSpreadPerMetre = 0.035f;
        public const float ArcherPauseMin = 1.6f, ArcherPauseMax = 3.6f, ArcherLitChance = 0.08f;
        public const float ShieldCatchesArrow = 0.4f;       // a man not blocking, facing the arrow: the shield on his arm stops this share
        public const float PlayerArrowBonus = 1.9f;         // the player's aim is worth more than a line of men loosing at a crowd
        public const float BurnPerSecond = 6f, BurnSeconds = 6f;
    }

    /// <summary>Who tried what, and what came of it: per unit type.</summary>
    public static class Tally
    {
        public static readonly int[] Attempts = new int[3], Hits = new int[3], Kills = new int[3], Deaths = new int[3];
        public static readonly float[] Damage = new float[3];
        public static string Line(UnitType t)
        {
            int i = (int)t;
            return $"{t,-8} attacks {Attempts[i],4}  hits {Hits[i],4} ({(Attempts[i] > 0 ? 100f * Hits[i] / Attempts[i] : 0f):F0}%)  damage {Damage[i],6:F0}  kills {Kills[i],3}  lost {Deaths[i],3}";
        }
    }

    /// <summary>The rules of a blow: who it reaches, whether it is stopped, how much it hurts.</summary>
    public static class Combat
    {
        static readonly List<Unit> Near = new(64);

        /// <summary>A sword cut at its moment of striking. It can catch more than one man when the player swings; AI soldiers hit one.</summary>
        public static void Strike(Unit a, SwingDef d)
        {
            float reach = a.S.Reach * d.ReachMul;
            float mounted = a.Type == UnitType.Cavalry ? Mathf.Clamp01(a.Speed / a.S.Sprint) : 0f;
            float dmg = a.S.Damage * d.DamageMul * (1f + mounted * 0.3f);      // a charge puts the horse's weight behind the blade
            Vector3 fwd = a.Type == UnitType.Cavalry ? (Mathx.Flat(a.Aim).sqrMagnitude > 0.01f ? Mathx.Flat(a.Aim).normalized : a.Forward) : a.Forward;
            int maxHits = a.IsPlayer ? 2 : 1;
            Battle.I.Query(a.Pos, reach + 2f, Near);
            Near.Sort((x, y) => (x.Pos - a.Pos).sqrMagnitude.CompareTo((y.Pos - a.Pos).sqrMagnitude));
            int hits = 0;
            foreach (var v in Near)
            {
                if (v == a || !v.Alive || v.Team == a.Team) continue;
                Vector3 to = Mathx.Flat(v.Pos - a.Pos);
                float dist = to.magnitude - v.S.Radius;
                if (dist > reach) continue;
                if (to.sqrMagnitude > 0.01f && Vector3.Angle(fwd, to) > d.Arc) continue;
                Vector3 dir = to.sqrMagnitude > 0.01f ? to.normalized : fwd;
                if (hits++ >= maxHits) break;
                Tally.Attempts[(int)a.Type]++;
                // The player's cuts land if they are aimed; a soldier's own chance decides his.
                if (!a.IsPlayer && Random.value > ChanceToHit(a, v)) continue;
                Tally.Hits[(int)a.Type]++;
                Land(a, v, dmg, dir, d);
            }
        }

        /// <summary>A computer soldier's chance that a cut in reach connects: its type's skill, then whether the target can get out of the way.</summary>
        static float ChanceToHit(Unit a, Unit v)
        {
            float c = a.Type == UnitType.Cavalry ? Balance.CavalryHit : Balance.InfantryHit;
            if (v.Stunned) c += 0.25f;
            if (v.Speed > 5f) c -= 0.15f;
            if (v.Type == UnitType.Cavalry && a.Type != UnitType.Cavalry) c += v.Speed < 3f ? 0.25f : 0.1f;       // a horse is a big target, above all a standing one
            return Mathf.Clamp(c, 0.1f, 0.95f);
        }

        static void Land(Unit a, Unit v, float dmg, Vector3 dir, SwingDef d)
        {
            Vector3 hitPos = v.ChestPos + Vector3.up * Random.Range(-0.25f, 0.2f);
            if (v.Invulnerable) return;
            if (Frontal(v, a.Pos) && v.Blocking)
            {
                // Stopped on the shield. Wood chips and sparks; the attacker's blade bounces, a mounted one drives him back.
                bool heavy = a.Type == UnitType.Cavalry && a.Speed > 7f;
                Fx.Sparks(v.ShieldHolder != null ? v.ShieldHolder.position : hitPos, -dir, heavy ? 26 : 16);
                GameAudio.Play("block", hitPos, 1f);
                GameAudio.Play("clash", hitPos, 0.6f);
                v.ShieldTakesBlow();
                if (heavy) { v.TakeDamage(dmg * 0.35f, a, dir, HitKind.Sword, hitPos); v.Knock += dir * 5f; v.Stagger(0.55f); }
                else { v.TakeDamage(dmg * 0.08f, a, dir, HitKind.Sword, hitPos); a.Stagger(0.32f); }
                if (a.IsPlayer) Battle.I.PlayerCam.Shake(0.18f);
                return;
            }
            GameAudio.Play("flesh", hitPos, 1f);
            GameAudio.Play("gore", hitPos, 0.55f);
            Gore.Wound(hitPos, dir, Mathf.Clamp01(dmg / 30f), v);
            v.TakeDamage(dmg, a, dir, HitKind.Sword, hitPos);
            a.HitsThisRun++;            // a horseman rides out after a few of these
            if (v.Alive)
            {
                v.Knock += dir * 1.6f;
                if (v.Type != UnitType.Cavalry) v.Stagger(0.32f, Random.value < 0.5f ? "HitChest" : "HitHead");
                else v.HorseHitReact();
                // Badly cut soldiers can lose the shield arm and keep fighting, bleeding. Not the player: it would take his block away for good.
                if (!v.IsPlayer && v.Type == UnitType.Infantry && !v.LeftArmGone && v.Hp < v.S.MaxHp * 0.4f && Random.value < 0.3f)
                    Gore.SeverLive(v, Gore.Part.ForearmL, dir);
            }
            if (a.IsPlayer)
            {
                Battle.I.PlayerCam.Shake(v.Alive ? 0.22f : 0.38f);
                Battle.I.HitStop(v.Alive ? 0.045f : 0.085f);
                Battle.I.Hud.HitMarker(v.Alive);
                if (Vector3.Distance(a.Pos, v.Pos) < 3.2f) Battle.I.Hud.LensSplat(v.Alive ? 0.45f : 1f);
            }
        }

        public static bool Frontal(Unit v, Vector3 from)
        {
            Vector3 to = Mathx.Flat(from - v.Pos);
            return to.sqrMagnitude < 0.01f || Vector3.Dot(to.normalized, v.Forward) > 0.3f;
        }

        /// <summary>The shield punched forward: little damage, breaks a guard and leaves the man dazed.</summary>
        public static void Bash(Unit a)
        {
            Battle.I.Query(a.Pos, 3.4f, Near);
            foreach (var v in Near)
            {
                if (v == a || !v.Alive || v.Team == a.Team || v.Type == UnitType.Cavalry) continue;
                Vector3 to = Mathx.Flat(v.Pos - a.Pos);
                if (to.magnitude - v.S.Radius > 1.5f) continue;
                if (to.sqrMagnitude > 0.01f && Vector3.Angle(a.Forward, to) > 55f) continue;
                Vector3 dir = to.sqrMagnitude > 0.01f ? to.normalized : a.Forward;
                GameAudio.Play("bash", v.ChestPos, 1f);
                Gore.Wound(v.HeadPos, dir, 0.3f, v);
                v.TakeDamage(8f, a, dir, HitKind.Bash, v.ChestPos);
                v.Knock += dir * 9f;          // shoved back a couple of metres
                if (v.Alive) v.Knocked(1.1f);
                if (a.IsPlayer) { Battle.I.PlayerCam.Shake(0.28f); Battle.I.HitStop(0.06f); Battle.I.Hud.HitMarker(v.Alive); }
                break;
            }
        }

        /// <summary>A horse at a walk or trot does not trample, but it shoulders men on foot out of its way.</summary>
        static void Shoulder(Unit rider, Dictionary<long, float> recent)
        {
            Battle.I.Query(rider.Pos + rider.Forward * 1.1f, 2f, Near);
            foreach (var v in Near)
            {
                if (v == rider || !v.Alive || v.Type == UnitType.Cavalry || v.Invulnerable) continue;
                Vector3 to = Mathx.Flat(v.Pos - rider.Pos);
                if (to.magnitude > 1.9f || Vector3.Dot(to.normalized, rider.Forward) < 0.3f) continue;
                long key = rider.Id * 1000L + v.Id;
                if (recent.TryGetValue(key, out float t) && Time.time - t < 1.5f) continue;
                recent[key] = Time.time;
                Vector3 side = Vector3.Cross(Vector3.up, rider.Forward) * (Vector3.Dot(to, Vector3.Cross(Vector3.up, rider.Forward)) >= 0f ? 1f : -1f);
                v.Knock += (rider.Forward * 0.6f + side).normalized * (2.5f + rider.Speed * 0.7f);
                if (v.Team != rider.Team) { GameAudio.Play("bash", v.ChestPos, 0.6f); v.Knocked(0.7f); }
            }
        }

        /// <summary>A horse at speed goes through men on foot.</summary>
        public static void Trample(Unit rider, Dictionary<long, float> recent)
        {
            if (rider.Speed < 2.2f) return;
            if (rider.Speed < 6f) { Shoulder(rider, recent); return; }
            Battle.I.Query(rider.Pos + rider.Forward * 1.2f, 2.4f, Near);
            foreach (var v in Near)
            {
                if (v == rider || !v.Alive || v.Team == rider.Team || v.Type == UnitType.Cavalry || v.Invulnerable) continue;
                Vector3 to = Mathx.Flat(v.Pos - rider.Pos);
                if (to.magnitude > 2f || Vector3.Dot(to.normalized, rider.Forward) < 0.25f) continue;
                long key = rider.Id * 1000L + v.Id;
                if (recent.TryGetValue(key, out float t) && Time.time - t < 2.5f) continue;
                recent[key] = Time.time;
                Vector3 side = Vector3.Cross(Vector3.up, rider.Forward) * (Vector3.Dot(to, Vector3.Cross(Vector3.up, rider.Forward)) >= 0f ? 1f : -1f);
                Vector3 dir = (rider.Forward + side * 0.8f).normalized;
                float dmg = Mathf.Lerp(6f, 16f, Mathf.InverseLerp(6f, rider.S.Sprint, rider.Speed));
                bool blocked = v.Blocking && Frontal(v, rider.Pos);
                if (blocked) dmg *= 0.5f;
                GameAudio.Play("trample", v.ChestPos, 1f);
                Gore.Wound(v.ChestPos, dir, 1f, v);
                v.TakeDamage(dmg, rider, dir, HitKind.Trample, v.ChestPos);
                v.Knock += dir * (rider.Speed * 0.95f + 3f);
                if (v.Alive) v.Knocked(1.1f);
                rider.Speed *= 0.68f;      // each man hit takes the charge out of the horse: it bogs down in a crowd
                rider.AddBlood(0.04f);
                if (rider.IsPlayer) { Battle.I.PlayerCam.Shake(0.45f); Battle.I.HitStop(0.05f); Battle.I.Hud.HitMarker(v.Alive); }
            }
        }
    }

    /// <summary>
    /// Every arrow in the air and every arrow stuck in something, drawn instanced: hundreds cost a few draw calls.
    /// Flying arrows fall under gravity and are tested against men and horses as capsules. Lit ones set a man on fire.
    /// </summary>
    public class Arrows
    {
        struct Flying { public Vector3 Pos, Vel; public Unit Owner; public float Damage, Age; public bool Fire; }
        struct Stuck { public Transform Parent; public Vector3 LocalPos; public Quaternion LocalRot; public Vector3 Pos; public Quaternion Rot; public bool Fire; public float FireLeft, Born; }
        public const float ArrowStays = 6f;     // seconds a landed arrow stays in the ground, a prop, a shield or a man

        readonly List<Flying> flying = new(256);
        const int MaxStuck = 2400;
        readonly Stuck[] stuck = new Stuck[MaxStuck];
        int stuckCount, stuckNext;
        readonly Matrix4x4[] batch = new Matrix4x4[1023];
        readonly List<Unit> near = new(32);
        public int FlyingCount => flying.Count;
        public int FireHits;

        public void Clear() { flying.Clear(); stuckCount = 0; stuckNext = 0; }

        public void Fire(Vector3 from, Vector3 velocity, Unit owner, float damage, bool fire)
        {
            flying.Add(new Flying { Pos = from, Vel = velocity, Owner = owner, Damage = damage, Fire = fire });
        }

        public void Tick(float dt)
        {
            var field = Battle.I.Field;
            for (int i = flying.Count - 1; i >= 0; i--)
            {
                var a = flying[i];
                Vector3 start = a.Pos;
                a.Vel += Vector3.down * (9.81f * dt);
                a.Pos += a.Vel * dt;
                a.Age += dt;
                if (a.Fire) { Fx.ArrowFlame(a.Pos, 2); }
                bool gone = false;

                // Men first: the arrow's path this frame against nearby capsules.
                Vector3 mid = (start + a.Pos) * 0.5f;
                float seg = Vector3.Distance(start, a.Pos);
                Battle.I.Query(mid, seg * 0.5f + 2f, near);
                foreach (var v in near)
                {
                    if (!v.Alive || v.Team == a.Owner.Team || v.Invulnerable) continue;
                    if (!HitsUnit(start, a.Pos, v, out Vector3 hit)) continue;
                    Impact(a, v, hit);
                    gone = true;
                    break;
                }
                if (gone) { flying.RemoveAt(i); continue; }

                // Where the path first goes under the ground this frame, if it does.
                float groundT = 2f;
                if (a.Pos.y <= field.Height(a.Pos.x, a.Pos.z))
                {
                    float lo = 0f, hi = 1f;
                    if (start.y <= field.Height(start.x, start.z)) hi = 0f;
                    else
                        for (int k = 0; k < 10; k++)
                        {
                            float mt = (lo + hi) * 0.5f; Vector3 q = Vector3.Lerp(start, a.Pos, mt);
                            if (q.y > field.Height(q.x, q.z)) lo = mt; else hi = mt;
                        }
                    groundT = hi;
                }
                // A prop's own surface, if it comes first.
                Vector3 step = a.Pos - start; float len = step.magnitude;
                if (len > 1e-4f && Physics.Raycast(start, step / len, out var rh, len * Mathf.Min(1f, groundT) + 0.01f, Layers.PropMask, QueryTriggerInteraction.Ignore))
                {
                    var pr = rh.collider.GetComponentInParent<PropRef>();
                    if (a.Fire && pr != null) Battle.I.Fires.LightProp(pr.Prop, a.Owner);
                    GameAudio.Play("arrow_wood", rh.point, 0.35f);
                    Stick(rh.collider.transform, rh.point, a.Vel, a.Fire, 0.09f);
                    flying.RemoveAt(i);
                    continue;
                }
                if (groundT <= 1f)
                {
                    Vector3 g = Vector3.Lerp(start, a.Pos, groundT);
                    g.y = field.Height(g.x, g.z);
                    if (g.y <= Field.WaterLevel && field.InWater(g.x, g.z))
                    {
                        Fx.Splash(new Vector3(g.x, Field.WaterLevel, g.z), 2);
                        if (a.Fire) Fx.Puff(new Vector3(g.x, Field.WaterLevel, g.z), 4);
                    }
                    else
                    {
                        GameAudio.Play("arrow_ground", g, 0.3f);
                        if (a.Fire) Battle.I.Fires.LightGround(g, a.Owner);
                        Stick(null, g, a.Vel, a.Fire, 0.22f);
                    }
                    flying.RemoveAt(i);
                    continue;
                }
                if (a.Age > 9f) { flying.RemoveAt(i); continue; }
                flying[i] = a;
            }

            for (int i = 0; i < stuckCount; i++)
            {
                if (stuck[i].FireLeft > 0f)
                {
                    stuck[i].FireLeft -= dt;
                    Vector3 p = stuck[i].Parent != null ? stuck[i].Parent.TransformPoint(stuck[i].LocalPos) : stuck[i].Pos;
                    if (Random.value < 0.5f) Fx.ArrowFlame(p, 1);
                }
            }
        }

        void Impact(Flying a, Unit v, Vector3 hit)
        {
            Vector3 dir = a.Vel.normalized;
            Tally.Hits[(int)UnitType.Archer]++;
            bool frontal = Combat.Frontal(v, a.Owner.Pos);
            bool blocked = frontal && (v.Blocking || (v.ShieldHolder != null && Random.value < Balance.ShieldCatchesArrow));
            if (blocked && v.ShieldHolder != null)
            {
                GameAudio.Play("arrow_wood", hit, 0.9f);
                var sh = v.ShieldHolder;
                // Into the shield's face somewhere on the board, the head just through it.
                Vector3 onBoard = sh.position + sh.right * Random.Range(-0.15f, 0.15f) + sh.up * Random.Range(-0.2f, 0.22f);
                Stick(sh, onBoard, a.Vel, a.Fire, 0.06f);
                Fx.Sparks(hit, -dir, 3);
                // A lit arrow in the shield can still catch the man behind it, sometimes.
                if (a.Fire && Random.value < 0.25f) { v.Ignite(a.Owner); FireHits++; }
                return;
            }
            bool headshot = v.Type != UnitType.Cavalry ? hit.y > v.Pos.y + 1.5f : hit.y > v.Pos.y + 2.3f;
            float dmg = a.Damage * (headshot ? 2f : 1f);
            Transform bone = headshot ? v.HeadBone : v.BoneNear(hit);
            GameAudio.Play("arrow_body", hit, 1f);
            Gore.Wound(hit, dir, headshot ? 1f : 0.6f, v);
            if (bone != null) Stick(bone, v.SurfaceNear(hit, dir), a.Vel, a.Fire, 0.12f);
            if (a.Fire) { v.Ignite(a.Owner); FireHits++; }
            v.TakeDamage(dmg, a.Owner, dir, a.Fire ? HitKind.FireArrow : HitKind.Arrow, hit);
            if (v.Alive && v.Type != UnitType.Cavalry && dmg > 14f) v.Stagger(0.25f, headshot ? "HitHead" : "HitChest");
            else if (v.Alive && v.Type == UnitType.Cavalry) v.HorseHitReact();
            if (a.Owner.IsPlayer)
            {
                Battle.I.Hud.HitMarker(v.Alive);
                if (headshot) Battle.I.Hud.Message("HEADSHOT", 1.2f);
            }
        }

        const float ArrowLength = 0.82f;

        /// <summary>An arrow left in what it hit: pos is where it met the surface, depth how far the head went in.</summary>
        void Stick(Transform parent, Vector3 pos, Vector3 vel, bool fire, float depth)
        {
            var q = Quaternion.LookRotation(vel.normalized);
            Vector3 nock = pos - q * Vector3.forward * (ArrowLength - depth);
            var s = new Stuck { Parent = parent, Pos = nock, Rot = q, Fire = fire, FireLeft = fire ? 5f : 0f, Born = Time.time };
            if (parent != null)
            {
                s.LocalPos = parent.InverseTransformPoint(nock);
                s.LocalRot = Quaternion.Inverse(parent.rotation) * q;
            }
            stuck[stuckNext] = s;
            stuckNext = (stuckNext + 1) % MaxStuck;
            stuckCount = Mathf.Min(MaxStuck, stuckCount + 1);
        }

        /// <summary>A man is a capsule. A horseman is the rider's capsule above the saddle and the horse's barrel below it, an ellipse along its length.</summary>
        static bool HitsUnit(Vector3 a, Vector3 b, Unit v, out Vector3 hit)
        {
            if (v.Type != UnitType.Cavalry) return HitsCapsule(a, b, v.Pos, v.S.Height, 0.3f, out hit);
            if (HitsCapsule(a, b, v.Pos + Vector3.up * 2.0f, v.S.Height - 2.0f, 0.3f, out hit)) return true;
            // The barrel and neck: an ellipse 1.35 m to front and back, 0.42 m to each side, from 1.0 to 2.0 m up.
            Vector3 f = v.Forward, r = Vector3.Cross(Vector3.up, f);
            for (int i = 0; i <= 12; i++)
            {
                Vector3 p = Vector3.Lerp(a, b, i / 12f);
                float y = p.y - v.Pos.y;
                if (y < 1.0f || y > 2.0f) continue;
                Vector3 d = p - v.Pos;
                float lf = Vector3.Dot(d, f) / 1.35f, lr = Vector3.Dot(d, r) / 0.42f;
                if (lf * lf + lr * lr <= 1f) { hit = p; return true; }
            }
            hit = a;
            return false;
        }

        /// <summary>The closest approach of a segment to a vertical capsule.</summary>
        static bool HitsCapsule(Vector3 a, Vector3 b, Vector3 foot, float height, float radius, out Vector3 hit)
        {
            hit = a;
            Vector2 p = new(a.x - foot.x, a.z - foot.z), d = new(b.x - a.x, b.z - a.z);
            float dd = d.sqrMagnitude;
            float t = dd > 1e-6f ? Mathf.Clamp01(-Vector2.Dot(p, d) / dd) : 0f;
            if ((p + d * t).sqrMagnitude > radius * radius) return false;
            hit = Vector3.Lerp(a, b, t);
            return hit.y >= foot.y && hit.y <= foot.y + height;
        }

        public void Draw()
        {
            float now = Time.time;
            int n = 0;
            var mesh = Fx.ArrowMesh; var mat = Fx.ArrowMaterial;
            void Add(Matrix4x4 m)
            {
                batch[n++] = m;
                if (n == batch.Length) { Flush(mesh, mat, n); n = 0; }
            }
            foreach (var a in flying)
                if (a.Vel.sqrMagnitude > 0.01f) Add(Matrix4x4.TRS(a.Pos - a.Vel.normalized * 0.56f, Quaternion.LookRotation(a.Vel), Vector3.one));
            for (int i = 0; i < stuckCount; i++)
            {
                ref var s = ref stuck[i];
                if (now - s.Born > ArrowStays) continue;
                if (s.Parent != null) { if (s.Parent.lossyScale.x > 0.01f) Add(Matrix4x4.TRS(s.Parent.TransformPoint(s.LocalPos), s.Parent.rotation * s.LocalRot, Vector3.one)); }
                else Add(Matrix4x4.TRS(s.Pos, s.Rot, Vector3.one));
            }
            if (n > 0) Flush(mesh, mat, n);

            // The feathers, as a second pass on the same matrices: cut-out material.
            n = 0; mesh = Fx.FletchMesh; mat = Fx.FletchMaterial;
            foreach (var a in flying)
                if (a.Vel.sqrMagnitude > 0.01f) Add(Matrix4x4.TRS(a.Pos - a.Vel.normalized * 0.56f, Quaternion.LookRotation(a.Vel), Vector3.one));
            for (int i = 0; i < stuckCount; i++)
            {
                ref var s = ref stuck[i];
                if (now - s.Born > ArrowStays) continue;
                if (s.Parent != null) { if (s.Parent.lossyScale.x > 0.01f) Add(Matrix4x4.TRS(s.Parent.TransformPoint(s.LocalPos), s.Parent.rotation * s.LocalRot, Vector3.one)); }
                else Add(Matrix4x4.TRS(s.Pos, s.Rot, Vector3.one));
            }
            if (n > 0) Flush(mesh, mat, n);
        }

        void Flush(Mesh mesh, Material mat, int count)
        {
            Graphics.DrawMeshInstanced(mesh, 0, mat, batch, count, null, UnityEngine.Rendering.ShadowCastingMode.Off, true);
        }
    }
}
