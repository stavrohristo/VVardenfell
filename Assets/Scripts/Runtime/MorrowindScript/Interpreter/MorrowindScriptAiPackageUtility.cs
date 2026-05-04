using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VVardenfell.Core;
using VVardenfell.Runtime.AI;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Pathfinding;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.WorldRefs;

namespace VVardenfell.Runtime.MorrowindScript
{
    static class MorrowindScriptAiPackageUtility
    {
        public static bool TryApplyRequest(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            in MorrowindScriptAiPackageRequest request,
            in LogicalRefLookup lookup)
        {
            Entity target = MorrowindRuntimeTargetResolver.ResolveLiveTarget(entityManager, request.TargetEntity, request.TargetPlacedRefId, lookup);
            if (target == Entity.Null)
                return false;

            return TryApplyRequest(contentDb, entityManager, target, request);
        }

        public static bool TryApplyRequest(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            Entity target,
            in MorrowindScriptAiPackageRequest request)
        {
            if (contentDb == null || target == Entity.Null || !entityManager.Exists(target))
                return false;

            if (request.PackageType == (byte)MorrowindScriptAiPackageRequestType.StopCombat)
                return MorrowindCombatTargetUtility.TryStopCombat(entityManager, target);

            if (request.PackageType == (byte)MorrowindScriptAiPackageRequestType.StartCombat)
            {
                if (request.FollowTargetEntity == Entity.Null || !entityManager.Exists(request.FollowTargetEntity))
                    return false;

                return MorrowindCombatTargetUtility.TryStartCombat(
                    contentDb,
                    entityManager,
                    target,
                    request.TargetPlacedRefId,
                    request.FollowTargetEntity,
                    request.FollowTargetPlacedRefId);
            }

            if (!entityManager.HasComponent<ActorSpawnSource>(target))
                return true;

            EnsureActorAiComponents(contentDb, entityManager, target, request.TargetPlacedRefId);
            ResetTraversal(entityManager, target);

            var packages = entityManager.HasBuffer<ActorAiPackageRuntime>(target)
                ? entityManager.GetBuffer<ActorAiPackageRuntime>(target)
                : entityManager.AddBuffer<ActorAiPackageRuntime>(target);
            packages.Clear();
            packages.Add(new ActorAiPackageRuntime
            {
                Type = ResolvePackageType(request.PackageType),
                ShouldRepeat = request.ShouldRepeat,
                AllowPartial = request.AllowPartial,
                TargetPathGridIndex = -1,
                TargetPosition = request.TargetPosition,
                WanderRadius = math.max(0f, request.WanderRadius),
                IdleSeconds = math.max(0f, request.IdleSeconds),
                DurationHours = math.max(0f, request.DurationHours),
                RemainingDurationHours = math.max(0f, request.DurationHours),
                FollowTargetEntity = request.FollowTargetEntity,
                FollowTargetPlacedRefId = request.FollowTargetPlacedRefId,
                FollowDistance = math.max(0f, request.FollowDistance),
                DestinationInteriorCellHash = request.DestinationInteriorCellHash,
                IdleChance0 = request.IdleChance0,
                IdleChance1 = request.IdleChance1,
                IdleChance2 = request.IdleChance2,
                IdleChance3 = request.IdleChance3,
                IdleChance4 = request.IdleChance4,
                IdleChance5 = request.IdleChance5,
                IdleChance6 = request.IdleChance6,
                IdleChance7 = request.IdleChance7,
                TargetId = request.TargetId,
            });

            var aiState = entityManager.GetComponentData<ActorAiState>(target);
            aiState.CurrentPackageIndex = 0;
            aiState.CurrentNodeIndex = -1;
            aiState.GoalNodeIndex = -1;
            aiState.WaitUntilTime = 0f;
            aiState.FollowActive = 0;
            aiState.PendingIdleGroup = 0;
            aiState.ActiveIdleGroupHash = 0UL;
            aiState.Status = (byte)ActorAiPlannerStatus.Idle;
            if (entityManager.HasComponent<LocalTransform>(target))
                aiState.HomePosition = entityManager.GetComponentData<LocalTransform>(target).Position;
            entityManager.SetComponentData(target, aiState);
            return true;
        }

        public static void ClearActorPackages(EntityManager entityManager, Entity actor)
        {
            if (actor == Entity.Null || !entityManager.Exists(actor))
                return;

            if (entityManager.HasBuffer<ActorAiPackageRuntime>(actor))
                entityManager.GetBuffer<ActorAiPackageRuntime>(actor).Clear();

            if (entityManager.HasComponent<ActorAiState>(actor))
            {
                var aiState = entityManager.GetComponentData<ActorAiState>(actor);
                aiState.CurrentPackageIndex = 0;
                aiState.CurrentNodeIndex = -1;
                aiState.GoalNodeIndex = -1;
                aiState.WaitUntilTime = 0f;
                aiState.Status = (byte)ActorAiPlannerStatus.Complete;
                entityManager.SetComponentData(actor, aiState);
            }

            ResetTraversal(entityManager, actor);
        }

        static byte ResolvePackageType(byte packageType)
        {
            if (packageType == (byte)MorrowindScriptAiPackageRequestType.Travel)
                return (byte)ActorAiRuntimePackageType.Travel;
            if (packageType == (byte)MorrowindScriptAiPackageRequestType.Follow)
                return (byte)ActorAiRuntimePackageType.Follow;
            if (packageType == (byte)MorrowindScriptAiPackageRequestType.Escort)
                return (byte)ActorAiRuntimePackageType.Escort;
            if (packageType == (byte)MorrowindScriptAiPackageRequestType.Activate)
                return (byte)ActorAiRuntimePackageType.Activate;
            return (byte)ActorAiRuntimePackageType.Wander;
        }

        static void EnsureActorAiComponents(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            Entity actor,
            uint placedRefId)
        {
            if (!entityManager.HasComponent<ActorAiState>(actor))
            {
                float3 position = entityManager.HasComponent<LocalTransform>(actor)
                    ? entityManager.GetComponentData<LocalTransform>(actor).Position
                    : float3.zero;
                entityManager.AddComponentData(actor, new ActorAiState
                {
                    HomePosition = position,
                    CurrentNodeIndex = -1,
                    GoalNodeIndex = -1,
                    RandomSeed = BuildSeed(placedRefId, position),
                    Status = (byte)ActorAiPlannerStatus.Idle,
                });
            }

            if (!entityManager.HasComponent<ActorAiNavigationAnchor>(actor))
                entityManager.AddComponentData(actor, BuildAnchor(contentDb, entityManager, actor));
            if (!entityManager.HasComponent<ActorAiNavigationAnchorDirty>(actor))
                entityManager.AddComponent<ActorAiNavigationAnchorDirty>(actor);
            entityManager.SetComponentEnabled<ActorAiNavigationAnchorDirty>(actor, false);

            EnsureMovementComponents(contentDb, entityManager, actor);
        }

        static ActorAiNavigationAnchor BuildAnchor(RuntimeContentDatabase contentDb, EntityManager entityManager, Entity actor)
        {
            if (!entityManager.HasComponent<LogicalRefLocation>(actor))
                throw new InvalidOperationException("[VVardenfell][MWScript] Ai package target is missing LogicalRefLocation.");

            var location = entityManager.GetComponentData<LogicalRefLocation>(actor);
            if (location.IsInterior != 0)
            {
                var anchor = new ActorAiNavigationAnchor
                {
                    PathGridIndex = -1,
                    InteriorCellHash = location.InteriorCellHash,
                    IsInterior = 1,
                };
                if (contentDb.TryGetInteriorPathGridHandle(location.InteriorCellHash, out var handle) && handle.IsValid)
                {
                    anchor.PathGridIndex = handle.Index;
                    anchor.IsResolved = 1;
                }

                return anchor;
            }

            var exterior = new ActorAiNavigationAnchor
            {
                PathGridIndex = -1,
                GridX = location.ExteriorCell.x,
                GridY = location.ExteriorCell.y,
                IsInterior = 0,
            };
            if (contentDb.TryGetExteriorPathGridHandle(location.ExteriorCell.x, location.ExteriorCell.y, out var exteriorHandle) && exteriorHandle.IsValid)
            {
                exterior.PathGridIndex = exteriorHandle.Index;
                exterior.IsResolved = 1;
            }

            return exterior;
        }

        static void EnsureMovementComponents(RuntimeContentDatabase contentDb, EntityManager entityManager, Entity actor)
        {
            if (!entityManager.HasComponent<MorrowindMovementInput>(actor))
                entityManager.AddComponentData(actor, new MorrowindMovementInput());
            if (!entityManager.HasComponent<MorrowindMovementState>(actor))
                entityManager.AddComponentData(actor, new MorrowindMovementState { GroundNormal = math.up() });

            if (!entityManager.HasComponent<MorrowindMovementSpeed>(actor))
            {
                if (!entityManager.HasComponent<ActorSpawnSource>(actor)
                    || !entityManager.HasComponent<ActorAttributeSet>(actor)
                    || !entityManager.HasComponent<ActorSkillSet>(actor)
                    || !entityManager.HasComponent<ActorVitalSet>(actor)
                    || !entityManager.HasComponent<ActorEffectStatModifiers>(actor))
                {
                    throw new InvalidOperationException("[VVardenfell][MWScript] Ai package target cannot be made movable because actor stats are incomplete.");
                }

                var source = entityManager.GetComponentData<ActorSpawnSource>(actor);
                ref readonly var actorDef = ref contentDb.Get(source.Definition);
                var attributes = entityManager.GetComponentData<ActorAttributeSet>(actor);
                var skills = entityManager.GetComponentData<ActorSkillSet>(actor);
                var vitals = entityManager.GetComponentData<ActorVitalSet>(actor);
                var effects = entityManager.GetComponentData<ActorEffectStatModifiers>(actor);
                ActorDerivedMovementStats derived = entityManager.HasComponent<ActorDerivedMovementStats>(actor)
                    ? entityManager.GetComponentData<ActorDerivedMovementStats>(actor)
                    : MorrowindActorMovementStats.BuildDerived(contentDb, attributes, skills, vitals, effects, inventoryWeight: 0f);
                entityManager.AddComponentData(actor, MorrowindActorMovementStats.BuildMovementSpeed(contentDb, actorDef.Kind, attributes, skills, vitals, effects, derived));
            }

            if (!entityManager.HasComponent<PathGridTraversalState>(actor))
                entityManager.AddComponentData(actor, new PathGridTraversalState());
            if (!entityManager.HasComponent<PathGridTraversalPendingRequest>(actor))
                entityManager.AddComponentData(actor, new PathGridTraversalPendingRequest());
            entityManager.SetComponentEnabled<PathGridTraversalPendingRequest>(actor, false);
            if (!entityManager.HasComponent<PathGridTraversalAwaitingResult>(actor))
                entityManager.AddComponentData(actor, new PathGridTraversalAwaitingResult());
            entityManager.SetComponentEnabled<PathGridTraversalAwaitingResult>(actor, false);
            if (!entityManager.HasBuffer<PathGridTraversalNode>(actor))
                entityManager.AddBuffer<PathGridTraversalNode>(actor);
        }

        static void ResetTraversal(EntityManager entityManager, Entity actor)
        {
            if (entityManager.HasComponent<PathGridTraversalState>(actor))
                entityManager.SetComponentData(actor, new PathGridTraversalState());
            if (entityManager.HasComponent<PathGridTraversalPendingRequest>(actor))
                entityManager.SetComponentEnabled<PathGridTraversalPendingRequest>(actor, false);
            if (entityManager.HasComponent<PathGridTraversalAwaitingResult>(actor))
                entityManager.SetComponentEnabled<PathGridTraversalAwaitingResult>(actor, false);
            if (entityManager.HasBuffer<PathGridTraversalNode>(actor))
                entityManager.GetBuffer<PathGridTraversalNode>(actor).Clear();
        }

        static uint BuildSeed(uint placedRefId, float3 position)
        {
            uint seed = placedRefId != 0u ? placedRefId : 2166136261u;
            seed = (seed ^ math.asuint(position.x)) * 16777619u;
            seed = (seed ^ math.asuint(position.y)) * 16777619u;
            seed = (seed ^ math.asuint(position.z)) * 16777619u;
            return seed == 0u ? 1u : seed;
        }
    }
}
