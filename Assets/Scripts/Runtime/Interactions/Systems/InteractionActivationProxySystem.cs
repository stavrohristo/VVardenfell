using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Physics;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;
using BoxCollider = Unity.Physics.BoxCollider;
using Collider = Unity.Physics.Collider;

namespace VVardenfell.Runtime.Interactions
{
    [UpdateInGroup(typeof(MorrowindPhysicsPreBuildSystemGroup))]
    public partial struct InteractionActivationProxySystem : ISystem
    {
        EntityQuery _pendingQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _pendingQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<InteractionActivationProxyBuildPending>(),
                ComponentType.ReadOnly<LogicalRefTag>(),
            ComponentType.ReadOnly<PlacedRefIdentity>());
            systemState.RequireForUpdate<LoadedCellsMap>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
            systemState.RequireForUpdate<RuntimeWorldCellBlobReference>();
            systemState.RequireForUpdate(_pendingQuery);
        }

        public void OnUpdate(ref SystemState systemState)
        {
            systemState.EntityManager.CompleteDependencyBeforeRO<LocalToWorld>();
            var loaded = SystemAPI.GetSingleton<LoadedCellsMap>();
            ref RuntimeContentBlob contentBlob = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;
            var worldCellReference = SystemAPI.GetSingleton<RuntimeWorldCellBlobReference>();
            if (!worldCellReference.Blob.IsCreated)
                throw new System.InvalidOperationException("[VVardenfell][WorldCellBlob] interaction proxy resolution requires runtime world cell blob.");
            ref RuntimeWorldCellBlob worldCells = ref worldCellReference.Blob.Value;
            using (var pending = _pendingQuery.ToEntityArray(Allocator.Temp))
            {
                var ecb = new EntityCommandBuffer(Allocator.Temp);
                for (int i = 0; i < pending.Length; i++)
                {
                    var logicalEntity = pending[i];
                    if (!systemState.EntityManager.Exists(logicalEntity))
                        continue;

                    if (InteractionActivationProxyBuildUtility.HasLiveProxy(systemState.EntityManager, logicalEntity))
                    {
                        InteractionActivationProxyBuildUtility.QueuePendingCleared(systemState.EntityManager, ref ecb, logicalEntity);
                        continue;
                    }

                    bool exteriorActive = true;
                    if (systemState.EntityManager.HasComponent<CellLink>(logicalEntity))
                        exteriorActive = loaded.Active.Contains(systemState.EntityManager.GetComponentData<CellLink>(logicalEntity).Value);

                    if (!exteriorActive)
                        continue;

                    if (!InteractionTargetResolver.TryResolveSupportedKind(ref contentBlob, ref worldCells, systemState.EntityManager, logicalEntity, out _))
                    {
                        InteractionActivationProxyBuildUtility.QueuePendingCleared(systemState.EntityManager, ref ecb, logicalEntity);
                        continue;
                    }

                    if (!InteractionProxyBoundsUtility.TryBuildAggregateWorldBounds(systemState.EntityManager, logicalEntity, out AABB worldBounds))
                    {
                        InteractionActivationProxyBuildUtility.QueuePendingCleared(systemState.EntityManager, ref ecb, logicalEntity);
                        continue;
                    }

                    bool followsActor = systemState.EntityManager.HasComponent<PassiveActorPresence>(logicalEntity);
                    Entity proxyEntity = QueueCreateProxyEntity(ref systemState, ref ecb, logicalEntity, worldBounds, exteriorActive, followsActor);
                    if (systemState.EntityManager.HasComponent<InteractionActivationProxyState>(logicalEntity))
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

                    if (systemState.EntityManager.HasBuffer<LogicalRefChild>(logicalEntity))
                        ecb.AppendToBuffer(logicalEntity, new LogicalRefChild { Value = proxyEntity });

                    InteractionActivationProxyBuildUtility.QueuePendingCleared(systemState.EntityManager, ref ecb, logicalEntity);
                }

                ecb.Playback(systemState.EntityManager);
                ecb.Dispose();
            }
        }

        Entity QueueCreateProxyEntity(ref SystemState systemState, ref EntityCommandBuffer ecb, Entity logicalEntity, in AABB worldBounds, bool exteriorActive, bool followsActor)
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
            ecb.AddComponent(proxyEntity, LocalTransform.FromPositionRotationScale(worldBounds.Center, quaternion.identity, 1f));
            ecb.AddComponent(proxyEntity, new LocalToWorld
            {
                Value = float4x4.TRS(worldBounds.Center, quaternion.identity, new float3(1f)),
            });
            RuntimeColliderAttachmentUtility.QueueAttachNewSource(
                systemState.EntityManager,
                ref ecb,
                proxyEntity,
                collider,
                RuntimeColliderKind.ActivationProxy,
                active: exteriorActive || !systemState.EntityManager.HasComponent<CellLink>(logicalEntity),
                temporary: true);
            ecb.AddComponent(proxyEntity, new LogicalRefParent { Value = logicalEntity });
            ecb.AddComponent<InteractionActivationProxyTag>(proxyEntity);
            if (followsActor)
                ecb.AddComponent<InteractionActivationProxyFollowTag>(proxyEntity);
            else
                ecb.AddComponent<Unity.Transforms.Static>(proxyEntity);

            if (systemState.EntityManager.HasComponent<CellLink>(logicalEntity))
                ecb.AddComponent(proxyEntity, systemState.EntityManager.GetComponentData<CellLink>(logicalEntity));
            if (systemState.EntityManager.HasComponent<InteriorCellMember>(logicalEntity))
                ecb.AddComponent<InteriorCellMember>(proxyEntity);

            return proxyEntity;
        }
    }
}
