// Tree crowns made of photographic leaf cards (the grass's style), instanced by the clump. The mesh normals point out from each
// clump's middle, so a crown is lit as one rounded mass: bright on the sun's side, darker inside and underneath. They sway gently,
// more the higher they are, and they cast shadows.
Shader "BattleBlood/Leaves"
{
    Properties
    {
        [MainTexture] _BaseMap ("Card", 2D) = "white" {}
        _Color ("Colour", Color) = (1,1,1,1)
        _Wind ("Wind", Float) = 0.06
        _Cutoff ("Cut-off", Range(0,1)) = 0.45
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" }
        Cull Off
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST; half4 _Color; float _Wind; float _Cutoff;
        CBUFFER_END
        float3 Sway(float3 pos)
        {
            float3 ws = TransformObjectToWorld(pos);
            float3 origin = TransformObjectToWorld(float3(0, 0, 0));
            float t = _Time.y;
            float phase = origin.x * 0.21 + origin.z * 0.17;
            float gust = sin(t * 0.6 + phase * 0.3) * 0.5 + 0.7;
            float lift = saturate(origin.y * 0.08);           // the higher the clump, the more it moves
            ws.xz += float2(sin(t * 1.3 + phase + pos.y), cos(t * 1.1 + phase * 1.4 + pos.x)) * _Wind * gust * (0.4 + lift);
            return ws;
        }
        ENDHLSL
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            struct A { float4 pos:POSITION; float3 n:NORMAL; float2 uv:TEXCOORD0; float4 col:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 cs:SV_POSITION; float2 uv:TEXCOORD0; float3 ws:TEXCOORD1; half3 n:TEXCOORD2; half ao:TEXCOORD3; float fog:TEXCOORD4; UNITY_VERTEX_INPUT_INSTANCE_ID };
            V vert(A i)
            {
                V o; UNITY_SETUP_INSTANCE_ID(i); UNITY_TRANSFER_INSTANCE_ID(i, o);
                o.ws = Sway(i.pos.xyz);
                o.cs = TransformWorldToHClip(o.ws);
                o.uv = i.uv; o.n = TransformObjectToWorldNormal(i.n); o.ao = i.col.r;
                o.fog = ComputeFogFactor(o.cs.z);
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half4 t = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                clip(t.a - _Cutoff);
                half3 n = normalize(i.n);
                Light l = GetMainLight(TransformWorldToShadowCoord(i.ws));
                half wrap = saturate(dot(n, l.direction) * 0.5 + 0.5);            // soft light round the crown
                half3 col = t.rgb * _Color.rgb * lerp(0.55, 1.0, i.ao);           // inside the clump is darker
                half3 lit = col * (SampleSH(n) * 0.9 + l.color * l.shadowAttenuation * wrap * 0.85);
                return half4(MixFog(lit, i.fog), 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            struct A { float4 pos:POSITION; float3 n:NORMAL; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 cs:SV_POSITION; float2 uv:TEXCOORD0; };
            V vert(A i)
            {
                V o; UNITY_SETUP_INSTANCE_ID(i);
                float3 ws = Sway(i.pos.xyz);
                o.cs = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(ws, TransformObjectToWorldNormal(i.n), _LightDirection)));
                o.uv = i.uv; return o;
            }
            half4 frag(V i) : SV_Target { clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a - _Cutoff); return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct A { float4 pos:POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 cs:SV_POSITION; float2 uv:TEXCOORD0; };
            V vert(A i) { V o; UNITY_SETUP_INSTANCE_ID(i); o.cs = TransformWorldToHClip(Sway(i.pos.xyz)); o.uv = i.uv; return o; }
            half frag(V i) : SV_Target { clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a - _Cutoff); return i.cs.z; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct A { float4 pos:POSITION; float3 n:NORMAL; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 cs:SV_POSITION; float2 uv:TEXCOORD0; half3 n:TEXCOORD1; };
            V vert(A i) { V o; UNITY_SETUP_INSTANCE_ID(i); o.cs = TransformWorldToHClip(Sway(i.pos.xyz)); o.uv = i.uv; o.n = TransformObjectToWorldNormal(i.n); return o; }
            half4 frag(V i) : SV_Target { clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a - _Cutoff); return half4(normalize(i.n), 0); }
            ENDHLSL
        }
    }
}
