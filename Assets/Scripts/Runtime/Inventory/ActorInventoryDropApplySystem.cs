using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.MorrowindScript;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.WorldRefs;
using VVardenfell.Runtime.WorldState;

namespace VVardenfell.Runtime.Inventory
{
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    public partial struct ActorInventoryDropApplySystem : ISystem
    {
        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<MorrowindScriptRuntimeState>();
            systemState.RequireForUpdate<ActorInventoryDropRequest>();
            systemState.RequireForUpdate<RuntimeSpawnState>();
            systemState.RequireForUpdate<RuntimeSpawnRequest>();
            systemState.RequireForUpdate<LogicalRefLookup>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = systemState.EntityManager.GetBuffer<ActorInventoryDropRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            ref RuntimeContentBlob contentBlob = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;

            var logicalRefLookup = SystemAPI.GetSingleton<LogicalRefLookup>();
            Entity spawnEntity = SystemAPI.GetSingletonEntity<RuntimeSpawnState>();
            var spawnState = systemState.EntityManager.GetComponentData<RuntimeSpawnState>(spawnEntity);
            var spawnRequests = systemState.EntityManager.GetBuffer<RuntimeSpawnRequest>(spawnEntity);

            for (int i = 0; i < requests.Length; i++)
            {
                var request = requests[i];
                ValidateRequest(ref contentBlob, request);
                if (request.Count == 0)
                    continue;

                Entity target = MorrowindRuntimeTargetResolver.ResolveLiveTarget(
                    systemState.EntityManager,
                    request.TargetEntity,
                    request.TargetPlacedRefId,
                    logicalRefLookup);
                if (target == Entity.Null)
                    throw new InvalidOperationException($"[VVardenfell][Inventory] Drop target ref={request.TargetPlacedRefId} is not live.");

                if (!IsActorTarget(ref systemState, target))
                    continue;

                bool equipmentChanged = RemoveAvailableItems(ref systemState, target, request.Content, request.Count);
                PlayerEncumbranceDirtyUtility.MarkIfPlayer(systemState.EntityManager, target);
                if (equipmentChanged)
                    MarkActorPresentationEquipmentDirty(ref systemState, target);
                EnqueueDropSpawns(ref systemState, target, request, spawnRequests, ref spawnState);
            }

            systemState.EntityManager.SetComponentData(spawnEntity, spawnState);
            requests.Clear();
        }

        static void ValidateRequest(ref RuntimeContentBlob contentBlob, in ActorInventoryDropRequest request)
        {
            if (request.Count < 0)
                throw new InvalidOperationException("[VVardenfell][Inventory] Drop count must be non-negative.");

            if (!request.Content.IsValid || !RuntimeContentBlobUtility.IsValid(ref contentBlob, request.Content))
                throw new InvalidOperationException("[VVardenfell][Inventory] Drop content reference is invalid.");

            if (!RuntimeContentMetadataResolver.TryResolveCarryable(ref contentBlob, request.Content, out _))
                throw new InvalidOperationException("[VVardenfell][Inventory] Drop content is not carryable.");

            if (!WorldResources.TryGetRuntimeSpawnPrefab(request.Content, out _))
                throw new InvalidOperationException("[VVardenfell][Inventory] Drop content has no runtime spawn prefab.");
        }

        bool IsActorTarget(ref SystemState systemState, Entity target)
            => systemState.EntityManager.HasComponent<ActorSpawnSource>(target)
               || systemState.EntityManager.HasComponent<PlayerTag>(target);

        bool RemoveAvailableItems(ref SystemState systemState, Entity target, ContentReference content, int count)
        {
            if (systemState.EntityManager.HasBuffer<ActorInventoryItem>(target))
            {
                var inventory = systemState.EntityManager.GetBuffer<ActorInventoryItem>(target);
                DynamicBuffer<ActorEquipmentSlot> equipment = systemState.EntityManager.HasBuffer<ActorEquipmentSlot>(target)
                    ? systemState.EntityManager.GetBuffer<ActorEquipmentSlot>(target)
                    : default;
                ulong previousSignature = equipment.IsCreated
                    ? ActorPresentationEquipmentUtility.BuildEquipmentSignature(equipment)
                    : 0ul;
                RemoveActorItems(inventory, equipment, content, count);
                ulong currentSignature = equipment.IsCreated
                    ? ActorPresentationEquipmentUtility.BuildEquipmentSignature(equipment)
                    : 0ul;
                return previousSignature != currentSignature;
            }

            if (systemState.EntityManager.HasBuffer<PlayerInventoryItem>(target))
            {
                var inventory = systemState.EntityManager.GetBuffer<PlayerInventoryItem>(target);
                DynamicBuffer<ActorEquipmentSlot> equipment = systemState.EntityManager.HasBuffer<ActorEquipmentSlot>(target)
                    ? systemState.EntityManager.GetBuffer<ActorEquipmentSlot>(target)
                    : default;
                ulong previousSignature = equipment.IsCreated
                    ? ActorPresentationEquipmentUtility.BuildEquipmentSignature(equipment)
                    : 0ul;
                RemovePlayerItems(inventory, equipment, content, count);
                ulong currentSignature = equipment.IsCreated
                    ? ActorPresentationEquipmentUtility.BuildEquipmentSignature(equipment)
                    : 0ul;
                return previousSignature != currentSignature;
            }

            return false;
        }

        void MarkActorPresentationEquipmentDirty(ref SystemState systemState, Entity actor)
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            ActorPresentationEquipmentUtility.QueueEnsurePresentationEquipmentDirty(
                systemState.EntityManager,
                ref ecb,
                actor,
                enabled: true);

            foreach (var (visual, entity) in
                     SystemAPI.Query<RefRO<LocalPlayerVisual>>()
                         .WithEntityAccess())
            {
                if (visual.ValueRO.Player == actor)
                {
                    ActorPresentationEquipmentUtility.QueueEnsurePresentationEquipmentDirty(
                        systemState.EntityManager,
                        ref ecb,
                        entity,
                        enabled: true);
                }
            }

            ecb.Playback(systemState.EntityManager);
            ecb.Dispose();
        }

        void EnqueueDropSpawns(ref SystemState systemState, 
            Entity target,
            in ActorInventoryDropRequest request,
            DynamicBuffer<RuntimeSpawnRequest> spawnRequests,
            ref RuntimeSpawnState spawnState)
        {
            if (!systemState.EntityManager.HasComponent<LocalTransform>(target))
                throw new InvalidOperationException($"[VVardenfell][Inventory] Drop target ref={request.TargetPlacedRefId} has no LocalTransform.");

            var transform = systemState.EntityManager.GetComponentData<LocalTransform>(target);
            ResolveDropLocation(ref systemState, target, transform.Position, out int2 exteriorCell, out FixedString128Bytes interiorCellId, out ulong interiorCellHash, out byte isInterior);
            for (int i = 0; i < request.Count; i++)
            {
                spawnState.NextRequestSequence += 1u;
                spawnRequests.Add(new RuntimeSpawnRequest
                {
                    Sequence = spawnState.NextRequestSequence,
                    Content = request.Content,
                    Position = transform.Position,
                    Rotation = transform.Rotation,
                    Scale = math.max(0.0001f, transform.Scale),
                    ExteriorCell = exteriorCell,
                    InteriorCellId = interiorCellId,
                    InteriorCellHash = interiorCellHash,
                    IsInterior = isInterior,
                    PersistencePolicy = (byte)RuntimeSpawnPersistencePolicy.CellOwnedSession,
                });
            }
        }

        void ResolveDropLocation(ref SystemState systemState, 
            Entity target,
            float3 position,
            out int2 exteriorCell,
            out FixedString128Bytes interiorCellId,
            out ulong interiorCellHash,
            out byte isInterior)
        {
            if (systemState.EntityManager.HasComponent<LogicalRefLocation>(target))
            {
                var location = systemState.EntityManager.GetComponentData<LogicalRefLocation>(target);
                exteriorCell = location.ExteriorCell;
                interiorCellId = location.InteriorCellId;
                interiorCellHash = location.InteriorCellHash;
                isInterior = location.IsInterior;
                return;
            }

            if (SystemAPI.TryGetSingleton<InteriorTransitionState>(out var transition) && transition.InteriorActive != 0)
            {
                if (transition.ActiveInteriorCellHash == 0UL || transition.ActiveInteriorCellId.IsEmpty)
                    throw new InvalidOperationException("[VVardenfell][Inventory] Drop active interior context is incomplete.");

                exteriorCell = default;
                interiorCellId = transition.ActiveInteriorCellId;
                interiorCellHash = transition.ActiveInteriorCellHash;
                isInterior = 1;
                return;
            }

            exteriorCell = WorldBootstrap.WorldPositionToCell(position);
            interiorCellId = default;
            interiorCellHash = 0UL;
            isInterior = 0;
        }

        static void RemoveActorItems(
            DynamicBuffer<ActorInventoryItem> inventory,
            DynamicBuffer<ActorEquipmentSlot> equipment,
            ContentReference content,
            int count)
        {
            int remaining = count;
            RemoveUnequippedActorItems(inventory, equipment, content, ref remaining);
            if (remaining <= 0)
                return;

            UnequipContent(inventory, equipment, content);
            RemoveUnequippedActorItems(inventory, equipment, content, ref remaining);
        }

        static void RemovePlayerItems(
            DynamicBuffer<PlayerInventoryItem> inventory,
            DynamicBuffer<ActorEquipmentSlot> equipment,
            ContentReference content,
            int count)
        {
            int remaining = count;
            RemoveUnequippedPlayerItems(inventory, equipment, content, ref remaining);
            if (remaining <= 0)
                return;

            UnequipContent(inventory, equipment, content);
            RemoveUnequippedPlayerItems(inventory, equipment, content, ref remaining);
        }

        static void RemoveUnequippedActorItems(
            DynamicBuffer<ActorInventoryItem> inventory,
            DynamicBuffer<ActorEquipmentSlot> equipment,
            ContentReference content,
            ref int remaining)
        {
            for (int i = inventory.Length - 1; i >= 0 && remaining > 0; i--)
            {
                if (!Matches(inventory[i].Content, content) || IsEquipped(equipment, i, content))
                    continue;

                int removed = math.min(remaining, inventory[i].Count);
                remaining -= removed;
                RemoveActorCountAt(inventory, equipment, i, removed);
            }
        }

        static void RemoveUnequippedPlayerItems(
            DynamicBuffer<PlayerInventoryItem> inventory,
            DynamicBuffer<ActorEquipmentSlot> equipment,
            ContentReference content,
            ref int remaining)
        {
            for (int i = inventory.Length - 1; i >= 0 && remaining > 0; i--)
            {
                if (!Matches(inventory[i].Content, content) || IsEquipped(equipment, i, content))
                    continue;

                int removed = math.min(remaining, inventory[i].Count);
                remaining -= removed;
                RemovePlayerCountAt(inventory, equipment, i, removed);
            }
        }

        static void RemoveActorCountAt(
            DynamicBuffer<ActorInventoryItem> inventory,
            DynamicBuffer<ActorEquipmentSlot> equipment,
            int index,
            int count)
        {
            var entry = inventory[index];
            if (count >= entry.Count)
            {
                inventory.RemoveAt(index);
                AdjustEquipmentAfterInventoryRemove(equipment, index);
                return;
            }

            entry.Count -= count;
            inventory[index] = entry;
        }

        static void RemovePlayerCountAt(
            DynamicBuffer<PlayerInventoryItem> inventory,
            DynamicBuffer<ActorEquipmentSlot> equipment,
            int index,
            int count)
        {
            var entry = inventory[index];
            if (count >= entry.Count)
            {
                inventory.RemoveAt(index);
                AdjustEquipmentAfterInventoryRemove(equipment, index);
                return;
            }

            entry.Count -= count;
            inventory[index] = entry;
        }

        static void UnequipContent<TInventory>(
            DynamicBuffer<TInventory> inventory,
            DynamicBuffer<ActorEquipmentSlot> equipment,
            ContentReference content)
            where TInventory : unmanaged, IBufferElementData
        {
            if (!equipment.IsCreated)
                return;

            for (int i = equipment.Length - 1; i >= 0; i--)
            {
                var slot = equipment[i];
                if ((uint)slot.InventoryIndex >= (uint)inventory.Length || !Matches(slot.Content, content))
                    continue;

                equipment.RemoveAt(i);
            }
        }

        static bool IsEquipped(DynamicBuffer<ActorEquipmentSlot> equipment, int inventoryIndex, ContentReference content)
        {
            if (!equipment.IsCreated)
                return false;

            for (int i = 0; i < equipment.Length; i++)
            {
                var slot = equipment[i];
                if (slot.InventoryIndex == inventoryIndex && Matches(slot.Content, content))
                    return true;
            }

            return false;
        }

        static void AdjustEquipmentAfterInventoryRemove(DynamicBuffer<ActorEquipmentSlot> equipment, int removedIndex)
        {
            if (!equipment.IsCreated)
                return;

            for (int i = equipment.Length - 1; i >= 0; i--)
            {
                var slot = equipment[i];
                if (slot.InventoryIndex == removedIndex)
                {
                    equipment.RemoveAt(i);
                    continue;
                }

                if (slot.InventoryIndex > removedIndex)
                {
                    slot.InventoryIndex--;
                    equipment[i] = slot;
                }
            }
        }

        static bool Matches(ContentReference left, ContentReference right)
            => left.Kind == right.Kind && left.HandleValue == right.HandleValue;
    }
}
