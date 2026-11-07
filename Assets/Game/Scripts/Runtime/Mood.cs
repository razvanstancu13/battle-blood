using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BattleBlood
{
    /// <summary>The look of one battle, picked by its seed: which generated sky, how the sun stands, how thick the air is, how the picture is graded.</summary>
    public class Mood
    {
        public string Name, Sky;
        public Vector2 SunAngles;      // elevation, azimuth
        public Color SunColor, AmbientSky, AmbientGround, Fog;
        public float SunPower, FogDensity, SkyGain, Haze, Contrast, Saturation, Exposure;
        public Color SplitShadow, SplitHigh;

        public static readonly Mood[] All =
        {
            new Mood { Name = "Dawn", Sky = "sky_dawn", SunAngles = new(20f, 62f), SunColor = new(1f, 0.72f, 0.48f), SunPower = 2.2f, AmbientSky = new(0.72f, 0.72f, 0.84f), AmbientGround = new(0.44f, 0.38f, 0.3f),
                Fog = new(0.78f, 0.62f, 0.52f), FogDensity = 0.0052f, SkyGain = 1f, Haze = 0.42f, Contrast = 14f, Saturation = -4f, Exposure = 0.05f, SplitShadow = new(0.44f, 0.5f, 0.6f), SplitHigh = new(0.66f, 0.54f, 0.44f) },
            new Mood { Name = "Overcast", Sky = "sky_overcast", SunAngles = new(48f, 140f), SunColor = new(0.78f, 0.82f, 0.9f), SunPower = 0.95f, AmbientSky = new(0.66f, 0.7f, 0.76f), AmbientGround = new(0.34f, 0.34f, 0.32f),
                Fog = new(0.58f, 0.61f, 0.64f), FogDensity = 0.0072f, SkyGain = 0.9f, Haze = 0.6f, Contrast = 18f, Saturation = -20f, Exposure = 0.1f, SplitShadow = new(0.46f, 0.52f, 0.56f), SplitHigh = new(0.54f, 0.54f, 0.52f) },
            new Mood { Name = "Blood Sunset", Sky = "sky_dusk", SunAngles = new(19f, 250f), SunColor = new(1f, 0.62f, 0.44f), SunPower = 2.2f, AmbientSky = new(0.78f, 0.7f, 0.74f), AmbientGround = new(0.48f, 0.38f, 0.32f),
                Fog = new(0.5f, 0.38f, 0.34f), FogDensity = 0.0058f, SkyGain = 0.62f, Haze = 0.5f, Contrast = 16f, Saturation = -18f, Exposure = 0.05f, SplitShadow = new(0.45f, 0.46f, 0.54f), SplitHigh = new(0.62f, 0.52f, 0.44f) },
            new Mood { Name = "Clear Noon", Sky = "sky_fair", SunAngles = new(52f, 30f), SunColor = new(1f, 0.95f, 0.86f), SunPower = 2.4f, AmbientSky = new(0.58f, 0.66f, 0.78f), AmbientGround = new(0.34f, 0.32f, 0.26f),
                Fog = new(0.74f, 0.8f, 0.86f), FogDensity = 0.0030f, SkyGain = 0.95f, Haze = 0.25f, Contrast = 12f, Saturation = -6f, Exposure = 0f, SplitShadow = new(0.48f, 0.5f, 0.54f), SplitHigh = new(0.58f, 0.55f, 0.5f) },
        };

        public static Mood Pick(int seed) => All[Mathf.Abs(seed * 31 + 7) % All.Length];
        /// <summary>A copy to change for one battle (the weather does), leaving the presets alone.</summary>
        public Mood Copy() => (Mood)MemberwiseClone();
        public Light Sun { get; private set; }

        public void Apply(Field field)
        {
            var sunGo = new GameObject("Sun");
            Sun = sunGo.AddComponent<Light>();
            Sun.type = LightType.Directional;
            Sun.color = SunColor; Sun.intensity = SunPower;
            Sun.shadows = LightShadows.Soft; Sun.shadowStrength = 0.9f;
            sunGo.transform.rotation = Quaternion.Euler(SunAngles.x, SunAngles.y, 0);
            RenderSettings.sun = Sun;

            var skyTex = Art.Tex(Sky);
            var sky = new Material(Art.Shader("BBSky")) { name = "m_sky" };
            sky.SetTexture("_MainTex", skyTex);
            sky.SetVector("_SunDir", -sunGo.transform.forward);
            sky.SetColor("_SunCol", SunColor * 1.4f);
            sky.SetColor("_FogCol", Fog);
            sky.SetFloat("_Haze", Haze);
            sky.SetFloat("_Gain", SkyGain);
            RenderSettings.skybox = sky;

            RenderSettings.ambientMode = AmbientMode.Custom;
            var sh = new UnityEngine.Rendering.SphericalHarmonicsL2();
            sh.AddAmbientLight((AmbientSky + AmbientGround) * 0.5f);
            sh.AddDirectionalLight(Vector3.up, (AmbientSky - AmbientGround) * 0.9f, 1f);
            RenderSettings.ambientProbe = sh;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = Fog;
            RenderSettings.fogDensity = FogDensity;

            // Metal needs something to reflect, and a runtime sky has no baked probe: a small cube made from the same panorama.
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = MakeCube(skyTex);
            RenderSettings.reflectionIntensity = 0.9f;

            var post = new GameObject("Post Processing").AddComponent<PostFx>();
            post.Build(this);
        }

        Cubemap MakeCube(Texture2D pano)
        {
            const int size = 64;
            var cube = new Cubemap(size, TextureFormat.RGBA32, true) { name = "sky cube" };
            var px = new Color[size * size];
            for (int f = 0; f < 6; f++)
            {
                var face = (CubemapFace)f;
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                        Vector3 d = face switch
                        {
                            CubemapFace.PositiveX => new Vector3(1, -v, -u),
                            CubemapFace.NegativeX => new Vector3(-1, -v, u),
                            CubemapFace.PositiveY => new Vector3(u, 1, v),
                            CubemapFace.NegativeY => new Vector3(u, -1, -v),
                            CubemapFace.PositiveZ => new Vector3(u, -v, 1),
                            _ => new Vector3(-u, -v, -1),
                        };
                        d.Normalize();
                        Color c;
                        if (d.y < 0f) c = Color.Lerp(Fog, AmbientGround * 0.7f, Mathf.Clamp01(-d.y * 3f));
                        else
                        {
                            float az = Mathf.Atan2(d.x, d.z) / (2f * Mathf.PI) + 0.5f;
                            float el = Mathf.Asin(d.y) / (Mathf.PI * 0.5f);
                            c = pano.isReadable ? pano.GetPixelBilinear(az, 0.2f + 0.8f * el) : Color.Lerp(Fog, AmbientSky, el);
                        }
                        c.a = 1f;
                        px[x + y * size] = c;
                    }
                cube.SetPixels(px, face);
            }
            cube.Apply(true, false);
            return cube;
        }
    }

    /// <summary>The film look, and what the battle does to it: it greys and reddens as the player's health goes, and softens behind an archer's target.</summary>
    public class PostFx : MonoBehaviour
    {
        public static PostFx Instance { get; private set; }
        Volume volume;
        Vignette vignette;
        ColorAdjustments grade;
        ChromaticAberration chroma;
        DepthOfField dof;
        MotionBlur blur;
        LiftGammaGain lift;
        float baseSaturation, pain, killFlash;

        public void Build(Mood m)
        {
            Instance = this;
            volume = gameObject.AddComponent<Volume>();
            volume.isGlobal = true;
            // A profile asset in Resources: that is what keeps the effects' shader variants in the build.
            var source = Resources.Load<VolumeProfile>("post");
            var p = Instantiate(source);
            volume.profile = p;
            p.TryGet(out vignette); p.TryGet(out grade); p.TryGet(out chroma); p.TryGet(out dof); p.TryGet(out blur);
            if (p.TryGet(out SplitToning split))
            {
                split.shadows.Override(m.SplitShadow);
                split.highlights.Override(m.SplitHigh);
            }
            if (grade != null)
            {
                grade.contrast.Override(m.Contrast);
                grade.saturation.Override(m.Saturation);
                grade.postExposure.Override(m.Exposure);
                baseSaturation = m.Saturation;
            }
        }

        public void Pain(float amount) { pain = Mathf.Clamp01(pain + amount * 2.2f + 0.2f); }

        public void Follow(Unit player, float zoom, float focusDist)
        {
            float dt = Time.unscaledDeltaTime;
            pain = Mathf.MoveTowards(pain, 0f, dt * 1.1f);
            float hp = player != null ? Mathf.Clamp01(player.Hp / player.S.MaxHp) : 1f;
            float low = 1f - Mathf.Clamp01(hp / 0.55f);
            if (vignette != null)
            {
                vignette.intensity.value = 0.28f + low * 0.22f + pain * 0.22f;
                vignette.color.value = Color.Lerp(Color.black, new Color(0.42f, 0f, 0f), Mathf.Clamp01(low + pain));
            }
            if (grade != null) grade.saturation.value = baseSaturation - low * 40f;
            if (chroma != null) chroma.intensity.value = 0.07f + pain * 0.55f;
            if (blur != null)
            {
                float speed = player != null ? player.Speed : 0f;
                blur.intensity.value = Mathf.Lerp(blur.intensity.value, Mathf.InverseLerp(5f, 15f, speed) * 0.42f, dt * 5f);
                blur.active = blur.intensity.value > 0.01f;
            }
            if (dof != null)
            {
                dof.active = zoom > 0.02f;
                dof.gaussianStart.value = Mathf.Max(8f, focusDist * 1.2f);
                dof.gaussianEnd.value = dof.gaussianStart.value * 2.6f;
                dof.gaussianMaxRadius.value = 1.1f * zoom;
            }
        }
    }
}
