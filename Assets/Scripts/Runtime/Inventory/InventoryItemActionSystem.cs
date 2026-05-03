using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Shell;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.WorldState;

namespace VVardenfell.Runtime.Inventory
{
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(ContainerWindowStateSystem))]
    [UpdateAfter(typeof(InventoryWindowStateSystem))]
    [UpdateBefore(typeof(RuntimeShellInputSystem))]
    public partial class InventoryItemActionSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<InventoryItemActionRequest>();
            RequireForUpdate<InventoryHeldItemState>();
            RequireForUpdate<PlayerInventoryItem>();
        }

        protected override void OnUpdate()
        {
            bool hasQueuedRequests = SystemAPI.HasSingleton<InventoryItemActionRequestElement>();
            ref var request = ref SystemAPI.GetSingletonRW<InventoryItemActionRequest>().ValueRW;
            if (request.Pending == 0 && !hasQueuedRequests)
                return;

            DynamicBuffer<InventoryItemActionRequestElement> queue = default;
            bool processQueue = hasQueuedRequests;
            if (processQueue)
                queue = SystemAPI.GetSingletonBuffer<InventoryItemActionRequestElement>();
            if (request.Pending == 0 && (!processQueue || queue.Length == 0))
                return;

            CompleteDependency();

            var inventory = SystemAPI.GetSingletonBuffer<PlayerInventoryItem>();
            DynamicBuffer<ActorEquipmentSlot> equipment = default;
            bool hasEquipment = TryGetPlayerEquipment(out equipment);
            ref var held = ref SystemAPI.GetSingletonRW<InventoryHeldItemState>().ValueRW;

            if (processQueue)
            {
                for (int i = 0; i < queue.Length; i++)
                    ProcessAction(queue[i], inventory, hasEquipment ? equipment : default, hasEquipment, ref held);
                queue.Clear();
            }

            if (request.Pending == 0)
                return;

            var componentRequest = new InventoryItemActionRequestElement
            {
                Action = request.Action,
                SourceOwner = request.SourceOwner,
                TargetOwner = request.TargetOwner,
                SourceIndex = request.SourceIndex,
                SourcePlacedRefId = request.SourcePlacedRefId,
                TargetPlacedRefId = request.TargetPlacedRefId,
                Content = request.Content,
                SoulId = request.SoulId,
                SoulActorHandleValue = request.SoulActorHandleValue,
                Count = request.Count,
                Sequence = request.Sequence,
            };
            request = default;
            ProcessAction(componentRequest, inventory, hasEquipment ? equipment : default, hasEquipment, ref held);
        }

        void ProcessAction(
            in InventoryItemActionRequestElement request,
            DynamicBuffer<PlayerInventoryItem> inventory,
            DynamicBuffer<ActorEquipmentSlot> equipment,
            bool hasEquipment,
            ref InventoryHeldItemState held)
        {
            var action = (InventoryItemActionKind)request.Action;
            var source = (InventoryItemOwnerKind)request.SourceOwner;
            var target = (InventoryItemOwnerKind)request.TargetOwner;
            switch (action)
            {
                case InventoryItemActionKind.BeginDrag:
                    BeginDrag(source, request.SourceIndex, request.SourcePlacedRefId, request.Count, inventory, equipment, ref held);
                    break;
                case InventoryItemActionKind.DirectTransfer:
                    DirectTransfer(source, target, request.SourceIndex, request.SourcePlacedRefId, request.TargetPlacedRefId, request.Count, inventory, equipment);
                    break;
                case InventoryItemActionKind.DropHeldToInventory:
                    ClearHeld(ref held);
                    break;
                case InventoryItemActionKind.DropHeldToContainer:
                    DropHeldToContainer(request.TargetPlacedRefId, inventory, equipment, ref held);
                    break;
                case InventoryItemActionKind.UseHeld:
                    UseHeld(inventory, equipment, hasEquipment, ref held);
                    break;
                case InventoryItemActionKind.ClearHeld:
                    ClearHeld(ref held);
                    break;
                case InventoryItemActionKind.UnequipInventoryItem:
                    UnequipInventoryItem(request.SourceIndex, inventory, equipment, hasEquipment);
                    break;
            }
        }

        bool TryGetPlayerEquipment(out DynamicBuffer<ActorEquipmentSlot> equipment)
        {
            using var query = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadWrite<ActorEquipmentSlot>());
            if (query.CalculateEntityCount() != 1)
            {
                equipment = default;
                return false;
            }

            equipment = EntityManager.GetBuffer<ActorEquipmentSlot>(query.GetSingletonEntity());
            return true;
        }

        void BeginDrag(
            InventoryItemOwnerKind source,
            int sourceIndex,
            uint sourcePlacedRefId,
            int requestedCount,
            DynamicBuffer<PlayerInventoryItem> inventory,
            DynamicBuffer<ActorEquipmentSlot> equipment,
            ref InventoryHeldItemState held)
        {
            ClearHeld(ref held);

            if (source == InventoryItemOwnerKind.PlayerInventory)
            {
                if (!TryGetPlayerEntry(inventory, sourceIndex, out var entry))
                    return;

                int count = math.clamp(requestedCount, 1, entry.Count);
                held = new InventoryHeldItemState
                {
                    Active = 1,
                    Owner = (byte)InventoryItemOwnerKind.PlayerInventory,
                    InventoryIndex = sourceIndex,
                    Content = entry.Content,
                    SoulId = entry.SoulId,
                    SoulActorHandleValue = entry.SoulActorHandleValue,
                    Count = count,
                };
                return;
            }

            if (source != InventoryItemOwnerKind.Container)
                return;
            if (!TryGetContainerItems(out var items) || !TryGetContainerEntry(items, sourcePlacedRefId, sourceIndex, out var containerEntry))
                return;

            int transferCount = math.clamp(requestedCount, 1, containerEntry.Count);
            WorldJournalUtility.AppendContainerDelta(EntityManager, sourcePlacedRefId, containerEntry.Content, -transferCount);
            RemoveContainerCountAt(items, sourceIndex, transferCount);
            ContainerLootUtility.AddInventoryStack(
                inventory,
                containerEntry.Content,
                containerEntry.SoulId,
                containerEntry.SoulActorHandleValue,
                transferCount);

            held = new InventoryHeldItemState
            {
                Active = 1,
                Owner = (byte)InventoryItemOwnerKind.PlayerInventory,
                InventoryIndex = FindPlayerStackIndex(inventory, containerEntry.Content, containerEntry.SoulId, containerEntry.SoulActorHandleValue),
                SourcePlacedRefId = sourcePlacedRefId,
                Content = containerEntry.Content,
                SoulId = containerEntry.SoulId,
                SoulActorHandleValue = containerEntry.SoulActorHandleValue,
                Count = transferCount,
            };
        }

        void DirectTransfer(
            InventoryItemOwnerKind source,
            InventoryItemOwnerKind target,
            int sourceIndex,
            uint sourcePlacedRefId,
            uint targetPlacedRefId,
            int requestedCount,
            DynamicBuffer<PlayerInventoryItem> inventory,
            DynamicBuffer<ActorEquipmentSlot> equipment)
        {
            if (source == InventoryItemOwnerKind.Container && target == InventoryItemOwnerKind.PlayerInventory)
            {
                if (!TryGetContainerItems(out var items) || !TryGetContainerEntry(items, sourcePlacedRefId, sourceIndex, out var entry))
                    return;

                int count = math.clamp(requestedCount, 1, entry.Count);
                WorldJournalUtility.AppendContainerDelta(EntityManager, sourcePlacedRefId, entry.Content, -count);
                RemoveContainerCountAt(items, sourceIndex, count);
                ContainerLootUtility.AddInventoryStack(inventory, entry.Content, entry.SoulId, entry.SoulActorHandleValue, count);
                return;
            }

            if (source == InventoryItemOwnerKind.PlayerInventory && target == InventoryItemOwnerKind.Container)
            {
                if (targetPlacedRefId == 0u || !TryGetContainerItems(out var items) || !TryGetPlayerEntry(inventory, sourceIndex, out var entry))
                    return;

                int count = math.clamp(requestedCount, 1, entry.Count);
                UnequipInventoryIndex(equipment, sourceIndex, entry.Content);
                ContainerLootUtility.AddOrIncrementContainerStack(items, targetPlacedRefId, entry.Content, entry.SoulId, entry.SoulActorHandleValue, count);
                WorldJournalUtility.AppendContainerDelta(EntityManager, targetPlacedRefId, entry.Content, count);
                RemovePlayerCountAt(inventory, sourceIndex, count, equipment);
            }
        }

        void DropHeldToContainer(
            uint targetPlacedRefId,
            DynamicBuffer<PlayerInventoryItem> inventory,
            DynamicBuffer<ActorEquipmentSlot> equipment,
            ref InventoryHeldItemState held)
        {
            if (held.Active == 0 || targetPlacedRefId == 0u || !TryGetContainerItems(out var items))
                return;

            int sourceIndex = ResolveHeldPlayerIndex(inventory, held);
            if (!TryGetPlayerEntry(inventory, sourceIndex, out var entry))
            {
                ClearHeld(ref held);
                return;
            }

            int count = math.clamp(held.Count, 1, entry.Count);
            UnequipInventoryIndex(equipment, sourceIndex, entry.Content);
            ContainerLootUtility.AddOrIncrementContainerStack(items, targetPlacedRefId, entry.Content, entry.SoulId, entry.SoulActorHandleValue, count);
            WorldJournalUtility.AppendContainerDelta(EntityManager, targetPlacedRefId, entry.Content, count);
            RemovePlayerCountAt(inventory, sourceIndex, count, equipment);
            ClearHeld(ref held);
        }

        void UseHeld(
            DynamicBuffer<PlayerInventoryItem> inventory,
            DynamicBuffer<ActorEquipmentSlot> equipment,
            bool hasEquipment,
            ref InventoryHeldItemState held)
        {
            if (held.Active == 0)
                return;

            int inventoryIndex = ResolveHeldPlayerIndex(inventory, held);
            if (!TryGetPlayerEntry(inventory, inventoryIndex, out var entry))
            {
                ClearHeld(ref held);
                return;
            }

            var contentDb = RuntimeContentDatabase.Active;
            if (RuntimeContentMetadataResolver.TryResolveBook(contentDb, entry.Content, out _))
            {
                QueueBookRead(inventoryIndex);
                ClearHeld(ref held);
                return;
            }

            if (!hasEquipment || entry.Content.Kind != ContentReferenceKind.Item)
            {
                WarnUnsupportedUse(entry.Content);
                ClearHeld(ref held);
                return;
            }

            var itemHandle = new ItemDefHandle { Value = entry.Content.HandleValue };
            if (contentDb == null || !contentDb.TryGetItemEquipment(itemHandle, out var itemEquipment))
            {
                WarnUnsupportedUse(entry.Content);
                ClearHeld(ref held);
                return;
            }

            ToggleEquipment(equipment, inventoryIndex, entry.Content, itemEquipment);
            ClearHeld(ref held);
        }

        static void UnequipInventoryItem(
            int inventoryIndex,
            DynamicBuffer<PlayerInventoryItem> inventory,
            DynamicBuffer<ActorEquipmentSlot> equipment,
            bool hasEquipment)
        {
            if (!hasEquipment || !TryGetPlayerEntry(inventory, inventoryIndex, out var entry))
                return;

            UnequipInventoryIndex(equipment, inventoryIndex, entry.Content);
        }

        void QueueBookRead(int inventoryIndex)
        {
            if (!SystemAPI.HasSingleton<BookInventoryReadRequest>())
                return;

            ref var request = ref SystemAPI.GetSingletonRW<BookInventoryReadRequest>().ValueRW;
            request.Pending = 1;
            request.InventoryIndex = inventoryIndex;
            request.Sequence++;
        }

        static void ToggleEquipment(
            DynamicBuffer<ActorEquipmentSlot> equipment,
            int inventoryIndex,
            ContentReference content,
            in ItemEquipmentDef itemEquipment)
        {
            for (int i = equipment.Length - 1; i >= 0; i--)
            {
                var slot = equipment[i];
                if (slot.InventoryIndex == inventoryIndex
                    && slot.Content.Kind == content.Kind
                    && slot.Content.HandleValue == content.HandleValue)
                {
                    equipment.RemoveAt(i);
                    return;
                }
            }

            RemoveConflictingSlots(equipment, itemEquipment.Slot);
            equipment.Add(new ActorEquipmentSlot
            {
                Slot = itemEquipment.Slot,
                Content = content,
                InventoryIndex = inventoryIndex,
                VisualMode = ActorEquipmentRuntimeUtility.ResolveEquipmentVisualMode(itemEquipment),
            });
        }

        static void RemoveConflictingSlots(DynamicBuffer<ActorEquipmentSlot> equipment, ItemEquipmentSlot slot)
        {
            for (int i = equipment.Length - 1; i >= 0; i--)
            {
                if (Conflicts(equipment[i].Slot, slot))
                    equipment.RemoveAt(i);
            }
        }

        static bool Conflicts(ItemEquipmentSlot equipped, ItemEquipmentSlot candidate)
        {
            if (equipped == candidate)
                return true;
            return (equipped == ItemEquipmentSlot.Boots && candidate == ItemEquipmentSlot.Shoes)
                   || (equipped == ItemEquipmentSlot.Shoes && candidate == ItemEquipmentSlot.Boots);
        }

        void WarnUnsupportedUse(ContentReference content)
        {
            Debug.LogWarning($"[VVardenfell][Inventory] item cannot be used from inventory yet: {content.Kind}/{content.HandleValue}");
            if (!SystemAPI.HasSingleton<InteractionPresentationState>())
                return;

            ref var presentation = ref SystemAPI.GetSingletonRW<InteractionPresentationState>().ValueRW;
            presentation.NotificationText = RuntimeFixedStringUtility.ToFixed128OrDefault("You cannot use that item.");
            presentation.NotificationSecondsRemaining = 3f;
            presentation.ShowNotification = 1;
        }

        static bool TryGetPlayerEntry(DynamicBuffer<PlayerInventoryItem> inventory, int index, out PlayerInventoryItem entry)
        {
            if (index >= 0 && index < inventory.Length && inventory[index].Count > 0 && inventory[index].Content.IsValid)
            {
                entry = inventory[index];
                return true;
            }

            entry = default;
            return false;
        }

        bool TryGetContainerItems(out DynamicBuffer<ContainerSessionItem> items)
        {
            if (!SystemAPI.HasSingleton<ContainerSessionItem>())
            {
                items = default;
                return false;
            }

            items = SystemAPI.GetSingletonBuffer<ContainerSessionItem>();
            return true;
        }

        static bool TryGetContainerEntry(
            DynamicBuffer<ContainerSessionItem> items,
            uint placedRefId,
            int index,
            out ContainerSessionItem entry)
        {
            if (placedRefId != 0u
                && index >= 0
                && index < items.Length
                && items[index].PlacedRefId == placedRefId
                && items[index].Count > 0
                && items[index].Content.IsValid)
            {
                entry = items[index];
                return true;
            }

            entry = default;
            return false;
        }

        static void RemoveContainerCountAt(DynamicBuffer<ContainerSessionItem> items, int index, int count)
        {
            var entry = items[index];
            if (count >= entry.Count)
            {
                items.RemoveAt(index);
                return;
            }

            entry.Count -= count;
            items[index] = entry;
        }

        static void RemovePlayerCountAt(
            DynamicBuffer<PlayerInventoryItem> inventory,
            int index,
            int count,
            DynamicBuffer<ActorEquipmentSlot> equipment)
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

        static void UnequipInventoryIndex(DynamicBuffer<ActorEquipmentSlot> equipment, int inventoryIndex, ContentReference content)
        {
            if (!equipment.IsCreated)
                return;

            for (int i = equipment.Length - 1; i >= 0; i--)
            {
                var slot = equipment[i];
                if (slot.InventoryIndex == inventoryIndex
                    && slot.Content.Kind == content.Kind
                    && slot.Content.HandleValue == content.HandleValue)
                {
                    equipment.RemoveAt(i);
                }
            }
        }

        static int ResolveHeldPlayerIndex(DynamicBuffer<PlayerInventoryItem> inventory, in InventoryHeldItemState held)
        {
            if (held.InventoryIndex >= 0
                && held.InventoryIndex < inventory.Length
                && Matches(inventory[held.InventoryIndex], held.Content, held.SoulId, held.SoulActorHandleValue))
            {
                return held.InventoryIndex;
            }

            return FindPlayerStackIndex(inventory, held.Content, held.SoulId, held.SoulActorHandleValue);
        }

        static int FindPlayerStackIndex(
            DynamicBuffer<PlayerInventoryItem> inventory,
            ContentReference content,
            FixedString64Bytes soulId,
            int soulActorHandleValue)
        {
            for (int i = 0; i < inventory.Length; i++)
            {
                if (Matches(inventory[i], content, soulId, soulActorHandleValue))
                    return i;
            }

            return -1;
        }

        static bool Matches(
            in PlayerInventoryItem entry,
            ContentReference content,
            FixedString64Bytes soulId,
            int soulActorHandleValue)
        {
            return entry.Content.Kind == content.Kind
                   && entry.Content.HandleValue == content.HandleValue
                   && entry.SoulId.Equals(soulId)
                   && entry.SoulActorHandleValue == soulActorHandleValue;
        }

        static void ClearHeld(ref InventoryHeldItemState held)
        {
            held = new InventoryHeldItemState
            {
                InventoryIndex = -1,
            };
        }
    }
}
