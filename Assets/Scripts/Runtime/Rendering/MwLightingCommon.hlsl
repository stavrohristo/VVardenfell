#ifndef VVARDENFELL_MW_LIGHTING_COMMON_INCLUDED
#define VVARDENFELL_MW_LIGHTING_COMMON_INCLUDED

static const half4 k_MwDefaultShadowMask = half4(1.0h, 1.0h, 1.0h, 1.0h);

float4 _VV_FogRange;
float4 _VV_FogColor;

half3 MwSampleEnvironmentDiffuse(half3 normalWS)
{
    return SampleSH(normalWS);
}

void MwSampleScreenSpaceAmbientOcclusion(float4 positionCS, out half indirectAmbientOcclusion, out half directAmbientOcclusion)
{
    indirectAmbientOcclusion = half(1.0h);
    directAmbientOcclusion = half(1.0h);

    #if defined(_SCREEN_SPACE_OCCLUSION)
        AmbientOcclusionFactor aoFactor = GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(positionCS));
        indirectAmbientOcclusion = aoFactor.indirectAmbientOcclusion;
        directAmbientOcclusion = aoFactor.directAmbientOcclusion;
    #endif
}

half3 MwEvaluateMainLightDiffuse(
    float3 positionWS,
    half3 normalWS,
    half4 shadowMask,
    half directAmbientOcclusion)
{
    float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
    Light mainLight = GetMainLight(shadowCoord, positionWS, shadowMask);

    #if defined(_LIGHT_LAYERS)
        if (!IsMatchingLightLayer(mainLight.layerMask, GetMeshRenderingLayer()))
            return half3(0.0h, 0.0h, 0.0h);
    #endif

    half ndotl = saturate(dot(normalWS, mainLight.direction));
    return mainLight.color
        * (mainLight.distanceAttenuation * mainLight.shadowAttenuation * directAmbientOcclusion * ndotl);
}

half3 MwEvaluateAdditionalLightsDiffuse(
    float3 positionWS,
    half3 normalWS,
    float4 positionCS,
    half4 shadowMask,
    half directAmbientOcclusion)
{
    half3 result = half3(0.0h, 0.0h, 0.0h);

    #if defined(_ADDITIONAL_LIGHTS) || defined(_CLUSTER_LIGHT_LOOP)
        InputData inputData = (InputData)0;
        inputData.positionWS = positionWS;
        inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(positionCS);
    #endif

    #if defined(_ADDITIONAL_LIGHTS)
        uint lightCount = GetAdditionalLightsCount();
        uint meshRenderingLayers = GetMeshRenderingLayer();

        LIGHT_LOOP_BEGIN(lightCount)
            Light additionalLight = GetAdditionalLight(lightIndex, positionWS, shadowMask);

            #if defined(_LIGHT_LAYERS)
                if (!IsMatchingLightLayer(additionalLight.layerMask, meshRenderingLayers))
                    continue;
            #endif

            half ndotl = saturate(dot(normalWS, additionalLight.direction));
            result += additionalLight.color
                * (additionalLight.distanceAttenuation * additionalLight.shadowAttenuation * directAmbientOcclusion * ndotl);
        LIGHT_LOOP_END
    #endif

    return result;
}

half3 MwEvaluateDiffuseLighting(float3 positionWS, float4 positionCS, half3 normalWS)
{
    half indirectAmbientOcclusion;
    half directAmbientOcclusion;
    MwSampleScreenSpaceAmbientOcclusion(positionCS, indirectAmbientOcclusion, directAmbientOcclusion);

    half3 environment = MwSampleEnvironmentDiffuse(normalWS) * indirectAmbientOcclusion;
    half3 mainLight = MwEvaluateMainLightDiffuse(positionWS, normalWS, k_MwDefaultShadowMask, directAmbientOcclusion);
    half3 additionalLights = MwEvaluateAdditionalLightsDiffuse(
        positionWS,
        normalWS,
        positionCS,
        k_MwDefaultShadowMask,
        directAmbientOcclusion);
    return environment + mainLight + additionalLights;
}

half3 MwMixRadialFog(half3 color, float3 positionWS)
{
    if (_VV_FogRange.w <= 0.5)
        return color;

    float fogAmount = saturate((distance(_WorldSpaceCameraPos.xyz, positionWS) - _VV_FogRange.x) * _VV_FogRange.z);
    return lerp(color, _VV_FogColor.rgb, fogAmount);
}

#endif
