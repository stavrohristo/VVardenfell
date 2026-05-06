using Unity.Burst;
using Unity.Entities;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Player
{
    [BurstCompile]
    [UpdateInGroup(typeof(MorrowindGameplayMutationSystemGroup))]
    public partial struct LocalPlayerVisualEquipmentSyncSystem : ISystem
    {
        EntityQuery _playerQuery;
        EntityQuery _dirtyVisualQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _playerQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadOnly<ActorEquipmentSlot>());
            _dirtyVisualQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<LocalPlayerVisual>(),
                ComponentType.ReadOnly<ActorPresentationEquipmentDirty>());
            systemState.RequireForUpdate(_playerQuery);
            systemState.RequireForUpdate(_dirtyVisualQuery);
        }
        [BurstCompile]
        public void OnUpdate(ref SystemState systemState)
        {
            Entity player = _playerQuery.GetSingletonEntity();
            var playerEquipment = systemState.EntityManager.GetBuffer<ActorEquipmentSlot>(player, true);
            var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);

            foreach (var (visual, entity) in
                     SystemAPI.Query<RefRO<LocalPlayerVisual>>()
                         .WithAll<ActorPresentationEquipmentDirty>()
                         .WithEntityAccess())
            {
                if (visual.ValueRO.Player != player)
                    continue;

                if (!systemState.EntityManager.HasBuffer<ActorEquipmentSlot>(entity))
                    ecb.AddBuffer<ActorEquipmentSlot>(entity);
            }

            ecb.Playback(systemState.EntityManager);
            ecb.Dispose();

            var dirtyEcb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
            bool markedDirty = false;
            foreach (var (visual, equipment, entity) in
                     SystemAPI.Query<RefRO<LocalPlayerVisual>, DynamicBuffer<ActorEquipmentSlot>>()
                         .WithAll<ActorPresentationEquipmentDirty>()
                         .WithEntityAccess())
            {
                if (visual.ValueRO.Player != player)
                    continue;

                ulong previousSignature = ActorPresentationEquipmentUtility.BuildEquipmentSignature(equipment);
                equipment.Clear();
                for (int i = 0; i < playerEquipment.Length; i++)
                    equipment.Add(playerEquipment[i]);
                ulong currentSignature = ActorPresentationEquipmentUtility.BuildEquipmentSignature(equipment);
                if (previousSignature != currentSignature)
                {
                    ActorPresentationEquipmentUtility.QueueEnsurePresentationEquipmentDirty(
                        systemState.EntityManager,
                        ref dirtyEcb,
                        entity,
                        enabled: true);
                    markedDirty = true;
                }
            }

            if (markedDirty)
                dirtyEcb.Playback(systemState.EntityManager);
            dirtyEcb.Dispose();
        }
    }
}
