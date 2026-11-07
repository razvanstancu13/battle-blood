// Instanced ground decals (blood splats, pools, scorch marks): wet paint lit by the scene, drawn over the ground without writing depth.
// The instance colour's alpha fades them; a pool grows by its matrix.
Shader "BattleBlood/Decal"
{
    Properties
    {
        [MainTexture] _BaseMap ("Shape", 2D) = "white" {}
        _Color ("Colour", Color) = (1,1,1,1)
        _Smoothness ("Smoothness", Range(0,1)) = 0.72
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-50" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off Cull Off
            Offset -2, -2
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST; half4 _Color; half _Smoothness;
            CBUFFER_END
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(half, _Alpha)
            UNITY_INSTANCING_BUFFER_END(Props)
            struct A { float4 pos:POSITION; float3 n:NORMAL; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 cs:SV_POSITION; float2 uv:TEXCOORD0; float3 ws:TEXCOORD1; half3 n:TEXCOORD2; half a:TEXCOORD3; };
            V vert(A i)
            {
                V o; UNITY_SETUP_INSTANCE_ID(i);
                VertexPositionInputs p = GetVertexPositionInputs(i.pos.xyz);
                o.cs = p.positionCS; o.ws = p.positionWS; o.uv = i.uv;
                o.n = TransformObjectToWorldNormal(i.n);
                o.a = UNITY_ACCESS_INSTANCED_PROP(Props, _Alpha);
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _Color;
                tex.a *= i.a;
                if (tex.a < 0.01) discard;
                half3 n = normalize(i.n);
                half3 v = GetWorldSpaceNormalizeViewDir(i.ws);
                Light l = GetMainLight(TransformWorldToShadowCoord(i.ws));
                half3 h = normalize(l.direction + v);
                half spec = pow(saturate(dot(n, h)), lerp(8, 180, _Smoothness)) * _Smoothness * 1.4;
                half3 c = tex.rgb * (SampleSH(n) + l.color * saturate(dot(n, l.direction)) * l.shadowAttenuation) + l.color * spec * l.shadowAttenuation;
                return half4(MixFog(c, ComputeFogFactor(i.cs.z)), tex.a);
            }
            ENDHLSL
        }
    }
}
