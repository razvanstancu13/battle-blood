// Swamp water: a sky-tinted mirror with two panning ripple layers, opacity from the depth stored per vertex, reddening as the battle bleeds into it.
Shader "BattleBlood/Water"
{
    Properties
    {
        _Ripple ("Ripple normal", 2D) = "bump" {}
        _Deep ("Deep colour", Color) = (0.03,0.05,0.04,1)
        _SkyCol ("Sky colour", Color) = (0.6,0.65,0.7,1)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-60" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_Ripple); SAMPLER(sampler_Ripple);
            TEXTURE2D(_BB_BloodMap); SAMPLER(sampler_BB_BloodMap);
            float4 _BB_FieldToUv;
            CBUFFER_START(UnityPerMaterial)
                half4 _Deep; half4 _SkyCol;
            CBUFFER_END
            struct A { float4 pos:POSITION; float4 c:COLOR; };
            struct V { float4 cs:SV_POSITION; float3 ws:TEXCOORD0; half depth:TEXCOORD1; float fog:TEXCOORD2; };
            V vert(A i)
            {
                V o; o.ws = TransformObjectToWorld(i.pos.xyz); o.cs = TransformWorldToHClip(o.ws);
                o.depth = i.c.r; o.fog = ComputeFogFactor(o.cs.z);
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                float t = _Time.y * 0.03;
                half3 a = UnpackNormal(SAMPLE_TEXTURE2D(_Ripple, sampler_Ripple, i.ws.xz * 0.19 + float2(t, t * 0.6)));
                half3 b = UnpackNormal(SAMPLE_TEXTURE2D(_Ripple, sampler_Ripple, i.ws.xz * 0.07 - float2(t * 0.7, t)));
                half3 n = normalize(half3(a.x + b.x, 5.0, a.y + b.y));
                half3 v = GetWorldSpaceNormalizeViewDir(i.ws);
                half f = pow(1 - saturate(dot(n, v)), 4) * 0.9 + 0.05;
                Light l = GetMainLight(TransformWorldToShadowCoord(i.ws));
                half3 h = normalize(l.direction + v);
                half spec = pow(saturate(dot(n, h)), 220) * 3.0 * l.shadowAttenuation;
                float2 buv = i.ws.xz * _BB_FieldToUv.xy + _BB_FieldToUv.zw;
                half blood = saturate(SAMPLE_TEXTURE2D(_BB_BloodMap, sampler_BB_BloodMap, buv).r * 2.4);
                half3 deep = lerp(_Deep.rgb, half3(0.17, 0.008, 0.008), blood);
                half3 sky = lerp(_SkyCol.rgb, _SkyCol.rgb * half3(0.75, 0.3, 0.3), blood);
                half3 c = lerp(deep, sky, f) + l.color * spec;
                half alpha = saturate(i.depth * 1.6 + f * 0.6) * lerp(0.82, 0.96, blood);
                return half4(MixFog(c, i.fog), alpha);
            }
            ENDHLSL
        }
    }
}
