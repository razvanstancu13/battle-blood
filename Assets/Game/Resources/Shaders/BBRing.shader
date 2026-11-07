// Command markers on the ground: a ring per selected soldier, its bright arc showing his health; the same ring makes move and target markers.
// Drawn instanced, no texture: the ring is computed from the quad's UV.
Shader "BattleBlood/Ring"
{
    Properties
    {
        _Width ("Ring width", Range(0.02, 0.5)) = 0.12
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+10" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Ring"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _Width;
            CBUFFER_END
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Tint)
                UNITY_DEFINE_INSTANCED_PROP(float, _Fill)
            UNITY_INSTANCING_BUFFER_END(Props)
            struct A { float4 pos:POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 cs:SV_POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            V vert(A i) { V o; UNITY_SETUP_INSTANCE_ID(i); UNITY_TRANSFER_INSTANCE_ID(i, o); o.cs = TransformObjectToHClip(i.pos.xyz); o.uv = i.uv; return o; }
            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float4 tint = UNITY_ACCESS_INSTANCED_PROP(Props, _Tint);
                float fill = UNITY_ACCESS_INSTANCED_PROP(Props, _Fill);
                float2 p = i.uv * 2 - 1;
                float r = length(p);
                float aa = fwidth(r) * 1.5;
                float ring = smoothstep(1 - _Width - aa, 1 - _Width, r) * (1 - smoothstep(1 - aa, 1, r));
                // The arc from 12 o'clock round clockwise is the health left; the rest is dim.
                float a = frac(atan2(p.x, p.y) / 6.28318 + 1);
                float lit = a <= fill ? 1.0 : 0.28;
                return half4(tint.rgb, tint.a * ring * lit);
            }
            ENDHLSL
        }
    }
}
