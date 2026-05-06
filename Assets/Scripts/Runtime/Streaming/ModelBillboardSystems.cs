using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Rendering;

namespace VVardenfell.Runtime.Streaming
{
    [UpdateInGroup(typeof(MorrowindPresentationSystemGroup))]
    public partial struct ModelBillboardSystem : ISystem
    {
        EntityQuery _billboardQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _billboardQuery = systemState.GetEntityQuery(
                ComponentType.ReadWrite<LocalTransform>(),
                ComponentType.ReadOnly<LocalToWorld>(),
                ComponentType.ReadOnly<ModelBillboardState>(),
                ComponentType.ReadOnly<ModelBillboardTag>());

            systemState.RequireForUpdate<MainCameraSingleton>();
            systemState.RequireForUpdate(_billboardQuery);
        }

        public void OnUpdate(ref SystemState systemState)
        {
            var cam = SystemAPI.GetSingleton<MainCameraSingleton>().GetRequiredCamera();
            float3 cameraPosition = cam.transform.position;
            var entityManager = systemState.EntityManager;
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
