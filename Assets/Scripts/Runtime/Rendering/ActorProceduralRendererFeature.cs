using UnityEngine;
using Unity.Profiling;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using VVardenfell.Runtime.Streaming;

namespace VVardenfell.Runtime.Rendering
{
    public sealed class ActorProceduralRendererFeature : ScriptableRendererFeature
    {
        [SerializeField] RenderPassEvent _renderPassEvent = RenderPassEvent.AfterRenderingOpaques;

        ActorProceduralRenderPass _pass;

        public override void Create()
        {
            _pass = new ActorProceduralRenderPass
            {
                renderPassEvent = _renderPassEvent,
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var resources = WorldResources.ActorProceduralRenderer;
            if (resources == null)
                return;

            if (!resources.IsReadyForDraw)
                return;

            _pass.Setup(resources);
            renderer.EnqueuePass(_pass);
        }

        sealed class ActorProceduralRenderPass : ScriptableRenderPass
        {
            static readonly ProfilerMarker k_ExecutePass = new("VV.ActorProcedural.ExecutePass");
            static readonly ProfilerMarker k_BindResources = new("VV.ActorProcedural.BindResources");
            static readonly ProfilerMarker k_IterateBatches = new("VV.ActorProcedural.IterateBatches");
            static readonly ProfilerMarker k_SubmitBatchDraws = new("VV.ActorProcedural.SubmitBatchDraws");

            sealed class PassData
            {
                public ActorProceduralRenderResources Resources;
                public MaterialPropertyBlock Properties;
                public ActorProceduralRenderPass Owner;
                public Vector4 MainLightDirection;
                public Vector4 MainLightColor;
                public float MainLightValid;
            }

            readonly MaterialPropertyBlock _properties = new();
            ActorProceduralRenderResources _resources;
            bool _loggedSkippedBatch;

            public void Setup(ActorProceduralRenderResources resources)
            {
                _resources = resources;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (_resources == null || !_resources.IsReadyForDraw)
                    return;

                using var builder = renderGraph.AddRasterRenderPass<PassData>("VV Actor Procedural Render", out var passData);
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                UniversalLightData lightData = frameData.Get<UniversalLightData>();
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.ReadWrite);
                builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);

                passData.Resources = _resources;
                passData.Properties = _properties;
                passData.Owner = this;
                ResolveMainLight(lightData, out passData.MainLightDirection, out passData.MainLightColor, out passData.MainLightValid);
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
                    resources.Bind(properties);
                }
                properties.SetFloat(ActorProceduralRenderResources.MainLightValidId, data.MainLightValid);
                properties.SetVector(ActorProceduralRenderResources.MainLightDirectionId, data.MainLightDirection);
                properties.SetVector(ActorProceduralRenderResources.MainLightColorId, data.MainLightColor);

                using var iterateScope = k_IterateBatches.Auto();
                for (int i = 0; i < resources.Batches.Length; i++)
                {
                    var batch = resources.Batches[i];
                    Material material = resources.GetMaterial(batch.BucketIndex, batch.MaterialIndex);
                    if (material == null || batch.DrawCount <= 0 || batch.IndexCount <= 0)
                    {
                        data.Owner.LogSkippedBatch(i, batch.BucketIndex, batch.MaterialIndex, batch.DrawCount, batch.IndexCount, material == null);
                        continue;
                    }

                    using var submitBatchScope = k_SubmitBatchDraws.Auto();
                    properties.SetInt(ActorProceduralRenderResources.DrawBaseId, batch.DrawBase);
                    context.cmd.DrawProceduralIndirect(
                        Matrix4x4.identity,
                        material,
                        0,
                        MeshTopology.Triangles,
                        resources.IndirectArgsBuffer,
                        resources.GetIndirectArgsOffsetBytes(i),
                        properties);
                }
            }

            void LogSkippedBatch(int batchIndex, int bucketIndex, int materialIndex, int drawCount, int indexCount, bool missingMaterial)
            {
                if (_loggedSkippedBatch)
                    return;

                _loggedSkippedBatch = true;
                Debug.LogWarning($"[VVardenfell] actor procedural skipped batch {batchIndex}: bucket={bucketIndex} material={materialIndex} draws={drawCount} indices={indexCount} missingMaterial={missingMaterial}");
            }

            static void ResolveMainLight(
                UniversalLightData lightData,
                out Vector4 direction,
                out Vector4 color,
                out float valid)
            {
                direction = Vector4.zero;
                color = Vector4.zero;
                valid = 0f;

                int mainLightIndex = lightData.mainLightIndex;
                if (mainLightIndex < 0
                    || !lightData.visibleLights.IsCreated
                    || mainLightIndex >= lightData.visibleLights.Length)
                {
                    return;
                }

                VisibleLight mainLight = lightData.visibleLights[mainLightIndex];
                if (mainLight.lightType != LightType.Directional)
                    return;

                Vector4 lightForward = mainLight.localToWorldMatrix.GetColumn(2);
                direction = new Vector4(-lightForward.x, -lightForward.y, -lightForward.z, 0f);
                color = mainLight.finalColor;
                valid = 1f;
            }
        }
    }
}
