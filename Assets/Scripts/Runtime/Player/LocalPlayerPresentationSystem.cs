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

            Entity firstPersonVisual = CreatePlayerVisual(ref systemState, 
                ref ecb,
                player,
                view,
                actorHandle,
                appearance,
                hasAppearance,
                firstPerson: true,
                actorRecipeFirstPerson: false,
                hiddenPartMask: BuildFirstPersonBodyHiddenPartMask(),
                visible: true);
            Entity thirdPersonVisual = CreatePlayerVisual(ref systemState, 
                ref ecb,
                player,
                view,
                actorHandle,
                appearance,
                hasAppearance,
                firstPerson: false,
                actorRecipeFirstPerson: false,
                hiddenPartMask: 0u,
                visible: false);

            ecb.AddComponent(player, new LocalPlayerPresentationState
            {
                Mode = PlayerViewMode.FirstPerson,
                ThirdPersonDistance = 3f,
                FirstPersonVisual = firstPersonVisual,
                ThirdPersonVisual = thirdPersonVisual,
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
            bool hasAppearance,
            bool firstPerson,
            bool actorRecipeFirstPerson,
            uint hiddenPartMask,
            bool visible)
        {
            Entity visual = ecb.CreateEntity();
            ecb.SetName(visual, new FixedString64Bytes(firstPerson
                ? "VVardenfell.PlayerFirstPersonVisual"
                : "VVardenfell.PlayerThirdPersonVisual"));
            ecb.AddComponent(visual, new ActorSpawnSource
            {
                Definition = actorHandle,
                FirstPerson = (byte)(actorRecipeFirstPerson ? 1 : 0),
            });
            if (hiddenPartMask != 0u)
                ecb.AddComponent(visual, new ActorHiddenVisualPartMask { Mask = hiddenPartMask });
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
                FirstPerson = (byte)(firstPerson ? 1 : 0),
            });
            ecb.AddComponent(visual, LocalTransform.Identity);
            ecb.AddComponent(visual, new LocalToWorld());
            ecb.AddComponent(visual, ResolveInitialMovementState(ref systemState, player));
            ecb.AddComponent(visual, new ActorWeaponAnimationState
            {
                WeaponType = ActorWeaponAnimationUtility.NoWeaponType,
                Phase = ActorWeaponAnimationPhase.Hidden,
            });
            ecb.AddComponent<ActorRenderVisible>(visual);
            ecb.SetComponentEnabled<ActorRenderVisible>(visual, visible);
            ecb.AddComponent<ActorShadowCasterVisible>(visual);
            ecb.SetComponentEnabled<ActorShadowCasterVisible>(visual, !firstPerson);

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

        static uint BuildFirstPersonBodyHiddenPartMask()
            => ActorVisualContentRules.PartMask(ActorVisualPartReference.Head)
               | ActorVisualContentRules.PartMask(ActorVisualPartReference.Hair);

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
