using System;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.WorldState;

namespace VVardenfell.Runtime.MorrowindScript
{
    [UpdateInGroup(typeof(MorrowindWorldMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptInterpreterSystem))]
    public partial class MorrowindScriptInventoryMutationApplySystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<MorrowindScriptRuntimeState>();
            RequireForUpdate<MorrowindScriptInventoryMutationRequest>();
            RequireForUpdate<LogicalRefLookup>();
        }

        protected override void OnUpdate()
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = EntityManager.GetBuffer<MorrowindScriptInventoryMutationRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            var lookup = SystemAPI.GetSingleton<LogicalRefLookup>();
            for (int i = 0; i < requests.Length; i++)
                ApplyRequest(requests[i], lookup);

            requests.Clear();
        }

        void ApplyRequest(in MorrowindScriptInventoryMutationRequest request, in LogicalRefLookup lookup)
        {
            if (!request.Content.IsValid || request.Count == 0)
                return;

            int count = NormalizeScriptCount(request.Count);
            if (count == 0)
                return;

            if (request.TargetMode == (byte)MorrowindScriptRefTargetMode.Player)
            {
                Entity inventoryEntity = WorldStateEntityQueryUtility.GetSingletonBufferOwner<PlayerInventoryItem>(EntityManager);
                if (inventoryEntity == Entity.Null || !EntityManager.HasBuffer<PlayerInventoryItem>(inventoryEntity))
                    throw new InvalidOperationException("[VVardenfell][MWScript] Player inventory mutation requested before player inventory was bootstrapped.");

                var inventory = EntityManager.GetBuffer<PlayerInventoryItem>(inventoryEntity);
                if (request.Operation == 0)
                    AddPlayerItem(inventory, request.Content, count);
                else
                    RemovePlayerItem(inventory, request.Content, count);
                return;
            }

            Entity target = ResolveTarget(request, lookup);
            if (target == Entity.Null || !EntityManager.Exists(target))
                throw new InvalidOperationException($"[VVardenfell][MWScript] Inventory mutation target ref={request.TargetPlacedRefId} is not loaded.");

            if (!EntityManager.HasBuffer<ActorInventoryItem>(target))
                throw new InvalidOperationException($"[VVardenfell][MWScript] Inventory mutation target ref={request.TargetPlacedRefId} has no actor inventory.");

            var actorInventory = EntityManager.GetBuffer<ActorInventoryItem>(target);
            if (request.Operation == 0)
                AddActorItem(actorInventory, request.Content, count);
            else
                RemoveActorItem(actorInventory, request.Content, count);
        }

        Entity ResolveTarget(in MorrowindScriptInventoryMutationRequest request, in LogicalRefLookup lookup)
        {
            if (request.TargetEntity != Entity.Null && EntityManager.Exists(request.TargetEntity))
                return request.TargetEntity;

            if (request.TargetPlacedRefId != 0u && lookup.Map.IsCreated && lookup.Map.TryGetValue(request.TargetPlacedRefId, out Entity target))
                return target;

            return Entity.Null;
        }

        static void AddPlayerItem(DynamicBuffer<PlayerInventoryItem> inventory, ContentReference content, int count)
        {
            for (int i = 0; i < inventory.Length; i++)
            {
                if (!SameContent(inventory[i].Content, content))
                    continue;

                var entry = inventory[i];
                entry.Count += count;
                inventory[i] = entry;
                return;
            }

            inventory.Add(new PlayerInventoryItem
            {
                Content = content,
                Count = count,
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

        static void AddActorItem(DynamicBuffer<ActorInventoryItem> inventory, ContentReference content, int count)
        {
            for (int i = 0; i < inventory.Length; i++)
            {
                if (!SameContent(inventory[i].Content, content))
                    continue;

                var entry = inventory[i];
                entry.Count += count;
                inventory[i] = entry;
                return;
            }

            inventory.Add(new ActorInventoryItem
            {
                Content = content,
                Count = count,
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
