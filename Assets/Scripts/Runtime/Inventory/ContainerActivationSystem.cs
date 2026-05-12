using System;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Interactions;
using VVardenfell.Runtime.Shell;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.WorldState;

namespace VVardenfell.Runtime.Inventory
{
    [UpdateInGroup(typeof(MorrowindFramePhysicsQuerySystemGroup))]
    [UpdateAfter(typeof(PlayerInteractionActivationSystem))]
    public partial struct ContainerActivationSystem : ISystem
    {
        EntityQuery _requestQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _requestQuery = systemState.GetEntityQuery(ComponentType.ReadWrite<InteractionActivationRequest>());

            systemState.RequireForUpdate(_requestQuery);
            systemState.RequireForUpdate<RuntimeShellState>();
            systemState.RequireForUpdate<ContainerWindowState>();
            systemState.RequireForUpdate<ContainerWindowRequest>();
            systemState.RequireForUpdate<ContainerSessionHeader>();
            systemState.RequireForUpdate<ContainerSessionItem>();
            systemState.RequireForUpdate<PlayerInteractionFocus>();
            systemState.RequireForUpdate<InteractionActivationResult>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            var requestRef = _requestQuery.GetSingletonRW<InteractionActivationRequest>();
            ref var request = ref requestRef.ValueRW;
            if (request.Pending == 0 || request.Kind != (byte)InteractableKind.Container)
                return;

            systemState.Dependency.Complete();

            Entity target = request.TargetEntity;
            uint placedRefId = request.TargetPlacedRefId;
            uint sequence = request.Sequence;
            request.Pending = 0;
            request.TargetEntity = Entity.Null;

            ref var result = ref SystemAPI.GetSingletonRW<InteractionActivationResult>().ValueRW;
            result.Sequence = sequence;
            result.Kind = (byte)InteractableKind.Container;
            result.Success = 0;
            result.PendingNotification = 0;
            result.NotificationText = default;

            bool isAuthoredContainer = systemState.EntityManager.Exists(target) && systemState.EntityManager.HasComponent<ContainerAuthoring>(target);
            bool isCorpse = systemState.EntityManager.Exists(target) && ActorCorpseLootUtility.IsDeadLootableActor(systemState.EntityManager, target);
            if ((!isAuthoredContainer && !isCorpse)
                || !systemState.EntityManager.HasComponent<PlacedRefIdentity>(target))
            {
                Debug.LogWarning("[VVardenfell][Interaction] container activation request resolved to a missing or non-container logical entity.");
                ClearFocus(ref systemState);
                return;
            }

            ref RuntimeContentBlob contentBlob = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;
            var headers = SystemAPI.GetSingletonBuffer<ContainerSessionHeader>();
            var items = SystemAPI.GetSingletonBuffer<ContainerSessionItem>();
            int playerLevel = MorrowindLeveledItemResolverUtility.ResolvePlayerLevel(systemState.EntityManager);
            ContainerDefHandle definition = default;
            string title;
            if (isCorpse)
            {
                ActorCorpseLootUtility.RequireDeadLootableActor(systemState.EntityManager, target, placedRefId);
                ActorCorpseLootUtility.EnsureSessionInitialized(systemState.EntityManager, headers, items, target, placedRefId);
                title = ActorCorpseLootUtility.ResolveTitle(ref contentBlob, systemState.EntityManager, target);
            }
            else
            {
                var authoring = systemState.EntityManager.GetComponentData<ContainerAuthoring>(target);
                definition = authoring.Definition;
                EnsureContainerSessionInitialized(systemState.EntityManager, ref contentBlob, headers, items, placedRefId, definition, playerLevel);
                title = ContainerLootUtility.ResolveContainerTitle(ref contentBlob, definition);
            }

            ref var shell = ref SystemAPI.GetSingletonRW<RuntimeShellState>().ValueRW;
            ref var windowState = ref SystemAPI.GetSingletonRW<ContainerWindowState>().ValueRW;
            ContainerWindowRuntimeUtility.OpenContainer(
                ref shell,
                ref windowState,
                target,
                placedRefId,
                definition,
                title);
            windowState.SelectedItemIndex = ContainerLootUtility.FindFirstItemIndex(items, placedRefId);

            var requestState = SystemAPI.GetSingletonRW<ContainerWindowRequest>();
            requestState.ValueRW = default;

            ClearFocus(ref systemState);
            result.Success = 1;

        }

        static void EnsureContainerSessionInitialized(
            EntityManager entityManager,
            ref RuntimeContentBlob contentBlob,
            DynamicBuffer<ContainerSessionHeader> headers,
            DynamicBuffer<ContainerSessionItem> items,
            uint placedRefId,
            ContainerDefHandle definition,
            int playerLevel)
        {
            if (placedRefId == 0u || !definition.IsValid)
                return;

            if (ContainerLootUtility.FindHeaderIndex(headers, placedRefId) >= 0)
                return;

            headers.Add(new ContainerSessionHeader
            {
                PlacedRefId = placedRefId,
                Definition = definition,
            });

            ContainerLootUtility.MaterializeContainerContents(ref contentBlob, items, placedRefId, definition, playerLevel);
            ScriptVisibleSaveStateUtility.ApplyContainerOverlay(entityManager, placedRefId, items);
        }

        void ClearFocus(ref SystemState systemState)
        {
            var focus = SystemAPI.GetSingletonRW<PlayerInteractionFocus>();
            focus.ValueRW = new PlayerInteractionFocus
            {
                TargetEntity = Entity.Null,
            };
        }
    }
}
