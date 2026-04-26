Shader "VVardenfell/MwActorProcedural"
{
    Properties
    {
        [NoScaleOffset] _BaseArray ("Base color array", 2DArray) = "white" {}
        _Cutoff ("Alpha clip threshold", Range(0, 1)) = 0.5
        [HideInInspector] _SrcBlend ("__src", Float) = 1
        [HideInInspector] _DstBlend ("__dst", Float) = 0
        [HideInInspector] _ZWrite   ("__zw",  Float) = 1
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
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local _SURFACE_TYPE_TRANSPARENT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            TEXTURE2D_ARRAY(_BaseArray); SAMPLER(sampler_BaseArray);

            CBUFFER_START(UnityPerMaterial)
                float _Cutoff;
                float _SrcBlend;
                float _DstBlend;
                float _ZWrite;
            CBUFFER_END

            struct ActorVertex
            {
                float3 Position;
                float3 Normal;
                float2 Uv;
                int4 BoneIndices0;
                int4 BoneIndices1;
                float4 Weights0;
                float4 Weights1;
            };

            struct ActorMatrix3x4
            {
                float4 Row0;
                float4 Row1;
                float4 Row2;
            };

            struct ActorDraw
            {
                int FirstIndex;
                int FirstVertex;
                int BoneMatrixOffset;
                int BoneMatrixSource;
                int TextureSlice;
                int Padding0;
                int Padding1;
                int Padding2;
                ActorMatrix3x4 LocalToWorld;
            };

            StructuredBuffer<ActorVertex> _ActorVertices;
            StructuredBuffer<int> _ActorIndices;
            StructuredBuffer<ActorMatrix3x4> _ActorCpuBoneMatrices;
            StructuredBuffer<ActorMatrix3x4> _ActorGpuBoneMatrices;
            StructuredBuffer<ActorDraw> _ActorDraws;
            int _ActorDrawBase;
            float4 _ActorMainLightDirection;
            float4 _ActorMainLightColor;
            float _ActorMainLightValid;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float fogFactor : TEXCOORD3;
                nointerpolation int textureSlice : TEXCOORD4;
            };

            ActorMatrix3x4 IdentityMatrix3x4()
            {
                ActorMatrix3x4 result;
                result.Row0 = float4(1.0, 0.0, 0.0, 0.0);
                result.Row1 = float4(0.0, 1.0, 0.0, 0.0);
                result.Row2 = float4(0.0, 0.0, 1.0, 0.0);
                return result;
            }

            ActorMatrix3x4 LoadBone(int boneIndex, int baseIndex, int sourceIndex)
            {
                if (boneIndex >= 0)
                {
                    if (sourceIndex != 0)
                        return _ActorGpuBoneMatrices[baseIndex + boneIndex];

                    return _ActorCpuBoneMatrices[baseIndex + boneIndex];
                }

                return IdentityMatrix3x4();
            }

            float3 TransformActorPoint(ActorMatrix3x4 actorMatrix, float3 objectPosition)
            {
                float4 p = float4(objectPosition, 1.0);
                return float3(dot(actorMatrix.Row0, p), dot(actorMatrix.Row1, p), dot(actorMatrix.Row2, p));
            }

            float3 TransformActorDirection(ActorMatrix3x4 actorMatrix, float3 objectDirection)
            {
                float4 v = float4(objectDirection, 0.0);
                return float3(dot(actorMatrix.Row0, v), dot(actorMatrix.Row1, v), dot(actorMatrix.Row2, v));
            }

            void AccumulateInfluence(
                int boneIndex,
                float weight,
                int boneBase,
                float3 objectPosition,
                float3 objectNormal,
                inout float3 skinnedPosition,
                inout float3 skinnedNormal,
                int boneSource)
            {
                if (weight <= 0.0 || boneIndex < 0)
                    return;

                ActorMatrix3x4 bone = LoadBone(boneIndex, boneBase, boneSource);
                skinnedPosition += TransformActorPoint(bone, objectPosition) * weight;
                skinnedNormal += TransformActorDirection(bone, objectNormal) * weight;
            }

            Varyings vert(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
            {
                ActorDraw draw = _ActorDraws[_ActorDrawBase + instanceId];
                int sourceIndex = _ActorIndices[draw.FirstIndex + vertexId];
                ActorVertex source = _ActorVertices[draw.FirstVertex + sourceIndex];

                float3 objectPosition = source.Position;
                float3 skinnedPosition = 0.0;
                float3 skinnedNormal = 0.0;

                AccumulateInfluence(source.BoneIndices0.x, source.Weights0.x, draw.BoneMatrixOffset, objectPosition, source.Normal, skinnedPosition, skinnedNormal, draw.BoneMatrixSource);
                AccumulateInfluence(source.BoneIndices0.y, source.Weights0.y, draw.BoneMatrixOffset, objectPosition, source.Normal, skinnedPosition, skinnedNormal, draw.BoneMatrixSource);
                AccumulateInfluence(source.BoneIndices0.z, source.Weights0.z, draw.BoneMatrixOffset, objectPosition, source.Normal, skinnedPosition, skinnedNormal, draw.BoneMatrixSource);
                AccumulateInfluence(source.BoneIndices0.w, source.Weights0.w, draw.BoneMatrixOffset, objectPosition, source.Normal, skinnedPosition, skinnedNormal, draw.BoneMatrixSource);
                AccumulateInfluence(source.BoneIndices1.x, source.Weights1.x, draw.BoneMatrixOffset, objectPosition, source.Normal, skinnedPosition, skinnedNormal, draw.BoneMatrixSource);
                AccumulateInfluence(source.BoneIndices1.y, source.Weights1.y, draw.BoneMatrixOffset, objectPosition, source.Normal, skinnedPosition, skinnedNormal, draw.BoneMatrixSource);
                AccumulateInfluence(source.BoneIndices1.z, source.Weights1.z, draw.BoneMatrixOffset, objectPosition, source.Normal, skinnedPosition, skinnedNormal, draw.BoneMatrixSource);
                AccumulateInfluence(source.BoneIndices1.w, source.Weights1.w, draw.BoneMatrixOffset, objectPosition, source.Normal, skinnedPosition, skinnedNormal, draw.BoneMatrixSource);

                float weightSum = dot(source.Weights0, float4(1.0, 1.0, 1.0, 1.0))
                    + dot(source.Weights1, float4(1.0, 1.0, 1.0, 1.0));
                if (weightSum <= 0.0)
                {
                    skinnedPosition = objectPosition;
                    skinnedNormal = source.Normal;
                }

                float3 positionWS = TransformActorPoint(draw.LocalToWorld, skinnedPosition);
                float3 normalWS = normalize(TransformActorDirection(draw.LocalToWorld, skinnedNormal));

                Varyings OUT;
                OUT.positionCS = TransformWorldToHClip(positionWS);
                OUT.positionWS = positionWS;
                OUT.normalWS = normalWS;
                OUT.uv = source.Uv;
                OUT.textureSlice = draw.TextureSlice;
                OUT.fogFactor = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 albedo = SAMPLE_TEXTURE2D_ARRAY(_BaseArray, sampler_BaseArray, IN.uv, IN.textureSlice);

                #ifdef _ALPHATEST_ON
                clip(albedo.a - _Cutoff);
                #endif

                float3 N = normalize(IN.normalWS);
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                float3 mainDirection = _ActorMainLightValid > 0.5
                    ? normalize(_ActorMainLightDirection.xyz)
                    : mainLight.direction;
                half3 mainColor = _ActorMainLightValid > 0.5
                    ? _ActorMainLightColor.rgb
                    : mainLight.color;
                float ndotl = saturate(dot(N, mainDirection));
                half3 lit = albedo.rgb * (mainColor * ndotl * mainLight.shadowAttenuation + SampleSH(N));

                #if defined(_ADDITIONAL_LIGHTS)
                uint additionalLightCount = GetAdditionalLightsCount();
                for (uint lightIndex = 0u; lightIndex < additionalLightCount; lightIndex++)
                {
                    Light additionalLight = GetAdditionalLight(lightIndex, IN.positionWS);
                    float addNdotL = saturate(dot(N, additionalLight.direction));
                    lit += albedo.rgb * additionalLight.color * addNdotL
                        * (additionalLight.distanceAttenuation * additionalLight.shadowAttenuation);
                }
                #endif

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
