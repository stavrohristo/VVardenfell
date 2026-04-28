using Unity.Entities;
using UnityEngine;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Interactions;
using VVardenfell.Runtime.Inventory;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Books
{
    [UpdateInGroup(typeof(MorrowindPhysicsPostQueryMutationSystemGroup))]
    [UpdateAfter(typeof(TeleportDoorTransitionSystem))]
    [UpdateBefore(typeof(LooseItemPickupSystem))]
    public partial class LooseBookReadSystem : SystemBase
    {
        EntityQuery _requestQuery;
        EntityQuery _focusQuery;

        protected override void OnCreate()
        {
            _requestQuery = GetEntityQuery(ComponentType.ReadWrite<InteractionActivationRequest>());
            _focusQuery = GetEntityQuery(ComponentType.ReadWrite<PlayerInteractionFocus>());

            RequireForUpdate(_requestQuery);
            RequireForUpdate(_focusQuery);
            RequireForUpdate<BookReadRequest>();
            RequireForUpdate<InteractionActivationResult>();
        }

        protected override void OnUpdate()
        {
            var activationRef = _requestQuery.GetSingletonRW<InteractionActivationRequest>();
            ref var activation = ref activationRef.ValueRW;
            if (activation.Pending == 0 || activation.Kind != (byte)InteractableKind.LooseItem)
                return;

            Entity target = activation.TargetEntity;
            if (!EntityManager.Exists(target)
                || !EntityManager.HasComponent<BookTag>(target)
                || !LooseCarryableResolver.TryResolveContent(RuntimeContentDatabase.Active, EntityManager, target, out ContentReference content)
                || !RuntimeContentMetadataResolver.TryResolveBook(RuntimeContentDatabase.Active, content, out _))
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

    [UpdateInGroup(typeof(MorrowindInputSystemGroup))]
    [UpdateBefore(typeof(BookReadRequestSystem))]
    public partial class InventoryBookReadRequestSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<BookInventoryReadRequest>();
            RequireForUpdate<BookReadRequest>();
            RequireForUpdate<PlayerInventoryItem>();
        }

        protected override void OnUpdate()
        {
            ref var inventoryRequest = ref SystemAPI.GetSingletonRW<BookInventoryReadRequest>().ValueRW;
            if (inventoryRequest.Pending == 0)
                return;

            int inventoryIndex = inventoryRequest.InventoryIndex;
            uint sequence = inventoryRequest.Sequence;
            inventoryRequest = default;
            inventoryRequest.InventoryIndex = -1;

            var inventory = SystemAPI.GetSingletonBuffer<PlayerInventoryItem>();
            if (inventoryIndex < 0 || inventoryIndex >= inventory.Length)
                return;

            ContentReference content = inventory[inventoryIndex].Content;
            if (!RuntimeContentMetadataResolver.TryResolveBook(RuntimeContentDatabase.Active, content, out _))
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
