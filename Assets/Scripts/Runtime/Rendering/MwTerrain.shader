Shader "VVardenfell/MwTerrain"
{
    // Faithful Morrowind-style terrain: 16x16 LTEX quadrant grid per cell, stored as a 16x16
    // R16_UInt splatmap; textures packed into native-resolution atlas pages. Fragment does a
    // 4-tap bilinear blend across the splatmap, sampling the atlas array 4 times per fragment.
    // Mathematically identical to OpenMW's per-layer alpha-blend multi-pass, compressed into
    // one draw call so Entities.Graphics batching stays intact.
    //
    // URP, forward-only, main directional light + ambient. No normal map, no specular —
    // vanilla MW terrain has none.

    Properties
    {
        [NoScaleOffset] _LayerArray ("Terrain layer atlas pages", 2DArray) = "white" {}
        [NoScaleOffset] _LayerMeta0 ("Terrain layer meta 0", 2D) = "black" {}
        [NoScaleOffset] _LayerMeta1 ("Terrain layer meta 1", 2D) = "black" {}
        [NoScaleOffset] _SplatArray ("Terrain splat array (R16 UInt, 16x16)", 2DArray) = "black" {}
        [HideInInspector] _SplatSlice ("__splatSlice", Float) = 0
        _TileScale  ("Diffuse tiles per cell edge", Float) = 16
        _SplatSize  ("Splatmap side (=LAND_TEXTURE_SIZE)", Float) = 16
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            // Entities.Graphics / BatchRendererGroup demands this variant to hoist UnityPerMaterial
            // into the per-instance buffer. Without it the BRG refuses to draw and logs a warning.
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #pragma multi_compile _ DOTS_INSTANCING_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "Assets/Scripts/Runtime/Rendering/MwLightingCommon.hlsl"

            TEXTURE2D_ARRAY(_LayerArray);        SAMPLER(sampler_LayerArray);
            TEXTURE2D(_LayerMeta0);
            TEXTURE2D(_LayerMeta1);
            TEXTURE2D_ARRAY(_SplatArray);        // point-sampled via Load(); no SamplerState needed

            CBUFFER_START(UnityPerMaterial)
                float _TileScale;
                float _SplatSize;
                float _SplatSlice;
            CBUFFER_END

            #if defined(DOTS_INSTANCING_ON)
            UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
                UNITY_DOTS_INSTANCED_PROP(float, _SplatSlice)
            UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)
            #define _SplatSlice UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _SplatSlice)
            #endif

            float _VV_LocalMapRender;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float2 uv          : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                // BRG / DOTS-instancing demands UNITY_SETUP_INSTANCE_ID + UNITY_TRANSFER_INSTANCE_ID
                // so unity_ObjectToWorld (UNITY_MATRIX_M) resolves from the per-instance buffer.
                // Omitting these is why every cell was collapsing onto the last-drawn cell's
                // world position — the fallback ObjectToWorld was shared across instances.
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS);
                VertexNormalInputs   nrm = GetVertexNormalInputs(IN.normalOS);
                OUT.positionCS  = pos.positionCS;
                OUT.positionWS  = pos.positionWS;
                OUT.normalWS    = nrm.normalWS;
                OUT.uv          = IN.uv;
                // shadowCoord intentionally NOT computed here — see frag(). With
                // cascades enabled, picking the cascade per-vertex and interpolating
                // produces ring-shaped seams at cascade boundaries (follows the
                // camera because cascades are camera-distance shells). Per-pixel
                // resolution is required on large triangles like terrain.
                return OUT;
            }

            // Load one splatmap texel as a layer index (R channel of an R16_UInt-style tex).
            // Unity exposes uint-format Texture2D through Load with an int3(x,y,mip) coord.
            float LoadLayer(int2 xy)
            {
                xy = clamp(xy, int2(0, 0), int2((int)_SplatSize - 1, (int)_SplatSize - 1));
                // Splatmap is R16 but sampled as RGBA16 — the layer index lives in the red channel.
                // We wrote it as UNorm16 so multiplying by 65535 gets us back the integer index.
                float r = _SplatArray.Load(int4(xy, (int)_SplatSlice, 0)).r;
                return floor(r * 65535.0 + 0.5);
            }

            float4 SampleLayer(float2 tiledUv, float layer)
            {
                int layerIndex = (int)layer;
                float4 meta0 = _LayerMeta0.Load(int3(layerIndex, 0, 0));
                float4 meta1 = _LayerMeta1.Load(int3(layerIndex, 0, 0));
                float page = meta0.x;
                float2 rectMin = meta0.yz;
                float2 rectSize = meta1.xy;
                float2 atlasUv = rectMin + frac(tiledUv) * rectSize;
                return SAMPLE_TEXTURE2D_ARRAY(_LayerArray, sampler_LayerArray, atlasUv, page);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                // Shift the splat sampling space so quadrant CENTRES land at integer coords.
                // Without the -0.5 the bilinear weights are zero at quadrant centres and the
                // blend becomes 50/50 at the middle of a quadrant — wrong for MW's "one tile per
                // quadrant, soft border in between" model. See OpenMW material.cpp:36-43.
                float2 sp = IN.uv * _SplatSize - 0.5;
                int2 p0 = int2(floor(sp));
                float2 f = sp - p0;

                float l00 = LoadLayer(p0 + int2(0, 0));
                float l10 = LoadLayer(p0 + int2(1, 0));
                float l01 = LoadLayer(p0 + int2(0, 1));
                float l11 = LoadLayer(p0 + int2(1, 1));

                float2 duv = IN.uv * _TileScale;
                float4 c00 = SampleLayer(duv, l00);
                float4 c10 = SampleLayer(duv, l10);
                float4 c01 = SampleLayer(duv, l01);
                float4 c11 = SampleLayer(duv, l11);

                float4 cx0 = lerp(c00, c10, f.x);
                float4 cx1 = lerp(c01, c11, f.x);
                float3 albedo = lerp(cx0, cx1, f.y).rgb;

                if (_VV_LocalMapRender > 0.5)
                    return half4(albedo, 1.0);

                // Minimal Lambert + ambient + main-light shadows. Compute shadow
                // coord per-pixel from positionWS so cascade selection is per-pixel
                // — interpolating a vert-selected cascade across huge terrain tris
                // caused ring artifacts at cascade split boundaries.
                half3 N = normalize(IN.normalWS);
                float3 lit = albedo * MwEvaluateDiffuseLighting(IN.positionWS, IN.positionCS, N);

                lit = MwMixRadialFog(lit, IN.positionWS);

                return half4(lit, 1.0);
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

            HLSLPROGRAM
            #pragma vertex depthVert
            #pragma fragment depthFrag
            #pragma target 4.5
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct DepthAttributes
            {
                float3 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            DepthVaryings depthVert(DepthAttributes IN)
            {
                DepthVaryings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS);
                return OUT;
            }

            half4 depthFrag(DepthVaryings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
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

            HLSLPROGRAM
            #pragma vertex   shadowVert
            #pragma fragment shadowFrag
            #pragma target 4.5
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct SAttr
            {
                float3 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct SVar
            {
                float4 positionCS : SV_POSITION;
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
                return OUT;
            }

            half4 shadowFrag(SVar IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                return 0;
            }
            ENDHLSL
        }
    }
}
