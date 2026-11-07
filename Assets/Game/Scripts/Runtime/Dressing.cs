using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BattleBlood
{
    /// <summary>
    /// What grows and stands on the field. Each model is generated once and placed many times: scaled, turned and tinted.
    /// Grass, flowers and reeds are instanced cards, in cells drawn only near the camera.
    /// </summary>
    public class Dressing
    {
        readonly Field field;
        readonly System.Random rng;
        readonly List<GrassCell> cells = new();
        Mesh cardMesh;
        Material grassMat, flowerMat, reedMat;
        public int PropCount { get; private set; }
        public int CardCount { get; private set; }

        class GrassCell { public Vector3 Centre; public Material Mat; public Mesh Mesh; public float MaxDist; public List<Matrix4x4[]> Batches = new(); public List<int> Counts = new(); }

        public Dressing(Field f, int seed) { field = f; rng = new System.Random(seed ^ 0x5eed); }

        /// <summary>The oaks and pines: trunks and branches with crowns of leaf cards (CardTrees).</summary>
        public readonly CardTrees Trees = new();

        public GameObject PlaceProp(string model, Vector3 pos, float yaw, float scale, Material mat) => Place(model, pos, yaw, scale, mat);

        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        // ---- props ----

        // Colour variations of one texture: the same tree reads as several.
        static readonly Color[] LeafTints = { new(1f, 1f, 1f), new(0.85f, 0.95f, 0.75f), new(1.05f, 0.92f, 0.65f), new(0.78f, 0.86f, 0.8f) };
        static readonly Color[] StoneTints = { new(1f, 1f, 1f), new(0.85f, 0.84f, 0.8f), new(0.72f, 0.74f, 0.76f) };

        readonly Dictionary<string, Material[]> tinted = new();
        Material Variant(string model, Color[] tints, int i, float smooth = 0.2f, float teamTint = 0f)
        {
            if (!tinted.TryGetValue(model, out var arr))
            {
                arr = new Material[tints.Length];
                for (int k = 0; k < tints.Length; k++)
                {
                    arr[k] = new Material(Art.ModelMat(model, teamTint, 0f, smooth));
                    arr[k].SetColor("_Color", tints[k]);
                }
                tinted[model] = arr;
            }
            return arr[i % arr.Length];
        }

        GameObject Place(string model, Vector3 pos, float yaw, float scale, Material mat, bool shadows = true, Vector3? tiltUp = null, string lod = null)
        {
            var mesh = Art.PropMesh(model);
            if (mesh == null) return null;
            var go = new GameObject(model);
            go.transform.SetParent(field.Root, false);
            go.transform.position = pos;
            var up = tiltUp ?? Vector3.up;
            go.transform.rotation = Quaternion.FromToRotation(Vector3.up, up) * Quaternion.Euler(0, yaw, 0);
            go.transform.localScale = Vector3.one * scale;
            Add(go.transform, model, mesh, mat, shadows, out var near);
            MeshRenderer far = null;
            if (lod != null)
            {
                var lodMesh = Art.PropMesh(lod);
                if (lodMesh != null)
                {
                    Add(go.transform, lod, lodMesh, mat, false, out far);
                    var group = go.AddComponent<LODGroup>();
                    const float swap = 0.16f;
                    group.SetLODs(new[] { new LOD(swap, new Renderer[] { near }), new LOD(0.005f, new Renderer[] { far }) });
                }
            }
            // Arrows stop on the prop's own surface: a mesh collider, from the lighter LOD mesh when there is one.
            var shape = far != null ? far : near;
            if (shape.GetComponent<MeshFilter>().sharedMesh.isReadable)
            {
                var col = shape.gameObject.AddComponent<MeshCollider>();
                col.sharedMesh = shape.GetComponent<MeshFilter>().sharedMesh;
                shape.gameObject.layer = Layers.Props;
            }
            PropCount++;
            return go;
        }

        static void Add(Transform parent, string model, Mesh mesh, Material mat, bool shadows, out MeshRenderer r)
        {
            var child = new GameObject("mesh");
            child.transform.SetParent(parent, false);
            var b = Art.PropBase(model);
            child.transform.localPosition = b.GetColumn(3);
            child.transform.localRotation = b.rotation;
            child.transform.localScale = b.lossyScale;
            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            r = child.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
        }

        static Material deadTreeMat;

        /// <summary>
        /// A tree that has burnt: its living mesh goes and the dead tree model stands in its place, scaled to a similar height,
        /// with its own collider for the arrows. The object (and its burning state) stays the same.
        /// </summary>
        public static void ToDeadTree(GameObject go)
        {
            float height = go.name == "pine" ? 18f : 14f;
            var lod = go.GetComponent<LODGroup>();
            if (lod != null) Object.Destroy(lod);
            foreach (Transform c in go.transform) Object.Destroy(c.gameObject);
            var mesh = Art.PropMesh("dead_tree");
            if (mesh == null) return;
            if (deadTreeMat == null) deadTreeMat = new Material(Art.ModelMat("dead_tree", 0f, 0f, 0.2f)) { name = "m_dead_tree_burnt" };
            Add(go.transform, "dead_tree", mesh, new Material(deadTreeMat), true, out var r);
            float k = height * 0.75f / Mathf.Max(1f, Art.PropBounds("dead_tree").size.y);
            r.transform.localPosition *= k; r.transform.localScale *= k;
            var col = r.gameObject.AddComponent<MeshCollider>();
            col.sharedMesh = mesh;
            r.gameObject.layer = Layers.Props;
        }

        /// <summary>A free spot away from the armies' deployment lines, or false.</summary>
        bool Spot(out Vector3 p, float clearance, bool allowLane = false, float min = -188f, float max = 188f)
        {
            for (int tries = 0; tries < 24; tries++)
            {
                float x = R(min, max), z = R(min, max);
                if (!allowLane && Mathf.Abs(x) < 95f && Mathf.Abs(Mathf.Abs(z) - 72f) < 28f) continue;
                p = new Vector3(x, field.Height(x, z), z);
                if (!field.FreeOfObstacles(p, clearance)) continue;
                return true;
            }
            p = default;
            return false;
        }

        public void BuildProps()
        {
            // A wall of pines round the edge hides where the field stops.
            for (int i = 0; i < 150; i++)
            {
                float side = R(-1f, 1f), band = Field.Half - R(0f, 17f);
                int s = i % 4;
                float x = s == 0 ? band : s == 1 ? -band : side * Field.Half;
                float z = s == 2 ? band : s == 3 ? -band : side * Field.Half;
                var p = new Vector3(x, field.Height(x, z), z);
                if (p.y < 0.3f) continue;
                if (i % 3 == 0) Trees.Oak(this, p, R(0, 360), R(0.75f, 1.2f), i, rng);
                else Trees.Pine(this, field.Root, p, R(0, 360), R(0.75f, 1.2f), i, rng);
            }
            int oaks = 34, pines = 24, deadTrees = 26, rocksA = 44, rocksB = 18, logs = 24;
            for (int i = 0; i < oaks; i++)
                if (Spot(out var p, 6f) && p.y > 0.6f)
                {
                    var g = Trees.Oak(this, p, R(0, 360), R(0.7f, 1.25f), i, rng);
                    field.AddObstacle(p, 1.1f, 6f, g);
                }
            for (int i = 0; i < pines; i++)
                if (Spot(out var p, 4f) && p.y > 1.2f)
                {
                    var g = Trees.Pine(this, field.Root, p, R(0, 360), R(0.7f, 1.2f), i, rng);
                    field.AddObstacle(p, 0.6f, 8f, g);
                }
            // Dead trees and logs want the swamp.
            for (int i = 0, tries = 0; i < deadTrees && tries < 400; tries++)
                if (Spot(out var p, 3f) && p.y < 1.4f)
                {
                    var g = Place("dead_tree", p - Vector3.up * 0.15f, R(0, 360), R(0.65f, 1.3f), Variant("dead_tree", StoneTints, i));
                    field.AddObstacle(p, 0.5f, 5f, g);
                    i++;
                }
            for (int i = 0; i < rocksA; i++)
                if (Spot(out var p, 3f))
                {
                    float s = R(0.45f, 1.6f);
                    Place("rock_a", p - Vector3.up * 0.25f * s, R(0, 360), s, Variant("rock_a", StoneTints, i), true, field.Normal(p.x, p.z));
                    field.AddObstacle(p, 1.05f * s, 2f * s);
                }
            for (int i = 0; i < rocksB; i++)
                if (Spot(out var p, 4f) && p.y > 2f)
                {
                    float s = R(0.7f, 1.8f);
                    Place("rock_b", p - Vector3.up * 0.3f, R(0, 360), s, Variant("rock_b", StoneTints, i));
                    field.AddObstacle(p, 1.4f * s, 3f * s);
                }
            for (int i = 0; i < logs; i++)
                if (Spot(out var p, 2.5f))
                    Place("log", p + Vector3.up * 0.05f, R(0, 360), R(0.7f, 1.3f), Variant("log", StoneTints, i), true, field.Normal(p.x, p.z));

            // A few ruins with smoke rising, for the tone of the place.
            for (int i = 0; i < 7; i++)
                if (Spot(out var p, 10f) && p.y > 0.8f)
                {
                    float s = R(0.9f, 1.8f);
                    Place("ruin", p - Vector3.up * 0.3f, R(0, 360), s, Variant("ruin", StoneTints, i));
                    field.AddObstacle(p, 2.8f * s, 5f * s);
                }
            Trees.Finish();
            Debug.Log($"[DRESS] trees: {Trees.Clumps} leaf clumps");
        }

        /// <summary>Camps behind each army: the tents, banners, carts and barrels are tinted by team, so they exist once each.</summary>
        public void BuildCamp(int team, Color tint, Vector3 centre, Vector3 facing, Material bannerMat)
        {
            var side = Vector3.Cross(Vector3.up, facing).normalized;
            for (int i = 0; i < 9; i++)
            {
                var p = centre + side * ((i - 4) * 15f + R(-3, 3)) - facing * R(6f, 22f);
                p = field.Ground(p);
                if (p.y < 0.4f) continue;
                var m = new Material(Art.ModelMat("tent", 1f, 0f, 0.2f));
                m.SetColor("_TintColor", tint);
                float tentScale = R(0.9f, 1.25f);
                var tentGo = Place("tent", p - Vector3.up * 0.36f * tentScale, Mathx.Yaw(facing) + R(-30, 30), tentScale, m);      // its centre pole reaches a little below the walls
                field.AddObstacle(p, 2.6f, 4f, tentGo);
                if (i % 2 == 0) MakeBanner(p + side * 3.6f + facing * 2.2f, bannerMat, 6.2f);
                if (i % 3 == 1)
                {
                    var q = field.Ground(p + side * R(-6, 6) + facing * R(3, 6));
                    var cartGo = Place("cart", q, R(0, 360), R(0.9f, 1.15f), Variant("cart", StoneTints, i), true, field.Normal(q.x, q.z));
                    field.AddObstacle(q, 1.5f, 1.8f, cartGo);
                }
                for (int b = 0; b < 3; b++)
                {
                    var q = field.Ground(p + new Vector3(R(-5, 5), 0, R(-5, 5)));
                    var barrelGo = Place("barrel", q, R(0, 360), R(0.85f, 1.1f), Variant("barrel", StoneTints, b));
                    field.AddObstacle(q, 0.42f, 1f, barrelGo);
                }
            }
            // A line of stakes in front of the camp, to give the cavalry something to go around.
            for (int i = 0; i < 12; i++)
            {
                var q = field.Ground(centre + side * ((i - 5.5f) * 9f + R(-1.5f, 1.5f)) + facing * R(5f, 7f));
                if (q.y < 0.3f) continue;
                var stakesGo = Place("stakes", q, Mathx.Yaw(side) + R(-10, 10), R(0.95f, 1.25f), Variant("stakes", StoneTints, i));
                field.AddObstacle(q, 1.1f, 1.5f, stakesGo);
            }
        }

        /// <summary>A pole and a cloth: the banner texture, tinted and blown about by the wind in the vertex-animated cloth.</summary>
        public static GameObject MakeBanner(Vector3 foot, Material bannerMat, float height)
        {
            var go = new GameObject("banner");
            go.transform.position = foot;
            var pole = new GameObject("pole");
            pole.transform.SetParent(go.transform, false);
            pole.AddComponent<MeshFilter>().sharedMesh = ProcMesh.Tube(height, 0.035f, 6);
            pole.AddComponent<MeshRenderer>().sharedMaterial = Art.TexMat("wood", 0.2f, 1f);
            var cloth = new GameObject("cloth");
            cloth.transform.SetParent(go.transform, false);
            cloth.transform.localPosition = new Vector3(0, height - 0.05f, 0);
            var f = cloth.AddComponent<MeshFilter>();
            f.sharedMesh = ProcMesh.Banner(1.05f, 2.1f, 10);
            var r = cloth.AddComponent<MeshRenderer>();
            r.sharedMaterial = bannerMat;
            cloth.AddComponent<BannerWave>();
            return go;
        }

        // ---- cards ----

        public void BuildCards()
        {
            cardMesh = ProcMesh.CrossCards();
            grassMat = CardMat("grass_card", new Color(0.85f, 0.95f, 0.8f), 0.14f, 85f);
            flowerMat = CardMat("flower_card", Color.white, 0.14f, 70f);
            reedMat = CardMat("reed_card", new Color(0.85f, 0.9f, 0.75f), 0.08f, 95f);
            BuildBushes();
            const float cell = 24f;
            int n = Mathf.CeilToInt(2f * Field.Half / cell);
            for (int cz = 0; cz < n; cz++)
                for (int cx = 0; cx < n; cx++)
                {
                    float x0 = -Field.Half + cx * cell, z0 = -Field.Half + cz * cell;
                    var grass = new List<Matrix4x4>(); var flowers = new List<Matrix4x4>(); var reeds = new List<Matrix4x4>();
                    for (int i = 0; i < 560; i++)
                    {
                        float x = x0 + R(0, cell), z = z0 + R(0, cell), y = field.Height(x, z);
                        float dens = Mathf.PerlinNoise(x * 0.04f + 13f, z * 0.04f + 7f);
                        var nor = field.Normal(x, z);
                        if (nor.y < 0.84f) continue;
                        if (y < Field.WaterLevel + 0.45f && y > Field.WaterLevel - 0.5f)
                        {
                            if (i % 14 == 0) reeds.Add(Matrix4x4.TRS(new Vector3(x, y - 0.05f, z), Quaternion.Euler(0, R(0, 360), 0), new Vector3(R(0.9f, 1.5f), R(1.4f, 2.5f), R(0.9f, 1.5f))));
                            continue;
                        }
                        if (y < Field.WaterLevel + 0.45f) continue;
                        if (dens < 0.18f) continue;
                        var rot = Quaternion.Euler(R(-6, 6), R(0, 360), R(-6, 6));
                        var scale = new Vector3(R(0.8f, 1.5f), R(0.6f, 1.2f), R(0.8f, 1.5f));
                        (i % 37 == 0 ? flowers : grass).Add(Matrix4x4.TRS(new Vector3(x, y - 0.03f, z), rot, scale));
                    }
                    AddCell(new Vector3(x0 + cell / 2, 0, z0 + cell / 2), grassMat, grass);
                    AddCell(new Vector3(x0 + cell / 2, 0, z0 + cell / 2), flowerMat, flowers);
                    AddCell(new Vector3(x0 + cell / 2, 0, z0 + cell / 2), reedMat, reeds);
                }
        }

        /// <summary>Bushes: one clump of leaf cards, instanced in a few tints, each scaled and turned. They do not block anyone: men push through them.</summary>
        void BuildBushes()
        {
            var clump = ProcMesh.CardClump(26, 0.62f, 0.62f, 91);
            var mats = new Material[LeafTints.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = CardMat("leaf_card", LeafTints[i] * 0.9f, 0.035f, 150f);
            const float cell = 48f;
            var groups = new Dictionary<(int, int, int), List<Matrix4x4>>();
            for (int i = 0; i < 170; i++)
            {
                if (!Spot(out var p, 1.2f, true) || p.y < 0.25f || field.Normal(p.x, p.z).y < 0.8f) continue;
                float s = R(0.9f, 2.1f);
                var m = Matrix4x4.TRS(p - Vector3.up * 0.08f * s, Quaternion.Euler(0, R(0, 360), 0), new Vector3(s * R(0.9f, 1.2f), s * R(0.7f, 1f), s * R(0.9f, 1.2f)));
                var key = (Mathf.FloorToInt(p.x / cell), Mathf.FloorToInt(p.z / cell), i % mats.Length);
                if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<Matrix4x4>();
                list.Add(m);
                BushCount++;
            }
            foreach (var g in groups)
                AddCell(new Vector3((g.Key.Item1 + 0.5f) * cell, 0, (g.Key.Item2 + 0.5f) * cell), mats[g.Key.Item3], g.Value, clump, 165f);
        }
        public int BushCount { get; private set; }

        static Material CardMat(string tex, Color tint, float wind, float fade)
        {
            var m = new Material(Art.Shader("BBFoliage")) { name = "m_" + tex, enableInstancing = true };
            m.SetTexture("_BaseMap", Art.Tex(tex));
            m.SetColor("_Color", tint);
            m.SetFloat("_Wind", wind);
            m.SetFloat("_FadeDist", fade);
            return m;
        }

        void AddCell(Vector3 centre, Material mat, List<Matrix4x4> list, Mesh mesh = null, float maxDist = 110f)
        {
            if (list.Count == 0) return;
            var c = new GrassCell { Centre = centre, Mat = mat, Mesh = mesh != null ? mesh : cardMesh, MaxDist = maxDist };
            c.Batches.Add(list.ToArray());
            c.Counts.Add(list.Count);
            cells.Add(c);
            CardCount += list.Count;
        }

        /// <summary>Draws the cells near the camera. One draw call per cell and card type.</summary>
        public void DrawCards(Camera cam)
        {
            Trees.Draw(cam);
            if (cardMesh == null || cam == null) return;
            Vector3 cp = cam.transform.position;
            Vector3 cf = cam.transform.forward;
            foreach (var c in cells)
            {
                float dx = c.Centre.x - cp.x, dz = c.Centre.z - cp.z;
                float d2 = dx * dx + dz * dz;
                if (d2 > c.MaxDist * c.MaxDist) continue;
                // Behind the camera, except the cells it stands in.
                if (d2 > 40f * 40f && dx * cf.x + dz * cf.z < 0f) continue;
                for (int b = 0; b < c.Batches.Count; b++)
                    Graphics.DrawMeshInstanced(c.Mesh, 0, c.Mat, c.Batches[b], c.Counts[b], null, ShadowCastingMode.Off, true);
            }
        }
    }

    /// <summary>Wind on a banner: a travelling wave along the fly end. Only near banners get updated.</summary>
    public class BannerWave : MonoBehaviour
    {
        Mesh mesh;
        Vector3[] rest, cur;
        float phase;
        void Start()
        {
            var f = GetComponent<MeshFilter>();
            mesh = Instantiate(f.sharedMesh);
            mesh.MarkDynamic();
            f.sharedMesh = mesh;
            rest = mesh.vertices;
            cur = new Vector3[rest.Length];
            phase = transform.position.x * 0.37f;
        }
        void Update()
        {
            var cam = Camera.main;
            if (cam == null || (cam.transform.position - transform.position).sqrMagnitude > 170f * 170f) return;
            float t = Time.time * 2.6f + phase;
            for (int i = 0; i < rest.Length; i++)
            {
                var v = rest[i];
                float along = v.x, down = -v.y;
                v.z = Mathf.Sin(along * 3.1f - t + down * 0.5f) * 0.14f * (0.2f + along) + Mathf.Sin(along * 7f - t * 1.7f) * 0.03f;
                v.y += Mathf.Sin(along * 2.1f - t * 0.8f) * 0.04f * along;
                cur[i] = v;
            }
            mesh.vertices = cur;
            mesh.RecalculateNormals();
        }
    }

    /// <summary>Meshes made in code, textured with the generated tiles: the things that must bend or are too thin to survive image-to-3D.</summary>
    public static class ProcMesh
    {
        static Mesh cross;
        public static Mesh CrossCards()
        {
            if (cross != null) return cross;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int k = 0; k < 3; k++)
            {
                float a = k * 60f * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 0.6f;
                int b = v.Count;
                v.Add(-dir); v.Add(dir); v.Add(-dir + Vector3.up); v.Add(dir + Vector3.up);
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(0, 1)); uv.Add(new Vector2(1, 1));
                t.Add(b); t.Add(b + 2); t.Add(b + 1); t.Add(b + 1); t.Add(b + 2); t.Add(b + 3);
            }
            cross = new Mesh { name = "cards" };
            cross.SetVertices(v); cross.SetUVs(0, uv); cross.SetTriangles(t, 0);
            cross.RecalculateNormals();
            cross.bounds = new Bounds(Vector3.up * 0.5f, new Vector3(3, 2, 3));
            return cross;
        }

        /// <summary>A clump of leaf cards facing every way over a dome: seen from any side it reads as a solid bush with a broken outline.</summary>
        public static Mesh CardClump(int cards, float radius, float cardSize, int seed)
        {
            var r = new System.Random(seed);
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int k = 0; k < cards; k++)
            {
                // Evenly over a dome, by the golden angle; the height is what makes it a mound.
                float f = (k + 0.5f) / cards;
                float y = Mathf.Lerp(0.05f, 1f, Mathf.Sqrt(f));
                float rad = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
                float a = k * 2.39996f;
                var centre = new Vector3(Mathf.Cos(a) * rad, y * 0.95f, Mathf.Sin(a) * rad) * radius;
                var normal = (centre + Vector3.up * 0.1f).normalized;
                var side = Vector3.Cross(normal, new Vector3((float)r.NextDouble() - 0.5f, 0.3f, (float)r.NextDouble() - 0.5f)).normalized;
                var up = Vector3.Cross(side, normal).normalized;
                float s = cardSize * (0.8f + (float)r.NextDouble() * 0.5f);
                int b = v.Count;
                v.Add(centre - side * s - up * s); v.Add(centre + side * s - up * s); v.Add(centre - side * s + up * s); v.Add(centre + side * s + up * s);
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(0, 1)); uv.Add(new Vector2(1, 1));
                t.Add(b); t.Add(b + 2); t.Add(b + 1); t.Add(b + 1); t.Add(b + 2); t.Add(b + 3);
            }
            var m = new Mesh { name = "bush clump" };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateNormals();
            m.bounds = new Bounds(Vector3.up * radius * 0.5f, Vector3.one * (radius + cardSize) * 3f);
            return m;
        }

        public static Mesh Tube(float length, float radius, int sides)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int i = 0; i <= sides; i++)
            {
                float a = i / (float)sides * Mathf.PI * 2f;
                var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                v.Add(d * radius); v.Add(d * radius + Vector3.up * length);
                n.Add(d); n.Add(d);
                uv.Add(new Vector2(i / (float)sides, 0)); uv.Add(new Vector2(i / (float)sides, length * 2f));
            }
            for (int i = 0; i < sides; i++)
            {
                int b = i * 2;
                t.Add(b); t.Add(b + 1); t.Add(b + 2); t.Add(b + 2); t.Add(b + 1); t.Add(b + 3);
            }
            var m = new Mesh { name = "tube" };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>A cloth hanging from its top edge, with x running from the pole out.</summary>
        public static Mesh Banner(float width, float height, int seg)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int y = 0; y <= seg; y++)
                for (int x = 0; x <= seg / 2; x++)
                {
                    float u = x / (seg / 2f), w = y / (float)seg;
                    v.Add(new Vector3(u * width, -w * height, 0));
                    uv.Add(new Vector2(u, 1f - w));
                }
            int row = seg / 2 + 1;
            for (int y = 0; y < seg; y++)
                for (int x = 0; x < seg / 2; x++)
                {
                    int a = x + y * row;
                    t.Add(a); t.Add(a + 1); t.Add(a + row); t.Add(a + 1); t.Add(a + row + 1); t.Add(a + row);
                }
            var m = new Mesh { name = "banner" };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        /// <summary>An arrow along +Z from the nock at 0: a shaft, a steel head and three feather cards.</summary>
        public static Mesh Arrow(out Mesh feathers)
        {
            const float len = 0.82f, r = 0.0055f;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            const int sides = 5;
            for (int i = 0; i <= sides; i++)
            {
                float a = i / (float)sides * Mathf.PI * 2f;
                var d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
                v.Add(d * r); v.Add(d * r + Vector3.forward * (len - 0.06f));
                n.Add(d); n.Add(d);
                uv.Add(new Vector2(i * 0.2f, 0)); uv.Add(new Vector2(i * 0.2f, 1.5f));
            }
            for (int i = 0; i < sides; i++)
            {
                int b = i * 2;
                t.Add(b); t.Add(b + 2); t.Add(b + 1); t.Add(b + 2); t.Add(b + 3); t.Add(b + 1);
            }
            // The head: a thin four-sided point.
            int tip = v.Count;
            v.Add(Vector3.forward * len); n.Add(Vector3.forward); uv.Add(Vector2.zero);
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * 0.5f;
                var d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
                v.Add(d * (i % 2 == 0 ? 0.018f : 0.005f) + Vector3.forward * (len - 0.07f)); n.Add(d); uv.Add(Vector2.zero);
            }
            for (int i = 0; i < 4; i++) { t.Add(tip); t.Add(tip + 1 + (i + 1) % 4); t.Add(tip + 1 + i); }
            var m = new Mesh { name = "arrow" };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateBounds();

            var fv = new List<Vector3>(); var fuv = new List<Vector2>(); var ft = new List<int>();
            for (int k = 0; k < 3; k++)
            {
                float a = k * 120f * Mathf.Deg2Rad;
                var d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * 0.024f;
                int b = fv.Count;
                fv.Add(Vector3.forward * 0.015f); fv.Add(Vector3.forward * 0.13f); fv.Add(Vector3.forward * 0.015f + d); fv.Add(Vector3.forward * 0.13f + d);
                fuv.Add(new Vector2(0, 0.35f)); fuv.Add(new Vector2(1, 0.35f)); fuv.Add(new Vector2(0, 0.85f)); fuv.Add(new Vector2(1, 0.85f));
                ft.Add(b); ft.Add(b + 2); ft.Add(b + 1); ft.Add(b + 1); ft.Add(b + 2); ft.Add(b + 3);
            }
            feathers = new Mesh { name = "fletching" };
            feathers.SetVertices(fv); feathers.SetUVs(0, fuv); feathers.SetTriangles(ft, 0);
            feathers.RecalculateNormals(); feathers.RecalculateBounds();
            return m;
        }

        /// <summary>A recurved longbow in the XY plane (limbs along Y, belly towards -Z), its grip at the origin.</summary>
        public static Mesh Bow(float height)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            const int segs = 16, sides = 5;
            for (int s = 0; s <= segs; s++)
            {
                float w = s / (float)segs * 2f - 1f;       // -1 bottom tip, 1 top tip
                float y = w * height * 0.5f;
                float z = -0.13f * (1f - w * w) + 0.05f * w * w * w * w;     // bent away from the archer, tips recurved
                float r = Mathf.Lerp(0.018f, 0.007f, Mathf.Abs(w));
                for (int i = 0; i <= sides; i++)
                {
                    float a = i / (float)sides * Mathf.PI * 2f;
                    var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    v.Add(new Vector3(0, y, -z) + d * r);
                    n.Add(d);
                    uv.Add(new Vector2(i * 0.3f, s * 0.4f));
                }
            }
            int row = sides + 1;
            for (int s = 0; s < segs; s++)
                for (int i = 0; i < sides; i++)
                {
                    int a = i + s * row;
                    t.Add(a); t.Add(a + row); t.Add(a + 1); t.Add(a + 1); t.Add(a + row); t.Add(a + row + 1);
                }
            var m = new Mesh { name = "bow" };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }
    }
}
