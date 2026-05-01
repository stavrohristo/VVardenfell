using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Interactions
{
    [UpdateInGroup(typeof(MorrowindPhysicsPreBuildSystemGroup))]
    [UpdateBefore(typeof(InteractionActivationProxySystem))]
    public partial class InteractionActivationProxyFollowSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            EntityManager.CompleteDependencyBeforeRO<LocalToWorld>();

            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (parent, transform, localToWorld, entity) in
                     SystemAPI.Query<RefRO<LogicalRefParent>, RefRW<LocalTransform>, RefRW<LocalToWorld>>()
                         .WithAll<InteractionActivationProxyTag>()
                         .WithEntityAccess())
            {
                Entity target = parent.ValueRO.Value;
                if (target == Entity.Null
                    || !EntityManager.Exists(target)
                    || !EntityManager.HasComponent<PassiveActorPresence>(target))
                {
                    continue;
                }

                if (!InteractionProxyBoundsUtility.TryBuildAggregateWorldBounds(EntityManager, target, out AABB worldBounds))
                    continue;

                transform.ValueRW = LocalTransform.FromPositionRotationScale(worldBounds.Center, quaternion.identity, 1f);
                localToWorld.ValueRW = new LocalToWorld
                {
                    Value = float4x4.TRS(worldBounds.Center, quaternion.identity, new float3(1f)),
                };

                if (EntityManager.HasComponent<Unity.Transforms.Static>(entity))
                    ecb.RemoveComponent<Unity.Transforms.Static>(entity);
            }

            ecb.Playback(EntityManager);
            ecb.Dispose();
        }
    }
}
