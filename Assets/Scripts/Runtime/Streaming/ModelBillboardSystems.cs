using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.Streaming
{
    [UpdateInGroup(typeof(MorrowindPresentationSystemGroup))]
    public partial class ModelBillboardSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            var cam = Camera.main;
            if (cam == null)
                return;

            float3 cameraPosition = cam.transform.position;
            var entityManager = EntityManager;
            entityManager.CompleteDependencyBeforeRO<LocalToWorld>();

            foreach (var (localTransform, localToWorld, billboardState, entity) in
                     SystemAPI.Query<RefRW<LocalTransform>, RefRO<LocalToWorld>, RefRO<ModelBillboardState>>()
                         .WithAll<ModelBillboardTag>()
                         .WithEntityAccess())
            {
                float3 worldPosition = localToWorld.ValueRO.Position;
                float3 forward = math.normalizesafe(cameraPosition - worldPosition, new float3(0f, 0f, 1f));
                quaternion desiredWorldRotation = quaternion.LookRotationSafe(forward, math.up());

                quaternion localFacing = desiredWorldRotation;
                if (entityManager.HasComponent<Parent>(entity))
                {
                    Entity parent = entityManager.GetComponentData<Parent>(entity).Value;
                    if (entityManager.HasComponent<LocalToWorld>(parent))
                    {
                        quaternion parentWorldRotation = ExtractRotation(entityManager.GetComponentData<LocalToWorld>(parent).Value);
                        localFacing = math.mul(math.inverse(parentWorldRotation), desiredWorldRotation);
                    }
                }

                localTransform.ValueRW = LocalTransform.FromPositionRotationScale(
                    localTransform.ValueRO.Position,
                    math.mul(localFacing, billboardState.ValueRO.BaseLocalRotation),
                    localTransform.ValueRO.Scale);
            }
        }

        private static quaternion ExtractRotation(float4x4 matrix)
        {
            float3 forward = math.normalizesafe(matrix.c2.xyz, new float3(0f, 0f, 1f));
            float3 up = math.normalizesafe(matrix.c1.xyz, math.up());
            return quaternion.LookRotationSafe(forward, up);
        }
    }
}
