using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Player
{
    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    [UpdateBefore(typeof(LocalPlayerVisualCombatSyncSystem))]
    public partial class LocalPlayerVisualEquipmentSyncSystem : SystemBase
    {
        EntityQuery _playerQuery;

        protected override void OnCreate()
        {
            _playerQuery = GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadOnly<ActorEquipmentSlot>());
            RequireForUpdate(_playerQuery);
            RequireForUpdate<LocalPlayerVisual>();
        }

        protected override void OnUpdate()
        {
            Entity player = _playerQuery.GetSingletonEntity();
            var playerEquipment = EntityManager.GetBuffer<ActorEquipmentSlot>(player, true);
            var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);

            foreach (var (visual, entity) in
                     SystemAPI.Query<RefRO<LocalPlayerVisual>>()
                         .WithEntityAccess())
            {
                if (visual.ValueRO.Player != player)
                    continue;

                if (!EntityManager.HasBuffer<ActorEquipmentSlot>(entity))
                    ecb.AddBuffer<ActorEquipmentSlot>(entity);
            }

            ecb.Playback(EntityManager);
            ecb.Dispose();

            foreach (var (visual, equipment) in
                     SystemAPI.Query<RefRO<LocalPlayerVisual>, DynamicBuffer<ActorEquipmentSlot>>())
            {
                if (visual.ValueRO.Player != player)
                    continue;

                equipment.Clear();
                for (int i = 0; i < playerEquipment.Length; i++)
                    equipment.Add(playerEquipment[i]);
            }
        }
    }
}
