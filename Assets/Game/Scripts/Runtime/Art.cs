using System.Collections.Generic;
using UnityEngine;

namespace BattleBlood
{
    /// <summary>Everything LAGP made is under Resources/Art, so the whole game can be put together at runtime from one seed.</summary>
    public static class Art
    {
        static readonly Dictionary<string, AudioClip[]> ClipCache = new();
        static readonly Dictionary<string, Material> Mats = new();

        public static Texture2D Tex(string n)
        {
            var t = Resources.Load<Texture2D>("Art/Textures/" + n);
            if (t == null) Debug.LogError("[ART] missing texture " + n);
            return t;
        }
        /// <summary>
        /// A tile and its variants (name, name_v1, name_v2, ...; or name_n, name_v1_n, ... for normal maps) as one texture array:
        /// the terrain blends between the slices. Copied on the GPU, so the tiles need not be readable.
        /// </summary>
        public static bool OneTileOnly;      // -bbonetile: the ground's first tile alone, to compare with the variants

        public static Texture2DArray TexArray(string name, bool normal)
        {
            var list = new List<Texture2D>();
            var first = Resources.Load<Texture2D>("Art/Textures/" + name + (normal ? "_n" : ""));
            if (first == null) { Debug.LogError("[ART] missing texture " + name); return null; }
            list.Add(first);
            for (int k = 1; k < 32 && !OneTileOnly; k++)
            {
                var t = Resources.Load<Texture2D>($"Art/Textures/{name}_v{k}{(normal ? "_n" : "")}");
                if (t == null) break;
                if (t.width != first.width || t.height != first.height || t.format != first.format || t.mipmapCount != first.mipmapCount)
                { Debug.LogWarning($"[ART] {t.name} differs from {first.name} ({t.format} {t.width}, {first.format} {first.width}): left out"); continue; }
                list.Add(t);
            }
            var arr = new Texture2DArray(first.width, first.height, list.Count, first.format, first.mipmapCount > 1, normal)      // last argument: linear. Colour tiles are sRGB, normal maps linear
            { name = name + (normal ? " normals" : "") + " variants", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = normal ? 4 : 8 };
            for (int i = 0; i < list.Count; i++) Graphics.CopyTexture(list[i], 0, arr, i);
            Debug.Log($"[ART] {arr.name}: {list.Count} tiles, {first.graphicsFormat} {first.width} mips {first.mipmapCount} -> {arr.graphicsFormat} mips {arr.mipmapCount}, copy support {SystemInfo.copyTextureSupport}");
            return arr;
        }

        public static Texture2D Ui(string n) => Resources.Load<Texture2D>("Art/UI/" + n);
        public static GameObject Model(string n)
        {
            var m = Resources.Load<GameObject>("Art/Models/" + n);
            if (m == null) Debug.LogError("[ART] missing model " + n);
            return m;
        }
        public static Texture2D ModelTex(string n) => Resources.Load<Texture2D>("Art/Models/" + n);
        public static Shader Shader(string n)
        {
            var s = Resources.Load<Shader>("Shaders/" + n);
            if (s == null) Debug.LogError("[ART] missing shader " + n);
            return s;
        }
        public static AudioClip[] Clips(string group)
        {
            if (ClipCache.TryGetValue(group, out var c)) return c;
            var list = new List<AudioClip>();
            for (int i = 0; i < 8; i++)
            {
                var a = Resources.Load<AudioClip>($"Art/Audio/{group}_{i}");
                if (a == null) break;
                list.Add(a);
            }
            if (list.Count == 0) { var one = Resources.Load<AudioClip>("Art/Audio/" + group); if (one != null) list.Add(one); }
            return ClipCache[group] = list.ToArray();
        }

        /// <summary>The shared material of a model. Units copy it so each one can carry its own blood and burn.</summary>
        public static Material ModelMat(string model, float tint = 0f, float metal = 0f, float smooth = 0.25f)
        {
            string key = $"{model}:{tint}:{metal}:{smooth}";
            if (Mats.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(Shader("BBLit")) { name = "m_" + model, enableInstancing = true };
            m.SetTexture("_BaseMap", ModelTex(model));
            m.SetFloat("_TintAmount", tint);
            m.SetFloat("_MetalAmount", metal);
            m.SetFloat("_Smooth", smooth);
            return Mats[key] = m;
        }

        public static Material TexMat(string texture, float smooth = 0.2f, float tile = 1f)
        {
            string key = $"tex:{texture}:{smooth}:{tile}";
            if (Mats.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(Shader("BBLit")) { name = "m_" + texture, enableInstancing = true };
            m.SetTexture("_BaseMap", Tex(texture));
            m.SetTextureScale("_BaseMap", Vector2.one * tile);
            m.SetFloat("_Smooth", smooth);
            return Mats[key] = m;
        }

        /// <summary>The mesh of a prop's FBX: the props have one mesh each.</summary>
        public static Mesh PropMesh(string n)
        {
            var go = Model(n);
            if (go == null) return null;
            var f = go.GetComponentInChildren<MeshFilter>();
            return f != null ? f.sharedMesh : null;
        }

        /// <summary>World rotation and scale baked into a prop's FBX hierarchy, so instances can be placed from the mesh alone.</summary>
        public static Matrix4x4 PropBase(string n)
        {
            if (BaseCache.TryGetValue(n, out var cached)) return cached;
            var go = Model(n);
            var f = go.GetComponentInChildren<MeshFilter>();
            // Not relative to the root: a single-object FBX keeps the object's rotation and scale on the root itself.
            var m = f.transform.localToWorldMatrix;
            return BaseCache[n] = m;
        }
        static readonly Dictionary<string, Matrix4x4> BaseCache = new();

        /// <summary>A prop's box in its prefab's own space, after the rotation and scale in its FBX: the stored mesh often has its axes swapped.</summary>
        public static Bounds PropBounds(string n)
        {
            var b = PropMesh(n).bounds;
            var m = PropBase(n);
            var result = new Bounds(m.MultiplyPoint3x4(b.center), Vector3.zero);
            for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                    for (int z = -1; z <= 1; z += 2)
                        result.Encapsulate(m.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3(x, y, z))));
            return result;
        }

        public static void ClearCache() { Mats.Clear(); BaseCache.Clear(); }
    }

    public static class Layers
    {
        public const int Default = 0;
        public const int Props = 8;          // prop colliders: what arrows stick in
        public const int PropMask = 1 << Props;
    }

    /// <summary>Deterministic small helpers.</summary>
    public static class Mathx
    {
        public static float Yaw(Vector3 dir) => Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        public static Vector3 FromYaw(float yaw) { float r = yaw * Mathf.Deg2Rad; return new Vector3(Mathf.Sin(r), 0, Mathf.Cos(r)); }
        public static Vector3 Flat(Vector3 v) { v.y = 0; return v; }
        public static float SmoothStep(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3 - 2 * t); }
        public static float Fbm(float x, float z, int oct, float lac = 2.03f, float gain = 0.5f)
        {
            float a = 1, s = 0, n = 0;
            for (int i = 0; i < oct; i++)
            {
                s += a * (Mathf.PerlinNoise(x + 37.1f * i, z - 19.7f * i) * 2f - 1f);
                n += a; a *= gain; x *= lac; z *= lac;
            }
            return s / n;
        }
        public static Vector3 Catmull(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3);
        }
    }
}
