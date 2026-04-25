using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Rendering
{
    [UpdateInGroup(typeof(MorrowindPresentationSystemGroup))]
    public partial class ActorProceduralRenderUploadSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<ActorAnimationBlobCatalog>();
            RequireForUpdate<ActorSkinMesh>();
        }

        protected override void OnUpdate()
        {
            var catalogRef = SystemAPI.GetSingleton<ActorAnimationBlobCatalog>().Blob;
            if (!catalogRef.IsCreated)
                return;

            var resources = WorldResources.ActorProceduralRenderer;
            if (resources == null)
            {
                resources = new ActorProceduralRenderResources();
                WorldResources.ActorProceduralRenderer = resources;
            }

            Dependency.Complete();
            ref var catalog = ref catalogRef.Value;
            resources.EnsureStaticResources(ref catalog);
            resources.BeginFrame();

            foreach (var (renderState, localToWorld, bones, skinMeshes) in
                     SystemAPI.Query<RefRW<ActorProceduralRenderState>, RefRO<LocalToWorld>, DynamicBuffer<ActorBone>, DynamicBuffer<ActorSkinMesh>>())
            {
                if (bones.Length == 0 || skinMeshes.Length == 0)
                {
                    renderState.ValueRW.BoneMatrixCount = 0;
                    renderState.ValueRW.DrawCount = 0;
                    continue;
                }

                int boneOffset = resources.BoneMatrices.Length;
                int drawStart = resources.Draws.Length;
                float4x4 actorLocalToWorld = localToWorld.ValueRO.Value;

                for (int i = 0; i < skinMeshes.Length; i++)
                {
                    var skinMeshRef = skinMeshes[i];
                    if ((uint)skinMeshRef.SkinMeshIndex >= (uint)catalog.SkinMeshes.Length)
                        continue;

                    var skinMesh = catalog.SkinMeshes[skinMeshRef.SkinMeshIndex];
                    resources.AddDraw(
                        ref catalog,
                        new ActorProceduralDraw
                        {
                            SkinMeshIndex = skinMeshRef.SkinMeshIndex,
                            MaterialIndex = skinMesh.MaterialIndex,
                            TextureIndex = skinMesh.TextureIndex,
                            BoneMatrixCount = skinMesh.SkinBoneCount,
                            DrawIndexCount = skinMesh.IndexCount,
                            DrawVertexCount = skinMesh.VertexCount,
                            AttachBoneIndex = skinMeshRef.AttachBoneIndex,
                            RigidMirrorX = skinMeshRef.RigidMirrorX,
                        },
                        bones,
                        actorLocalToWorld);
                }

                renderState.ValueRW.BoneMatrixOffset = boneOffset;
                renderState.ValueRW.BoneMatrixCount = resources.BoneMatrices.Length - boneOffset;
                renderState.ValueRW.DrawStart = drawStart;
                renderState.ValueRW.DrawCount = resources.Draws.Length - drawStart;
            }

            resources.UploadFrame();
        }

        protected override void OnDestroy()
        {
            WorldResources.ActorProceduralRenderer?.Dispose();
            WorldResources.ActorProceduralRenderer = null;
        }
    }
}
