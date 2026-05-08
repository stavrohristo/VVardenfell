using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Streaming;

namespace VVardenfell.Runtime.WorldState
{
    static class ScriptVisibleSaveStateUtility
    {
        const float TransformEpsilon = 0.0001f;

        public static void EnsureRuntimeBuffers(EntityManager entityManager, Entity runtimeEntity)
        {
            if (!entityManager.HasBuffer<PlacedRefSavedState>(runtimeEntity))
                entityManager.AddBuffer<PlacedRefSavedState>(runtimeEntity);
            if (!entityManager.HasBuffer<PlacedRefSavedScriptInstance>(runtimeEntity))
                entityManager.AddBuffer<PlacedRefSavedScriptInstance>(runtimeEntity);
            if (!entityManager.HasBuffer<PlacedRefSavedScriptLocalValue>(runtimeEntity))
                entityManager.AddBuffer<PlacedRefSavedScriptLocalValue>(runtimeEntity);
            if (!entityManager.HasBuffer<PlacedRefSavedActorInventoryItem>(runtimeEntity))
                entityManager.AddBuffer<PlacedRefSavedActorInventoryItem>(runtimeEntity);
        }

        public static void ClearRuntimeOverlay(EntityManager entityManager, Entity runtimeEntity)
        {
            EnsureRuntimeBuffers(entityManager, runtimeEntity);
            entityManager.GetBuffer<PlacedRefSavedState>(runtimeEntity).Clear();
            entityManager.GetBuffer<PlacedRefSavedScriptInstance>(runtimeEntity).Clear();
            entityManager.GetBuffer<PlacedRefSavedScriptLocalValue>(runtimeEntity).Clear();
            entityManager.GetBuffer<PlacedRefSavedActorInventoryItem>(runtimeEntity).Clear();

            Entity stateLookupEntity = WorldStateEntityQueryUtility.GetSingletonEntity<PlacedRefRuntimeStateLookup>(entityManager);
            if (stateLookupEntity == Entity.Null)
                return;

            var lookup = entityManager.GetComponentData<PlacedRefRuntimeStateLookup>(stateLookupEntity);
            if (lookup.DisabledByPlacedRef.IsCreated)
                lookup.DisabledByPlacedRef.Clear();
            entityManager.SetComponentData(stateLookupEntity, lookup);
        }

        public static void UpsertDisabled(EntityManager entityManager, uint placedRefId, byte disabled)
        {
            if (placedRefId == 0u)
                throw new InvalidOperationException("[VVardenfell][Save] Cannot save disabled state for placed ref 0.");

            Entity runtimeEntity = RequireRuntimeEntity(entityManager);
            var states = entityManager.GetBuffer<PlacedRefSavedState>(runtimeEntity);
            int index = FindStateIndex(states, placedRefId);
            var state = index >= 0 ? states[index] : new PlacedRefSavedState { PlacedRefId = placedRefId };
            state.HasDisabled = 1;
            state.Disabled = disabled;
            UpsertState(states, index, state);
        }

        public static void UpsertLock(EntityManager entityManager, uint placedRefId, in PlacedRefLockState lockState)
        {
            if (placedRefId == 0u)
                throw new InvalidOperationException("[VVardenfell][Save] Cannot save lock state for placed ref 0.");

            Entity runtimeEntity = RequireRuntimeEntity(entityManager);
            var states = entityManager.GetBuffer<PlacedRefSavedState>(runtimeEntity);
            int index = FindStateIndex(states, placedRefId);
            var state = index >= 0 ? states[index] : new PlacedRefSavedState { PlacedRefId = placedRefId };
            state.HasLock = 1;
            state.LockLevel = lockState.LockLevel;
            state.Locked = lockState.Locked;
            state.KeyId = lockState.KeyId;
            state.TrapId = lockState.TrapId;
            UpsertState(states, index, state);
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
            var states = entityManager.GetBuffer<PlacedRefSavedState>(runtimeEntity);
            int index = FindStateIndex(states, placedRefId);
            var state = index >= 0 ? states[index] : new PlacedRefSavedState { PlacedRefId = placedRefId };
            state.HasTransform = 1;
            state.Position = position;
            state.Rotation = rotation;
            state.Scale = scale;
            state.ExteriorCell = location.ExteriorCell;
            state.InteriorCellId = location.InteriorCellId;
            state.InteriorCellHash = location.InteriorCellHash;
            state.IsInterior = location.IsInterior;
            UpsertState(states, index, state);
        }

        public static void UpsertObjectScript(EntityManager entityManager, Entity entity, uint placedRefId)
        {
            if (placedRefId == 0u)
                throw new InvalidOperationException("[VVardenfell][Save] Cannot save object script state for placed ref 0.");
            if (!entityManager.HasComponent<MorrowindScriptInstance>(entity))
                return;

            Entity runtimeEntity = RequireRuntimeEntity(entityManager);
            var instance = entityManager.GetComponentData<MorrowindScriptInstance>(entity);
            var instances = entityManager.GetBuffer<PlacedRefSavedScriptInstance>(runtimeEntity);
            int instanceIndex = FindScriptInstanceIndex(instances, placedRefId, instance.ProgramIndex);
            var saved = new PlacedRefSavedScriptInstance
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

            var locals = entityManager.HasBuffer<MorrowindScriptLocalValue>(entity)
                ? entityManager.GetBuffer<MorrowindScriptLocalValue>(entity)
                : default;
            var savedLocals = entityManager.GetBuffer<PlacedRefSavedScriptLocalValue>(runtimeEntity);
            RemoveScriptLocals(savedLocals, placedRefId, instance.ProgramIndex);
            if (locals.IsCreated)
            {
                for (int i = 0; i < locals.Length; i++)
                {
                    savedLocals.Add(new PlacedRefSavedScriptLocalValue
                    {
                        PlacedRefId = placedRefId,
                        ProgramIndex = instance.ProgramIndex,
                        LocalIndex = i,
                        Value = locals[i],
                    });
                }
            }
        }

        public static void ReplaceActorInventory(EntityManager entityManager, Entity entity, uint placedRefId)
        {
            if (placedRefId == 0u)
                throw new InvalidOperationException("[VVardenfell][Save] Cannot save actor inventory for placed ref 0.");
            if (!entityManager.HasBuffer<ActorInventoryItem>(entity))
                throw new InvalidOperationException($"[VVardenfell][Save] Placed ref 0x{placedRefId:X8} has no actor inventory to save.");

            Entity runtimeEntity = RequireRuntimeEntity(entityManager);
            var saved = entityManager.GetBuffer<PlacedRefSavedActorInventoryItem>(runtimeEntity);
            RemoveActorInventory(saved, placedRefId);
            var inventory = entityManager.GetBuffer<ActorInventoryItem>(entity);
            for (int i = 0; i < inventory.Length; i++)
            {
                saved.Add(new PlacedRefSavedActorInventoryItem
                {
                    PlacedRefId = placedRefId,
                    Item = inventory[i],
                });
            }
        }

        public static bool TryProjectSavedStateForLiveRefs(EntityManager entityManager, out string error)
        {
            error = null;
            Entity runtimeEntity = WorldStateEntityQueryUtility.GetSingletonEntity<MorrowindScriptRuntimeState>(entityManager);
            if (runtimeEntity == Entity.Null)
                return true;

            EnsureRuntimeBuffers(entityManager, runtimeEntity);
            var contentBlob = RequireRuntimeContentBlob(entityManager);
            ref RuntimeContentBlob content = ref contentBlob.Value;

            EntityQuery query = LogicalRefProjectionQueryCache.Get(entityManager);
            using var entities = query.ToEntityArray(Allocator.Temp);
            using var identities = query.ToComponentDataArray<PlacedRefIdentity>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                if (entityManager.HasComponent<PlacedRefSavedStateProjectionApplied>(entity))
                    continue;

                uint placedRefId = identities[i].Value;
                if (placedRefId == 0u)
                {
                    error = "Live logical ref has placed ref id 0 during save-state projection.";
                    return false;
                }

                var states = entityManager.GetBuffer<PlacedRefSavedState>(runtimeEntity);
                int stateIndex = FindStateIndex(states, placedRefId);
                if (stateIndex >= 0)
                {
                    var savedState = states[stateIndex];
                    ProjectState(entityManager, entity, savedState);
                }

                var scriptInstances = entityManager.GetBuffer<PlacedRefSavedScriptInstance>(runtimeEntity);
                var scriptLocals = entityManager.GetBuffer<PlacedRefSavedScriptLocalValue>(runtimeEntity);
                if (!ProjectScript(ref content, entityManager, entity, placedRefId, scriptInstances, scriptLocals, out error))
                    return false;

                var inventories = entityManager.GetBuffer<PlacedRefSavedActorInventoryItem>(runtimeEntity);
                ProjectActorInventory(entityManager, entity, placedRefId, inventories);
                entityManager.AddComponent<PlacedRefSavedStateProjectionApplied>(entity);
            }

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

        static void ProjectState(EntityManager entityManager, Entity entity, in PlacedRefSavedState saved)
        {
            if (saved.HasDisabled != 0)
            {
                if (!entityManager.HasComponent<PlacedRefRuntimeState>(entity))
                    throw new InvalidOperationException($"[VVardenfell][Save] Placed ref 0x{saved.PlacedRefId:X8} has no PlacedRefRuntimeState.");

                entityManager.SetComponentData(entity, new PlacedRefRuntimeState { Disabled = saved.Disabled });
                Entity stateLookupEntity = WorldStateEntityQueryUtility.GetSingletonEntity<PlacedRefRuntimeStateLookup>(entityManager);
                if (stateLookupEntity != Entity.Null)
                {
                    var lookup = entityManager.GetComponentData<PlacedRefRuntimeStateLookup>(stateLookupEntity);
                    if (lookup.DisabledByPlacedRef.IsCreated)
                    {
                        lookup.DisabledByPlacedRef[saved.PlacedRefId] = saved.Disabled;
                        entityManager.SetComponentData(stateLookupEntity, lookup);
                    }
                }
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
                    entityManager.AddComponentData(entity, lockState);
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
                    entityManager.AddComponentData(entity, location);

                if (location.IsInterior != 0)
                {
                    if (!entityManager.HasComponent<InteriorCellMember>(entity))
                        entityManager.AddComponent<InteriorCellMember>(entity);
                    if (entityManager.HasComponent<CellLink>(entity))
                        entityManager.RemoveComponent<CellLink>(entity);
                }
                else
                {
                    if (entityManager.HasComponent<InteriorCellMember>(entity))
                        entityManager.RemoveComponent<InteriorCellMember>(entity);
                    if (entityManager.HasComponent<CellLink>(entity))
                        entityManager.SetComponentData(entity, new CellLink { Value = location.ExteriorCell });
                    else
                        entityManager.AddComponentData(entity, new CellLink { Value = location.ExteriorCell });
                }
            }
        }

        static bool ProjectScript(
            ref RuntimeContentBlob content,
            EntityManager entityManager,
            Entity entity,
            uint placedRefId,
            DynamicBuffer<PlacedRefSavedScriptInstance> scriptInstances,
            DynamicBuffer<PlacedRefSavedScriptLocalValue> scriptLocals,
            out string error)
        {
            error = null;
            int firstInstanceIndex = FindFirstScriptInstanceIndex(scriptInstances, placedRefId);
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

            for (int i = 0; i < scriptLocals.Length; i++)
            {
                var local = scriptLocals[i];
                if (local.PlacedRefId != placedRefId || local.ProgramIndex != saved.ProgramIndex)
                    continue;

                if ((uint)local.LocalIndex >= (uint)locals.Length)
                {
                    error = $"Placed ref 0x{placedRefId:X8} saved local index {local.LocalIndex} is outside live count {locals.Length}.";
                    return false;
                }

                locals[local.LocalIndex] = local.Value;
            }

            return true;
        }

        static void ProjectActorInventory(
            EntityManager entityManager,
            Entity entity,
            uint placedRefId,
            DynamicBuffer<PlacedRefSavedActorInventoryItem> savedItems)
        {
            int firstIndex = FindFirstActorInventoryIndex(savedItems, placedRefId);
            if (firstIndex < 0)
                return;

            if (!entityManager.HasBuffer<ActorInventoryItem>(entity))
                throw new InvalidOperationException($"[VVardenfell][Save] Placed ref 0x{placedRefId:X8} has saved actor inventory but no live ActorInventoryItem buffer.");

            var inventory = entityManager.GetBuffer<ActorInventoryItem>(entity);
            inventory.Clear();
            for (int i = firstIndex; i < savedItems.Length; i++)
            {
                var item = savedItems[i];
                if (item.PlacedRefId != placedRefId)
                    continue;
                if (item.Item.Count > 0 && item.Item.Content.IsValid)
                    inventory.Add(item.Item);
            }
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

        static void UpsertState(DynamicBuffer<PlacedRefSavedState> states, int index, in PlacedRefSavedState state)
        {
            if (index >= 0)
                states[index] = state;
            else
                states.Add(state);
        }

        static int FindStateIndex(DynamicBuffer<PlacedRefSavedState> states, uint placedRefId)
        {
            for (int i = 0; i < states.Length; i++)
            {
                if (states[i].PlacedRefId == placedRefId)
                    return i;
            }

            return -1;
        }

        static int FindScriptInstanceIndex(DynamicBuffer<PlacedRefSavedScriptInstance> instances, uint placedRefId, int programIndex)
        {
            for (int i = 0; i < instances.Length; i++)
            {
                if (instances[i].PlacedRefId == placedRefId && instances[i].ProgramIndex == programIndex)
                    return i;
            }

            return -1;
        }

        static int FindFirstScriptInstanceIndex(DynamicBuffer<PlacedRefSavedScriptInstance> instances, uint placedRefId)
        {
            for (int i = 0; i < instances.Length; i++)
            {
                if (instances[i].PlacedRefId == placedRefId)
                    return i;
            }

            return -1;
        }

        static int FindFirstActorInventoryIndex(DynamicBuffer<PlacedRefSavedActorInventoryItem> items, uint placedRefId)
        {
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i].PlacedRefId == placedRefId)
                    return i;
            }

            return -1;
        }

        static void RemoveScriptLocals(DynamicBuffer<PlacedRefSavedScriptLocalValue> locals, uint placedRefId, int programIndex)
        {
            for (int i = locals.Length - 1; i >= 0; i--)
            {
                if (locals[i].PlacedRefId == placedRefId && locals[i].ProgramIndex == programIndex)
                    locals.RemoveAt(i);
            }
        }

        static void RemoveActorInventory(DynamicBuffer<PlacedRefSavedActorInventoryItem> items, uint placedRefId)
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
