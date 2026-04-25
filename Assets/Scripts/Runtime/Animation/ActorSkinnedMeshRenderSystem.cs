using Unity.Burst;
using Unity.Entities;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Animation
{
    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    [UpdateAfter(typeof(ActorRootMotionSystem))]
    public partial struct ActorSkinnedMeshRenderSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ActorAnimationBlobCatalog>();
            state.RequireForUpdate<ActorSkinMesh>();
            state.RequireForUpdate<ActorBone>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var catalog = SystemAPI.GetSingleton<ActorAnimationBlobCatalog>().Blob;
            if (!catalog.IsCreated)
                return;

            state.Dependency = new PackProceduralActorDrawsJob
            {
                Catalog = catalog,
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        partial struct PackProceduralActorDrawsJob : IJobEntity
        {
            [Unity.Collections.ReadOnly]
            public BlobAssetReference<ActorAnimationCatalogBlob> Catalog;

            void Execute(
                ref ActorProceduralRenderState renderState,
                DynamicBuffer<ActorSkinMesh> skinMeshes,
                DynamicBuffer<ActorBone> bones,
                DynamicBuffer<ActorProceduralDraw> draws)
            {
                draws.Clear();
                if (!Catalog.IsCreated || skinMeshes.Length == 0 || bones.Length == 0)
                {
                    renderState.BoneMatrixCount = 0;
                    renderState.DrawCount = 0;
                    renderState.Version++;
                    return;
                }

                ref var catalog = ref Catalog.Value;
                for (int i = 0; i < skinMeshes.Length; i++)
                {
                    var skinMeshRef = skinMeshes[i];
                    if ((uint)skinMeshRef.SkinMeshIndex >= (uint)catalog.SkinMeshes.Length)
                        continue;

                    var skinMesh = catalog.SkinMeshes[skinMeshRef.SkinMeshIndex];
                    draws.Add(new ActorProceduralDraw
                    {
                        SkinMeshIndex = skinMeshRef.SkinMeshIndex,
                        MaterialIndex = skinMesh.MaterialIndex,
                        TextureIndex = skinMesh.TextureIndex,
                        BoneMatrixOffset = 0,
                        BoneMatrixCount = skinMesh.SkinBoneCount,
                        DrawIndexCount = skinMesh.IndexCount,
                        DrawVertexCount = skinMesh.VertexCount,
                        AttachBoneIndex = skinMeshRef.AttachBoneIndex,
                        RigidMirrorX = skinMeshRef.RigidMirrorX,
                    });
                }

                renderState.BoneMatrixCount = bones.Length;
                renderState.DrawCount = draws.Length;
                renderState.Version++;
            }
        }
    }
}
