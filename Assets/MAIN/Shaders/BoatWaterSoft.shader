Shader "MAIN/BoatWaterSoft"
{
    Properties
    {
        _Shallow("Shallow", Color) = (0.12, 0.30, 0.46, 0.55)
        _Deep("Deep", Color) = (0.15, 0.21, 0.34, 0.72)
        _Glint("Glint", Color) = (0.62, 0.80, 0.88, 0.28)
        _Foam("Foam", Color) = (0.42, 0.58, 0.68, 0.35)
        _ContactMeters("Contact Meters", Range(0.2, 4)) = 1.55
        _FoamMeters("Foam Meters", Range(0.04, 0.8)) = 0.22
        _Wave("Wave", Range(0, 0.12)) = 0.038
    }
    SubShader
    {
        Tags
        {
            "Queue" = "Transparent+10"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }
        Pass
        {
            Name "WaterContact"
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
                float4 _Foam;
                float _ContactMeters;
                float _FoamMeters;
                float _Wave;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float fog : TEXCOORD1;
            };

            float WaveY(float3 ws, float t)
            {
                return sin(ws.x * 0.85 + t * 1.35) * _Wave
                    + sin(ws.z * 0.72 + t * 1.08 + 0.7) * _Wave * 0.78
                    + sin((ws.x + ws.z) * 1.55 + t * 2.05) * _Wave * 0.38
                    + sin(ws.x * 2.1 - ws.z * 1.4 + t * 2.6) * _Wave * 0.16;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 pos = input.positionOS.xyz;
                float3 ws = TransformObjectToWorld(pos);
                pos.y += WaveY(ws, _Time.y);
                VertexPositionInputs vi = GetVertexPositionInputs(pos);
                output.positionCS = vi.positionCS;
                output.positionWS = vi.positionWS;
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
                if (behind <= 0.002)
                    discard;

                float contact = 1.0 - saturate(behind / max(0.12, _ContactMeters));
                contact *= contact;
                if (contact < 0.02)
                    discard;

                float3 dx = ddx(input.positionWS);
                float3 dy = ddy(input.positionWS);
                float3 n = normalize(cross(dy, dx));
                if (n.y < 0.0)
                    n = -n;

                float3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float ndv = saturate(dot(n, v));
                float fresnel = pow(1.0 - ndv, 2.8);
                float deep = saturate(behind / 1.8);
                float3 col = lerp(_Shallow.rgb, _Deep.rgb, deep);
                col = lerp(col, _Glint.rgb, fresnel * _Glint.a);
                col += pow(ndv, 40.0) * 0.07;

                float foam = saturate(1.0 - behind / max(0.04, _FoamMeters));
                foam *= saturate(behind * 10.0);
                float ripple = 0.5 + 0.5 * sin(input.positionWS.x * 7.5 + input.positionWS.z * 6.2 + _Time.y * 4.2);
                col = lerp(col, _Foam.rgb, foam * (0.45 + ripple * 0.25));

                float alpha = lerp(_Shallow.a, _Deep.a, deep);
                alpha *= contact;
                alpha = max(alpha, foam * _Foam.a);
                alpha = min(alpha, 0.78);

                col = MixFog(col, input.fog);
                return float4(col, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
