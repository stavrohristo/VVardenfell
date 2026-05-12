using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Interactions;
using VVardenfell.Runtime.Inventory;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.WorldState;

namespace VVardenfell.Runtime.Books
{
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(BookReaderRequestSystem))]
    public partial class BookTakeApplySystem : SystemBase
    {
        EntityQuery _playerInventoryQuery;

        protected override void OnCreate()
        {
            _playerInventoryQuery = GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadWrite<PlayerInventoryItem>());

            RequireForUpdate<BookTakeRequest>();
            RequireForUpdate(_playerInventoryQuery);
            RequireForUpdate<PickedItemRecord>();
            RequireForUpdate<LogicalRefLookup>();
            RequireForUpdate<RuntimeContentBlobReference>();
            RequireForUpdate<InteractionAudioRequestState>();
            RequireForUpdate<InteractionAudioRequest>();
        }

        protected override void OnUpdate()
        {
            ref var request = ref SystemAPI.GetSingletonRW<BookTakeRequest>().ValueRW;
            if (request.Pending == 0)
                return;

            Entity source = request.SourceEntity;
            uint placedRefId = request.SourcePlacedRefId;
            ContentReference content = request.Content;
            request = default;

            if (!content.IsValid)
                throw new InvalidOperationException("[VVardenfell][Books] Take requested without valid book content.");
            if (!EntityManager.Exists(source))
                throw new InvalidOperationException("[VVardenfell][Books] Take requested for a missing world book entity.");

            ref RuntimeContentBlob contentBlob = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;
            if (!RuntimeContentMetadataResolver.TryResolveBookFixed(ref contentBlob, content, out _))
                throw new InvalidOperationException("[VVardenfell][Books] Take requested for content that is not a BOOK record.");

            Entity inventoryEntity = _playerInventoryQuery.GetSingletonEntity();
            var inventory = EntityManager.GetBuffer<PlayerInventoryItem>(inventoryEntity);
            ContainerLootUtility.AddInventoryStack(ref contentBlob, inventory, content, 1);
            PlayerEncumbranceDirtyUtility.MarkPlayerDirty(EntityManager, inventoryEntity);

            bool runtimeSpawned = RuntimeSpawnRegistryUtility.IsRuntimeRefId(placedRefId);
            if (!runtimeSpawned)
            {
                var pickedItems = SystemAPI.GetSingletonBuffer<PickedItemRecord>();
                if (!HasPickedItem(pickedItems, placedRefId))
                {
                    pickedItems.Add(new PickedItemRecord
                    {
                        PlacedRefId = placedRefId,
                        Definition = new ItemDefHandle { Value = content.HandleValue },
                    });
                    ScriptVisibleSaveStateUtility.UpsertRemoved(EntityManager, placedRefId, content, 1);
                }
            }

            QueueBookPickupSound(ref contentBlob, source, placedRefId);

            Entity lookupEntity = SystemAPI.GetSingletonEntity<LogicalRefLookup>();
            var lookup = EntityManager.GetComponentData<LogicalRefLookup>(lookupEntity);
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            InteractionEntityDestroyUtility.QueueDestroyLogicalRef(EntityManager, ref ecb, source, ref lookup);
            ecb.Playback(EntityManager);
            ecb.Dispose();
            EntityManager.SetComponentData(lookupEntity, lookup);
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

        void QueueBookPickupSound(ref RuntimeContentBlob contentBlob, Entity source, uint placedRefId)
        {
            if (!RuntimeContentBlobUtility.TryGetSoundHandleByIdHash(ref contentBlob, RuntimeContentStableHash.HashId("Item Book Up"), out var sound)
                || !sound.IsValid)
            {
                throw new InvalidOperationException("[VVardenfell][Books] Required pickup sound 'Item Book Up' is missing.");
            }

            float3 position = float3.zero;
            if (EntityManager.HasComponent<LocalToWorld>(source))
                position = EntityManager.GetComponentData<LocalToWorld>(source).Value.c3.xyz;
            else if (EntityManager.HasComponent<LocalTransform>(source))
                position = EntityManager.GetComponentData<LocalTransform>(source).Position;

            ref var audioState = ref SystemAPI.GetSingletonRW<InteractionAudioRequestState>().ValueRW;
            audioState.NextSequence++;
            var requests = SystemAPI.GetSingletonBuffer<InteractionAudioRequest>();
            requests.Add(new InteractionAudioRequest
            {
                Sequence = audioState.NextSequence,
                Sound = sound,
                Position = position,
                SourcePlacedRefId = placedRefId,
                Kind = (byte)InteractionAudioKind.LooseItem,
            });
        }
    }
}
