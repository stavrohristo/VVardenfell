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
    public partial struct LocalPlayerPresentationSpawnSystem : ISystem
    {
        EntityQuery _missingPresentationQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _missingPresentationQuery = systemState.GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<PlayerTag>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<LocalPlayerPresentationState>(),
                },
            });

            systemState.RequireForUpdate(_missingPresentationQuery);
            systemState.RequireForUpdate<PlayerViewComponent>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            ref RuntimeContentBlob contentBlob = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;
            if (!RuntimeContentBlobUtility.TryGetActorHandleByIdHash(ref contentBlob, RuntimeContentKnownHashes.player, out var actorHandle) || !actorHandle.IsValid)
                throw new System.InvalidOperationException("[VVardenfell] local player presentation requires NPC_ 'player'.");

            Entity player = Entity.Null;
            foreach (var (_, entity) in
                     SystemAPI.Query<RefRO<PlayerTag>>()
                         .WithNone<LocalPlayerPresentationState>()
                         .WithEntityAccess())
            {
                player = entity;
                break;
            }

            if (player == Entity.Null)
                return;

            Entity view = Entity.Null;
            foreach (var (playerView, entity) in
                     SystemAPI.Query<RefRO<PlayerViewComponent>>()
                         .WithEntityAccess())
            {
                if (playerView.ValueRO.ControlledCharacter == player)
                {
                    view = entity;
                    break;
                }
            }

            if (view == Entity.Null)
                return;

            var ecb = new EntityCommandBuffer(Allocator.Temp);
            PlayerRaceAppearance appearance = systemState.EntityManager.HasComponent<PlayerRaceAppearance>(player)
                ? systemState.EntityManager.GetComponentData<PlayerRaceAppearance>(player)
                : default;
            bool hasAppearance = !appearance.RaceId.IsEmpty;

            Entity playerVisual = CreatePlayerVisual(ref systemState,
                ref ecb,
                player,
                view,
                actorHandle,
                appearance,
                hasAppearance);

            ecb.AddComponent(player, new LocalPlayerPresentationState
            {
                Mode = PlayerViewMode.FirstPerson,
                ThirdPersonDistance = 3f,
                FirstPersonVisual = playerVisual,
                ThirdPersonVisual = playerVisual,
                Actor = actorHandle,
            });
            ecb.AddComponent(player, new LocalPlayerPresentationPose());
            ecb.Playback(systemState.EntityManager);
            ecb.Dispose();
        }

        Entity CreatePlayerVisual(ref SystemState systemState, 
            ref EntityCommandBuffer ecb,
            Entity player,
            Entity view,
            ActorDefHandle actorHandle,
            PlayerRaceAppearance appearance,
            bool hasAppearance)
        {
            Entity visual = ecb.CreateEntity();
            ecb.AddComponent(visual, new ActorSpawnSource
            {
                Definition = actorHandle,
                FirstPerson = 0,
            });
            if (hasAppearance)
            {
                ecb.AddComponent(visual, new ActorRuntimeAppearance
                {
                    RaceId = appearance.RaceId,
                    HeadId = appearance.HeadId,
                    HairId = appearance.HairId,
                    Male = appearance.Male,
                });
            }
            ecb.AddComponent(visual, new LocalPlayerVisual
            {
                Player = player,
                View = view,
                FirstPerson = 1,
            });
            ecb.AddComponent(visual, LocalTransform.Identity);
            ecb.AddComponent(visual, new LocalToWorld());
            ecb.AddComponent(visual, ResolveInitialMovementState(ref systemState, player));
            ecb.AddComponent(visual, new ActorWeaponAnimationState
            {
                WeaponType = ActorWeaponAnimationUtility.NoWeaponType,
                Phase = ActorWeaponAnimationPhase.Hidden,
            });
            ecb.AddComponent<ActorRigidEquipmentRenderOwnerActorDirty>(visual);
            ecb.SetComponentEnabled<ActorRigidEquipmentRenderOwnerActorDirty>(visual, false);
            ecb.AddComponent<ActorRenderVisible>(visual);
            ecb.SetComponentEnabled<ActorRenderVisible>(visual, true);
            ecb.AddComponent<ActorShadowCasterVisible>(visual);
            ecb.SetComponentEnabled<ActorShadowCasterVisible>(visual, true);

            if (systemState.EntityManager.HasBuffer<ActorEquipmentSlot>(player))
            {
                var playerEquipment = systemState.EntityManager.GetBuffer<ActorEquipmentSlot>(player, true);
                if (playerEquipment.Length > 0)
                {
                    var equipmentBuffer = ecb.AddBuffer<ActorEquipmentSlot>(visual);
                    for (int i = 0; i < playerEquipment.Length; i++)
                        equipmentBuffer.Add(playerEquipment[i]);
                }
            }

            return visual;
        }

        MorrowindMovementState ResolveInitialMovementState(ref SystemState systemState, Entity player)
        {
            if (systemState.EntityManager.HasComponent<MorrowindMovementState>(player))
                return systemState.EntityManager.GetComponentData<MorrowindMovementState>(player);

            return new MorrowindMovementState
            {
                GroundNormal = math.up(),
            };
        }

    }

    [UpdateInGroup(typeof(MorrowindGameplayInputSystemGroup))]
    [UpdateAfter(typeof(PlayerInputReceivingSystem))]
    public partial struct LocalPlayerViewModeSystem : ISystem
    {
        EntityQuery _dirtyQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _dirtyQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<LocalPlayerPresentationState>(),
                ComponentType.ReadOnly<LocalPlayerViewModeDirty>());

            systemState.RequireForUpdate(_dirtyQuery);
            systemState.RequireForUpdate<LocalPlayerPresentationState>();
            systemState.RequireForUpdate<PlayerCharacterControl>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            var stateEntity = SystemAPI.GetSingletonEntity<LocalPlayerPresentationState>();
            if (!SystemAPI.IsComponentEnabled<LocalPlayerViewModeDirty>(stateEntity))
                return;

            var state = systemState.EntityManager.GetComponentData<LocalPlayerPresentationState>(stateEntity);
            var controlEntity = SystemAPI.GetSingletonEntity<PlayerCharacterControl>();
            var control = systemState.EntityManager.GetComponentData<PlayerCharacterControl>(controlEntity);

            if (control.ToggleViewPressed)
            {
                state.Mode = state.Mode == PlayerViewMode.FirstPerson
                    ? PlayerViewMode.ThirdPerson
                    : PlayerViewMode.FirstPerson;
                systemState.EntityManager.SetComponentData(stateEntity, state);
                control.ToggleViewPressed = false;
                systemState.EntityManager.SetComponentData(controlEntity, control);
            }

            SetVisualGpuActive(ref systemState, state.FirstPersonVisual);
            SetVisualGpuActive(ref systemState, state.ThirdPersonVisual);
            systemState.EntityManager.SetComponentEnabled<LocalPlayerViewModeDirty>(stateEntity, false);
        }

        void SetVisualGpuActive(ref SystemState systemState, Entity entity)
        {
            if (entity == Entity.Null
                || !systemState.EntityManager.Exists(entity)
                || !systemState.EntityManager.HasComponent<ActorRenderVisible>(entity))
            {
                return;
            }

            systemState.EntityManager.SetComponentEnabled<ActorRenderVisible>(entity, true);
        }
    }
}
