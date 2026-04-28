#ifndef VVARDENFELL_MW_LIGHTING_COMMON_INCLUDED
#define VVARDENFELL_MW_LIGHTING_COMMON_INCLUDED

static const half4 k_MwDefaultShadowMask = half4(1.0h, 1.0h, 1.0h, 1.0h);

half3 MwSampleEnvironmentDiffuse(half3 normalWS)
{
    return SampleSH(normalWS);
}

half3 MwEvaluateMainLightDiffuse(float3 positionWS, half3 normalWS, half4 shadowMask)
{
    float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
    Light mainLight = GetMainLight(shadowCoord, positionWS, shadowMask);

    #if defined(_LIGHT_LAYERS)
        if (!IsMatchingLightLayer(mainLight.layerMask, GetMeshRenderingLayer()))
            return half3(0.0h, 0.0h, 0.0h);
    #endif

    half ndotl = saturate(dot(normalWS, mainLight.direction));
    return mainLight.color * (mainLight.distanceAttenuation * mainLight.shadowAttenuation * ndotl);
}

half3 MwEvaluateAdditionalLightsDiffuse(float3 positionWS, half3 normalWS, float4 positionCS, half4 shadowMask)
{
    half3 result = half3(0.0h, 0.0h, 0.0h);

    #if defined(_ADDITIONAL_LIGHTS)
        InputData inputData = (InputData)0;
        inputData.positionWS = positionWS;
        inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(positionCS);

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
                * (additionalLight.distanceAttenuation * additionalLight.shadowAttenuation * ndotl);
        LIGHT_LOOP_END
    #endif

    return result;
}

half3 MwEvaluateDiffuseLighting(float3 positionWS, float4 positionCS, half3 normalWS)
{
    half3 environment = MwSampleEnvironmentDiffuse(normalWS);
    half3 mainLight = MwEvaluateMainLightDiffuse(positionWS, normalWS, k_MwDefaultShadowMask);
    half3 additionalLights = MwEvaluateAdditionalLightsDiffuse(positionWS, normalWS, positionCS, k_MwDefaultShadowMask);
    return environment + mainLight + additionalLights;
}

#endif
