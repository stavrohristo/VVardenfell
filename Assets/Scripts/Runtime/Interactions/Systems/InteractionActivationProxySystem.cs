using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Physics;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;
using BoxCollider = Unity.Physics.BoxCollider;
using Collider = Unity.Physics.Collider;

namespace VVardenfell.Runtime.Interactions
{
    [UpdateInGroup(typeof(MorrowindPhysicsPreBuildSystemGroup))]
    public partial class InteractionActivationProxySystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<LoadedCellsMap>();
        }

        protected override void OnUpdate()
        {
            EntityManager.CompleteDependencyBeforeRO<LocalToWorld>();
            var loaded = SystemAPI.GetSingleton<LoadedCellsMap>();
            var pendingQuery = GetEntityQuery(
                ComponentType.ReadOnly<InteractionActivationProxyBuildPending>(),
                ComponentType.ReadOnly<LogicalRefTag>(),
                ComponentType.ReadOnly<PlacedRefIdentity>());
            using (var pending = pendingQuery.ToEntityArray(Allocator.Temp))
            {
                var ecb = new EntityCommandBuffer(Allocator.Temp);
                for (int i = 0; i < pending.Length; i++)
                {
                    var logicalEntity = pending[i];
                    if (!EntityManager.Exists(logicalEntity))
                        continue;

                    if (InteractionActivationProxyBuildUtility.HasLiveProxy(EntityManager, logicalEntity))
                    {
                        InteractionActivationProxyBuildUtility.QueuePendingCleared(EntityManager, ref ecb, logicalEntity);
                        continue;
                    }

                    bool exteriorActive = true;
                    if (EntityManager.HasComponent<CellLink>(logicalEntity))
                        exteriorActive = loaded.Active.Contains(EntityManager.GetComponentData<CellLink>(logicalEntity).Value);

                    if (!exteriorActive)
                        continue;

                    if (!InteractionTargetResolver.TryResolveSupportedKind(EntityManager, logicalEntity, out _))
                    {
                        InteractionActivationProxyBuildUtility.QueuePendingCleared(EntityManager, ref ecb, logicalEntity);
                        continue;
                    }

                    if (!InteractionProxyBoundsUtility.TryBuildAggregateWorldBounds(EntityManager, logicalEntity, out AABB worldBounds))
                    {
                        InteractionActivationProxyBuildUtility.QueuePendingCleared(EntityManager, ref ecb, logicalEntity);
                        continue;
                    }

                    bool followsActor = EntityManager.HasComponent<PassiveActorPresence>(logicalEntity);
                    Entity proxyEntity = QueueCreateProxyEntity(ref ecb, logicalEntity, worldBounds, exteriorActive, followsActor);
                    if (EntityManager.HasComponent<InteractionActivationProxyState>(logicalEntity))
                    {
                        ecb.SetComponent(logicalEntity, new InteractionActivationProxyState
                        {
                            ProxyEntity = proxyEntity,
                        });
                    }
                    else
                    {
                        ecb.AddComponent(logicalEntity, new InteractionActivationProxyState
                        {
                            ProxyEntity = proxyEntity,
                        });
                    }

                    if (EntityManager.HasBuffer<LogicalRefChild>(logicalEntity))
                        ecb.AppendToBuffer(logicalEntity, new LogicalRefChild { Value = proxyEntity });

                    InteractionActivationProxyBuildUtility.QueuePendingCleared(EntityManager, ref ecb, logicalEntity);
                }

                ecb.Playback(EntityManager);
                ecb.Dispose();
            }
        }

        Entity QueueCreateProxyEntity(ref EntityCommandBuffer ecb, Entity logicalEntity, in AABB worldBounds, bool exteriorActive, bool followsActor)
        {
            float3 size = math.max(worldBounds.Extents * 2f, new float3(0.16f));
            BlobAssetReference<Collider> collider = BoxCollider.Create(
                new BoxGeometry
                {
                    Center = float3.zero,
                    Orientation = quaternion.identity,
                    Size = size,
                    BevelRadius = 0f,
                },
                InteractionCollisionLayers.ActivationProxyFilter);

            Entity proxyEntity = ecb.CreateEntity();
            ecb.SetName(proxyEntity, new FixedString64Bytes("InteractionActivationProxy"));
            ecb.AddComponent(proxyEntity, LocalTransform.FromPositionRotationScale(worldBounds.Center, quaternion.identity, 1f));
            ecb.AddComponent(proxyEntity, new LocalToWorld
            {
                Value = float4x4.TRS(worldBounds.Center, quaternion.identity, new float3(1f)),
            });
            RuntimeColliderAttachmentUtility.QueueAttachNewSource(
                ref ecb,
                proxyEntity,
                collider,
                RuntimeColliderKind.ActivationProxy,
                active: exteriorActive || !EntityManager.HasComponent<CellLink>(logicalEntity),
                temporary: true);
            ecb.AddComponent(proxyEntity, new LogicalRefParent { Value = logicalEntity });
            ecb.AddComponent<InteractionActivationProxyTag>(proxyEntity);
            if (!followsActor)
                ecb.AddComponent<Unity.Transforms.Static>(proxyEntity);

            if (EntityManager.HasComponent<CellLink>(logicalEntity))
                ecb.AddComponent(proxyEntity, EntityManager.GetComponentData<CellLink>(logicalEntity));
            if (EntityManager.HasComponent<InteriorCellMember>(logicalEntity))
                ecb.AddComponent<InteriorCellMember>(proxyEntity);

            return proxyEntity;
        }
    }
}
