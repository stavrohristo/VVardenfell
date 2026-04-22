Shader "VVardenfell/MwRef"
{
    // Morrowind ref shader: samples a single Texture2DArray via a per-instance
    // slice index plumbed through DOTS_INSTANCING. Replaces the 1156-Material
    // explosion with 3 Materials (Opaque / AlphaTest / AlphaBlend), each
    // pointing at the same array. BRG batches collapse from thousands to a
    // handful per pass.
    //
    // UVs stay 0..1 per slice — MW's wall/floor textures tile exactly as before;
    // the slice axis picks which texture, the UV axes pick where in that tile.

    Properties
    {
        [NoScaleOffset] _BaseArray ("Base color array", 2DArray) = "white" {}
        _Cutoff ("Alpha clip threshold", Range(0, 1)) = 0.5

        // SRP Batcher requires every UnityPerMaterial CBUFFER field to be declared in
        // Properties too. These are driven in C# (CacheLoader.ApplyAlpha flips blend/zwrite
        // per alpha variant; _Slice is overridden per-instance by DOTS) — hidden from the
        // inspector because editing them on the Material asset does nothing useful.
        [HideInInspector] _SrcBlend ("__src", Float) = 1
        [HideInInspector] _DstBlend ("__dst", Float) = 0
        [HideInInspector] _ZWrite   ("__zw",  Float) = 1
        [HideInInspector] _Slice    ("__slc", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            // Blend/ZWrite toggled per-material in C# (see CacheLoader.ApplyAlpha).
            // Shader must honour whatever the keywords say, not bake fixed state.
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull  Back

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
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #pragma multi_compile _ DOTS_INSTANCING_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            TEXTURE2D_ARRAY(_BaseArray); SAMPLER(sampler_BaseArray);

            // _Slice lives in UnityPerMaterial so SRP Batcher handles the non-DOTS path,
            // and gets overridden by the DOTS-instancing macro below when the per-instance
            // buffer is active. Matches URP/Lit's _BaseColor pattern — keeping the CBUFFER
            // declaration avoids "undeclared identifier" errors on DX12 when the
            // UNITY_DOTS_INSTANCING_ENABLED guard doesn't match the DOTS_INSTANCING_ON
            // keyword for a given variant.
            CBUFFER_START(UnityPerMaterial)
                float _Cutoff;
                float _SrcBlend;
                float _DstBlend;
                float _ZWrite;
                float _Slice;
            CBUFFER_END

            #if defined(DOTS_INSTANCING_ON)
            UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
                UNITY_DOTS_INSTANCED_PROP(float, _Slice)
            UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)
            #define _Slice UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _Slice)
            #endif

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                float fogFactor   : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                // BRG / DOTS-instancing requires the SETUP/TRANSFER pair so
                // unity_ObjectToWorld resolves from the per-instance buffer.
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS);
                VertexNormalInputs   nrm = GetVertexNormalInputs(IN.normalOS);
                OUT.positionCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                OUT.normalWS   = nrm.normalWS;
                OUT.uv         = IN.uv;
                OUT.fogFactor  = ComputeFogFactor(pos.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                float slice = _Slice;
                half4 albedo = SAMPLE_TEXTURE2D_ARRAY(_BaseArray, sampler_BaseArray, IN.uv, slice);

                #ifdef _ALPHATEST_ON
                clip(albedo.a - _Cutoff);
                #endif

                // Per-pixel shadow coord — cascade selection is distance-based, so
                // baking it per-vertex produces ring seams on large refs (same issue
                // as MwTerrain.shader, same fix).
                float3 N = normalize(IN.normalWS);
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                float ndotl = saturate(dot(N, mainLight.direction));
                half3 lit = albedo.rgb * (mainLight.color * ndotl * mainLight.shadowAttenuation + SampleSH(N));

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

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex   shadowVert
            #pragma fragment shadowFrag
            #pragma target 4.5
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            TEXTURE2D_ARRAY(_BaseArray); SAMPLER(sampler_BaseArray);

            // Same CBUFFER layout as the ForwardLit pass so SRP Batcher treats the two
            // passes as one batchable material. _Slice is in the CBUFFER for the non-DOTS
            // path and overridden by the macro below when DOTS_INSTANCING_ON is active.
            CBUFFER_START(UnityPerMaterial)
                float _Cutoff;
                float _SrcBlend;
                float _DstBlend;
                float _ZWrite;
                float _Slice;
            CBUFFER_END

            #if defined(DOTS_INSTANCING_ON)
            UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
                UNITY_DOTS_INSTANCED_PROP(float, _Slice)
            UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)
            #define _Slice UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _Slice)
            #endif

            float3 _LightDirection;
            float3 _LightPosition;

            struct SAttr
            {
                float3 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct SVar
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            SVar shadowVert(SAttr IN)
            {
                SVar OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float3 positionWS = TransformObjectToWorld(IN.positionOS);
                float3 normalWS   = TransformObjectToWorldNormal(IN.normalOS);
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

            half4 shadowFrag(SVar IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                #ifdef _ALPHATEST_ON
                // Alpha-tested shadows need the texture too — cutout leaves cast
                // leaf-shaped shadows, not rectangle-shaped ones.
                float slice = _Slice;
                half4 sampled = SAMPLE_TEXTURE2D_ARRAY(_BaseArray, sampler_BaseArray, IN.uv, slice);
                clip(sampled.a - _Cutoff);
                #endif
                return 0;
            }
            ENDHLSL
        }
    }
}
