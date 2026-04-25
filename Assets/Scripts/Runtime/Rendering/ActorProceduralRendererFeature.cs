using UnityEngine;
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
            sealed class PassData
            {
                public ActorProceduralRenderResources Resources;
                public MaterialPropertyBlock Properties;
                public ActorProceduralRenderPass Owner;
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
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.ReadWrite);
                builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);

                passData.Resources = _resources;
                passData.Properties = _properties;
                passData.Owner = this;
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) => ExecutePass(data, context));
            }

            static void ExecutePass(PassData data, RasterGraphContext context)
            {
                var resources = data.Resources;
                if (resources == null || !resources.IsReadyForDraw)
                    return;

                MaterialPropertyBlock properties = data.Properties;
                resources.Bind(properties);

                for (int i = 0; i < resources.Batches.Length; i++)
                {
                    var batch = resources.Batches[i];
                    Material material = resources.GetMaterial(batch.BucketIndex, batch.MaterialIndex);
                    if (material == null || batch.DrawCount <= 0 || batch.IndexCount <= 0)
                    {
                        data.Owner.LogSkippedBatch(i, batch.BucketIndex, batch.MaterialIndex, batch.DrawCount, batch.IndexCount, material == null);
                        continue;
                    }

                    int drawEnd = batch.DrawBase + batch.DrawCount;
                    for (int drawIndex = batch.DrawBase; drawIndex < drawEnd; drawIndex++)
                    {
                        if ((uint)drawIndex >= (uint)resources.Draws.Length)
                            continue;

                        var draw = resources.Draws[drawIndex];
                        if (draw.IndexCount <= 0)
                            continue;

                        properties.SetInt(ActorProceduralRenderResources.DrawIndexId, drawIndex);
                        context.cmd.DrawProcedural(
                            Matrix4x4.identity,
                            material,
                            0,
                            MeshTopology.Triangles,
                            draw.IndexCount,
                            1,
                            properties);
                    }
                }
            }

            void LogSkippedBatch(int batchIndex, int bucketIndex, int materialIndex, int drawCount, int indexCount, bool missingMaterial)
            {
                if (_loggedSkippedBatch)
                    return;

                _loggedSkippedBatch = true;
                Debug.LogWarning($"[VVardenfell] actor procedural skipped batch {batchIndex}: bucket={bucketIndex} material={materialIndex} draws={drawCount} indices={indexCount} missingMaterial={missingMaterial}");
            }
        }
    }
}
