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
using VVardenfell.Runtime.Content;
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
        const float MinUsableHitFraction = 0.001f;

        public static PlayerInteractionRaycastHit CastFromViewRay(
            in PhysicsWorldSingleton physicsWorld,
            in PlayerPhysicsViewPose viewPose,
            uint sequence)
        {
            float3 origin = viewPose.Position;
            float3 forward = math.normalizesafe(math.rotate(viewPose.Rotation, new float3(0f, 0f, 1f)), new float3(0f, 0f, 1f));
            return CastFromCameraRay(physicsWorld, origin, forward, MaxInteractDistance, 0f, sequence);
        }

        public static PlayerInteractionRaycastHit CastFromCameraRay(
            in PhysicsWorldSingleton physicsWorld,
            float3 origin,
            float3 forward,
            float queryDistance,
            float reportedDistanceOffset,
            uint sequence)
        {
            forward = math.normalizesafe(forward, new float3(0f, 0f, 1f));
            queryDistance = math.max(0f, queryDistance);
            reportedDistanceOffset = math.max(0f, reportedDistanceOffset);
            var input = new RaycastInput
            {
                Start = origin,
                End = origin + forward * queryDistance,
                Filter = InteractionCollisionLayers.InteractionPickQueryFilter,
            };

            var hit = new PlayerInteractionRaycastHit
            {
                Sequence = sequence,
                HitEntity = Entity.Null,
                ProxyHitEntity = Entity.Null,
                SolidHitEntity = Entity.Null,
            };

            if (queryDistance <= 0f || !TryCastNearestUsableHit(physicsWorld, input, out Unity.Physics.RaycastHit pickHit))
                return hit;

            SetPrimaryHit(
                ref hit,
                pickHit.Entity,
                pickHit.Position,
                pickHit.SurfaceNormal,
                pickHit.Fraction,
                math.max(0f, pickHit.Fraction * queryDistance - reportedDistanceOffset));

            return hit;
        }

        static bool TryCastNearestUsableHit(
            in PhysicsWorldSingleton physicsWorld,
            in RaycastInput input,
            out Unity.Physics.RaycastHit nearestHit)
        {
            nearestHit = default;
            var hits = new NativeList<Unity.Physics.RaycastHit>(Allocator.Temp);
            try
            {
                if (!physicsWorld.CastRay(input, ref hits))
                    return false;

                bool found = false;
                float nearestFraction = float.PositiveInfinity;
                for (int i = 0; i < hits.Length; i++)
                {
                    var hit = hits[i];
                    if (hit.Fraction <= MinUsableHitFraction || hit.Fraction >= nearestFraction)
                        continue;

                    nearestHit = hit;
                    nearestFraction = hit.Fraction;
                    found = true;
                }

                return found;
            }
            finally
            {
                if (hits.IsCreated)
                    hits.Dispose();
            }
        }

        static void SetPrimaryHit(ref PlayerInteractionRaycastHit hit, Entity entity, float3 position, float3 normal, float fraction, float distance)
        {
            hit.HasHit = 1;
            hit.HitEntity = entity;
            hit.HitPosition = position;
            hit.HitNormal = normal;
            hit.HitFraction = fraction;
            hit.HitDistance = distance;
        }

        public static bool TryResolveFromRaycastHit(
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
            if (!TryResolveEntity(entityManager, logicalRefLookup, hit.HitEntity, out resolved))
                return false;

            resolved = new ResolvedInteractionTarget(
                resolved.TargetEntity,
                resolved.PlacedRefId,
                resolved.Kind,
                hit.HitDistance);
            return true;
        }

        public static bool TryResolveFromViewRay(
            EntityManager entityManager,
            in PhysicsWorldSingleton physicsWorld,
            in LogicalRefLookup logicalRefLookup,
            in PlayerPhysicsViewPose viewPose,
            out Entity hitEntity,
            out ResolvedInteractionTarget resolved)
        {
            var hit = CastFromViewRay(physicsWorld, viewPose, 0u);
            return TryResolveFromRaycastHit(entityManager, logicalRefLookup, hit, out hitEntity, out resolved);
        }

        public static bool TryResolveSupportedKind(EntityManager entityManager, Entity logicalEntity, out InteractableKind kind)
        {
            kind = InteractableKind.None;

            if (entityManager.HasComponent<DoorInteractable>(logicalEntity)
                || (entityManager.HasComponent<DoorAuthoring>(logicalEntity)
                    && DoorInteractableResolver.TryResolve(entityManager, logicalEntity, out _)))
            {
                kind = InteractableKind.Door;
                return true;
            }

            if (LooseCarryableResolver.TryResolveContent(RuntimeContentDatabase.Active, entityManager, logicalEntity, out _))
            {
                kind = InteractableKind.LooseItem;
                return true;
            }

            if (entityManager.HasComponent<ContainerAuthoring>(logicalEntity))
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
            EntityManager entityManager,
            in LogicalRefLookup logicalRefLookup,
            Entity hitEntity,
            out ResolvedInteractionTarget resolved)
        {
            resolved = default;

            if (!TryResolveLogicalEntity(entityManager, logicalRefLookup, hitEntity, out Entity logicalEntity))
                return false;

            if (!entityManager.Exists(logicalEntity)
                || !entityManager.HasComponent<LogicalRefTag>(logicalEntity)
                || !entityManager.HasComponent<PlacedRefIdentity>(logicalEntity))
            {
                return false;
            }

            if (!TryResolveSupportedKind(entityManager, logicalEntity, out InteractableKind kind))
                return false;

            uint placedRefId = entityManager.GetComponentData<PlacedRefIdentity>(logicalEntity).Value;
            if (placedRefId == 0u)
                return false;

            resolved = new ResolvedInteractionTarget(logicalEntity, placedRefId, kind, 0f);
            return true;
        }
    }


    [UpdateInGroup(typeof(MorrowindPhysicsQuerySystemGroup))]
    [UpdateAfter(typeof(PlayerPhysicsViewPoseSystem))]
    [UpdateBefore(typeof(InteractionTargetResolutionSystem))]
    public partial class PlayerInteractionRaycastSystem : SystemBase
    {
        EntityQuery _viewPoseQuery;
        EntityQuery _raycastHitQuery;

        protected override void OnCreate()
        {
            _viewPoseQuery = GetEntityQuery(ComponentType.ReadOnly<PlayerPhysicsViewPose>());
            _raycastHitQuery = GetEntityQuery(ComponentType.ReadWrite<PlayerInteractionRaycastHit>());
            RequireForUpdate(_viewPoseQuery);
            RequireForUpdate(_raycastHitQuery);
            RequireForUpdate<PhysicsWorldSingleton>();
        }

        protected override void OnUpdate()
        {
            var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
            var viewPose = _viewPoseQuery.GetSingleton<PlayerPhysicsViewPose>();
            var hitRef = _raycastHitQuery.GetSingletonRW<PlayerInteractionRaycastHit>();
            uint sequence = hitRef.ValueRO.Sequence + 1u;
            hitRef.ValueRW = InteractionTargetResolver.CastFromViewRay(physicsWorld, viewPose, sequence);
        }
    }

    [UpdateInGroup(typeof(MorrowindPhysicsQuerySystemGroup))]
    [UpdateAfter(typeof(PlayerInteractionRaycastSystem))]
    public partial class InteractionTargetResolutionSystem : SystemBase
    {
        EntityQuery _raycastHitQuery;
        EntityQuery _focusQuery;

        protected override void OnCreate()
        {
            _raycastHitQuery = GetEntityQuery(ComponentType.ReadOnly<PlayerInteractionRaycastHit>());
            _focusQuery = GetEntityQuery(ComponentType.ReadWrite<PlayerInteractionFocus>());
            RequireForUpdate(_raycastHitQuery);
            RequireForUpdate(_focusQuery);
            RequireForUpdate<LogicalRefLookup>();
        }

        protected override void OnUpdate()
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
            if (!InteractionTargetResolver.TryResolveFromRaycastHit(
                    EntityManager,
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
