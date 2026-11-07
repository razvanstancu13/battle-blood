using UnityEngine;
using UnityEngine.Rendering;

namespace BattleBlood
{
    public enum WeatherKind { Clear, Fog, Rain, Storm }

    /// <summary>
    /// The weather of one battle, picked by its seed after the mood: clear, fog, rain or a thunderstorm.
    /// Fog thickens the air; rain darkens the light, falls round the camera as streaks and damps fires; a storm adds lightning and thunder.
    /// </summary>
    public class Weather : MonoBehaviour
    {
        public static WeatherKind Kind { get; private set; } = WeatherKind.Clear;
        public static bool Wet => Kind == WeatherKind.Rain || Kind == WeatherKind.Storm;

        ParticleSystem rain;
        Light sun;
        float sunPower, nextFlash, flashLeft, thunderAt = -1f;
        AudioSource rainLoop;
        Color ambientBase;

        /// <summary>Picks the weather; rain and storms come under a grey sky, so they change the mood to Overcast.</summary>
        public static WeatherKind Pick(int seed, ref Mood mood)
        {
            var r = new System.Random(seed ^ 0x3ea7);
            double v = r.NextDouble();
            Kind = v < 0.4 ? WeatherKind.Clear : v < 0.6 ? WeatherKind.Fog : v < 0.85 ? WeatherKind.Rain : WeatherKind.Storm;
            if (Battle.ForcedWeather >= 0) Kind = (WeatherKind)Battle.ForcedWeather;
            if (Wet) mood = Mood.All[1];
            return Kind;
        }

        /// <summary>Changes the mood's numbers before it is applied.</summary>
        public static void Shape(Mood m)
        {
            switch (Kind)
            {
                case WeatherKind.Fog:
                    m.FogDensity *= 3.2f; m.Haze = Mathf.Min(1f, m.Haze + 0.35f); m.SunPower *= 0.6f;
                    m.Fog = Color.Lerp(m.Fog, new Color(0.62f, 0.64f, 0.66f), 0.6f);
                    m.Saturation -= 10f;
                    break;
                case WeatherKind.Rain:
                case WeatherKind.Storm:
                    bool storm = Kind == WeatherKind.Storm;
                    m.FogDensity *= storm ? 2.4f : 1.8f; m.SunPower *= storm ? 0.35f : 0.5f; m.SkyGain *= storm ? 0.55f : 0.7f;
                    m.AmbientSky *= 0.78f; m.AmbientGround *= 0.78f;
                    m.Fog = Color.Lerp(m.Fog, new Color(0.38f, 0.41f, 0.45f), storm ? 0.7f : 0.5f);
                    m.Saturation -= 12f; m.Contrast += 4f;
                    break;
            }
        }

        public void Build(Mood mood)
        {
            sun = mood.Sun; sunPower = sun != null ? sun.intensity : 1f;
            ambientBase = RenderSettings.ambientLight;
            if (!Wet) return;
            bool storm = Kind == WeatherKind.Storm;
            rain = MakeRain(storm ? 9000 : 5000);
            nextFlash = Time.time + Random.Range(5f, 12f);
            var clip = Art.Clips("rain");
            if (clip.Length > 0)
            {
                rainLoop = gameObject.AddComponent<AudioSource>();
                rainLoop.clip = clip[0]; rainLoop.loop = true; rainLoop.spatialBlend = 0f; rainLoop.volume = storm ? 0.55f : 0.4f;
                rainLoop.Play();
            }
        }

        static ParticleSystem MakeRain(int perSecond)
        {
            var go = new GameObject("rain");
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.playOnAwake = true; main.maxParticles = perSecond * 2;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 1.4f; main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.022f);
            main.gravityModifier = 0f;
            var em = ps.emission; em.rateOverTime = perSecond;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(44f, 2f, 44f);
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-1.8f, -1.2f); vel.y = new ParticleSystem.MinMaxCurve(-22f, -18f); vel.z = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.045f; r.lengthScale = 1f;
            r.shadowCastingMode = ShadowCastingMode.Off;
            var mat = new Material(Art.Shader("BBParticle")) { name = "m_rain" };
            mat.SetTexture("_BaseMap", Streak());
            mat.SetColor("_Color", new Color(0.75f, 0.8f, 0.88f, 0.42f));
            r.sharedMaterial = mat;
            ps.Play();
            return ps;
        }

        static Texture2D Streak()
        {
            const int w = 8, h = 64;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "rain streak", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float across = 1f - Mathf.Abs(x - (w - 1) * 0.5f) / (w * 0.5f);
                    float along = Mathf.Sin(y / (h - 1f) * Mathf.PI);
                    px[x + y * w] = new Color32(255, 255, 255, (byte)(255 * Mathf.Clamp01(across * across * along)));
                }
            t.SetPixels32(px); t.Apply();
            return t;
        }

        void LateUpdate()
        {
            var b = Battle.I;
            if (b == null || !b.Ready) return;
            var cam = Camera.main;
            if (rain != null && cam != null)
            {
                // The rain falls round the camera: a box over it, wider when the view is high.
                float above = cam.transform.position.y - b.Field.Height(cam.transform.position.x, cam.transform.position.z);
                rain.transform.position = cam.transform.position + cam.transform.forward * 8f + Vector3.up * Mathf.Min(14f, 6f + above * 0.3f);
                var shape = rain.shape; float s = Mathf.Lerp(44f, 90f, Mathf.InverseLerp(10f, 120f, above)); shape.scale = new Vector3(s, 2f, s);
            }
            if (Kind != WeatherKind.Storm) return;
            // Lightning: the sky goes white for a moment, twice; the thunder comes after, later the further it struck.
            if (Time.time >= nextFlash)
            {
                nextFlash = Time.time + Random.Range(7f, 18f);
                flashLeft = 0.32f;
                thunderAt = Time.time + Random.Range(0.6f, 3f);
                Flashes++;
            }
            if (flashLeft > 0f)
            {
                flashLeft -= Time.deltaTime;
                float k = flashLeft > 0.2f ? 1f : flashLeft > 0.12f ? 0.2f : flashLeft > 0.06f ? 0.8f : 0f;
                if (sun != null) sun.intensity = sunPower + k * 3.5f;
                RenderSettings.ambientIntensity = 1f + k * 1.6f;
            }
            else { if (sun != null) sun.intensity = sunPower; RenderSettings.ambientIntensity = 1f; }
            if (thunderAt > 0f && Time.time >= thunderAt) { thunderAt = -1f; GameAudio.Play2D("thunder", Random.Range(0.6f, 1f)); }
        }
        public static int Flashes;

        void OnDestroy() { RenderSettings.ambientIntensity = 1f; }
    }
}
