using Unity.Entities;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Inventory;
using VVardenfell.Runtime.Shell;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Player
{
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(InventoryItemActionSystem))]
    [UpdateBefore(typeof(RuntimeShellInputSystem))]
    public partial class LocalPlayerVisualEquipmentSyncSystem : SystemBase
    {
        EntityQuery _playerQuery;
        EntityQuery _dirtyVisualQuery;

        protected override void OnCreate()
        {
            _playerQuery = GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadOnly<ActorEquipmentSlot>());
            _dirtyVisualQuery = GetEntityQuery(
                ComponentType.ReadOnly<LocalPlayerVisual>(),
                ComponentType.ReadOnly<ActorPresentationEquipmentDirty>());
            RequireForUpdate(_playerQuery);
            RequireForUpdate(_dirtyVisualQuery);
        }

        protected override void OnUpdate()
        {
            Entity player = _playerQuery.GetSingletonEntity();
            var playerEquipment = EntityManager.GetBuffer<ActorEquipmentSlot>(player, true);
            var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);

            foreach (var (visual, entity) in
                     SystemAPI.Query<RefRO<LocalPlayerVisual>>()
                         .WithAll<ActorPresentationEquipmentDirty>()
                         .WithEntityAccess())
            {
                if (visual.ValueRO.Player != player)
                    continue;

                if (!EntityManager.HasBuffer<ActorEquipmentSlot>(entity))
                    ecb.AddBuffer<ActorEquipmentSlot>(entity);
            }

            ecb.Playback(EntityManager);
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
                        EntityManager,
                        ref dirtyEcb,
                        entity,
                        enabled: true);
                    markedDirty = true;
                }
            }

            if (markedDirty)
                dirtyEcb.Playback(EntityManager);
            dirtyEcb.Dispose();
        }
    }
}
