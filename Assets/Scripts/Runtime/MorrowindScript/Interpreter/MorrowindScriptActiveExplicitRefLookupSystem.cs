using System;
using Unity.Collections;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.WorldRefs;

namespace VVardenfell.Runtime.MorrowindScript
{
    [UpdateInGroup(typeof(MorrowindGameplayMutationSystemGroup))]
    [UpdateBefore(typeof(MorrowindScriptInterpreterSystem))]
    public partial struct MorrowindScriptActiveExplicitRefLookupSystem : ISystem
    {
        EntityQuery _logicalRefQuery;
        EntityQuery _dirtyQuery;
        EntityQuery _sessionQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _logicalRefQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<LogicalRefTag>(),
                ComponentType.ReadOnly<LogicalRefContent>(),
                ComponentType.ReadOnly<PlacedRefIdentity>(),
                ComponentType.ReadOnly<LogicalRefLocation>());

            _dirtyQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<ActiveExplicitRefLookup>(),
                ComponentType.ReadOnly<ActiveExplicitRefLookupDirty>(),
                ComponentType.ReadOnly<ActiveExplicitRefLookupBuildState>());

            _sessionQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<ActiveExplicitRefLookup>(),
                ComponentType.ReadOnly<SessionTeardown>());

            systemState.RequireAnyForUpdate(_dirtyQuery, _sessionQuery);
        }

        public void OnDestroy(ref SystemState systemState)
        {
            ActiveExplicitRefLookupLifecycleUtility.DisposeAll(systemState.EntityManager);
        }

        public void OnUpdate(ref SystemState systemState)
        {
            Entity lookupEntity = SystemAPI.GetSingletonEntity<ActiveExplicitRefLookup>();
            if (SystemAPI.IsComponentEnabled<SessionTeardown>(lookupEntity))
            {
                ActiveExplicitRefLookupLifecycleUtility.Dispose(systemState.EntityManager, lookupEntity);
                systemState.EntityManager.DestroyEntity(lookupEntity);
                return;
            }
            if (!SystemAPI.IsComponentEnabled<ActiveExplicitRefLookupDirty>(lookupEntity))
                return;

            var lookup = SystemAPI.GetSingleton<ActiveExplicitRefLookup>();
            if (!SystemAPI.TryGetSingleton<LoadedCellsMap>(out var loadedCells))
                throw new InvalidOperationException("[VVardenfell][MWScript] Active explicit-ref lookup rebuild requires LoadedCellsMap.");
            if (!SystemAPI.TryGetSingleton<RuntimeWorldCellBlobReference>(out var worldCellReference) || !worldCellReference.Blob.IsCreated)
                throw new InvalidOperationException("[VVardenfell][MWScript] Active explicit-ref lookup rebuild requires RuntimeWorldCellBlobReference.");
            ref RuntimeWorldCellBlob worldCells = ref worldCellReference.Blob.Value;

            byte interiorActive = 0;
            ulong activeInteriorCellHash = 0UL;
            if (SystemAPI.TryGetSingleton<InteriorTransitionState>(out var transition) && transition.InteriorActive != 0)
            {
                interiorActive = 1;
                activeInteriorCellHash = transition.ActiveInteriorCellHash;
            }

            int entityCount = _logicalRefQuery.CalculateEntityCount();
            int orderVersion = _logicalRefQuery.GetCombinedComponentOrderVersion(includeEntityType: true);
            int count = Math.Max(entityCount + worldCells.Refs.Length, 1024);
            if (lookup.ByContentKey.Capacity < count)
                lookup.ByContentKey.Capacity = count;
            if (lookup.AllByContentKey.Capacity < count)
                lookup.AllByContentKey.Capacity = count;

            lookup.ByContentKey.Clear();
            lookup.AllByContentKey.Clear();

            for (int i = 0; i < worldCells.Refs.Length; i++)
            {
                RefEntry entry = worldCells.Refs[i];
                if (entry.PlacedRefId == 0u || entry.ContentHandleValue <= 0 || entry.ContentKind <= 0)
                    continue;

                var content = new ContentReference
                {
                    Kind = (ContentReferenceKind)(byte)entry.ContentKind,
                    HandleValue = entry.ContentHandleValue,
                };
                if (!content.IsValid)
                    continue;

                int key = ActiveExplicitRefLookupUtility.Pack(content);
                AddExplicitRefTarget(lookup.AllByContentKey, key, Entity.Null, entry.PlacedRefId);
            }

            foreach (var (content, identity, location, entity) in
                     SystemAPI.Query<RefRO<LogicalRefContent>, RefRO<PlacedRefIdentity>, RefRO<LogicalRefLocation>>()
                         .WithAll<LogicalRefTag>()
                         .WithEntityAccess())
            {
                uint placedRefId = identity.ValueRO.Value;
                if (placedRefId == 0u || !content.ValueRO.Value.IsValid)
                    continue;

                int key = ActiveExplicitRefLookupUtility.Pack(content.ValueRO.Value);
                AddExplicitRefTarget(lookup.AllByContentKey, key, entity, placedRefId);
                if (!IsActive(location.ValueRO, loadedCells, interiorActive, activeInteriorCellHash))
                    continue;

                AddExplicitRefTarget(lookup.ByContentKey, key, entity, placedRefId);
            }

            systemState.EntityManager.SetComponentData(lookupEntity, lookup);
            systemState.EntityManager.SetComponentEnabled<ActiveExplicitRefLookupDirty>(lookupEntity, false);
            systemState.EntityManager.SetComponentData(lookupEntity, new ActiveExplicitRefLookupBuildState
            {
                HasBuilt = 1,
                LastActiveRevision = loadedCells.ActiveRevision,
                LastActiveInteriorCellHash = activeInteriorCellHash,
                LastEntityCount = entityCount,
                LastOrderVersion = orderVersion,
                LastInteriorActive = interiorActive,
            });
        }

        static void AddExplicitRefTarget(
            NativeParallelHashMap<int, ActiveExplicitRefTarget> targets,
            int key,
            Entity entity,
            uint placedRefId)
        {
            if (!targets.TryGetValue(key, out var existing))
            {
                targets.Add(key, new ActiveExplicitRefTarget
                {
                    Entity = entity,
                    PlacedRefId = placedRefId,
                    Ambiguous = 0,
                });
                return;
            }

            if (existing.PlacedRefId == placedRefId)
            {
                if (existing.Entity == Entity.Null && entity != Entity.Null)
                {
                    existing.Entity = entity;
                    targets[key] = existing;
                }

                return;
            }

            existing.Ambiguous = 1;
            targets[key] = existing;
        }

        static bool IsActive(
            in LogicalRefLocation location,
            in LoadedCellsMap loadedCells,
            byte interiorActive,
            ulong activeInteriorCellHash)
        {
            if (interiorActive != 0)
                return location.IsInterior != 0 && location.InteriorCellHash == activeInteriorCellHash;

            return location.IsInterior == 0 && loadedCells.Active.IsCreated && loadedCells.Active.Contains(location.ExteriorCell);
        }
    }
}
