using UnityEngine;

namespace BattleBlood
{
    /// <summary>One cut of the sword: the hand's path and the blade's direction in the chest's frame (x right, y up, z forward).</summary>
    public struct SwingDef
    {
        public Vector3 A, B, C, BladeA, BladeB, BladeC;
        public float Duration, Strike, Twist, DamageMul, Arc, ReachMul;
        public string Name, Clip;       // Clip: the motion-captured cut (tools/mixamo_clips.json)
    }

    public static class Moves
    {
        public static readonly SwingDef[] Swings =
        {
            new SwingDef { Name = "slash", Clip = "SSSlash1", Duration = 0.8f, Strike = 0.5f, Twist = -58f, DamageMul = 1f, Arc = 100f, ReachMul = 1f,
                A = new(0.62f, 0.40f, -0.18f), B = new(0.04f, 0.10f, 0.66f), C = new(-0.60f, -0.14f, 0.22f),
                BladeA = new(0.85f, 0.45f, -0.45f), BladeB = new(-0.1f, 0.05f, 1f), BladeC = new(-1f, -0.15f, -0.1f) },
            new SwingDef { Name = "backhand", Clip = "SSSlash3", Duration = 0.8f, Strike = 0.62f, Twist = 52f, DamageMul = 0.9f, Arc = 100f, ReachMul = 1f,
                A = new(-0.48f, 0.32f, 0.10f), B = new(0.12f, 0.12f, 0.66f), C = new(0.68f, -0.04f, 0.06f),
                BladeA = new(-0.95f, 0.4f, -0.2f), BladeB = new(0.15f, 0.05f, 1f), BladeC = new(1f, -0.2f, -0.15f) },
        };
    }

    /// <summary>A two-bone chain and what the T-pose says about its hand.</summary>
    public class Limb
    {
        public readonly Transform Upper, Lower, Hand, Finger, Thumb;
        public readonly float UpperLen, LowerLen;
        readonly Vector3 thumbLocal, backLocal;
        readonly Quaternion handRest;
        // The way the joint folds, in the upper bone's own space: the forearm swings forward from an arm held out in the T-pose,
        // the shin swings back from a straight leg.
        readonly Vector3 bendLocal;

        public Limb(Transform upper, Transform lower, Transform hand, Transform finger, Transform thumb, Transform root)
        {
            Upper = upper; Lower = lower; Hand = hand; Finger = finger; Thumb = thumb;
            UpperLen = Vector3.Distance(upper.position, lower.position);
            LowerLen = Vector3.Distance(lower.position, hand.position);
            handRest = hand.localRotation;
            bendLocal = upper.InverseTransformDirection(finger != null ? root.forward : -root.forward);
            if (finger != null)
            {
                Vector3 fingers = (finger.position - hand.position).normalized;
                Vector3 t = thumb != null ? Vector3.ProjectOnPlane(thumb.position - hand.position, fingers).normalized : root.forward;
                thumbLocal = hand.InverseTransformDirection(t);
                backLocal = hand.InverseTransformDirection(Vector3.Cross(t, fingers) * Mathf.Sign(Vector3.Dot(Vector3.Cross(t, fingers), Vector3.up) + 1e-4f));
            }
        }

        /// <summary>The hand turned so the thumb side points along thumb (a sword's blade) and the back of the hand towards back.</summary>
        public Quaternion HandRotation(Vector3 thumb, Vector3 back)
            => Quaternion.LookRotation(thumb, back) * Quaternion.Inverse(Quaternion.LookRotation(thumbLocal, backLocal));

        // The fingers, each joint with the axis it bends on towards the palm, measured once in the T-pose.
        readonly System.Collections.Generic.List<(Transform bone, Vector3 axisLocal, bool thumb)> fingers = new();

        /// <summary>Collects the finger joints under the hand (named ...Index1, ...Middle2, ...Thumb3 and so on) and how each one bends.</summary>
        public void FindFingers()
        {
            if (Finger == null) return;
            Vector3 palm = -Hand.TransformDirection(backLocal);       // the way the palm faces
            foreach (var t in Hand.GetComponentsInChildren<Transform>(true))
            {
                if (t == Hand) continue;
                string n = t.name;
                bool thumb = n.Contains("Thumb");
                if (!(thumb || n.Contains("Index") || n.Contains("Middle") || n.Contains("Ring") || n.Contains("Pinky"))) continue;
                if (char.IsDigit(n[n.Length - 1]) == false) continue;
                Vector3 along = t.childCount > 0 ? t.GetChild(0).position - t.position : t.position - t.parent.position;
                if (along.sqrMagnitude < 1e-8f) continue;
                Vector3 axis = Vector3.Cross(along.normalized, palm);
                if (axis.sqrMagnitude < 1e-6f) continue;
                fingers.Add((t, t.InverseTransformDirection(axis.normalized), thumb));
            }
        }

        /// <summary>Closes the hand: degrees per finger joint (a fist is about 75), the thumb by its own amount.</summary>
        public void Curl(float degrees, float thumbDegrees, float weight)
        {
            if (weight <= 0.001f) return;
            foreach (var f in fingers)
            {
                float a = (f.thumb ? thumbDegrees : degrees) * weight;
                if (Mathf.Abs(a) < 0.01f) continue;
                f.bone.rotation = Quaternion.AngleAxis(a, f.bone.TransformDirection(f.axisLocal)) * f.bone.rotation;
            }
        }

        /// <summary>
        /// After the hand has been turned: the forearm takes a share of that turn about its own length, as a real forearm rolls,
        /// so the skin at the wrist is not wrung. The hand keeps the rotation it was given.
        /// </summary>
        public void ShareTwist(float share)
        {
            Quaternion hand = Hand.rotation;
            Vector3 axis = (Hand.position - Lower.position).normalized;
            if (axis.sqrMagnitude < 1e-6f) return;
            Quaternion rel = Quaternion.Inverse(Lower.rotation) * hand;          // the hand in the forearm's frame
            Vector3 a = Quaternion.Inverse(Lower.rotation) * axis;
            Vector3 v = new(rel.x, rel.y, rel.z);
            Vector3 p = Vector3.Project(v, a);
            var twist = new Quaternion(p.x, p.y, p.z, rel.w);
            float m = Mathf.Sqrt(twist.x * twist.x + twist.y * twist.y + twist.z * twist.z + twist.w * twist.w);
            if (m < 1e-6f) return;
            twist = new Quaternion(twist.x / m, twist.y / m, twist.z / m, twist.w / m);
            twist.ToAngleAxis(out float ang, out Vector3 ax);
            if (ang > 180f) ang -= 360f;
            if (Vector3.Dot(ax, a) < 0f) ang = -ang;
            Lower.rotation = Quaternion.AngleAxis(ang * share, axis) * Lower.rotation;
            Hand.rotation = hand;
        }

        /// <summary>The way the back of the hand faces now.</summary>
        public Vector3 BackOfHand => Hand.TransformDirection(backLocal).normalized;

        /// <summary>Middle of the palm: where a held grip sits.</summary>
        public Vector3 Palm => Finger != null ? Vector3.Lerp(Hand.position, Finger.position, 0.7f) : Hand.position;

        public void Solve(Vector3 target, Vector3 pole, float weight)
        {
            if (weight <= 0.001f) return;
            Vector3 root = Upper.position;
            Vector3 toT = target - root;
            float reach = (UpperLen + LowerLen) * Upper.lossyScale.x / Mathf.Max(1e-4f, Upper.lossyScale.x) - 0.002f;
            float d = Mathf.Clamp(toT.magnitude, 0.05f, reach);
            Vector3 dir = toT.normalized;
            target = root + dir * d;

            float along = (UpperLen * UpperLen - LowerLen * LowerLen + d * d) / (2f * d);
            float h = Mathf.Sqrt(Mathf.Max(0f, UpperLen * UpperLen - along * along));
            Vector3 side = Vector3.ProjectOnPlane(pole, dir);
            if (side.sqrMagnitude < 1e-5f) side = Vector3.ProjectOnPlane(Vector3.down, dir);
            Vector3 elbow = root + dir * along + side.normalized * h;

            Quaternion upper = Quaternion.FromToRotation(Lower.position - root, elbow - root) * Upper.rotation;
            // Roll the upper bone about its own length so its hinge folds towards the hand: the elbow (or knee) then bends
            // the way a real one does, not twisted round to the wrong side.
            Vector3 armDir = (elbow - root).normalized;
            Vector3 wantBend = Vector3.ProjectOnPlane(target - elbow, armDir);
            if (wantBend.sqrMagnitude > 1e-5f)
            {
                Vector3 haveBend = Vector3.ProjectOnPlane(upper * bendLocal, armDir);
                if (haveBend.sqrMagnitude > 1e-6f) upper = Quaternion.AngleAxis(Vector3.SignedAngle(haveBend, wantBend, armDir), armDir) * upper;
            }
            Upper.rotation = Quaternion.Slerp(Upper.rotation, upper, weight);
            Quaternion lower = Quaternion.FromToRotation(Hand.position - Lower.position, target - Lower.position) * Lower.rotation;
            Lower.rotation = Quaternion.Slerp(Lower.rotation, lower, weight);
        }
    }

    public partial class Unit
    {
        float ikWeight = 1f;
        float recoil;

        /// <summary>After the animators have run. Near units get their arms placed by IK; the far ones keep what the clips give them.</summary>
        public void LatePose(float dt)
        {
            if (severed.Count > 0)
                for (int i = 0; i < severed.Count; i++) severed[i].localScale = Vector3.one * 0.001f;

            if (Type == UnitType.Cavalry) { PlaceRider(dt); if (CamDist < 90f) tail?.Tick(dt, Alive); }
            if (!Alive) return;

            ikWeight = Mathf.MoveTowards(ikWeight, NearCamera ? 1f : 0f, dt * 4f);
            if (Type == UnitType.Archer)
            {
                Vector3 fa = Mathx.Flat(Aim).sqrMagnitude > 1e-3f ? Mathx.Flat(Aim).normalized : Mathx.FromYaw(Yaw);
                PoseArcher(fa, Vector3.Cross(Vector3.up, fa), Vector3.up, Mathf.Asin(Mathf.Clamp(Aim.normalized.y, -1f, 1f)) * Mathf.Rad2Deg, dt);
                return;
            }
            if (ikWeight <= 0.001f) { UpdateBowFar(); return; }

            Vector3 F = Mathx.FromYaw(Yaw);
            Vector3 flatAim = Mathx.Flat(Aim);
            if (flatAim.sqrMagnitude > 1e-3f && Type != UnitType.Cavalry) F = flatAim.normalized;
            Vector3 U = Vector3.up, R = Vector3.Cross(U, F);
            float pitch = Mathf.Asin(Mathf.Clamp(Aim.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;

            recoil = Mathf.MoveTowards(recoil, 0f, dt * 5f);
            if (Type == UnitType.Archer) PoseArcher(F, R, U, pitch, dt);
            else PoseSwordsman(F, R, U, pitch, dt);
        }

        Vector3 Local(Vector3 chest, Vector3 F, Vector3 R, Vector3 U, Vector3 p) => chest + R * p.x + U * p.y + F * p.z;

        /// <summary>
        /// Infantry and cavalry move by Mixamo's sword-and-shield motion capture (cuts, block, hits); this only finishes it off:
        /// a slight lean of the chest into the aim's height, the shield strapped to the forearm, the shield punch (no clip for it),
        /// the rider's left fist on the reins, and fists round the sword grip and the shield strap.
        /// </summary>
        float holdW, carryW;

        void PoseSwordsman(Vector3 F, Vector3 R, Vector3 U, float pitch, float dt)
        {
            if (spine2 != null) spine2.rotation = Quaternion.AngleAxis(pitch * 0.25f * ikWeight, R) * spine2.rotation;
            Vector3 chest = spine2.position;
            if (Type == UnitType.Cavalry)
            {
                // The riding clip lets the sword arm hang: between cuts the sword is held up before him, blade raised.
                holdW = Mathf.MoveTowards(holdW, Action == Act.Swing ? 0f : 1f, dt * (Action == Act.Swing ? 8f : 3f));
                float w = holdW * ikWeight;
                if (w > 0.001f)
                {
                    Vector3 blade = (R * 0.15f + U * 0.85f + F * 0.5f).normalized;
                    Vector3 back = Vector3.ProjectOnPlane(R * 0.8f + F * 0.3f, blade).normalized;
                    armR.Solve(Local(chest, F, R, U, new Vector3(0.26f, -0.14f, 0.32f)), R + Vector3.down * 0.9f - F * 0.2f, w);
                    armR.Hand.rotation = Quaternion.Slerp(armR.Hand.rotation, armR.HandRotation(blade, back), w);
                }
            }
            armR.Curl(70f, 30f, ikWeight);
            if (Type == UnitType.Infantry && !leftArmGone)
            {
                if (Action == Act.Bash)
                {
                    // The shield punched straight out from the chest and back.
                    float t = Mathf.Clamp01(ActT / ActDur);
                    float punch = t < 0.35f ? t / 0.35f : 1f - (t - 0.35f) / 0.65f;
                    Vector3 target = Vector3.Lerp(new Vector3(-0.1f, 0.08f, 0.38f), new Vector3(-0.05f, 0.1f, 0.68f), punch);
                    armL.Solve(Local(chest, F, R, U, target), -R * 1.2f + Vector3.down * 0.6f - F * 0.3f, ikWeight);
                    armL.Hand.rotation = Quaternion.Slerp(armL.Hand.rotation, armL.HandRotation(U, -R), ikWeight);
                }
                else
                {
                    float carry = (Blocking || Action == Act.Swing) ? 0f : Mathf.Clamp01((Speed - 2.2f) / 1.5f);
                    carryW = Mathf.MoveTowards(carryW, carry, dt * 5f);
                    if (carryW > 0.001f)
                    {
                        armL.Solve(Local(chest, F, R, U, new Vector3(-0.24f, -0.14f, 0.3f)), -R * 1.2f + Vector3.down * 0.6f - F * 0.3f, carryW * ikWeight);
                        armL.Hand.rotation = Quaternion.Slerp(armL.Hand.rotation, armL.HandRotation(U, -R), carryW * ikWeight);
                    }
                }
                armL.Curl(65f, 30f, ikWeight);
                PlaceShield(F, U);
            }
            else if (Type == UnitType.Cavalry)
            {
                // On the reins: the fist brought in front of the belly. Only the arm is placed; the hand keeps its natural angle
                // to the forearm (turning it as well wrung the wrist).
                Vector3 reins = new(-0.1f, -0.22f, 0.36f);
                armL.Solve(Local(chest, F, R, U, reins), -R * 0.8f + Vector3.down, ikWeight);
                armL.Curl(70f, 25f, ikWeight);
            }
        }

        /// <summary>
        /// The archer's shot is Mixamo motion capture (BowDraw, BowAim, BowRelease: tools/mixamo_clips.py); this only lines it up:
        /// his whole body turns so the arrow's line (string hand to bow hand) points where he aims, the spine bends for a high or low
        /// shot, and the bow, string and arrow are put in his hands.
        /// </summary>
        void PoseArcher(Vector3 F, Vector3 R, Vector3 U, float pitch, float dt)
        {
            bool aiming = Action == Act.Nock || Action == Act.Draw || Action == Act.Loose;
            aimBlend = Mathf.MoveTowards(aimBlend, aiming ? 1f : 0f, dt * (aiming ? 6f : 3f));
            Vector3 aim = Aim.sqrMagnitude > 1e-3f ? Aim.normalized : F;
            Vector3 flatAim = Mathx.Flat(aim).sqrMagnitude > 1e-4f ? Mathx.Flat(aim).normalized : F;

            // Turn the body until the shot's line lies along the aim (the clip stands him side-on; which way it points is measured, not assumed).
            Vector3 shot = Mathx.Flat(armL.Hand.position - armR.Hand.position);
            float want = aiming && shot.sqrMagnitude > 0.01f ? Vector3.SignedAngle(shot, flatAim, Vector3.up) : -shotYaw;
            shotYaw += Mathf.Clamp(want, -540f * dt, 540f * dt) * (aiming ? Mathf.Clamp01(dt * 12f) * 8f : 1f);
            shotYaw = Mathf.Clamp(shotYaw, -170f, 170f);
            Body.localRotation = Quaternion.AngleAxis(shotYaw, Vector3.up) * bodyRest;

            // High or low: the spine bends about the line across the shot.
            Vector3 across = Vector3.Cross(Vector3.up, flatAim);
            float bend = -pitch * 0.5f * aimBlend;
            if (spine1 != null) spine1.rotation = Quaternion.AngleAxis(bend, across) * spine1.rotation;
            if (spine2 != null) spine2.rotation = Quaternion.AngleAxis(bend, across) * spine2.rotation;

            // The bow hand closes round the grip; the string hand hooks the string once it has an arrow on it.
            // In the draw clip the hand fetches an arrow from the quiver first and reaches the string 45 % of the way through.
            float hook = (Action == Act.Nock || Action == Act.Draw) ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(StringAt, StringAt + 0.08f, DrawClip)) : 0f;
            bool onTheString = hook > 0.5f;
            armL.Curl(78f, 35f, 1f);
            armR.Curl(onTheString ? 48f : 25f, onTheString ? 20f : 10f, 1f);
            Vector3 fingers = armR.Finger != null ? armR.Finger.position : armR.Hand.position;
            Vector3 restMid = bow != null ? bow.TransformPoint(new Vector3(0, 0, -0.18f)) : fingers;
            Vector3 nockAt = Vector3.Lerp(restMid, fingers, hook);

            if (bow != null)
            {
                Vector3 bowFwd = (armL.Palm - nockAt);
                bowFwd = onTheString && bowFwd.sqrMagnitude > 0.01f ? bowFwd.normalized : Vector3.Slerp(F + Vector3.down * 0.5f, aim, aimBlend).normalized;
                Vector3 bowUp = Vector3.ProjectOnPlane(Vector3.up, bowFwd).normalized;
                bow.SetPositionAndRotation(armL.Palm, Quaternion.LookRotation(bowFwd, bowUp));
                UpdateString(hook > 0.01f ? nockAt : (Vector3?)null);
            }

            // The arrow on the string: from the string hand to past the bow.
            if (heldArrow != null)
            {
                bool show = onTheString;
                if (heldArrow.gameObject.activeSelf != show) heldArrow.gameObject.SetActive(show);
                if (show)
                {
                    heldArrow.position = nockAt;
                    Vector3 toBow = armL.Palm - nockAt;
                    heldArrow.rotation = Quaternion.LookRotation(toBow.sqrMagnitude > 1e-3f ? toBow.normalized : aim, U);
                    if (FireNocked) Fx.ArrowFlame(heldArrow.position + heldArrow.forward * 0.8f, 1);
                }
            }
        }
        float shotYaw;
        const float StringAt = 0.48f;     // share of the draw clip at which the string hand meets the string
        float aimBlend;
        const float ArcherStance = 30f;      // how far an archer's feet turn off the target while he shoots

        static Vector3 Bezier(Vector3 a, Vector3 c, Vector3 b, float t)
        {
            float u = 1f - t;
            return u * u * a + 2f * u * t * c + t * t * b;
        }

        /// <summary>
        /// A strapped shield: the forearm lies across its back, so its face is turned square to the forearm, towards where he looks
        /// (off to the side when at rest), and it sits a hand's width out from the arm so neither hand nor arm comes through it.
        /// </summary>
        Vector3 shieldFace;

        void PlaceShield(Vector3 F, Vector3 U)
        {
            if (shield == null) return;
            // Strapped on the forearm and always held upright: the board's top edge points straight up in every clip
            // (it used to be set square to the forearm, which laid it on its side, or flipped it, whenever the forearm pointed up or down).
            // It faces the way the back of the hand faces, turned level; with the hand pointing up or down, forward and out to his left.
            Vector3 forearm = (armL.Hand.position - armL.Lower.position).normalized;
            Vector3 left = Vector3.ProjectOnPlane(-Vector3.Cross(Vector3.up, F), Vector3.up).normalized;
            Vector3 level = Vector3.ProjectOnPlane(armL.BackOfHand, Vector3.up);
            Vector3 fallback = Vector3.ProjectOnPlane(F, Vector3.up).normalized * 0.7f + left * 0.7f;
            Vector3 want = level + fallback * Mathf.Max(0f, 0.6f - level.magnitude);
            if (want.sqrMagnitude < 1e-4f) want = fallback;
            want.Normalize();
            // Eased, so a clip that turns the wrist quickly cannot snap the board round.
            // Turned only about the vertical (a slerp between near-opposite directions can swing through straight up and tip the board over).
            if (shieldFace.sqrMagnitude < 0.5f) shieldFace = want;
            else
            {
                float turn = Vector3.SignedAngle(shieldFace, want, Vector3.up);
                shieldFace = Quaternion.AngleAxis(turn * Mathf.Clamp01(Time.deltaTime * 14f), Vector3.up) * shieldFace;
                shieldFace = Vector3.ProjectOnPlane(shieldFace, Vector3.up).normalized;
            }
            Vector3 face = shieldFace;
            // Its middle over the grip, just short of the wrist, a board's and an arm's thickness out, and further out when the hand points at the board.
            Vector3 grip = Vector3.Lerp(armL.Lower.position, armL.Hand.position, 0.6f);
            float out_ = ShieldOut + Mathf.Max(0f, Vector3.Dot(armL.Hand.position - grip, face)) + Mathf.Max(0f, Vector3.Dot(forearm, face)) * 0.05f;
            shield.SetPositionAndRotation(grip + face * out_, Quaternion.LookRotation(face, Vector3.up));
        }
        public static float ShieldOut = 0.15f;

        void UpdateString(Vector3? nock)
        {
            if (bowString == null) return;
            // In the bow's space (the string's transform is the bow's): it stays on the bow when he drops it or dies.
            Vector3 top = new(0, 0.76f, -0.18f), bottom = new(0, -0.76f, -0.18f);
            bowString.SetPosition(0, top);
            bowString.SetPosition(1, nock.HasValue ? bow.InverseTransformPoint(nock.Value) : (top + bottom) * 0.5f);
            bowString.SetPosition(2, bottom);
        }

        void UpdateBowFar()
        {
            if (bow != null && bowString != null)
            {
                bow.rotation = Quaternion.LookRotation(Forward, Vector3.up);
                UpdateString(null);
            }
            if (heldArrow != null && heldArrow.gameObject.activeSelf) heldArrow.gameObject.SetActive(false);
            if (shield != null) PlaceShield(Forward, Vector3.up);
        }

        /// <summary>The rider sits on the horse's spine, whatever the horse's clip is doing, with his feet in the stirrups.</summary>
        const float SeatToHips = 0.13f;

        void PlaceRider(float dt)
        {
            if (!Alive && riderFall >= 0f)
            {
                // Thrown off: an arc to the ground beside the horse.
                riderFall = Mathf.Min(1f, riderFall + dt * 1.6f);
                Vector3 p = Vector3.Lerp(riderFallFrom, riderFallTo, riderFall);
                p.y += Mathf.Sin(riderFall * Mathf.PI) * 0.5f;
                Body.position = p;
                return;
            }
            if (!Alive) return;
            Vector3 seat = saddleBone.TransformPoint(seatLocal);
            Vector3 saddle = seat + Vector3.up * SeatToHips - Root.forward * 0.06f;      // his hip joints sit this far over the seat, a little behind its lowest point
            Body.position += saddle - hips.position;
            if (!NearCamera) return;
            Vector3 F = Root.forward, R = Root.right;
            // Legs down the horse's sides, feet a little ahead of the knees, as in stirrups.
            float out_ = barrelHalf + 0.07f;
            legL.Solve(seat - R * out_ - Vector3.up * 0.62f + F * 0.12f, F + Vector3.down * 0.1f, 1f);
            legR.Solve(seat + R * out_ - Vector3.up * 0.62f + F * 0.12f, F + Vector3.down * 0.1f, 1f);
        }

        public void HorseHitReact() { horseRig?.Play("Hit", 1.4f, 0.08f); }
        public void Cheer() { if (Alive && Type != UnitType.Cavalry) rig.Play("Roar", Random.Range(0.8f, 1.2f), 0.2f); }
    }
}
