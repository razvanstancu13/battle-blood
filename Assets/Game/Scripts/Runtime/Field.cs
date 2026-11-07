using System.Collections.Generic;
using UnityEngine;

namespace BattleBlood
{
    /// <summary>The random battlefield: height, ground mix, swamp water, the blood soaking into the ground, and the static obstacles.</summary>
    public class Field
    {
        public const float Half = 200f;
        public const float Limit = Half - 14f;      // units stay inside this
        const int Cells = 320;
        const float Step = 2f * Half / Cells;
        const int ChunkCells = 20;
        public const float WaterLevel = 0f;

        readonly float[] h = new float[(Cells + 1) * (Cells + 1)];
        readonly float swampAmount, hillAmount, ox, oz;
        public Transform Root { get; private set; }
        public float SwampShare { get; private set; }
        public float HillShare { get; private set; }

        // Blood soaking into the ground (also read by the grass and the water).
        const int BloodRes = 768;
        readonly byte[] blood = new byte[BloodRes * BloodRes * 2];     // R blood, G burnt
        Texture2D bloodTex;
        bool bloodDirty;
        float bloodFlush, bloodDry;
        readonly byte[] bloodAge = new byte[BloodRes * BloodRes];       // per texel: counts up every quarter second after the last blood; at 255 it starts to dry
        const byte BloodWetTicks = 255 - 20;                               // 20 quarter seconds = 5 s fresh (Gore.BloodHold)
        const int BloodDryPerSecond = 64;
        readonly byte[] burnAge = new byte[BloodRes * BloodRes];        // the same for the burnt ground (green channel)
        const byte BurnWetTicks = 255 - 44;                                // 11 s black, then it fades out                                  // then full red is gone in 4 s

        // Obstacles: circles in a coarse grid.
        public struct Obstacle { public Vector2 Pos; public float Radius, Top; public BurningProp Prop; }
        const float ObsCell = 10f;
        readonly Dictionary<int, List<Obstacle>> obstacles = new();

        public Field(int seed)
        {
            var r = new System.Random(seed);
            ox = (float)r.NextDouble() * 900f + 50f;
            oz = (float)r.NextDouble() * 900f + 50f;
            // Every battle mixes plains, hills and swamp differently.
            swampAmount = 0.25f + (float)r.NextDouble() * 0.75f;
            hillAmount = 0.3f + (float)r.NextDouble() * 0.7f;
            int wet = 0, high = 0, n = (Cells + 1) * (Cells + 1);
            for (int z = 0; z <= Cells; z++)
                for (int x = 0; x <= Cells; x++)
                {
                    float wx = -Half + x * Step, wz = -Half + z * Step;
                    float v = Raw(wx, wz);
                    h[x + z * (Cells + 1)] = v;
                    if (v < WaterLevel) wet++;
                    if (v > 7f) high++;
                }
            SwampShare = wet / (float)n;
            HillShare = high / (float)n;
        }

        float SwampMask(float x, float z)
        {
            float s = Mathx.Fbm(ox + x * 0.0065f, oz + z * 0.0065f, 3);
            return Mathx.SmoothStep(0.44f - swampAmount * 0.14f, 0.54f - swampAmount * 0.14f, -s);
        }

        /// <summary>The height at any point, inside the field or in the distant hills beyond it.</summary>
        float Raw(float x, float z)
        {
            float wx = x + Mathx.Fbm(ox + x * 0.01f, oz + z * 0.01f, 2) * 18f;   // domain warp, so hills are not noise blobs
            float wz = z + Mathx.Fbm(oz - z * 0.01f, ox - x * 0.01f, 2) * 18f;
            float plains = Mathx.Fbm(ox + wx * 0.012f, oz + wz * 0.012f, 4) * 2.4f;
            float hillMask = Mathx.SmoothStep(-0.08f, 0.38f, Mathx.Fbm(oz + wx * 0.0052f, ox + wz * 0.0052f, 3) + (hillAmount - 0.6f) * 0.4f);
            float ridge = 1f - Mathf.Abs(Mathx.Fbm(ox * 2f + wx * 0.0085f, oz * 2f + wz * 0.0085f, 4));
            float hills = hillMask * ridge * ridge * 24f;
            // The ground the armies meet on rolls gently; steep hills are kept to the sides.
            float centre = 1f - Mathx.SmoothStep(35f, 120f, Mathf.Abs(x)) * Mathx.SmoothStep(0f, 1f, 1f);
            float armyLane = Mathf.Lerp(1f, 0.4f, centre * (1f - Mathx.SmoothStep(100f, 170f, Mathf.Abs(z))));
            float v = 2.7f + plains + hills * armyLane;
            float swamp = SwampMask(x, z);
            float bed = -0.5f + Mathx.Fbm(ox - wx * 0.05f, oz - wz * 0.05f, 3) * 1.1f;   // wadeable, with mud banks breaking the surface
            v = Mathf.Lerp(v, bed, swamp);
            // Beyond the field the land rises to a horizon of hills.
            float r = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
            if (r > Half - 20f)
            {
                float t = (r - (Half - 20f)) / 700f;
                v += Mathf.Sqrt(Mathf.Max(t, 0f)) * 52f * (0.55f + 0.45f * Mathx.Fbm(ox + x * 0.004f, oz + z * 0.004f, 3));
            }
            return v;
        }

        public float Height(float x, float z)
        {
            float fx = Mathf.Clamp((x + Half) / Step, 0f, Cells - 0.001f);
            float fz = Mathf.Clamp((z + Half) / Step, 0f, Cells - 0.001f);
            int ix = (int)fx, iz = (int)fz;
            float tx = fx - ix, tz = fz - iz;
            int s = Cells + 1, i = ix + iz * s;
            float a = h[i], b = h[i + 1], c = h[i + s], d = h[i + s + 1];
            // The same triangle split as the mesh, so feet sit exactly on the surface.
            return tx + tz < 1f ? a + (b - a) * tx + (c - a) * tz : d + (c - d) * (1f - tx) + (b - d) * (1f - tz);
        }

        public Vector3 Normal(float x, float z)
        {
            const float e = 1.25f;
            return new Vector3(Height(x - e, z) - Height(x + e, z), 2f * e, Height(x, z - e) - Height(x, z + e)).normalized;
        }

        public Vector3 Ground(Vector3 p) { p.y = Height(p.x, p.z); return p; }
        public bool InWater(float x, float z) => Height(x, z) < WaterLevel - 0.08f;
        public float WaterDepth(float x, float z) => Mathf.Max(0f, WaterLevel - Height(x, z));
        public bool Muddy(float x, float z) => Height(x, z) < WaterLevel + 0.5f;

        /// <summary>Roughly how far across the ground to the nearest swamp water (rings 4 m apart, out to 60 m; beyond that, 999).</summary>
        public float DistanceToSwamp(Vector3 p)
        {
            if (Height(p.x, p.z) < WaterLevel) return 0f;
            for (float r = 4f; r <= 60f; r += 4f)
            {
                int n = Mathf.Max(8, Mathf.RoundToInt(r * 0.8f));
                for (int i = 0; i < n; i++)
                {
                    float a = i * Mathf.PI * 2f / n;
                    if (Height(p.x + Mathf.Cos(a) * r, p.z + Mathf.Sin(a) * r) < WaterLevel) return r;
                }
            }
            return 999f;
        }

        // ---- building the meshes ----

        public void Build()
        {
            Root = new GameObject("Field").transform;
            var mat = new Material(Art.Shader("BBTerrain")) { name = "m_ground" };
            // Each ground type is a stack of generated tiles; the shader drifts from one to another across the land.
            foreach (var (prop, tex) in new[] { ("_Grass", "grass"), ("_Dry", "grass_dry"), ("_Dirt", "dirt"), ("_Mud", "mud"), ("_Rock", "rock"), ("_Moss", "moss") })
            {
                var arr = Art.TexArray(tex, false);
                mat.SetTexture(prop, arr);
                mat.SetFloat(prop + "Count", arr != null ? arr.depth : 1);
                if (prop != "_Moss") mat.SetTexture(prop + "N", Art.TexArray(tex, true));
                Debug.Log($"[FIELD] {tex}: {(arr != null ? arr.depth : 0)} tiles");
            }
            mat.SetFloat("_Scale", 3.6f);
            Shader.SetGlobalTexture("_BB_BloodTile", Art.Tex("blood_tile"));

            bloodTex = new Texture2D(BloodRes, BloodRes, TextureFormat.RG16, false, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "blood map" };
            bloodTex.SetPixelData(blood, 0);
            bloodTex.Apply(false);
            Shader.SetGlobalTexture("_BB_BloodMap", bloodTex);
            float k = 1f / (2f * Half);
            Shader.SetGlobalVector("_BB_FieldToUv", new Vector4(k, k, 0.5f, 0.5f));

            int chunks = Cells / ChunkCells;
            for (int cz = 0; cz < chunks; cz++)
                for (int cx = 0; cx < chunks; cx++)
                    AddMesh($"ground {cx} {cz}", BuildChunk(cx, cz), mat, true);
            AddMesh("distant land", BuildHorizon(), mat, false);
            BuildWater();
        }

        void AddMesh(string name, Mesh m, Material mat, bool shadows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(Root, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
        }

        Color Mix(float x, float z, float height, float slope)
        {
            float rock = Mathx.SmoothStep(0.42f, 0.72f, slope + Mathx.Fbm(ox + x * 0.09f, oz + z * 0.09f, 2) * 0.12f);
            float wet = Mathx.SmoothStep(0.55f, 0.1f, height + Mathx.Fbm(oz + x * 0.07f, ox + z * 0.07f, 2) * 0.5f);
            float path = Mathf.Abs(Mathx.Fbm(oz + x * 0.0075f, ox - z * 0.0075f, 2));
            float dirt = Mathx.SmoothStep(0.035f, 0.01f, path) * 0.6f;                                   // old cart tracks
            dirt = Mathf.Max(dirt, Mathx.SmoothStep(0.15f, 0.55f, Mathx.Fbm(ox + x * 0.05f, oz + z * 0.05f, 3)) * 0.22f);
            float dry = Mathx.SmoothStep(-0.05f, 0.3f, Mathx.Fbm(oz + x * 0.018f, ox + z * 0.018f, 3)) * 0.38f;
            rock = Mathf.Clamp01(rock);
            wet = Mathf.Clamp01(wet) * (1f - rock);
            dirt *= (1f - rock) * (1f - wet);
            dry *= (1f - rock) * (1f - wet) * (1f - dirt);
            return new Color(dry, dirt, wet, rock);
        }

        Mesh BuildChunk(int cx, int cz)
        {
            int n = ChunkCells + 1;
            var v = new Vector3[n * n]; var nr = new Vector3[n * n]; var c = new Color[n * n];
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    int gx = cx * ChunkCells + x, gz = cz * ChunkCells + z;
                    float wx = -Half + gx * Step, wz = -Half + gz * Step;
                    float y = h[gx + gz * (Cells + 1)];
                    int i = x + z * n;
                    v[i] = new Vector3(wx, y, wz);
                    nr[i] = Normal(wx, wz);
                    c[i] = Mix(wx, wz, y, 1f - nr[i].y + (1f - nr[i].y) * 2.2f);
                }
            var t = new int[ChunkCells * ChunkCells * 6];
            int k = 0;
            for (int z = 0; z < ChunkCells; z++)
                for (int x = 0; x < ChunkCells; x++)
                {
                    int a = x + z * n;
                    t[k++] = a; t[k++] = a + n; t[k++] = a + 1;
                    t[k++] = a + 1; t[k++] = a + n; t[k++] = a + n + 1;
                }
            var m = new Mesh { name = "ground" };
            m.vertices = v; m.normals = nr; m.colors = c; m.triangles = t;
            m.RecalculateBounds();
            return m;
        }

        /// <summary>The land around the field out to the horizon: coarse, and sunk a little under the field where they overlap.</summary>
        Mesh BuildHorizon()
        {
            const int n = 96; const float size = 3200f;
            var v = new Vector3[n * n]; var c = new Color[n * n];
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    // Spacing widens outwards: fine near the field, wide at the horizon.
                    float sx = (x / (n - 1f)) * 2f - 1f, sz = (z / (n - 1f)) * 2f - 1f;
                    float wx = Mathf.Sign(sx) * Mathf.Pow(Mathf.Abs(sx), 2.2f) * size, wz = Mathf.Sign(sz) * Mathf.Pow(Mathf.Abs(sz), 2.2f) * size;
                    float y = Raw(wx, wz);
                    float r = Mathf.Max(Mathf.Abs(wx), Mathf.Abs(wz));
                    if (r < Half - 2f) y -= 6f;
                    v[x + z * n] = new Vector3(wx, y, wz);
                    c[x + z * n] = new Color(Mathf.PerlinNoise(wx * 0.004f, wz * 0.004f) * 0.35f, 0.02f, 0f, Mathf.InverseLerp(38f, 70f, y) * 0.5f);
                }
            var t = new List<int>((n - 1) * (n - 1) * 6);
            for (int z = 0; z < n - 1; z++)
                for (int x = 0; x < n - 1; x++)
                {
                    int a = x + z * n;
                    t.Add(a); t.Add(a + n); t.Add(a + 1);
                    t.Add(a + 1); t.Add(a + n); t.Add(a + n + 1);
                }
            var m = new Mesh { name = "horizon", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.vertices = v; m.colors = c; m.SetTriangles(t, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        void BuildWater()
        {
            // Only where the ground is near or under the level. Vertex colour carries the depth for the shader.
            const int g = 160; float s = 2f * Half / g;
            var v = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>();
            for (int z = 0; z < g; z++)
                for (int x = 0; x < g; x++)
                {
                    float wx = -Half + x * s, wz = -Half + z * s;
                    if (Height(wx + s / 2, wz + s / 2) > WaterLevel + 0.6f && Height(wx, wz) > WaterLevel + 0.4f && Height(wx + s, wz + s) > WaterLevel + 0.4f) continue;
                    int b = v.Count;
                    for (int j = 0; j < 2; j++)
                        for (int i = 0; i < 2; i++)
                        {
                            float px = wx + i * s, pz = wz + j * s;
                            v.Add(new Vector3(px, WaterLevel, pz));
                            c.Add(new Color(Mathf.Clamp01((WaterLevel - Height(px, pz)) * 1.4f + 0.04f), 0, 0, 1));
                        }
                    t.Add(b); t.Add(b + 2); t.Add(b + 1); t.Add(b + 1); t.Add(b + 2); t.Add(b + 3);
                }
            if (v.Count == 0) return;
            var m = new Mesh { name = "water", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.SetVertices(v); m.SetColors(c); m.SetTriangles(t, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            var mat = new Material(Art.Shader("BBWater")) { name = "m_water" };
            mat.SetTexture("_Ripple", Art.Tex("water_n"));
            var go = new GameObject("water");
            go.transform.SetParent(Root, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            WaterMaterial = mat;
        }

        public Material WaterMaterial { get; private set; }

        // ---- blood on the ground ----

        public void PaintBlood(Vector3 p, float radius, float amount)
        {
            float k = BloodRes / (2f * Half);
            int cx = (int)((p.x + Half) * k), cz = (int)((p.z + Half) * k), rad = Mathf.Max(1, (int)(radius * k));
            int rr = rad * rad;
            for (int z = Mathf.Max(0, cz - rad); z <= Mathf.Min(BloodRes - 1, cz + rad); z++)
                for (int x = Mathf.Max(0, cx - rad); x <= Mathf.Min(BloodRes - 1, cx + rad); x++)
                {
                    int dx = x - cx, dz = z - cz, d = dx * dx + dz * dz;
                    if (d > rr) continue;
                    int i = (x + z * BloodRes) * 2;
                    float add = amount * 255f * (1f - d / (float)rr);
                    blood[i] = (byte)Mathf.Min(255, blood[i] + (int)add + 1);
                    bloodAge[i >> 1] = BloodWetTicks;
                }
            bloodDirty = true;
        }

        /// <summary>Burnt ground: the terrain chars and the grass there lies flat and black.</summary>
        public void PaintBurn(Vector3 p, float radius)
        {
            float k = BloodRes / (2f * Half);
            int cx = (int)((p.x + Half) * k), cz = (int)((p.z + Half) * k), rad = Mathf.Max(1, (int)(radius * k));
            int rr = rad * rad;
            for (int z = Mathf.Max(0, cz - rad); z <= Mathf.Min(BloodRes - 1, cz + rad); z++)
                for (int x = Mathf.Max(0, cx - rad); x <= Mathf.Min(BloodRes - 1, cx + rad); x++)
                {
                    int dx = x - cx, dz = z - cz, d = dx * dx + dz * dz;
                    if (d > rr) continue;
                    int i = (x + z * BloodRes) * 2 + 1;
                    blood[i] = (byte)Mathf.Max(blood[i], (int)(255f * Mathf.Clamp01(1.4f * (1f - d / (float)rr))));
                    burnAge[i >> 1] = BurnWetTicks;
                }
            bloodDirty = true;
        }

        public bool Burnt(Vector3 p)
        {
            float k = BloodRes / (2f * Half);
            int x = Mathf.Clamp((int)((p.x + Half) * k), 0, BloodRes - 1), z = Mathf.Clamp((int)((p.z + Half) * k), 0, BloodRes - 1);
            return blood[(x + z * BloodRes) * 2 + 1] > 90;
        }

        /// <summary>Can the ground here catch fire: not water, mud or bare rock.</summary>
        public bool GrassAt(Vector3 p)
        {
            float h = Height(p.x, p.z);
            return h > WaterLevel + 0.55f && Normal(p.x, p.z).y > 0.8f;
        }

        /// <summary>Share of the map with visible blood, for the test.</summary>
        public float BloodShare()
        {
            int n = 0;
            for (int i = 0; i < blood.Length; i += 14) if (blood[i] > 30) n++;
            return n / (blood.Length / 14f);
        }

        public void Tick(float dt)
        {
            // Blood soaks away: a few seconds after it was spilt, the ground's red fades back out (the burn channel stays).
            bloodDry += dt;
            if (bloodDry >= 0.25f)
            {
                int dec = Mathf.RoundToInt(bloodDry * BloodDryPerSecond);
                bloodDry = 0f;
                bool any = false;
                for (int i = 0; i < blood.Length; i += 2)
                {
                    int r = blood[i];
                    if (r == 0) continue;
                    any = true;
                    if (bloodAge[i >> 1] < 255) { bloodAge[i >> 1]++; continue; }
                    blood[i] = (byte)Mathf.Max(0, r - dec);
                }
                for (int i = 1; i < blood.Length; i += 2)
                {
                    int g = blood[i];
                    if (g == 0) continue;
                    any = true;
                    if (burnAge[i >> 1] < 255) { burnAge[i >> 1]++; continue; }
                    blood[i] = (byte)Mathf.Max(0, g - dec);
                }
                if (any) bloodDirty = true;
            }
            bloodFlush -= dt;
            if (bloodDirty && bloodFlush <= 0f)
            {
                bloodTex.SetPixelData(blood, 0);
                bloodTex.Apply(false);
                bloodDirty = false;
                bloodFlush = 0.25f;
            }
        }

        // ---- obstacles ----

        static int ObsKey(int x, int z) => (x + 1000) * 4096 + (z + 1000);

        /// <param name="flammable">The prop's object when it can burn; stone leaves this out.</param>
        public void AddObstacle(Vector3 p, float radius, float top, GameObject flammable = null)
        {
            var o = new Obstacle { Pos = new Vector2(p.x, p.z), Radius = radius, Top = p.y + top };
            if (flammable != null)
            {
                o.Prop = new BurningProp { Go = flammable, Pos = p, Radius = radius, Height = top };
                flammable.AddComponent<PropRef>().Prop = o.Prop;
            }
            int x0 = Mathf.FloorToInt((p.x - radius) / ObsCell), x1 = Mathf.FloorToInt((p.x + radius) / ObsCell);
            int z0 = Mathf.FloorToInt((p.z - radius) / ObsCell), z1 = Mathf.FloorToInt((p.z + radius) / ObsCell);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    int key = ObsKey(x, z);
                    if (!obstacles.TryGetValue(key, out var l)) obstacles[key] = l = new List<Obstacle>(4);
                    l.Add(o);
                }
        }

        /// <summary>Pushes a circle out of the obstacles. True when it touched one.</summary>
        public bool PushOut(ref Vector3 p, float radius)
        {
            int key = ObsKey(Mathf.FloorToInt(p.x / ObsCell), Mathf.FloorToInt(p.z / ObsCell));
            if (!obstacles.TryGetValue(key, out var l)) return false;
            bool hit = false;
            foreach (var o in l)
            {
                Vector2 d = new Vector2(p.x, p.z) - o.Pos;
                float min = o.Radius + radius, m2 = d.sqrMagnitude;
                if (m2 >= min * min || m2 < 1e-6f) continue;
                float m = Mathf.Sqrt(m2);
                d *= (min - m) / m;
                p.x += d.x; p.z += d.y;
                hit = true;
            }
            return hit;
        }

        /// <summary>True when an arrow at this point is inside an obstacle; prop is what it hit, if that can burn.</summary>
        public bool HitsObstacle(Vector3 p, out BurningProp prop)
        {
            prop = null;
            int key = ObsKey(Mathf.FloorToInt(p.x / ObsCell), Mathf.FloorToInt(p.z / ObsCell));
            if (!obstacles.TryGetValue(key, out var l)) return false;
            foreach (var o in l)
            {
                if (p.y > o.Top) continue;
                if ((new Vector2(p.x, p.z) - o.Pos).sqrMagnitude < o.Radius * o.Radius) { prop = o.Prop; return true; }
            }
            return false;
        }

        public bool FreeOfObstacles(Vector3 p, float radius)
        {
            Vector3 q = p;
            return !PushOut(ref q, radius);
        }
    }
}
