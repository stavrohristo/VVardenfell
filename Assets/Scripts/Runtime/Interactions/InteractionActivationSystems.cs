using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Profiling;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Player;
using BoxCollider = Unity.Physics.BoxCollider;
using Collider = Unity.Physics.Collider;

using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Inventory;
using VVardenfell.Runtime.Physics;
using VVardenfell.Runtime.WorldState;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Interactions
{

    static class InteractionProxyBoundsUtility
    {
        static readonly float3 MinExtents = new(0.08f, 0.08f, 0.08f);

        public static bool TryBuildAggregateWorldBounds(EntityManager entityManager, Entity logicalEntity, out AABB worldBounds)
        {
            worldBounds = default;
            if (!entityManager.Exists(logicalEntity))
                return false;

            bool hasBounds = false;
            if (TryGetWorldBounds(entityManager, logicalEntity, out AABB logicalBounds))
            {
                worldBounds = logicalBounds;
                hasBounds = true;
            }

            if (!entityManager.HasBuffer<LogicalRefChild>(logicalEntity))
                return hasBounds;

            var children = entityManager.GetBuffer<LogicalRefChild>(logicalEntity);
            for (int i = 0; i < children.Length; i++)
            {
                Entity child = children[i].Value;
                if (!entityManager.Exists(child) || entityManager.HasComponent<InteractionActivationProxyTag>(child))
                    continue;

                if (!TryGetWorldBounds(entityManager, child, out AABB childBounds))
                    continue;

                worldBounds = hasBounds ? Encapsulate(worldBounds, childBounds) : childBounds;
                hasBounds = true;
            }

            if (!hasBounds)
                return false;

            worldBounds.Extents = math.max(worldBounds.Extents, MinExtents);
            return true;
        }

        static bool TryGetWorldBounds(EntityManager entityManager, Entity entity, out AABB worldBounds)
        {
            worldBounds = default;
            if (!entityManager.HasComponent<RenderBounds>(entity) || !entityManager.HasComponent<LocalToWorld>(entity))
                return false;

            var localBounds = entityManager.GetComponentData<RenderBounds>(entity).Value;
            float4x4 localToWorld = entityManager.GetComponentData<LocalToWorld>(entity).Value;
            float3 center = math.transform(localToWorld, localBounds.Center);
            float3x3 rotationScale = new(localToWorld.c0.xyz, localToWorld.c1.xyz, localToWorld.c2.xyz);
            float3 extents = math.abs(rotationScale.c0) * localBounds.Extents.x
                + math.abs(rotationScale.c1) * localBounds.Extents.y
                + math.abs(rotationScale.c2) * localBounds.Extents.z;

            worldBounds = new AABB
            {
                Center = center,
                Extents = extents,
            };
            return true;
        }

        static AABB Encapsulate(AABB a, AABB b)
        {
            float3 min = math.min(a.Min, b.Min);
            float3 max = math.max(a.Max, b.Max);
            return new AABB
            {
                Center = (min + max) * 0.5f,
                Extents = (max - min) * 0.5f,
            };
        }
    }

    static class InteractionActivationProxyBuildUtility
    {
        public static bool QueueEnsureQueued(EntityManager entityManager, ref EntityCommandBuffer ecb, Entity logicalEntity)
        {
            if (!entityManager.Exists(logicalEntity))
                return false;

            if (!entityManager.HasComponent<LogicalRefTag>(logicalEntity) || !entityManager.HasComponent<PlacedRefIdentity>(logicalEntity))
                return false;

            if (entityManager.HasComponent<InteractionActivationProxyBuildPending>(logicalEntity))
                return false;

            if (HasLiveProxy(entityManager, logicalEntity))
                return false;

            if (entityManager.HasComponent<InteractionActivationProxyState>(logicalEntity))
            {
                Entity existingProxy = entityManager.GetComponentData<InteractionActivationProxyState>(logicalEntity).ProxyEntity;
                if (existingProxy == Entity.Null || !entityManager.Exists(existingProxy))
                    ecb.RemoveComponent<InteractionActivationProxyState>(logicalEntity);
                else
                    return false;
            }

            if (!InteractionTargetResolver.TryResolveSupportedKind(entityManager, logicalEntity, out _))
                return false;

            ecb.AddComponent<InteractionActivationProxyBuildPending>(logicalEntity);
            return true;
        }

        public static void QueuePendingCleared(EntityManager entityManager, ref EntityCommandBuffer ecb, Entity logicalEntity)
        {
            if (entityManager.HasComponent<InteractionActivationProxyBuildPending>(logicalEntity))
                ecb.RemoveComponent<InteractionActivationProxyBuildPending>(logicalEntity);
        }

        public static bool HasLiveProxy(EntityManager entityManager, Entity logicalEntity)
        {
            if (!entityManager.HasComponent<InteractionActivationProxyState>(logicalEntity))
                return false;

            Entity proxyEntity = entityManager.GetComponentData<InteractionActivationProxyState>(logicalEntity).ProxyEntity;
            return proxyEntity != Entity.Null && entityManager.Exists(proxyEntity);
        }
    }

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

                    Entity proxyEntity = QueueCreateProxyEntity(ref ecb, logicalEntity, worldBounds, exteriorActive);
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

        Entity QueueCreateProxyEntity(ref EntityCommandBuffer ecb, Entity logicalEntity, in AABB worldBounds, bool exteriorActive)
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
            ecb.AddComponent<Unity.Transforms.Static>(proxyEntity);

            if (EntityManager.HasComponent<CellLink>(logicalEntity))
                ecb.AddComponent(proxyEntity, EntityManager.GetComponentData<CellLink>(logicalEntity));
            if (EntityManager.HasComponent<InteriorCellMember>(logicalEntity))
                ecb.AddComponent<InteriorCellMember>(proxyEntity);

            return proxyEntity;
        }
    }


    [UpdateInGroup(typeof(MorrowindPhysicsQuerySystemGroup))]
    [UpdateAfter(typeof(InteractionTargetResolutionSystem))]
    public partial class PlayerInteractionActivationSystem : SystemBase
    {
        EntityQuery _playerQuery;
        EntityQuery _focusQuery;
        EntityQuery _requestQuery;
        EntityQuery _transitionQuery;

        protected override void OnCreate()
        {
            _playerQuery = GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadWrite<PlayerCharacterControl>());
            _focusQuery = GetEntityQuery(ComponentType.ReadOnly<PlayerInteractionFocus>());
            _requestQuery = GetEntityQuery(ComponentType.ReadWrite<InteractionActivationRequest>());
            _transitionQuery = GetEntityQuery(ComponentType.ReadOnly<InteriorTransitionState>());
            RequireForUpdate(_playerQuery);
            RequireForUpdate(_focusQuery);
            RequireForUpdate(_requestQuery);
            RequireForUpdate(_transitionQuery);
            RequireForUpdate<InteractionRuntimeState>();
        }

        protected override void OnUpdate()
        {
            var requestRef = _requestQuery.GetSingletonRW<InteractionActivationRequest>();
            ref var request = ref requestRef.ValueRW;
            var transition = _transitionQuery.GetSingleton<InteriorTransitionState>();
            var controlRef = _playerQuery.GetSingletonRW<PlayerCharacterControl>();
            ref var control = ref controlRef.ValueRW;
            if (!control.InteractPressed)
                return;

            control.InteractPressed = false;

            if (request.Pending != 0 || transition.TransitionInProgress != 0)
                return;

            var focus = _focusQuery.GetSingleton<PlayerInteractionFocus>();
            if (focus.HasTarget == 0 || !EntityManager.Exists(focus.TargetEntity))
            {
                return;
            }

            var resolved = new ResolvedInteractionTarget(
                focus.TargetEntity,
                focus.PlacedRefId,
                (InteractableKind)focus.InteractKind,
                focus.HitDistance);

            ref var runtimeState = ref SystemAPI.GetSingletonRW<InteractionRuntimeState>().ValueRW;
            uint sequence = runtimeState.NextActivationSequence + 1u;
            runtimeState.NextActivationSequence = sequence;

            request = new InteractionActivationRequest
            {
                Pending = 1,
                Sequence = sequence,
                Kind = (byte)resolved.Kind,
                TargetEntity = resolved.TargetEntity,
                TargetPlacedRefId = resolved.PlacedRefId,
            };

        }
    }
}
