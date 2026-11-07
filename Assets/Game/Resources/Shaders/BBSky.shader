// Skybox: one LAGP panorama chosen by the battle's seed, a sun disc, and haze that meets the fog colour at the horizon.
Shader "BattleBlood/Sky"
{
    Properties
    {
        _MainTex ("Panorama", 2D) = "white" {}
        _SunDir ("Direction to sun", Vector) = (0,1,0,0)
        _SunCol ("Sun colour", Color) = (1.4,1.2,1,1)
        _FogCol ("Fog colour", Color) = (.7,.7,.75,1)
        _Haze ("Haze", Range(0,1)) = 0.3
        _Gain ("Gain", Float) = 1
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _SunDir, _SunCol, _FogCol; float _Haze, _Gain;
            CBUFFER_END
            struct A { float4 pos:POSITION; };
            struct V { float4 cs:SV_POSITION; float3 dir:TEXCOORD0; };
            V vert(A i) { V o; o.cs = TransformObjectToHClip(i.pos.xyz); o.dir = i.pos.xyz; return o; }
            float4 frag(V i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float elev = asin(clamp(d.y, -1, 1)) / 1.5708;
                float az = atan2(d.x, d.z) / 6.28318 + 0.5;
                float2 uv = float2(az, 0.2 + 0.8 * saturate(elev));
                float2 dx = ddx(float2(frac(az + 0.25), uv.y)), dy = ddy(float2(frac(az + 0.25), uv.y));
                float3 c = SAMPLE_TEXTURE2D_GRAD(_MainTex, sampler_MainTex, uv, dx, dy).rgb * _Gain;
                // The generated panoramas have a soft line down their middle: cross-fade to another part of the sky there.
                float seam = smoothstep(0.075, 0.03, abs(az - 0.5));
                if (seam > 0.001)
                    c = lerp(c, SAMPLE_TEXTURE2D_GRAD(_MainTex, sampler_MainTex, uv + float2(0.27, 0), dx, dy).rgb * _Gain, seam);
                float3 s = normalize(_SunDir.xyz);
                float cosA = dot(d, s);
                c += _SunCol.rgb * smoothstep(0.99945, 0.99975, cosA) * 18;
                c += _SunCol.rgb * pow(saturate(cosA), 340) * 1.1;
                c += _SunCol.rgb * pow(saturate(cosA), 18) * 0.2;
                float haze = saturate(1 - saturate(elev) * lerp(5.0, 1.2, _Haze));
                c = lerp(c, _FogCol.rgb, haze * lerp(0.75, 1.0, _Haze));
                if (d.y < 0) c = _FogCol.rgb;
                return float4(c, 1);
            }
            ENDHLSL
        }
    }
}
