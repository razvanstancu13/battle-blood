// Distant mountains: the generated rock and grass tiles seen from far off (projected from above and the sides), lit by the sun,
// snow on the high steep-free tops, and their own aerial haze instead of the scene's fog, which is far too thick at this distance:
// they fade to the fog colour slowly, so they stand as pale silhouettes behind the battle.
Shader "BattleBlood/Mountain"
{
    Properties
    {
        _Rock ("Rock", 2D) = "grey" {}
        _Grass ("Grass", 2D) = "green" {}
        _Scale ("Tile size (m)", Float) = 40
        _Haze ("Haze per km", Float) = 0.9
        _SnowLine ("Snow line (m)", Float) = 300
        _FogCol ("Haze colour", Color) = (.7,.72,.76,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+10" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_Rock); SAMPLER(sampler_Rock);
            TEXTURE2D(_Grass); SAMPLER(sampler_Grass);
            CBUFFER_START(UnityPerMaterial)
                float _Scale, _Haze, _SnowLine; float4 _FogCol;
            CBUFFER_END
            struct A { float4 pos:POSITION; float3 n:NORMAL; float4 col:COLOR; };
            struct V { float4 cs:SV_POSITION; float3 wp:TEXCOORD0; float3 n:TEXCOORD1; float4 col:TEXCOORD2; };
            V vert(A i)
            {
                V o;
                o.wp = TransformObjectToWorld(i.pos.xyz);
                o.cs = TransformWorldToHClip(o.wp);
                o.n = TransformObjectToWorldNormal(i.n);
                o.col = i.col;
                return o;
            }
            float4 frag(V i) : SV_Target
            {
                float3 n = normalize(i.n);
                float3 w = abs(n); w = w / (w.x + w.y + w.z);
                float2 uxz = i.wp.xz / _Scale, uxy = i.wp.xy / _Scale, uzy = i.wp.zy / _Scale;
                float3 rock = SAMPLE_TEXTURE2D(_Rock, sampler_Rock, uxz).rgb * w.y + SAMPLE_TEXTURE2D(_Rock, sampler_Rock, uxy).rgb * w.z + SAMPLE_TEXTURE2D(_Rock, sampler_Rock, uzy).rgb * w.x;
                float3 grass = SAMPLE_TEXTURE2D(_Grass, sampler_Grass, uxz).rgb;
                // Grass and forest low on gentle ground, rock on the steep and the high, snow on the gentler tops.
                float steep = 1 - n.y;
                float rocky = saturate(smoothstep(0.18, 0.4, steep) + smoothstep(_SnowLine * 0.35, _SnowLine * 0.8, i.wp.y));
                float3 albedo = lerp(grass * 0.75, rock * 0.9, rocky);
                float snow = smoothstep(_SnowLine - 30, _SnowLine + 40, i.wp.y + i.col.r * 60) * smoothstep(0.55, 0.3, steep);
                albedo = lerp(albedo, float3(0.86, 0.88, 0.92), snow);
                Light sun = GetMainLight();
                float ndl = saturate(dot(n, sun.direction));
                float3 c = albedo * (sun.color * ndl * 0.85 + SampleSH(n) * 0.9);
                // Aerial perspective: further is paler and bluer, never quite gone before the horizon.
                float d = distance(i.wp, _WorldSpaceCameraPos) / 1000;
                float haze = 1 - exp(-d * _Haze);
                c = lerp(c, _FogCol.rgb, saturate(haze * 0.92));
                return float4(c, 1);
            }
            ENDHLSL
        }
    }
}
