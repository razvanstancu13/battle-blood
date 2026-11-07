// The one model shader of Battle Blood: URP PBR with everything derived from a single LAGP texture.
// - team tint: near-white, unsaturated texels (cloth, shield face, canvas) take _TintColor, so one model serves both armies
// - metal: unsaturated mid greys become metallic by _MetalAmount (mail, plate, blades)
// - blood: a tiled smear mask soaks in as _Blood rises, wet and glossy
// - burn: _Burn chars the surface towards black, with ember cracks glowing while _Ember is up
Shader "BattleBlood/Lit"
{
    Properties
    {
        [MainTexture] _BaseMap ("Albedo", 2D) = "white" {}
        [MainColor] _Color ("Colour", Color) = (1,1,1,1)
        _TintColor ("Team tint", Color) = (1,1,1,1)
        _TintAmount ("Tint amount", Range(0,1)) = 0
        _TintWarm ("Tint only warm whites (cloth beside bright steel)", Range(0,1)) = 0
        _MetalAmount ("Metal from greys", Range(0,1)) = 0
        _Smooth ("Smoothness", Range(0,1)) = 0.25
        _Blood ("Blood", Range(0,1)) = 0
        _Burn ("Burn", Range(0,1)) = 0
        _Ember ("Ember glow", Range(0,1)) = 0
        _Cutoff ("Cutoff (0 = opaque)", Range(0,1)) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BB_BloodTile); SAMPLER(sampler_BB_BloodTile);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _Color;
            half4 _TintColor;
            half _TintAmount;
            half _TintWarm;
            half _MetalAmount;
            half _Smooth;
            half _Blood;
            half _Burn;
            half _Ember;
            half _Cutoff;
        CBUFFER_END
        void CutClip(float2 uv)
        {
            if (_Cutoff > 0.001) clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).a - _Cutoff);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct A { float4 pos:POSITION; float3 n:NORMAL; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 cs:SV_POSITION; float2 uv:TEXCOORD0; float3 ws:TEXCOORD1; half3 n:TEXCOORD2; half3 sh:TEXCOORD3; UNITY_VERTEX_INPUT_INSTANCE_ID };

            V vert(A i)
            {
                V o; UNITY_SETUP_INSTANCE_ID(i); UNITY_TRANSFER_INSTANCE_ID(i, o);
                VertexPositionInputs p = GetVertexPositionInputs(i.pos.xyz);
                o.cs = p.positionCS; o.ws = p.positionWS; o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                o.n = TransformObjectToWorldNormal(i.n);
                o.sh = SampleSH(o.n);
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                if (_Cutoff > 0.001) clip(tex.a - _Cutoff);
                half lum = dot(tex.rgb, half3(0.3, 0.59, 0.11));
                half mx = max(tex.r, max(tex.g, tex.b)), mn = min(tex.r, min(tex.g, tex.b));
                half sat = mx > 0.001 ? (mx - mn) / mx : 0;

                half cloth = saturate((lum - 0.42) * 6) * saturate((0.36 - sat) * 8) * _TintAmount;
                // A model with bright steel: its cloth is the warm, light white; steel is neutral or cool grey and keeps its colour.
                half warm = saturate((tex.r - tex.b - 0.015) * 60) * saturate((lum - 0.6) * 8);
                cloth *= lerp(1, warm, _TintWarm);
                half3 albedo = tex.rgb * _Color.rgb * lerp(half3(1,1,1), _TintColor.rgb, cloth);

                half grey = saturate((0.18 - sat) * 10) * saturate((lum - 0.06) * 12) * (1 - cloth);
                half metallic = grey * _MetalAmount;
                half smooth = lerp(_Smooth, 0.62, metallic);

                // Blood soaks in along the smear mask (dark in the tile = blood), in the atlas' own coordinates.
                half smear = 1 - SAMPLE_TEXTURE2D(_BB_BloodTile, sampler_BB_BloodTile, i.uv * 3.0).g;
                half soak = saturate((smear + _Blood * 1.25 - 1.0) * 5.0) * step(0.001, _Blood);
                albedo = lerp(albedo, half3(0.23, 0.012, 0.012), soak * 0.94);
                metallic *= 1 - soak;
                smooth = lerp(smooth, 0.72, soak);

                // Fire: charred from the mask outwards, embers in what is left of the cracks.
                half emberMask = 0;
                if (_Burn > 0.001)
                {
                    half n = SAMPLE_TEXTURE2D(_BB_BloodTile, sampler_BB_BloodTile, i.uv * 1.7 + 0.37).g;
                    half charred = saturate((n * 0.6 + _Burn * 1.5 - 0.6) * 3.0);
                    albedo = lerp(albedo, half3(0.018, 0.015, 0.013), charred);
                    metallic *= 1 - charred; smooth *= 1 - charred * 0.85;
                    half crack = 1 - abs(frac(n * 9.0) * 2 - 1);
                    emberMask = charred * smoothstep(0.82, 0.98, crack);
                }

                InputData d = (InputData)0;
                d.positionWS = i.ws; d.positionCS = i.cs; d.normalWS = normalize(i.n);
                d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.ws);
                d.shadowCoord = TransformWorldToShadowCoord(i.ws);
                d.fogCoord = InitializeInputDataFog(float4(i.ws, 1), 0);
                d.bakedGI = i.sh; d.shadowMask = half4(1,1,1,1);
                d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.cs);

                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo; s.alpha = 1; s.occlusion = 1; s.normalTS = half3(0,0,1);
                s.metallic = metallic; s.specular = 0; s.smoothness = smooth;
                s.emission = half3(6.0, 1.7, 0.25) * emberMask * _Ember;
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
            ZWrite On ZTest LEqual ColorMask 0 Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection; float3 _LightPosition;
            struct A { float4 pos:POSITION; float3 n:NORMAL; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 cs:SV_POSITION; float2 uv:TEXCOORD0; };
            V vert(A i)
            {
                V o; UNITY_SETUP_INSTANCE_ID(i);
                float3 ws = TransformObjectToWorld(i.pos.xyz);
                float3 n = TransformObjectToWorldNormal(i.n);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 l = normalize(_LightPosition - ws);
                #else
                    float3 l = _LightDirection;
                #endif
                o.cs = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(ws, n, l)));
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                return o;
            }
            half4 frag(V i) : SV_Target { CutClip(i.uv); return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode"="MotionVectors" }
            ColorMask RG
            Cull [_Cull]
            HLSLPROGRAM
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma shader_feature_local_vertex _ADD_PRECOMPUTED_VELOCITY
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ObjectMotionVectors.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct A { float4 pos:POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 cs:SV_POSITION; float2 uv:TEXCOORD0; };
            V vert(A i) { V o; UNITY_SETUP_INSTANCE_ID(i); o.cs = TransformObjectToHClip(i.pos.xyz); o.uv = TRANSFORM_TEX(i.uv, _BaseMap); return o; }
            half frag(V i) : SV_Target { CutClip(i.uv); return i.cs.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct A { float4 pos:POSITION; float3 n:NORMAL; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 cs:SV_POSITION; half3 n:TEXCOORD0; float2 uv:TEXCOORD1; };
            V vert(A i) { V o; UNITY_SETUP_INSTANCE_ID(i); o.cs = TransformObjectToHClip(i.pos.xyz); o.uv = TRANSFORM_TEX(i.uv, _BaseMap); o.n = TransformObjectToWorldNormal(i.n); return o; }
            half4 frag(V i) : SV_Target { CutClip(i.uv); return half4(NormalizeNormalPerPixel(i.n), 0); }
            ENDHLSL
        }
    }
}
