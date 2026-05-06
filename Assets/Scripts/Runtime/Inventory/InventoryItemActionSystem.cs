using System;
using Unity.Collections;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Shell;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.WorldState;

namespace VVardenfell.Runtime.Inventory
{
    [BurstCompile]
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(ContainerWindowStateSystem))]
    [UpdateAfter(typeof(InventoryWindowStateSystem))]
    [UpdateBefore(typeof(RuntimeShellInputSystem))]
    public partial struct InventoryItemActionSystem : ISystem
    {
        EntityQuery _playerInventoryQuery;
        EntityQuery _playerEquipmentQuery;
        EntityQuery _worldJournalQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _playerInventoryQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadWrite<PlayerInventoryItem>());
            _playerEquipmentQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadWrite<ActorEquipmentSlot>());
            _worldJournalQuery = systemState.GetEntityQuery(
                ComponentType.ReadWrite<WorldJournalState>(),
                ComponentType.ReadWrite<WorldJournalEntry>());

            systemState.RequireForUpdate<InventoryItemActionRequest>();
            systemState.RequireForUpdate<InventoryHeldItemState>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
            systemState.RequireForUpdate(_playerInventoryQuery);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState systemState)
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

            systemState.Dependency.Complete();

            Entity inventoryEntity = _playerInventoryQuery.GetSingletonEntity();
            var inventory = systemState.EntityManager.GetBuffer<PlayerInventoryItem>(inventoryEntity);
            ulong previousInventorySignature = BuildInventoryWeightSignature(inventory);
            DynamicBuffer<ActorEquipmentSlot> equipment = default;
            bool hasEquipment = TryGetPlayerEquipment(ref systemState, out equipment, out Entity playerEquipmentEntity);
            ulong previousEquipmentSignature = hasEquipment
                ? ActorPresentationEquipmentUtility.BuildEquipmentSignature(equipment)
                : 0ul;
            ref var held = ref SystemAPI.GetSingletonRW<InventoryHeldItemState>().ValueRW;
            ref RuntimeContentBlob contentBlob = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;

            if (processQueue)
            {
                for (int i = 0; i < queue.Length; i++)
                    ProcessAction(ref systemState, ref contentBlob, queue[i], inventory, hasEquipment ? equipment : default, hasEquipment, ref held);
                queue.Clear();
            }

            if (request.Pending == 0)
            {
                MarkPlayerEncumbranceDirtyIfChanged(ref systemState, inventoryEntity, inventory, previousInventorySignature);
                MarkPlayerPresentationEquipmentDirtyIfChanged(ref systemState, hasEquipment, playerEquipmentEntity, equipment, previousEquipmentSignature);
                return;
            }

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
            ProcessAction(ref systemState, ref contentBlob, componentRequest, inventory, hasEquipment ? equipment : default, hasEquipment, ref held);
            MarkPlayerEncumbranceDirtyIfChanged(ref systemState, inventoryEntity, inventory, previousInventorySignature);
            MarkPlayerPresentationEquipmentDirtyIfChanged(ref systemState, hasEquipment, playerEquipmentEntity, equipment, previousEquipmentSignature);
        }

        void ProcessAction(ref SystemState systemState, 
            ref RuntimeContentBlob contentBlob,
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
                    BeginDrag(ref systemState, ref contentBlob, source, request.SourceIndex, request.SourcePlacedRefId, request.Count, inventory, equipment, ref held);
                    break;
                case InventoryItemActionKind.DirectTransfer:
                    DirectTransfer(ref systemState, ref contentBlob, source, target, request.SourceIndex, request.SourcePlacedRefId, request.TargetPlacedRefId, request.Count, inventory, equipment);
                    break;
                case InventoryItemActionKind.DropHeldToInventory:
                    ClearHeld(ref held);
                    break;
                case InventoryItemActionKind.DropHeldToContainer:
                    DropHeldToContainer(ref systemState, ref contentBlob, request.TargetPlacedRefId, inventory, equipment, ref held);
                    break;
                case InventoryItemActionKind.UseHeld:
                    UseHeld(ref systemState, ref contentBlob, inventory, equipment, hasEquipment, ref held);
                    break;
                case InventoryItemActionKind.ClearHeld:
                    ClearHeld(ref held);
                    break;
                case InventoryItemActionKind.UnequipInventoryItem:
                    UnequipInventoryItem(request.SourceIndex, inventory, equipment, hasEquipment);
                    break;
            }
        }

        bool TryGetPlayerEquipment(ref SystemState systemState, out DynamicBuffer<ActorEquipmentSlot> equipment, out Entity player)
        {
            if (_playerEquipmentQuery.CalculateEntityCount() != 1)
            {
                equipment = default;
                player = Entity.Null;
                return false;
            }

            player = _playerEquipmentQuery.GetSingletonEntity();
            equipment = systemState.EntityManager.GetBuffer<ActorEquipmentSlot>(player);
            return true;
        }

        void MarkPlayerEncumbranceDirtyIfChanged(ref SystemState systemState, 
            Entity player,
            DynamicBuffer<PlayerInventoryItem> inventory,
            ulong previousInventorySignature)
        {
            if (previousInventorySignature == BuildInventoryWeightSignature(inventory))
                return;

            PlayerEncumbranceDirtyUtility.MarkPlayerDirty(systemState.EntityManager, player);
        }

        static ulong BuildInventoryWeightSignature(DynamicBuffer<PlayerInventoryItem> inventory)
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            ulong hash = offset;
            for (int i = 0; i < inventory.Length; i++)
            {
                var entry = inventory[i];
                if (entry.Count <= 0 || !entry.Content.IsValid)
                    continue;

                Mix(ref hash, (uint)entry.Content.Kind);
                Mix(ref hash, unchecked((uint)entry.Content.HandleValue));
                Mix(ref hash, unchecked((uint)entry.Count));
            }

            return hash;

            void Mix(ref ulong hash, ulong value)
            {
                hash ^= value;
                hash *= prime;
            }
        }

        void MarkPlayerPresentationEquipmentDirtyIfChanged(ref SystemState systemState, 
            bool hasEquipment,
            Entity player,
            DynamicBuffer<ActorEquipmentSlot> equipment,
            ulong previousEquipmentSignature)
        {
            if (!hasEquipment || player == Entity.Null)
                return;

            ulong currentEquipmentSignature = ActorPresentationEquipmentUtility.BuildEquipmentSignature(equipment);
            if (previousEquipmentSignature == currentEquipmentSignature)
                return;

            var ecb = new EntityCommandBuffer(Allocator.Temp);
            ActorPresentationEquipmentUtility.QueueEnsurePresentationEquipmentDirty(
                systemState.EntityManager,
                ref ecb,
                player,
                enabled: true);

            foreach (var (visual, entity) in
                     SystemAPI.Query<RefRO<LocalPlayerVisual>>()
                         .WithEntityAccess())
            {
                if (visual.ValueRO.Player == player)
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

        void BeginDrag(ref SystemState systemState, 
            ref RuntimeContentBlob contentBlob,
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
            if (!TryGetContainerItems(ref systemState, out var items) || !TryGetContainerEntry(items, sourcePlacedRefId, sourceIndex, out var containerEntry))
                return;

            int transferCount = math.clamp(requestedCount, 1, containerEntry.Count);
            AppendContainerDelta(ref systemState, sourcePlacedRefId, containerEntry.Content, -transferCount);
            RemoveContainerCountAt(items, sourceIndex, transferCount);
            ContainerLootUtility.AddInventoryStack(
                ref contentBlob,
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

        void DirectTransfer(ref SystemState systemState, 
            ref RuntimeContentBlob contentBlob,
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
                if (!TryGetContainerItems(ref systemState, out var items) || !TryGetContainerEntry(items, sourcePlacedRefId, sourceIndex, out var entry))
                    return;

                int count = math.clamp(requestedCount, 1, entry.Count);
                RemoveCorpseBackingInventory(ref systemState, sourcePlacedRefId, entry, count);
                AppendContainerDelta(ref systemState, sourcePlacedRefId, entry.Content, -count);
                RemoveContainerCountAt(items, sourceIndex, count);
                ContainerLootUtility.AddInventoryStack(ref contentBlob, inventory, entry.Content, entry.SoulId, entry.SoulActorHandleValue, count);
                return;
            }

            if (source == InventoryItemOwnerKind.PlayerInventory && target == InventoryItemOwnerKind.Container)
            {
                if (targetPlacedRefId == 0u || !TryGetContainerItems(ref systemState, out var items) || !TryGetPlayerEntry(inventory, sourceIndex, out var entry))
                    return;

                int count = math.clamp(requestedCount, 1, entry.Count);
                UnequipInventoryIndex(equipment, sourceIndex, entry.Content);
                ContainerLootUtility.AddOrIncrementContainerStack(items, targetPlacedRefId, entry.Content, entry.SoulId, entry.SoulActorHandleValue, count);
                AddCorpseBackingInventory(ref systemState, ref contentBlob, targetPlacedRefId, entry.Content, entry.SoulId, entry.SoulActorHandleValue, count);
                AppendContainerDelta(ref systemState, targetPlacedRefId, entry.Content, count);
                RemovePlayerCountAt(inventory, sourceIndex, count, equipment);
            }
        }

        void DropHeldToContainer(ref SystemState systemState, 
            ref RuntimeContentBlob contentBlob,
            uint targetPlacedRefId,
            DynamicBuffer<PlayerInventoryItem> inventory,
            DynamicBuffer<ActorEquipmentSlot> equipment,
            ref InventoryHeldItemState held)
        {
            if (held.Active == 0 || targetPlacedRefId == 0u || !TryGetContainerItems(ref systemState, out var items))
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
            AddCorpseBackingInventory(ref systemState, ref contentBlob, targetPlacedRefId, entry.Content, entry.SoulId, entry.SoulActorHandleValue, count);
            AppendContainerDelta(ref systemState, targetPlacedRefId, entry.Content, count);
            RemovePlayerCountAt(inventory, sourceIndex, count, equipment);
            ClearHeld(ref held);
        }

        void UseHeld(ref SystemState systemState, 
            ref RuntimeContentBlob contentBlob,
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

            if (RuntimeContentMetadataResolver.TryResolveBookFixed(ref contentBlob, entry.Content, out _))
            {
                QueueBookRead(ref systemState, inventoryIndex);
                ClearHeld(ref held);
                return;
            }

            if (!hasEquipment || entry.Content.Kind != ContentReferenceKind.Item)
            {
                WarnUnsupportedUse(ref systemState, entry.Content);
                ClearHeld(ref held);
                return;
            }

            var itemHandle = new ItemDefHandle { Value = entry.Content.HandleValue };
            if (!RuntimeContentBlobUtility.TryGetItemEquipment(ref contentBlob, itemHandle, out var itemEquipment))
            {
                WarnUnsupportedUse(ref systemState, entry.Content);
                ClearHeld(ref held);
                return;
            }

            ToggleEquipment(equipment, inventoryIndex, entry, itemEquipment);
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

        void QueueBookRead(ref SystemState systemState, int inventoryIndex)
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
            in PlayerInventoryItem entry,
            in ItemEquipmentDef itemEquipment)
        {
            for (int i = equipment.Length - 1; i >= 0; i--)
            {
                var slot = equipment[i];
                if (slot.InventoryIndex == inventoryIndex
                    && slot.Content.Kind == entry.Content.Kind
                    && slot.Content.HandleValue == entry.Content.HandleValue)
                {
                    equipment.RemoveAt(i);
                    return;
                }
            }

            RemoveConflictingSlots(equipment, itemEquipment.Slot);
            equipment.Add(new ActorEquipmentSlot
            {
                Slot = itemEquipment.Slot,
                Content = entry.Content,
                InventoryIndex = inventoryIndex,
                Condition = ActorEquipmentConditionUtility.ResolveInitialCondition(
                    itemEquipment,
                    entry.Count,
                    entry.Condition,
                    entry.Content),
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

        void WarnUnsupportedUse(ref SystemState systemState, ContentReference content)
        {
            if (!SystemAPI.HasSingleton<InteractionPresentationState>())
                return;

            ref var presentation = ref SystemAPI.GetSingletonRW<InteractionPresentationState>().ValueRW;
            presentation.NotificationText = BuildCannotUseItemText();
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

        bool TryGetContainerItems(ref SystemState systemState, out DynamicBuffer<ContainerSessionItem> items)
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

        void RemoveCorpseBackingInventory(ref SystemState systemState, uint placedRefId, in ContainerSessionItem entry, int count)
        {
            Entity target = ResolveOpenContainerTarget(ref systemState, placedRefId);
            if (!ActorCorpseLootUtility.IsDeadLootableActor(systemState.EntityManager, target))
                return;

            if (!systemState.EntityManager.HasBuffer<ActorInventoryItem>(target))
                throw new InvalidOperationException("[VVardenfell][Corpse] Corpse has visible loot but no ActorInventoryItem buffer.");

            var actorInventory = systemState.EntityManager.GetBuffer<ActorInventoryItem>(target);
            DynamicBuffer<ActorEquipmentSlot> actorEquipment = systemState.EntityManager.HasBuffer<ActorEquipmentSlot>(target)
                ? systemState.EntityManager.GetBuffer<ActorEquipmentSlot>(target)
                : default;
            ulong previousSignature = actorEquipment.IsCreated
                ? ActorPresentationEquipmentUtility.BuildEquipmentSignature(actorEquipment)
                : 0UL;

            int removed = ActorInventoryBufferMutationUtility.RemoveActorItems(
                actorInventory,
                actorEquipment,
                entry.Content,
                entry.SoulId,
                entry.SoulActorHandleValue,
                count);
            if (removed != count)
                throw new InvalidOperationException("[VVardenfell][Corpse] Corpse inventory could not remove the requested item count.");

            ulong currentSignature = actorEquipment.IsCreated
                ? ActorPresentationEquipmentUtility.BuildEquipmentSignature(actorEquipment)
                : 0UL;
            if (previousSignature != currentSignature)
                MarkActorPresentationEquipmentDirty(ref systemState, target);
        }

        void AddCorpseBackingInventory(ref SystemState systemState, 
            ref RuntimeContentBlob contentBlob,
            uint placedRefId,
            ContentReference content,
            FixedString64Bytes soulId,
            int soulActorHandleValue,
            int count)
        {
            Entity target = ResolveOpenContainerTarget(ref systemState, placedRefId);
            if (!ActorCorpseLootUtility.IsDeadLootableActor(systemState.EntityManager, target))
                return;

            if (!systemState.EntityManager.HasBuffer<ActorInventoryItem>(target))
                systemState.EntityManager.AddBuffer<ActorInventoryItem>(target);

            ActorInventoryBufferMutationUtility.AddActorItems(
                ref contentBlob,
                systemState.EntityManager.GetBuffer<ActorInventoryItem>(target),
                content,
                soulId,
                soulActorHandleValue,
                count);
        }

        Entity ResolveOpenContainerTarget(ref SystemState systemState, uint placedRefId)
        {
            if (placedRefId == 0u || !SystemAPI.HasSingleton<ContainerWindowState>())
                return Entity.Null;

            var container = SystemAPI.GetSingleton<ContainerWindowState>();
            return container.OpenPlacedRefId == placedRefId ? container.OpenTargetEntity : Entity.Null;
        }

        void MarkActorPresentationEquipmentDirty(ref SystemState systemState, Entity actor)
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            ActorPresentationEquipmentUtility.QueueEnsurePresentationEquipmentDirty(
                systemState.EntityManager,
                ref ecb,
                actor,
                enabled: true);
            ecb.Playback(systemState.EntityManager);
            ecb.Dispose();
        }

        uint AppendContainerDelta(ref SystemState systemState, uint placedRefId, ContentReference content, int deltaCount)
        {
            if (placedRefId == 0u || !content.IsValid || deltaCount == 0)
                return 0u;

            if (_worldJournalQuery.CalculateEntityCount() != 1)
                throw new InvalidOperationException("[VVardenfell][Inventory] cannot mutate container contents without exactly one world journal entity.");

            Entity journalEntity = _worldJournalQuery.GetSingletonEntity();
            var state = systemState.EntityManager.GetComponentData<WorldJournalState>(journalEntity);
            uint sequence = state.NextSequence + 1u;
            state.NextSequence = sequence;
            systemState.EntityManager.SetComponentData(journalEntity, state);

            var journal = systemState.EntityManager.GetBuffer<WorldJournalEntry>(journalEntity);
            journal.Add(new WorldJournalEntry
            {
                Sequence = sequence,
                Kind = (byte)WorldJournalEntryKind.ContainerDelta,
                PlacedRefId = placedRefId,
                Content = content,
                DeltaCount = deltaCount,
            });
            return sequence;
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

        static FixedString128Bytes BuildCannotUseItemText()
        {
            var result = default(FixedString128Bytes);
            result.Append('Y'); result.Append('o'); result.Append('u'); result.Append(' ');
            result.Append('c'); result.Append('a'); result.Append('n'); result.Append('n'); result.Append('o'); result.Append('t'); result.Append(' ');
            result.Append('u'); result.Append('s'); result.Append('e'); result.Append(' ');
            result.Append('t'); result.Append('h'); result.Append('a'); result.Append('t'); result.Append(' ');
            result.Append('i'); result.Append('t'); result.Append('e'); result.Append('m'); result.Append('.');
            return result;
        }
    }
}
