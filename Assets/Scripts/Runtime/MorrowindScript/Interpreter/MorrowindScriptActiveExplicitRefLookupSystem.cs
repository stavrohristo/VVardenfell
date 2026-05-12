using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;
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
        static readonly ProfilerMarker k_FullRebuild = new("VV.MWScript.ActiveExplicitRef.FullRebuild");
        static readonly ProfilerMarker k_ConsumeEvents = new("VV.MWScript.ActiveExplicitRef.ConsumeEvents");
        static readonly ProfilerCounterValue<int> k_ChangedSectionCount = new(ProfilerCategory.Scripts, "VV.MWScript.ActiveExplicitRef.ChangedSectionCount", ProfilerMarkerDataUnit.Count);
        static readonly ProfilerCounterValue<int> k_DirtyKeyCount = new(ProfilerCategory.Scripts, "VV.MWScript.ActiveExplicitRef.DirtyKeyCount", ProfilerMarkerDataUnit.Count);

        EntityQuery _workQuery;
        EntityQuery _sessionQuery;
        NativeParallelHashSet<int> _dirtyKeysScratch;

        public void OnCreate(ref SystemState systemState)
        {
            _workQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<ActiveExplicitRefLookup>(),
                ComponentType.ReadOnly<ActiveExplicitRefLookupWorkPending>(),
                ComponentType.ReadOnly<ActiveExplicitRefLookupBuildState>(),
                ComponentType.ReadOnly<ActiveExplicitSectionChange>(),
                ComponentType.ReadOnly<ActiveExplicitDynamicRefChange>());

            _sessionQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<ActiveExplicitRefLookup>(),
                ComponentType.ReadOnly<SessionTeardown>());

            systemState.RequireAnyForUpdate(_workQuery, _sessionQuery);
            _dirtyKeysScratch = new NativeParallelHashSet<int>(1024, Allocator.Persistent);
        }

        public void OnDestroy(ref SystemState systemState)
        {
            ActiveExplicitRefLookupLifecycleUtility.DisposeAll(systemState.EntityManager);
            if (_dirtyKeysScratch.IsCreated)
                _dirtyKeysScratch.Dispose();
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

            if (!SystemAPI.IsComponentEnabled<ActiveExplicitRefLookupWorkPending>(lookupEntity))
                return;

            var lookup = SystemAPI.GetSingleton<ActiveExplicitRefLookup>();
            var buildState = SystemAPI.GetComponent<ActiveExplicitRefLookupBuildState>(lookupEntity);
            bool repaired = EnsureLookupContainers(ref lookup);
            bool fullRebuild = repaired
                               || buildState.HasBuilt == 0
                               || SystemAPI.IsComponentEnabled<ActiveExplicitRefLookupFullRebuild>(lookupEntity);

            if (fullRebuild)
            {
                using (k_FullRebuild.Auto())
                {
                    RebuildAllTargetsFromWorldBlob(ref lookup);
                    RebuildActiveTargets(ref systemState, ref lookup);
                }
            }
            else
            {
                using (k_ConsumeEvents.Auto())
                {
                    ProcessEvents(ref systemState, lookupEntity, ref lookup);
                }
            }

            systemState.EntityManager.SetComponentData(lookupEntity, lookup);
            ClearEventsAndDisableWork(ref systemState, lookupEntity);
            systemState.EntityManager.SetComponentData(lookupEntity, BuildCurrentState(ref systemState));
        }

        void RebuildAllTargetsFromWorldBlob(ref ActiveExplicitRefLookup lookup)
        {
            if (!SystemAPI.TryGetSingleton<RuntimeWorldCellBlobReference>(out var worldCellReference) || !worldCellReference.Blob.IsCreated)
                throw new InvalidOperationException("[VVardenfell][MWScript] Active explicit-ref bootstrap rebuild requires RuntimeWorldCellBlobReference.");

            ref RuntimeWorldCellBlob worldCells = ref worldCellReference.Blob.Value;
            int allCount = Math.Max(worldCells.Refs.Length, 1024);
            if (lookup.AllByContentKey.Capacity < allCount)
                lookup.AllByContentKey.Capacity = allCount;
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

                AddExplicitRefTarget(lookup.AllByContentKey, ActiveExplicitRefLookupUtility.Pack(content), Entity.Null, entry.PlacedRefId);
            }
        }

        void RebuildActiveTargets(ref SystemState systemState, ref ActiveExplicitRefLookup lookup)
        {
            if (!SystemAPI.TryGetSingleton<LoadedCellsMap>(out var loadedCells))
                throw new InvalidOperationException("[VVardenfell][MWScript] Active explicit-ref bootstrap rebuild requires LoadedCellsMap.");
            if (!SystemAPI.TryGetSingleton<RuntimeSectionRegistry>(out var sectionRegistry))
                throw new InvalidOperationException("[VVardenfell][MWScript] Active explicit-ref bootstrap rebuild requires RuntimeSectionRegistry.");

            lookup.ByContentKey.Clear();
            lookup.ActiveEntriesByContentKey.Clear();
            lookup.ActiveDynamicEntriesByEntity.Clear();
            lookup.ActiveExteriorCells.Clear();

            byte interiorActive = 0;
            ulong activeInteriorCellHash = 0UL;
            if (SystemAPI.TryGetSingleton<InteriorTransitionState>(out var transition) && transition.InteriorActive != 0)
            {
                interiorActive = 1;
                activeInteriorCellHash = transition.ActiveInteriorCellHash;
            }

            if (interiorActive != 0)
                AddActiveInteriorSection(systemState.EntityManager, sectionRegistry, activeInteriorCellHash, ref lookup, updateFinal: true);
            else
                AddActiveExteriorSections(systemState.EntityManager, sectionRegistry, loadedCells, ref lookup, updateFinal: true);

            AddCurrentDynamicActiveEntries(ref systemState, loadedCells, interiorActive, activeInteriorCellHash, ref lookup, updateFinal: true);
        }

        void ProcessEvents(ref SystemState systemState, Entity lookupEntity, ref ActiveExplicitRefLookup lookup)
        {
            DynamicBuffer<ActiveExplicitSectionChange> sectionChanges = SystemAPI.GetBuffer<ActiveExplicitSectionChange>(lookupEntity);
            DynamicBuffer<ActiveExplicitDynamicRefChange> dynamicChanges = SystemAPI.GetBuffer<ActiveExplicitDynamicRefChange>(lookupEntity);
            int dirtyKeyCapacity = Math.Max(sectionChanges.Length * 32 + dynamicChanges.Length * 2, 64);
            if (_dirtyKeysScratch.Capacity < dirtyKeyCapacity)
                _dirtyKeysScratch.Capacity = dirtyKeyCapacity;
            _dirtyKeysScratch.Clear();

            int changedSections = 0;
            for (int i = 0; i < sectionChanges.Length; i++)
            {
                var change = sectionChanges[i];
                if (change.Section == Entity.Null || !systemState.EntityManager.Exists(change.Section))
                    throw new InvalidOperationException("[VVardenfell][MWScript] active explicit-ref section event references a missing section.");
                if (change.Activate != 0)
                    AddSectionActiveEntries(systemState.EntityManager, change.Section, ref lookup, _dirtyKeysScratch, updateFinal: false);
                else
                    RemoveSectionActiveEntries(systemState.EntityManager, change.Section, ref lookup, _dirtyKeysScratch);
                UpdateTrackedExteriorCell(systemState.EntityManager, change.Section, change.Activate != 0, ref lookup);
                changedSections++;
            }

            for (int i = 0; i < dynamicChanges.Length; i++)
                ApplyDynamicChange(systemState.EntityManager, dynamicChanges[i], ref lookup, _dirtyKeysScratch);

            RebuildFinalTargetsForDirtyKeys(ref lookup, _dirtyKeysScratch);
            k_ChangedSectionCount.Value = changedSections;
            k_DirtyKeyCount.Value = _dirtyKeysScratch.Count();
        }

        static void ApplyDynamicChange(
            EntityManager em,
            in ActiveExplicitDynamicRefChange change,
            ref ActiveExplicitRefLookup lookup,
            NativeParallelHashSet<int> dirtyKeys)
        {
            if (change.ContentKey == 0 || change.PlacedRefId == 0u)
                throw new InvalidOperationException("[VVardenfell][MWScript] active explicit-ref dynamic event has invalid key/id.");
            if (change.Operation != (byte)ActiveExplicitDynamicRefOperation.Add
                && change.Operation != (byte)ActiveExplicitDynamicRefOperation.Remove
                && change.Operation != (byte)ActiveExplicitDynamicRefOperation.Move)
            {
                throw new InvalidOperationException($"[VVardenfell][MWScript] active explicit-ref dynamic event has invalid operation {change.Operation}.");
            }

            if (change.WasActive != 0 || lookup.ActiveDynamicEntriesByEntity.ContainsKey(change.Entity))
            {
                if (lookup.ActiveDynamicEntriesByEntity.TryGetValue(change.Entity, out var previous))
                {
                    RemoveActiveEntry(ref lookup, previous.Key, change.Entity, previous.PlacedRefId);
                    lookup.ActiveDynamicEntriesByEntity.Remove(change.Entity);
                    dirtyKeys.Add(previous.Key);
                }
                else
                {
                    RemoveActiveEntry(ref lookup, change.ContentKey, change.Entity, change.PlacedRefId);
                    dirtyKeys.Add(change.ContentKey);
                }
            }

            if (change.IsActive == 0)
                return;

            if (change.Entity == Entity.Null || !em.Exists(change.Entity))
                throw new InvalidOperationException("[VVardenfell][MWScript] active explicit-ref dynamic add references a missing entity.");
            AddActiveEntry(ref lookup, change.ContentKey, new ActiveExplicitRefTarget
            {
                Entity = change.Entity,
                PlacedRefId = change.PlacedRefId,
                Ambiguous = 0,
            });
            lookup.ActiveDynamicEntriesByEntity[change.Entity] = new ActiveExplicitDynamicRefEntry
            {
                Key = change.ContentKey,
                PlacedRefId = change.PlacedRefId,
            };
            dirtyKeys.Add(change.ContentKey);
        }

        static void AddActiveInteriorSection(
            EntityManager em,
            in RuntimeSectionRegistry sectionRegistry,
            ulong activeInteriorCellHash,
            ref ActiveExplicitRefLookup lookup,
            bool updateFinal)
        {
            if (activeInteriorCellHash == 0UL)
                return;
            if (!sectionRegistry.InteriorSectionsByHash.IsCreated)
                throw new InvalidOperationException("[VVardenfell][MWScript] active interior explicit-ref rebuild requires interior section registry.");
            if (!sectionRegistry.InteriorSectionsByHash.TryGetValue(activeInteriorCellHash, out Entity sectionEntity)
                || sectionEntity == Entity.Null
                || !em.Exists(sectionEntity))
            {
                throw new InvalidOperationException("[VVardenfell][MWScript] active interior section is missing from RuntimeSectionRegistry.");
            }

            AddSectionActiveEntries(em, sectionEntity, ref lookup, default, updateFinal);
        }

        static void AddActiveExteriorSections(
            EntityManager em,
            in RuntimeSectionRegistry sectionRegistry,
            in LoadedCellsMap loadedCells,
            ref ActiveExplicitRefLookup lookup,
            bool updateFinal)
        {
            if (!loadedCells.Active.IsCreated || !sectionRegistry.ExteriorSections.IsCreated)
                throw new InvalidOperationException("[VVardenfell][MWScript] active exterior explicit-ref rebuild requires loaded active cells and exterior section registry.");

            var activeEnumerator = loadedCells.Active.GetEnumerator();
            while (activeEnumerator.MoveNext())
            {
                int2 coord = activeEnumerator.Current;
                Entity sectionEntity = RequireExteriorSection(em, sectionRegistry, coord);
                lookup.ActiveExteriorCells[coord] = 1;
                AddSectionActiveEntries(em, sectionEntity, ref lookup, default, updateFinal);
            }
        }

        void AddCurrentDynamicActiveEntries(
            ref SystemState systemState,
            in LoadedCellsMap loadedCells,
            byte interiorActive,
            ulong activeInteriorCellHash,
            ref ActiveExplicitRefLookup lookup,
            bool updateFinal)
        {
            foreach (var (content, identity, location, entity) in
                     SystemAPI.Query<RefRO<LogicalRefContent>, RefRO<PlacedRefIdentity>, RefRO<LogicalRefLocation>>()
                         .WithAll<LogicalRefTag>()
                         .WithNone<RuntimeCellSectionMember>()
                         .WithEntityAccess())
            {
                uint placedRefId = identity.ValueRO.Value;
                if (placedRefId == 0u || !content.ValueRO.Value.IsValid)
                    continue;
                if (!IsActive(location.ValueRO, loadedCells, interiorActive, activeInteriorCellHash))
                    continue;

                int key = ActiveExplicitRefLookupUtility.Pack(content.ValueRO.Value);
                AddActiveEntry(ref lookup, key, new ActiveExplicitRefTarget
                {
                    Entity = entity,
                    PlacedRefId = placedRefId,
                    Ambiguous = 0,
                });
                lookup.ActiveDynamicEntriesByEntity[entity] = new ActiveExplicitDynamicRefEntry
                {
                    Key = key,
                    PlacedRefId = placedRefId,
                };
                if (updateFinal)
                    AddExplicitRefTarget(lookup.ByContentKey, key, entity, placedRefId);
            }
        }

        static void AddSectionActiveEntries(
            EntityManager em,
            Entity sectionEntity,
            ref ActiveExplicitRefLookup lookup,
            NativeParallelHashSet<int> dirtyKeys,
            bool updateFinal)
        {
            var entries = RequireExplicitRefBuffer(em, sectionEntity);
            for (int i = 0; i < entries.Length; i++)
            {
                var entry = RequireValidExplicitRefEntry(em, entries[i]);
                AddActiveEntry(ref lookup, entry.ContentKey, new ActiveExplicitRefTarget
                {
                    Entity = entry.Entity,
                    PlacedRefId = entry.PlacedRefId,
                    Ambiguous = 0,
                });
                if (updateFinal)
                    AddExplicitRefTarget(lookup.ByContentKey, entry.ContentKey, entry.Entity, entry.PlacedRefId);
                else
                    dirtyKeys.Add(entry.ContentKey);
            }
        }

        static void RemoveSectionActiveEntries(
            EntityManager em,
            Entity sectionEntity,
            ref ActiveExplicitRefLookup lookup,
            NativeParallelHashSet<int> dirtyKeys)
        {
            var entries = RequireExplicitRefBuffer(em, sectionEntity);
            for (int i = 0; i < entries.Length; i++)
            {
                var entry = RequireValidExplicitRefEntry(em, entries[i]);
                RemoveActiveEntry(ref lookup, entry.ContentKey, entry.Entity, entry.PlacedRefId);
                dirtyKeys.Add(entry.ContentKey);
            }
        }

        static void UpdateTrackedExteriorCell(EntityManager em, Entity sectionEntity, bool activate, ref ActiveExplicitRefLookup lookup)
        {
            if (!em.HasComponent<RuntimeCellSectionHeader>(sectionEntity))
                throw new InvalidOperationException("[VVardenfell][MWScript] active explicit-ref section event references a section without a header.");

            var header = em.GetComponentData<RuntimeCellSectionHeader>(sectionEntity);
            if (header.IsInterior != 0)
                return;

            var coord = new int2(header.GridX, header.GridY);
            if (activate)
                lookup.ActiveExteriorCells[coord] = 1;
            else
                lookup.ActiveExteriorCells.Remove(coord);
        }

        static bool EnsureLookupContainers(ref ActiveExplicitRefLookup lookup)
        {
            bool repaired = false;
            if (!lookup.ByContentKey.IsCreated)
            {
                lookup.ByContentKey = new NativeParallelHashMap<int, ActiveExplicitRefTarget>(1024, Allocator.Persistent);
                repaired = true;
            }
            if (!lookup.AllByContentKey.IsCreated)
            {
                lookup.AllByContentKey = new NativeParallelHashMap<int, ActiveExplicitRefTarget>(1024, Allocator.Persistent);
                repaired = true;
            }
            if (!lookup.ActiveEntriesByContentKey.IsCreated)
            {
                lookup.ActiveEntriesByContentKey = new NativeParallelMultiHashMap<int, ActiveExplicitRefTarget>(1024, Allocator.Persistent);
                repaired = true;
            }
            if (!lookup.ActiveDynamicEntriesByEntity.IsCreated)
            {
                lookup.ActiveDynamicEntriesByEntity = new NativeParallelHashMap<Entity, ActiveExplicitDynamicRefEntry>(1024, Allocator.Persistent);
                repaired = true;
            }
            if (!lookup.ActiveExteriorCells.IsCreated)
            {
                lookup.ActiveExteriorCells = new NativeParallelHashMap<int2, byte>(1024, Allocator.Persistent);
                repaired = true;
            }
            return repaired;
        }

        static Entity RequireExteriorSection(EntityManager em, in RuntimeSectionRegistry sectionRegistry, int2 coord)
        {
            if (!sectionRegistry.ExteriorSections.TryGetValue(coord, out Entity sectionEntity)
                || sectionEntity == Entity.Null
                || !em.Exists(sectionEntity))
            {
                throw new InvalidOperationException($"[VVardenfell][MWScript] active exterior cell ({coord.x},{coord.y}) is missing from RuntimeSectionRegistry.");
            }

            return sectionEntity;
        }

        static RuntimeCellSectionExplicitRefEntry RequireValidExplicitRefEntry(EntityManager em, RuntimeCellSectionExplicitRefEntry entry)
        {
            if (entry.ContentKey == 0 || entry.PlacedRefId == 0u)
                throw new InvalidOperationException("[VVardenfell][MWScript] section explicit-ref buffer contains an invalid key/id; rebake required.");
            if (entry.Entity == Entity.Null || !em.Exists(entry.Entity))
                throw new InvalidOperationException("[VVardenfell][MWScript] section explicit-ref buffer references a missing entity; rebake required.");
            return entry;
        }

        static void AddActiveEntry(ref ActiveExplicitRefLookup lookup, int key, ActiveExplicitRefTarget target)
            => lookup.ActiveEntriesByContentKey.Add(key, target);

        static void RemoveActiveEntry(ref ActiveExplicitRefLookup lookup, int key, Entity entity, uint placedRefId)
        {
            if (!lookup.ActiveEntriesByContentKey.TryGetFirstValue(key, out var target, out var iterator))
                throw new InvalidOperationException("[VVardenfell][MWScript] active explicit-ref entry is missing from the active entry index.");

            do
            {
                if (target.PlacedRefId == placedRefId && target.Entity == entity)
                {
                    lookup.ActiveEntriesByContentKey.Remove(iterator);
                    return;
                }
            }
            while (lookup.ActiveEntriesByContentKey.TryGetNextValue(out target, ref iterator));

            throw new InvalidOperationException("[VVardenfell][MWScript] active explicit-ref entry index is missing the expected target.");
        }

        static void RebuildFinalTargetsForDirtyKeys(
            ref ActiveExplicitRefLookup lookup,
            NativeParallelHashSet<int> dirtyKeys)
        {
            var dirtyEnumerator = dirtyKeys.GetEnumerator();
            while (dirtyEnumerator.MoveNext())
                RebuildFinalTargetForKey(ref lookup, dirtyEnumerator.Current);
        }

        static void RebuildFinalTargetForKey(ref ActiveExplicitRefLookup lookup, int key)
        {
            lookup.ByContentKey.Remove(key);
            if (!lookup.ActiveEntriesByContentKey.TryGetFirstValue(key, out var target, out var iterator))
                return;

            AddExplicitRefTarget(lookup.ByContentKey, key, target.Entity, target.PlacedRefId);
            while (lookup.ActiveEntriesByContentKey.TryGetNextValue(out target, ref iterator))
                AddExplicitRefTarget(lookup.ByContentKey, key, target.Entity, target.PlacedRefId);
        }

        static DynamicBuffer<RuntimeCellSectionExplicitRefEntry> RequireExplicitRefBuffer(EntityManager em, Entity sectionEntity)
        {
            if (!em.HasBuffer<RuntimeCellSectionExplicitRefEntry>(sectionEntity))
                throw new InvalidOperationException("[VVardenfell][MWScript] active section root is missing explicit-ref entry buffer; rebake required.");
            return em.GetBuffer<RuntimeCellSectionExplicitRefEntry>(sectionEntity);
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

        ActiveExplicitRefLookupBuildState BuildCurrentState(ref SystemState systemState)
        {
            var state = new ActiveExplicitRefLookupBuildState
            {
                HasBuilt = 1,
                LastEntityCount = 0,
                LastOrderVersion = 0,
            };
            if (SystemAPI.TryGetSingleton<LoadedCellsMap>(out var loadedCells))
                state.LastActiveRevision = loadedCells.ActiveRevision;
            if (SystemAPI.TryGetSingleton<InteriorTransitionState>(out var transition) && transition.InteriorActive != 0)
            {
                state.LastInteriorActive = 1;
                state.LastActiveInteriorCellHash = transition.ActiveInteriorCellHash;
            }
            return state;
        }

        void ClearEventsAndDisableWork(ref SystemState systemState, Entity lookupEntity)
        {
            SystemAPI.GetBuffer<ActiveExplicitSectionChange>(lookupEntity).Clear();
            SystemAPI.GetBuffer<ActiveExplicitDynamicRefChange>(lookupEntity).Clear();
            systemState.EntityManager.SetComponentEnabled<ActiveExplicitRefLookupWorkPending>(lookupEntity, false);
            systemState.EntityManager.SetComponentEnabled<ActiveExplicitRefLookupFullRebuild>(lookupEntity, false);
            if (systemState.EntityManager.HasComponent<ActiveExplicitRefLookupDirty>(lookupEntity))
                systemState.EntityManager.SetComponentEnabled<ActiveExplicitRefLookupDirty>(lookupEntity, false);
        }
    }
}
