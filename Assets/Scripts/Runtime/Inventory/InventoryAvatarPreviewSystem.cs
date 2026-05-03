using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Shell;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Inventory
{
    [UpdateInGroup(typeof(MorrowindWorldMutationSystemGroup))]
    [UpdateBefore(typeof(ActorPresentationSpawnSystem))]
    public partial class InventoryAvatarPreviewSystem : SystemBase
    {
        Entity _previewEntity;
        ulong _lastSignature;
        ActorDefHandle _lastActor;

        protected override void OnCreate()
        {
            RequireForUpdate<RuntimeShellState>();
            RequireForUpdate<InventoryWindowState>();
            RequireForUpdate<PlayerTag>();
            RequireForUpdate<ActorEquipmentSlot>();
        }

        protected override void OnDestroy()
        {
            if (_previewEntity != Entity.Null && EntityManager.Exists(_previewEntity))
                EntityManager.DestroyEntity(_previewEntity);
            _previewEntity = Entity.Null;
        }

        protected override void OnUpdate()
        {
            RuntimeContentDatabase contentDb = RuntimeContentDatabase.Active;
            if (contentDb == null || !contentDb.TryGetActorHandle("player", out var actorHandle) || !actorHandle.IsValid)
            {
                DestroyPreview();
                return;
            }

            var shell = SystemAPI.GetSingleton<RuntimeShellState>();
            var inventoryState = SystemAPI.GetSingleton<InventoryWindowState>();
            bool visible = shell.InventoryOpen != 0 || shell.ContainerOpen != 0 || inventoryState.Pinned != 0;
            if (!visible)
            {
                DestroyPreview();
                return;
            }

            Entity player = SystemAPI.GetSingletonEntity<PlayerTag>();
            if (!EntityManager.HasBuffer<ActorEquipmentSlot>(player))
            {
                DestroyPreview();
                return;
            }

            var equipment = EntityManager.GetBuffer<ActorEquipmentSlot>(player, true);
            ulong signature = BuildSignature(equipment);
            if (_previewEntity != Entity.Null
                && EntityManager.Exists(_previewEntity)
                && signature == _lastSignature
                && actorHandle.Value == _lastActor.Value)
            {
                return;
            }

            DestroyPreview();
            CreatePreview(actorHandle, equipment, signature);
        }

        void CreatePreview(ActorDefHandle actorHandle, DynamicBuffer<ActorEquipmentSlot> equipment, ulong signature)
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            Entity preview = ecb.CreateEntity();
            ecb.SetName(preview, new FixedString64Bytes("VVardenfell.InventoryAvatarPreview"));
            ecb.AddComponent(preview, new ActorSpawnSource
            {
                Definition = actorHandle,
                FirstPerson = 0,
            });
            ecb.AddComponent(preview, LocalTransform.FromPositionRotationScale(
                InventoryAvatarPreviewRuntimeUtility.Position,
                InventoryAvatarPreviewRuntimeUtility.Rotation,
                1f));
            ecb.AddComponent(preview, new LocalToWorld
            {
                Value = float4x4.TRS(
                    InventoryAvatarPreviewRuntimeUtility.Position,
                    InventoryAvatarPreviewRuntimeUtility.Rotation,
                    new float3(1f)),
            });
            ecb.AddComponent(preview, new MorrowindMovementState
            {
                Grounded = true,
                GroundNormal = math.up(),
            });
            ecb.AddComponent(preview, new ActorWeaponAnimationState
            {
                WeaponType = ActorWeaponAnimationUtility.NoWeaponType,
                Phase = ActorWeaponAnimationPhase.Hidden,
            });
            ecb.AddComponent<ActorRenderVisible>(preview);
            ecb.SetComponentEnabled<ActorRenderVisible>(preview, true);
            ecb.AddComponent<ActorShadowCasterVisible>(preview);
            ecb.SetComponentEnabled<ActorShadowCasterVisible>(preview, false);

            var previewEquipment = ecb.AddBuffer<ActorEquipmentSlot>(preview);
            for (int i = 0; i < equipment.Length; i++)
                previewEquipment.Add(equipment[i]);

            ecb.Playback(EntityManager);
            ecb.Dispose();

            _previewEntity = preview;
            _lastActor = actorHandle;
            _lastSignature = signature;
        }

        void DestroyPreview()
        {
            if (_previewEntity == Entity.Null)
                return;

            var ecb = new EntityCommandBuffer(Allocator.Temp);
            if (EntityManager.Exists(_previewEntity))
                ecb.DestroyEntity(_previewEntity);

            foreach (var (attachment, entity) in
                     SystemAPI.Query<RefRO<ActorRigidEquipmentAttachment>>()
                         .WithEntityAccess())
            {
                if (attachment.ValueRO.Actor == _previewEntity)
                    ecb.DestroyEntity(entity);
            }

            ecb.Playback(EntityManager);
            ecb.Dispose();
            _previewEntity = Entity.Null;
            _lastActor = default;
            _lastSignature = 0ul;
        }

        static ulong BuildSignature(DynamicBuffer<ActorEquipmentSlot> equipment)
        {
            unchecked
            {
                ulong hash = 1469598103934665603ul;
                for (int i = 0; i < equipment.Length; i++)
                {
                    var slot = equipment[i];
                    hash = (hash ^ (byte)slot.Slot) * 1099511628211ul;
                    hash = (hash ^ (uint)slot.Content.Kind) * 1099511628211ul;
                    hash = (hash ^ (uint)slot.Content.HandleValue) * 1099511628211ul;
                    hash = (hash ^ (uint)slot.InventoryIndex) * 1099511628211ul;
                    hash = (hash ^ slot.VisualMode) * 1099511628211ul;
                }

                return hash;
            }
        }
    }
}
