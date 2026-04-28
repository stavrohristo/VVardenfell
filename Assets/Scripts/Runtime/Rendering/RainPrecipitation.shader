Shader "VVardenfell/RainPrecipitation"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1, 1, 1, 1)
        _Alpha ("Alpha", Range(0, 1)) = 1
        _DepthFadeMeters ("Depth Fade Meters", Float) = 0.65
        _DepthClipBiasMeters ("Depth Clip Bias Meters", Float) = 0.08
        _StreakPresentation ("Streak Presentation", Vector) = (1, 1, 0, 0)
        _RainMaskTex ("Rain Mask", 2D) = "white" {}
        _RainMaskCenterSize ("Rain Mask Center Size", Vector) = (0, 0, 64, 64)
        _RainMaskParams ("Rain Mask Params", Vector) = (1, 0, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Cull Off
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_RainMaskTex);
            SAMPLER(sampler_RainMaskTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float _Alpha;
                float _DepthFadeMeters;
                float _DepthClipBiasMeters;
                float4 _StreakPresentation;
                float4 _RainMaskCenterSize;
                float4 _RainMaskParams;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float4 screenPosition : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float4 color : COLOR;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionHCS = positionInputs.positionCS;
                output.screenPosition = ComputeScreenPos(positionInputs.positionCS);
                output.positionWS = positionInputs.positionWS;
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.color = input.color;
                return output;
            }

            float DepthVisibility(Varyings input)
            {
                float2 screenUv = input.screenPosition.xy / max(0.0001, input.screenPosition.w);
                float rawSceneDepth = SampleSceneDepth(screenUv);
                float sceneEyeDepth = LinearEyeDepth(rawSceneDepth, _ZBufferParams);
                float fragmentEyeDepth = LinearEyeDepth(input.positionHCS.z, _ZBufferParams);
                float depthDelta = sceneEyeDepth - fragmentEyeDepth;
                return saturate((depthDelta + max(0.0, _DepthClipBiasMeters)) / max(0.001, _DepthFadeMeters));
            }

            float RainMaskVisibility(float3 positionWS)
            {
                float2 size = max(_RainMaskCenterSize.zw, float2(0.001, 0.001));
                float2 uv = (positionWS.xz - _RainMaskCenterSize.xy) / size + 0.5;
                float inside = step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
                float mask = SAMPLE_TEXTURE2D(_RainMaskTex, sampler_RainMaskTex, uv).r;
                return lerp(1.0, mask, inside * saturate(_RainMaskParams.x));
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half4 color = tex * _Color * input.color;
                color.rgb *= lerp(0.82, 1.18, saturate(input.uv.y)) * max(0.05, _StreakPresentation.x);
                color.a *= _Alpha;
                color.a *= DepthVisibility(input);
                color.a *= RainMaskVisibility(input.positionWS);
                clip(color.a - 0.002);
                return color;
            }
            ENDHLSL
        }
    }
}
