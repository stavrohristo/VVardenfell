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

    static class InteractionTargetResolver
    {
        public const float MaxInteractDistance = 2.25f;

        public static PlayerInteractionRaycastHit CastFromViewRay(
            in PhysicsWorldSingleton physicsWorld,
            in PlayerPhysicsViewPose viewPose,
            uint sequence)
        {
            float3 origin = viewPose.Position;
            float3 forward = math.normalizesafe(math.rotate(viewPose.Rotation, new float3(0f, 0f, 1f)), new float3(0f, 0f, 1f));
            var activationInput = new RaycastInput
            {
                Start = origin,
                End = origin + forward * MaxInteractDistance,
                Filter = InteractionCollisionLayers.ActivationQueryFilter,
            };

            var solidInput = new RaycastInput
            {
                Start = origin,
                End = activationInput.End,
                Filter = InteractionCollisionLayers.SolidQueryFilter,
            };

            bool hasProxyHit = physicsWorld.CastRay(activationInput, out Unity.Physics.RaycastHit proxyHit);
            bool hasSolidHit = physicsWorld.CastRay(solidInput, out Unity.Physics.RaycastHit solidHit);

            var hit = new PlayerInteractionRaycastHit
            {
                Sequence = sequence,
                HitEntity = Entity.Null,
                ProxyHitEntity = Entity.Null,
                SolidHitEntity = Entity.Null,
                HasProxyHit = (byte)(hasProxyHit ? 1 : 0),
                HasSolidHit = (byte)(hasSolidHit ? 1 : 0),
            };

            if (hasProxyHit)
            {
                hit.ProxyHitEntity = proxyHit.Entity;
                hit.ProxyHitPosition = proxyHit.Position;
                hit.ProxyHitNormal = proxyHit.SurfaceNormal;
                hit.ProxyHitFraction = proxyHit.Fraction;
                hit.ProxyHitDistance = proxyHit.Fraction * MaxInteractDistance;
            }

            if (hasSolidHit)
            {
                hit.SolidHitEntity = solidHit.Entity;
                hit.SolidHitPosition = solidHit.Position;
                hit.SolidHitNormal = solidHit.SurfaceNormal;
                hit.SolidHitFraction = solidHit.Fraction;
                hit.SolidHitDistance = solidHit.Fraction * MaxInteractDistance;
            }

            if (hasProxyHit && (!hasSolidHit || proxyHit.Fraction <= solidHit.Fraction))
            {
                SetPrimaryHit(ref hit, proxyHit.Entity, proxyHit.Position, proxyHit.SurfaceNormal, proxyHit.Fraction);
            }
            else if (hasSolidHit)
            {
                SetPrimaryHit(ref hit, solidHit.Entity, solidHit.Position, solidHit.SurfaceNormal, solidHit.Fraction);
            }

            return hit;
        }

        static void SetPrimaryHit(ref PlayerInteractionRaycastHit hit, Entity entity, float3 position, float3 normal, float fraction)
        {
            hit.HasHit = 1;
            hit.HitEntity = entity;
            hit.HitPosition = position;
            hit.HitNormal = normal;
            hit.HitFraction = fraction;
            hit.HitDistance = fraction * MaxInteractDistance;
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

            bool hasProxyHit = hit.HasProxyHit != 0;
            bool hasSolidHit = hit.HasSolidHit != 0;

            if (hasProxyHit && TryResolveEntity(entityManager, logicalRefLookup, hit.ProxyHitEntity, out ResolvedInteractionTarget proxyResolved))
            {
                if (hasSolidHit && SolidHitBlocksProxy(entityManager, logicalRefLookup, hit.SolidHitEntity, hit.SolidHitFraction, proxyResolved.TargetEntity, hit.ProxyHitFraction))
                {
                    hitEntity = hit.SolidHitEntity;
                    if (TryResolveEntity(entityManager, logicalRefLookup, hit.SolidHitEntity, out resolved))
                    {
                        resolved = new ResolvedInteractionTarget(
                            resolved.TargetEntity,
                            resolved.PlacedRefId,
                            resolved.Kind,
                            hit.SolidHitDistance);
                        return true;
                    }

                    return false;
                }

                hitEntity = hit.ProxyHitEntity;
                resolved = new ResolvedInteractionTarget(
                    proxyResolved.TargetEntity,
                    proxyResolved.PlacedRefId,
                    proxyResolved.Kind,
                    hit.ProxyHitDistance);
                return true;
            }

            if (!hasSolidHit)
                return false;

            hitEntity = hit.SolidHitEntity;
            if (!TryResolveEntity(entityManager, logicalRefLookup, hit.SolidHitEntity, out resolved))
                return false;

            resolved = new ResolvedInteractionTarget(
                resolved.TargetEntity,
                resolved.PlacedRefId,
                resolved.Kind,
                hit.SolidHitDistance);
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

        static bool SolidHitBlocksProxy(
            EntityManager entityManager,
            in LogicalRefLookup logicalRefLookup,
            Entity solidHitEntity,
            float solidHitFraction,
            Entity proxyTargetEntity,
            float proxyFraction)
        {
            const float FractionEpsilon = 0.0005f;
            if (solidHitFraction + FractionEpsilon >= proxyFraction)
                return false;

            if (TryResolveLogicalEntity(entityManager, logicalRefLookup, solidHitEntity, out Entity solidLogicalEntity)
                && solidLogicalEntity == proxyTargetEntity)
            {
                return false;
            }

            return true;
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
    [UpdateBefore(typeof(InteractionTargetResolutionSystem))]
    public partial class PlayerInteractionRaycastSystem : SystemBase
    {
        EntityQuery _viewQuery;
        EntityQuery _raycastHitQuery;

        protected override void OnCreate()
        {
            _viewQuery = GetEntityQuery(ComponentType.ReadOnly<PlayerPhysicsViewPose>());
            _raycastHitQuery = GetEntityQuery(ComponentType.ReadWrite<PlayerInteractionRaycastHit>());
            RequireForUpdate(_viewQuery);
            RequireForUpdate(_raycastHitQuery);
            RequireForUpdate<PhysicsWorldSingleton>();
        }

        protected override void OnUpdate()
        {
            var viewPose = _viewQuery.GetSingleton<PlayerPhysicsViewPose>();
            var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
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
