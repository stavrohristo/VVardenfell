using System;
using Unity.Collections;
using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.MorrowindScript
{
    [UpdateInGroup(typeof(MorrowindInputSystemGroup))]
    [UpdateBefore(typeof(MorrowindDialogueSessionSystem))]
    public partial class MorrowindScriptActiveExplicitRefLookupSystem : SystemBase
    {
        EntityQuery _logicalRefQuery;
        Entity _lookupEntity;

        protected override void OnCreate()
        {
            _logicalRefQuery = GetEntityQuery(
                ComponentType.ReadOnly<LogicalRefTag>(),
                ComponentType.ReadOnly<LogicalRefContent>(),
                ComponentType.ReadOnly<PlacedRefIdentity>(),
                ComponentType.ReadOnly<LogicalRefLocation>());

            _lookupEntity = EntityManager.CreateEntity(typeof(ActiveExplicitRefLookup));
            EntityManager.SetName(_lookupEntity, "VVardenfell.ActiveExplicitRefs");
            EntityManager.SetComponentData(_lookupEntity, new ActiveExplicitRefLookup
            {
                ByContentKey = new NativeParallelHashMap<int, ActiveExplicitRefTarget>(1024, Allocator.Persistent),
            });

            RequireForUpdate<ActiveExplicitRefLookup>();
            RequireForUpdate<LoadedCellsMap>();
        }

        protected override void OnDestroy()
        {
            if (_lookupEntity != Entity.Null && EntityManager.Exists(_lookupEntity))
            {
                var lookup = EntityManager.GetComponentData<ActiveExplicitRefLookup>(_lookupEntity);
                if (lookup.ByContentKey.IsCreated)
                    lookup.ByContentKey.Dispose();
            }
        }

        protected override void OnUpdate()
        {
            var lookup = EntityManager.GetComponentData<ActiveExplicitRefLookup>(_lookupEntity);
            if (!lookup.ByContentKey.IsCreated)
                throw new InvalidOperationException("[VVardenfell][MWScript] Active explicit reference lookup is not constructed.");

            int count = Math.Max(_logicalRefQuery.CalculateEntityCount(), 1024);
            if (lookup.ByContentKey.Capacity < count)
                lookup.ByContentKey.Capacity = count;

            lookup.ByContentKey.Clear();
            var loadedCells = SystemAPI.GetSingleton<LoadedCellsMap>();
            byte interiorActive = 0;
            ulong activeInteriorCellHash = 0UL;
            if (SystemAPI.TryGetSingleton<InteriorTransitionState>(out var transition) && transition.InteriorActive != 0)
            {
                interiorActive = 1;
                activeInteriorCellHash = transition.ActiveInteriorCellHash;
            }

            using var entities = _logicalRefQuery.ToEntityArray(Allocator.Temp);
            using var contents = _logicalRefQuery.ToComponentDataArray<LogicalRefContent>(Allocator.Temp);
            using var identities = _logicalRefQuery.ToComponentDataArray<PlacedRefIdentity>(Allocator.Temp);
            using var locations = _logicalRefQuery.ToComponentDataArray<LogicalRefLocation>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                uint placedRefId = identities[i].Value;
                if (placedRefId == 0u || !contents[i].Value.IsValid || !IsActive(locations[i], loadedCells, interiorActive, activeInteriorCellHash))
                    continue;

                int key = ActiveExplicitRefLookupUtility.Pack(contents[i].Value);
                if (!lookup.ByContentKey.TryGetValue(key, out var existing))
                {
                    lookup.ByContentKey.Add(key, new ActiveExplicitRefTarget
                    {
                        Entity = entities[i],
                        PlacedRefId = placedRefId,
                        Ambiguous = 0,
                    });
                    continue;
                }

                if (existing.PlacedRefId == placedRefId)
                    continue;

                existing.Ambiguous = 1;
                existing.Entity = Entity.Null;
                existing.PlacedRefId = 0u;
                lookup.ByContentKey[key] = existing;
            }

            EntityManager.SetComponentData(_lookupEntity, lookup);
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
