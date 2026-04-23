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
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Content;
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


    [UpdateInGroup(typeof(MorrowindFixedPostPhysicsSystemGroup))]
    [UpdateAfter(typeof(TeleportDoorTransitionSystem))]
    public partial class LooseItemPickupSystem : SystemBase
    {
        readonly HashSet<uint> _loggedMissingInteractionSounds = new();

        EntityQuery _requestQuery;
        EntityQuery _focusQuery;

        protected override void OnCreate()
        {
            _requestQuery = GetEntityQuery(ComponentType.ReadWrite<InteractionActivationRequest>());
            _focusQuery = GetEntityQuery(ComponentType.ReadWrite<PlayerInteractionFocus>());

            RequireForUpdate(_requestQuery);
            RequireForUpdate(_focusQuery);
            RequireForUpdate<LogicalRefLookup>();
            RequireForUpdate<InteractionActivationResult>();
            RequireForUpdate<InteractionAudioRequestState>();
            RequireForUpdate<InteractionAudioRequest>();
            RequireForUpdate<PlayerInventoryItem>();
            RequireForUpdate<PickedItemRecord>();
            RequireForUpdate<WorldJournalEntry>();
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

            if (!EntityManager.Exists(target)
                || !EntityManager.HasComponent<ItemPickupAuthoring>(target)
                || !EntityManager.HasComponent<PlacedRefIdentity>(target))
            {
                Debug.LogWarning("[VVardenfell][Interaction] loose-item activation request resolved to a missing or non-item logical entity.");
                ClearFocus();
                return;
            }

            var itemAuthoring = EntityManager.GetComponentData<ItemPickupAuthoring>(target);
            var pickedItems = SystemAPI.GetSingletonBuffer<PickedItemRecord>();
            bool isRuntimeSpawnedItem = RuntimeSpawnRegistryUtility.IsRuntimeRefId(targetPlacedRefId);
            if (!isRuntimeSpawnedItem && HasPickedItem(pickedItems, targetPlacedRefId))
            {
                Debug.Log($"[VVardenfell][Interaction] ignored duplicate pickup for placedRef=0x{targetPlacedRefId:X8}.");
                ClearFocus();
                return;
            }

            string itemName = ResolveItemName(RuntimeContentDatabase.Active, itemAuthoring.Definition);
            ContentReference itemContent = ContainerLootUtility.ToContentReference(itemAuthoring.Definition);

            var inventory = SystemAPI.GetSingletonBuffer<PlayerInventoryItem>();
            int stackCount = AddInventoryItem(inventory, itemAuthoring.Definition);
            if (!isRuntimeSpawnedItem)
            {
                pickedItems.Add(new PickedItemRecord
                {
                    PlacedRefId = targetPlacedRefId,
                    Definition = itemAuthoring.Definition,
                });
                WorldJournalUtility.AppendLooseItemRemoved(EntityManager, targetPlacedRefId, itemContent);
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
            activationResult.NotificationText = ToFixedString($"Picked up {itemName}");

            Debug.Log($"[VVardenfell][Interaction] picked up '{itemName}' from placedRef=0x{targetPlacedRefId:X8}; stack={stackCount}.");
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

        static int AddInventoryItem(DynamicBuffer<PlayerInventoryItem> inventory, ItemDefHandle definition)
        {
            return ContainerLootUtility.AddInventoryStack(inventory, ContainerLootUtility.ToContentReference(definition), 1);
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

            Debug.Log($"[VVardenfell][Audio] queued {label} interaction one-shot: seq={sequence}, placedRef=0x{placedRefId:X8}, pos=({position.x:F2}, {position.y:F2}, {position.z:F2}).");
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

            Debug.Log($"[VVardenfell][Audio] {label} 0x{placedRefId:X8} {reason}");
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
            InteractionEntityDestroyUtility.DestroyLogicalRef(EntityManager, logicalEntity, ref logicalRefLookup);
        }

        static string ResolveItemName(RuntimeContentDatabase contentDb, ItemDefHandle definition)
        {
            if (contentDb == null || !definition.IsValid)
                return "item";

            ref readonly var item = ref contentDb.Get(definition);
            if (!string.IsNullOrWhiteSpace(item.Name))
                return item.Name;
            if (!string.IsNullOrWhiteSpace(item.Id))
                return item.Id;
            return "item";
        }

        static FixedString128Bytes ToFixedString(string value)
        {
            if (string.IsNullOrEmpty(value))
                return default;

            if (value.Length > 127)
                value = value.Substring(0, 127);

            return new FixedString128Bytes(value);
        }
    }

    [UpdateInGroup(typeof(MorrowindFixedPostPhysicsSystemGroup))]
    [UpdateAfter(typeof(TeleportDoorTransitionSystem))]
    [UpdateAfter(typeof(LooseItemPickupSystem))]
    [UpdateAfter(typeof(NpcInteractionDeferredSystem))]
    [UpdateAfter(typeof(ActivatorInteractionDeferredSystem))]
    public partial class PickedItemRespawnPruneSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<InteractionRuntimeState>();
            RequireForUpdate<PickedItemRecord>();
            RequireForUpdate<LogicalRefLookup>();
        }

        protected override void OnUpdate()
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
            foreach (var (placedRefId, entity) in SystemAPI
                         .Query<RefRO<PlacedRefIdentity>>()
                         .WithAll<LogicalRefTag, ItemPickupAuthoring, InteriorCellMember>()
                         .WithEntityAccess())
            {
                if (pickedSet.Contains(placedRefId.ValueRO.Value))
                    entitiesToDestroy.Add(entity);
            }

            if (entitiesToDestroy.Count == 0)
                return;

            CompleteDependency();

            Entity lookupEntity = SystemAPI.GetSingletonEntity<LogicalRefLookup>();
            var logicalRefLookup = EntityManager.GetComponentData<LogicalRefLookup>(lookupEntity);
            for (int i = 0; i < entitiesToDestroy.Count; i++)
                DestroyLogicalRef(entitiesToDestroy[i], ref logicalRefLookup);
            EntityManager.SetComponentData(lookupEntity, logicalRefLookup);

            Debug.Log($"[VVardenfell][Interaction] pruned {entitiesToDestroy.Count} previously picked loose items after interior spawn.");
        }

        void DestroyLogicalRef(Entity logicalEntity, ref LogicalRefLookup logicalRefLookup)
        {
            InteractionEntityDestroyUtility.DestroyLogicalRef(EntityManager, logicalEntity, ref logicalRefLookup);
        }
    }
}
