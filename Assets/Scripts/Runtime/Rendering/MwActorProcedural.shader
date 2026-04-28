Shader "VVardenfell/MwActorProcedural"
{
    Properties
    {
        [NoScaleOffset] _BaseArray ("Base color array", 2DArray) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _Cutoff ("Alpha clip threshold", Range(0, 1)) = 0.5
        _Smoothness ("Smoothness", Range(0, 1)) = 0.25
        _Metallic ("Metallic", Range(0, 1)) = 0
        _SpecColor ("Specular", Color) = (0.2, 0.2, 0.2, 1)
        [HDR] _EmissionColor ("Emission", Color) = (0, 0, 0, 0)
        _OcclusionStrength ("Occlusion", Range(0, 1)) = 1
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
            "UniversalMaterialType" = "Lit"
            "IgnoreProjector" = "True"
        }

        HLSLINCLUDE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/Utils/SurfaceType.hlsl"

            TEXTURE2D_ARRAY(_BaseArray); SAMPLER(sampler_BaseArray);

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _SpecColor;
                half4 _EmissionColor;
                half _Cutoff;
                half _Smoothness;
                half _Metallic;
                half _OcclusionStrength;
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

            struct ActorSurface
            {
                float3 PositionWS;
                float3 NormalWS;
                float2 Uv;
                int TextureSlice;
                float4 PositionCS;
            };

            StructuredBuffer<ActorVertex> _ActorVertices;
            StructuredBuffer<int> _ActorIndices;
            StructuredBuffer<ActorMatrix3x4> _ActorCpuBoneMatrices;
            StructuredBuffer<ActorMatrix3x4> _ActorGpuBoneMatrices;
            StructuredBuffer<ActorDraw> _ActorDraws;
            int _ActorDrawBase;

            half4 _VV_EnvironmentAmbientColor;
            float3 _LightDirection;
            float3 _LightPosition;

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
                if (boneIndex < 0)
                    return IdentityMatrix3x4();

                if (sourceIndex != 0)
                    return _ActorGpuBoneMatrices[baseIndex + boneIndex];

                return _ActorCpuBoneMatrices[baseIndex + boneIndex];
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

            ActorSurface BuildActorSurface(uint vertexId, uint instanceId)
            {
                ActorDraw draw = _ActorDraws[_ActorDrawBase + instanceId];
                int sourceIndex = _ActorIndices[draw.FirstIndex + vertexId];
                ActorVertex source = _ActorVertices[draw.FirstVertex + sourceIndex];

                float3 skinnedPosition = 0.0;
                float3 skinnedNormal = 0.0;
                AccumulateInfluence(source.BoneIndices0.x, source.Weights0.x, draw.BoneMatrixOffset, source.Position, source.Normal, skinnedPosition, skinnedNormal, draw.BoneMatrixSource);
                AccumulateInfluence(source.BoneIndices0.y, source.Weights0.y, draw.BoneMatrixOffset, source.Position, source.Normal, skinnedPosition, skinnedNormal, draw.BoneMatrixSource);
                AccumulateInfluence(source.BoneIndices0.z, source.Weights0.z, draw.BoneMatrixOffset, source.Position, source.Normal, skinnedPosition, skinnedNormal, draw.BoneMatrixSource);
                AccumulateInfluence(source.BoneIndices0.w, source.Weights0.w, draw.BoneMatrixOffset, source.Position, source.Normal, skinnedPosition, skinnedNormal, draw.BoneMatrixSource);
                AccumulateInfluence(source.BoneIndices1.x, source.Weights1.x, draw.BoneMatrixOffset, source.Position, source.Normal, skinnedPosition, skinnedNormal, draw.BoneMatrixSource);
                AccumulateInfluence(source.BoneIndices1.y, source.Weights1.y, draw.BoneMatrixOffset, source.Position, source.Normal, skinnedPosition, skinnedNormal, draw.BoneMatrixSource);
                AccumulateInfluence(source.BoneIndices1.z, source.Weights1.z, draw.BoneMatrixOffset, source.Position, source.Normal, skinnedPosition, skinnedNormal, draw.BoneMatrixSource);
                AccumulateInfluence(source.BoneIndices1.w, source.Weights1.w, draw.BoneMatrixOffset, source.Position, source.Normal, skinnedPosition, skinnedNormal, draw.BoneMatrixSource);

                float weightSum = dot(source.Weights0, float4(1.0, 1.0, 1.0, 1.0))
                    + dot(source.Weights1, float4(1.0, 1.0, 1.0, 1.0));
                if (weightSum <= 0.0)
                {
                    skinnedPosition = source.Position;
                    skinnedNormal = source.Normal;
                }

                ActorSurface surface;
                surface.PositionWS = TransformActorPoint(draw.LocalToWorld, skinnedPosition);
                surface.NormalWS = NormalizeNormalPerVertex(TransformActorDirection(draw.LocalToWorld, skinnedNormal));
                surface.Uv = source.Uv;
                surface.TextureSlice = draw.TextureSlice;
                surface.PositionCS = TransformWorldToHClip(surface.PositionWS);
                return surface;
            }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag

            #pragma shader_feature_local_fragment _SURFACE_TYPE_TRANSPARENT
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _SPECULARHIGHLIGHTS_OFF
            #pragma shader_feature_local_fragment _ENVIRONMENTREFLECTIONS_OFF
            #pragma shader_feature_local_fragment _SPECULAR_SETUP

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half4 fogFactorAndVertexLight : TEXCOORD3;
                nointerpolation int textureSlice : TEXCOORD4;
            };

            Varyings vert(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
            {
                ActorSurface surface = BuildActorSurface(vertexId, instanceId);

                half fogFactor = 0;
                #if !defined(_FOG_FRAGMENT)
                    fogFactor = ComputeFogFactor(surface.PositionCS.z);
                #endif

                Varyings OUT;
                OUT.positionCS = surface.PositionCS;
                OUT.positionWS = surface.PositionWS;
                OUT.normalWS = surface.NormalWS;
                OUT.uv = surface.Uv;
                OUT.textureSlice = surface.TextureSlice;
                OUT.fogFactorAndVertexLight = half4(fogFactor, VertexLighting(surface.PositionWS, surface.NormalWS));
                return OUT;
            }

            SurfaceData BuildActorSurfaceData(half4 albedo)
            {
                half alpha = albedo.a * _BaseColor.a;
                #ifdef _ALPHATEST_ON
                    clip(alpha - _Cutoff);
                #endif

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = AlphaModulate(albedo.rgb * _BaseColor.rgb, alpha);
                surfaceData.metallic = _Metallic;
                surfaceData.specular = _SpecColor.rgb;
                surfaceData.smoothness = _Smoothness;
                surfaceData.normalTS = half3(0.0, 0.0, 1.0);
                surfaceData.emission = _EmissionColor.rgb;
                surfaceData.occlusion = _OcclusionStrength;
                surfaceData.alpha = alpha;
                surfaceData.clearCoatMask = 0.0;
                surfaceData.clearCoatSmoothness = 1.0;
                return surfaceData;
            }

            InputData BuildActorInputData(Varyings IN)
            {
                InputData inputData = (InputData)0;
                inputData.positionWS = IN.positionWS;
                inputData.positionCS = IN.positionCS;
                inputData.normalWS = NormalizeNormalPerPixel(IN.normalWS);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                inputData.fogCoord = InitializeInputDataFog(float4(IN.positionWS, 1.0), IN.fogFactorAndVertexLight.x);
                inputData.vertexLighting = IN.fogFactorAndVertexLight.yzw;
                inputData.bakedGI = max(SampleSH(inputData.normalWS), _VV_EnvironmentAmbientColor.rgb);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                inputData.shadowMask = half4(1.0, 1.0, 1.0, 1.0);
                return inputData;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 albedo = SAMPLE_TEXTURE2D_ARRAY(_BaseArray, sampler_BaseArray, IN.uv, IN.textureSlice);
                SurfaceData surfaceData = BuildActorSurfaceData(albedo);
                InputData inputData = BuildActorInputData(IN);

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = OutputAlpha(color.a, IsSurfaceTypeTransparent());
                return color;
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
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                nointerpolation int textureSlice : TEXCOORD1;
            };

            ShadowVaryings shadowVert(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
            {
                ActorSurface surface = BuildActorSurface(vertexId, instanceId);

                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDirectionWS = normalize(_LightPosition - surface.PositionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif

                ShadowVaryings OUT;
                OUT.positionCS = TransformWorldToHClip(ApplyShadowBias(surface.PositionWS, surface.NormalWS, lightDirectionWS));
                OUT.positionCS = ApplyShadowClamping(OUT.positionCS);
                OUT.uv = surface.Uv;
                OUT.textureSlice = surface.TextureSlice;
                return OUT;
            }

            half4 shadowFrag(ShadowVaryings IN) : SV_Target
            {
                #ifdef _ALPHATEST_ON
                    half alpha = SAMPLE_TEXTURE2D_ARRAY(_BaseArray, sampler_BaseArray, IN.uv, IN.textureSlice).a * _BaseColor.a;
                    clip(alpha - _Cutoff);
                #endif

                return 0;
            }
            ENDHLSL
        }
    }
}
