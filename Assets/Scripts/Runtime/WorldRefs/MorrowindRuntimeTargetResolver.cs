using System;
using Unity.Collections;
using Unity.Entities;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Streaming;

namespace VVardenfell.Runtime.WorldRefs
{
    public static class MorrowindRuntimeTargetResolver
    {
        static World s_PlayerQueryWorld;
        static EntityQuery s_PlayerQuery;
        static bool s_PlayerQueryCreated;
        static World s_ActorQueryWorld;
        static EntityQuery s_ActorQuery;
        static bool s_ActorQueryCreated;

        public static Entity ResolveLiveTarget(
            EntityManager entityManager,
            Entity targetEntity,
            uint targetPlacedRefId,
            in LogicalRefLookup lookup)
        {
            if (targetEntity != Entity.Null && entityManager.Exists(targetEntity))
                return targetEntity;

            if (targetPlacedRefId != 0u && lookup.Map.IsCreated && lookup.Map.TryGetValue(targetPlacedRefId, out Entity target))
                return target;

            return Entity.Null;
        }

        public static bool TryResolveExplicitRefTarget(
            ref RuntimeContentBlob content,
            ActiveExplicitRefLookup activeExplicitRefs,
            string target,
            out Entity targetEntity,
            out uint targetPlacedRefId)
        {
            targetEntity = Entity.Null;
            targetPlacedRefId = 0u;
            if (string.IsNullOrWhiteSpace(target))
                return false;

            ulong targetHash = RuntimeContentStableHash.HashId(target);
            if (RuntimeContentBlobUtility.TryGetExplicitRefTargetByIdHash(ref content, targetHash, out targetPlacedRefId) && targetPlacedRefId != 0u)
                return true;

            if (!RuntimeContentBlobUtility.TryResolvePlaceableByIdHash(ref content, targetHash, out var contentRef) || !RuntimeContentBlobUtility.IsValid(ref content, contentRef))
                return false;

            if (!activeExplicitRefs.ByContentKey.IsCreated)
                throw new InvalidOperationException($"[VVardenfell][WorldRefs] active explicit-ref lookup is not initialized for '{target}'.");

            int key = ActiveExplicitRefLookupUtility.Pack(contentRef);
            if (!activeExplicitRefs.ByContentKey.TryGetValue(key, out var activeTarget))
                throw new InvalidOperationException($"[VVardenfell][WorldRefs] explicit reference '{target}' resolved to content, but no active loaded ref was found.");

            if (activeTarget.Ambiguous != 0)
                throw new InvalidOperationException($"[VVardenfell][WorldRefs] explicit reference '{target}' resolved to multiple active loaded refs.");

            if (activeTarget.PlacedRefId == 0u || activeTarget.Entity == Entity.Null)
                throw new InvalidOperationException($"[VVardenfell][WorldRefs] explicit reference '{target}' resolved to content, but no active loaded ref was found.");

            targetEntity = activeTarget.Entity;
            targetPlacedRefId = activeTarget.PlacedRefId;
            return true;
        }

        public static bool TryResolveActorTarget(
            ref RuntimeContentBlob content,
            EntityManager entityManager,
            ActiveExplicitRefLookup activeExplicitRefs,
            string target,
            Entity defaultEntity,
            uint defaultPlacedRefId,
            bool allowPlayer,
            out Entity targetEntity,
            out uint targetPlacedRefId)
        {
            targetEntity = Entity.Null;
            targetPlacedRefId = 0u;
            if (string.IsNullOrWhiteSpace(target))
            {
                targetEntity = defaultEntity;
                targetPlacedRefId = defaultPlacedRefId;
                return targetEntity != Entity.Null
                       && entityManager.Exists(targetEntity)
                       && entityManager.HasComponent<ActorSpawnSource>(targetEntity);
            }

            if (allowPlayer && MorrowindCommandTextUtility.IsPlayerTarget(target))
            {
                targetEntity = ResolvePlayerEntity(entityManager);
                targetPlacedRefId = targetEntity != Entity.Null && entityManager.HasComponent<PlacedRefIdentity>(targetEntity)
                    ? entityManager.GetComponentData<PlacedRefIdentity>(targetEntity).Value
                    : 0u;
                return targetEntity != Entity.Null && entityManager.Exists(targetEntity);
            }

            if (TryResolveExplicitRefTarget(ref content, activeExplicitRefs, target, out targetEntity, out targetPlacedRefId))
                return targetEntity == Entity.Null || entityManager.Exists(targetEntity);

            return TryResolveUniqueActorById(ref content, entityManager, target, out targetEntity, out targetPlacedRefId);
        }

        public static bool TryResolveDefaultOrUniqueActorById(
            ref RuntimeContentBlob content,
            EntityManager entityManager,
            string target,
            Entity defaultEntity,
            uint defaultPlacedRefId,
            out Entity targetEntity,
            out uint targetPlacedRefId)
        {
            targetEntity = Entity.Null;
            targetPlacedRefId = 0u;
            if (string.IsNullOrWhiteSpace(target))
            {
                targetEntity = defaultEntity;
                targetPlacedRefId = defaultPlacedRefId;
                return targetEntity != Entity.Null
                       && entityManager.Exists(targetEntity)
                       && entityManager.HasComponent<ActorSpawnSource>(targetEntity);
            }

            return TryResolveUniqueActorById(ref content, entityManager, target, out targetEntity, out targetPlacedRefId);
        }

        public static Entity ResolvePlayerEntity(EntityManager entityManager)
        {
            EntityQuery query = GetPlayerQuery(entityManager);
            return query.CalculateEntityCount() == 1 ? query.GetSingletonEntity() : Entity.Null;
        }

        public static bool IsPlayerEntity(EntityManager entityManager, Entity entity)
        {
            return entity != Entity.Null
                   && entityManager.Exists(entity)
                   && entityManager.HasComponent<PlayerTag>(entity);
        }

        static bool TryResolveUniqueActorById(
            ref RuntimeContentBlob content,
            EntityManager entityManager,
            string target,
            out Entity targetEntity,
            out uint targetPlacedRefId)
        {
            targetEntity = Entity.Null;
            targetPlacedRefId = 0u;
            ulong targetHash = RuntimeContentStableHash.HashId(target);
            if (targetHash == 0UL)
                return false;

            Entity matchEntity = Entity.Null;
            uint matchPlacedRefId = 0u;
            int matchCount = 0;
            EntityQuery query = GetActorQuery(entityManager);
            using var entities = query.ToEntityArray(Allocator.Temp);
            using var sources = query.ToComponentDataArray<ActorSpawnSource>(Allocator.Temp);
            using var placedRefs = query.ToComponentDataArray<PlacedRefIdentity>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                ActorDefHandle actorHandle = sources[i].Definition;
                if (!actorHandle.IsValid)
                    continue;

                ref RuntimeActorDefBlob actor = ref RuntimeContentBlobUtility.Get(ref content, actorHandle);
                if (!ActorIdMatches(ref actor, targetHash))
                    continue;

                matchCount++;
                matchEntity = entities[i];
                matchPlacedRefId = placedRefs[i].Value;
                if (matchCount > 1)
                    return false;
            }

            if (matchCount != 1)
                return false;

            targetEntity = matchEntity;
            targetPlacedRefId = matchPlacedRefId;
            return true;
        }

        static EntityQuery GetPlayerQuery(EntityManager entityManager)
        {
            World world = entityManager.World;
            if (s_PlayerQueryCreated && s_PlayerQueryWorld == world)
                return s_PlayerQuery;

            if (s_PlayerQueryCreated)
                s_PlayerQuery.Dispose();

            s_PlayerQueryWorld = world;
            s_PlayerQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>());
            s_PlayerQueryCreated = true;
            return s_PlayerQuery;
        }

        static EntityQuery GetActorQuery(EntityManager entityManager)
        {
            World world = entityManager.World;
            if (s_ActorQueryCreated && s_ActorQueryWorld == world)
                return s_ActorQuery;

            if (s_ActorQueryCreated)
                s_ActorQuery.Dispose();

            s_ActorQueryWorld = world;
            s_ActorQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ActorSpawnSource>(),
                ComponentType.ReadOnly<PlacedRefIdentity>());
            s_ActorQueryCreated = true;
            return s_ActorQuery;
        }

        static bool ActorIdMatches(ref RuntimeActorDefBlob actor, ulong targetHash)
            => actor.IdHash == targetHash || actor.OriginalIdHash == targetHash;
    }
}
