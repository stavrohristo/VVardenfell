using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Shell;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.WorldState;

namespace VVardenfell.Runtime.Inventory
{
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(ContainerWindowStateSystem))]
    [UpdateBefore(typeof(RuntimeShellInputSystem))]
    public partial class ContainerTransferSystem : SystemBase
    {
        readonly HashSet<uint> _loggedMissingInteractionSounds = new();
        EntityQuery _playerInventoryQuery;

        protected override void OnCreate()
        {
            _playerInventoryQuery = GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadWrite<PlayerInventoryItem>());

            RequireForUpdate<RuntimeShellState>();
            RequireForUpdate<ContainerWindowState>();
            RequireForUpdate<ContainerWindowRequest>();
            RequireForUpdate<RuntimeContentBlobReference>();
            RequireForUpdate(_playerInventoryQuery);
        }

        protected override void OnUpdate()
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

            EnsureTransferStateReady();
            CompleteDependency();

            uint placedRefId = state.OpenPlacedRefId;
            var items = SystemAPI.GetSingletonBuffer<ContainerSessionItem>();
            Entity inventoryEntity = _playerInventoryQuery.GetSingletonEntity();
            var inventory = EntityManager.GetBuffer<PlayerInventoryItem>(inventoryEntity);
            ref RuntimeContentBlob contentBlob = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;
            int transferredStacks = 0;

            if (request.PendingTakeAll != 0)
            {
                for (int i = items.Length - 1; i >= 0; i--)
                {
                    var entry = items[i];
                    if (entry.PlacedRefId != placedRefId || entry.Count <= 0)
                        continue;

                    RemoveCorpseBackingInventory(state.OpenTargetEntity, placedRefId, entry, entry.Count);
                    WorldJournalUtility.AppendContainerDelta(EntityManager, placedRefId, entry.Content, -entry.Count);
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
                        RemoveCorpseBackingInventory(state.OpenTargetEntity, placedRefId, entry, entry.Count);
                        WorldJournalUtility.AppendContainerDelta(EntityManager, placedRefId, entry.Content, -entry.Count);
                        ContainerLootUtility.AddInventoryStack(ref contentBlob, inventory, entry.Content, entry.SoulId, entry.SoulActorHandleValue, entry.Count);
                        items.RemoveAt(selectedIndex);
                        transferredStacks = 1;
                    }
                }

                request.PendingTakeSelected = 0;
            }

            if (transferredStacks > 0)
            {
                PlayerEncumbranceDirtyUtility.MarkPlayerDirty(EntityManager, inventoryEntity);
                TryQueueInteractionAudio(state.OpenTargetEntity, InteractionAudioKind.Container, "container");
            }
        }

        void RemoveCorpseBackingInventory(Entity target, uint placedRefId, in ContainerSessionItem entry, int count)
        {
            if (!ActorCorpseLootUtility.IsDeadLootableActor(EntityManager, target))
                return;

            if (!EntityManager.HasBuffer<ActorInventoryItem>(target))
                throw new InvalidOperationException($"[VVardenfell][Corpse] Corpse ref={placedRefId} has visible loot but no ActorInventoryItem buffer.");

            var actorInventory = EntityManager.GetBuffer<ActorInventoryItem>(target);
            DynamicBuffer<ActorEquipmentSlot> equipment = EntityManager.HasBuffer<ActorEquipmentSlot>(target)
                ? EntityManager.GetBuffer<ActorEquipmentSlot>(target)
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
                throw new InvalidOperationException($"[VVardenfell][Corpse] Corpse ref={placedRefId} inventory could remove {removed} of requested {count} for content {entry.Content.Kind}:{entry.Content.HandleValue}.");

            ulong currentSignature = equipment.IsCreated
                ? ActorPresentationEquipmentUtility.BuildEquipmentSignature(equipment)
                : 0UL;
            if (previousSignature != currentSignature)
                MarkActorPresentationEquipmentDirty(target);
        }

        void MarkActorPresentationEquipmentDirty(Entity actor)
        {
            var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
            ActorPresentationEquipmentUtility.QueueEnsurePresentationEquipmentDirty(
                EntityManager,
                ref ecb,
                actor,
                enabled: true);
            ecb.Playback(EntityManager);
            ecb.Dispose();
        }

        void TryQueueInteractionAudio(Entity target, InteractionAudioKind kind, string label)
        {
            if (!SystemAPI.HasSingleton<InteractionAudioRequestState>() || !SystemAPI.HasSingleton<InteractionAudioRequest>())
                return;

            if (!EntityManager.Exists(target) || !EntityManager.HasComponent<PlacedRefIdentity>(target))
                return;

            uint placedRefId = EntityManager.GetComponentData<PlacedRefIdentity>(target).Value;
            if (!EntityManager.HasComponent<AudioEmitterAuthoring>(target))
            {
                WarnMissingInteractionSoundOnce(placedRefId, label, "has no AudioEmitterAuthoring component; skipping interaction one-shot.");
                return;
            }

            var emitter = EntityManager.GetComponentData<AudioEmitterAuthoring>(target);
            if (!emitter.PrimarySound.IsValid)
            {
                WarnMissingInteractionSoundOnce(placedRefId, label, "has no primary interaction sound; skipping interaction one-shot.");
                return;
            }

            float3 position = ResolveAudioPosition(target);
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

        void EnsureTransferStateReady()
        {
            if (!SystemAPI.HasSingleton<ContainerSessionItem>())
                throw new InvalidOperationException("[VVardenfell][Container] cannot transfer items without exactly one ContainerSessionItem buffer.");

            if (_playerInventoryQuery.CalculateEntityCount() != 1)
                throw new InvalidOperationException("[VVardenfell][Container] cannot transfer items without exactly one player inventory entity.");

            if (!WorldJournalUtility.TryGetJournalEntity(EntityManager, out _))
                throw new InvalidOperationException("[VVardenfell][Container] cannot transfer items without exactly one world journal entity.");
        }

        float3 ResolveAudioPosition(Entity target)
        {
            if (EntityManager.HasComponent<LocalToWorld>(target))
                return EntityManager.GetComponentData<LocalToWorld>(target).Value.c3.xyz;

            if (EntityManager.HasComponent<LocalTransform>(target))
                return EntityManager.GetComponentData<LocalTransform>(target).Position;

            return float3.zero;
        }

        void WarnMissingInteractionSoundOnce(uint placedRefId, string label, string reason)
        {
            if (placedRefId == 0u || !_loggedMissingInteractionSounds.Add(placedRefId))
                return;

        }
    }
}
