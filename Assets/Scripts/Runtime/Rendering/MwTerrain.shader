Shader "VVardenfell/MwTerrain"
{
    // Faithful Morrowind-style terrain: 16x16 LTEX quadrant grid per cell, stored as a 16x16
    // R16_UInt splatmap; textures packed into a single Texture2DArray. Fragment does a 4-tap
    // bilinear blend across the splatmap, sampling the texture array 4 times per fragment.
    // Mathematically identical to OpenMW's per-layer alpha-blend multi-pass, compressed into
    // one draw call so Entities.Graphics batching stays intact.
    //
    // URP, forward-only, main directional light + ambient. No normal map, no specular —
    // vanilla MW terrain has none.

    Properties
    {
        [NoScaleOffset] _LayerArray ("Terrain layer array", 2DArray) = "white" {}
        [NoScaleOffset] _Splat      ("Splatmap (R16 UInt, 16x16)", 2D) = "black" {}
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
            #pragma multi_compile _ _SHADOWS_SOFT
            // Entities.Graphics / BatchRendererGroup demands this variant to hoist UnityPerMaterial
            // into the per-instance buffer. Without it the BRG refuses to draw and logs a warning.
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #pragma multi_compile _ DOTS_INSTANCING_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            TEXTURE2D_ARRAY(_LayerArray);        SAMPLER(sampler_LayerArray);
            TEXTURE2D(_Splat);                   // point-sampled via Load(); no SamplerState needed

            CBUFFER_START(UnityPerMaterial)
                float _TileScale;
                float _SplatSize;
            CBUFFER_END

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
                float r = _Splat.Load(int3(xy, 0)).r;
                return floor(r * 65535.0 + 0.5);
            }

            float4 SampleLayer(float2 duv, float layer)
            {
                return SAMPLE_TEXTURE2D_ARRAY(_LayerArray, sampler_LayerArray, duv, layer);
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

                // Minimal Lambert + ambient + main-light shadows. Compute shadow
                // coord per-pixel from positionWS so cascade selection is per-pixel
                // — interpolating a vert-selected cascade across huge terrain tris
                // caused ring artifacts at cascade split boundaries.
                float3 N = normalize(IN.normalWS);
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                float ndotl = saturate(dot(N, mainLight.direction));
                float3 lit = albedo * (mainLight.color * ndotl * mainLight.shadowAttenuation + SampleSH(N));

                return half4(lit, 1.0);
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
