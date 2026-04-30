Shader "VVardenfell/MwStaticRefInstanced"
{
    Properties
    {
        [NoScaleOffset] _BaseArray ("Base color array", 2DArray) = "white" {}
        _Cutoff ("Alpha clip threshold", Range(0, 1)) = 0.5
        [HideInInspector] _SrcBlend ("__src", Float) = 1
        [HideInInspector] _DstBlend ("__dst", Float) = 0
        [HideInInspector] _ZWrite ("__zw", Float) = 1
        [HideInInspector] _StaticRefTextureSlice ("__slice", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local _SURFACE_TYPE_TRANSPARENT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "Assets/Scripts/Runtime/Rendering/MwLightingCommon.hlsl"

            TEXTURE2D_ARRAY(_BaseArray); SAMPLER(sampler_BaseArray);

            CBUFFER_START(UnityPerMaterial)
                float _Cutoff;
                float _SrcBlend;
                float _DstBlend;
                float _ZWrite;
                float _StaticRefTextureSlice;
            CBUFFER_END

            struct StaticRefInstance
            {
                float4 Row0;
                float4 Row1;
                float4 Row2;
                float4 Data;
            };

            StructuredBuffer<StaticRefInstance> _StaticRefInstances;
            int _StaticRefInstanceBase;
            float _VV_LocalMapRender;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                uint instanceId : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float fogFactor : TEXCOORD3;
                float textureSlice : TEXCOORD4;
            };

            float3 TransformPoint(StaticRefInstance instanceData, float3 value)
            {
                float4 p = float4(value, 1.0);
                return float3(dot(instanceData.Row0, p), dot(instanceData.Row1, p), dot(instanceData.Row2, p));
            }

            float3 TransformDirection(StaticRefInstance instanceData, float3 value)
            {
                float4 p = float4(value, 0.0);
                return float3(dot(instanceData.Row0, p), dot(instanceData.Row1, p), dot(instanceData.Row2, p));
            }

            Varyings vert(Attributes IN)
            {
                StaticRefInstance instanceData = _StaticRefInstances[_StaticRefInstanceBase + IN.instanceId];
                float3 positionWS = TransformPoint(instanceData, IN.positionOS);
                float3 normalWS = NormalizeNormalPerVertex(TransformDirection(instanceData, IN.normalOS));
                float4 positionCS = TransformWorldToHClip(positionWS);

                Varyings OUT;
                OUT.positionCS = positionCS;
                OUT.positionWS = positionWS;
                OUT.normalWS = normalWS;
                OUT.uv = IN.uv;
                OUT.fogFactor = ComputeFogFactor(positionCS.z);
                OUT.textureSlice = instanceData.Data.x;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 albedo = SAMPLE_TEXTURE2D_ARRAY(_BaseArray, sampler_BaseArray, IN.uv, IN.textureSlice);
                #ifdef _ALPHATEST_ON
                clip(albedo.a - _Cutoff);
                #endif

                if (_VV_LocalMapRender > 0.5)
                {
                    #ifdef _SURFACE_TYPE_TRANSPARENT
                    return half4(albedo.rgb, albedo.a);
                    #else
                    return half4(albedo.rgb, 1.0);
                    #endif
                }

                half3 N = normalize(IN.normalWS);
                half3 lit = albedo.rgb * MwEvaluateDiffuseLighting(IN.positionWS, IN.positionCS, N);
                lit = MixFog(lit, IN.fogFactor);

                #ifdef _SURFACE_TYPE_TRANSPARENT
                return half4(lit, albedo.a);
                #else
                return half4(lit, 1.0);
                #endif
            }
            ENDHLSL
        }
    }
}
