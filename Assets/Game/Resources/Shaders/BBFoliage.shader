// Grass, flower and reed cards: instanced thousands of times, swaying in the wind, shrinking away with distance, and stained by the blood map.
Shader "BattleBlood/Foliage"
{
    Properties
    {
        [MainTexture] _BaseMap ("Card", 2D) = "white" {}
        _Color ("Colour", Color) = (1,1,1,1)
        _FadeDist ("Fade distance", Float) = 90
        _Wind ("Wind", Float) = 0.12
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" }
        Cull Off
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BB_BloodMap); SAMPLER(sampler_BB_BloodMap);
        float4 _BB_FieldToUv;
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST; half4 _Color; float _FadeDist; float _Wind;
        CBUFFER_END
        float3 Sway(float3 pos, float height)
        {
            float3 ws = TransformObjectToWorld(pos);
            float t = _Time.y;
            float phase = ws.x * 0.31 + ws.z * 0.23;
            float gust = sin(t * 0.7 + phase * 0.25) * 0.6 + 0.6;
            float2 off = float2(sin(t * 1.9 + phase), cos(t * 1.4 + phase * 1.3)) * _Wind * gust * height * height;
            ws.xz += off;
            // Cards sink into the ground as they near the fade distance, so there is no popping line.
            float d = distance(ws.xz, _WorldSpaceCameraPos.xz);      // across the ground only, so the grass still shows from the high command view
            float fade = saturate((_FadeDist - d) / (_FadeDist * 0.5));      // a long fade, so seen from above there is no ring where the grass ends
            float3 root = TransformObjectToWorld(float3(pos.x, 0, pos.z));
            ws.y = lerp(root.y - 0.05, ws.y, fade);
            // Burnt away: what stood here lies flat.
            float burnt = SAMPLE_TEXTURE2D_LOD(_BB_BloodMap, sampler_BB_BloodMap, root.xz * _BB_FieldToUv.xy + _BB_FieldToUv.zw, 0).g;
            ws.y = lerp(ws.y, root.y - 0.02, saturate(burnt * 2.2));
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
            struct A { float4 pos:POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 cs:SV_POSITION; float2 uv:TEXCOORD0; float3 ws:TEXCOORD1; half h:TEXCOORD2; float fog:TEXCOORD3; UNITY_VERTEX_INPUT_INSTANCE_ID };
            V vert(A i)
            {
                V o; UNITY_SETUP_INSTANCE_ID(i); UNITY_TRANSFER_INSTANCE_ID(i, o);
                o.ws = Sway(i.pos.xyz, i.uv.y);
                o.cs = TransformWorldToHClip(o.ws);
                o.uv = i.uv; o.h = i.uv.y;
                o.fog = ComputeFogFactor(o.cs.z);
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half4 t = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                clip(t.a - 0.4);
                // Darker at the root (cheap ambient occlusion), but less so looking down: from above the dark roots read as black stars.
                half down = saturate(normalize(_WorldSpaceCameraPos - i.ws).y * 1.4 - 0.3);
                half3 col = t.rgb * _Color.rgb * lerp(lerp(0.45, 1.1, i.h), 1.05, down * 0.85);
                float2 buv = i.ws.xz * _BB_FieldToUv.xy + _BB_FieldToUv.zw;
                half b = SAMPLE_TEXTURE2D(_BB_BloodMap, sampler_BB_BloodMap, buv).r;
                col = lerp(col, half3(0.2, 0.01, 0.01), saturate(b * 2.2) * lerp(1.0, 0.55, i.h));
                Light l = GetMainLight(TransformWorldToShadowCoord(i.ws));
                half3 lit = col * (SampleSH(half3(0, 1, 0)) + l.color * l.shadowAttenuation * 0.72);
                return half4(MixFog(lit, i.fog), 1);
            }
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
            V vert(A i) { V o; UNITY_SETUP_INSTANCE_ID(i); o.cs = TransformWorldToHClip(Sway(i.pos.xyz, i.uv.y)); o.uv = i.uv; return o; }
            half frag(V i) : SV_Target { clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a - 0.4); return i.cs.z; }
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
            struct A { float4 pos:POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 cs:SV_POSITION; float2 uv:TEXCOORD0; };
            V vert(A i) { V o; UNITY_SETUP_INSTANCE_ID(i); o.cs = TransformWorldToHClip(Sway(i.pos.xyz, i.uv.y)); o.uv = i.uv; return o; }
            half4 frag(V i) : SV_Target { clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a - 0.4); return half4(0, 1, 0, 0); }
            ENDHLSL
        }
    }
}
