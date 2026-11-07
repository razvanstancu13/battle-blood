// The battlefield: six kinds of ground blended by vertex weights. Each kind is a stack of generated tiles (a texture array); a slow noise
// across the land picks two of them and blends, so no one tile repeats over the field. Within a tile, a second, larger sample hides the repeat.
// Vertex colour: R dry grass, G dirt, B mud, A rock; the remainder is grass. Blood from the battle's blood map soaks into the ground.
Shader "BattleBlood/Terrain"
{
    Properties
    {
        _Grass ("Grass", 2DArray) = "" {} _GrassN ("Grass n", 2DArray) = "" {} _GrassCount ("Grass tiles", Float) = 1
        _Dry ("Dry", 2DArray) = "" {} _DryN ("Dry n", 2DArray) = "" {} _DryCount ("Dry tiles", Float) = 1
        _Dirt ("Dirt", 2DArray) = "" {} _DirtN ("Dirt n", 2DArray) = "" {} _DirtCount ("Dirt tiles", Float) = 1
        _Mud ("Mud", 2DArray) = "" {} _MudN ("Mud n", 2DArray) = "" {} _MudCount ("Mud tiles", Float) = 1
        _Rock ("Rock", 2DArray) = "" {} _RockN ("Rock n", 2DArray) = "" {} _RockCount ("Rock tiles", Float) = 1
        _Moss ("Moss", 2DArray) = "" {} _MossCount ("Moss tiles", Float) = 1
        _Scale ("Metres per tile", Float) = 5
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float _Scale, _GrassCount, _DryCount, _DirtCount, _MudCount, _RockCount, _MossCount;
        CBUFFER_END
        ENDHLSL
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            Texture2DArray _Grass, _GrassN, _Dry, _DryN, _Dirt, _DirtN, _Mud, _MudN, _Rock, _RockN, _Moss;
            SAMPLER(sampler_Grass);
            TEXTURE2D(_BB_BloodMap); SAMPLER(sampler_BB_BloodMap);
            TEXTURE2D(_BB_BloodTile); SAMPLER(sampler_BB_BloodTile);
            float4 _BB_FieldToUv;   // xy scale, zw offset: world XZ to blood map UV

            struct A { float4 pos:POSITION; float3 n:NORMAL; float4 c:COLOR; };
            struct V { float4 cs:SV_POSITION; float3 ws:TEXCOORD0; half3 n:TEXCOORD1; half4 c:COLOR; half3 sh:TEXCOORD2; };
            V vert(A i)
            {
                V o; VertexPositionInputs p = GetVertexPositionInputs(i.pos.xyz);
                o.cs = p.positionCS; o.ws = p.positionWS; o.n = TransformObjectToWorldNormal(i.n); o.c = i.c; o.sh = SampleSH(o.n);
                return o;
            }
            // Smooth value noise, for which tile of a kind lies where.
            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
            }
            // Which tile, and where in it, at this spot. A noise over the land, a few metres across, counts through "looks": each look is
            // one of the kind's tiles (slice) shifted by its own random offset, so neither the tile grid nor one tile repeats in sight.
            // Two neighbouring looks are blended (k), which keeps the change soft (after Inigo Quilez's texture repetition technique).
            struct Pick { float sa, sb, k; float2 oa, ob; };
            Pick Choose(float2 ws, float count, float seed)
            {
                Pick p;
                count = max(count, 1);
                float v = Noise(ws / 13 + seed) * 0.6 + Noise(ws / 37 + seed * 1.7) * 0.4;
                v = v * 1.5 * count;
                float ia = floor(v), ib = ia + 1;
                p.k = smoothstep(0.0, 1.0, frac(v));
                p.sa = fmod(ia, count); p.sb = fmod(ib, count);
                p.oa = frac(sin(float2(ia * 3.17 + seed, ia * 7.31 - seed)) * 137.5);
                p.ob = frac(sin(float2(ib * 3.17 + seed, ib * 7.31 - seed)) * 137.5);
                return p;
            }
            // Samples are taken inside branches that differ from pixel to pixel, where the GPU cannot work out the mip level itself:
            // the uv's screen gradients are worked out once, outside them (G), and every sample is given them.
            struct G { float2 dx, dy; };
            G Grad(float2 uv) { G g; g.dx = ddx(uv); g.dy = ddy(uv); return g; }
            G Scale(G g, float k) { g.dx *= k; g.dy *= k; return g; }
            // Two scales of the same tile mixed: the repeat of one hides the repeat of the other.
            half3 One(Texture2DArray t, float2 uv, float slice, G g)
            {
                half3 fine = t.SampleGrad(sampler_Grass, float3(uv, slice), g.dx, g.dy).rgb;
                half3 broad = t.SampleGrad(sampler_Grass, float3(uv * 0.13 + 0.13, slice), g.dx * 0.13, g.dy * 0.13).rgb;
                half l = dot(broad, half3(0.3, 0.59, 0.11)) / max(0.05, dot(fine, half3(0.3, 0.59, 0.11)) * 0.5 + 0.25);
                return fine * clamp(0.55 + l * 0.45, 0.75, 1.25);
            }
            half3 Two(Texture2DArray t, float2 uv, Pick p, G g)
            {
                half3 c = One(t, uv + p.oa, p.sa, g);
                if (p.k > 0.01) c = lerp(c, One(t, uv + p.ob, p.sb, g), p.k);
                return c;
            }
            half3 N(Texture2DArray t, float2 uv, Pick p, G g)
            {
                half3 n = UnpackNormal(t.SampleGrad(sampler_Grass, float3(uv + p.oa, p.sa), g.dx, g.dy));
                if (p.k > 0.01) n = lerp(n, UnpackNormal(t.SampleGrad(sampler_Grass, float3(uv + p.ob, p.sb), g.dx, g.dy)), p.k);
                return n;
            }

            half4 frag(V i) : SV_Target
            {
                float2 uv = i.ws.xz / _Scale;
                // Height-aware weights: rocks and stones poke through a blend rather than fading into it.
                half4 w = saturate(i.c);
                half g = saturate(1 - w.r - w.g - w.b - w.a);
                half3 col = 0; half3 nt = 0;
                float2 ws2 = i.ws.xz;
                float2 ruv = float2(i.ws.x + i.ws.z, i.ws.y * 1.5) / (_Scale * 1.4);     // a rock face is steep: projected along the slope
                G gd = Grad(uv), gr = Grad(ruv);
                if (g > 0.01) { Pick p = Choose(ws2, _GrassCount, 3.1); col += Two(_Grass, uv, p, gd) * g; nt += N(_GrassN, uv, p, gd) * g; }
                if (w.r > 0.01) { Pick p = Choose(ws2, _DryCount, 17.3); G q = Scale(gd, 0.9); col += Two(_Dry, uv * 0.9, p, q) * w.r; nt += N(_DryN, uv * 0.9, p, q) * w.r; }
                if (w.g > 0.01) { Pick p = Choose(ws2, _DirtCount, 41.7); G q = Scale(gd, 1.1); col += Two(_Dirt, uv * 1.1, p, q) * w.g; nt += N(_DirtN, uv * 1.1, p, q) * w.g; }
                if (w.b > 0.01)
                {
                    Pick pm = Choose(ws2, _MudCount, 63.9), pg = Choose(ws2, _MossCount, 88.2);
                    G q = Scale(gd, 0.8), qs = Scale(gd, 0.11);
                    half mossy = saturate(_Moss.SampleGrad(sampler_Grass, float3(uv * 0.11, 0), qs.dx, qs.dy).g * 2.2 - 0.6);
                    col += lerp(Two(_Mud, uv * 0.8, pm, q), Two(_Moss, uv * 0.8, pg, q), mossy) * w.b;
                    nt += N(_MudN, uv * 0.8, pm, q) * w.b;
                }
                if (w.a > 0.01)
                {
                    Pick p = Choose(ws2, _RockCount, 101.5);
                    col += Two(_Rock, ruv, p, gr) * w.a; nt += N(_RockN, ruv, p, gr) * w.a;
                }
                half3 n = normalize(i.n);
                half3 tang = normalize(cross(n, half3(0, 0, 1)));
                half3 bit = cross(tang, n);
                half3 nWS = normalize(tang * nt.x * -1 + bit * nt.y * -1 + n * max(nt.z, 0.25));

                half smooth = 0.1 + w.b * 0.32;

                // Blood map: soaks in, wet and dark, broken up by the smear tile.
                float2 buv = i.ws.xz * _BB_FieldToUv.xy + _BB_FieldToUv.zw;
                half2 marks = _BB_BloodMap.Sample(sampler_BB_BloodMap, buv).rg;
                half b = marks.r;
                if (marks.g > 0.004)
                {
                    // Burnt ground: black ash, broken up by the ground's own pattern.
                    half ash = saturate(marks.g * 1.6) * (0.75 + 0.25 * saturate(dot(col, half3(1, 1, 1))));
                    col = lerp(col, half3(0.022, 0.02, 0.018) + col * 0.05, ash);
                    smooth *= 1 - ash;
                }
                if (b > 0.004)
                {
                    half smear = 1 - _BB_BloodTile.Sample(sampler_BB_BloodTile, i.ws.xz * 0.21).g;
                    half soak = saturate((b * 1.7 + smear * 0.55 - 0.55) * 3.5);
                    col = lerp(col, half3(0.15, 0.006, 0.006) * (0.8 + col * 0.6), soak * 0.96);
                    smooth = lerp(smooth, 0.66, soak);
                }

                InputData d = (InputData)0;
                d.positionWS = i.ws; d.positionCS = i.cs; d.normalWS = nWS;
                d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.ws);
                d.shadowCoord = TransformWorldToShadowCoord(i.ws);
                d.fogCoord = InitializeInputDataFog(float4(i.ws, 1), 0);
                d.bakedGI = i.sh; d.shadowMask = half4(1,1,1,1);
                d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.cs);
                SurfaceData s = (SurfaceData)0;
                s.albedo = col; s.alpha = 1; s.occlusion = 1; s.normalTS = half3(0,0,1); s.smoothness = smooth;
                half4 c = UniversalFragmentPBR(d, s);
                c.rgb = MixFog(c.rgb, d.fogCoord);
                return half4(c.rgb, 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            struct A { float4 pos:POSITION; float3 n:NORMAL; };
            float4 vert(A i) : SV_POSITION
            {
                float3 ws = TransformObjectToWorld(i.pos.xyz);
                return ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(ws, TransformObjectToWorldNormal(i.n), _LightDirection)));
            }
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 vert(float4 pos:POSITION) : SV_POSITION { return TransformObjectToHClip(pos.xyz); }
            half frag(float4 cs:SV_POSITION) : SV_Target { return cs.z; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct A { float4 pos:POSITION; float3 n:NORMAL; };
            struct V { float4 cs:SV_POSITION; half3 n:TEXCOORD0; };
            V vert(A i) { V o; o.cs = TransformObjectToHClip(i.pos.xyz); o.n = TransformObjectToWorldNormal(i.n); return o; }
            half4 frag(V i) : SV_Target { return half4(NormalizeNormalPerPixel(i.n), 0); }
            ENDHLSL
        }
    }
}
