using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Interactions
{
    [BurstCompile]
    [UpdateInGroup(typeof(MorrowindPhysicsPreBuildSystemGroup))]
    [UpdateBefore(typeof(InteractionActivationProxySystem))]
    public partial struct InteractionActivationProxyFollowSystem : ISystem
    {
        EntityQuery _proxyQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _proxyQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<InteractionActivationProxyTag>(),
                ComponentType.ReadOnly<InteractionActivationProxyFollowTag>(),
                ComponentType.ReadOnly<LogicalRefParent>(),
                ComponentType.ReadWrite<LocalTransform>(),
                ComponentType.ReadWrite<LocalToWorld>());
            systemState.RequireForUpdate(_proxyQuery);
        }
        [BurstCompile]
        public void OnUpdate(ref SystemState systemState)
        {
            systemState.EntityManager.CompleteDependencyBeforeRO<LocalToWorld>();

            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (parent, transform, localToWorld, entity) in
                     SystemAPI.Query<RefRO<LogicalRefParent>, RefRW<LocalTransform>, RefRW<LocalToWorld>>()
                         .WithAll<InteractionActivationProxyTag, InteractionActivationProxyFollowTag>()
                         .WithEntityAccess())
            {
                Entity target = parent.ValueRO.Value;
                if (target == Entity.Null
                    || !systemState.EntityManager.Exists(target)
                    || !systemState.EntityManager.HasComponent<PassiveActorPresence>(target))
                {
                    continue;
                }

                if (!InteractionProxyBoundsUtility.TryBuildAggregateWorldBounds(systemState.EntityManager, target, out AABB worldBounds))
                    continue;

                transform.ValueRW = LocalTransform.FromPositionRotationScale(worldBounds.Center, quaternion.identity, 1f);
                localToWorld.ValueRW = new LocalToWorld
                {
                    Value = float4x4.TRS(worldBounds.Center, quaternion.identity, new float3(1f)),
                };

                if (systemState.EntityManager.HasComponent<Unity.Transforms.Static>(entity))
                    ecb.RemoveComponent<Unity.Transforms.Static>(entity);
            }

            ecb.Playback(systemState.EntityManager);
            ecb.Dispose();
        }
    }
}
