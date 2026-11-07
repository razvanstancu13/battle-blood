using UnityEngine;
using UnityEngine.Rendering;

namespace BattleBlood
{
    /// <summary>
    /// A ring of mountains far beyond the field, new with every seed: ridged noise for sharp crests, taller in some directions than others,
    /// rising out of the distant hills. Drawn with their own haze (BBMountain), since the battle's fog would hide anything this far away.
    /// </summary>
    public static class Mountains
    {
        const float Inner = 900f, Outer = 2700f;
        const int Around = 320, Out = 44;

        public static GameObject Build(int seed, Mood mood)
        {
            var r = new System.Random(seed ^ 0x6d07);
            float ox = (float)r.NextDouble() * 500f, oz = (float)r.NextDouble() * 500f;
            float tall = 0.75f + (float)r.NextDouble() * 0.5f;          // some battles under high peaks, some under low ranges
            var v = new Vector3[Around * Out]; var c = new Color[Around * Out];
            for (int a = 0; a < Around; a++)
            {
                float ang = a / (float)Around * Mathf.PI * 2f;
                Vector2 dir = new(Mathf.Cos(ang), Mathf.Sin(ang));
                // How big the range is this way round: low gaps and high massifs.
                float range = Mathf.Lerp(0.25f, 1.15f, Mathf.PerlinNoise(ox + Mathf.Cos(ang) * 1.6f + 3f, oz + Mathf.Sin(ang) * 1.6f + 3f));
                for (int k = 0; k < Out; k++)
                {
                    float t = k / (Out - 1f);
                    float rad = Mathf.Lerp(Inner, Outer, Mathf.Pow(t, 1.3f));
                    float x = dir.x * rad, z = dir.y * rad;
                    float rise = Mathx.SmoothStep(Inner, Inner + 650f, rad) * (1f - 0.35f * Mathx.SmoothStep(Outer - 700f, Outer, rad));
                    float ridged = Ridged(ox + x * 0.0016f, oz + z * 0.0016f);
                    float y = 25f + rise * range * tall * (90f + 380f * ridged * ridged);
                    int i = a * Out + k;
                    v[i] = new Vector3(x, y, z);
                    c[i] = new Color(Mathf.PerlinNoise(x * 0.01f, z * 0.01f) - 0.5f, 0, 0, 1);
                }
            }
            var tris = new int[Around * (Out - 1) * 6];
            int n = 0;
            for (int a = 0; a < Around; a++)
            {
                int a2 = (a + 1) % Around;
                for (int k = 0; k < Out - 1; k++)
                {
                    int p = a * Out + k, q = a2 * Out + k;
                    tris[n++] = p; tris[n++] = p + 1; tris[n++] = q;
                    tris[n++] = q; tris[n++] = p + 1; tris[n++] = q + 1;
                }
            }
            var m = new Mesh { name = "mountains", indexFormat = IndexFormat.UInt32 };
            m.vertices = v; m.colors = c; m.triangles = tris;
            m.RecalculateNormals(); m.RecalculateBounds();

            var go = new GameObject("Mountains");
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var mr = go.AddComponent<MeshRenderer>();
            var mat = new Material(Art.Shader("BBMountain")) { name = "m_mountains" };
            mat.SetTexture("_Rock", Art.Tex("rock"));
            mat.SetTexture("_Grass", Art.Tex("grass_dry"));
            mat.SetFloat("_Scale", 60f);
            mat.SetColor("_FogCol", mood.Fog);
            mat.SetFloat("_SnowLine", 260f + (float)r.NextDouble() * 120f);
            mat.SetFloat("_Haze", Weather.Kind switch { WeatherKind.Fog => 1.5f, WeatherKind.Rain => 1.2f, WeatherKind.Storm => 1.5f, _ => 0.55f });
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        /// <summary>Ridged multifractal noise in 0..1: sharp crests where the noise crosses zero.</summary>
        static float Ridged(float x, float z)
        {
            float sum = 0f, amp = 0.55f, w = 1f;
            for (int o = 0; o < 5; o++)
            {
                float s = 1f - Mathf.Abs(Mathf.PerlinNoise(x, z) * 2f - 1f);
                s *= s * w;
                w = Mathf.Clamp01(s * 1.6f);
                sum += s * amp;
                x *= 2.1f; z *= 2.1f; amp *= 0.5f;
            }
            return Mathf.Clamp01(sum);
        }
    }
}
