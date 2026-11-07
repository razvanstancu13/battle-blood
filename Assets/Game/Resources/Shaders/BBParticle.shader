// Particles: one shader for blood mist, droplets, smoke, dust, fire and sparks. _Additive = 1 glows (fire, sparks), 0 is an ordinary alpha blend.
Shader "BattleBlood/Particle"
{
    Properties
    {
        [MainTexture] _BaseMap ("Sprite", 2D) = "white" {}
        [HDR] _Color ("Colour", Color) = (1,1,1,1)
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst", Float) = 10
        _Lit ("Lit by the scene", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Particle"
            Tags { "LightMode"="UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST; half4 _Color; half _Lit;
            CBUFFER_END
            struct A { float4 pos:POSITION; half4 c:COLOR; float2 uv:TEXCOORD0; };
            struct V { float4 cs:SV_POSITION; half4 c:COLOR; float2 uv:TEXCOORD0; half3 light:TEXCOORD1; float fog:TEXCOORD2; };
            V vert(A i)
            {
                V o; VertexPositionInputs p = GetVertexPositionInputs(i.pos.xyz);
                o.cs = p.positionCS; o.c = i.c; o.uv = i.uv;
                Light l = GetMainLight();
                o.light = lerp(half3(1,1,1), SampleSH(half3(0,1,0)) + l.color * 0.6, _Lit);
                o.fog = ComputeFogFactor(o.cs.z);
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                half4 t = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _Color * i.c;
                t.rgb *= i.light;
                if (_Lit > 0.01) t.rgb = MixFog(t.rgb, i.fog);
                return t;
            }
            ENDHLSL
        }
    }
}
