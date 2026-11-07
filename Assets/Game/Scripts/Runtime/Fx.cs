using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BattleBlood
{
    /// <summary>All non-blood particles in a handful of world-space systems, fed by Emit: a burning man costs a few particles, not a system of his own.</summary>
    public static class Fx
    {
        static ParticleSystem dust, smoke, flame, spark, splash, ambient;
        public static Mesh ArrowMesh, FletchMesh, BowMesh;
        public static Material ArrowMaterial, FletchMaterial, StringMaterial, TrailMaterial;
        static Light fireLight;
        static float lightHold;

        /// <summary>The materials and meshes units are built with: made once, shared by all.</summary>
        public static void CreateShared()
        {
            ArrowMesh = ProcMesh.Arrow(out FletchMesh);
            BowMesh = ProcMesh.Bow(1.55f);
            ArrowMaterial = new Material(Art.TexMat("wood", 0.25f, 0.6f)) { name = "m_arrow", enableInstancing = true };
            FletchMaterial = new Material(Art.Shader("BBLit")) { name = "m_fletch", enableInstancing = true };
            FletchMaterial.SetTexture("_BaseMap", Art.Tex("feather"));
            FletchMaterial.SetFloat("_Cutoff", 0.35f);
            FletchMaterial.SetFloat("_Cull", 0f);
            StringMaterial = new Material(Art.Shader("BBParticle")) { name = "m_string" };
            StringMaterial.SetColor("_Color", new Color(0.72f, 0.68f, 0.58f, 1f));
            TrailMaterial = new Material(Art.Shader("BBParticle")) { name = "m_trail" };
            TrailMaterial.SetColor("_Color", new Color(0.9f, 0.92f, 1f, 0.07f));
        }

        public static void CreateSystems()
        {
            dust = Make("dust", "dust", 500, false, 0.02f, new Color(0.62f, 0.52f, 0.38f, 0.5f), 1f);
            smoke = Make("smoke", "smoke", 800, false, -0.03f, new Color(0.2f, 0.19f, 0.18f, 0.55f), 1f);
            flame = Make("flame", "fire", 1500, true, -0.12f, new Color(2.6f, 1.25f, 0.42f, 1f), 0f);
            spark = Make("spark", "spark", 500, true, 0.7f, new Color(3.5f, 1.9f, 0.6f, 1f), 0f);
            splash = Make("splash", "smoke", 300, false, 1f, new Color(0.5f, 0.55f, 0.55f, 0.2f), 1f);
            var lg = new GameObject("fire light");
            fireLight = lg.AddComponent<Light>();
            fireLight.type = LightType.Point; fireLight.color = new Color(1f, 0.55f, 0.22f); fireLight.range = 11f; fireLight.intensity = 0f;
            fireLight.shadows = LightShadows.None;
        }

        static ParticleSystem Make(string name, string tex, int max, bool add, float gravity, Color color, float lit)
        {
            var go = new GameObject("fx " + name);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false; main.playOnAwake = false; main.maxParticles = max;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = gravity;
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = false;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var size = ps.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, add ? AnimationCurve.Linear(0f, 1f, 1f, 0.3f) : AnimationCurve.EaseInOut(0f, 0.5f, 1f, 1.5f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            var mat = new Material(Art.Shader("BBParticle")) { name = "m_fx_" + name };
            mat.SetTexture("_BaseMap", Art.Tex(tex));
            mat.SetColor("_Color", color);
            mat.SetFloat("_Lit", lit);
            if (add) { mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); mat.SetFloat("_DstBlend", (float)BlendMode.One); }
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            ps.Play();
            return ps;
        }

        public static void Dust(Vector3 pos, int n)
        {
            if (dust == null) return;
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < n; i++)
            {
                ep.position = pos + Random.insideUnitSphere * 0.4f + Vector3.up * 0.1f;
                ep.velocity = Random.insideUnitSphere * 0.6f + Vector3.up * 0.3f;
                ep.startSize = Random.Range(0.9f, 1.9f);
                ep.startLifetime = Random.Range(0.9f, 2f);
                ep.rotation = Random.Range(0f, 360f);
                dust.Emit(ep, 1);
            }
        }

        public static void Puff(Vector3 pos, int n)
        {
            if (smoke == null) return;
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < n; i++)
            {
                ep.position = pos + Random.insideUnitSphere * 0.25f;
                ep.velocity = Random.insideUnitSphere * 0.4f + Vector3.up * 0.8f;
                ep.startSize = Random.Range(0.5f, 1.2f);
                ep.startLifetime = Random.Range(1.2f, 2.4f);
                ep.rotation = Random.Range(0f, 360f);
                smoke.Emit(ep, 1);
            }
        }

        public static void Splash(Vector3 pos, int n)
        {
            if (splash == null) return;
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < n * 3; i++)
            {
                ep.position = new Vector3(pos.x, Field.WaterLevel + 0.05f, pos.z) + Random.insideUnitSphere * 0.15f;
                ep.velocity = Random.insideUnitSphere * 1.3f + Vector3.up * Random.Range(1f, 2.4f);
                ep.startSize = Random.Range(0.1f, 0.3f);
                ep.startLifetime = Random.Range(0.3f, 0.6f);
                ep.rotation = Random.Range(0f, 360f);
                splash.Emit(ep, 1);
            }
        }

        public static void Sparks(Vector3 pos, Vector3 dir, int n)
        {
            if (spark == null) return;
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < n; i++)
            {
                ep.position = pos;
                ep.velocity = dir * Random.Range(1f, 4f) + Random.insideUnitSphere * 3.2f + Vector3.up * 0.8f;
                ep.startSize = Random.Range(0.03f, 0.16f);
                ep.startLifetime = Random.Range(0.12f, 0.4f);
                ep.rotation = Random.Range(0f, 360f);
                spark.Emit(ep, 1);
            }
        }

        public static void ArrowFlame(Vector3 pos, int n)
        {
            if (flame == null) return;
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < n; i++)
            {
                ep.position = pos + Random.insideUnitSphere * 0.03f;
                ep.velocity = Random.insideUnitSphere * 0.15f + Vector3.up * 0.3f;
                ep.startSize = Random.Range(0.16f, 0.34f);
                ep.startLifetime = Random.Range(0.1f, 0.26f);
                ep.rotation = Random.Range(0f, 360f);
                flame.Emit(ep, 1);
            }
        }

        public static void GroundFlame(Vector3 pos, float size)
        {
            if (flame == null || size <= 0.02f) return;
            var ep = new ParticleSystem.EmitParams();
            ep.position = pos;
            ep.velocity = Vector3.up * Random.Range(0.8f, 1.9f) + Random.insideUnitSphere * 0.3f;
            ep.startSize = Random.Range(0.8f, 1.5f) * size;
            ep.startLifetime = Random.Range(0.4f, 0.85f);
            ep.rotation = Random.Range(0f, 360f);
            flame.Emit(ep, 1);
        }

        /// <summary>Flames over a burning body, placed on its bones, so they follow it as it runs and falls.</summary>
        public static void BodyFlames(Unit u, List<Transform> bones, float dt, float strength)
        {
            if (flame == null || bones.Count == 0) return;
            float rate = u.CamDist < 50f ? 60f : 20f;
            int n = Mathf.Max(1, Mathf.RoundToInt(rate * dt * strength));
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < n; i++)
            {
                var b = bones[Random.Range(0, bones.Count)];
                ep.position = b.position + Random.insideUnitSphere * (u.Type == UnitType.Cavalry ? 0.22f : 0.16f);
                ep.velocity = u.Vel * 0.75f + Vector3.up * Random.Range(0.6f, 1.6f) + Random.insideUnitSphere * 0.3f;
                ep.startSize = Random.Range(0.35f, 0.8f);
                ep.startLifetime = Random.Range(0.25f, 0.6f);
                ep.rotation = Random.Range(0f, 360f);
                flame.Emit(ep, 1);
            }
            if (Random.value < dt * 9f) Puff(u.ChestPos + Vector3.up * 0.5f, 1);
            if (u.CamDist < 38f)
            {
                fireLight.transform.position = u.ChestPos;
                fireLight.intensity = Mathf.Lerp(fireLight.intensity, 3.2f + Random.Range(-0.8f, 0.8f), 0.3f);
                lightHold = 0.2f;
            }
        }

        public static void Tick(float dt)
        {
            if (fireLight == null) return;
            lightHold -= dt;
            if (lightHold <= 0f) fireLight.intensity = Mathf.MoveTowards(fireLight.intensity, 0f, dt * 8f);
        }
    }

    /// <summary>A prop that can catch fire: found through the obstacle it stands on.</summary>
    /// <summary>On a flammable prop's object: an arrow that hits its collider finds what can burn.</summary>
    public class PropRef : MonoBehaviour { public BurningProp Prop; }

    public class BurningProp
    {
        public GameObject Go; public Vector3 Pos; public float Radius, Height, Left, Burn; public Unit By;
        public bool Burning, Spent, Withered;
        public Renderer[] Renderers;
        public bool IsTree => Go != null && (Go.name == "oak" || Go.name == "pine");
    }

    /// <summary>
    /// Fires started by lit arrows. Grass burns and creeps outwards for a few generations, then dies out, leaving black ground; a prop burns longer and chars.
    /// Anyone standing in the flames catches fire. All of it goes through the shared flame and smoke particles.
    /// </summary>
    public class Fires
    {
        class Patch { public Vector3 Pos; public float Left, Spread, Burn; public int Gen; public Unit By; }
        const int MaxPatches = 30, MaxGen = 2;
        readonly List<Patch> patches = new();
        readonly List<BurningProp> props = new();
        readonly List<Unit> near = new(16);
        float emitTimer, burnTimer;
        public int Started { get; private set; }
        public int PropsLit { get; private set; }

        /// <param name="by">Whose arrow lit it: his own army is never set alight by it.</param>
        public void LightGround(Vector3 p, Unit by, int gen = 0)
        {
            var f = Battle.I.Field;
            if (patches.Count >= MaxPatches || !f.GrassAt(p) || f.Burnt(p)) return;
            if (Weather.Wet && Random.value < 0.6f) return;           // wet grass mostly will not take
            p.y = f.Height(p.x, p.z);
            float wet = Weather.Wet ? 0.5f : 1f;
            patches.Add(new Patch { Pos = p, Left = Random.Range(6f, 11f) * wet, Spread = Random.Range(0.8f, 1.8f) / wet, Gen = gen, By = by });
            f.PaintBurn(p, 1.6f);
            Gore.Scorch(p);
            Started++;
            if (gen == 0) GameAudio.Play("ignite", p, 0.6f);
        }

        public void LightProp(BurningProp b, Unit by)
        {
            if (b == null || b.Burning || b.Spent || b.Go == null) return;
            b.Burning = true;
            b.By = by;
            b.Left = Random.Range(16f, 26f);
            b.Renderers = b.Go.GetComponentsInChildren<Renderer>();
            foreach (var r in b.Renderers) r.material = new Material(r.sharedMaterial);    // its own copy, so only this one chars
            props.Add(b);
            PropsLit++;
            GameAudio.Play("ignite", b.Pos + Vector3.up, 0.9f);
            LightGround(b.Pos + new Vector3(Random.Range(-1f, 1f), 0, Random.Range(-1f, 1f)) * (b.Radius + 0.5f), by);
        }

        public void Tick(float dt)
        {
            var field = Battle.I.Field;
            var cam = Battle.I.PlayerCam.transform.position;
            emitTimer -= dt; burnTimer -= dt;
            bool emit = emitTimer <= 0f, burnNow = burnTimer <= 0f;
            if (emit) emitTimer = 0.07f;
            if (burnNow) burnTimer = 0.5f;

            for (int i = patches.Count - 1; i >= 0; i--)
            {
                var p = patches[i];
                p.Left -= dt;
                if (p.Left <= 0f) { patches.RemoveAt(i); continue; }
                float dist = (p.Pos - cam).sqrMagnitude;
                if (emit && dist < 150f * 150f)
                {
                    float strength = Mathf.Clamp01(p.Left / 3f);
                    Fx.GroundFlame(p.Pos + new Vector3(Random.Range(-1.1f, 1.1f), 0.2f, Random.Range(-1.1f, 1.1f)), 1.1f * strength);
                    if (Random.value < 0.3f) Fx.Puff(p.Pos + Vector3.up * 1.2f, 1);
                }
                p.Spread -= dt;
                if (p.Spread <= 0f)
                {
                    p.Spread = Random.Range(1.2f, 2.6f);
                    if (p.Gen < MaxGen && Random.value < 0.4f)
                    {
                        float a = Random.Range(0f, Mathf.PI * 2f);
                        LightGround(p.Pos + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * Random.Range(2f, 3.4f), p.By, p.Gen + 1);
                    }
                }
                if (burnNow) Scald(p.Pos, 1.5f, p.By);
            }

            for (int i = props.Count - 1; i >= 0; i--)
            {
                var b = props[i];
                b.Left -= dt;
                b.Burn = Mathf.Min(1f, b.Burn + dt * 0.09f);
                float ember = Mathf.Clamp01(b.Left * 0.25f);
                if (b.IsTree)
                {
                    // Leaves and all go in the flames; what stands after is a dead tree, its own colour, not a black one.
                    if (!b.Withered && b.Burn > 0.4f)
                    {
                        b.Withered = true;
                        // A card tree loses its leaves and stands bare; an old modelled one is swapped for the dead tree.
                        if (!Battle.I.Dressing.Trees.Wither(b.Go)) Dressing.ToDeadTree(b.Go);
                        b.Renderers = b.Go.GetComponentsInChildren<Renderer>();
                        Fx.Puff(b.Pos + Vector3.up * b.Height * 0.6f, 10);
                    }
                    foreach (var r in b.Renderers) if (r != null) r.material.SetFloat("_Ember", ember * 0.6f);
                }
                else
                    foreach (var r in b.Renderers)
                        if (r != null) { r.material.SetFloat("_Burn", b.Burn); r.material.SetFloat("_Ember", ember); }
                if (b.Left <= 0f) { b.Burning = false; b.Spent = true; props.RemoveAt(i); continue; }
                float dist = (b.Pos - cam).sqrMagnitude;
                if (emit && dist < 220f * 220f)
                {
                    int n = dist < 70f * 70f ? 3 : 1;
                    for (int k = 0; k < n; k++)
                    {
                        float a = Random.Range(0f, Mathf.PI * 2f), r = Random.Range(0f, b.Radius * 1.6f);
                        Fx.GroundFlame(b.Pos + new Vector3(Mathf.Cos(a) * r, Random.Range(0.2f, b.Height), Mathf.Sin(a) * r), Mathf.Lerp(1f, 2.1f, b.Height / 8f));
                    }
                    if (Random.value < 0.55f) Fx.Puff(b.Pos + Vector3.up * b.Height * Random.Range(0.6f, 1.1f), 1);
                }
                if (burnNow) Scald(b.Pos, b.Radius + 1f, b.By);
                // A burning prop sets the grass round it alight.
                if (burnNow && Random.value < 0.12f)
                {
                    float a = Random.Range(0f, Mathf.PI * 2f);
                    LightGround(b.Pos + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (b.Radius + 1.5f), b.By, 2);
                }
            }
            GameAudio.Instance?.FireLoopAt(NearestFire(cam));
        }

        void Scald(Vector3 p, float radius, Unit by)
        {
            Battle.I.Query(p, radius + 1f, near);
            foreach (var u in near)
                if (u.Alive && !u.Burning && (by == null || u.Team != by.Team) && Random.value < 0.05f && Mathx.Flat(u.Pos - p).sqrMagnitude < radius * radius && Battle.I.Field.WaterDepth(u.Pos.x, u.Pos.z) < 0.3f) u.Ignite(by);
        }

        Vector3? NearestFire(Vector3 cam)
        {
            Vector3? best = null; float d = 45f * 45f;
            foreach (var p in patches) { float x = (p.Pos - cam).sqrMagnitude; if (x < d) { d = x; best = p.Pos; } }
            foreach (var b in props) { float x = (b.Pos - cam).sqrMagnitude; if (x < d) { d = x; best = b.Pos; } }
            return best;
        }
    }

    /// <summary>Fires burning in the field for tone: a column of smoke seen from far off, and flames at its foot.</summary>
    public class FieldFire : MonoBehaviour
    {
        ParticleSystem s;
        float t;
        void Update()
        {
            t -= Time.deltaTime;
            if (t > 0f) return;
            t = 0.12f;
            Fx.ArrowFlame(transform.position + Random.insideUnitSphere * 0.8f, 2);
            Fx.Puff(transform.position + Vector3.up * 1.4f + Random.insideUnitSphere * 0.6f, 1);
        }
    }
}
