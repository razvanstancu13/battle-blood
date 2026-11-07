using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BattleBlood
{
    /// <summary>
    /// The blood. Everything here is reuse: one particle system for droplets, one for mist, instanced ground decals that fade a few seconds after they land,
    /// the blood map the ground, grass and water read, and severed parts cut at runtime from the unit's own mesh by bone weights.
    /// </summary>
    public class Gore
    {
        public enum Part { Head, ArmR, ArmL, ForearmL, LegR, LegL }

        static Gore I;
        ParticleSystem drops, mist;
        Mesh quad;
        Material woundMat;

        // Blood stays fresh this long, then fades out over BloodFade seconds.
        public const float BloodHold = 5f, BloodFade = 4f;

        // Ground decals in a ring. Blood ones fade away; scorch marks stay.
        class DecalSet
        {
            public Material Mat; public Matrix4x4[] M = new Matrix4x4[1023]; public float[] A = new float[1023], Base = new float[1023], Born = new float[1023];
            public int Count, Next; public MaterialPropertyBlock Block = new(); public bool Fades = true;
            public void Add(Matrix4x4 m, float alpha)
            {
                M[Next] = m; A[Next] = Base[Next] = alpha; Born[Next] = Time.time; Next = (Next + 1) % M.Length; Count = Mathf.Min(M.Length, Count + 1);
            }
            public void Age(float now)
            {
                if (!Fades) return;
                for (int i = 0; i < Count; i++) A[i] = Base[i] * Mathf.Clamp01(1f - (now - Born[i] - BloodHold) / BloodFade);
            }
        }
        readonly List<DecalSet> splats = new();
        DecalSet pools, scorch;

        struct Landing { public float Time; public Vector3 Pos; public float Size; }
        readonly List<Landing> landings = new(256);

        class Pool { public int Index; public Vector3 Pos; public Quaternion Rot; public float Size, Grow; }
        readonly List<Pool> growing = new();

        class Spurt { public Transform Bone; public Vector3 LocalDir; public float Left, Beat, Strength; public Vector3 Fixed; public Vector3 FixedDir; }
        readonly List<Spurt> spurts = new();

        class Chunk { public GameObject Go; public Vector3 Vel, Spin; public float Life; public bool Rest; public float Trail; public Material Bloody; }
        readonly List<Chunk> chunks = new();

        public static int SeveredCount, DecalsPlaced;
        public static bool LastKillSevered;

        public static void Create()
        {
            I = new Gore();
            I.Build();
        }

        void Build()
        {
            quad = MakeQuad();
            drops = MakeParticles("blood drops", "blood_c", 14000, stretched: true);
            mist = MakeParticles("blood mist", "blood_spray", 800, stretched: false);
            foreach (var t in new[] { "blood_a", "blood_b", "blood_c" }) splats.Add(new DecalSet { Mat = DecalMat(t, new Color(0.62f, 0.04f, 0.04f)) });
            pools = new DecalSet { Mat = DecalMat("blood_pool", new Color(0.46f, 0.02f, 0.02f)) };
            scorch = new DecalSet { Mat = DecalMat("smoke", new Color(0.015f, 0.012f, 0.01f)) };
            woundMat = new Material(Art.Shader("BBLit")) { name = "m_wound" };
            woundMat.SetTexture("_BaseMap", Art.Tex("wound"));
            woundMat.SetFloat("_Cutoff", 0.35f);
            woundMat.SetFloat("_Smooth", 0.7f);
            woundMat.SetFloat("_Cull", 0f);
        }

        static Material DecalMat(string tex, Color c)
        {
            var m = new Material(Art.Shader("BBDecal")) { name = "m_" + tex, enableInstancing = true };
            m.SetTexture("_BaseMap", Art.Tex(tex));
            m.SetColor("_Color", c);
            return m;
        }

        static Mesh MakeQuad()
        {
            var m = new Mesh { name = "decal" };
            m.vertices = new[] { new Vector3(-.5f, 0, -.5f), new Vector3(.5f, 0, -.5f), new Vector3(-.5f, 0, .5f), new Vector3(.5f, 0, .5f) };
            m.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
            m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            m.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            m.bounds = new Bounds(Vector3.zero, new Vector3(1, 0.2f, 1));
            return m;
        }

        static ParticleSystem MakeParticles(string name, string tex, int max, bool stretched)
        {
            var go = new GameObject(name);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false; main.playOnAwake = false; main.maxParticles = max;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = stretched ? 1.25f : 0.2f;
            main.startLifetime = 1f; main.startSpeed = 0f; main.startSize = 0.1f;
            var em = ps.emission; em.enabled = false;
            var shape = ps.shape; shape.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            var mat = new Material(Art.Shader("BBParticle")) { name = "m_" + name };
            mat.SetTexture("_BaseMap", Art.Tex(tex));
            mat.SetColor("_Color", stretched ? new Color(0.5f, 0.02f, 0.02f, 1f) : new Color(0.55f, 0.03f, 0.03f, 0.75f));
            r.sharedMaterial = mat;
            r.renderMode = stretched ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
            r.velocityScale = stretched ? 0.035f : 0f;
            r.lengthScale = stretched ? 1.2f : 1f;
            r.shadowCastingMode = ShadowCastingMode.Off;
            var col = ps.colorOverLifetime;
            col.enabled = !stretched;
            if (!stretched)
            {
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0.95f, 0f), new GradientAlphaKey(0f, 1f) });
                col.color = g;
                var size = ps.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.4f, 1f, 1.6f));
            }
            ps.Play();
            return ps;
        }

        // ---- blows ----

        /// <summary>A wound: a burst out along the blow, more behind than in front, and blood flung to the ground.</summary>
        public static void Wound(Vector3 pos, Vector3 dir, float strength, Unit victim)
        {
            if (I == null) return;
            float cam = victim != null ? victim.CamDist : 0f;
            float lod = cam < 40f ? 1f : cam < 90f ? 0.5f : 0.2f;
            I.Burst(pos, dir, strength, lod);
            Battle.I.Field.PaintBlood(pos, 1.2f + strength * 1.1f, 0.12f + strength * 0.2f);
            if (Battle.I.PlayerCam != null && Battle.I.PlayerCam.DistanceTo(pos) < 2.8f) Battle.I.Hud.LensSplat(strength * 0.9f);
        }

        void Burst(Vector3 pos, Vector3 dir, float strength, float lod)
        {
            int n = Mathf.RoundToInt(Mathf.Lerp(30, 110, strength) * lod);
            var ep = new ParticleSystem.EmitParams();
            Vector3 d = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.up;
            for (int i = 0; i < n; i++)
            {
                Vector3 v = (d * Random.Range(1f, 5.2f) + Random.insideUnitSphere * 2.6f + Vector3.up * Random.Range(0.7f, 3.4f)) * Mathf.Lerp(0.8f, 1.4f, strength);
                ep.position = pos + Random.insideUnitSphere * 0.08f;
                ep.velocity = v;
                ep.startSize = Random.Range(0.012f, 0.05f);
                ep.startLifetime = Random.Range(0.45f, 1.0f);
                drops.Emit(ep, 1);
                if (i % 9 == 0) QueueLanding(ep.position, v);
            }
            int m = Mathf.RoundToInt(Mathf.Lerp(4, 12, strength) * lod);
            for (int i = 0; i < m; i++)
            {
                ep.position = pos + Random.insideUnitSphere * 0.12f;
                ep.velocity = d * Random.Range(0.5f, 2.8f) + Random.insideUnitSphere * 0.7f;
                ep.startSize = Random.Range(0.22f, 0.62f) * Mathf.Lerp(0.8f, 1.5f, strength);
                ep.startLifetime = Random.Range(0.22f, 0.5f);
                ep.rotation = Random.Range(0, 360f);
                mist.Emit(ep, 1);
            }
        }

        void QueueLanding(Vector3 p, Vector3 v)
        {
            var field = Battle.I.Field;
            const float g = 9.81f * 1.25f;
            float y0 = p.y - field.Height(p.x, p.z);
            float t = (v.y + Mathf.Sqrt(Mathf.Max(0f, v.y * v.y + 2f * g * y0))) / g;
            var q = new Vector3(p.x + v.x * t, 0, p.z + v.z * t);
            landings.Add(new Landing { Time = Time.time + t, Pos = q, Size = Random.Range(0.18f, 0.62f) });
        }

        /// <summary>A killing blow: what comes off, what sprays, and the pool that spreads under him.</summary>
        public static void Kill(Unit u, Unit src, Vector3 dir, HitKind kind, Vector3 hitPos)
        {
            if (I == null) return;
            I.DoKill(u, src, dir, kind, hitPos);
        }

        void DoKill(Unit u, Unit src, Vector3 dir, HitKind kind, Vector3 hitPos)
        {
            if (dir.sqrMagnitude < 0.01f) dir = -u.Forward;
            Burst(u.ChestPos, dir, 1f, u.CamDist < 90f ? 1f : 0.4f);
            Battle.I.Field.PaintBlood(u.Pos, 2.6f, 0.5f);
            u.Knock += Mathx.Flat(dir) * (kind == HitKind.Trample ? 7f : kind == HitKind.Sword ? 2.5f : 1f);
            AddPool(u.Pos + u.Forward * -0.2f, u.Type == UnitType.Cavalry ? 4.6f : 2.8f);
            StartSpurt(u.SpineBone, Vector3.up, 7f, 0.6f, u);

            // The cut decides what comes away.
            Part? part = null;
            float r = Random.value;
            switch (kind)
            {
                case HitKind.Sword:
                    float k = src != null && src.Type == UnitType.Cavalry ? 0.9f : 0.75f;
                    if (r < k * 0.5f) part = Part.Head;
                    else if (r < k * 0.8f) part = Random.value < 0.6f ? Part.ArmR : Part.ArmL;
                    else if (r < k) part = Random.value < 0.5f ? Part.LegL : Part.LegR;
                    break;
                case HitKind.Trample:
                    if (r < 0.7f) part = Random.value < 0.5f ? Part.LegL : Part.LegR;
                    else part = Part.ArmL;
                    break;
                case HitKind.Arrow:
                case HitKind.FireArrow:
                    break;
            }
            LastKillSevered = part.HasValue;
            if (part.HasValue)
            {
                Sever(u, part.Value, dir, true);
                if (part == Part.Head) GameAudio.Play("sever", hitPos, 1f);
                if (part == Part.ArmR) u.DropWeapons();
                if (part == Part.ArmL && u.ShieldHolder != null) { DropProp(u.ShieldHolder); }
            }
            else u.DropWeapons();
            GameAudio.Play("spurt", u.ChestPos, 0.8f);
        }

        public static void SeverLive(Unit u, Part part, Vector3 dir)
        {
            if (I == null) return;
            I.Sever(u, part, dir, false);
            u.LoseLeftArm();
            GameAudio.Play("sever", u.ChestPos, 1f);
            GameAudio.Play("scream", u.ChestPos, 1f);
        }

        void Sever(Unit u, Part part, Vector3 dir, bool dead)
        {
            var bone = u.BoneFor(part);
            if (bone == null) return;
            SeveredCount++;
            var wound = bone.position;
            Burst(wound, dir + Vector3.up * 0.6f, 1f, u.CamDist < 90f ? 1f : 0.4f);
            GameAudio.Play("sever", wound, 1f);
            // A piece of his own mesh, in his own colours and blood.
            var mesh = PartMesh(u, part, bone);
            if (mesh != null && u.CamDist < 110f)
            {
                var go = new GameObject("severed " + part);
                go.transform.SetPositionAndRotation(wound, bone.rotation);
                go.transform.localScale = bone.lossyScale;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                var mat = new Material(u.BodyMaterial);
                mat.SetFloat("_Blood", 0.72f);
                r.sharedMaterial = mat;
                AddWoundCard(go.transform, bone, 0.09f);
                chunks.Add(new Chunk
                {
                    Go = go,
                    Vel = dir.normalized * Random.Range(2.5f, 5f) + Vector3.up * Random.Range(part == Part.Head ? 4.2f : 2.6f, part == Part.Head ? 6.2f : 4.2f),
                    Spin = Random.onUnitSphere * Random.Range(320f, 800f),
                    Life = 0f,
                    Trail = part == Part.Head ? 2.4f : 1.6f,
                    Bloody = mat,
                });
                if (chunks.Count > 70) { Object.Destroy(chunks[0].Go); chunks.RemoveAt(0); }
            }
            u.MarkSevered(bone);
            // The stump pumps for a while.
            StartSpurt(bone.parent, (bone.position - bone.parent.position).normalized, dead ? 6.5f : 16f, 1f, u, true);
        }

        void AddWoundCard(Transform parent, Transform bone, float size)
        {
            var go = new GameObject("wound");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localScale = Vector3.one * size / Mathf.Max(1e-4f, parent.lossyScale.x);
            go.AddComponent<MeshFilter>().sharedMesh = quad;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = woundMat;
            r.shadowCastingMode = ShadowCastingMode.Off;
        }

        static readonly Dictionary<string, Mesh> PartMeshes = new();

        /// <summary>The triangles whose vertices all belong mostly to this bone or below it, moved into that bone's space.</summary>
        Mesh PartMesh(Unit u, Part part, Transform bone)
        {
            string key = u.S.Model + ":" + part;
            if (PartMeshes.TryGetValue(key, out var cached)) return cached;
            var smr = u.BodyRenderer;
            var src = smr.sharedMesh;
            if (src == null || !src.isReadable) { PartMeshes[key] = null; return null; }
            var bones = smr.bones;
            int rootIndex = System.Array.IndexOf(bones, bone);
            if (rootIndex < 0) { PartMeshes[key] = null; return null; }
            var inPart = new bool[bones.Length];
            for (int i = 0; i < bones.Length; i++) inPart[i] = bones[i] == bone || bones[i].IsChildOf(bone);
            var weights = src.boneWeights;
            var verts = src.vertices; var norms = src.normals; var uv = src.uv; var tris = src.triangles;
            var bind = src.bindposes[rootIndex];
            var map = new int[verts.Length];
            for (int i = 0; i < map.Length; i++) map[i] = -1;
            var nv = new List<Vector3>(); var nn = new List<Vector3>(); var nu = new List<Vector2>(); var nt = new List<int>();
            bool V(int i) => inPart[weights[i].boneIndex0];
            int Get(int i)
            {
                if (map[i] >= 0) return map[i];
                map[i] = nv.Count;
                nv.Add(bind.MultiplyPoint3x4(verts[i]));
                nn.Add(bind.MultiplyVector(norms[i]).normalized);
                nu.Add(uv[i]);
                return map[i];
            }
            for (int t = 0; t < tris.Length; t += 3)
            {
                int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                if (V(a) && V(b) && V(c)) { nt.Add(Get(a)); nt.Add(Get(b)); nt.Add(Get(c)); }
            }
            if (nt.Count == 0) { PartMeshes[key] = null; return null; }
            var m = new Mesh { name = "severed " + key };
            m.SetVertices(nv); m.SetNormals(nn); m.SetUVs(0, nu); m.SetTriangles(nt, 0);
            m.RecalculateBounds();
            return PartMeshes[key] = m;
        }

        public static void DropProp(Transform prop)
        {
            if (I == null || prop == null) return;
            prop.SetParent(null, true);
            I.chunks.Add(new Chunk { Go = prop.gameObject, Vel = Random.onUnitSphere * 1.6f + Vector3.up * 1.6f, Spin = Random.onUnitSphere * 280f, Trail = 0f });
        }

        void StartSpurt(Transform bone, Vector3 dirWorld, float seconds, float strength, Unit u, bool always = false)
        {
            if (bone == null) return;
            if (!always && spurts.Count > 40) return;
            spurts.Add(new Spurt { Bone = bone, LocalDir = bone.InverseTransformDirection(dirWorld), Left = seconds, Strength = strength });
            if (spurts.Count > 64) spurts.RemoveAt(0);
        }

        void AddPool(Vector3 pos, float size)
        {
            var field = Battle.I.Field;
            pos.y = field.Height(pos.x, pos.z) + 0.035f;
            if (pos.y < Field.WaterLevel + 0.02f) { field.PaintBlood(pos, size * 0.8f, 0.6f); return; }  // it spreads in the water instead
            var rot = Quaternion.FromToRotation(Vector3.up, field.Normal(pos.x, pos.z)) * Quaternion.Euler(0, Random.Range(0f, 360f), 0);
            int index = pools.Next;
            pools.Add(Matrix4x4.TRS(pos, rot, Vector3.one * 0.25f), 1f);
            growing.Add(new Pool { Index = index, Pos = pos, Rot = rot, Size = 0.25f, Grow = size });
            DecalsPlaced++;
        }

        void AddSplat(Vector3 pos, float size)
        {
            var field = Battle.I.Field;
            pos.y = field.Height(pos.x, pos.z) + 0.03f;
            if (pos.y < Field.WaterLevel + 0.02f) { field.PaintBlood(pos, size, 0.12f); return; }
            var rot = Quaternion.FromToRotation(Vector3.up, field.Normal(pos.x, pos.z)) * Quaternion.Euler(0, Random.Range(0f, 360f), 0);
            splats[Random.Range(0, splats.Count)].Add(Matrix4x4.TRS(pos, rot, Vector3.one * size), Random.Range(0.75f, 1f));
            field.PaintBlood(pos, size * 0.8f, 0.05f);
            DecalsPlaced++;
        }

        public static void Scorch(Vector3 pos)
        {
            if (I == null) return;
            var field = Battle.I.Field;
            pos.y = field.Height(pos.x, pos.z) + 0.03f;
            var rot = Quaternion.FromToRotation(Vector3.up, field.Normal(pos.x, pos.z)) * Quaternion.Euler(0, Random.Range(0f, 360f), 0);
            I.scorch.Add(Matrix4x4.TRS(pos, rot, Vector3.one * Random.Range(0.8f, 1.5f)), 0.9f);
        }

        // ---- per frame ----

        public static void Tick(float dt) => I?.Update(dt);

        void Update(float dt)
        {
            float now = Time.time;
            for (int i = landings.Count - 1; i >= 0; i--)
                if (landings[i].Time <= now)
                {
                    AddSplat(landings[i].Pos, landings[i].Size);
                    landings[i] = landings[landings.Count - 1];
                    landings.RemoveAt(landings.Count - 1);
                }

            for (int i = growing.Count - 1; i >= 0; i--)
            {
                var p = growing[i];
                p.Size = Mathf.MoveTowards(p.Size, p.Grow, dt * 0.7f);
                pools.M[p.Index] = Matrix4x4.TRS(p.Pos, p.Rot, Vector3.one * p.Size);
                if (p.Size >= p.Grow) growing.RemoveAt(i);
            }

            var ep = new ParticleSystem.EmitParams();
            for (int i = spurts.Count - 1; i >= 0; i--)
            {
                var s = spurts[i];
                s.Left -= dt;
                if (s.Left <= 0f || s.Bone == null) { spurts.RemoveAt(i); continue; }
                // A heartbeat: strong pulses, slowing as he empties.
                s.Beat -= dt;
                if (s.Beat <= 0f)
                {
                    s.Beat = Mathf.Lerp(0.72f, 0.3f, Mathf.Clamp01(s.Left / 6f));
                    Vector3 d = s.Bone.TransformDirection(s.LocalDir).normalized;
                    Vector3 p = s.Bone.position;
                    float force = Mathf.Lerp(1.5f, 4.6f, Mathf.Clamp01(s.Left / 5f)) * s.Strength;
                    for (int k = 0; k < 16; k++)
                    {
                        Vector3 v = (d + Random.insideUnitSphere * 0.24f + Vector3.up * 0.3f).normalized * force * Random.Range(0.7f, 1.1f);
                        ep.position = p; ep.velocity = v;
                        ep.startSize = Random.Range(0.016f, 0.05f);
                        ep.startLifetime = Random.Range(0.5f, 1.0f);
                        drops.Emit(ep, 1);
                        if (k == 0) QueueLanding(p, v);
                    }
                }
            }

            var field = Battle.I.Field;
            foreach (var c in chunks)
            {
                if (c.Go == null) continue;
                c.Life += dt;
                if (c.Bloody != null && c.Life > BloodHold)
                    c.Bloody.SetFloat("_Blood", 0.72f * Mathf.Clamp01(1f - (c.Life - BloodHold) / BloodFade));
                if (c.Rest) continue;
                var t = c.Go.transform;
                c.Vel += Vector3.down * (9.81f * dt);
                t.position += c.Vel * dt;
                t.rotation = Quaternion.Euler(c.Spin * dt) * t.rotation;
                if (c.Trail > 0f)
                {
                    c.Trail -= dt;
                    for (int k = 0; k < 2; k++)
                    {
                        ep.position = t.position + Random.insideUnitSphere * 0.03f;
                        ep.velocity = -c.Vel * 0.12f + Random.insideUnitSphere * 0.5f;
                        ep.startSize = Random.Range(0.02f, 0.04f);
                        ep.startLifetime = Random.Range(0.3f, 0.7f);
                        drops.Emit(ep, 1);
                    }
                }
                float ground = field.Height(t.position.x, t.position.z) + 0.05f;
                if (t.position.y < ground)
                {
                    t.position = new Vector3(t.position.x, ground, t.position.z);
                    if (c.Vel.sqrMagnitude > 3f)
                    {
                        c.Vel = new Vector3(c.Vel.x * 0.45f, -c.Vel.y * 0.3f, c.Vel.z * 0.45f);
                        c.Spin *= 0.5f;
                        AddSplat(t.position, Random.Range(0.45f, 0.9f));
                    }
                    else
                    {
                        c.Rest = true;
                        if (c.Trail >= 0f) AddPool(t.position, 0.75f);
                    }
                }
            }
        }

        public static void Draw() => I?.DrawDecals();

        void DrawDecals()
        {
            float now = Time.time;
            foreach (var d in splats) d.Age(now);
            pools.Age(now);
            scorch.Age(now);
            foreach (var d in splats) DrawSet(d);
            DrawSet(pools);
            DrawSet(scorch);
        }

        void DrawSet(DecalSet d)
        {
            if (d.Count == 0) return;
            d.Block.SetFloatArray("_Alpha", d.A);
            Graphics.DrawMeshInstanced(quad, 0, d.Mat, d.M, d.Count, d.Block, ShadowCastingMode.Off, true);
        }
    }
}
