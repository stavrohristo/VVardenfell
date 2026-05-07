using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Player
{
    [UpdateInGroup(typeof(MorrowindPresentationBuildSystemGroup))]
    [UpdateBefore(typeof(ActorPresentationSpawnSystem))]
    public partial struct CharacterGenerationRacePreviewSystem : ISystem
    {
        Entity _previewEntity;
        ulong _lastSignature;

        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<CharacterGenerationState>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
            systemState.RequireForUpdate<PlayerTag>();
        }

        public void OnDestroy(ref SystemState systemState)
        {
            DestroyPreview(ref systemState);
        }

        public void OnUpdate(ref SystemState systemState)
        {
            CharacterGenerationState charGen = SystemAPI.GetSingleton<CharacterGenerationState>();
            if ((CharacterGenerationMenu)charGen.CurrentMenu != CharacterGenerationMenu.Race)
            {
                DestroyPreview(ref systemState);
                return;
            }

            ref RuntimeContentBlob content = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;
            if (!RuntimeContentBlobUtility.TryGetActorHandleByIdHash(ref content, RuntimeContentKnownHashes.player, out var actorHandle) || !actorHandle.IsValid)
                throw new System.InvalidOperationException("[VVardenfell][CharGen] Race preview requires NPC_ 'player'.");

            Entity player = SystemAPI.GetSingletonEntity<PlayerTag>();
            ulong signature = BuildSignature(charGen, player, ref systemState);
            if (_previewEntity != Entity.Null
                && systemState.EntityManager.Exists(_previewEntity)
                && signature == _lastSignature)
            {
                return;
            }

            DestroyPreview(ref systemState);
            CreatePreview(ref systemState, actorHandle, player, charGen);
            _lastSignature = signature;
        }

        void CreatePreview(ref SystemState systemState, ActorDefHandle actorHandle, Entity player, in CharacterGenerationState charGen)
        {
            using var equipmentSnapshot = new NativeList<ActorEquipmentSlot>(Allocator.Temp);
            if (systemState.EntityManager.HasBuffer<ActorEquipmentSlot>(player))
            {
                DynamicBuffer<ActorEquipmentSlot> equipment = systemState.EntityManager.GetBuffer<ActorEquipmentSlot>(player, true);
                for (int i = 0; i < equipment.Length; i++)
                    equipmentSnapshot.Add(equipment[i]);
            }

            Entity preview = systemState.EntityManager.CreateEntity();
            systemState.EntityManager.SetName(preview, "VVardenfell.CharacterGenerationRacePreview");
            systemState.EntityManager.AddComponent<CharacterGenerationRacePreviewTag>(preview);
            systemState.EntityManager.AddComponentData(preview, new ActorSpawnSource
            {
                Definition = actorHandle,
                FirstPerson = 0,
            });
            systemState.EntityManager.AddComponentData(preview, new ActorRuntimeAppearance
            {
                RaceId = charGen.RaceId,
                HeadId = charGen.HeadId,
                HairId = charGen.HairId,
                Male = charGen.Male,
            });
            systemState.EntityManager.AddComponentData(preview, LocalTransform.FromPositionRotationScale(
                CharacterGenerationRacePreviewRuntimeUtility.Position,
                CharacterGenerationRacePreviewRuntimeUtility.Rotation,
                1f));
            systemState.EntityManager.AddComponentData(preview, new LocalToWorld
            {
                Value = float4x4.TRS(
                    CharacterGenerationRacePreviewRuntimeUtility.Position,
                    CharacterGenerationRacePreviewRuntimeUtility.Rotation,
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
            systemState.EntityManager.AddComponentData(preview, new ActorWeaponAnimationState
            {
                WeaponType = ActorWeaponAnimationUtility.NoWeaponType,
                Phase = ActorWeaponAnimationPhase.Hidden,
            });

            if (equipmentSnapshot.Length > 0)
            {
                DynamicBuffer<ActorEquipmentSlot> previewEquipment = systemState.EntityManager.AddBuffer<ActorEquipmentSlot>(preview);
                for (int i = 0; i < equipmentSnapshot.Length; i++)
                    previewEquipment.Add(equipmentSnapshot[i]);
            }

            _previewEntity = preview;
        }

        void DestroyPreview(ref SystemState systemState)
        {
            if (_previewEntity == Entity.Null)
                return;

            var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
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
            _lastSignature = 0ul;
        }

        static ulong BuildSignature(in CharacterGenerationState charGen, Entity player, ref SystemState systemState)
        {
            unchecked
            {
                ulong hash = 1469598103934665603ul;
                hash = (hash ^ RuntimeContentStableHash.HashId(charGen.RaceId.ToString())) * 1099511628211ul;
                hash = (hash ^ RuntimeContentStableHash.HashId(charGen.HeadId.ToString())) * 1099511628211ul;
                hash = (hash ^ RuntimeContentStableHash.HashId(charGen.HairId.ToString())) * 1099511628211ul;
                hash = (hash ^ charGen.Male) * 1099511628211ul;

                if (!systemState.EntityManager.HasBuffer<ActorEquipmentSlot>(player))
                    return hash;

                DynamicBuffer<ActorEquipmentSlot> equipment = systemState.EntityManager.GetBuffer<ActorEquipmentSlot>(player, true);
                for (int i = 0; i < equipment.Length; i++)
                {
                    ActorEquipmentSlot slot = equipment[i];
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
