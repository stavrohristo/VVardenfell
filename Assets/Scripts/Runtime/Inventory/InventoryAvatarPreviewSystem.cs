using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Inventory
{
    [UpdateInGroup(typeof(MorrowindPresentationBuildSystemGroup))]
    [UpdateBefore(typeof(ActorPresentationSpawnSystem))]
    public partial struct InventoryAvatarPreviewSystem : ISystem
    {
        Entity _previewEntity;
        ulong _lastSignature;
        ActorDefHandle _lastActor;

        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<RuntimeShellState>();
            systemState.RequireForUpdate<InventoryWindowState>();
            systemState.RequireForUpdate<PlayerTag>();
            systemState.RequireForUpdate<ActorEquipmentSlot>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
        }

        public void OnDestroy(ref SystemState systemState)
        {
            DestroyPreview(ref systemState);
            _previewEntity = Entity.Null;
        }

        public void OnUpdate(ref SystemState systemState)
        {
            var shell = SystemAPI.GetSingleton<RuntimeShellState>();
            var inventoryState = SystemAPI.GetSingleton<InventoryWindowState>();
            if (!ShouldShowPreview(shell, inventoryState))
            {
                DestroyPreview(ref systemState);
                return;
            }

            ref RuntimeContentBlob contentBlob = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;
            if (!RuntimeContentBlobUtility.TryGetActorHandleByIdHash(ref contentBlob, RuntimeContentKnownHashes.player, out var actorHandle) || !actorHandle.IsValid)
            {
                DestroyPreview(ref systemState);
                return;
            }

            Entity player = SystemAPI.GetSingletonEntity<PlayerTag>();
            if (!systemState.EntityManager.HasBuffer<ActorEquipmentSlot>(player))
            {
                DestroyPreview(ref systemState);
                return;
            }

            var equipment = systemState.EntityManager.GetBuffer<ActorEquipmentSlot>(player, true);
            ulong signature = BuildSignature(equipment);
            if (_previewEntity != Entity.Null
                && systemState.EntityManager.Exists(_previewEntity)
                && signature == _lastSignature
                && actorHandle.Value == _lastActor.Value)
            {
                return;
            }

            using var equipmentSnapshot = new NativeList<ActorEquipmentSlot>(equipment.Length, Allocator.Temp);
            for (int i = 0; i < equipment.Length; i++)
                equipmentSnapshot.Add(equipment[i]);

            DestroyPreview(ref systemState);
            CreatePreview(ref systemState, ref contentBlob, actorHandle, equipmentSnapshot.AsArray(), signature);
        }

        void CreatePreview(ref SystemState systemState, ref RuntimeContentBlob contentBlob, ActorDefHandle actorHandle, NativeArray<ActorEquipmentSlot> equipment, ulong signature)
        {
            Entity preview = systemState.EntityManager.CreateEntity();
            systemState.EntityManager.SetName(preview, "VVardenfell.InventoryAvatarPreview");
            systemState.EntityManager.AddComponent<InventoryAvatarPreviewTag>(preview);
            systemState.EntityManager.AddComponentData(preview, new ActorSpawnSource
            {
                Definition = actorHandle,
                FirstPerson = 0,
            });
            systemState.EntityManager.AddComponentData(preview, LocalTransform.FromPositionRotationScale(
                InventoryAvatarPreviewRuntimeUtility.Position,
                InventoryAvatarPreviewRuntimeUtility.Rotation,
                1f));
            systemState.EntityManager.AddComponentData(preview, new LocalToWorld
            {
                Value = float4x4.TRS(
                    InventoryAvatarPreviewRuntimeUtility.Position,
                    InventoryAvatarPreviewRuntimeUtility.Rotation,
                    new float3(1f)),
            });
            systemState.EntityManager.AddComponentData(preview, new MorrowindMovementState
            {
                Grounded = true,
                GroundNormal = math.up(),
            });
            systemState.EntityManager.AddComponent<ActorRenderVisible>(preview);
            systemState.EntityManager.SetComponentEnabled<ActorRenderVisible>(preview, true);
            systemState.EntityManager.AddComponent<ActorShadowCasterVisible>(preview);
            systemState.EntityManager.SetComponentEnabled<ActorShadowCasterVisible>(preview, false);

            var previewEquipment = systemState.EntityManager.AddBuffer<ActorEquipmentSlot>(preview);
            for (int i = 0; i < equipment.Length; i++)
                previewEquipment.Add(equipment[i]);

            int weaponType = ActorWeaponAnimationUtility.ResolveEquippedWeaponType(ref contentBlob, previewEquipment, out var weaponContent);
            systemState.EntityManager.AddComponentData(preview, new ActorWeaponAnimationState
            {
                WeaponContent = weaponContent,
                WeaponType = weaponType,
                Drawn = 1,
                Phase = ActorWeaponAnimationPhase.Equipped,
            });

            _previewEntity = preview;
            _lastActor = actorHandle;
            _lastSignature = signature;
        }

        void DestroyPreview(ref SystemState systemState)
        {
            if (_previewEntity == Entity.Null)
                return;

            var ecb = new EntityCommandBuffer(Allocator.Temp);
            if (systemState.EntityManager.Exists(_previewEntity))
                ecb.DestroyEntity(_previewEntity);

            foreach (var (attachment, entity) in
                     SystemAPI.Query<RefRO<ActorRigidEquipmentAttachment>>()
                         .WithEntityAccess())
            {
                if (attachment.ValueRO.Actor == _previewEntity)
                    ecb.DestroyEntity(entity);
            }

            ecb.Playback(systemState.EntityManager);
            ecb.Dispose();
            _previewEntity = Entity.Null;
            _lastActor = default;
            _lastSignature = 0ul;
        }

        static bool ShouldShowPreview(in RuntimeShellState shell, in InventoryWindowState inventoryState)
        {
            if (shell.ContainerOpen != 0 || shell.InventoryMenuDisabled != 0)
                return false;

            return shell.InventoryOpen != 0 || inventoryState.Pinned != 0;
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
