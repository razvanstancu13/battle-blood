using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BattleBlood
{
    public enum UnitType { Infantry, Archer, Cavalry }
    public enum Act { None, Swing, Bash, Roll, Nock, Draw, Loose, Stagger, Stun, Dead }
    public enum HitKind { Sword, Arrow, FireArrow, Burn, Bleed, Trample, Bash }

    public struct Stats
    {
        public float MaxHp, Walk, Run, Sprint, Damage, Radius, Height, Reach;
        public string Model, Label;
        public static Stats Of(UnitType t) => t switch
        {
            UnitType.Infantry => new Stats { MaxHp = 120, Walk = 1.5f, Run = 4.0f, Sprint = 5.6f, Damage = 25, Radius = 0.42f, Height = 1.85f, Reach = 2.35f, Model = "infantry", Label = "Infantry" },
            UnitType.Archer => new Stats { MaxHp = 70, Walk = 1.6f, Run = 4.4f, Sprint = 6.0f, Damage = 14, Radius = 0.38f, Height = 1.82f, Reach = 0f, Model = "archer", Label = "Archer" },
            _ => new Stats { MaxHp = 150, Walk = 2.0f, Run = 9.5f, Sprint = 14f, Damage = 20, Radius = 1.0f, Height = 2.95f, Reach = 3.0f, Model = "cavalry", Label = "Cavalry" },
        };
    }

    /// <summary>
    /// One soldier (and his horse). Not a MonoBehaviour: Battle updates all of them in one loop, thinks for each only a few times a second,
    /// and skips the costly pose work for those far from the camera.
    /// </summary>
    public partial class Unit
    {
        public static readonly Color[] TeamColor = { new(0.17f, 0.24f, 0.42f), new(0.47f, 0.12f, 0.10f) };
        public const int Blue = 0, Red = 1;

        public readonly int Id, Team;
        public readonly UnitType Type;
        public readonly Stats S;
        public Transform Root, Body;
        public Vector3 Pos, Vel, Knock;
        public float Yaw, Speed;
        public float Hp;
        public bool Alive = true, IsPlayer;
        public Act Action;
        public float ActT, ActDur;

        // What the brain or the player wants this frame.
        public Vector3 Wish;              // flat direction, its length 0..1
        public float WishSpeed;           // m/s
        public Vector3 Aim = Vector3.forward;  // where the chest and weapon point (3D)
        public bool WantBlock;
        public bool Blocking;
        float blockWeight;

        // Combat state.
        int swing, queued = -1;
        bool hitDone;
        public float AttackCooldown, BashCooldown, RollCooldown, ChargeLeft, ChargeCooldown;
        public int HitsThisRun;          // cuts that landed since a horseman last rode out
        public float DrawPower;
        public bool FireNocked;
        public float FireReload;          // seconds until a lit arrow can be nocked again
        public const float FireReloadTime = 8f, ArrowNockTime = 0.32f, FireNockTime = 1.05f, DrawTime = 1.05f;
        // The archer a person is steering nocks and draws normal arrows twice as fast; computer archers keep the pace above.
        bool QuickBow => IsPlayer && Battle.HumanControlled;
        float NockTime => QuickBow ? ArrowNockTime * 0.5f : ArrowNockTime;
        float FullDraw => QuickBow ? DrawTime * 0.5f : DrawTime;
        public float BurnLeft, BurnLevel, BleedLeft;
        public float BloodLevel;
        public int Kills;
        public Unit LastAttacker; public float LastHitTime = -100f;
        public int AttackersOnMe;

        // Rig.
        AnimRig rig, horseRig;
        Animator animator, horseAnimator;
        SkinnedMeshRenderer bodyRenderer, horseRenderer;
        Material bodyMat, horseMat, swordMat, shieldMat;
        Transform horseModel, saddleBone;
        Vector3 seatLocal;          // the saddle's seat, in the spine bone's space: it rides with the horse's back
        float barrelHalf = 0.42f;   // half the horse's width where the rider's legs hang

        static readonly Dictionary<string, (Vector3 seat, float half)> Seats = new();

        /// <summary>
        /// Where a rider sits, from the horse's own mesh in its rest pose: along the back near the spine bone, the highest point of each
        /// slice is the saddle's outline; the seat is the lowest of those between pommel and cantle. Measured once, kept in bone space.
        /// </summary>
        void MeasureSeat()
        {
            if (Seats.TryGetValue("horse", out var known)) { seatLocal = known.seat; barrelHalf = known.half; return; }
            // The rig's own seat bone (tools/horse_tail_rig.py found the saddle by its blanket) beats measuring the outline.
            var seatBone = FindBone(horseModel, "seat");
            // Each vertex where its main bone holds it now (the rest pose: no clip has run yet).
            var r = horseModel.GetComponentInChildren<SkinnedMeshRenderer>();
            var src = r.sharedMesh; var bw = src.boneWeights; var bp = src.bindposes; var bl = r.bones;
            var raw = src.vertices;
            var verts = new Vector3[raw.Length];
            for (int i = 0; i < raw.Length; i++) { int bi = bw[i].boneIndex0; verts[i] = (bl[bi].localToWorldMatrix * bp[bi]).MultiplyPoint3x4(raw[i]); }
            var toWorld = Matrix4x4.identity;
            Vector3 f = Root.forward, s = Root.right, c = saddleBone.position;
            const int bins = 14; const float span = 0.45f;
            var top = new float[bins]; var at = new Vector3[bins];
            for (int i = 0; i < bins; i++) top[i] = float.MinValue;
            float half = 0f; float seatGuess = c.y;
            foreach (var lv in verts)
            {
                var w = toWorld.MultiplyPoint3x4(lv);
                Vector3 d = w - c;
                float along = Vector3.Dot(d, f), side = Vector3.Dot(d, s);
                if (Mathf.Abs(along) > span) continue;
                if (Mathf.Abs(side) < 0.12f)
                {
                    int b = Mathf.Clamp((int)((along + span) / (2f * span) * bins), 0, bins - 1);
                    if (w.y > top[b]) { top[b] = w.y; at[b] = w; }
                }
            }
            // The two highest points (pommel ahead, cantle behind) and the lowest top between them: the seat.
            int hiA = 0, hiB = bins - 1;
            for (int i = 0; i < bins / 2; i++) if (top[i] > top[hiA]) hiA = i;
            for (int i = bins / 2; i < bins; i++) if (top[i] > top[hiB]) hiB = i;
            int seat = (hiA + hiB) / 2;
            for (int i = hiA + 1; i < hiB; i++) if (top[i] > float.MinValue && top[i] < top[seat]) seat = i;
            Vector3 seatWorld = top[seat] > float.MinValue ? at[seat] : c + Vector3.up * 0.3f;
            // The barrel's half width 40 cm under the seat, where the thighs and calves hang.
            foreach (var lv in verts)
            {
                var w = toWorld.MultiplyPoint3x4(lv);
                Vector3 d = w - seatWorld;
                if (Mathf.Abs(Vector3.Dot(d, f)) < 0.2f && Mathf.Abs(d.y + 0.4f) < 0.1f) half = Mathf.Max(half, Mathf.Abs(Vector3.Dot(d, s)));
            }
            if (seatBone != null) seatWorld = seatBone.position;
            seatLocal = saddleBone.InverseTransformPoint(seatWorld);
            barrelHalf = half > 0.1f ? half : 0.42f;
            Seats["horse"] = (seatLocal, barrelHalf);
            var prof = new System.Text.StringBuilder();
            for (int i = 0; i < bins; i++) prof.Append(top[i] > float.MinValue ? $" {(-span + (i + 0.5f) * 2f * span / bins):F2}:{top[i] - Root.position.y:F2}" : " -");
            Debug.Log($"[UNIT] horse: back outline (m ahead of spine: height){prof}; pommel bin {hiB}, cantle bin {hiA}, seat bin {seat}");
            Debug.Log($"[UNIT] horse: seat {seatWorld.y - Root.position.y:F2} m up, {Vector3.Dot(seatWorld - c, f):F2} m ahead of the spine bone; barrel half width {barrelHalf:F2} m");
        }
        Limb armR, armL, legR, legL;
        Transform hips, spine, spine1, spine2, neck, head;
        Transform sword, swordTip, shield, bow, heldArrow, quiver;
        Quaternion bodyRest;      // the model's own turn inside the unit; an archer's shot turns it further
        public static float QuiverBack = 0.14f;      // how far behind the chest bone the quiver's middle sits
        LineRenderer bowString;
        readonly List<Transform> severed = new();
        readonly List<Transform> burnBones = new();
        bool leftArmGone;
        float corpseSettle, deadTime;
        float stepTimer;
        Vector3 riderFallFrom, riderFallTo; float riderFall = -1f;
        bool frozen;
        TrailRenderer swordTrail;
        public bool NearCamera;
        public float CamDist;

        public Vector3 ChestPos => spine2 != null ? spine2.position : Pos + Vector3.up * 1.3f;
        public Vector3 HeadPos => head != null ? head.position : Pos + Vector3.up * 1.65f;
        public Vector3 Forward => Mathx.FromYaw(Yaw);
        public bool Burning => BurnLeft > 0f;
        public float DeadTime => deadTime;
        public bool CanAct => Alive && (Action == Act.None);
        public bool Invulnerable => Action == Act.Roll && ActT > 0.08f && ActT < 0.55f;
        public bool Stunned => Action == Act.Stun || Action == Act.Stagger;
        public float AimHeight => Type == UnitType.Cavalry ? 2.55f : 1.35f;
        public Transform HeadBone => head;
        public Transform SpineBone => spine2;

        static readonly Dictionary<string, (float yaw, float scale)> Measured = new();

        public Unit(int id, int team, UnitType type, Vector3 pos, float yaw)
        {
            Id = id; Team = team; Type = type; S = Stats.Of(type);
            Pos = pos; Yaw = yaw; Hp = S.MaxHp;
            Root = new GameObject($"{(team == Blue ? "blue" : "red")} {S.Label} {id}").transform;
            Root.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            Body = Object.Instantiate(Art.Model(S.Model), Root).transform;
            Body.name = "soldier";
            Body.localPosition = Vector3.zero;
            BuildSoldier();
            if (type == UnitType.Cavalry) BuildHorse();
            BuildWeapons();
            FireReload = Random.Range(0f, FireReloadTime);
        }

        Transform FindBone(Transform root, string n)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == n || t.name.EndsWith(":" + n)) return t;
            return null;
        }

        /// <summary>
        /// Puts every bone of a skinned model in the pose it was skinned in (its bind pose, kept in the mesh), whatever pose the file
        /// opens in: the facing and the arms' T-pose measurements below are taken from it. (A re-exported model can open in the
        /// first frame of a clip: measured from that, every soldier stood a quarter turn off.)
        /// </summary>
        static void ToBindPose(SkinnedMeshRenderer r)
        {
            if (r == null || r.sharedMesh == null) return;
            var bones = r.bones; var binds = r.sharedMesh.bindposes;
            if (bones.Length != binds.Length) return;
            var meshToWorld = r.transform.localToWorldMatrix;
            // Parents first, so a child placed in world space is not moved again by its parent.
            var order = new List<int>();
            for (int i = 0; i < bones.Length; i++) order.Add(i);
            int Depth(Transform t) { int d = 0; while (t != null) { d++; t = t.parent; } return d; }
            order.Sort((x, y) => Depth(bones[x]).CompareTo(Depth(bones[y])));
            foreach (int i in order)
            {
                if (bones[i] == null) continue;
                var m = meshToWorld * binds[i].inverse;
                bones[i].SetPositionAndRotation(m.GetColumn(3), m.rotation);
            }
        }

        void BuildSoldier()
        {
            ToBindPose(Body.GetComponentInChildren<SkinnedMeshRenderer>());
            hips = FindBone(Body, "Hips"); spine = FindBone(Body, "Spine"); spine1 = FindBone(Body, "Spine1"); spine2 = FindBone(Body, "Spine2");
            neck = FindBone(Body, "Neck"); head = FindBone(Body, "Head");
            var rightArm = FindBone(Body, "RightArm"); var leftArm = FindBone(Body, "LeftArm");

            // Which way the model faces and how big it is, from its own T-pose bones, once per model.
            if (!Measured.TryGetValue(S.Model, out var m))
            {
                var worldRight = rightArm.position - leftArm.position;
                var fwd = Vector3.Cross(worldRight, Vector3.up);
                // LAGP has already made the model 1.78 m tall: only the facing needs measuring.
                m = (Mathx.Yaw(fwd), 1f);
                Measured[S.Model] = m;
                Debug.Log($"[UNIT] {S.Model}: facing yaw {m.yaw:F0}, head bone at {head.position.y - Root.position.y:F2} m");
            }
            Body.localRotation = Quaternion.Euler(0, -m.yaw, 0) * Body.localRotation;
            Body.localScale *= m.scale;

            armR = new Limb(rightArm, FindBone(Body, "RightForeArm"), FindBone(Body, "RightHand"), FindBone(Body, "RightHandMiddle1"), FindBone(Body, "RightHandThumb1"), Root);
            armL = new Limb(leftArm, FindBone(Body, "LeftForeArm"), FindBone(Body, "LeftHand"), FindBone(Body, "LeftHandMiddle1"), FindBone(Body, "LeftHandThumb1"), Root);
            armR.FindFingers(); armL.FindFingers();
            legR = new Limb(FindBone(Body, "RightUpLeg"), FindBone(Body, "RightLeg"), FindBone(Body, "RightFoot"), null, null, Root);
            legL = new Limb(FindBone(Body, "LeftUpLeg"), FindBone(Body, "LeftLeg"), FindBone(Body, "LeftFoot"), null, null, Root);
            foreach (var b in new[] { hips, spine2, head, armR.Lower, armL.Lower, legR.Lower, legL.Lower }) if (b != null) burnBones.Add(b);

            bodyRenderer = Body.GetComponentInChildren<SkinnedMeshRenderer>();
            float metal = Type == UnitType.Archer ? 0.0f : Type == UnitType.Infantry ? 0.7f : 1f;
            bodyMat = new Material(Art.ModelMat(S.Model, 1f, metal, 0.28f));
            bodyMat.SetColor("_TintColor", TeamColor[Team]);
            if (Type == UnitType.Cavalry) bodyMat.SetFloat("_TintWarm", 1f);      // the knight's polished steel is as pale as his surcoat
            bodyRenderer.sharedMaterial = bodyMat;
            bodyRenderer.updateWhenOffscreen = false;
            bodyRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
            bodyRenderer.localBounds = new Bounds(new Vector3(0, 0.9f, 0) / m.scale, new Vector3(2.4f, 2.6f, 2.4f) / m.scale);

            animator = Body.GetComponent<Animator>();
            if (animator == null) animator = Body.gameObject.AddComponent<Animator>();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            animator.runtimeAnimatorController = null;

            var locos = Type == UnitType.Cavalry
                ? new[] { ("Driving", 0f) }
                : Type == UnitType.Infantry ? new[] { ("SSIdle", 0f), ("Walk", 1.4f), ("Jog", 3.1f), ("SSRun", 4.2f), ("Sprint", 6.0f) }
                : new[] { ("SwordIdle", 0f), ("Walk", 1.4f), ("Jog", 3.1f), ("Run", 4.2f), ("Sprint", 6.0f) };
            var mask = AnimRig.UpperMask(Body, spine);
            var actions = Type == UnitType.Archer ? new[] { "Roll", "Death", "Dizzy", "Roar", "HitChest", "HitHead", "BowDraw", "BowAim", "BowRelease" }
                                                  : new[] { "Roll", "Death", "Dizzy", "Roar", "HitChest", "HitHead", "SSDeath", "SSDeath2" };
            var uppers = Type == UnitType.Archer ? new[] { "SwordAttack", "HitChest", "HitHead" }
                                                 : new[] { "SwordAttack", "HitChest", "HitHead", "SSSlash1", "SSSlash3", "SSImpact", "SSBlock", "SSBlockIdle" };
            rig = new AnimRig(S.Model, animator, locos, actions, uppers, mask);
            bodyRest = Body.localRotation;
        }

        void BuildHorse()
        {
            horseModel = Object.Instantiate(Art.Model("horse"), Root).transform;
            horseModel.name = "horse";
            horseModel.localPosition = Vector3.zero;
            horseModel.localScale *= HorseScale;      // generated 1.65 m to the ears: a warhorse carries a 1.78 m man's head well above that
            // LAGP's four-legged rig: root > hips > spine > chest > neck > head, legs and tail.
            var hipsBone = FindBone(horseModel, "hips");
            var headBone = FindBone(horseModel, "head");
            saddleBone = FindBone(horseModel, "spine");
            if (hipsBone == null || headBone == null || saddleBone == null) throw new MissingReferenceException("horse: expected bones hips, spine and head");
            if (!Measured.TryGetValue("horse", out var m))
            {
                var fwd = headBone != null && hipsBone != null ? headBone.position - hipsBone.position : Vector3.right;
                m = (Mathx.Yaw(Mathx.Flat(fwd)), 1f);
                Measured["horse"] = m;
                Debug.Log($"[UNIT] horse: facing yaw {m.yaw:F0}, spine bone height {saddleBone.position.y - Root.position.y:F2}, bones {horseModel.GetComponentsInChildren<Transform>().Length}");
            }
            horseModel.localRotation = Quaternion.Euler(0, -m.yaw, 0) * horseModel.localRotation;
            MeasureSeat();

            foreach (var n in new[] { "hips", "spine", "chest", "neck", "head" }) { var b = FindBone(horseModel, n); if (b != null) horseBones.Add(b); }
            // The generated tail's own bones (tools/horse_tail_rig.py) swing with spring physics.
            tail = TailBones.Find(horseModel);
            if (tail == null) Debug.LogWarning("[UNIT] horse: no tail_hang bones, the tail stays as the clips have it");
            horseRenderer = horseModel.GetComponentInChildren<SkinnedMeshRenderer>();
            horseRenderer.sharedMesh = NoLegBridges(horseRenderer);
            horseMat = new Material(Art.ModelMat("horse", 1f, 0f, 0.38f));
            horseMat.SetColor("_TintColor", TeamColor[Team]);
            horseRenderer.sharedMaterial = horseMat;
            horseRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
            horseRenderer.localBounds = new Bounds(Vector3.up, new Vector3(4f, 3f, 4f));
            burnBones.Add(saddleBone);

            horseAnimator = horseModel.GetComponent<Animator>();
            if (horseAnimator == null) horseAnimator = horseModel.gameObject.AddComponent<Animator>();
            horseAnimator.applyRootMotion = false;
            horseAnimator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            horseRig = new AnimRig("horse", horseAnimator, new[] { ("Idle", 0f), ("Walk", 2.2f), ("Trot", 5.5f), ("Run", 13f) },
                new[] { "Death", "Hit", "Attack", "LookAround" }, new string[0], null);
        }

        const float HorseScale = 1.3f;
        readonly List<Transform> horseBones = new();
        TailBones tail;

        /// <summary>The body's bone chains as (bone, the joint it ends at, body thickness): what a stuck arrow hangs from.</summary>
        IEnumerable<(Transform bone, Transform end, float radius)> Chains(Vector3 near)
        {
            if (Type == UnitType.Cavalry && horseBones.Count > 1 && near.y < Pos.y + 2.0f)
            {
                for (int i = 0; i + 1 < horseBones.Count; i++) yield return (horseBones[i], horseBones[i + 1], 0.3f * HorseScale);
                yield break;
            }
            if (hips != null && spine2 != null) yield return (hips, spine2, 0.15f);
            if (spine2 != null && neck != null) yield return (spine2, neck, 0.15f);
            foreach (var l in new[] { armR, armL, legR, legL })
            {
                if (l == null) continue;
                yield return (l.Upper, l.Lower, 0.07f);
                yield return (l.Lower, l.Hand, 0.055f);
            }
        }

        static Vector3 OnSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a; float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            return a + ab * t;
        }

        /// <summary>The bone a hit at this point belongs to.</summary>
        public Transform BoneNear(Vector3 p)
        {
            Transform best = spine2; float bd = float.MaxValue;
            foreach (var c in Chains(p))
            {
                if (c.bone.lossyScale.x < 0.01f) continue;
                float d = (OnSegment(p, c.bone.position, c.end.position) - p).sqrMagnitude;
                if (d < bd) { bd = d; best = c.bone; }
            }
            return best;
        }

        /// <summary>Where an arrow on this line meets the body round the bone it hits: its head goes in there, not in the air beside him.</summary>
        public Vector3 SurfaceNear(Vector3 p, Vector3 dir)
        {
            dir = dir.normalized;
            var bone = BoneNear(p);
            foreach (var c in Chains(p))
            {
                if (c.bone != bone) continue;
                Vector3 axis = OnSegment(p, c.bone.position, c.end.position);
                // The point of the arrow's line closest to the bone, then back along the line by the body's thickness.
                Vector3 q = p + dir * Vector3.Dot(axis - p, dir);
                return q - dir * c.radius;
            }
            return p;
        }
        static Mesh cleanHorseMesh;

        /// <summary>
        /// A side-on concept overlaps a pair of legs, so their hooves can fuse in the generated mesh, and that skin stretches into a long spike when the legs part.
        /// Triangles joining the left and right lower leg of a pair are removed, once, on a copy of the mesh. The gaps are small and hidden inside the hooves.
        /// </summary>
        static Mesh NoLegBridges(SkinnedMeshRenderer r)
        {
            if (cleanHorseMesh != null) return cleanHorseMesh;
            var src = r.sharedMesh;
            if (!src.isReadable) { Debug.LogWarning("[UNIT] horse mesh is not readable, leg bridges stay"); return src; }
            var bones = r.bones;
            // 1 and 2 are a front pair, 4 and 8 a back pair; other bones count for nothing.
            var side = new int[bones.Length];
            for (int i = 0; i < bones.Length; i++)
                side[i] = bones[i].name switch { "legFL_lower" => 1, "legFR_lower" => 2, "legBL_lower" => 4, "legBR_lower" => 8, _ => 0 };
            var w = src.boneWeights;
            var tris = src.triangles;
            var keep = new List<int>(tris.Length);
            int removed = 0;
            for (int t = 0; t < tris.Length; t += 3)
            {
                int mask = 0;
                for (int k = 0; k < 3; k++) mask |= side[w[tris[t + k]].boneIndex0];
                if ((mask & 3) == 3 || (mask & 12) == 12) { removed++; continue; }
                keep.Add(tris[t]); keep.Add(tris[t + 1]); keep.Add(tris[t + 2]);
            }
            cleanHorseMesh = Object.Instantiate(src);
            cleanHorseMesh.name = "horse clean";
            cleanHorseMesh.SetTriangles(keep, 0);
            Debug.Log($"[UNIT] horse: removed {removed} triangles bridging a pair of legs");
            return cleanHorseMesh;
        }

        void BuildWeapons()
        {
            if (Type != UnitType.Archer)
            {
                sword = AttachBlade(armR);
                swordMat = sword.GetComponentInChildren<MeshRenderer>().sharedMaterial;       // each sword has its own (AttachBlade makes it)
                swordTip = new GameObject("tip").transform;
                swordTip.SetParent(sword, false);
                swordTip.localPosition = Vector3.forward * (SwordLength - 0.12f);
                var trailGo = new GameObject("trail");
                trailGo.transform.SetParent(sword, false);
                trailGo.transform.localPosition = Vector3.forward * (SwordLength - 0.18f);
                swordTrail = trailGo.AddComponent<TrailRenderer>();
                swordTrail.time = 0.1f; swordTrail.startWidth = 0.16f; swordTrail.endWidth = 0.01f;
                swordTrail.sharedMaterial = Fx.TrailMaterial;
                swordTrail.shadowCastingMode = ShadowCastingMode.Off;
                swordTrail.emitting = false;
                swordTrail.numCapVertices = 0;
            }
            if (Type == UnitType.Infantry)
            {
                shield = new GameObject("shield").transform;
                shield.SetParent(armL.Lower, false);
                var mesh = Art.PropMesh("shield");
                var bounds = Art.PropBounds("shield");
                var child = new GameObject("mesh");
                child.transform.SetParent(shield, false);
                // The generated shield stands facing the camera; its centre goes on the forearm, and it stands 0.8 m tall.
                var pb = Art.PropBase("shield");
                float k = 0.8f / Mathf.Max(0.05f, bounds.size.y);
                child.transform.localRotation = pb.rotation;
                child.transform.localScale = pb.lossyScale * k;
                child.transform.localPosition = ((Vector3)pb.GetColumn(3) - bounds.center) * k;
                child.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = child.AddComponent<MeshRenderer>();
                shieldMat = new Material(Art.ModelMat("shield", 1f, 0.6f, 0.3f));
                shieldMat.SetColor("_TintColor", TeamColor[Team]);
                r.sharedMaterial = shieldMat;
            }
            if (Type == UnitType.Archer)
            {
                bow = new GameObject("bow").transform;
                bow.SetParent(armL.Hand, false);
                var bm = new GameObject("mesh");
                bm.transform.SetParent(bow, false);
                bm.transform.localPosition = new Vector3(0, 0, -0.13f);
                bm.AddComponent<MeshFilter>().sharedMesh = Fx.BowMesh;
                bm.AddComponent<MeshRenderer>().sharedMaterial = Art.TexMat("wood", 0.32f, 0.5f);
                var lr = new GameObject("string").AddComponent<LineRenderer>();
                lr.transform.SetParent(bow, false);
                lr.positionCount = 3; lr.useWorldSpace = false; lr.startWidth = lr.endWidth = 0.005f;
                lr.sharedMaterial = Fx.StringMaterial;
                lr.shadowCastingMode = ShadowCastingMode.Off;
                bowString = lr;
                heldArrow = new GameObject("arrow").transform;
                heldArrow.SetParent(Root, false);
                heldArrow.gameObject.AddComponent<MeshFilter>().sharedMesh = Fx.ArrowMesh;
                heldArrow.gameObject.AddComponent<MeshRenderer>().sharedMaterial = Fx.ArrowMaterial;
                var fl = new GameObject("fletch");
                fl.transform.SetParent(heldArrow, false);
                fl.AddComponent<MeshFilter>().sharedMesh = Fx.FletchMesh;
                fl.AddComponent<MeshRenderer>().sharedMaterial = Fx.FletchMaterial;
                heldArrow.gameObject.SetActive(false);

                // The quiver hangs on the back: a generated prop reused for every archer.
                var q = new GameObject("quiver").transform;
                q.SetParent(spine2, false);
                q.position = spine2.position - Forward * QuiverBack + Root.right * 0.06f - Vector3.up * 0.12f;
                q.rotation = Root.rotation * Quaternion.Euler(-10f, 0, -24f);
                q.localScale = Vector3.one / Mathf.Max(1e-4f, spine2.lossyScale.x);
                var qb = Art.PropBounds("quiver"); var qp = Art.PropBase("quiver");
                float qk = 0.68f / Mathf.Max(0.05f, qb.size.y);
                var qc = new GameObject("mesh").transform;
                qc.SetParent(q, false);
                qc.localRotation = qp.rotation;
                qc.localScale = qp.lossyScale * qk;
                qc.localPosition = ((Vector3)qp.GetColumn(3) - qb.center) * qk;
                qc.gameObject.AddComponent<MeshFilter>().sharedMesh = Art.PropMesh("quiver");
                qc.gameObject.AddComponent<MeshRenderer>().sharedMaterial = Art.ModelMat("quiver", 0f, 0f, 0.25f);
                quiver = q;
            }
        }

        public const float SwordLength = 1.22f;
        public static float GripFromPommel = 0.13f;      // share of the sword's length from the pommel end to the middle of the grip     // a long arming sword: reach is what wins a press of men

        /// <summary>The sword in the hand: its blade along the thumb's direction, its grip in the palm. Measured from the T-pose, so any rig works.</summary>
        Transform AttachBlade(Limb arm)
        {
            var mesh = Art.PropMesh("sword");
            var holder = new GameObject("sword").transform;
            holder.SetParent(arm.Hand, false);
            Vector3 fingers = (arm.Finger.position - arm.Hand.position).normalized;
            Vector3 thumb = arm.Thumb != null ? Vector3.ProjectOnPlane(arm.Thumb.position - arm.Hand.position, fingers).normalized : Root.forward;
            holder.position = arm.Hand.position + fingers * 0.075f * Body.lossyScale.x - Vector3.up * 0.025f;
            holder.rotation = Quaternion.LookRotation(thumb, Vector3.up);
            holder.localScale = Vector3.one;

            // The generated sword stands point down: pommel at the top of its box. The grip is just under the pommel.
            var b = Art.PropBounds("sword");
            var pb = Art.PropBase("sword");
            var child = new GameObject("mesh");
            child.transform.SetParent(holder, false);
            float k = SwordLength / Mathf.Max(0.05f, b.size.y);
            // Model -Y (towards the point) becomes holder +Z (the blade's direction).
            Quaternion toBlade = Quaternion.FromToRotation(Vector3.down, Vector3.forward);
            child.transform.localRotation = toBlade * pb.rotation;
            child.transform.localScale = pb.lossyScale * k;
            Vector3 grip = new Vector3(b.center.x, b.max.y - b.size.y * GripFromPommel, b.center.z);
            child.transform.localPosition = toBlade * (((Vector3)pb.GetColumn(3) - grip) * k);
            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = child.AddComponent<MeshRenderer>();
            r.sharedMaterial = new Material(Art.ModelMat("sword", 0f, 1f, 0.55f));
            return holder;
        }

        // ---- per frame ----

        public void Tick(float dt)
        {
            FadeBlood(dt);
            if (!Alive) { TickDead(dt); return; }
            AttackCooldown -= dt; BashCooldown -= dt; RollCooldown -= dt; ChargeCooldown -= dt; FireReload -= dt;
            TickAction(dt);
            TickStatus(dt);
            if (!Alive) return;
            Move(dt);
            rig?.Tick(dt);
            if (Type == UnitType.Cavalry)
            {
                horseRig.SetSpeed(Speed, dt);
                horseRig.Tick(dt);
            }
            else if (Action != Act.Roll) rig.SetSpeed(Speed, dt);
        }

        void TickStatus(float dt)
        {
            float target = (WantBlock && Type == UnitType.Infantry && !leftArmGone && (Action == Act.None || Action == Act.Stagger)) ? 1f : 0f;
            blockWeight = Mathf.MoveTowards(blockWeight, target, dt * 7f / MeleePace);
            Blocking = blockWeight > 0.55f;
            if (Type == UnitType.Infantry)
            {
                string up = rig.UpperPlaying;
                if (target > 0.5f && up == null) rig.PlayUpper("SSBlockIdle", 1f, 0.12f / MeleePace, true);
                else if (target < 0.5f && up == "SSBlockIdle") rig.StopUpper(0.15f);
            }

            if (Burning)
            {
                BurnLeft -= dt * (Weather.Wet ? 2f : 1f);        // the rain puts him out sooner
                BurnLevel = Mathf.Min(1f, BurnLevel + dt * 0.14f);
                SetBurn(0f, Mathf.Clamp01(BurnLeft * 0.7f));       // alive: glowing, not charred
                Fx.BodyFlames(this, burnBones, dt, 1f);
                TakeDamage(Balance.BurnPerSecond * dt, LastBurner, Vector3.zero, HitKind.Burn, Pos);
                if (!Alive) return;
                // Water puts a man out.
                if (Battle.I.Field.WaterDepth(Pos.x, Pos.z) > 0.32f) Extinguish();
                // Flames jump to whoever is packed against him.
                if (Random.value < dt * 0.06f) Battle.I.SpreadFire(this);
                if (BurnLeft <= 0f) { BurnLevel = 0f; SetBurn(0f, 0f); }
            }
            if (BleedLeft > 0f)
            {
                BleedLeft -= dt;
                TakeDamage(3f * dt, null, Vector3.zero, HitKind.Bleed, Pos);
            }
        }

        public Unit LastBurner;

        public void Ignite(Unit by)
        {
            if (!Alive) return;
            bool fresh = !Burning;
            BurnLeft = Balance.BurnSeconds;
            LastBurner = by;
            if (fresh)
            {
                GameAudio.Play("ignite", ChestPos, 1f);
                if (Type != UnitType.Cavalry || Random.value < 0.5f) GameAudio.Play("burn_scream", ChestPos, 0.9f);
                else GameAudio.Play("horse_scream", ChestPos, 0.9f);
                BurnStat++;
            }
        }
        public static int BurnStat;

        public void Extinguish()
        {
            BurnLeft = 0f;
            BurnLevel = 0f;
            SetBurn(0f, 0f);
            Fx.Puff(Pos + Vector3.up * 0.5f, 6);
        }

        void SetBurn(float burn, float ember)
        {
            bodyMat.SetFloat("_Burn", burn); bodyMat.SetFloat("_Ember", ember);
            if (horseMat != null) { horseMat.SetFloat("_Burn", burn); horseMat.SetFloat("_Ember", ember); }
            if (shieldMat != null) { shieldMat.SetFloat("_Burn", burn * 0.8f); shieldMat.SetFloat("_Ember", ember); }
        }

        const float MaxBloodShown = 0.72f;
        public const float BloodHold = 4f, BloodFadePerSecond = 0.22f;     // seconds it stays fresh, then this much of full soaking goes each second
        float bloodAge, swordBlood;

        public void AddBlood(float amount)
        {
            BloodLevel = Mathf.Min(1f, BloodLevel + amount);
            bloodAge = 0f;
            ShowBlood();
        }

        void ShowBlood()
        {
            float shown = BloodLevel * MaxBloodShown;
            bodyMat.SetFloat("_Blood", shown);
            if (horseMat != null) horseMat.SetFloat("_Blood", shown);
            if (shieldMat != null) shieldMat.SetFloat("_Blood", shown * 0.9f);
            if (swordMat != null) swordMat.SetFloat("_Blood", swordBlood);
        }

        void BloodySword(float amount)
        {
            if (sword == null) return;
            swordBlood = Mathf.Min(MaxBloodShown, swordBlood + amount);
            bloodAge = 0f;
            ShowBlood();
        }

        void FadeBlood(float dt)
        {
            if (BloodLevel <= 0f && swordBlood <= 0f) return;
            bloodAge += dt;
            if (bloodAge < BloodHold) return;
            BloodLevel = Mathf.Max(0f, BloodLevel - BloodFadePerSecond * dt);
            swordBlood = Mathf.Max(0f, swordBlood - BloodFadePerSecond * dt);
            ShowBlood();
        }

        void Move(float dt)
        {
            var field = Battle.I.Field;
            Vector3 wish = Mathx.Flat(Wish);
            float want = WishSpeed;

            switch (Action)
            {
                case Act.Stun: case Act.Stagger: want = 0f; break;
                case Act.Swing: if (Type != UnitType.Cavalry) want = Mathf.Min(want, 1.9f); break;
                case Act.Bash: want = Mathf.Min(want, 2.6f); break;
                case Act.Nock: case Act.Draw: case Act.Loose: want = 0f; break;      // he stands to shoot: the bow clips plant his feet
            }
            if (Blocking && Type == UnitType.Infantry) want = Mathf.Min(want, S.Walk * 1.2f);

            // Going uphill costs speed and downhill gives some back; wading costs a lot.
            Vector3 normal = field.Normal(Pos.x, Pos.z);
            float depth = field.WaterDepth(Pos.x, Pos.z);
            float along = Vector3.Dot(new Vector3(normal.x, 0, normal.z), wish.sqrMagnitude > 1e-4f ? wish.normalized : Forward);
            float slopeMul = Mathf.Clamp(1f + along * 1.3f, 0.55f, 1.2f);
            float waterMul = depth > 0.2f ? Mathf.Lerp(0.8f, Type == UnitType.Cavalry ? 0.6f : 0.5f, Mathf.Clamp01(depth / 0.7f)) : field.Muddy(Pos.x, Pos.z) ? 0.86f : 1f;
            want *= slopeMul * waterMul;

            if (Type == UnitType.Cavalry)
            {
                // A horse has momentum and a turning circle: it only goes the way it faces.
                if (wish.sqrMagnitude > 0.01f)
                {
                    float turn = Mathf.Lerp(120f, 46f, Mathf.Clamp01(Speed / 12f));
                    Yaw = Mathf.MoveTowardsAngle(Yaw, Mathx.Yaw(wish), turn * dt);
                    float off = Mathf.Abs(Mathf.DeltaAngle(Yaw, Mathx.Yaw(wish)));
                    want *= Mathf.Lerp(1f, 0.45f, Mathf.Clamp01(off / 120f)) * Mathf.Clamp01(wish.magnitude);
                }
                else want = 0f;
                float accel = want > Speed ? 3.6f : 7f;
                Speed = Mathf.MoveTowards(Speed, want, accel * dt);
                Vel = Forward * Speed;
            }
            else if (Action == Act.Roll)
            {
                float t = ActT / ActDur;
                Vel = rollDir * Mathf.Lerp(6.8f, 1.5f, t * t);
                Speed = Vel.magnitude;
                // Getting up out of the roll he turns back to where he was looking, so he lands facing it, not the way he rolled.
                Vector3 look = Mathx.Flat(Aim);
                if (t > 0.6f && look.sqrMagnitude > 1e-3f) Yaw = Mathf.MoveTowardsAngle(Yaw, Mathx.Yaw(look), 600f * dt);
            }
            else
            {
                Vector3 target = (wish.sqrMagnitude > 1f ? wish.normalized : wish) * want;
                Vel = Vector3.MoveTowards(Vel, target, 22f * dt);
                Speed = Vel.magnitude;
                // Face the aim while fighting, the way of travel otherwise.
                Vector3 face = Mathx.Flat(Aim);
                bool fighting = Action != Act.None || Blocking || IsPlayer || Type == UnitType.Archer && wish.sqrMagnitude < 0.04f;
                if (!fighting && Vel.sqrMagnitude > 0.25f) face = Vel;

                if (face.sqrMagnitude > 1e-3f) Yaw = Mathf.MoveTowardsAngle(Yaw, Mathx.Yaw(face), 420f * dt);
            }

            Pos += (Vel + Knock) * dt;
            Knock = Vector3.MoveTowards(Knock, Vector3.zero, (Stunned ? 9f : 14f) * dt);
            Pos.x = Mathf.Clamp(Pos.x, -Field.Limit, Field.Limit);
            Pos.z = Mathf.Clamp(Pos.z, -Field.Limit, Field.Limit);
            if (field.PushOut(ref Pos, S.Radius) && Type == UnitType.Cavalry) Speed *= 1f - 2f * dt;
            Pos.y = field.Height(Pos.x, Pos.z);

            // Footfalls: sound for those close, dust and splashes.
            if (Speed > 0.6f && CamDist < 80f)
            {
                stepTimer -= dt * Speed / (Type == UnitType.Cavalry ? 3.4f : 1.55f);
                if (stepTimer <= 0f)
                {
                    stepTimer = 1f;
                    if (depth > 0.1f) { Fx.Splash(Pos, Type == UnitType.Cavalry ? 3 : 1); GameAudio.Play("step_mud", Pos, 0.3f); }
                    else
                    {
                        if (Type == UnitType.Cavalry || Speed > 3.5f) Fx.Dust(Pos, Type == UnitType.Cavalry ? 3 : 1);
                        if (Type == UnitType.Cavalry) { if (!IsPlayer) GameAudio.Play("hoof", Pos, 0.35f); }
                        else GameAudio.Play("step", Pos, IsPlayer ? 0.28f : 0.16f);
                    }
                }
            }
        }

        public void PushRoot()
        {
            Root.SetPositionAndRotation(Pos, Quaternion.Euler(0, Yaw, 0));
        }

        // ---- actions ----

        public void BeginSwing(int index)
        {
            if (!Alive || Type == UnitType.Archer) return;
            Action = Act.Swing; ActT = 0f; swing = index % Moves.Swings.Length; hitDone = false; queued = -1;
            ActDur = Moves.Swings[swing].Duration * (Type == UnitType.Cavalry ? 0.9f : 1f) * MeleePace;
            if (swordTrail != null) swordTrail.emitting = false;
            GameAudio.Play("swing", ChestPos, 0.6f);
            var cut = Moves.Swings[swing];
            float clipLen = rig.PlayUpper(cut.Clip, 1f, 0.06f);
            if (clipLen > 0f) rig.PlayUpper(cut.Clip, clipLen / ActDur, 0.06f);
            else rig.PlayUpper("SwordAttack", 1.3f);
        }

        /// <summary>The attack button. Pressed late in a swing it chains into the next one: three different cuts in a row.</summary>
        public void AttackInput()
        {
            if (Type == UnitType.Archer) return;
            if (Action == Act.None && AttackCooldown <= 0f) { BeginSwing(comboNext); comboNext = (comboNext + 1) % Moves.Swings.Length; }
            else if (Action == Act.Swing && ActT > ActDur * 0.42f) { queued = comboNext; comboNext = (comboNext + 1) % Moves.Swings.Length; }
        }
        int comboNext;
        /// <summary>How long a cut, a bash or raising the shield takes, against the player's: computer soldiers fight at half his speed.</summary>
        float MeleePace => IsPlayer && Battle.HumanControlled ? 1f : 2f;

        /// <summary>Knocked off his feet's balance: the whole body plays the hit while he is shoved back, and he cannot act until it is over.</summary>
        public void Knocked(float seconds)
        {
            if (!Alive || Action == Act.Roll) return;
            Action = Act.Stun; ActT = 0f; ActDur = seconds; DrawPower = 0f; FireNocked = false;
            if (swordTrail != null) swordTrail.emitting = false;
            if (Type != UnitType.Cavalry) rig.Play(Random.value < 0.5f ? "HitChest" : "HitHead", 1f, 0.05f);
            else HorseHitReact();
        }
        Vector3 rollDir = Vector3.forward;

        public void BeginBash()
        {
            if (!CanAct || Type != UnitType.Infantry || BashCooldown > 0f || leftArmGone) return;
            Action = Act.Bash; ActT = 0f; ActDur = 0.62f * MeleePace; hitDone = false; BashCooldown = 3.5f;
            GameAudio.Play("swing", ChestPos, 0.5f);
        }

        public void BeginRoll(Vector3 dir)
        {
            if (!CanAct || Type == UnitType.Cavalry || RollCooldown > 0f) return;
            if (dir.sqrMagnitude > 0.01f) Yaw = Mathx.Yaw(dir);
            rollDir = Forward;
            Action = Act.Roll; ActT = 0f;
            ActDur = rig.Play("Roll", 1.25f, 0.08f);
            if (ActDur <= 0f) ActDur = 0.85f;
            RollCooldown = 2.4f;
            GameAudio.Play("armour", Pos, 0.4f);
        }

        public void BeginCharge()
        {
            if (Type != UnitType.Cavalry || ChargeCooldown > 0f) return;
            ChargeLeft = 5f; ChargeCooldown = 12f;
            GameAudio.Play("neigh", Pos, 0.85f);
            if (horseRig != null && Speed < 3f) horseRig.Play("Attack", 1.2f);
        }

        // Archers: the button held nocks and draws, released it looses.
        public void BeginNock(bool fire)
        {
            if (!CanAct || Type != UnitType.Archer || AttackCooldown > 0f) return;
            FireNocked = fire && FireReload <= 0f;
            Action = Act.Nock; ActT = 0f; ActDur = FireNocked ? FireNockTime : NockTime; DrawPower = 0f;
            // The draw clip (reach for an arrow, nock it, pull) spans the nock and the full draw; then the held aim takes over.
            float span = ActDur + (FireNocked ? DrawTime : FullDraw);
            float len = rig.Play("BowDraw", 1f, 0.1f);
            if (len > 0f) { rig.Play("BowDraw", len / span, 0.1f); aimClipAt = span - ActDur; aimHeld = false; }
            drawSpan = span; drawClock = 0f;
            if (FireNocked) GameAudio.Play("arrow_light", ChestPos, 0.8f);
        }

        float aimClipAt; bool aimHeld;
        float drawSpan = 1f, drawClock;
        /// <summary>How far through the draw clip he is, 0..1 (past 1 while he holds the aim).</summary>
        public float DrawClip => drawClock / Mathf.Max(0.01f, drawSpan);

        public void Release(Vector3 aimDir)
        {
            if (Action == Act.Nock) { if (ActT < ActDur * 0.8f) { FireNocked = false; Action = Act.None; rig.StopAction(0.2f); return; } }
            if (Action != Act.Nock && Action != Act.Draw) return;
            float p = Action == Act.Draw ? DrawPower : 0.15f;
            Loose(aimDir, p);
        }

        void Loose(Vector3 dir, float power)
        {
            Vector3 from = armL.Hand.position + dir.normalized * 0.1f;
            float speed = Mathf.Lerp(26f, 66f, power);
            float dmg = Mathf.Lerp(6f, S.Damage, power) * (IsPlayer ? Balance.PlayerArrowBonus : 1f);
            Battle.I.Arrows.Fire(from, dir.normalized * speed, this, dmg, FireNocked);
            GameAudio.Play("bow_release", from, 0.7f);
            if (FireNocked) { FireReload = FireReloadTime; GameAudio.Play("fire_fly", from, 0.5f); }
            FireNocked = false;
            Action = Act.Loose; ActT = 0f; ActDur = 0.45f; DrawPower = 0f;
            float rl = rig.Play("BowRelease", 1f, 0.05f);
            if (rl > 0f) rig.Play("BowRelease", rl / 0.6f, 0.05f);
            AttackCooldown = 0.1f;
            ShotsFired++;
            Tally.Attempts[(int)UnitType.Archer]++;
        }
        public static int ShotsFired, RescuedCorpses;

        void TickAction(float dt)
        {
            if (Action == Act.None) return;
            ActT += dt;
            if (Action == Act.Nock || Action == Act.Draw) drawClock += dt;
            switch (Action)
            {
                case Act.Swing:
                {
                    var def = Moves.Swings[swing];
                    float strike = def.Strike * ActDur;
                    if (swordTrail != null) swordTrail.emitting = NearCamera && ActT > strike * 0.6f && ActT < strike + 0.14f;
                    if (!hitDone && ActT >= strike) { hitDone = true; Combat.Strike(this, def); }
                    if (queued >= 0 && ActT >= ActDur * 0.62f) { int q = queued; BeginSwing(q); return; }
                    if (ActT >= ActDur) { Action = Act.None; AttackCooldown = IsPlayer ? 0.05f : 0.25f; if (swordTrail != null) swordTrail.emitting = false; }
                    break;
                }
                case Act.Bash:
                    if (!hitDone && ActT >= ActDur * 0.39f) { hitDone = true; Combat.Bash(this); }
                    if (ActT >= ActDur) Action = Act.None;
                    break;
                case Act.Nock:
                    if (ActT >= ActDur) { Action = Act.Draw; ActT = 0f; ActDur = 99f; GameAudio.Play("bow_draw", ChestPos, 0.45f); }
                    break;
                case Act.Draw:
                    DrawPower = Mathf.Clamp01(ActT / (FireNocked ? DrawTime : FullDraw));
                    if (!aimHeld && ActT >= aimClipAt) { aimHeld = true; rig.Play("BowAim", 1f, 0.3f, true); }
                    break;
                default:
                    if (ActT >= ActDur) Action = Act.None;
                    break;
            }
        }

        // ---- being hit ----

        public void Stagger(float seconds, string clip = null)
        {
            if (!Alive || Action == Act.Roll) return;
            if (Type == UnitType.Archer && (Action == Act.Nock || Action == Act.Draw || Action == Act.Loose)) rig.StopAction(0.15f);
            Action = Act.Stagger; ActT = 0f; ActDur = seconds; DrawPower = 0f; FireNocked = false;
            if (swordTrail != null) swordTrail.emitting = false;
            if (Type == UnitType.Infantry && rig.HasUpper("SSImpact")) rig.PlayUpper("SSImpact", 1.4f, 0.06f);
            else rig.PlayUpper(clip ?? "HitChest", 1.3f);
        }

        /// <summary>A blow caught on the raised shield: the block clip jolts, then the shield is held up again.</summary>
        public void ShieldTakesBlow()
        {
            if (Type == UnitType.Infantry && rig.HasUpper("SSBlock")) rig.PlayUpper("SSBlock", 1.6f, 0.04f);
        }

        public void Stun(float seconds)
        {
            if (!Alive) return;
            Action = Act.Stun; ActT = 0f; ActDur = seconds; DrawPower = 0f; FireNocked = false;
            if (Type != UnitType.Cavalry) rig.Play("Dizzy", 1.2f, 0.12f);
        }

        /// <summary>The one way health goes down. Blocks and rolls are settled before this is called.</summary>
        public void TakeDamage(float amount, Unit src, Vector3 dir, HitKind kind, Vector3 hitPos)
        {
            if (!Alive) return;
            float bloodAmount = amount;
            if (Battle.NoDamage) amount = 0f;
            Hp -= amount;
            if (src != null) { LastAttacker = src; LastHitTime = Time.time; Tally.Damage[(int)src.Type] += amount; }
            if (kind != HitKind.Burn && kind != HitKind.Bleed)
            {
                AddBlood(Mathf.Clamp(bloodAmount / S.MaxHp, 0.03f, 0.3f) + 0.07f);
                if (src != null && kind == HitKind.Sword) { src.AddBlood(0.05f); src.BloodySword(0.22f); }
                if (Random.value < 0.6f) GameAudio.Play("pain", ChestPos, 0.7f);
            }
            if (IsPlayer && amount > 0.5f) Battle.I.Hud.OnPlayerHurt(amount / S.MaxHp);
            if (Hp <= 0f) { Hp = 0f; Die(src, dir, kind, hitPos); }
        }

        void Die(Unit src, Vector3 dir, HitKind kind, Vector3 hitPos)
        {
            Alive = false;
            Action = Act.Dead; ActT = 0f;
            BurnLevel = Burning ? 0.25f : 0f;
            Blocking = false; WantBlock = false;
            Speed = 0f; Vel = Vector3.zero;
            if (src != null) { src.Kills++; Tally.Kills[(int)src.Type]++; }
            Tally.Deaths[(int)Type]++;
            if (swordTrail != null) swordTrail.emitting = false;
            if (heldArrow != null) heldArrow.gameObject.SetActive(false);

            // A man dying off camera must still fall: a culled Animator does not move his bones, and he would be frozen standing.
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            if (horseAnimator != null) horseAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            float speed = Random.Range(0.8f, 1.15f);
            string deathClip = Type == UnitType.Infantry ? (Random.value < 0.5f ? "SSDeath" : "SSDeath2") : "Death";
            if (rig.Play(deathClip, speed, 0.12f, true) <= 0f) rig.Play("Death", speed, 0.12f, true);
            if (Type == UnitType.Cavalry)
            {
                horseRig.Play("Death", 1f, 0.15f, true);
                GameAudio.Play("horse_scream", Pos + Vector3.up, 1f);
                // The rider is thrown clear.
                riderFall = 0f;
                riderFallFrom = Body.position;
                var side = (Random.value < 0.5f ? 1f : -1f) * Root.right;
                riderFallTo = Battle.I.Field.Ground(Pos + side * 1.3f + Root.forward * 0.8f);
            }
            GameAudio.Play(Random.value < 0.5f ? "death" : "scream", ChestPos, 1f);
            GameAudio.Play("armour", Pos, 0.45f);
            Gore.Kill(this, src, dir, kind, hitPos);
            Battle.I.OnUnitDied(this, src, kind);
        }

        void TickDead(float dt)
        {
            deadTime += dt;
            if (Burning)
            {
                BurnLeft -= dt;
                BurnLevel = Mathf.Min(1f, BurnLevel + dt * 0.22f);
                SetBurn(BurnLevel, Mathf.Clamp01(BurnLeft * 0.7f));
                Fx.BodyFlames(this, burnBones, dt, 0.7f);
            }
            Pos += Knock * dt;
            Knock = Vector3.MoveTowards(Knock, Vector3.zero, 9f * dt);
            Pos.y = Battle.I.Field.Height(Pos.x, Pos.z);
            if (!frozen)
            {
                rig.Tick(dt);
                horseRig?.Tick(dt);
                // Lie along the slope, not through it.
                corpseSettle = Mathf.Min(1f, corpseSettle + dt);
                PushRoot();
                var n = Battle.I.Field.Normal(Pos.x, Pos.z);
                Root.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(Vector3.up, n), corpseSettle) * Root.rotation;
                if (deadTime > 3.6f && !Burning)
                {
                    frozen = true;
                    // Whatever interrupted it, a corpse lies down: if he is not down yet, his death clip is put on its last frame.
                    if (head != null && head.position.y - Body.position.y > 0.7f) { rig.SnapToEnd("Death"); RescuedCorpses++; }
                    rig.Freeze(); horseRig?.Freeze();
                    animator.enabled = false;
                    if (horseAnimator != null) horseAnimator.enabled = false;
                    if (bodyRenderer != null) bodyRenderer.shadowCastingMode = CamDist < 60f ? ShadowCastingMode.On : ShadowCastingMode.Off;
                }
            }
        }

        // ---- severing ----

        public Transform BoneFor(Gore.Part p) => p switch
        {
            Gore.Part.Head => head,
            Gore.Part.ArmR => armR.Upper,
            Gore.Part.ArmL => armL.Upper,
            Gore.Part.ForearmL => armL.Lower,
            Gore.Part.LegR => legR.Lower,
            Gore.Part.LegL => legL.Lower,
            _ => null,
        };
        public Material BodyMaterial => bodyMat;
        public SkinnedMeshRenderer BodyRenderer => bodyRenderer;
        public Transform SwordHolder => sword;
        public Transform ShieldHolder => shield;

        public void MarkSevered(Transform bone) { if (bone != null) severed.Add(bone); }

        /// <summary>Lost the left arm but still on his feet: the shield falls with it, and he bleeds.</summary>
        public void LoseLeftArm()
        {
            leftArmGone = true;
            BleedLeft = 18f;
            if (shield != null) { Gore.DropProp(shield); shield = null; }
        }
        public bool LeftArmGone => leftArmGone;

        public void DropWeapons()
        {
            if (sword != null) { Gore.DropProp(sword); sword = null; swordTrail = null; }
            if (shield != null) { Gore.DropProp(shield); shield = null; }
            if (bow != null) { bow.SetParent(null, true); Gore.DropProp(bow); bow = null; bowString = null; }
        }
    }
}
