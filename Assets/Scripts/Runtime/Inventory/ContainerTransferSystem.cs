using System;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Shell;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.WorldState;

namespace VVardenfell.Runtime.Inventory
{
    [BurstCompile]
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(ContainerWindowStateSystem))]
    [UpdateBefore(typeof(RuntimeShellInputSystem))]
    public partial struct ContainerTransferSystem : ISystem
    {
        EntityQuery _playerInventoryQuery;
        EntityQuery _worldJournalQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _playerInventoryQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadWrite<PlayerInventoryItem>());
            _worldJournalQuery = systemState.GetEntityQuery(
                ComponentType.ReadWrite<WorldJournalState>(),
                ComponentType.ReadWrite<WorldJournalEntry>());

            systemState.RequireForUpdate<RuntimeShellState>();
            systemState.RequireForUpdate<ContainerWindowState>();
            systemState.RequireForUpdate<ContainerWindowRequest>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
            systemState.RequireForUpdate(_playerInventoryQuery);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState systemState)
        {
            ref var shell = ref SystemAPI.GetSingletonRW<RuntimeShellState>().ValueRW;
            ref var state = ref SystemAPI.GetSingletonRW<ContainerWindowState>().ValueRW;
            ref var request = ref SystemAPI.GetSingletonRW<ContainerWindowRequest>().ValueRW;

            if (request.PendingClose != 0)
            {
                request.PendingClose = 0;
                ContainerWindowRuntimeUtility.CloseContainer(ref shell, ref state);
            }

            if (shell.ContainerOpen == 0 || state.OpenPlacedRefId == 0u)
            {
                request.PendingTakeSelected = 0;
                request.PendingTakeAll = 0;
                return;
            }

            if (request.PendingTakeAll == 0 && request.PendingTakeSelected == 0)
                return;

            EnsureTransferStateReady(ref systemState);
            systemState.Dependency.Complete();

            uint placedRefId = state.OpenPlacedRefId;
            var items = SystemAPI.GetSingletonBuffer<ContainerSessionItem>();
            Entity inventoryEntity = _playerInventoryQuery.GetSingletonEntity();
            var inventory = systemState.EntityManager.GetBuffer<PlayerInventoryItem>(inventoryEntity);
            ref RuntimeContentBlob contentBlob = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;
            int transferredStacks = 0;

            if (request.PendingTakeAll != 0)
            {
                for (int i = items.Length - 1; i >= 0; i--)
                {
                    var entry = items[i];
                    if (entry.PlacedRefId != placedRefId || entry.Count <= 0)
                        continue;

                    RemoveCorpseBackingInventory(ref systemState, state.OpenTargetEntity, placedRefId, entry, entry.Count);
                    AppendContainerDelta(ref systemState, placedRefId, entry.Content, -entry.Count);
                    ContainerLootUtility.AddInventoryStack(ref contentBlob, inventory, entry.Content, entry.SoulId, entry.SoulActorHandleValue, entry.Count);
                    items.RemoveAt(i);
                    transferredStacks++;
                }

                request.PendingTakeAll = 0;
                request.PendingTakeSelected = 0;
            }
            else if (request.PendingTakeSelected != 0)
            {
                int selectedIndex = request.PendingSelectionChange != 0 ? request.SelectedItemIndex : state.SelectedItemIndex;
                if (selectedIndex >= 0 && selectedIndex < items.Length)
                {
                    var entry = items[selectedIndex];
                    if (entry.PlacedRefId == placedRefId && entry.Count > 0)
                    {
                        RemoveCorpseBackingInventory(ref systemState, state.OpenTargetEntity, placedRefId, entry, entry.Count);
                        AppendContainerDelta(ref systemState, placedRefId, entry.Content, -entry.Count);
                        ContainerLootUtility.AddInventoryStack(ref contentBlob, inventory, entry.Content, entry.SoulId, entry.SoulActorHandleValue, entry.Count);
                        items.RemoveAt(selectedIndex);
                        transferredStacks = 1;
                    }
                }

                request.PendingTakeSelected = 0;
            }

            if (transferredStacks > 0)
            {
                PlayerEncumbranceDirtyUtility.MarkPlayerDirty(systemState.EntityManager, inventoryEntity);
                TryQueueInteractionAudio(ref systemState, state.OpenTargetEntity, InteractionAudioKind.Container);
            }
        }

        void RemoveCorpseBackingInventory(ref SystemState systemState, Entity target, uint placedRefId, in ContainerSessionItem entry, int count)
        {
            if (!ActorCorpseLootUtility.IsDeadLootableActor(systemState.EntityManager, target))
                return;

            if (!systemState.EntityManager.HasBuffer<ActorInventoryItem>(target))
                throw new InvalidOperationException("[VVardenfell][Corpse] Corpse has visible loot but no ActorInventoryItem buffer.");

            var actorInventory = systemState.EntityManager.GetBuffer<ActorInventoryItem>(target);
            DynamicBuffer<ActorEquipmentSlot> equipment = systemState.EntityManager.HasBuffer<ActorEquipmentSlot>(target)
                ? systemState.EntityManager.GetBuffer<ActorEquipmentSlot>(target)
                : default;
            ulong previousSignature = equipment.IsCreated
                ? ActorPresentationEquipmentUtility.BuildEquipmentSignature(equipment)
                : 0UL;

            int removed = ActorInventoryBufferMutationUtility.RemoveActorItems(
                actorInventory,
                equipment,
                entry.Content,
                entry.SoulId,
                entry.SoulActorHandleValue,
                count);
            if (removed != count)
                throw new InvalidOperationException("[VVardenfell][Corpse] Corpse inventory could not remove the requested item count.");

            ulong currentSignature = equipment.IsCreated
                ? ActorPresentationEquipmentUtility.BuildEquipmentSignature(equipment)
                : 0UL;
            if (previousSignature != currentSignature)
                MarkActorPresentationEquipmentDirty(ref systemState, target);
        }

        void MarkActorPresentationEquipmentDirty(ref SystemState systemState, Entity actor)
        {
            var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
            ActorPresentationEquipmentUtility.QueueEnsurePresentationEquipmentDirty(
                systemState.EntityManager,
                ref ecb,
                actor,
                enabled: true);
            ecb.Playback(systemState.EntityManager);
            ecb.Dispose();
        }

        void TryQueueInteractionAudio(ref SystemState systemState, Entity target, InteractionAudioKind kind)
        {
            if (!SystemAPI.HasSingleton<InteractionAudioRequestState>() || !SystemAPI.HasSingleton<InteractionAudioRequest>())
                return;

            if (!systemState.EntityManager.Exists(target) || !systemState.EntityManager.HasComponent<PlacedRefIdentity>(target))
                return;

            uint placedRefId = systemState.EntityManager.GetComponentData<PlacedRefIdentity>(target).Value;
            if (!systemState.EntityManager.HasComponent<AudioEmitterAuthoring>(target))
                return;

            var emitter = systemState.EntityManager.GetComponentData<AudioEmitterAuthoring>(target);
            if (!emitter.PrimarySound.IsValid)
                return;

            float3 position = ResolveAudioPosition(ref systemState, target);
            ref var audioState = ref SystemAPI.GetSingletonRW<InteractionAudioRequestState>().ValueRW;
            uint sequence = audioState.NextSequence + 1u;
            audioState.NextSequence = sequence;

            var requests = SystemAPI.GetSingletonBuffer<InteractionAudioRequest>();
            requests.Add(new InteractionAudioRequest
            {
                Sequence = sequence,
                Sound = emitter.PrimarySound,
                Position = position,
                SourcePlacedRefId = placedRefId,
                Kind = (byte)kind,
            });

        }

        void EnsureTransferStateReady(ref SystemState systemState)
        {
            if (!SystemAPI.HasSingleton<ContainerSessionItem>())
                throw new InvalidOperationException("[VVardenfell][Container] cannot transfer items without exactly one ContainerSessionItem buffer.");

            if (_playerInventoryQuery.CalculateEntityCount() != 1)
                throw new InvalidOperationException("[VVardenfell][Container] cannot transfer items without exactly one player inventory entity.");

            if (_worldJournalQuery.CalculateEntityCount() != 1)
                throw new InvalidOperationException("[VVardenfell][Container] cannot transfer items without exactly one world journal entity.");
        }

        uint AppendContainerDelta(ref SystemState systemState, uint placedRefId, ContentReference content, int deltaCount)
        {
            if (placedRefId == 0u || !content.IsValid || deltaCount == 0)
                return 0u;

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

        float3 ResolveAudioPosition(ref SystemState systemState, Entity target)
        {
            if (systemState.EntityManager.HasComponent<LocalToWorld>(target))
                return systemState.EntityManager.GetComponentData<LocalToWorld>(target).Value.c3.xyz;

            if (systemState.EntityManager.HasComponent<LocalTransform>(target))
                return systemState.EntityManager.GetComponentData<LocalTransform>(target).Position;

            return float3.zero;
        }
    }
}
