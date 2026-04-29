Shader "VVardenfell/MwActorEntitiesGraphics"
{
    Properties
    {
        [NoScaleOffset] _BaseArray ("Base color array", 2DArray) = "white" {}
        _Cutoff ("Alpha clip threshold", Range(0, 1)) = 0.5
        [HideInInspector] _SrcBlend ("__src", Float) = 1
        [HideInInspector] _DstBlend ("__dst", Float) = 0
        [HideInInspector] _ZWrite ("__zw", Float) = 1
        [HideInInspector] _Slice ("__slc", Float) = 0
        [HideInInspector] _ActorDeformedMeshIndex ("__actorDeformedMeshIndex", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "IgnoreProjector" = "True"
        }
        LOD 200

        HLSLINCLUDE
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
                float _Slice;
                float _ActorDeformedMeshIndex;
            CBUFFER_END

            #if defined(DOTS_INSTANCING_ON)
            UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
                UNITY_DOTS_INSTANCED_PROP(float, _Slice)
                UNITY_DOTS_INSTANCED_PROP(float, _ActorDeformedMeshIndex)
            UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)
            #define _Slice UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _Slice)
            #define _ActorDeformedMeshIndex UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _ActorDeformedMeshIndex)
            #endif

            struct ActorDeformedVertex
            {
                float3 Position;
                float Padding0;
                float3 Normal;
                float Padding1;
            };

            StructuredBuffer<ActorDeformedVertex> _ActorDeformedVertices;
            float _VV_LocalMapRender;

            struct ActorVertexData
            {
                float3 PositionOS;
                float3 NormalOS;
            };

            ActorVertexData LoadActorVertex(uint vertexId)
            {
                uint baseIndex = (uint)max(0.0, _ActorDeformedMeshIndex);
                ActorDeformedVertex source = _ActorDeformedVertices[baseIndex + vertexId];
                ActorVertexData result;
                result.PositionOS = source.Position;
                result.NormalOS = source.Normal;
                return result;
            }
        ENDHLSL

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
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local _SURFACE_TYPE_TRANSPARENT
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #pragma multi_compile _ DOTS_INSTANCING_ON

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                uint vertexId : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float fogFactor : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                ActorVertexData actorVertex = LoadActorVertex(IN.vertexId);
                VertexPositionInputs pos = GetVertexPositionInputs(actorVertex.PositionOS);
                VertexNormalInputs nrm = GetVertexNormalInputs(actorVertex.NormalOS);
                OUT.positionCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                OUT.normalWS = nrm.normalWS;
                OUT.uv = IN.uv;
                OUT.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                half4 albedo = SAMPLE_TEXTURE2D_ARRAY(_BaseArray, sampler_BaseArray, IN.uv, _Slice);
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

                half3 normalWS = normalize(IN.normalWS);
                half3 lit = albedo.rgb * MwEvaluateDiffuseLighting(IN.positionWS, IN.positionCS, normalWS);
                lit = MixFog(lit, IN.fogFactor);

                #ifdef _SURFACE_TYPE_TRANSPARENT
                return half4(lit, albedo.a);
                #else
                return half4(lit, 1.0);
                #endif
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex depthVert
            #pragma fragment depthFrag
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON

            struct DepthAttributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                uint vertexId : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            DepthVaryings depthVert(DepthAttributes IN)
            {
                DepthVaryings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                ActorVertexData actorVertex = LoadActorVertex(IN.vertexId);
                OUT.positionCS = TransformObjectToHClip(actorVertex.PositionOS);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 depthFrag(DepthVaryings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                #ifdef _ALPHATEST_ON
                half alpha = SAMPLE_TEXTURE2D_ARRAY(_BaseArray, sampler_BaseArray, IN.uv, _Slice).a;
                clip(alpha - _Cutoff);
                #endif
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON

            float3 _LightDirection;

            struct ShadowAttributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                uint vertexId : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            ShadowVaryings shadowVert(ShadowAttributes IN)
            {
                ShadowVaryings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                ActorVertexData actorVertex = LoadActorVertex(IN.vertexId);
                float3 positionWS = TransformObjectToWorld(actorVertex.PositionOS);
                float3 normalWS = TransformObjectToWorldNormal(actorVertex.NormalOS);
                float4 posCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    posCS.z = min(posCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    posCS.z = max(posCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                OUT.positionCS = posCS;
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 shadowFrag(ShadowVaryings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                #ifdef _ALPHATEST_ON
                half alpha = SAMPLE_TEXTURE2D_ARRAY(_BaseArray, sampler_BaseArray, IN.uv, _Slice).a;
                clip(alpha - _Cutoff);
                #endif
                return 0;
            }
            ENDHLSL
        }
    }
}
