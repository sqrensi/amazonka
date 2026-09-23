Shader "MAIN/BoatWaterSoft"
{
    Properties
    {
        _Shallow("Shallow", Color) = (0.22, 0.48, 0.52, 0.92)
        _Deep("Deep", Color) = (0.07, 0.18, 0.28, 0.96)
        _Glint("Glint", Color) = (0.55, 0.78, 0.82, 0.18)
        _ShoreMeters("Shore Fade Meters", Range(0.15, 6)) = 1.1
        _Wave("Wave", Range(0, 0.08)) = 0.02
    }
    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }
        Pass
        {
            Name "Water"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Shallow;
                float4 _Deep;
                float4 _Glint;
                float _ShoreMeters;
                float _Wave;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float fog : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 pos = input.positionOS.xyz;
                float3 ws = TransformObjectToWorld(pos);
                float t = _Time.y;
                pos.y += sin(ws.x * 0.35 + t * 0.85) * _Wave + sin(ws.z * 0.28 + t * 0.62) * _Wave * 0.7;
                VertexPositionInputs vi = GetVertexPositionInputs(pos);
                output.positionCS = vi.positionCS;
                output.positionWS = vi.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fog = ComputeFogFactor(vi.positionCS.z);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = GetNormalizedScreenSpaceUV(input.positionCS);
                float sceneRaw = SampleSceneDepth(uv);
                float sceneEye = LinearEyeDepth(sceneRaw, _ZBufferParams);
                float waterEye = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                float behind = sceneEye - waterEye;
                float band = lerp(_ShoreMeters, _ShoreMeters * 6.5, saturate(waterEye / 90.0));
                float shore = saturate(behind / max(0.12, band));
                if (shore <= 0.001)
                    discard;

                float3 n = normalize(input.normalWS);
                float3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float ndv = saturate(dot(n, v));
                float fresnel = pow(1.0 - ndv, 3.2);
                float deep = saturate(behind / 8.0);
                float3 col = lerp(_Shallow.rgb, _Deep.rgb, deep);
                col = lerp(col, _Glint.rgb, fresnel * _Glint.a);
                float spark = pow(ndv, 28.0) * 0.04;
                col += spark;

                float alpha = lerp(_Shallow.a, _Deep.a, deep);
                alpha *= shore;
                alpha = min(alpha, 0.94);

                col = MixFog(col, input.fog);
                return float4(col, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
