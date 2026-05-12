using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Profiling;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Player;
using BoxCollider = Unity.Physics.BoxCollider;
using Collider = Unity.Physics.Collider;

using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Inventory;
using VVardenfell.Runtime.Physics;
using VVardenfell.Runtime.WorldState;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Interactions
{


    [UpdateInGroup(typeof(MorrowindPhysicsPostQueryMutationSystemGroup))]
    [UpdateAfter(typeof(TeleportDoorTransitionSystem))]
    public partial class LooseItemPickupSystem : SystemBase
    {
        readonly HashSet<uint> _loggedMissingInteractionSounds = new();

        EntityQuery _requestQuery;
        EntityQuery _focusQuery;
        EntityQuery _playerInventoryQuery;

        protected override void OnCreate()
        {
            _requestQuery = GetEntityQuery(ComponentType.ReadWrite<InteractionActivationRequest>());
            _focusQuery = GetEntityQuery(ComponentType.ReadWrite<PlayerInteractionFocus>());
            _playerInventoryQuery = GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadWrite<PlayerInventoryItem>());

            RequireForUpdate(_requestQuery);
            RequireForUpdate(_focusQuery);
            RequireForUpdate<LogicalRefLookup>();
            RequireForUpdate<InteractionActivationResult>();
            RequireForUpdate<InteractionAudioRequestState>();
            RequireForUpdate<InteractionAudioRequest>();
            RequireForUpdate(_playerInventoryQuery);
            RequireForUpdate<PickedItemRecord>();
            RequireForUpdate<RuntimeContentBlobReference>();
        }

        protected override void OnUpdate()
        {
            var requestRef = _requestQuery.GetSingletonRW<InteractionActivationRequest>();
            ref var request = ref requestRef.ValueRW;
            if (request.Pending == 0 || request.Kind != (byte)InteractableKind.LooseItem)
                return;

            CompleteDependency();

            Entity target = request.TargetEntity;
            uint targetPlacedRefId = request.TargetPlacedRefId;
            uint sequence = request.Sequence;
            request.Pending = 0;
            request.TargetEntity = Entity.Null;
            ref RuntimeContentBlob contentBlob = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;

            if (!EntityManager.Exists(target)
                || !EntityManager.HasComponent<PlacedRefIdentity>(target)
                || !LooseCarryableResolver.TryResolveContent(ref contentBlob, EntityManager, target, out ContentReference content, out _))
            {
                Debug.LogWarning("[VVardenfell][Interaction] loose-item activation request resolved to a missing or non-carryable logical entity.");
                ClearFocus();
                return;
            }

            var pickedItems = SystemAPI.GetSingletonBuffer<PickedItemRecord>();
            bool isRuntimeSpawnedItem = RuntimeSpawnRegistryUtility.IsRuntimeRefId(targetPlacedRefId);
            if (!isRuntimeSpawnedItem && HasPickedItem(pickedItems, targetPlacedRefId))
            {
                ClearFocus();
                return;
            }

            string itemName = RuntimeContentMetadataResolver.TryResolveCarryable(ref contentBlob, content, out var metadata)
                ? metadata.DisplayName
                : "item";

            Entity inventoryEntity = _playerInventoryQuery.GetSingletonEntity();
            var inventory = EntityManager.GetBuffer<PlayerInventoryItem>(inventoryEntity);
            AddInventoryItem(ref contentBlob, inventory, content, target);
            PlayerEncumbranceDirtyUtility.MarkPlayerDirty(EntityManager, inventoryEntity);
            if (!isRuntimeSpawnedItem)
            {
                pickedItems.Add(new PickedItemRecord
                {
                    PlacedRefId = targetPlacedRefId,
                    Definition = content.Kind == ContentReferenceKind.Item
                        ? new ItemDefHandle { Value = content.HandleValue }
                        : default,
                });
                ScriptVisibleSaveStateUtility.UpsertRemoved(EntityManager, targetPlacedRefId, content, 1);
            }

            TryQueueInteractionAudio(target, InteractionAudioKind.LooseItem, "item");

            Entity lookupEntity = SystemAPI.GetSingletonEntity<LogicalRefLookup>();
            var logicalRefLookup = EntityManager.GetComponentData<LogicalRefLookup>(lookupEntity);
            DestroyLogicalRef(target, ref logicalRefLookup);
            EntityManager.SetComponentData(lookupEntity, logicalRefLookup);

            ClearFocus();

            ref var activationResult = ref SystemAPI.GetSingletonRW<InteractionActivationResult>().ValueRW;
            activationResult.Sequence = sequence;
            activationResult.Kind = (byte)InteractableKind.LooseItem;
            activationResult.Success = 1;
            activationResult.PendingNotification = 1;
            activationResult.NotificationText = RuntimeFixedStringUtility.ToFixed128OrDefault($"Picked up {itemName}");

        }

        static bool HasPickedItem(DynamicBuffer<PickedItemRecord> pickedItems, uint placedRefId)
        {
            for (int i = 0; i < pickedItems.Length; i++)
            {
                if (pickedItems[i].PlacedRefId == placedRefId)
                    return true;
            }

            return false;
        }

        int AddInventoryItem(ref RuntimeContentBlob contentBlob, DynamicBuffer<PlayerInventoryItem> inventory, ContentReference content, Entity target)
        {
            if (EntityManager.Exists(target) && EntityManager.HasComponent<PlacedRefCapturedSoul>(target))
            {
                var soul = EntityManager.GetComponentData<PlacedRefCapturedSoul>(target);
                return ContainerLootUtility.AddInventoryStack(ref contentBlob, inventory, content, soul.SoulId, soul.SoulActorHandleValue, 1);
            }

            return ContainerLootUtility.AddInventoryStack(ref contentBlob, inventory, content, 1);
        }

        void TryQueueInteractionAudio(Entity target, InteractionAudioKind kind, string label)
        {
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

        void ClearFocus()
        {
            var focusRef = _focusQuery.GetSingletonRW<PlayerInteractionFocus>();
            focusRef.ValueRW = new PlayerInteractionFocus
            {
                TargetEntity = Entity.Null,
            };
        }

        void DestroyLogicalRef(Entity logicalEntity, ref LogicalRefLookup logicalRefLookup)
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            InteractionEntityDestroyUtility.QueueDestroyLogicalRef(EntityManager, ref ecb, logicalEntity, ref logicalRefLookup);
            ecb.Playback(EntityManager);
            ecb.Dispose();
        }

    }

    [UpdateInGroup(typeof(MorrowindPhysicsPostQueryMutationSystemGroup))]
    [UpdateAfter(typeof(TeleportDoorTransitionSystem))]
    [UpdateAfter(typeof(LooseItemPickupSystem))]
    [UpdateAfter(typeof(NpcInteractionDeferredSystem))]
    [UpdateAfter(typeof(ActivatorInteractionDeferredSystem))]
    public partial struct PickedItemRespawnPruneSystem : ISystem
    {
        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<InteractionRuntimeState>();
            systemState.RequireForUpdate<PickedItemRecord>();
            systemState.RequireForUpdate<LogicalRefLookup>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            ref var runtimeState = ref SystemAPI.GetSingletonRW<InteractionRuntimeState>().ValueRW;
            if (runtimeState.PendingPickedItemPrune == 0)
                return;

            runtimeState.PendingPickedItemPrune = 0;

            var pickedItems = SystemAPI.GetSingletonBuffer<PickedItemRecord>();
            if (pickedItems.Length == 0)
                return;

            using var pickedSet = new NativeParallelHashSet<uint>(pickedItems.Length, Allocator.Temp);
            for (int i = 0; i < pickedItems.Length; i++)
                pickedSet.Add(pickedItems[i].PlacedRefId);

            var entitiesToDestroy = new List<Entity>();
            ref RuntimeContentBlob contentBlob = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;
            foreach (var (placedRefId, entity) in SystemAPI
                         .Query<RefRO<PlacedRefIdentity>>()
                         .WithAll<LogicalRefTag, InteriorCellMember>()
                         .WithEntityAccess())
            {
                if (pickedSet.Contains(placedRefId.ValueRO.Value)
                    && LooseCarryableResolver.TryResolveContent(ref contentBlob, systemState.EntityManager, entity, out _))
                    entitiesToDestroy.Add(entity);
            }

            if (entitiesToDestroy.Count == 0)
                return;

            systemState.Dependency.Complete();

            Entity lookupEntity = SystemAPI.GetSingletonEntity<LogicalRefLookup>();
            var logicalRefLookup = systemState.EntityManager.GetComponentData<LogicalRefLookup>(lookupEntity);
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            for (int i = 0; i < entitiesToDestroy.Count; i++)
                InteractionEntityDestroyUtility.QueueDestroyLogicalRef(systemState.EntityManager, ref ecb, entitiesToDestroy[i], ref logicalRefLookup);
            ecb.Playback(systemState.EntityManager);
            ecb.Dispose();
            systemState.EntityManager.SetComponentData(lookupEntity, logicalRefLookup);

        }

    }
}
