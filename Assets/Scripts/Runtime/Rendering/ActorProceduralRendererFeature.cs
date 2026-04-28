using Unity.Profiling;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using VVardenfell.Runtime.Streaming;

namespace VVardenfell.Runtime.Rendering
{
    public sealed class ActorProceduralRendererFeature : ScriptableRendererFeature
    {
        const int ForwardPassIndex = 0;
        const int ShadowPassIndex = 1;
        const int MaxMainLightShadowSlices = 4;
        const int MaxAdditionalShadowRequests = 256;

        static readonly int k_ShadowBiasId = Shader.PropertyToID("_ShadowBias");
        static readonly int k_LightDirectionId = Shader.PropertyToID("_LightDirection");
        static readonly int k_LightPositionId = Shader.PropertyToID("_LightPosition");
        static readonly int k_ActorShadowViewProjectionId = Shader.PropertyToID("_ActorShadowViewProjection");
        static readonly int k_RenderingLayerId = Shader.PropertyToID("unity_RenderingLayer");
        const string CastingPunctualShadowKeywordName = "_CASTING_PUNCTUAL_LIGHT_SHADOW";

        [SerializeField] RenderPassEvent _renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
        [SerializeField] bool _enableMainLightActorShadows = true;
        [SerializeField] bool _enableAdditionalPointLightActorShadows = true;
        [SerializeField, Min(0)] int _maxAdditionalPointShadowLights = 4;
        [SerializeField] uint _actorRenderingLayerMask = 1u;
        [SerializeField, Min(0f)] float _actorShadowCasterPadding = 8f;
        [SerializeField, Min(0)] int _maxActorShadowCasters = 128;

        ActorProceduralForwardPass _forwardPass;
        ActorProceduralShadowPass _shadowPass;
        GlobalKeyword _castingPunctualShadowKeyword;
        bool _hasCastingPunctualShadowKeyword;

        void OnEnable()
        {
            EnsureGlobalKeywords();
        }

        public override void Create()
        {
            EnsureGlobalKeywords();
            _forwardPass = new ActorProceduralForwardPass
            {
                renderPassEvent = _renderPassEvent,
            };
            _shadowPass = new ActorProceduralShadowPass
            {
                renderPassEvent = RenderPassEvent.AfterRenderingShadows,
            };
        }

        void EnsureGlobalKeywords()
        {
            if (_hasCastingPunctualShadowKeyword)
                return;

            _castingPunctualShadowKeyword = GlobalKeyword.Create(CastingPunctualShadowKeywordName);
            _hasCastingPunctualShadowKeyword = true;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var resources = WorldResources.ActorProceduralRenderer;
            if (resources == null || (!resources.IsReadyForDraw && !resources.IsReadyForShadowDraw))
                return;

            WorldResources.ActorShadowCasterDistance = math.max(0f, renderingData.cameraData.maxShadowDistance);
            WorldResources.ActorShadowCasterPadding = math.max(0f, _actorShadowCasterPadding);
            WorldResources.MaxActorShadowCasters = math.max(0, _maxActorShadowCasters);

            Vector4 renderingLayer = new(math.asfloat(_actorRenderingLayerMask), 0f, 0f, 0f);

            if (resources.IsReadyForShadowDraw && (_enableMainLightActorShadows || _enableAdditionalPointLightActorShadows))
            {
                _shadowPass.Setup(
                    resources,
                    _enableMainLightActorShadows,
                    _enableAdditionalPointLightActorShadows,
                    _maxAdditionalPointShadowLights,
                    _castingPunctualShadowKeyword,
                    renderingLayer);
                renderer.EnqueuePass(_shadowPass);
            }

            if (resources.IsReadyForDraw)
            {
                _forwardPass.Setup(resources, renderingLayer);
                renderer.EnqueuePass(_forwardPass);
            }
        }

        sealed class ActorProceduralForwardPass : ScriptableRenderPass
        {
            static readonly ProfilerMarker k_ExecutePass = new("VV.ActorProcedural.ExecutePass");
            static readonly ProfilerMarker k_BindResources = new("VV.ActorProcedural.BindResources");
            static readonly ProfilerMarker k_SubmitBatchDraws = new("VV.ActorProcedural.SubmitBatchDraws");

            sealed class PassData
            {
                public ActorProceduralRenderResources Resources;
                public MaterialPropertyBlock Properties;
                public Vector4 RenderingLayer;
            }

            readonly MaterialPropertyBlock _properties = new();
            ActorProceduralRenderResources _resources;
            Vector4 _renderingLayer;

            public void Setup(ActorProceduralRenderResources resources, Vector4 renderingLayer)
            {
                _resources = resources;
                _renderingLayer = renderingLayer;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (_resources == null || !_resources.IsReadyForDraw)
                    return;

                using var builder = renderGraph.AddRasterRenderPass<PassData>("VV Actor Procedural Render", out var passData);
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                builder.UseAllGlobalTextures(true);
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.ReadWrite);
                builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.ReadWrite);

                TextureHandle mainShadowsTexture = resourceData.mainShadowsTexture;
                if (mainShadowsTexture.IsValid())
                    builder.UseTexture(mainShadowsTexture, AccessFlags.Read);

                TextureHandle additionalShadowsTexture = resourceData.additionalShadowsTexture;
                if (additionalShadowsTexture.IsValid())
                    builder.UseTexture(additionalShadowsTexture, AccessFlags.Read);

                builder.AllowPassCulling(false);

                passData.Resources = _resources;
                passData.Properties = _properties;
                passData.RenderingLayer = _renderingLayer;
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) => ExecutePass(data, context));
            }

            static void ExecutePass(PassData data, RasterGraphContext context)
            {
                using var executeScope = k_ExecutePass.Auto();
                var resources = data.Resources;
                if (resources == null || !resources.IsReadyForDraw)
                    return;

                MaterialPropertyBlock properties = data.Properties;
                using (k_BindResources.Auto())
                {
                    resources.Bind(properties, ActorProceduralRenderSet.Forward);
                }
                properties.SetVector(k_RenderingLayerId, data.RenderingLayer);

                using var submitBatchScope = k_SubmitBatchDraws.Auto();
                resources.DrawBatches(context.cmd, properties, ForwardPassIndex, ActorProceduralRenderSet.Forward);
            }
        }

        sealed class ActorProceduralShadowPass : ScriptableRenderPass
        {
            static readonly ProfilerMarker k_MainShadowPass = new("VV.ActorProcedural.MainLightShadows");
            static readonly ProfilerMarker k_AdditionalShadowPass = new("VV.ActorProcedural.AdditionalPointShadows");
            static readonly ProfilerMarker k_BindResources = new("VV.ActorProcedural.Shadow.BindResources");
            static readonly ProfilerMarker k_DrawSlices = new("VV.ActorProcedural.Shadow.DrawSlices");

            sealed class ShadowPassData
            {
                public ActorProceduralRenderResources Resources;
                public MaterialPropertyBlock Properties;
                public ActorProceduralShadowSlice[] Slices;
                public int SliceCount;
                public Camera Camera;
                public Matrix4x4 CameraView;
                public Matrix4x4 CameraProjection;
                public GlobalKeyword CastingPunctualShadowKeyword;
                public Vector4 RenderingLayer;
            }

            readonly MaterialPropertyBlock _properties = new();
            readonly ActorProceduralShadowSlice[] _mainSlices = new ActorProceduralShadowSlice[MaxMainLightShadowSlices];
            readonly ActorProceduralShadowSlice[] _additionalSlices = new ActorProceduralShadowSlice[MaxAdditionalShadowRequests];
            readonly ActorProceduralShadowAtlasRequest[] _requests = new ActorProceduralShadowAtlasRequest[MaxAdditionalShadowRequests];
            readonly ActorProceduralShadowAtlasRequest[] _sortedRequests = new ActorProceduralShadowAtlasRequest[MaxAdditionalShadowRequests];
            readonly RectInt[] _unusedAtlasAreas = new RectInt[MaxAdditionalShadowRequests];
            readonly int[] _selectedPointLights = new int[MaxAdditionalShadowRequests];

            ActorProceduralRenderResources _resources;
            bool _enableMainLightActorShadows;
            bool _enableAdditionalPointLightActorShadows;
            int _maxAdditionalPointShadowLights;
            GlobalKeyword _castingPunctualShadowKeyword;
            Vector4 _renderingLayer;

            public void Setup(
                ActorProceduralRenderResources resources,
                bool enableMainLightActorShadows,
                bool enableAdditionalPointLightActorShadows,
                int maxAdditionalPointShadowLights,
                GlobalKeyword castingPunctualShadowKeyword,
                Vector4 renderingLayer)
            {
                _resources = resources;
                _enableMainLightActorShadows = enableMainLightActorShadows;
                _enableAdditionalPointLightActorShadows = enableAdditionalPointLightActorShadows;
                _maxAdditionalPointShadowLights = Mathf.Max(0, maxAdditionalPointShadowLights);
                _castingPunctualShadowKeyword = castingPunctualShadowKeyword;
                _renderingLayer = renderingLayer;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (_resources == null || !_resources.IsReadyForShadowDraw)
                    return;

                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalLightData lightData = frameData.Get<UniversalLightData>();
                UniversalShadowData shadowData = frameData.Get<UniversalShadowData>();

                if (_enableMainLightActorShadows && resourceData.mainShadowsTexture.IsValid())
                {
                    int mainSliceCount = BuildMainLightShadowSlices(
                        renderingData,
                        lightData,
                        shadowData,
                        _mainSlices);
                    if (mainSliceCount > 0)
                    {
                        using var builder = renderGraph.AddRasterRenderPass<ShadowPassData>("VV Actor Main Light Shadows", out var passData);
                        builder.SetRenderAttachmentDepth(resourceData.mainShadowsTexture, AccessFlags.ReadWrite);
                        builder.AllowPassCulling(false);
                        builder.AllowGlobalStateModification(true);
                        FillPassData(passData, _mainSlices, mainSliceCount, cameraData);
                        builder.SetRenderFunc(static (ShadowPassData data, RasterGraphContext context) => ExecuteShadowPass(data, context, k_MainShadowPass));
                    }
                }

                if (_enableAdditionalPointLightActorShadows
                    && _maxAdditionalPointShadowLights > 0
                    && resourceData.additionalShadowsTexture.IsValid())
                {
                    int additionalSliceCount = BuildAdditionalPointShadowSlices(
                        renderingData,
                        cameraData,
                        lightData,
                        shadowData,
                        _additionalSlices);
                    if (additionalSliceCount > 0)
                    {
                        using var builder = renderGraph.AddRasterRenderPass<ShadowPassData>("VV Actor Additional Point Shadows", out var passData);
                        builder.SetRenderAttachmentDepth(resourceData.additionalShadowsTexture, AccessFlags.ReadWrite);
                        builder.AllowPassCulling(false);
                        builder.AllowGlobalStateModification(true);
                        FillPassData(passData, _additionalSlices, additionalSliceCount, cameraData);
                        builder.SetRenderFunc(static (ShadowPassData data, RasterGraphContext context) => ExecuteShadowPass(data, context, k_AdditionalShadowPass));
                    }
                }
            }

            void FillPassData(
                ShadowPassData passData,
                ActorProceduralShadowSlice[] slices,
                int sliceCount,
                UniversalCameraData cameraData)
            {
                passData.Resources = _resources;
                passData.Properties = _properties;
                passData.Slices = slices;
                passData.SliceCount = sliceCount;
                passData.Camera = cameraData.camera;
                passData.CameraView = cameraData.GetViewMatrix();
                passData.CameraProjection = cameraData.GetProjectionMatrix();
                passData.CastingPunctualShadowKeyword = _castingPunctualShadowKeyword;
                passData.RenderingLayer = _renderingLayer;
            }

            static void ExecuteShadowPass(ShadowPassData data, RasterGraphContext context, ProfilerMarker marker)
            {
                using var passScope = marker.Auto();
                var resources = data.Resources;
                if (resources == null || !resources.IsReadyForShadowDraw || data.SliceCount <= 0)
                    return;

                MaterialPropertyBlock properties = data.Properties;
                using (k_BindResources.Auto())
                {
                    resources.Bind(properties, ActorProceduralRenderSet.Shadow);
                }
                properties.SetVector(k_RenderingLayerId, data.RenderingLayer);

                RasterCommandBuffer cmd = context.cmd;
                cmd.SetGlobalDepthBias(1.0f, 2.5f);

                using (k_DrawSlices.Auto())
                {
                    for (int i = 0; i < data.SliceCount; i++)
                    {
                        ref readonly var slice = ref data.Slices[i];
                        cmd.SetKeyword(data.CastingPunctualShadowKeyword, slice.Punctual);
                        cmd.SetGlobalVector(k_ShadowBiasId, slice.ShadowBias);
                        cmd.SetGlobalVector(k_LightDirectionId, slice.LightDirection);
                        cmd.SetGlobalVector(k_LightPositionId, slice.LightPosition);
                        cmd.SetGlobalMatrix(k_ActorShadowViewProjectionId, slice.ViewProjectionMatrix);
                        cmd.SetViewport(slice.Viewport);
                        cmd.SetViewProjectionMatrices(slice.ViewMatrix, slice.ProjectionMatrix);
                        resources.DrawBatches(cmd, properties, ShadowPassIndex, ActorProceduralRenderSet.Shadow);
                    }
                }

                cmd.SetKeyword(data.CastingPunctualShadowKeyword, false);
                cmd.DisableScissorRect();
                cmd.SetGlobalDepthBias(0.0f, 0.0f);
                cmd.SetViewProjectionMatrices(data.CameraView, data.CameraProjection);
                if (data.Camera != null)
                    cmd.SetViewport(data.Camera.pixelRect);
            }

            static int BuildMainLightShadowSlices(
                UniversalRenderingData renderingData,
                UniversalLightData lightData,
                UniversalShadowData shadowData,
                ActorProceduralShadowSlice[] slices)
            {
                if (!shadowData.supportsMainLightShadows
                    || lightData.mainLightIndex < 0
                    || !lightData.visibleLights.IsCreated
                    || lightData.mainLightIndex >= lightData.visibleLights.Length)
                {
                    return 0;
                }

                int shadowLightIndex = lightData.mainLightIndex;
                VisibleLight shadowLight = lightData.visibleLights[shadowLightIndex];
                if (shadowLight.lightType != LightType.Directional || shadowLight.light == null)
                    return 0;

                int cascadeCount = Mathf.Clamp(shadowData.mainLightShadowCascadesCount, 1, MaxMainLightShadowSlices);
                int atlasWidth = Mathf.Max(1, shadowData.mainLightShadowmapWidth);
                int atlasHeight = cascadeCount == 2
                    ? Mathf.Max(1, shadowData.mainLightShadowmapHeight >> 1)
                    : Mathf.Max(1, shadowData.mainLightShadowmapHeight);
                int shadowResolution = ShadowUtils.GetMaxTileResolutionInAtlas(atlasWidth, atlasHeight, cascadeCount);
                var cullResults = renderingData.cullResults;
                int count = 0;

                for (int cascadeIndex = 0; cascadeIndex < cascadeCount && count < slices.Length; cascadeIndex++)
                {
                    if (!ShadowUtils.ExtractDirectionalLightMatrix(
                            ref cullResults,
                            shadowData,
                            shadowLightIndex,
                            cascadeIndex,
                            atlasWidth,
                            atlasHeight,
                            shadowResolution,
                            shadowLight.light.shadowNearPlane,
                            out _,
                            out ShadowSliceData sliceData))
                    {
                        continue;
                    }

                    Vector4 shadowBias = ShadowUtils.GetShadowBias(
                        ref shadowLight,
                        shadowLightIndex,
                        shadowData,
                        sliceData.projectionMatrix,
                        sliceData.resolution);
                    Vector3 lightDirection = -shadowLight.localToWorldMatrix.GetColumn(2);
                    Vector3 lightPosition = shadowLight.localToWorldMatrix.GetColumn(3);
                    slices[count++] = new ActorProceduralShadowSlice
                    {
                        ViewMatrix = sliceData.viewMatrix,
                        ProjectionMatrix = sliceData.projectionMatrix,
                        ViewProjectionMatrix = BuildShadowViewProjection(sliceData.viewMatrix, sliceData.projectionMatrix),
                        Viewport = new Rect(sliceData.offsetX, sliceData.offsetY, sliceData.resolution, sliceData.resolution),
                        ShadowBias = shadowBias,
                        LightDirection = new Vector4(lightDirection.x, lightDirection.y, lightDirection.z, 0f),
                        LightPosition = new Vector4(lightPosition.x, lightPosition.y, lightPosition.z, 1f),
                        Punctual = false,
                    };
                }

                return count;
            }

            int BuildAdditionalPointShadowSlices(
                UniversalRenderingData renderingData,
                UniversalCameraData cameraData,
                UniversalLightData lightData,
                UniversalShadowData shadowData,
                ActorProceduralShadowSlice[] slices)
            {
                if (!shadowData.supportsAdditionalLightShadows
                    || !lightData.visibleLights.IsCreated
                    || shadowData.resolution == null)
                {
                    return 0;
                }

                int requestCount = BuildAdditionalShadowRequests(cameraData, lightData, shadowData);
                if (requestCount <= 0)
                    return 0;

                SortAdditionalShadowRequests(requestCount);
                int atlasSize = Mathf.Max(1, shadowData.additionalLightsShadowmapWidth);
                int packedCount = PackAdditionalShadowRequests(requestCount, atlasSize);
                if (packedCount <= 0)
                    return 0;

                var cullResults = renderingData.cullResults;
                int selectedPointCount = 0;
                int sliceCount = 0;
                for (int requestIndex = 0; requestIndex < packedCount && sliceCount < slices.Length; requestIndex++)
                {
                    var request = _sortedRequests[requestIndex];
                    if (!request.PointLight)
                        continue;

                    if (!IsSelectedPointLight(request.VisibleLightIndex, selectedPointCount))
                    {
                        if (selectedPointCount >= _maxAdditionalPointShadowLights)
                            continue;

                        _selectedPointLights[selectedPointCount++] = request.VisibleLightIndex;
                    }

                    VisibleLight shadowLight = lightData.visibleLights[request.VisibleLightIndex];
                    if (shadowLight.light == null)
                        continue;

                    bool softShadow = shadowLight.light.shadows == LightShadows.Soft;
                    float fovBias = GetPointLightShadowFrustumFovBiasInDegrees(
                        request.AllocatedResolution,
                        softShadow);
                    if (!ShadowUtils.ExtractPointLightMatrix(
                            ref cullResults,
                            shadowData,
                            request.VisibleLightIndex,
                            (CubemapFace)request.PerLightSliceIndex,
                            fovBias,
                            out _,
                            out Matrix4x4 viewMatrix,
                            out Matrix4x4 projectionMatrix,
                            out _))
                    {
                        continue;
                    }

                    Vector4 shadowBias = ShadowUtils.GetShadowBias(
                        ref shadowLight,
                        request.VisibleLightIndex,
                        shadowData,
                        projectionMatrix,
                        request.AllocatedResolution);
                    Vector3 lightDirection = -shadowLight.localToWorldMatrix.GetColumn(2);
                    Vector3 lightPosition = shadowLight.localToWorldMatrix.GetColumn(3);
                    slices[sliceCount++] = new ActorProceduralShadowSlice
                    {
                        ViewMatrix = viewMatrix,
                        ProjectionMatrix = projectionMatrix,
                        ViewProjectionMatrix = BuildShadowViewProjection(viewMatrix, projectionMatrix),
                        Viewport = new Rect(request.OffsetX, request.OffsetY, request.AllocatedResolution, request.AllocatedResolution),
                        ShadowBias = shadowBias,
                        LightDirection = new Vector4(lightDirection.x, lightDirection.y, lightDirection.z, 0f),
                        LightPosition = new Vector4(lightPosition.x, lightPosition.y, lightPosition.z, 1f),
                        Punctual = true,
                    };
                }

                return sliceCount;
            }

            int BuildAdditionalShadowRequests(
                UniversalCameraData cameraData,
                UniversalLightData lightData,
                UniversalShadowData shadowData)
            {
                int requestCount = 0;
                for (int visibleLightIndex = 0; visibleLightIndex < lightData.visibleLights.Length; visibleLightIndex++)
                {
                    if (visibleLightIndex == lightData.mainLightIndex)
                        continue;

                    VisibleLight visibleLight = lightData.visibleLights[visibleLightIndex];
                    Light light = visibleLight.light;
                    if (light == null
                        || light.shadows == LightShadows.None
                        || light.shadowStrength <= 0f
                        || visibleLight.lightType == LightType.Directional
                        || (uint)visibleLightIndex >= (uint)shadowData.resolution.Count)
                    {
                        continue;
                    }

                    int sliceCount = visibleLight.lightType == LightType.Point ? 6 : visibleLight.lightType == LightType.Spot ? 1 : 0;
                    if (sliceCount <= 0)
                        continue;

                    int requestedResolution = shadowData.resolution[visibleLightIndex];
                    if (requestedResolution <= 0)
                        continue;

                    bool pointLight = visibleLight.lightType == LightType.Point;
                    bool softShadow = light.shadows == LightShadows.Soft;
                    float distanceSq = (cameraData.worldSpaceCameraPos - light.transform.position).sqrMagnitude;
                    for (int sliceIndex = 0; sliceIndex < sliceCount && requestCount < _requests.Length; sliceIndex++)
                    {
                        _requests[requestCount++] = new ActorProceduralShadowAtlasRequest
                        {
                            VisibleLightIndex = visibleLightIndex,
                            PerLightSliceIndex = sliceIndex,
                            RequestedResolution = requestedResolution,
                            SoftShadow = softShadow,
                            PointLight = pointLight,
                            DistanceSq = distanceSq,
                        };
                    }
                }

                return requestCount;
            }

            void SortAdditionalShadowRequests(int requestCount)
            {
                for (int i = 0; i < requestCount; i++)
                    _sortedRequests[i] = _requests[i];

                for (int i = 1; i < requestCount; i++)
                {
                    var value = _sortedRequests[i];
                    int j = i - 1;
                    while (j >= 0 && CompareAdditionalShadowRequests(value, _sortedRequests[j]) < 0)
                    {
                        _sortedRequests[j + 1] = _sortedRequests[j];
                        j--;
                    }
                    _sortedRequests[j + 1] = value;
                }
            }

            int PackAdditionalShadowRequests(int requestCount, int atlasSize)
            {
                int totalSlices = Mathf.Min(requestCount, UniversalRenderPipeline.maxVisibleAdditionalLights);
                int estimatedScaleFactor = 1;
                bool allShadowSlicesHaveEnoughResolution = false;
                while (!allShadowSlicesHaveEnoughResolution && totalSlices > 0)
                {
                    var request = _sortedRequests[totalSlices - 1];
                    estimatedScaleFactor = EstimateScaleFactorNeededToFitAllShadows(totalSlices, atlasSize);
                    if (request.RequestedResolution >= estimatedScaleFactor * MinimalPunctualLightShadowResolution(request.SoftShadow))
                        allShadowSlicesHaveEnoughResolution = true;
                    else
                        totalSlices -= request.PointLight ? 6 : 1;
                }

                bool allShadowSlicesFit = false;
                bool tooManyShadows = false;
                int shadowSlicesScaleFactor = estimatedScaleFactor;
                while (!allShadowSlicesFit && !tooManyShadows)
                {
                    int unusedAreaCount = 1;
                    _unusedAtlasAreas[0] = new RectInt(0, 0, atlasSize, atlasSize);
                    allShadowSlicesFit = true;

                    for (int requestIndex = 0; requestIndex < totalSlices; requestIndex++)
                    {
                        int resolution = _sortedRequests[requestIndex].RequestedResolution / shadowSlicesScaleFactor;
                        if (resolution < MinimalPunctualLightShadowResolution(_sortedRequests[requestIndex].SoftShadow))
                        {
                            tooManyShadows = true;
                            break;
                        }

                        bool foundSpace = false;
                        for (int areaIndex = 0; areaIndex < unusedAreaCount; areaIndex++)
                        {
                            RectInt area = _unusedAtlasAreas[areaIndex];
                            if (area.width < resolution || area.height < resolution)
                                continue;

                            var request = _sortedRequests[requestIndex];
                            request.OffsetX = area.x;
                            request.OffsetY = area.y;
                            request.AllocatedResolution = resolution;
                            _sortedRequests[requestIndex] = request;

                            RemoveUnusedAreaAt(areaIndex, ref unusedAreaCount);

                            int remainingRequests = totalSlices - requestIndex - 1;
                            int newAreaCount = 0;
                            int newAreaX = area.x;
                            int newAreaY = area.y;
                            while (newAreaCount < remainingRequests && unusedAreaCount < _unusedAtlasAreas.Length)
                            {
                                newAreaX += resolution;
                                if (newAreaX + resolution > area.x + area.width)
                                {
                                    newAreaX = area.x;
                                    newAreaY += resolution;
                                    if (newAreaY + resolution > area.y + area.height)
                                        break;
                                }

                                InsertUnusedAreaAt(
                                    areaIndex + newAreaCount,
                                    new RectInt(newAreaX, newAreaY, resolution, resolution),
                                    ref unusedAreaCount);
                                newAreaCount++;
                            }

                            foundSpace = true;
                            break;
                        }

                        if (!foundSpace)
                        {
                            allShadowSlicesFit = false;
                            break;
                        }
                    }

                    if (!allShadowSlicesFit && !tooManyShadows)
                        shadowSlicesScaleFactor *= 2;
                }

                return tooManyShadows ? 0 : totalSlices;
            }

            void RemoveUnusedAreaAt(int index, ref int count)
            {
                for (int i = index; i < count - 1; i++)
                    _unusedAtlasAreas[i] = _unusedAtlasAreas[i + 1];
                count--;
            }

            void InsertUnusedAreaAt(int index, RectInt area, ref int count)
            {
                if (count >= _unusedAtlasAreas.Length)
                    return;

                index = Mathf.Clamp(index, 0, count);
                for (int i = count; i > index; i--)
                    _unusedAtlasAreas[i] = _unusedAtlasAreas[i - 1];
                _unusedAtlasAreas[index] = area;
                count++;
            }

            int EstimateScaleFactorNeededToFitAllShadows(int endIndex, int atlasSize)
            {
                long atlasTexels = (long)atlasSize * atlasSize;
                long requestTexels = 0;
                for (int i = 0; i < endIndex; i++)
                    requestTexels += (long)_sortedRequests[i].RequestedResolution * _sortedRequests[i].RequestedResolution;

                int scaleFactor = 1;
                while (requestTexels > atlasTexels * scaleFactor * scaleFactor)
                    scaleFactor *= 2;
                return scaleFactor;
            }

            bool IsSelectedPointLight(int visibleLightIndex, int selectedPointCount)
            {
                for (int i = 0; i < selectedPointCount; i++)
                    if (_selectedPointLights[i] == visibleLightIndex)
                        return true;
                return false;
            }

            static int CompareAdditionalShadowRequests(
                ActorProceduralShadowAtlasRequest current,
                ActorProceduralShadowAtlasRequest other)
            {
                if (current.RequestedResolution != other.RequestedResolution)
                    return current.RequestedResolution > other.RequestedResolution ? -1 : 1;
                if (current.SoftShadow != other.SoftShadow)
                    return !current.SoftShadow && other.SoftShadow ? -1 : 1;
                int distanceCompare = current.DistanceSq.CompareTo(other.DistanceSq);
                if (distanceCompare != 0)
                    return distanceCompare;
                if (current.VisibleLightIndex != other.VisibleLightIndex)
                    return current.VisibleLightIndex.CompareTo(other.VisibleLightIndex);
                return current.PerLightSliceIndex.CompareTo(other.PerLightSliceIndex);
            }

            static int MinimalPunctualLightShadowResolution(bool softShadow) => softShadow ? 16 : 8;

            static Matrix4x4 BuildShadowViewProjection(Matrix4x4 viewMatrix, Matrix4x4 projectionMatrix)
            {
                return GL.GetGPUProjectionMatrix(projectionMatrix, true) * viewMatrix;
            }

            static float GetPointLightShadowFrustumFovBiasInDegrees(int shadowSliceResolution, bool shadowFiltering)
            {
                float fovBias = 4.00f;
                if (shadowSliceResolution <= 16)
                    fovBias = 43.0f;
                else if (shadowSliceResolution <= 32)
                    fovBias = 18.55f;
                else if (shadowSliceResolution <= 64)
                    fovBias = 8.63f;
                else if (shadowSliceResolution <= 128)
                    fovBias = 4.13f;
                else if (shadowSliceResolution <= 256)
                    fovBias = 2.03f;
                else if (shadowSliceResolution <= 512)
                    fovBias = 1.00f;
                else if (shadowSliceResolution <= 1024)
                    fovBias = 0.50f;
                else if (shadowSliceResolution <= 2048)
                    fovBias = 0.25f;

                if (shadowFiltering)
                {
                    if (shadowSliceResolution <= 32)
                        fovBias += 9.35f;
                    else if (shadowSliceResolution <= 64)
                        fovBias += 4.07f;
                    else if (shadowSliceResolution <= 128)
                        fovBias += 1.77f;
                    else if (shadowSliceResolution <= 256)
                        fovBias += 0.85f;
                    else if (shadowSliceResolution <= 512)
                        fovBias += 0.39f;
                    else if (shadowSliceResolution <= 1024)
                        fovBias += 0.17f;
                    else if (shadowSliceResolution <= 2048)
                        fovBias += 0.074f;
                }

                return fovBias;
            }
        }

        struct ActorProceduralShadowSlice
        {
            public Matrix4x4 ViewMatrix;
            public Matrix4x4 ProjectionMatrix;
            public Matrix4x4 ViewProjectionMatrix;
            public Rect Viewport;
            public Vector4 ShadowBias;
            public Vector4 LightDirection;
            public Vector4 LightPosition;
            public bool Punctual;
        }

        struct ActorProceduralShadowAtlasRequest
        {
            public int VisibleLightIndex;
            public int PerLightSliceIndex;
            public int RequestedResolution;
            public int OffsetX;
            public int OffsetY;
            public int AllocatedResolution;
            public float DistanceSq;
            public bool SoftShadow;
            public bool PointLight;
        }
    }
}
