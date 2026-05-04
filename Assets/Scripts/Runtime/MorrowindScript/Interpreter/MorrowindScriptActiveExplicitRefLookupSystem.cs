using System;
using Unity.Collections;
using Unity.Entities;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.WorldRefs;

namespace VVardenfell.Runtime.MorrowindScript
{
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateBefore(typeof(MorrowindDialogueSessionSystem))]
    public partial class MorrowindScriptActiveExplicitRefLookupSystem : SystemBase
    {
        EntityQuery _logicalRefQuery;
        EntityQuery _dirtyQuery;
        EntityQuery _sessionQuery;

        protected override void OnCreate()
        {
            _logicalRefQuery = GetEntityQuery(
                ComponentType.ReadOnly<LogicalRefTag>(),
                ComponentType.ReadOnly<LogicalRefContent>(),
                ComponentType.ReadOnly<PlacedRefIdentity>(),
                ComponentType.ReadOnly<LogicalRefLocation>());

            _dirtyQuery = GetEntityQuery(
                ComponentType.ReadOnly<ActiveExplicitRefLookup>(),
                ComponentType.ReadOnly<ActiveExplicitRefLookupDirty>(),
                ComponentType.ReadOnly<ActiveExplicitRefLookupBuildState>());

            _sessionQuery = GetEntityQuery(
                ComponentType.ReadOnly<ActiveExplicitRefLookup>(),
                ComponentType.ReadOnly<SessionTeardown>());

            RequireAnyForUpdate(_dirtyQuery, _sessionQuery);
        }

        protected override void OnDestroy()
        {
            ActiveExplicitRefLookupLifecycleUtility.DisposeAll(EntityManager);
        }

        protected override void OnUpdate()
        {
            Entity lookupEntity = SystemAPI.GetSingletonEntity<ActiveExplicitRefLookup>();
            if (SystemAPI.IsComponentEnabled<SessionTeardown>(lookupEntity))
            {
                ActiveExplicitRefLookupLifecycleUtility.Dispose(EntityManager, lookupEntity);
                EntityManager.DestroyEntity(lookupEntity);
                return;
            }
            if (!SystemAPI.IsComponentEnabled<ActiveExplicitRefLookupDirty>(lookupEntity))
                return;

            var lookup = SystemAPI.GetSingleton<ActiveExplicitRefLookup>();
            if (!SystemAPI.TryGetSingleton<LoadedCellsMap>(out var loadedCells))
                throw new InvalidOperationException("[VVardenfell][MWScript] Active explicit-ref lookup rebuild requires LoadedCellsMap.");

            byte interiorActive = 0;
            ulong activeInteriorCellHash = 0UL;
            if (SystemAPI.TryGetSingleton<InteriorTransitionState>(out var transition) && transition.InteriorActive != 0)
            {
                interiorActive = 1;
                activeInteriorCellHash = transition.ActiveInteriorCellHash;
            }

            int entityCount = _logicalRefQuery.CalculateEntityCount();
            int orderVersion = _logicalRefQuery.GetCombinedComponentOrderVersion(includeEntityType: true);
            int count = Math.Max(entityCount, 1024);
            if (lookup.ByContentKey.Capacity < count)
                lookup.ByContentKey.Capacity = count;
            if (lookup.AllByContentKey.Capacity < count)
                lookup.AllByContentKey.Capacity = count;

            lookup.ByContentKey.Clear();
            lookup.AllByContentKey.Clear();

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

            EntityManager.SetComponentData(lookupEntity, lookup);
            EntityManager.SetComponentEnabled<ActiveExplicitRefLookupDirty>(lookupEntity, false);
            EntityManager.SetComponentData(lookupEntity, new ActiveExplicitRefLookupBuildState
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
                return;

            existing.Ambiguous = 1;
            existing.Entity = Entity.Null;
            existing.PlacedRefId = 0u;
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
