using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.WorldRefs;

namespace VVardenfell.Runtime.WorldState
{
    static class ScriptVisibleSaveStateUtility
    {
        const float TransformEpsilon = 0.0001f;

        public struct ProjectionStats
        {
            public int CandidateCount;
            public int OverlayHitCount;
            public int OverlayMissCount;
            public int ProjectionMarkerStructuralCount;
        }

        public static void EnsureRuntimeBuffers(EntityManager entityManager, Entity runtimeEntity)
        {
            if (!entityManager.HasBuffer<PlacedRefOverlayState>(runtimeEntity))
                entityManager.AddBuffer<PlacedRefOverlayState>(runtimeEntity);
            if (!entityManager.HasBuffer<PlacedRefOverlayScriptInstance>(runtimeEntity))
                entityManager.AddBuffer<PlacedRefOverlayScriptInstance>(runtimeEntity);
            if (!entityManager.HasBuffer<PlacedRefOverlayScriptLocalValue>(runtimeEntity))
                entityManager.AddBuffer<PlacedRefOverlayScriptLocalValue>(runtimeEntity);
            if (!entityManager.HasBuffer<PlacedRefOverlayActorInventoryItem>(runtimeEntity))
                entityManager.AddBuffer<PlacedRefOverlayActorInventoryItem>(runtimeEntity);
            if (!entityManager.HasBuffer<PlacedRefOverlayContainerItem>(runtimeEntity))
                entityManager.AddBuffer<PlacedRefOverlayContainerItem>(runtimeEntity);
            if (!entityManager.HasComponent<PlacedRefOverlayRuntimeIndex>(runtimeEntity))
            {
                entityManager.AddComponentData(runtimeEntity, PlacedRefOverlayRuntimeIndex.Create(1024));
            }
            else
            {
                var index = entityManager.GetComponentData<PlacedRefOverlayRuntimeIndex>(runtimeEntity);
                if (!index.IsCreated)
                    entityManager.SetComponentData(runtimeEntity, PlacedRefOverlayRuntimeIndex.Create(1024));
            }

            if (!entityManager.HasComponent<PlacedRefOverlayIndexDirty>(runtimeEntity))
            {
                entityManager.AddComponent<PlacedRefOverlayIndexDirty>(runtimeEntity);
                entityManager.SetComponentEnabled<PlacedRefOverlayIndexDirty>(runtimeEntity, true);
            }
        }

        public static void ClearRuntimeOverlay(EntityManager entityManager, Entity runtimeEntity)
        {
            EnsureRuntimeBuffers(entityManager, runtimeEntity);
            entityManager.GetBuffer<PlacedRefOverlayState>(runtimeEntity).Clear();
            entityManager.GetBuffer<PlacedRefOverlayScriptInstance>(runtimeEntity).Clear();
            entityManager.GetBuffer<PlacedRefOverlayScriptLocalValue>(runtimeEntity).Clear();
            entityManager.GetBuffer<PlacedRefOverlayActorInventoryItem>(runtimeEntity).Clear();
            entityManager.GetBuffer<PlacedRefOverlayContainerItem>(runtimeEntity).Clear();
            ClearOverlayIndex(entityManager, runtimeEntity);

            Entity stateLookupEntity = WorldStateEntityQueryUtility.GetSingletonEntity<PlacedRefRuntimeStateLookup>(entityManager);
            if (stateLookupEntity == Entity.Null)
                return;

            var lookup = entityManager.GetComponentData<PlacedRefRuntimeStateLookup>(stateLookupEntity);
            if (lookup.DisabledByPlacedRef.IsCreated)
                lookup.DisabledByPlacedRef.Clear();
            entityManager.SetComponentData(stateLookupEntity, lookup);
        }

        public static void UpsertRemoved(EntityManager entityManager, uint placedRefId, ContentReference content, byte removed)
        {
            if (placedRefId == 0u)
                throw new InvalidOperationException("[VVardenfell][Save] Cannot save removed state for placed ref 0.");

            Entity runtimeEntity = RequireRuntimeEntity(entityManager);
            var states = entityManager.GetBuffer<PlacedRefOverlayState>(runtimeEntity);
            int index = FindStateIndex(entityManager, runtimeEntity, states, placedRefId);
            var state = index >= 0 ? states[index] : new PlacedRefOverlayState { PlacedRefId = placedRefId };
            state.HasRemoved = 1;
            state.Removed = removed;
            state.RemovedContent = content;
            if (removed != 0)
            {
                state.HasDisabled = 1;
                state.Disabled = 1;
            }
            UpsertState(entityManager, runtimeEntity, states, index, state);
        }

        public static void UpsertDisabled(EntityManager entityManager, uint placedRefId, byte disabled)
        {
            if (placedRefId == 0u)
                throw new InvalidOperationException("[VVardenfell][Save] Cannot save disabled state for placed ref 0.");

            Entity runtimeEntity = RequireRuntimeEntity(entityManager);
            var states = entityManager.GetBuffer<PlacedRefOverlayState>(runtimeEntity);
            int index = FindStateIndex(entityManager, runtimeEntity, states, placedRefId);
            var state = index >= 0 ? states[index] : new PlacedRefOverlayState { PlacedRefId = placedRefId };
            state.HasDisabled = 1;
            state.Disabled = disabled;
            UpsertState(entityManager, runtimeEntity, states, index, state);
        }

        public static void ReplaceContainerItems(
            EntityManager entityManager,
            uint placedRefId,
            DynamicBuffer<ContainerSessionItem> sessionItems)
        {
            if (placedRefId == 0u)
                throw new InvalidOperationException("[VVardenfell][Save] Cannot save container state for placed ref 0.");

            Entity runtimeEntity = RequireRuntimeEntity(entityManager);
            var states = entityManager.GetBuffer<PlacedRefOverlayState>(runtimeEntity);
            int index = FindStateIndex(entityManager, runtimeEntity, states, placedRefId);
            var state = index >= 0 ? states[index] : new PlacedRefOverlayState { PlacedRefId = placedRefId };
            state.HasContainer = 1;
            UpsertState(entityManager, runtimeEntity, states, index, state);

            var overlayItems = entityManager.GetBuffer<PlacedRefOverlayContainerItem>(runtimeEntity);
            RemoveContainerItems(overlayItems, placedRefId);
            for (int i = 0; i < sessionItems.Length; i++)
            {
                var item = sessionItems[i];
                if (item.PlacedRefId != placedRefId)
                    continue;
                if (item.Count <= 0 || !item.Content.IsValid)
                    throw new InvalidOperationException($"[VVardenfell][Save] Container 0x{placedRefId:X8} has an invalid live session item at index {i}.");

                overlayItems.Add(new PlacedRefOverlayContainerItem
                {
                    PlacedRefId = placedRefId,
                    Item = item,
                });
            }
            MarkOverlayIndexDirty(entityManager, runtimeEntity);
        }

        public static void ApplyContainerOverlay(
            EntityManager entityManager,
            uint placedRefId,
            DynamicBuffer<ContainerSessionItem> sessionItems)
        {
            Entity runtimeEntity = WorldStateEntityQueryUtility.GetSingletonEntity<MorrowindScriptRuntimeState>(entityManager);
            if (runtimeEntity == Entity.Null)
                return;

            EnsureRuntimeBuffers(entityManager, runtimeEntity);
            var states = entityManager.GetBuffer<PlacedRefOverlayState>(runtimeEntity);
            EnsureOverlayIndexCurrent(entityManager, runtimeEntity);
            int stateIndex = FindStateIndex(entityManager, runtimeEntity, states, placedRefId);
            if (stateIndex < 0 || states[stateIndex].HasContainer == 0)
                return;

            for (int i = sessionItems.Length - 1; i >= 0; i--)
            {
                if (sessionItems[i].PlacedRefId == placedRefId)
                    sessionItems.RemoveAt(i);
            }

            var overlayItems = entityManager.GetBuffer<PlacedRefOverlayContainerItem>(runtimeEntity);
            var overlayIndex = entityManager.GetComponentData<PlacedRefOverlayRuntimeIndex>(runtimeEntity);
            if (overlayIndex.IsCreated
                && overlayIndex.ContainerItemsByPlacedRefId.TryGetFirstValue(placedRefId, out int itemIndex, out var iterator))
            {
                do
                {
                    sessionItems.Add(overlayItems[itemIndex].Item);
                }
                while (overlayIndex.ContainerItemsByPlacedRefId.TryGetNextValue(out itemIndex, ref iterator));
                return;
            }

            for (int i = 0; i < overlayItems.Length; i++)
            {
                if (overlayItems[i].PlacedRefId != placedRefId)
                    continue;

                sessionItems.Add(overlayItems[i].Item);
            }
        }

        public static void RebuildPickedItemsFromOverlay(EntityManager entityManager, DynamicBuffer<PickedItemRecord> pickedItems)
        {
            pickedItems.Clear();
            Entity runtimeEntity = WorldStateEntityQueryUtility.GetSingletonEntity<MorrowindScriptRuntimeState>(entityManager);
            if (runtimeEntity == Entity.Null)
                return;

            EnsureRuntimeBuffers(entityManager, runtimeEntity);
            var states = entityManager.GetBuffer<PlacedRefOverlayState>(runtimeEntity);
            for (int i = 0; i < states.Length; i++)
            {
                var state = states[i];
                if (state.HasRemoved == 0 || state.Removed == 0 || state.PlacedRefId == 0u)
                    continue;

                pickedItems.Add(new PickedItemRecord
                {
                    PlacedRefId = state.PlacedRefId,
                    Definition = state.RemovedContent.Kind == ContentReferenceKind.Item
                        ? new ItemDefHandle { Value = state.RemovedContent.HandleValue }
                        : default,
                });
            }
        }

        public static void UpsertLock(EntityManager entityManager, uint placedRefId, in PlacedRefLockState lockState)
        {
            if (placedRefId == 0u)
                throw new InvalidOperationException("[VVardenfell][Save] Cannot save lock state for placed ref 0.");

            Entity runtimeEntity = RequireRuntimeEntity(entityManager);
            var states = entityManager.GetBuffer<PlacedRefOverlayState>(runtimeEntity);
            int index = FindStateIndex(entityManager, runtimeEntity, states, placedRefId);
            var state = index >= 0 ? states[index] : new PlacedRefOverlayState { PlacedRefId = placedRefId };
            state.HasLock = 1;
            state.LockLevel = lockState.LockLevel;
            state.Locked = lockState.Locked;
            state.KeyId = lockState.KeyId;
            state.TrapId = lockState.TrapId;
            UpsertState(entityManager, runtimeEntity, states, index, state);
        }

        public static void UpsertTransform(EntityManager entityManager, Entity entity, uint placedRefId)
        {
            if (placedRefId == 0u)
                throw new InvalidOperationException("[VVardenfell][Save] Cannot save transform state for placed ref 0.");
            if (entity == Entity.Null || !entityManager.Exists(entity))
                throw new InvalidOperationException($"[VVardenfell][Save] Cannot save transform state for unloaded placed ref 0x{placedRefId:X8} without an overlay value.");
            if (!entityManager.HasComponent<LocalTransform>(entity))
                throw new InvalidOperationException($"[VVardenfell][Save] Placed ref 0x{placedRefId:X8} has no LocalTransform to save.");
            if (!entityManager.HasComponent<LogicalRefLocation>(entity))
                throw new InvalidOperationException($"[VVardenfell][Save] Placed ref 0x{placedRefId:X8} has no LogicalRefLocation to save.");

            var transform = entityManager.GetComponentData<LocalTransform>(entity);
            var location = entityManager.GetComponentData<LogicalRefLocation>(entity);
            UpsertTransform(entityManager, placedRefId, transform.Position, transform.Rotation, transform.Scale, location);
        }

        public static void UpsertTransform(
            EntityManager entityManager,
            uint placedRefId,
            float3 position,
            quaternion rotation,
            float scale,
            in LogicalRefLocation location)
        {
            Entity runtimeEntity = RequireRuntimeEntity(entityManager);
            var states = entityManager.GetBuffer<PlacedRefOverlayState>(runtimeEntity);
            int index = FindStateIndex(entityManager, runtimeEntity, states, placedRefId);
            var state = index >= 0 ? states[index] : new PlacedRefOverlayState { PlacedRefId = placedRefId };
            state.HasTransform = 1;
            state.Position = position;
            state.Rotation = rotation;
            state.Scale = scale;
            state.ExteriorCell = location.ExteriorCell;
            state.InteriorCellId = location.InteriorCellId;
            state.InteriorCellHash = location.InteriorCellHash;
            state.IsInterior = location.IsInterior;
            UpsertState(entityManager, runtimeEntity, states, index, state);
        }

        public static void UpsertObjectScript(EntityManager entityManager, Entity entity, uint placedRefId)
        {
            if (placedRefId == 0u)
                throw new InvalidOperationException("[VVardenfell][Save] Cannot save object script state for placed ref 0.");
            if (!entityManager.HasComponent<MorrowindScriptInstance>(entity))
                return;

            Entity runtimeEntity = RequireRuntimeEntity(entityManager);
            var instance = entityManager.GetComponentData<MorrowindScriptInstance>(entity);
            var instances = entityManager.GetBuffer<PlacedRefOverlayScriptInstance>(runtimeEntity);
            int instanceIndex = FindScriptInstanceIndex(instances, placedRefId, instance.ProgramIndex);
            var saved = new PlacedRefOverlayScriptInstance
            {
                PlacedRefId = placedRefId,
                ProgramIndex = instance.ProgramIndex,
                ProgramCounter = instance.ProgramCounter,
                Status = instance.Status,
                SuppressActivation = instance.SuppressActivation,
                DisabledReason = instance.DisabledReason,
            };
            if (instanceIndex >= 0)
                instances[instanceIndex] = saved;
            else
                instances.Add(saved);
            MarkOverlayIndexDirty(entityManager, runtimeEntity);

            var locals = entityManager.HasBuffer<MorrowindScriptLocalValue>(entity)
                ? entityManager.GetBuffer<MorrowindScriptLocalValue>(entity)
                : default;
            var savedLocals = entityManager.GetBuffer<PlacedRefOverlayScriptLocalValue>(runtimeEntity);
            RemoveScriptLocals(savedLocals, placedRefId, instance.ProgramIndex);
            if (locals.IsCreated)
            {
                for (int i = 0; i < locals.Length; i++)
                {
                    savedLocals.Add(new PlacedRefOverlayScriptLocalValue
                    {
                        PlacedRefId = placedRefId,
                        ProgramIndex = instance.ProgramIndex,
                        LocalIndex = i,
                        Value = locals[i],
                    });
                }
            }
            MarkOverlayIndexDirty(entityManager, runtimeEntity);
        }

        public static void ReplaceActorInventory(EntityManager entityManager, Entity entity, uint placedRefId)
        {
            if (placedRefId == 0u)
                throw new InvalidOperationException("[VVardenfell][Save] Cannot save actor inventory for placed ref 0.");
            if (!entityManager.HasBuffer<ActorInventoryItem>(entity))
                throw new InvalidOperationException($"[VVardenfell][Save] Placed ref 0x{placedRefId:X8} has no actor inventory to save.");

            Entity runtimeEntity = RequireRuntimeEntity(entityManager);
            var saved = entityManager.GetBuffer<PlacedRefOverlayActorInventoryItem>(runtimeEntity);
            RemoveActorInventory(saved, placedRefId);
            var inventory = entityManager.GetBuffer<ActorInventoryItem>(entity);
            for (int i = 0; i < inventory.Length; i++)
            {
                saved.Add(new PlacedRefOverlayActorInventoryItem
                {
                    PlacedRefId = placedRefId,
                    Item = inventory[i],
                });
            }
            MarkOverlayIndexDirty(entityManager, runtimeEntity);
        }

        public static bool TryProjectSavedStateForLiveRefs(EntityManager entityManager, out string error)
            => TryProjectSavedStateForLiveRefs(entityManager, out error, out _);

        public static bool TryPrepareProjection(
            EntityManager entityManager,
            out Entity runtimeEntity,
            out BlobAssetReference<RuntimeContentBlob> contentBlob,
            out PlacedRefOverlayRuntimeIndex overlayIndex,
            out string error)
        {
            error = null;
            runtimeEntity = WorldStateEntityQueryUtility.GetSingletonEntity<MorrowindScriptRuntimeState>(entityManager);
            contentBlob = default;
            overlayIndex = default;
            if (runtimeEntity == Entity.Null)
                return true;

            EnsureRuntimeBuffers(entityManager, runtimeEntity);
            EnsureOverlayIndexCurrent(entityManager, runtimeEntity);
            contentBlob = RequireRuntimeContentBlob(entityManager);
            overlayIndex = entityManager.GetComponentData<PlacedRefOverlayRuntimeIndex>(runtimeEntity);
            return true;
        }

        public static bool TryProjectLiveRef(
            EntityManager entityManager,
            ref EntityCommandBuffer structuralEcb,
            Entity runtimeEntity,
            Entity entity,
            uint placedRefId,
            ref RuntimeContentBlob content,
            in PlacedRefOverlayRuntimeIndex overlayIndex,
            ref ProjectionStats stats,
            out string error)
        {
            error = null;
            stats.CandidateCount++;
            if (placedRefId == 0u)
            {
                error = "Live logical ref has placed ref id 0 during save-state projection.";
                return false;
            }

            bool overlayHit = false;
            var states = entityManager.GetBuffer<PlacedRefOverlayState>(runtimeEntity);
            int stateIndex = FindStateIndex(entityManager, runtimeEntity, states, placedRefId);
            if (stateIndex >= 0)
            {
                overlayHit = true;
                var savedState = states[stateIndex];
                ProjectState(entityManager, ref structuralEcb, entity, savedState);
            }

            var scriptInstances = entityManager.GetBuffer<PlacedRefOverlayScriptInstance>(runtimeEntity);
            var scriptLocals = entityManager.GetBuffer<PlacedRefOverlayScriptLocalValue>(runtimeEntity);
            if (!ProjectScript(ref content, entityManager, entity, placedRefId, scriptInstances, scriptLocals, overlayIndex, out error))
                return false;
            overlayHit = overlayHit || HasScriptOverlay(scriptInstances, overlayIndex, placedRefId);

            var inventories = entityManager.GetBuffer<PlacedRefOverlayActorInventoryItem>(runtimeEntity);
            bool inventoryHit = ProjectActorInventory(entityManager, entity, placedRefId, inventories, overlayIndex);
            overlayHit = overlayHit || inventoryHit;
            if (entityManager.HasComponent<PlacedRefOverlayProjectionApplied>(entity))
            {
                entityManager.SetComponentEnabled<PlacedRefOverlayProjectionApplied>(entity, true);
            }
            else
            {
                structuralEcb.AddComponent<PlacedRefOverlayProjectionApplied>(entity);
                stats.ProjectionMarkerStructuralCount++;
            }

            if (overlayHit)
                stats.OverlayHitCount++;
            else
                stats.OverlayMissCount++;
            return true;
        }

        public static void ResolveGlobalScriptTargetsForProjection(EntityManager entityManager)
            => ResolveGlobalScriptTargets(entityManager);

        public static bool TryProjectSavedStateForLiveRefs(EntityManager entityManager, out string error, out ProjectionStats stats)
        {
            error = null;
            stats = default;
            Entity runtimeEntity = WorldStateEntityQueryUtility.GetSingletonEntity<MorrowindScriptRuntimeState>(entityManager);
            if (runtimeEntity == Entity.Null)
                return true;

            EnsureRuntimeBuffers(entityManager, runtimeEntity);
            EnsureOverlayIndexCurrent(entityManager, runtimeEntity);
            var contentBlob = RequireRuntimeContentBlob(entityManager);
            ref RuntimeContentBlob content = ref contentBlob.Value;

            EntityQuery query = LogicalRefProjectionQueryCache.Get(entityManager);
            var overlayIndex = entityManager.GetComponentData<PlacedRefOverlayRuntimeIndex>(runtimeEntity);
            using var entities = query.ToEntityArray(Allocator.Temp);
            using var identities = query.ToComponentDataArray<PlacedRefIdentity>(Allocator.Temp);
            var projectionMarkerEcb = new EntityCommandBuffer(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                if (entityManager.HasComponent<PlacedRefOverlayProjectionApplied>(entity))
                    continue;

                uint placedRefId = identities[i].Value;
                stats.CandidateCount++;
                if (placedRefId == 0u)
                {
                    error = "Live logical ref has placed ref id 0 during save-state projection.";
                    projectionMarkerEcb.Dispose();
                    return false;
                }

                bool overlayHit = false;
                var states = entityManager.GetBuffer<PlacedRefOverlayState>(runtimeEntity);
                int stateIndex = FindStateIndex(entityManager, runtimeEntity, states, placedRefId);
                if (stateIndex >= 0)
                {
                    overlayHit = true;
                    var savedState = states[stateIndex];
                    ProjectState(entityManager, ref projectionMarkerEcb, entity, savedState);
                }

                var scriptInstances = entityManager.GetBuffer<PlacedRefOverlayScriptInstance>(runtimeEntity);
                var scriptLocals = entityManager.GetBuffer<PlacedRefOverlayScriptLocalValue>(runtimeEntity);
                if (!ProjectScript(ref content, entityManager, entity, placedRefId, scriptInstances, scriptLocals, overlayIndex, out error))
                {
                    projectionMarkerEcb.Dispose();
                    return false;
                }
                overlayHit = overlayHit || HasScriptOverlay(scriptInstances, overlayIndex, placedRefId);

                var inventories = entityManager.GetBuffer<PlacedRefOverlayActorInventoryItem>(runtimeEntity);
                bool inventoryHit = ProjectActorInventory(entityManager, entity, placedRefId, inventories, overlayIndex);
                overlayHit = overlayHit || inventoryHit;
                if (entityManager.HasComponent<PlacedRefOverlayProjectionApplied>(entity))
                {
                    entityManager.SetComponentEnabled<PlacedRefOverlayProjectionApplied>(entity, true);
                }
                else
                {
                    projectionMarkerEcb.AddComponent<PlacedRefOverlayProjectionApplied>(entity);
                    stats.ProjectionMarkerStructuralCount++;
                }

                if (overlayHit)
                    stats.OverlayHitCount++;
                else
                    stats.OverlayMissCount++;
            }

            projectionMarkerEcb.Playback(entityManager);
            projectionMarkerEcb.Dispose();
            ResolveGlobalScriptTargets(entityManager);
            return true;
        }

        static void ResolveGlobalScriptTargets(EntityManager entityManager)
        {
            Entity lookupEntity = WorldStateEntityQueryUtility.GetSingletonEntity<LogicalRefLookup>(entityManager);
            if (lookupEntity == Entity.Null)
                return;

            var lookup = entityManager.GetComponentData<LogicalRefLookup>(lookupEntity);
            if (!lookup.Map.IsCreated)
                return;

            EntityQuery query = GlobalScriptQueryCache.Get(entityManager);
            using var entities = query.ToEntityArray(Allocator.Temp);
            using var globals = query.ToComponentDataArray<MorrowindGlobalScriptInstance>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                var global = globals[i];
                if (global.TargetPlacedRefId == 0u)
                    continue;

                if (!lookup.Map.TryGetValue(global.TargetPlacedRefId, out Entity target) || !entityManager.Exists(target))
                {
                    global.TargetEntity = Entity.Null;
                    entityManager.SetComponentData(entities[i], global);
                    continue;
                }

                global.TargetEntity = target;
                entityManager.SetComponentData(entities[i], global);
            }
        }

        static void ProjectState(EntityManager entityManager, ref EntityCommandBuffer ecb, Entity entity, in PlacedRefOverlayState saved)
        {
            byte effectiveDisabled = saved.HasRemoved != 0 && saved.Removed != 0
                ? (byte)1
                : saved.HasDisabled != 0 ? saved.Disabled : (byte)0;

            if (saved.HasRemoved != 0 || saved.HasDisabled != 0)
            {
                if (!entityManager.HasComponent<PlacedRefRuntimeState>(entity))
                    throw new InvalidOperationException($"[VVardenfell][Save] Placed ref 0x{saved.PlacedRefId:X8} has no PlacedRefRuntimeState.");

                entityManager.SetComponentData(entity, new PlacedRefRuntimeState { Disabled = effectiveDisabled });
                Entity stateLookupEntity = WorldStateEntityQueryUtility.GetSingletonEntity<PlacedRefRuntimeStateLookup>(entityManager);
                if (stateLookupEntity != Entity.Null)
                {
                    var lookup = entityManager.GetComponentData<PlacedRefRuntimeStateLookup>(stateLookupEntity);
                    if (lookup.DisabledByPlacedRef.IsCreated)
                    {
                        lookup.DisabledByPlacedRef[saved.PlacedRefId] = effectiveDisabled;
                        entityManager.SetComponentData(stateLookupEntity, lookup);
                    }
                }

                if (effectiveDisabled != 0)
                    ProjectLogicalRefInactive(entityManager, ref ecb, entity);
            }

            if (saved.HasLock != 0)
            {
                var lockState = new PlacedRefLockState
                {
                    LockLevel = saved.LockLevel,
                    Locked = saved.Locked,
                    KeyId = saved.KeyId,
                    TrapId = saved.TrapId,
                };
                if (entityManager.HasComponent<PlacedRefLockState>(entity))
                    entityManager.SetComponentData(entity, lockState);
                else
                    ecb.AddComponent(entity, lockState);
            }

            if (saved.HasTransform != 0)
            {
                if (!entityManager.HasComponent<LocalTransform>(entity))
                    throw new InvalidOperationException($"[VVardenfell][Save] Placed ref 0x{saved.PlacedRefId:X8} has no LocalTransform.");

                var transform = LocalTransform.FromPositionRotationScale(saved.Position, saved.Rotation, math.max(0.0001f, saved.Scale));
                entityManager.SetComponentData(entity, transform);
                if (entityManager.HasComponent<LocalToWorld>(entity))
                {
                    entityManager.SetComponentData(entity, new LocalToWorld
                    {
                        Value = float4x4.TRS(transform.Position, transform.Rotation, new float3(transform.Scale)),
                    });
                }

                var location = new LogicalRefLocation
                {
                    ExteriorCell = saved.ExteriorCell,
                    InteriorCellId = saved.InteriorCellId,
                    InteriorCellHash = saved.IsInterior != 0 && saved.InteriorCellHash == 0UL
                        ? InteriorCellIdHash.Hash(saved.InteriorCellId)
                        : saved.InteriorCellHash,
                    IsInterior = saved.IsInterior,
                };
                if (entityManager.HasComponent<LogicalRefLocation>(entity))
                    entityManager.SetComponentData(entity, location);
                else
                    ecb.AddComponent(entity, location);

                if (location.IsInterior != 0)
                {
                    if (!entityManager.HasComponent<InteriorCellMember>(entity))
                        ecb.AddComponent<InteriorCellMember>(entity);
                    if (entityManager.HasComponent<CellLink>(entity))
                        ecb.RemoveComponent<CellLink>(entity);
                }
                else
                {
                    if (entityManager.HasComponent<InteriorCellMember>(entity))
                        ecb.RemoveComponent<InteriorCellMember>(entity);
                    if (entityManager.HasComponent<CellLink>(entity))
                        entityManager.SetComponentData(entity, new CellLink { Value = location.ExteriorCell });
                    else
                        ecb.AddComponent(entity, new CellLink { Value = location.ExteriorCell });
                }
            }
        }

        static void ProjectLogicalRefInactive(EntityManager entityManager, ref EntityCommandBuffer ecb, Entity logicalEntity)
        {
            ActiveExplicitRefLookupLifecycleUtility.QueueDynamicRemoveIfTrackedOrActive(entityManager, logicalEntity);
            ProjectEntityInactive(entityManager, ref ecb, logicalEntity, entityManager.HasComponent<ActorSpawnSource>(logicalEntity));

            if (!entityManager.HasBuffer<LogicalRefChild>(logicalEntity))
                return;

            bool isActor = entityManager.HasComponent<ActorSpawnSource>(logicalEntity);
            var children = entityManager.GetBuffer<LogicalRefChild>(logicalEntity);
            for (int i = 0; i < children.Length; i++)
            {
                Entity child = children[i].Value;
                if (child == Entity.Null || !entityManager.Exists(child))
                    continue;

                ProjectEntityInactive(entityManager, ref ecb, child, isActor);
            }
        }

        static void ProjectEntityInactive(EntityManager entityManager, ref EntityCommandBuffer ecb, Entity entity, bool isActorRoot)
        {
            if (entityManager.HasComponent<ActorRenderVisible>(entity))
                entityManager.SetComponentEnabled<ActorRenderVisible>(entity, false);

            if (entityManager.HasComponent<ActorShadowCasterVisible>(entity))
                entityManager.SetComponentEnabled<ActorShadowCasterVisible>(entity, false);

            if (entityManager.HasComponent<MaterialMeshInfo>(entity) && !isActorRoot)
                entityManager.SetComponentEnabled<MaterialMeshInfo>(entity, false);

            if (entityManager.HasComponent<RuntimeColliderSource>(entity))
                RuntimeColliderAttachmentUtility.QueueDisablePhysics(entityManager, ref ecb, entity);
        }

        static bool ProjectScript(
            ref RuntimeContentBlob content,
            EntityManager entityManager,
            Entity entity,
            uint placedRefId,
            DynamicBuffer<PlacedRefOverlayScriptInstance> scriptInstances,
            DynamicBuffer<PlacedRefOverlayScriptLocalValue> scriptLocals,
            in PlacedRefOverlayRuntimeIndex overlayIndex,
            out string error)
        {
            error = null;
            int firstInstanceIndex = FindFirstScriptInstanceIndex(scriptInstances, overlayIndex, placedRefId);
            if (firstInstanceIndex < 0)
                return true;

            if (!entityManager.HasComponent<MorrowindScriptInstance>(entity))
            {
                error = $"Placed ref 0x{placedRefId:X8} has saved script state but no live script instance.";
                return false;
            }

            var instance = entityManager.GetComponentData<MorrowindScriptInstance>(entity);
            var saved = scriptInstances[firstInstanceIndex];
            if (instance.ProgramIndex != saved.ProgramIndex)
            {
                error = $"Placed ref 0x{placedRefId:X8} script program mismatch: live={instance.ProgramIndex} save={saved.ProgramIndex}.";
                return false;
            }

            if ((uint)saved.ProgramIndex >= (uint)content.MorrowindScriptPrograms.Length)
            {
                error = $"Placed ref 0x{placedRefId:X8} save references invalid script program index {saved.ProgramIndex}.";
                return false;
            }

            ref RuntimeMorrowindScriptProgramDefBlob program = ref content.MorrowindScriptPrograms[saved.ProgramIndex];
            if (!entityManager.HasBuffer<MorrowindScriptLocalValue>(entity) && program.LocalCount > 0)
            {
                error = $"Placed ref 0x{placedRefId:X8} has saved script locals but no live local buffer.";
                return false;
            }

            instance.ProgramCounter = saved.ProgramCounter;
            instance.Status = saved.Status;
            instance.SuppressActivation = saved.SuppressActivation;
            instance.DisabledReason = saved.DisabledReason;
            entityManager.SetComponentData(entity, instance);

            if (!entityManager.HasBuffer<MorrowindScriptLocalValue>(entity))
                return true;

            var locals = entityManager.GetBuffer<MorrowindScriptLocalValue>(entity);
            if (locals.Length != program.LocalCount)
            {
                error = $"Placed ref 0x{placedRefId:X8} local count mismatch: live={locals.Length} saveProgram={program.LocalCount}.";
                return false;
            }

            if (overlayIndex.IsCreated
                && overlayIndex.ScriptLocalsByPlacedRefId.TryGetFirstValue(placedRefId, out int localIndex, out var iterator))
            {
                do
                {
                    if (!ProjectScriptLocal(scriptLocals[localIndex], saved.ProgramIndex, locals, placedRefId, out error))
                        return false;
                }
                while (overlayIndex.ScriptLocalsByPlacedRefId.TryGetNextValue(out localIndex, ref iterator));

                return true;
            }

            for (int i = 0; i < scriptLocals.Length; i++)
            {
                var local = scriptLocals[i];
                if (local.PlacedRefId != placedRefId || local.ProgramIndex != saved.ProgramIndex)
                    continue;

                if (!ProjectScriptLocal(local, saved.ProgramIndex, locals, placedRefId, out error))
                    return false;
            }

            return true;
        }

        static bool ProjectScriptLocal(
            in PlacedRefOverlayScriptLocalValue local,
            int programIndex,
            DynamicBuffer<MorrowindScriptLocalValue> locals,
            uint placedRefId,
            out string error)
        {
            if (local.ProgramIndex != programIndex)
            {
                error = null;
                return true;
            }

            if ((uint)local.LocalIndex >= (uint)locals.Length)
            {
                error = $"Placed ref 0x{placedRefId:X8} saved local index {local.LocalIndex} is outside live count {locals.Length}.";
                return false;
            }

            locals[local.LocalIndex] = local.Value;
            error = null;
            return true;
        }

        static bool ProjectActorInventory(
            EntityManager entityManager,
            Entity entity,
            uint placedRefId,
            DynamicBuffer<PlacedRefOverlayActorInventoryItem> savedItems,
            in PlacedRefOverlayRuntimeIndex overlayIndex)
        {
            int firstIndex = FindFirstActorInventoryIndex(savedItems, overlayIndex, placedRefId);
            if (firstIndex < 0)
                return false;

            if (!entityManager.HasBuffer<ActorInventoryItem>(entity))
                throw new InvalidOperationException($"[VVardenfell][Save] Placed ref 0x{placedRefId:X8} has saved actor inventory but no live ActorInventoryItem buffer.");

            var inventory = entityManager.GetBuffer<ActorInventoryItem>(entity);
            inventory.Clear();
            if (overlayIndex.IsCreated
                && overlayIndex.ActorInventoryByPlacedRefId.TryGetFirstValue(placedRefId, out int itemIndex, out var iterator))
            {
                do
                {
                    var item = savedItems[itemIndex];
                    if (item.Item.Count > 0 && item.Item.Content.IsValid)
                        inventory.Add(item.Item);
                }
                while (overlayIndex.ActorInventoryByPlacedRefId.TryGetNextValue(out itemIndex, ref iterator));
                return true;
            }

            for (int i = firstIndex; i < savedItems.Length; i++)
            {
                var item = savedItems[i];
                if (item.PlacedRefId != placedRefId)
                    continue;
                if (item.Item.Count > 0 && item.Item.Content.IsValid)
                    inventory.Add(item.Item);
            }

            return true;
        }

        public static bool TransformDiffersFromInitial(EntityManager entityManager, Entity entity)
        {
            if (!entityManager.HasComponent<LocalTransform>(entity) || !entityManager.HasComponent<PlacedRefInitialTransform>(entity))
                return false;

            var transform = entityManager.GetComponentData<LocalTransform>(entity);
            var initial = entityManager.GetComponentData<PlacedRefInitialTransform>(entity);
            return math.lengthsq(transform.Position - initial.Position) > TransformEpsilon * TransformEpsilon
                   || math.lengthsq(transform.Rotation.value - initial.Rotation.value) > TransformEpsilon * TransformEpsilon
                   || math.abs(transform.Scale - initial.Scale) > TransformEpsilon;
        }

        static Entity RequireRuntimeEntity(EntityManager entityManager)
        {
            Entity runtimeEntity = WorldStateEntityQueryUtility.GetSingletonEntity<MorrowindScriptRuntimeState>(entityManager);
            if (runtimeEntity == Entity.Null)
                throw new InvalidOperationException("[VVardenfell][Save] Script visible save state requires script runtime.");
            EnsureRuntimeBuffers(entityManager, runtimeEntity);
            return runtimeEntity;
        }

        static BlobAssetReference<RuntimeContentBlob> RequireRuntimeContentBlob(EntityManager entityManager)
        {
            EntityQuery query = RuntimeContentBlobQueryCache.Get(entityManager);
            if (query.CalculateEntityCount() != 1)
                throw new InvalidOperationException("[VVardenfell][Save] Script visible save state requires exactly one runtime content blob.");

            var blob = query.GetSingleton<RuntimeContentBlobReference>().Blob;
            if (!blob.IsCreated)
                throw new InvalidOperationException("[VVardenfell][Save] Script visible save state requires created runtime content blob.");
            return blob;
        }

        public static void EnsureOverlayIndexCurrent(EntityManager entityManager, Entity runtimeEntity)
        {
            EnsureRuntimeBuffers(entityManager, runtimeEntity);
            if (!entityManager.IsComponentEnabled<PlacedRefOverlayIndexDirty>(runtimeEntity))
                return;

            RebuildOverlayIndex(entityManager, runtimeEntity);
        }

        public static void RebuildOverlayIndex(EntityManager entityManager, Entity runtimeEntity)
        {
            EnsureRuntimeBuffers(entityManager, runtimeEntity);
            var index = entityManager.GetComponentData<PlacedRefOverlayRuntimeIndex>(runtimeEntity);
            if (!index.IsCreated)
                index = PlacedRefOverlayRuntimeIndex.Create(1024);

            RebuildOverlayIndex(
                ref index,
                entityManager.GetBuffer<PlacedRefOverlayState>(runtimeEntity),
                entityManager.GetBuffer<PlacedRefOverlayScriptInstance>(runtimeEntity),
                entityManager.GetBuffer<PlacedRefOverlayScriptLocalValue>(runtimeEntity),
                entityManager.GetBuffer<PlacedRefOverlayActorInventoryItem>(runtimeEntity),
                entityManager.GetBuffer<PlacedRefOverlayContainerItem>(runtimeEntity));

            entityManager.SetComponentData(runtimeEntity, index);
            entityManager.SetComponentEnabled<PlacedRefOverlayIndexDirty>(runtimeEntity, false);
        }

        public static void RebuildOverlayIndex(
            ref PlacedRefOverlayRuntimeIndex index,
            DynamicBuffer<PlacedRefOverlayState> states,
            DynamicBuffer<PlacedRefOverlayScriptInstance> scriptInstances,
            DynamicBuffer<PlacedRefOverlayScriptLocalValue> scriptLocals,
            DynamicBuffer<PlacedRefOverlayActorInventoryItem> actorInventory,
            DynamicBuffer<PlacedRefOverlayContainerItem> containerItems)
        {
            int capacity = math.max(
                math.max(states.Length, scriptInstances.Length),
                math.max(math.max(scriptLocals.Length, actorInventory.Length), containerItems.Length));
            PrepareOverlayIndexForRebuild(ref index, capacity);

            for (int i = 0; i < states.Length; i++)
            {
                uint placedRefId = states[i].PlacedRefId;
                if (placedRefId != 0u)
                    index.StateByPlacedRefId[placedRefId] = i;
            }

            for (int i = 0; i < scriptInstances.Length; i++)
            {
                uint placedRefId = scriptInstances[i].PlacedRefId;
                if (placedRefId != 0u)
                    index.ScriptInstancesByPlacedRefId.Add(placedRefId, i);
            }

            for (int i = 0; i < scriptLocals.Length; i++)
            {
                uint placedRefId = scriptLocals[i].PlacedRefId;
                if (placedRefId != 0u)
                    index.ScriptLocalsByPlacedRefId.Add(placedRefId, i);
            }

            for (int i = 0; i < actorInventory.Length; i++)
            {
                uint placedRefId = actorInventory[i].PlacedRefId;
                if (placedRefId != 0u)
                    index.ActorInventoryByPlacedRefId.Add(placedRefId, i);
            }

            for (int i = 0; i < containerItems.Length; i++)
            {
                uint placedRefId = containerItems[i].PlacedRefId;
                if (placedRefId != 0u)
                    index.ContainerItemsByPlacedRefId.Add(placedRefId, i);
            }
        }

        public static void PrepareOverlayIndexForRebuild(ref PlacedRefOverlayRuntimeIndex index, int capacity)
        {
            EnsureOverlayIndexCapacity(ref index, capacity);
            index.StateByPlacedRefId.Clear();
            index.ScriptInstancesByPlacedRefId.Clear();
            index.ScriptLocalsByPlacedRefId.Clear();
            index.ActorInventoryByPlacedRefId.Clear();
            index.ContainerItemsByPlacedRefId.Clear();
            index.Revision++;
        }

        public static void DisposeOverlayIndex(EntityManager entityManager, Entity runtimeEntity)
        {
            if (!entityManager.Exists(runtimeEntity) || !entityManager.HasComponent<PlacedRefOverlayRuntimeIndex>(runtimeEntity))
                return;

            var index = entityManager.GetComponentData<PlacedRefOverlayRuntimeIndex>(runtimeEntity);
            index.Dispose();
            entityManager.SetComponentData(runtimeEntity, index);
        }

        static void ClearOverlayIndex(EntityManager entityManager, Entity runtimeEntity)
        {
            var index = entityManager.GetComponentData<PlacedRefOverlayRuntimeIndex>(runtimeEntity);
            if (!index.IsCreated)
                index = PlacedRefOverlayRuntimeIndex.Create(1024);

            index.StateByPlacedRefId.Clear();
            index.ScriptInstancesByPlacedRefId.Clear();
            index.ScriptLocalsByPlacedRefId.Clear();
            index.ActorInventoryByPlacedRefId.Clear();
            index.ContainerItemsByPlacedRefId.Clear();
            index.Revision++;
            entityManager.SetComponentData(runtimeEntity, index);
            entityManager.SetComponentEnabled<PlacedRefOverlayIndexDirty>(runtimeEntity, false);
        }

        static void EnsureOverlayIndexCapacity(ref PlacedRefOverlayRuntimeIndex index, int capacity)
        {
            capacity = math.max(capacity, 16);
            if (!index.IsCreated)
            {
                index = PlacedRefOverlayRuntimeIndex.Create(capacity);
                return;
            }

            if (index.StateByPlacedRefId.Capacity < capacity)
                index.StateByPlacedRefId.Capacity = capacity;
            if (index.ScriptInstancesByPlacedRefId.Capacity < capacity)
                index.ScriptInstancesByPlacedRefId.Capacity = capacity;
            if (index.ScriptLocalsByPlacedRefId.Capacity < capacity)
                index.ScriptLocalsByPlacedRefId.Capacity = capacity;
            if (index.ActorInventoryByPlacedRefId.Capacity < capacity)
                index.ActorInventoryByPlacedRefId.Capacity = capacity;
            if (index.ContainerItemsByPlacedRefId.Capacity < capacity)
                index.ContainerItemsByPlacedRefId.Capacity = capacity;
        }

        static bool IsOverlayIndexDirty(EntityManager entityManager, Entity runtimeEntity)
            => entityManager.HasComponent<PlacedRefOverlayIndexDirty>(runtimeEntity)
               && entityManager.IsComponentEnabled<PlacedRefOverlayIndexDirty>(runtimeEntity);

        static void MarkOverlayIndexDirty(EntityManager entityManager, Entity runtimeEntity)
        {
            if (!entityManager.HasComponent<PlacedRefOverlayIndexDirty>(runtimeEntity))
                entityManager.AddComponent<PlacedRefOverlayIndexDirty>(runtimeEntity);
            entityManager.SetComponentEnabled<PlacedRefOverlayIndexDirty>(runtimeEntity, true);
        }

        static void UpsertState(
            EntityManager entityManager,
            Entity runtimeEntity,
            DynamicBuffer<PlacedRefOverlayState> states,
            int index,
            in PlacedRefOverlayState state)
        {
            if (index >= 0)
            {
                states[index] = state;
                if (!IsOverlayIndexDirty(entityManager, runtimeEntity))
                    UpdateStateIndex(entityManager, runtimeEntity, state.PlacedRefId, index);
            }
            else
            {
                index = states.Length;
                states.Add(state);
                if (!IsOverlayIndexDirty(entityManager, runtimeEntity))
                    UpdateStateIndex(entityManager, runtimeEntity, state.PlacedRefId, index);
            }
        }

        static void UpdateStateIndex(EntityManager entityManager, Entity runtimeEntity, uint placedRefId, int index)
        {
            if (placedRefId == 0u || !entityManager.HasComponent<PlacedRefOverlayRuntimeIndex>(runtimeEntity))
                return;

            var overlayIndex = entityManager.GetComponentData<PlacedRefOverlayRuntimeIndex>(runtimeEntity);
            if (!overlayIndex.IsCreated)
                return;

            overlayIndex.StateByPlacedRefId[placedRefId] = index;
            entityManager.SetComponentData(runtimeEntity, overlayIndex);
        }

        static int FindStateIndex(EntityManager entityManager, Entity runtimeEntity, DynamicBuffer<PlacedRefOverlayState> states, uint placedRefId)
        {
            if (!IsOverlayIndexDirty(entityManager, runtimeEntity)
                && entityManager.HasComponent<PlacedRefOverlayRuntimeIndex>(runtimeEntity))
            {
                var index = entityManager.GetComponentData<PlacedRefOverlayRuntimeIndex>(runtimeEntity);
                if (index.IsCreated && index.StateByPlacedRefId.TryGetValue(placedRefId, out int stateIndex))
                    return stateIndex;
            }

            return FindStateIndexLinear(states, placedRefId);
        }

        static int FindStateIndexLinear(DynamicBuffer<PlacedRefOverlayState> states, uint placedRefId)
        {
            for (int i = 0; i < states.Length; i++)
            {
                if (states[i].PlacedRefId == placedRefId)
                    return i;
            }

            return -1;
        }

        static int FindScriptInstanceIndex(DynamicBuffer<PlacedRefOverlayScriptInstance> instances, uint placedRefId, int programIndex)
        {
            for (int i = 0; i < instances.Length; i++)
            {
                if (instances[i].PlacedRefId == placedRefId && instances[i].ProgramIndex == programIndex)
                    return i;
            }

            return -1;
        }

        static bool HasScriptOverlay(
            DynamicBuffer<PlacedRefOverlayScriptInstance> instances,
            in PlacedRefOverlayRuntimeIndex overlayIndex,
            uint placedRefId)
            => FindFirstScriptInstanceIndex(instances, overlayIndex, placedRefId) >= 0;

        static int FindFirstScriptInstanceIndex(
            DynamicBuffer<PlacedRefOverlayScriptInstance> instances,
            in PlacedRefOverlayRuntimeIndex overlayIndex,
            uint placedRefId)
        {
            if (overlayIndex.IsCreated
                && overlayIndex.ScriptInstancesByPlacedRefId.TryGetFirstValue(placedRefId, out int index, out _))
            {
                return index;
            }

            for (int i = 0; i < instances.Length; i++)
            {
                if (instances[i].PlacedRefId == placedRefId)
                    return i;
            }

            return -1;
        }

        static int FindFirstActorInventoryIndex(
            DynamicBuffer<PlacedRefOverlayActorInventoryItem> items,
            in PlacedRefOverlayRuntimeIndex overlayIndex,
            uint placedRefId)
        {
            if (overlayIndex.IsCreated
                && overlayIndex.ActorInventoryByPlacedRefId.TryGetFirstValue(placedRefId, out int index, out _))
            {
                return index;
            }

            for (int i = 0; i < items.Length; i++)
            {
                if (items[i].PlacedRefId == placedRefId)
                    return i;
            }

            return -1;
        }

        static void RemoveScriptLocals(DynamicBuffer<PlacedRefOverlayScriptLocalValue> locals, uint placedRefId, int programIndex)
        {
            for (int i = locals.Length - 1; i >= 0; i--)
            {
                if (locals[i].PlacedRefId == placedRefId && locals[i].ProgramIndex == programIndex)
                    locals.RemoveAt(i);
            }
        }

        static void RemoveActorInventory(DynamicBuffer<PlacedRefOverlayActorInventoryItem> items, uint placedRefId)
        {
            for (int i = items.Length - 1; i >= 0; i--)
            {
                if (items[i].PlacedRefId == placedRefId)
                    items.RemoveAt(i);
            }
        }

        static void RemoveContainerItems(DynamicBuffer<PlacedRefOverlayContainerItem> items, uint placedRefId)
        {
            for (int i = items.Length - 1; i >= 0; i--)
            {
                if (items[i].PlacedRefId == placedRefId)
                    items.RemoveAt(i);
            }
        }

        static class LogicalRefProjectionQueryCache
        {
            static World s_World;
            static EntityQuery s_Query;
            static bool s_QueryCreated;

            public static EntityQuery Get(EntityManager entityManager)
            {
                World world = entityManager.World;
                if (s_QueryCreated && s_World == world)
                    return s_Query;

                s_World = world;
                s_Query = entityManager.CreateEntityQuery(
                    ComponentType.ReadOnly<LogicalRefTag>(),
                    ComponentType.ReadOnly<PlacedRefIdentity>());
                s_QueryCreated = true;
                return s_Query;
            }
        }

        static class RuntimeContentBlobQueryCache
        {
            static World s_World;
            static EntityQuery s_Query;
            static bool s_QueryCreated;

            public static EntityQuery Get(EntityManager entityManager)
            {
                World world = entityManager.World;
                if (s_QueryCreated && s_World == world)
                    return s_Query;

                s_World = world;
                s_Query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<RuntimeContentBlobReference>());
                s_QueryCreated = true;
                return s_Query;
            }
        }

        static class GlobalScriptQueryCache
        {
            static World s_World;
            static EntityQuery s_Query;
            static bool s_QueryCreated;

            public static EntityQuery Get(EntityManager entityManager)
            {
                World world = entityManager.World;
                if (s_QueryCreated && s_World == world)
                    return s_Query;

                s_World = world;
                s_Query = entityManager.CreateEntityQuery(ComponentType.ReadWrite<MorrowindGlobalScriptInstance>());
                s_QueryCreated = true;
                return s_Query;
            }
        }
    }
}
