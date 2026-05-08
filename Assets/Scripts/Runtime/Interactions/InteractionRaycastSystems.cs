using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Profiling;
using Unity.Rendering;
using Unity.Transforms;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Cache;
using BoxCollider = Unity.Physics.BoxCollider;
using Collider = Unity.Physics.Collider;

using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Inventory;
using VVardenfell.Runtime.Physics;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Rendering;
using VVardenfell.Runtime.WorldState;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Interactions
{

    static class InteractionTargetResolver
    {
        public const float MaxInteractDistance = 2.25f;

        static void SetPrimaryHit(ref PlayerInteractionRaycastHit hit, Entity entity, float3 position, float3 normal, float fraction, float distance)
        {
            hit.HasHit = 1;
            hit.HitEntity = entity;
            hit.HitPosition = position;
            hit.HitNormal = normal;
            hit.HitFraction = fraction;
            hit.HitDistance = distance;
        }

        public static PlayerInteractionRaycastHit FromDeferredResult(
            in DeferredPhysicsQueryResult result,
            float queryDistance,
            float reportedDistanceOffset)
        {
            var hit = new PlayerInteractionRaycastHit
            {
                Sequence = result.Sequence,
                HitEntity = Entity.Null,
                ProxyHitEntity = Entity.Null,
                SolidHitEntity = Entity.Null,
            };

            if (result.Status != DeferredPhysicsQueryStatus.Hit)
                return hit;

            SetPrimaryHit(
                ref hit,
                result.HitEntity,
                result.Position,
                result.Normal,
                result.Fraction,
                math.max(0f, result.Fraction * math.max(0f, queryDistance) - math.max(0f, reportedDistanceOffset)));
            return hit;
        }

        public static bool TryResolveFromRaycastHit(
            ref RuntimeContentBlob contentBlob,
            ref RuntimeWorldCellBlob worldCells,
            EntityManager entityManager,
            in LogicalRefLookup logicalRefLookup,
            in PlayerInteractionRaycastHit hit,
            out Entity hitEntity,
            out ResolvedInteractionTarget resolved)
        {
            hitEntity = Entity.Null;
            resolved = default;

            if (hit.HasHit == 0)
                return false;

            hitEntity = hit.HitEntity;
            if (!TryResolveEntity(ref contentBlob, ref worldCells, entityManager, logicalRefLookup, hit.HitEntity, out resolved))
                return false;

            resolved = new ResolvedInteractionTarget(
                resolved.TargetEntity,
                resolved.PlacedRefId,
                resolved.Kind,
                hit.HitDistance);
            return true;
        }

        public static bool TryResolveSupportedKind(ref RuntimeContentBlob contentBlob, ref RuntimeWorldCellBlob worldCells, EntityManager entityManager, Entity logicalEntity, out InteractableKind kind)
        {
            kind = InteractableKind.None;

            if (entityManager.HasComponent<DoorInteractable>(logicalEntity))
            {
                kind = InteractableKind.Door;
                return true;
            }

            if (entityManager.HasComponent<DoorAuthoring>(logicalEntity)
                && entityManager.HasComponent<DoorMotionState>(logicalEntity))
            {
                kind = InteractableKind.Door;
                return true;
            }

            if (LooseCarryableResolver.TryResolveContent(ref contentBlob, entityManager, logicalEntity, out _))
            {
                kind = InteractableKind.LooseItem;
                return true;
            }

            if (entityManager.HasComponent<ContainerAuthoring>(logicalEntity))
            {
                kind = InteractableKind.Container;
                return true;
            }

            if (ActorCorpseLootUtility.IsDeadLootableActor(entityManager, logicalEntity))
            {
                kind = InteractableKind.Container;
                return true;
            }

            if (entityManager.HasComponent<ActivatorAuthoring>(logicalEntity))
            {
                kind = InteractableKind.Activator;
                return true;
            }

            if (entityManager.HasComponent<PassiveActorPresence>(logicalEntity))
            {
                var actor = entityManager.GetComponentData<PassiveActorPresence>(logicalEntity);
                if (actor.CanTalk != 0)
                {
                    kind = InteractableKind.Npc;
                    return true;
                }
            }

            return false;
        }

        public static bool TryResolveLogicalEntity(
            EntityManager entityManager,
            in LogicalRefLookup logicalRefLookup,
            Entity hitEntity,
            out Entity logicalEntity)
        {
            logicalEntity = Entity.Null;

            if (!entityManager.Exists(hitEntity))
                return false;

            if (entityManager.HasComponent<LogicalRefParent>(hitEntity))
            {
                logicalEntity = entityManager.GetComponentData<LogicalRefParent>(hitEntity).Value;
                return entityManager.Exists(logicalEntity);
            }

            if (entityManager.HasComponent<LogicalRefTag>(hitEntity))
            {
                logicalEntity = hitEntity;
                return true;
            }

            if (!entityManager.HasComponent<PlacedRefIdentity>(hitEntity))
                return false;

            uint childPlacedRefId = entityManager.GetComponentData<PlacedRefIdentity>(hitEntity).Value;
            if (childPlacedRefId == 0u || !logicalRefLookup.Map.IsCreated)
                return false;

            return logicalRefLookup.Map.TryGetValue(childPlacedRefId, out logicalEntity) && entityManager.Exists(logicalEntity);
        }

        public static bool TryResolveEntity(
            ref RuntimeContentBlob contentBlob,
            ref RuntimeWorldCellBlob worldCells,
            EntityManager entityManager,
            in LogicalRefLookup logicalRefLookup,
            Entity hitEntity,
            out ResolvedInteractionTarget resolved)
        {
            resolved = default;

            if (!TryResolveLogicalEntity(entityManager, logicalRefLookup, hitEntity, out Entity logicalEntity))
                return false;

            if (entityManager.HasComponent<RuntimeColliderSource>(hitEntity)
                && !entityManager.HasComponent<PhysicsCollider>(hitEntity))
            {
                return false;
            }

            if (!entityManager.Exists(logicalEntity)
                || !entityManager.HasComponent<LogicalRefTag>(logicalEntity)
                || !entityManager.HasComponent<PlacedRefIdentity>(logicalEntity))
            {
                return false;
            }

            if (entityManager.HasComponent<PlacedRefRuntimeState>(logicalEntity)
                && entityManager.GetComponentData<PlacedRefRuntimeState>(logicalEntity).Disabled != 0)
            {
                return false;
            }

            if (!TryResolveSupportedKind(ref contentBlob, ref worldCells, entityManager, logicalEntity, out InteractableKind kind))
                return false;

            uint placedRefId = entityManager.GetComponentData<PlacedRefIdentity>(logicalEntity).Value;
            if (placedRefId == 0u)
                return false;

            resolved = new ResolvedInteractionTarget(logicalEntity, placedRefId, kind, 0f);
            return true;
        }
    }


    [UpdateInGroup(typeof(MorrowindFramePhysicsQuerySystemGroup))]
    [UpdateAfter(typeof(DeferredPhysicsQueryResolveSystem))]
    [UpdateAfter(typeof(PlayerPhysicsViewPoseSystem))]
    [UpdateBefore(typeof(InteractionTargetResolutionSystem))]
    public partial struct PlayerInteractionRaycastSystem : ISystem
    {
        const float AssistCastRadius = 0.10f;
        const float MinUsableHitFraction = 0.0001f;

        EntityQuery _viewPoseQuery;
        EntityQuery _raycastHitQuery;
        EntityQuery _playerQuery;
        BlobAssetReference<Collider> _assistCastCollider;

        public void OnCreate(ref SystemState systemState)
        {
            _viewPoseQuery = systemState.GetEntityQuery(ComponentType.ReadOnly<PlayerPhysicsViewPose>());
            _raycastHitQuery = systemState.GetEntityQuery(ComponentType.ReadWrite<PlayerInteractionRaycastHit>());
            _playerQuery = systemState.GetEntityQuery(ComponentType.ReadOnly<PlayerTag>());
            _assistCastCollider = SphereCollider.Create(
                new SphereGeometry
                {
                    Center = float3.zero,
                    Radius = AssistCastRadius,
                },
                InteractionCollisionLayers.InteractionExactPickQueryFilter);

            systemState.RequireForUpdate(_viewPoseQuery);
            systemState.RequireForUpdate(_raycastHitQuery);
            systemState.RequireForUpdate(_playerQuery);
            systemState.RequireForUpdate<PhysicsWorldSingleton>();
            systemState.RequireForUpdate<LogicalRefLookup>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
            systemState.RequireForUpdate<RuntimeWorldCellBlobReference>();
        }

        public void OnDestroy(ref SystemState systemState)
        {
            if (_assistCastCollider.IsCreated)
                _assistCastCollider.Dispose();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            systemState.Dependency.Complete();

            var viewPose = _viewPoseQuery.GetSingleton<PlayerPhysicsViewPose>();
            var hitRef = _raycastHitQuery.GetSingletonRW<PlayerInteractionRaycastHit>();
            uint sequence = hitRef.ValueRO.Sequence + 1u;
            float3 forward = math.normalizesafe(math.rotate(viewPose.Rotation, new float3(0f, 0f, 1f)), new float3(0f, 0f, 1f));
            float3 start = viewPose.Position;
            float3 end = start + forward * InteractionTargetResolver.MaxInteractDistance;
            Entity player = _playerQuery.GetSingletonEntity();
            var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
            var logicalRefLookup = SystemAPI.GetSingleton<LogicalRefLookup>();
            ref RuntimeContentBlob contentBlob = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;
            var worldCellReference = SystemAPI.GetSingleton<RuntimeWorldCellBlobReference>();
            if (!worldCellReference.Blob.IsCreated)
                throw new System.InvalidOperationException("[VVardenfell][WorldCellBlob] interaction raycast requires runtime world cell blob.");
            ref RuntimeWorldCellBlob worldCells = ref worldCellReference.Blob.Value;

            var rayHits = new NativeList<Unity.Physics.RaycastHit>(Allocator.Temp);
            var castHits = new NativeList<ColliderCastHit>(Allocator.Temp);
            try
            {
                if (TryFindBestRayHit(
                        physicsWorld,
                        systemState.EntityManager,
                        ref contentBlob,
                        ref worldCells,
                        logicalRefLookup,
                        player,
                        start,
                        end,
                        InteractionCollisionLayers.InteractionExactPickQueryFilter,
                        allowProxyFallback: false,
                        ref rayHits,
                        out var selected)
                    || TryFindBestColliderHit(
                        physicsWorld,
                        systemState.EntityManager,
                        ref contentBlob,
                        ref worldCells,
                        logicalRefLookup,
                        player,
                        start,
                        end,
                        _assistCastCollider,
                        ref castHits,
                        out selected)
                    || TryFindBestRayHit(
                        physicsWorld,
                        systemState.EntityManager,
                        ref contentBlob,
                        ref worldCells,
                        logicalRefLookup,
                        player,
                        start,
                        end,
                        InteractionCollisionLayers.InteractionActivationProxyQueryFilter,
                        allowProxyFallback: true,
                        ref rayHits,
                        out selected))
                {
                    hitRef.ValueRW = BuildHit(sequence, selected);
                }
                else
                {
                    hitRef.ValueRW = new PlayerInteractionRaycastHit
                    {
                        Sequence = sequence,
                        HitEntity = Entity.Null,
                        ProxyHitEntity = Entity.Null,
                        SolidHitEntity = Entity.Null,
                    };
                }
            }
            finally
            {
                rayHits.Dispose();
                castHits.Dispose();
            }
        }

        static bool TryFindBestRayHit(
            in PhysicsWorldSingleton physicsWorld,
            EntityManager entityManager,
            ref RuntimeContentBlob contentBlob,
            ref RuntimeWorldCellBlob worldCells,
            in LogicalRefLookup logicalRefLookup,
            Entity player,
            float3 start,
            float3 end,
            CollisionFilter filter,
            bool allowProxyFallback,
            ref NativeList<Unity.Physics.RaycastHit> hits,
            out InteractionCandidateHit selected)
        {
            selected = default;
            hits.Clear();
            var input = new RaycastInput
            {
                Start = start,
                End = end,
                Filter = filter,
            };

            if (!physicsWorld.CollisionWorld.CastRay(input, ref hits))
                return false;

            bool found = false;
            for (int i = 0; i < hits.Length; i++)
            {
                var hit = hits[i];
                if (!TryBuildCandidate(
                        entityManager,
                        ref contentBlob,
                        ref worldCells,
                        logicalRefLookup,
                        player,
                        start,
                        hit.Entity,
                        hit.Position,
                        hit.SurfaceNormal,
                        hit.Fraction,
                        allowProxyFallback,
                        out var candidate))
                {
                    continue;
                }

                if (found && candidate.Distance >= selected.Distance)
                    continue;

                selected = candidate;
                found = true;
            }

            return found;
        }

        static bool TryFindBestColliderHit(
            in PhysicsWorldSingleton physicsWorld,
            EntityManager entityManager,
            ref RuntimeContentBlob contentBlob,
            ref RuntimeWorldCellBlob worldCells,
            in LogicalRefLookup logicalRefLookup,
            Entity player,
            float3 start,
            float3 end,
            BlobAssetReference<Collider> collider,
            ref NativeList<ColliderCastHit> hits,
            out InteractionCandidateHit selected)
        {
            selected = default;
            hits.Clear();
            var input = new ColliderCastInput(collider, start, end, quaternion.identity);
            if (!physicsWorld.CollisionWorld.CastCollider(input, ref hits))
                return false;

            bool found = false;
            for (int i = 0; i < hits.Length; i++)
            {
                var hit = hits[i];
                if (!TryBuildCandidate(
                        entityManager,
                        ref contentBlob,
                        ref worldCells,
                        logicalRefLookup,
                        player,
                        start,
                        hit.Entity,
                        hit.Position,
                        hit.SurfaceNormal,
                        hit.Fraction,
                        allowProxyFallback: false,
                        out var candidate))
                {
                    continue;
                }

                if (found && candidate.Distance >= selected.Distance)
                    continue;

                selected = candidate;
                found = true;
            }

            return found;
        }

        static bool TryBuildCandidate(
            EntityManager entityManager,
            ref RuntimeContentBlob contentBlob,
            ref RuntimeWorldCellBlob worldCells,
            in LogicalRefLookup logicalRefLookup,
            Entity player,
            float3 start,
            Entity hitEntity,
            float3 position,
            float3 normal,
            float fraction,
            bool allowProxyFallback,
            out InteractionCandidateHit candidate)
        {
            candidate = default;
            if (fraction <= MinUsableHitFraction)
                return false;
            if (hitEntity == Entity.Null || hitEntity == player)
                return false;
            if (!math.all(math.isfinite(position)))
                return false;

            float distance = math.distance(start, position);
            if (distance > InteractionTargetResolver.MaxInteractDistance)
                return false;
            if (!InteractionTargetResolver.TryResolveEntity(
                    ref contentBlob,
                    ref worldCells,
                    entityManager,
                    logicalRefLookup,
                    hitEntity,
                    out ResolvedInteractionTarget resolved))
            {
                return false;
            }

            bool isProxyHit = entityManager.HasComponent<InteractionActivationProxyTag>(hitEntity);
            if (isProxyHit)
            {
                if (!allowProxyFallback)
                    return false;
                if (!CanUseActivationProxyFallback(entityManager, resolved.TargetEntity))
                    return false;
            }

            candidate = new InteractionCandidateHit
            {
                Entity = hitEntity,
                Position = position,
                Normal = normal,
                Fraction = fraction,
                Distance = distance,
                IsProxy = isProxyHit,
            };
            return true;
        }

        static bool CanUseActivationProxyFallback(EntityManager entityManager, Entity logicalEntity)
        {
            if (entityManager.HasComponent<PassiveActorPresence>(logicalEntity))
                return true;
            if (entityManager.HasComponent<InteractionPickSurfaceTag>(logicalEntity)
                && entityManager.HasComponent<PhysicsCollider>(logicalEntity))
            {
                return false;
            }
            if (!entityManager.HasBuffer<LogicalRefChild>(logicalEntity))
                return true;

            var children = entityManager.GetBuffer<LogicalRefChild>(logicalEntity);
            for (int i = 0; i < children.Length; i++)
            {
                Entity child = children[i].Value;
                if (!entityManager.Exists(child))
                    continue;
                if (entityManager.HasComponent<InteractionActivationProxyTag>(child))
                    continue;
                if (!entityManager.HasComponent<InteractionPickSurfaceTag>(child))
                    continue;
                if (entityManager.HasComponent<PhysicsCollider>(child))
                    return false;
            }

            return true;
        }

        static PlayerInteractionRaycastHit BuildHit(uint sequence, in InteractionCandidateHit selected)
        {
            var hit = new PlayerInteractionRaycastHit
            {
                Sequence = sequence,
                HitEntity = selected.Entity,
                HitPosition = selected.Position,
                HitNormal = selected.Normal,
                HitDistance = selected.Distance,
                HitFraction = selected.Fraction,
                HasHit = 1,
                ProxyHitEntity = Entity.Null,
                SolidHitEntity = Entity.Null,
            };

            if (selected.IsProxy)
            {
                hit.ProxyHitEntity = selected.Entity;
                hit.ProxyHitPosition = selected.Position;
                hit.ProxyHitNormal = selected.Normal;
                hit.ProxyHitDistance = selected.Distance;
                hit.ProxyHitFraction = selected.Fraction;
                hit.HasProxyHit = 1;
            }

            return hit;
        }

        struct InteractionCandidateHit
        {
            public Entity Entity;
            public float3 Position;
            public float3 Normal;
            public float Distance;
            public float Fraction;
            public bool IsProxy;
        }
    }

    [UpdateInGroup(typeof(MorrowindFramePhysicsQuerySystemGroup))]
    [UpdateAfter(typeof(PlayerInteractionRaycastSystem))]
    public partial struct InteractionTargetResolutionSystem : ISystem
    {
        EntityQuery _raycastHitQuery;
        EntityQuery _focusQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _raycastHitQuery = systemState.GetEntityQuery(ComponentType.ReadOnly<PlayerInteractionRaycastHit>());
            _focusQuery = systemState.GetEntityQuery(ComponentType.ReadWrite<PlayerInteractionFocus>());
            systemState.RequireForUpdate(_raycastHitQuery);
            systemState.RequireForUpdate(_focusQuery);
            systemState.RequireForUpdate<LogicalRefLookup>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
            systemState.RequireForUpdate<RuntimeWorldCellBlobReference>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            var focusRef = _focusQuery.GetSingletonRW<PlayerInteractionFocus>();
            ref var focus = ref focusRef.ValueRW;
            focus.TargetEntity = Entity.Null;
            focus.PlacedRefId = 0u;
            focus.InteractKind = (byte)InteractableKind.None;
            focus.HitDistance = 0f;
            focus.HasTarget = 0;

            var raycastHit = _raycastHitQuery.GetSingleton<PlayerInteractionRaycastHit>();
            var logicalRefLookup = SystemAPI.GetSingleton<LogicalRefLookup>();
            ref RuntimeContentBlob contentBlob = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;
            var worldCellReference = SystemAPI.GetSingleton<RuntimeWorldCellBlobReference>();
            if (!worldCellReference.Blob.IsCreated)
                throw new System.InvalidOperationException("[VVardenfell][WorldCellBlob] interaction target resolution requires runtime world cell blob.");
            ref RuntimeWorldCellBlob worldCells = ref worldCellReference.Blob.Value;
            if (!InteractionTargetResolver.TryResolveFromRaycastHit(
                    ref contentBlob,
                    ref worldCells,
                    systemState.EntityManager,
                    logicalRefLookup,
                    raycastHit,
                    out _,
                    out ResolvedInteractionTarget resolved))
            {
                return;
            }

            focus.TargetEntity = resolved.TargetEntity;
            focus.PlacedRefId = resolved.PlacedRefId;
            focus.InteractKind = (byte)resolved.Kind;
            focus.HitDistance = resolved.HitDistance;
            focus.HasTarget = 1;
        }
    }
}
