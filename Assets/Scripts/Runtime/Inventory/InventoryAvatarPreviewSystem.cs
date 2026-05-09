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
        EntityQuery _previewQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _previewQuery = systemState.GetEntityQuery(ComponentType.ReadOnly<InventoryAvatarPreviewTag>());
            systemState.RequireForUpdate<RuntimeShellState>();
            systemState.RequireForUpdate<InventoryWindowState>();
            systemState.RequireForUpdate<PlayerTag>();
            systemState.RequireForUpdate<PlayerRaceAppearance>();
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
            PlayerRaceAppearance appearance = systemState.EntityManager.HasComponent<PlayerRaceAppearance>(player)
                ? systemState.EntityManager.GetComponentData<PlayerRaceAppearance>(player)
                : default;
            if (appearance.RaceId.IsEmpty)
            {
                DestroyPreview(ref systemState);
                return;
            }

            ulong signature = BuildSignature(equipment, appearance);
            if (_previewEntity != Entity.Null
                && systemState.EntityManager.Exists(_previewEntity)
                && signature == _lastSignature
                && actorHandle.Value == _lastActor.Value)
            {
                DestroyPreviewOrphans(ref systemState);
                return;
            }

            using var equipmentSnapshot = new NativeList<ActorEquipmentSlot>(equipment.Length, Allocator.Temp);
            for (int i = 0; i < equipment.Length; i++)
                equipmentSnapshot.Add(equipment[i]);

            DestroyPreview(ref systemState);
            CreatePreview(ref systemState, ref contentBlob, actorHandle, equipmentSnapshot.AsArray(), appearance, signature);
        }

        void CreatePreview(
            ref SystemState systemState,
            ref RuntimeContentBlob contentBlob,
            ActorDefHandle actorHandle,
            NativeArray<ActorEquipmentSlot> equipment,
            in PlayerRaceAppearance appearance,
            ulong signature)
        {
            Entity preview = systemState.EntityManager.CreateEntity();
            systemState.EntityManager.SetName(preview, "VVardenfell.InventoryAvatarPreview");
            systemState.EntityManager.AddComponent<InventoryAvatarPreviewTag>(preview);
            systemState.EntityManager.AddComponentData(preview, new ActorSpawnSource
            {
                Definition = actorHandle,
                FirstPerson = 0,
            });
            systemState.EntityManager.AddComponentData(preview, new ActorRuntimeAppearance
            {
                RaceId = appearance.RaceId,
                HeadId = appearance.HeadId,
                HairId = appearance.HairId,
                Male = appearance.Male,
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
            systemState.EntityManager.AddComponent<ActorRigidEquipmentRenderOwnerActorDirty>(preview);
            systemState.EntityManager.SetComponentEnabled<ActorRigidEquipmentRenderOwnerActorDirty>(preview, false);

            _previewEntity = preview;
            _lastActor = actorHandle;
            _lastSignature = signature;
        }

        void DestroyPreview(ref SystemState systemState)
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            using var previewEntities = _previewQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < previewEntities.Length; i++)
            {
                if (systemState.EntityManager.Exists(previewEntities[i]))
                    ecb.DestroyEntity(previewEntities[i]);
            }

            foreach (var (attachment, entity) in
                     SystemAPI.Query<RefRO<ActorRigidEquipmentAttachment>>()
                         .WithEntityAccess())
            {
                if (Contains(previewEntities, attachment.ValueRO.Actor))
                    ecb.DestroyEntity(entity);
            }

            ecb.Playback(systemState.EntityManager);
            ecb.Dispose();
            _previewEntity = Entity.Null;
            _lastActor = default;
            _lastSignature = 0ul;
        }

        void DestroyPreviewOrphans(ref SystemState systemState)
        {
            if (_previewEntity == Entity.Null)
                return;

            var ecb = new EntityCommandBuffer(Allocator.Temp);
            using var previewEntities = _previewQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < previewEntities.Length; i++)
            {
                Entity entity = previewEntities[i];
                if (entity == _previewEntity)
                    continue;

                if (systemState.EntityManager.Exists(entity))
                    ecb.DestroyEntity(entity);

                foreach (var (attachment, attachmentEntity) in
                         SystemAPI.Query<RefRO<ActorRigidEquipmentAttachment>>()
                             .WithEntityAccess())
                {
                    if (attachment.ValueRO.Actor == entity)
                        ecb.DestroyEntity(attachmentEntity);
                }
            }

            ecb.Playback(systemState.EntityManager);
            ecb.Dispose();
        }

        static bool Contains(NativeArray<Entity> entities, Entity value)
        {
            for (int i = 0; i < entities.Length; i++)
            {
                if (entities[i] == value)
                    return true;
            }

            return false;
        }

        static bool ShouldShowPreview(in RuntimeShellState shell, in InventoryWindowState inventoryState)
        {
            if (shell.ContainerOpen != 0 || shell.InventoryMenuDisabled != 0)
                return false;

            return shell.InventoryOpen != 0 || inventoryState.Pinned != 0;
        }

        static ulong BuildSignature(DynamicBuffer<ActorEquipmentSlot> equipment, in PlayerRaceAppearance appearance)
        {
            unchecked
            {
                ulong hash = 1469598103934665603ul;
                hash = (hash ^ RuntimeContentStableHash.HashId(appearance.RaceId.ToString())) * 1099511628211ul;
                hash = (hash ^ RuntimeContentStableHash.HashId(appearance.HeadId.ToString())) * 1099511628211ul;
                hash = (hash ^ RuntimeContentStableHash.HashId(appearance.HairId.ToString())) * 1099511628211ul;
                hash = (hash ^ appearance.Male) * 1099511628211ul;
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
