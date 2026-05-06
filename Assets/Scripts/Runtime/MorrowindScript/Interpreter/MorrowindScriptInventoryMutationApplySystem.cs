using System;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Inventory;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.WorldState;

namespace VVardenfell.Runtime.MorrowindScript
{
    [UpdateInGroup(typeof(MorrowindGameplayMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptInterpreterSystem))]
    public partial struct MorrowindScriptInventoryMutationApplySystem : ISystem
    {
        EntityQuery _playerInventoryQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _playerInventoryQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadWrite<PlayerInventoryItem>());

            systemState.RequireForUpdate<MorrowindScriptRuntimeState>();
            systemState.RequireForUpdate<MorrowindScriptInventoryMutationRequest>();
            systemState.RequireForUpdate<LogicalRefLookup>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = systemState.EntityManager.GetBuffer<MorrowindScriptInventoryMutationRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            var lookup = SystemAPI.GetSingleton<LogicalRefLookup>();
            ref RuntimeContentBlob contentBlob = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;
            for (int i = 0; i < requests.Length; i++)
                ApplyRequest(ref systemState, ref contentBlob, requests[i], lookup);

            requests.Clear();
        }

        void ApplyRequest(ref SystemState systemState, ref RuntimeContentBlob contentBlob, in MorrowindScriptInventoryMutationRequest request, in LogicalRefLookup lookup)
        {
            if (request.Operation == 2)
            {
                ApplyRemoveSoulGem(ref systemState, request);
                return;
            }

            if (!request.Content.IsValid || request.Count == 0)
                return;

            int count = NormalizeScriptCount(request.Count);
            if (count == 0)
                return;

            if (request.TargetMode == (byte)MorrowindScriptRefTargetMode.Player)
            {
                Entity inventoryEntity = RequirePlayerInventoryEntity("[VVardenfell][MWScript] Player inventory mutation requested before player inventory was bootstrapped.");
                var inventory = systemState.EntityManager.GetBuffer<PlayerInventoryItem>(inventoryEntity);
                if (request.Operation == 0)
                    AddPlayerItem(ref contentBlob, inventory, request.Content, count);
                else
                    RemovePlayerItem(inventory, request.Content, count);
                PlayerEncumbranceDirtyUtility.MarkPlayerDirty(systemState.EntityManager, inventoryEntity);
                return;
            }

            Entity target = ResolveTarget(ref systemState, request, lookup);
            if (target == Entity.Null || !systemState.EntityManager.Exists(target))
                throw new InvalidOperationException($"[VVardenfell][MWScript] Inventory mutation target ref={request.TargetPlacedRefId} is not loaded.");

            if (!systemState.EntityManager.HasBuffer<ActorInventoryItem>(target))
                throw new InvalidOperationException($"[VVardenfell][MWScript] Inventory mutation target ref={request.TargetPlacedRefId} has no actor inventory.");

            var actorInventory = systemState.EntityManager.GetBuffer<ActorInventoryItem>(target);
            if (request.Operation == 0)
                AddActorItem(ref contentBlob, actorInventory, request.Content, count);
            else
                RemoveActorItem(actorInventory, request.Content, count);
        }

        void ApplyRemoveSoulGem(ref SystemState systemState, in MorrowindScriptInventoryMutationRequest request)
        {
            if (request.TargetMode != (byte)MorrowindScriptRefTargetMode.Player || request.SoulActorHandleValue <= 0)
                throw new InvalidOperationException("[VVardenfell][MWScript] RemoveSoulGem supports only explicit Player targets with a known soul actor.");

            Entity inventoryEntity = RequirePlayerInventoryEntity("[VVardenfell][MWScript] RemoveSoulGem requested before player inventory was bootstrapped.");

            RemovePlayerSoulGem(systemState.EntityManager.GetBuffer<PlayerInventoryItem>(inventoryEntity), request.SoulActorHandleValue);
            PlayerEncumbranceDirtyUtility.MarkPlayerDirty(systemState.EntityManager, inventoryEntity);
        }

        Entity RequirePlayerInventoryEntity(string error)
        {
            int count = _playerInventoryQuery.CalculateEntityCount();
            if (count != 1)
                throw new InvalidOperationException($"{error} Found {count} player inventory entities.");

            return _playerInventoryQuery.GetSingletonEntity();
        }

        Entity ResolveTarget(ref SystemState systemState, in MorrowindScriptInventoryMutationRequest request, in LogicalRefLookup lookup)
        {
            if (request.TargetEntity != Entity.Null && systemState.EntityManager.Exists(request.TargetEntity))
                return request.TargetEntity;

            if (request.TargetPlacedRefId != 0u && lookup.Map.IsCreated && lookup.Map.TryGetValue(request.TargetPlacedRefId, out Entity target))
                return target;

            return Entity.Null;
        }

        static void AddPlayerItem(ref RuntimeContentBlob contentBlob, DynamicBuffer<PlayerInventoryItem> inventory, ContentReference content, int count)
        {
            int condition = InventoryConditionUtility.ResolveInitialCondition(ref contentBlob, content);
            for (int i = 0; i < inventory.Length; i++)
            {
                if (!SameContent(inventory[i].Content, content)
                    || !inventory[i].SoulId.IsEmpty
                    || !InventoryConditionUtility.CanStackCondition(content, inventory[i].Condition, condition))
                {
                    continue;
                }

                var entry = inventory[i];
                entry.Count += count;
                inventory[i] = entry;
                return;
            }

            inventory.Add(new PlayerInventoryItem
            {
                Content = content,
                Count = count,
                Condition = condition,
            });
        }

        static void RemovePlayerItem(DynamicBuffer<PlayerInventoryItem> inventory, ContentReference content, int count)
        {
            int remaining = count;
            for (int i = inventory.Length - 1; i >= 0 && remaining > 0; i--)
            {
                var entry = inventory[i];
                if (!SameContent(entry.Content, content))
                    continue;

                if (entry.Count <= remaining)
                {
                    remaining -= Math.Max(0, entry.Count);
                    inventory.RemoveAt(i);
                    continue;
                }

                entry.Count -= remaining;
                inventory[i] = entry;
                remaining = 0;
            }
        }

        static void RemovePlayerSoulGem(DynamicBuffer<PlayerInventoryItem> inventory, int soulActorHandleValue)
        {
            for (int i = inventory.Length - 1; i >= 0; i--)
            {
                var entry = inventory[i];
                if (entry.SoulActorHandleValue != soulActorHandleValue || entry.SoulId.IsEmpty)
                    continue;

                if (entry.Count <= 1)
                {
                    inventory.RemoveAt(i);
                    return;
                }

                entry.Count--;
                inventory[i] = entry;
                return;
            }
        }

        static void AddActorItem(ref RuntimeContentBlob contentBlob, DynamicBuffer<ActorInventoryItem> inventory, ContentReference content, int count)
        {
            int condition = InventoryConditionUtility.ResolveInitialCondition(ref contentBlob, content);
            for (int i = 0; i < inventory.Length; i++)
            {
                if (!SameContent(inventory[i].Content, content)
                    || !inventory[i].SoulId.IsEmpty
                    || !InventoryConditionUtility.CanStackCondition(content, inventory[i].Condition, condition))
                {
                    continue;
                }

                var entry = inventory[i];
                entry.Count += count;
                inventory[i] = entry;
                return;
            }

            inventory.Add(new ActorInventoryItem
            {
                Content = content,
                Count = count,
                Condition = condition,
                AuthoredOrder = inventory.Length,
            });
        }

        static void RemoveActorItem(DynamicBuffer<ActorInventoryItem> inventory, ContentReference content, int count)
        {
            int remaining = count;
            for (int i = inventory.Length - 1; i >= 0 && remaining > 0; i--)
            {
                var entry = inventory[i];
                if (!SameContent(entry.Content, content))
                    continue;

                if (entry.Count <= remaining)
                {
                    remaining -= Math.Max(0, entry.Count);
                    inventory.RemoveAt(i);
                    continue;
                }

                entry.Count -= remaining;
                inventory[i] = entry;
                remaining = 0;
            }
        }

        static bool SameContent(ContentReference left, ContentReference right)
            => left.Kind == right.Kind && left.HandleValue == right.HandleValue;

        static int NormalizeScriptCount(int count)
            => count < 0 ? (ushort)count : count;
    }
}
