Shader "Hidden/BoatWaterDepthBias"
{
    SubShader
    {
        Tags
        {
            "Queue" = "Transparent-1"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }
        Pass
        {
            Name "ShoreBias"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite On
            ZTest LEqual
            Offset 12, 8
            ColorMask 0
            Cull Off
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
