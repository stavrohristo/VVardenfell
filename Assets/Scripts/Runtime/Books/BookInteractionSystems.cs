using Unity.Entities;
using UnityEngine;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Interactions;
using VVardenfell.Runtime.Inventory;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Books
{
    [UpdateInGroup(typeof(MorrowindPhysicsPostQueryMutationSystemGroup))]
    [UpdateAfter(typeof(TeleportDoorTransitionSystem))]
    [UpdateBefore(typeof(LooseItemPickupSystem))]
    public partial struct LooseBookReadSystem : ISystem
    {
        EntityQuery _requestQuery;
        EntityQuery _focusQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _requestQuery = systemState.GetEntityQuery(ComponentType.ReadWrite<InteractionActivationRequest>());
            _focusQuery = systemState.GetEntityQuery(ComponentType.ReadWrite<PlayerInteractionFocus>());

            systemState.RequireForUpdate(_requestQuery);
            systemState.RequireForUpdate(_focusQuery);
            systemState.RequireForUpdate<BookReadRequest>();
            systemState.RequireForUpdate<InteractionActivationResult>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            var activationRef = _requestQuery.GetSingletonRW<InteractionActivationRequest>();
            ref var activation = ref activationRef.ValueRW;
            if (activation.Pending == 0 || activation.Kind != (byte)InteractableKind.LooseItem)
                return;

            Entity target = activation.TargetEntity;
            ref RuntimeContentBlob contentBlob = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;
            ContentReference content = default;
            if (!systemState.EntityManager.Exists(target)
                || !systemState.EntityManager.HasComponent<BookTag>(target)
                || !LooseCarryableResolver.TryResolveContent(ref contentBlob, systemState.EntityManager, target, out content)
                || !RuntimeContentMetadataResolver.TryResolveBook(ref contentBlob, content, out _))
            {
                return;
            }

            uint placedRefId = activation.TargetPlacedRefId;
            uint sequence = activation.Sequence;
            activation.Pending = 0;
            activation.TargetEntity = Entity.Null;

            ref var readRequest = ref SystemAPI.GetSingletonRW<BookReadRequest>().ValueRW;
            readRequest = new BookReadRequest
            {
                Pending = 1,
                Source = (byte)BookReadSource.World,
                SourceEntity = target,
                SourcePlacedRefId = placedRefId,
                Content = content,
                InventoryIndex = -1,
                AllowTake = 1,
                Sequence = sequence,
            };

            ClearFocus();

            ref var result = ref SystemAPI.GetSingletonRW<InteractionActivationResult>().ValueRW;
            result.Sequence = sequence;
            result.Kind = (byte)InteractableKind.LooseItem;
            result.Success = 1;
            result.PendingNotification = 0;
            result.NotificationText = default;
        }

        void ClearFocus()
        {
            var focusRef = _focusQuery.GetSingletonRW<PlayerInteractionFocus>();
            focusRef.ValueRW = new PlayerInteractionFocus
            {
                TargetEntity = Entity.Null,
            };
        }
    }

    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateBefore(typeof(BookReadRequestSystem))]
    public partial struct InventoryBookReadRequestSystem : ISystem
    {
        EntityQuery _playerInventoryQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _playerInventoryQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadOnly<PlayerInventoryItem>());

            systemState.RequireForUpdate<BookInventoryReadRequest>();
            systemState.RequireForUpdate<BookReadRequest>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
            systemState.RequireForUpdate(_playerInventoryQuery);
        }

        public void OnUpdate(ref SystemState systemState)
        {
            ref var inventoryRequest = ref SystemAPI.GetSingletonRW<BookInventoryReadRequest>().ValueRW;
            if (inventoryRequest.Pending == 0)
                return;

            int inventoryIndex = inventoryRequest.InventoryIndex;
            uint sequence = inventoryRequest.Sequence;
            inventoryRequest = default;
            inventoryRequest.InventoryIndex = -1;

            Entity inventoryEntity = _playerInventoryQuery.GetSingletonEntity();
            var inventory = systemState.EntityManager.GetBuffer<PlayerInventoryItem>(inventoryEntity, true);
            if (inventoryIndex < 0 || inventoryIndex >= inventory.Length)
                return;

            ContentReference content = inventory[inventoryIndex].Content;
            ref RuntimeContentBlob contentBlob = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;
            if (!RuntimeContentMetadataResolver.TryResolveBook(ref contentBlob, content, out _))
            {
                Debug.LogWarning("[VVardenfell][Books] inventory read request targeted a non-book item.");
                return;
            }

            ref var readRequest = ref SystemAPI.GetSingletonRW<BookReadRequest>().ValueRW;
            readRequest = new BookReadRequest
            {
                Pending = 1,
                Source = (byte)BookReadSource.Inventory,
                SourceEntity = Entity.Null,
                SourcePlacedRefId = 0u,
                Content = content,
                InventoryIndex = inventoryIndex,
                AllowTake = 0,
                Sequence = sequence,
            };
        }
    }
}
