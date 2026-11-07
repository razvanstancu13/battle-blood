using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BattleBlood
{
    /// <summary>
    /// Trees in the grass's style: a real trunk and branches, the crown made of photographic leaf cards.
    /// An oak is the generated dead tree (its bark tinted living) with clumps of oak-leaf cards along its upper branches; a pine is a
    /// tapered trunk of pine bark ringed by tiers of needle clumps narrowing to the top. The clumps are instanced by map cell, sway,
    /// cast shadows, and are hidden when the tree burns, leaving it standing bare.
    /// </summary>
    public class CardTrees
    {
        class Cell { public Vector3 Centre; public Material Mat; public Mesh Mesh; public readonly List<Matrix4x4> Build = new(); public List<Matrix4x4[]> Batches = new(); }
        readonly Dictionary<(int, int, Mesh, Material), Cell> cells = new();
        readonly Dictionary<GameObject, List<(Cell cell, int index)>> leavesOf = new();
        readonly List<Cell> ordered = new();
        const float CellSize = 48f;
        public int Clumps { get; private set; }

        static Mesh oakClump, pineClump, pineTrunk;
        static Material[] oakMats, pineMats;
        static Material barkMat, oakBark;
        static List<Vector3> oakAnchors;
        static readonly Color[] Tints = { new(1f, 1f, 1f), new(0.88f, 0.96f, 0.8f), new(1.02f, 0.94f, 0.72f), new(0.8f, 0.9f, 0.84f) };

        static void Shared()
        {
            if (oakClump != null) return;
            oakClump = LeafClump(18, 1f, 0.62f, 0.25f, 11);
            pineClump = LeafClump(16, 1f, 0.6f, 0.8f, 23);
            pineTrunk = Trunk(1f, 0.4f, 0.07f, 8);
            oakMats = new Material[Tints.Length]; pineMats = new Material[Tints.Length];
            for (int i = 0; i < Tints.Length; i++)
            {
                oakMats[i] = LeafMat("oak_card", Tints[i]);
                pineMats[i] = LeafMat("pine_card", Tints[i] * 1.6f);      // the pine card photo is dark (about 40/255): brought up to a deep green, still darker than the oaks
            }
            barkMat = new Material(Art.TexMat("pine_bark", 0.15f, 1f)) { name = "m_pine_trunk" };
            oakBark = new Material(Art.ModelMat("dead_tree", 0f, 0f, 0.15f)) { name = "m_oak_bark" };
            oakBark.SetColor("_Color", new Color(0.62f, 0.52f, 0.42f));
            oakAnchors = Anchors(Art.PropMesh("dead_tree"), Art.PropBase("dead_tree"), 42, 1.25f);
        }

        static Material LeafMat(string tex, Color tint)
        {
            var m = new Material(Art.Shader("BBLeaves")) { name = "m_" + tex, enableInstancing = true };
            m.SetTexture("_BaseMap", Art.Tex(tex));
            m.SetColor("_Color", tint);
            return m;
        }

        /// <summary>A clump of leaf cards filling a ball (flatness squashes it into a disc for pine sprays). Normals point out from the middle; vertex red is how far out.</summary>
        static Mesh LeafClump(int cards, float radius, float cardSize, float flatness, int seed)
        {
            var r = new System.Random(seed);
            float Rn() => (float)r.NextDouble();
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var c = new List<Color>(); var t = new List<int>();
            for (int k = 0; k < cards; k++)
            {
                // Spread through the ball, more towards its surface, where leaves catch the light.
                Vector3 dir = new Vector3(Rn() * 2 - 1, (Rn() * 2 - 1) * (1f - flatness), Rn() * 2 - 1).normalized;
                float dist = Mathf.Lerp(0.3f, 1f, Mathf.Sqrt(Rn())) * radius;
                Vector3 centre = dir * dist;
                Vector3 a = new Vector3(Rn() - 0.5f, Rn() - 0.5f, Rn() - 0.5f).normalized;
                Vector3 side = Vector3.Cross(dir, a).normalized, up = Vector3.Cross(side, dir).normalized;
                if (flatness > 0.5f)
                {
                    // Pine sprays droop outward and down, 35 to 70 degrees from flat, so they show from the side as well as from above.
                    Vector3 outward = new Vector3(centre.x, 0f, centre.z).sqrMagnitude > 0.01f ? new Vector3(centre.x, 0f, centre.z).normalized : new Vector3(a.x, 0f, a.z).normalized;
                    side = Vector3.Cross(Vector3.up, outward).normalized;
                    float tilt = Mathf.Lerp(35f, 70f, Rn()) * Mathf.Deg2Rad;
                    up = (outward * Mathf.Cos(tilt) - Vector3.up * Mathf.Sin(tilt)).normalized;
                    Vector3 twist = Quaternion.AngleAxis((Rn() - 0.5f) * 50f, up) * side;
                    side = twist.normalized;
                }
                float s = cardSize * (0.8f + Rn() * 0.5f);
                int b = v.Count;
                v.Add(centre - side * s - up * s); v.Add(centre + side * s - up * s); v.Add(centre - side * s + up * s); v.Add(centre + side * s + up * s);
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(0, 1)); uv.Add(new Vector2(1, 1));
                for (int q = 0; q < 4; q++) { n.Add(((v[b + q]) + Vector3.up * 0.25f * radius).normalized); c.Add(new Color(dist / radius, 0, 0, 1)); }
                t.Add(b); t.Add(b + 2); t.Add(b + 1); t.Add(b + 1); t.Add(b + 2); t.Add(b + 3);
            }
            var m = new Mesh { name = "leaf clump" };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetColors(c); m.SetTriangles(t, 0);
            m.bounds = new Bounds(Vector3.zero, Vector3.one * (radius + cardSize) * 2.4f);
            return m;
        }

        /// <summary>A tapered round trunk, 1 high (scaled per tree), bark wrapped twice round, tiled up it.</summary>
        static Mesh Trunk(float height, float baseRadius, float topRadius, int sides)
        {
            const int rings = 6;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int y = 0; y <= rings; y++)
            {
                float f = y / (float)rings, rad = Mathf.Lerp(baseRadius, topRadius, f);
                for (int s = 0; s <= sides; s++)
                {
                    float a = s / (float)sides * Mathf.PI * 2f;
                    var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    v.Add(d * rad + Vector3.up * f * height); n.Add(d); uv.Add(new Vector2(s / (float)sides * 2f, f * 8f));
                }
            }
            int row = sides + 1;
            for (int y = 0; y < rings; y++)
                for (int s = 0; s < sides; s++)
                {
                    int a = y * row + s;
                    t.Add(a); t.Add(a + row); t.Add(a + 1); t.Add(a + 1); t.Add(a + row); t.Add(a + row + 1);
                }
            var m = new Mesh { name = "pine trunk" };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateBounds();
            return m;
        }

        /// <summary>Points along a branched model's upper branches, spaced apart: where an oak's leaf clumps go (in its prefab space).</summary>
        static List<Vector3> Anchors(Mesh mesh, Matrix4x4 m, int count, float spacing)
        {
            var pts = new List<Vector3>();
            if (mesh == null || !mesh.isReadable) return pts;
            var verts = mesh.vertices;
            float y0 = float.MaxValue, y1 = float.MinValue;
            var w = new Vector3[verts.Length];
            for (int i = 0; i < verts.Length; i++) { w[i] = m.MultiplyPoint3x4(verts[i]); y0 = Mathf.Min(y0, w[i].y); y1 = Mathf.Max(y1, w[i].y); }
            float h = y1 - y0;
            var cand = new List<Vector3>();
            foreach (var p in w)
                if (p.y > y0 + 0.4f * h && new Vector2(p.x, p.z).magnitude > 0.35f) cand.Add(p);
            // The outermost and highest first: the crown's outline is what reads.
            cand.Sort((a, b) => (b.y + new Vector2(b.x, b.z).magnitude).CompareTo(a.y + new Vector2(a.x, a.z).magnitude));
            foreach (var p in cand)
            {
                bool far = true;
                foreach (var q in pts) if ((q - p).sqrMagnitude < spacing * spacing) { far = false; break; }
                if (far) pts.Add(p);
                if (pts.Count >= count) break;
            }
            // And a few inside the crown, so it is not hollow.
            var r = new System.Random(5);
            for (int i = 0; i < count / 5; i++)
                pts.Add(new Vector3(((float)r.NextDouble() - 0.5f) * 2f, y0 + h * (0.62f + 0.25f * (float)r.NextDouble()), ((float)r.NextDouble() - 0.5f) * 2f));
            return pts;
        }

        void AddClump(GameObject tree, Mesh mesh, Material mat, Matrix4x4 m)
        {
            Vector3 p = m.GetColumn(3);
            var key = (Mathf.FloorToInt(p.x / CellSize), Mathf.FloorToInt(p.z / CellSize), mesh, mat);
            if (!cells.TryGetValue(key, out var cell))
            {
                cell = new Cell { Centre = new Vector3((key.Item1 + 0.5f) * CellSize, 0, (key.Item2 + 0.5f) * CellSize), Mat = mat, Mesh = mesh };
                cells[key] = cell; ordered.Add(cell);
            }
            if (!leavesOf.TryGetValue(tree, out var list)) leavesOf[tree] = list = new List<(Cell, int)>();
            list.Add((cell, cell.Build.Count));
            cell.Build.Add(m);
            Clumps++;
        }

        /// <summary>After all trees are placed: the cells' clumps into batches of at most 1023 (one draw call each).</summary>
        public void Finish()
        {
            foreach (var c in ordered)
            {
                for (int i = 0; i < c.Build.Count; i += 1023)
                    c.Batches.Add(c.Build.GetRange(i, Mathf.Min(1023, c.Build.Count - i)).ToArray());
            }
        }

        /// <summary>An oak: the dead tree's trunk and branches as the frame, leaf clumps along the upper branches.</summary>
        public GameObject Oak(Dressing d, Vector3 pos, float yaw, float scale, int variant, System.Random rng)
        {
            Shared();
            var go = d.PlaceProp("dead_tree", pos - Vector3.up * 0.1f, yaw, scale * 1.35f, oakBark);
            if (go == null) return null;
            go.name = "oak";
            var mat = oakMats[variant % oakMats.Length];
            foreach (var a in oakAnchors)
            {
                Vector3 p = go.transform.TransformPoint(a);
                float s = scale * 1.35f * (1.5f + (float)rng.NextDouble() * 0.9f);
                AddClump(go, oakClump, mat, Matrix4x4.TRS(p, Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0), new Vector3(s, s * 0.85f, s)));
            }
            return go;
        }

        /// <summary>A pine: a tapered trunk of pine bark and tiers of needle sprays, wide low down, narrowing to the tip.</summary>
        public GameObject Pine(Dressing d, Transform parent, Vector3 pos, float yaw, float scale, int variant, System.Random rng)
        {
            Shared();
            float height = 17f * scale;
            var go = new GameObject("pine");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos - Vector3.up * 0.2f, Quaternion.Euler(0, yaw, 0));
            var trunk = new GameObject("trunk");
            trunk.transform.SetParent(go.transform, false);
            trunk.transform.localScale = new Vector3(scale, height, scale);
            trunk.AddComponent<MeshFilter>().sharedMesh = pineTrunk;
            var r = trunk.AddComponent<MeshRenderer>(); r.sharedMaterial = barkMat;
            var col = go.AddComponent<CapsuleCollider>();
            col.height = height; col.radius = 0.3f * scale; col.center = Vector3.up * height * 0.5f;
            go.layer = Layers.Props;
            var mat = pineMats[variant % pineMats.Length];
            const int tiers = 15;
            for (int t = 0; t < tiers; t++)
            {
                float f = t / (tiers - 1f);
                float y = Mathf.Lerp(0.22f, 0.96f, f) * height;
                float ring = Mathf.Lerp(2.9f, 0.35f, f) * scale;
                int n = Mathf.Max(2, Mathf.RoundToInt(ring / scale * 3.2f));
                float spin = (float)rng.NextDouble() * Mathf.PI * 2f;
                for (int k = 0; k < n; k++)
                {
                    float a = spin + k / (float)n * Mathf.PI * 2f;
                    Vector3 p = go.transform.position + new Vector3(Mathf.Cos(a) * ring, y + ((float)rng.NextDouble() - 0.5f) * 0.6f * scale, Mathf.Sin(a) * ring);
                    float s = scale * Mathf.Lerp(1.5f, 0.8f, f) * (0.85f + (float)rng.NextDouble() * 0.3f);
                    AddClump(go, pineClump, mat, Matrix4x4.TRS(p, Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0), new Vector3(s * 1.2f, s * 0.95f, s * 1.2f)));
                    // An inner ring between the outer sprays and the trunk fills the tier in.
                    if (k % 2 == 0)
                    {
                        Vector3 q = go.transform.position + new Vector3(Mathf.Cos(a + 0.4f) * ring * 0.5f, y + 0.3f * scale, Mathf.Sin(a + 0.4f) * ring * 0.5f);
                        AddClump(go, pineClump, mat, Matrix4x4.TRS(q, Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0), new Vector3(s, s * 0.95f, s)));
                    }
                }
                // The middle of each tier, round the trunk.
                if (f < 0.9f)
                    AddClump(go, pineClump, mat, Matrix4x4.TRS(go.transform.position + Vector3.up * y, Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0), new Vector3(1.4f, 0.9f, 1.4f) * scale));
            }
            AddClump(go, pineClump, mat, Matrix4x4.TRS(go.transform.position + Vector3.up * height, Quaternion.identity, new Vector3(0.7f, 1.1f, 0.7f) * scale));
            return go;
        }

        /// <summary>The tree's leaves burnt away: its clumps are hidden; the trunk and branches stand on.</summary>
        public bool Wither(GameObject tree)
        {
            if (!leavesOf.TryGetValue(tree, out var list)) return false;
            foreach (var (cell, index) in list)
            {
                int b = index / 1023, i = index % 1023;
                if (b < cell.Batches.Count) cell.Batches[b][i] = Matrix4x4.zero;
            }
            leavesOf.Remove(tree);
            return true;
        }

        public void Draw(Camera cam)
        {
            if (cam == null) return;
            Vector3 cp = cam.transform.position;
            foreach (var c in ordered)
            {
                float dx = c.Centre.x - cp.x, dz = c.Centre.z - cp.z;
                if (dx * dx + dz * dz > 800f * 800f) continue;
                foreach (var b in c.Batches)
                    Graphics.DrawMeshInstanced(c.Mesh, 0, c.Mat, b, b.Length, null, ShadowCastingMode.On, true);
            }
        }
    }
}
