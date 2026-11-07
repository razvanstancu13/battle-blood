using System.Collections.Generic;
using UnityEngine;

namespace BattleBlood
{
    /// <summary>The soldiers' minds. Think() runs a few times a second per unit, staggered; Steer() every frame applies the last decision.</summary>
    public partial class Unit
    {
        public Unit Target;
        float nextThink;
        Vector3 goal;                     // where the brain wants to be
        float goalSpeed;
        bool hasGoal;
        float aiAttackDelay, aiBlockUntil, aiDrawTarget, panicUntil;

        // ---- orders from the commander (RTS mode) ----
        public enum OrderKind { None, Move, Attack, AttackMove, Hold }
        public OrderKind Order;
        public Vector3 OrderPos, OrderFacing = Vector3.forward;
        public Unit OrderTarget;
        public bool Selected;
        const float HoldLeash = 22f;      // how far a holding man chases before he goes back to his place

        public void GiveOrder(OrderKind kind, Vector3 pos, Vector3 facing, Unit target = null)
        {
            Order = kind; OrderPos = pos; OrderTarget = target;
            if (facing.sqrMagnitude > 0.01f) OrderFacing = Mathx.Flat(facing).normalized;
            nextThink = 0f;                // act on it this frame
            if (kind == OrderKind.Move) DropTarget();
        }

        void DropTarget()
        {
            if (Target != null) Target.AttackersOnMe = Mathf.Max(0, Target.AttackersOnMe - 1);
            Target = null;
        }

        /// <summary>Applies the order. True when it decided everything; false when the man should fight his Target as he would on his own.</summary>
        bool ThinkOrder()
        {
            float range = Type == UnitType.Archer ? 60f : Type == UnitType.Cavalry ? 18f : 14f;
            switch (Order)
            {
                case OrderKind.Move:
                {
                    if (Mathx.Flat(OrderPos - Pos).magnitude < (Type == UnitType.Cavalry ? 2.5f : 1.2f)) { Order = OrderKind.Hold; return ThinkOrder(); }
                    goal = OrderPos; goalSpeed = S.Run; hasGoal = true;
                    return true;
                }
                case OrderKind.Attack:
                    if (OrderTarget != null && OrderTarget.Alive)
                    {
                        OrderPos = OrderTarget.Pos;
                        if (Target != OrderTarget) { DropTarget(); Target = OrderTarget; Target.AttackersOnMe++; }
                        return false;
                    }
                    // The man he was sent for is dead: carry on into whoever is there.
                    Order = OrderKind.AttackMove; OrderTarget = null;
                    return ThinkOrder();
                case OrderKind.AttackMove:
                {
                    if (Target != null && Target.Alive && Mathx.Flat(Target.Pos - Pos).magnitude < range * 1.3f) return false;
                    if (PickTargetNear(Pos, range)) return false;
                    if (Mathx.Flat(OrderPos - Pos).magnitude < 2f) { Order = OrderKind.Hold; return ThinkOrder(); }
                    DropTarget();
                    goal = OrderPos; goalSpeed = S.Run; hasGoal = true;
                    return true;
                }
                case OrderKind.Hold:
                {
                    // Fight what comes near his place; never chase it far from there.
                    if (Target != null && Target.Alive && Mathx.Flat(Target.Pos - OrderPos).magnitude < HoldLeash + range * 0.3f) return false;
                    if (PickTargetNear(OrderPos, Type == UnitType.Archer ? range : HoldLeash)) return false;
                    DropTarget();
                    float d = Mathx.Flat(OrderPos - Pos).magnitude;
                    goal = OrderPos; goalSpeed = d > 2f ? S.Run * 0.8f : 0f; hasGoal = d > 0.6f;
                    return true;
                }
            }
            return false;
        }

        bool PickTargetNear(Vector3 centre, float radius)
        {
            Unit best = null;
            float bestScore = float.MaxValue;
            foreach (var u in Battle.I.Army(1 - Team))
            {
                if (!u.Alive || Mathx.Flat(u.Pos - centre).sqrMagnitude > radius * radius) continue;
                float score = Vector3.Distance(Pos, u.Pos) + u.AttackersOnMe * 4f;
                if (score < bestScore) { bestScore = score; best = u; }
            }
            if (best == null) return false;
            if (best != Target) { DropTarget(); Target = best; best.AttackersOnMe++; }
            return true;
        }
        static readonly List<Unit> Near = new(32);

        /// <summary>Called for every computer-controlled unit every frame. Decisions are spaced out; aim and attacks are not.</summary>
        /// <summary>Practice: 0 fights, 1 stands still facing the player, 2 stands and holds up his shield.</summary>
        public int Passive;

        public void Brain(float dt, float now, Battle.Phase phase)
        {
            if (!Alive) return;
            if (Passive != 0)
            {
                Wish = Vector3.zero; WishSpeed = 0f;
                var p = Battle.I.Player;
                if (p != null && p != this) Aim = (p.ChestPos - ChestPos).normalized;
                WantBlock = Passive == 2;
                return;
            }
            if (now >= nextThink)
            {
                nextThink = now + Random.Range(0.18f, 0.32f);
                Think(now, phase);
            }
            Steer(dt, now, phase);
        }

        void Think(float now, Battle.Phase phase)
        {
            if (phase == Battle.Phase.Deploy)
            {
                // Before the start: still, or walking to the place he was sent. Never a fight.
                if (Order == OrderKind.Move || Order == OrderKind.Hold)
                {
                    float d = Mathx.Flat(OrderPos - Pos).magnitude;
                    hasGoal = d > (Type == UnitType.Cavalry ? 2.5f : 1f);
                    goal = OrderPos; goalSpeed = d > 8f ? S.Run * 0.75f : S.Walk;
                    if (!hasGoal) Order = OrderKind.Hold;
                }
                else hasGoal = false;
                return;
            }
            if (phase == Battle.Phase.Over) { hasGoal = false; return; }

            // Burning men sometimes lose their heads, and those with water near make for it.
            if (Burning && now > panicUntil && Random.value < 0.08f) panicUntil = now + Random.Range(1.2f, 2.6f);

            if (Order != OrderKind.None)
            {
                if (ThinkOrder()) return;
            }
            // One man until he is down; only archers move their aim about. A target run off far out of reach is given up.
            else if (Target == null || !Target.Alive || (Type == UnitType.Archer ? Random.value < 0.12f : Mathx.Flat(Target.Pos - Pos).magnitude > LostTarget)) PickTarget();
            if (Target == null) { hasGoal = false; return; }
            // Holding archers shoot from where they stand.
            if (Order == OrderKind.Hold && Type == UnitType.Archer) { goal = OrderPos; hasGoal = Mathx.Flat(OrderPos - Pos).magnitude > 1f; goalSpeed = S.Run * 0.8f; return; }

            switch (Type)
            {
                case UnitType.Infantry: ThinkInfantry(now); break;
                case UnitType.Archer: ThinkArcher(now); break;
                case UnitType.Cavalry: ThinkCavalry(now, phase); break;
            }
        }

        const float LostTarget = 70f;

        /// <summary>
        /// Who to go for, by type. Infantry: an enemy man or horse that is on our archers, else the enemy's foot, pushing forward.
        /// Archers: whoever is in range, any kind alike. Cavalry: the enemy's archers above all, reached round the flank.
        /// </summary>
        void PickTarget()
        {
            if (Target != null) Target.AttackersOnMe = Mathf.Max(0, Target.AttackersOnMe - 1);
            Unit best = null;
            float bestScore = float.MaxValue;
            var friends = Battle.I.Army(Team);
            foreach (var u in Battle.I.Army(1 - Team))
            {
                if (!u.Alive) continue;
                float d = Vector3.Distance(Pos, u.Pos);
                float score = d + u.AttackersOnMe * 5f + Random.Range(0f, 5f);
                switch (Type)
                {
                    case UnitType.Infantry:
                        if (u.Type != UnitType.Archer && d < 50f && NearOurArchers(u, friends, 16f)) score -= 40f;      // guard the bowmen first
                        else if (u.Type == UnitType.Infantry) score -= 12f;                                            // then into their foot
                        else if (u.Type == UnitType.Archer) score += 6f;
                        break;
                    case UnitType.Archer:
                        if (d > 72f) score += 200f;          // out of bow range; otherwise every kind is the same to him
                        // Not into the press: a man already at grips with one of ours is left to him, if there is anyone else to shoot.
                        if (u.Target != null && u.Target.Team == Team && Mathx.Flat(u.Target.Pos - u.Pos).sqrMagnitude < 16f) score += 60f;
                        break;
                    case UnitType.Cavalry:
                        if (u.Type == UnitType.Archer) score -= 45f;
                        if (u.Blocking) score += 10f;
                        break;
                }
                if (u.IsPlayer && Battle.HumanControlled) score -= 6f;       // the battle notices you, when it is you
                if (score < bestScore) { bestScore = score; best = u; }
            }
            Target = best;
            if (Target != null) Target.AttackersOnMe++;
        }

        static bool NearOurArchers(Unit enemy, List<Unit> friends, float radius)
        {
            float r2 = radius * radius;
            foreach (var f in friends)
                if (f.Alive && f.Type == UnitType.Archer && Mathx.Flat(f.Pos - enemy.Pos).sqrMagnitude < r2) return true;
            return false;
        }

        /// <summary>A horseman going for the archers does not ride into the front of their foot: he swings out round the end of their line first.</summary>
        Vector3 FlankWaypoint(Vector3 target)
        {
            var b = Battle.I;
            Vector3 line = b.InfantryCentre(1 - Team, out float halfWidth);
            if (halfWidth <= 0f) return target;
            float towardsEnemy = Team == Blue ? 1f : -1f;
            // Already level with or past their foot, or out beyond its end: straight in.
            bool past = (Pos.z - line.z) * towardsEnemy > -4f;
            float side = Pos.x - line.x;
            if (past || Mathf.Abs(side) > halfWidth + 12f) return target;
            float sx = Mathf.Abs(side) > 1f ? Mathf.Sign(side) : (Id % 2 == 0 ? 1f : -1f);
            return new Vector3(line.x + sx * (halfWidth + 14f), 0, line.z - towardsEnemy * 6f);
        }

        void ThinkInfantry(float now)
        {
            float d = Vector3.Distance(Pos, Target.Pos);
            goal = Target.Pos;
            hasGoal = true;
            // Advance in order, then run the last stretch.
            goalSpeed = d > 60f ? S.Run * 0.8f : d > 10f ? S.Run : S.Sprint * 0.85f;
            if (d < S.Reach + 0.2f + Target.S.Radius)
            {
                goalSpeed = 0.8f;
                if (Random.value < 0.5f) goal = Pos + Vector3.Cross(Vector3.up, (Target.Pos - Pos).normalized) * (Random.value < 0.5f ? 2f : -2f);    // circle
                else goal = Target.Pos;
            }
        }

        void ThinkArcher(float now)
        {
            float d = Vector3.Distance(Pos, Target.Pos);
            hasGoal = true;
            if (d < 9f)
            {
                // Too close: break away from it.
                goal = Pos + (Pos - Target.Pos).normalized * 12f;
                goalSpeed = S.Sprint;
            }
            else if (d > 42f) { goal = Target.Pos; goalSpeed = S.Run * 0.8f; hillSpot = null; }
            else
            {
                // In range: shoot from the highest ground near at hand, never uphill if it can be helped.
                if (now >= nextHillLook) { nextHillLook = now + Random.Range(3.5f, 5f); hillSpot = HigherGround(); }
                if (hillSpot.HasValue && Mathx.Flat(hillSpot.Value - Pos).magnitude > 1.2f) { goal = hillSpot.Value; goalSpeed = S.Run * 0.85f; }
                else { hillSpot = null; goal = Pos; goalSpeed = 0f; hasGoal = false; }
            }
        }

        Vector3? hillSpot; float nextHillLook;
        public static int AiShots, AiShotsUphill;       // for the test: how often a computer archer still shoots uphill
        bool GoingUpHill => hillSpot.HasValue && Mathx.Flat(hillSpot.Value - Pos).magnitude > 1.2f;

        /// <summary>A spot within 30 m that is at least a metre higher (0.6 m when he stands below his man) than here, dry, and still a fair shot at his man (15 to 60 m);
        /// better the higher it is, above all above the man he is shooting at. Null if none is worth the walk.</summary>
        Vector3? HigherGround()
        {
            var f = Battle.I.Field;
            float here = f.Height(Pos.x, Pos.z), theirs = f.Height(Target.Pos.x, Target.Pos.z);
            float Score(float h, float walk) => h + Mathf.Min(0f, h - theirs) * 0.8f - walk * 0.06f;
            float best = Score(here, 0f) + (here < theirs ? 0.6f : 1f);      // below him, even a little rise is worth it
            Vector3? spot = null;
            float spin = Id * 0.37f;
            for (float r = 6f; r <= 30f; r += 6f)
                for (int i = 0; i < 12; i++)
                {
                    float a = spin + i * Mathf.PI / 6f;
                    Vector3 p = Pos + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                    if (f.InWater(p.x, p.z)) continue;
                    float dt = Mathx.Flat(Target.Pos - p).magnitude;
                    if (dt < 15f || dt > 60f) continue;
                    float h = f.Height(p.x, p.z), sc = Score(h, r);
                    if (sc > best) { best = sc; spot = new Vector3(p.x, h, p.z); }
                }
            return spot;
        }

        // Cavalry: charge in, fight from the saddle on the move, and ride out after two or three cuts that landed, when hemmed in,
        // or when a fight drags on; then wheel and come again. They never stand.
        bool ridingOut; Vector3 rideOutGoal; float rideOutUntil, engagedSince = -1f;
        public bool CavalryEngaged { get; private set; }
        static readonly List<Unit> crowd = new();

        void ThinkCavalry(float now, Battle.Phase phase)
        {
            hasGoal = true; CavalryEngaged = false;
            if (ridingOut)
            {
                goal = rideOutGoal; goalSpeed = S.Run * 0.85f;
                if (Mathx.Flat(rideOutGoal - Pos).magnitude < 6f || now > rideOutUntil) ridingOut = false;
                return;
            }
            Vector3 toT = Mathx.Flat(Target.Pos - Pos);
            float d = toT.magnitude;
            Vector3 dir = d > 0.01f ? toT / d : Forward;
            float reach = S.Reach + Target.S.Radius;
            if (d > 22f)
            {
                // The approach: round the end of their foot, at a gallop, charging the last of it.
                engagedSince = -1f;
                goal = FlankWaypoint(Target.Pos);
                goalSpeed = S.Run;
                if (d < 45f && ChargeCooldown <= 0f && phase != Battle.Phase.Deploy) BeginCharge();
                return;
            }
            if (d > reach + 3f)
            {
                // Riding in on him.
                goal = Target.Pos; goalSpeed = d < 10f ? Mathf.Max(S.Run * 0.55f, 6f) : S.Run * 0.8f;
                return;
            }
            // In the fight: circling him at a trot, cutting as he comes round.
            CavalryEngaged = true;
            if (engagedSince < 0f) engagedSince = now;
            Vector3 right = Vector3.Cross(Vector3.up, dir);
            float spin = (Id & 1) == 0 ? 1f : -1f;
            goal = Target.Pos - dir * reach * 0.85f + right * spin * 3f;
            goalSpeed = 3.2f;
            int near = 0;
            Battle.I.Query(Pos, 5f, crowd);
            foreach (var u in crowd) if (u.Alive && u.Team != Team && u.Type != UnitType.Archer) near++;      // bowmen about him are prey, not a threat
            int enough = 2 + (Id % 2);
            if (HitsThisRun >= enough || near >= 3 || now - engagedSince > 8f)
            {
                // Out, away from the press and back round towards the clash, a short ride, then in again.
                Vector3 c = Battle.I.ClashCentre;
                Vector3 away = Mathx.Flat(Pos - c); away = away.sqrMagnitude > 1f ? away.normalized : -dir;
                Vector3 on = (Mathx.Flat(Forward).normalized + away * 0.8f + Vector3.Cross(Vector3.up, away) * spin * 0.6f).normalized;
                rideOutGoal = Pos + on * 16f;
                rideOutUntil = now + 3.5f;
                ridingOut = true; HitsThisRun = 0; engagedSince = -1f;
            }
        }

        void Steer(float dt, float now, Battle.Phase phase)
        {
            Wish = Vector3.zero; WishSpeed = 0f;
            if (phase == Battle.Phase.Deploy || phase == Battle.Phase.Over)
            {
                WantBlock = false;
                if (phase == Battle.Phase.Deploy)
                {
                    if (hasGoal) { Wish = Mathx.Flat(goal - Pos).normalized; WishSpeed = goalSpeed; Wish += Avoid(); Aim = Wish.normalized; }
                    else Aim = Order == OrderKind.Hold ? OrderFacing : Team == Blue ? Vector3.forward : Vector3.back;
                }
                return;
            }

            if (now < panicUntil)
            {
                Wish = Mathx.FromYaw(Yaw + Mathf.Sin(now * 3.1f + Id) * 80f);
                WishSpeed = S.Sprint;
                WantBlock = false;
                if (Action == Act.Draw) { Action = Act.None; DrawPower = 0f; FireNocked = false; }
                return;
            }

            if (hasGoal)
            {
                Vector3 to = Mathx.Flat(goal - Pos);
                if (to.sqrMagnitude > 0.25f || Type == UnitType.Cavalry)
                {
                    Wish = to.normalized;
                    WishSpeed = goalSpeed;
                    // Nobody runs a ruler-straight line: a slow weave and a lean of his own, and his own pace, fading out as he closes.
                    if (Order != OrderKind.Move && Order != OrderKind.Hold)
                    {
                        float h = Mathf.Repeat(Id * 0.6180339f, 1f), h2 = Mathf.Repeat(Id * 0.3819661f + 0.37f, 1f);
                        float far = Mathf.Clamp01((to.magnitude - 5f) / 25f);
                        float weave = Mathf.Sin(now * (0.35f + h * 0.5f) + h * 40f) * 0.28f + (h2 - 0.5f) * 0.4f;
                        Wish = (Wish + Vector3.Cross(Vector3.up, Wish) * weave * far).normalized;
                        WishSpeed *= Mathf.Lerp(1f, 0.9f + h2 * 0.2f, far);
                    }
                }
                // A horseman left to himself never stands: at least a canter.
                if (Type == UnitType.Cavalry && Order != OrderKind.Hold && Order != OrderKind.Move) WishSpeed = Mathf.Max(WishSpeed, CavalryEngaged ? 2.6f : Mathf.Max(S.Run * 0.55f, 6f));
                // Hold formation early on: keep off friends in front, feeling rather than pathfinding.
                Wish += Avoid();
            }

            if (Target == null || !Target.Alive || Order == OrderKind.Move)
            {
                WantBlock = false;
                // Arrived and waiting: face the way he was told.
                if (Order == OrderKind.Hold && !hasGoal) Aim = OrderFacing;
                else if (Wish.sqrMagnitude > 0.01f) Aim = Wish.normalized;
                return;
            }
            Vector3 toT = Target.Pos - Pos;
            float dist = Mathx.Flat(toT).magnitude;

            switch (Type)
            {
                case UnitType.Infantry: FightInfantry(dist, toT, now, dt); break;
                case UnitType.Archer: FightArcher(dist, toT, now, dt); break;
                case UnitType.Cavalry: FightCavalry(dist, toT, now); break;
            }
        }

        Vector3 Avoid()
        {
            var field = Battle.I.Field;
            Vector3 ahead = Pos + Forward * (Type == UnitType.Cavalry ? 5f : 2.2f);
            Vector3 p = ahead;
            if (field.PushOut(ref p, S.Radius + 0.5f)) return (p - ahead) * 0.7f;
            return Vector3.zero;
        }

        void FightInfantry(float dist, Vector3 toT, float now, float dt)
        {
            Aim = Vector3.Lerp(Aim, toT.normalized, dt * 9f);
            float range = S.Reach + Target.S.Radius + 0.1f;
            // Raise the shield when someone in front of him is about to bring a blade down, or when arrows come in.
            bool threat = Target.Action == Act.Swing && dist < range + 0.8f && Target.ActT < Target.ActDur * 0.5f;
            if (threat && now > aiBlockUntil && Random.value < 0.012f + dt * 3.2f && !LeftArmGone) aiBlockUntil = now + Random.Range(0.45f, 1.0f);
            WantBlock = now < aiBlockUntil;

            if (dist > range) return;
            if (now < aiAttackDelay || !CanAct || Blocking) return;
            aiAttackDelay = now + Random.Range(0.55f, 1.3f);
            if (BashCooldown <= 0f && Target.Blocking && Random.value < 0.55f) BeginBash();
            else if (BashCooldown <= 0f && Random.value < 0.08f) BeginBash();
            else BeginSwing(Random.Range(0, Moves.Swings.Length));
        }

        void FightArcher(float dist, Vector3 toT, float now, float dt)
        {
            if (dist > 70f || dist < 9f) return;
            Vector3 shoot = BallisticAim(dist);
            Aim = Vector3.Lerp(Aim, shoot, dt * 6f);
            // Lit arrows whenever the slow reload allows, at massed infantry above all.
            // With the enemy close he is rushed: a short draw (a weaker arrow), a wilder aim and a longer fumble between shots.
            float rushed = 1f - Mathf.InverseLerp(10f, 25f, dist);
            if (CanAct && now >= aiAttackDelay && !GoingUpHill)
            {
                bool fire = FireReload <= 0f && Random.value < Balance.ArcherLitChance * (1f - rushed);
                aiDrawTarget = Mathf.Lerp(Random.Range(0.72f, 1f), Random.Range(0.35f, 0.55f), rushed);
                BeginNock(fire);
                aiAttackDelay = now + Random.Range(Balance.ArcherPauseMin, Balance.ArcherPauseMax) * (1f + rushed * 0.8f);
            }
            if (Action == Act.Draw && DrawPower >= aiDrawTarget)
            {
                // A soldier's aim is honest, not perfect: the further the wider.
                float spread = Balance.ArcherSpreadBase + dist * Balance.ArcherSpreadPerMetre + (Target.Speed > 6f ? 1.5f : 0f) + rushed * 5f;
                Vector3 d = Quaternion.Euler(Random.Range(-spread, spread), Random.Range(-spread, spread), 0) * shoot;
                Release(d);
                AiShots++; if (Target.Pos.y - Pos.y > 1f) AiShotsUphill++;
                if (Order == OrderKind.None && Random.value < 0.45f) PickTarget();      // the next arrow may go elsewhere
            }
        }

        /// <summary>Where to point the bow: leading the target, and raised enough for the arrow's fall.</summary>
        Vector3 BallisticAim(float dist)
        {
            float speed = Mathf.Lerp(26f, 66f, Mathf.Max(DrawPower, 0.85f));
            float flight = dist / speed;
            Vector3 lead = Target.Pos + Target.Vel * flight + Vector3.up * (Target.AimHeight * 0.75f);
            Vector3 from = ChestPos;
            Vector3 to = lead - from;
            float x = Mathx.Flat(to).magnitude, y = to.y;
            float g = 9.81f, v2 = speed * speed;
            float root = v2 * v2 - g * (g * x * x + 2f * y * v2);
            float angle = root > 0f ? Mathf.Atan2(v2 - Mathf.Sqrt(root), g * x) : 45f * Mathf.Deg2Rad;
            Vector3 flat = Mathx.Flat(to).normalized;
            return (flat * Mathf.Cos(angle) + Vector3.up * Mathf.Sin(angle)).normalized;
        }

        void FightCavalry(float dist, Vector3 toT, float now)
        {
            Aim = toT.normalized;
            // The cut starts early enough to land as he goes by: the blade falls about 0.7 s in, by when the horse has moved on.
            float lead = Speed * 0.55f;
            bool ahead = Vector3.Dot(Mathx.Flat(toT).normalized, Forward) > 0.2f;
            if (dist < S.Reach + Target.S.Radius + 0.3f + (ahead ? lead : 0f) && CanAct && now >= aiAttackDelay)
            {
                aiAttackDelay = now + 1.7f;
                BeginSwing(Random.Range(0, 2));
            }
        }
    }
}
