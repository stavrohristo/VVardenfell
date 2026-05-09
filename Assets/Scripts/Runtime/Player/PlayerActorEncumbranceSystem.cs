using Unity.Entities;
using Unity.Burst;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Inventory;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Player
{
    [BurstCompile]
    [UpdateAfter(typeof(ActorActiveMagicEffectSystem))]
    [UpdateInGroup(typeof(MorrowindPhysicsPostQueryMutationSystemGroup), OrderFirst = true)]
    public partial struct PlayerActorEncumbranceSystem : ISystem
    {
        EntityQuery _dirtyPlayerQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _dirtyPlayerQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadWrite<ActorAttributeSet>(),
                ComponentType.ReadWrite<ActorEffectStatModifiers>(),
                ComponentType.ReadWrite<ActorDerivedMovementStats>(),
                ComponentType.ReadOnly<PlayerInventoryItem>(),
                ComponentType.ReadOnly<PlayerEncumbranceDirty>());

            systemState.RequireForUpdate(_dirtyPlayerQuery);
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState systemState)
        {
            var contentBlobReference = SystemAPI.GetSingleton<RuntimeContentBlobReference>();
            if (!contentBlobReference.Blob.IsCreated)
                throw new System.InvalidOperationException("[VVardenfell][ContentBlob] Player encumbrance requires runtime content blob.");
            ref RuntimeContentBlob content = ref contentBlobReference.Blob.Value;

            foreach (var (attributes, effectModifiers, derived, inventory, entity) in
                     SystemAPI.Query<
                             RefRO<ActorAttributeSet>,
                             RefRO<ActorEffectStatModifiers>,
                             RefRW<ActorDerivedMovementStats>,
                             DynamicBuffer<PlayerInventoryItem>>()
                         .WithAll<PlayerTag, PlayerEncumbranceDirty>()
                         .WithEntityAccess())
            {
                float inventoryWeight = SumInventoryWeight(ref content, inventory);

                derived.ValueRW.CarryCapacity = MorrowindActorMovementStats.ComputeCarryCapacity(ref content, attributes.ValueRO);
                derived.ValueRW.Encumbrance = MorrowindActorMovementStats.ComputeEncumbrance(effectModifiers.ValueRO, inventoryWeight);
                derived.ValueRW.NormalizedEncumbrance = MorrowindActorMovementStats.ComputeNormalizedEncumbrance(
                    derived.ValueRO.Encumbrance,
                    derived.ValueRO.CarryCapacity);

                systemState.EntityManager.SetComponentEnabled<PlayerEncumbranceDirty>(entity, false);
            }
        }

        static float SumInventoryWeight(ref RuntimeContentBlob content, DynamicBuffer<PlayerInventoryItem> inventory)
        {
            float totalWeight = 0f;
            for (int i = 0; i < inventory.Length; i++)
            {
                var entry = inventory[i];
                if (entry.Count <= 0 || !entry.Content.IsValid)
                    continue;

                float weight = RuntimeContentBlobUtility.RequireCarryWeight(ref content, entry.Content);
                if (weight < 0f)
                    continue;

                totalWeight += weight * entry.Count;
            }

            return totalWeight;
        }
    }
}
