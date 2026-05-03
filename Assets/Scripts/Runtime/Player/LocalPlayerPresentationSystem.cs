using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Inventory;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Player
{
    [UpdateInGroup(typeof(MorrowindWorldMutationSystemGroup))]
    [UpdateBefore(typeof(ActorPresentationSpawnSystem))]
    public partial class LocalPlayerPresentationSpawnSystem : SystemBase
    {
        static bool s_MissingPlayerActorWarned;

        protected override void OnCreate()
        {
            RequireForUpdate<PlayerTag>();
            RequireForUpdate<PlayerViewComponent>();
            RequireForUpdate<PlayerInventoryItem>();
        }

        protected override void OnUpdate()
        {
            CleanupOrphanVisuals();

            RuntimeContentDatabase contentDb = RuntimeContentDatabase.Active;
            if (contentDb == null)
                return;
            if (!contentDb.TryGetActorHandle("player", out var actorHandle) || !actorHandle.IsValid)
            {
                if (!s_MissingPlayerActorWarned)
                {
                    UnityEngine.Debug.LogWarning("[VVardenfell] local player presentation skipped because NPC_ 'player' could not be resolved.");
                    s_MissingPlayerActorWarned = true;
                }
                return;
            }

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

            var inventory = SystemAPI.GetSingletonBuffer<PlayerInventoryItem>();
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            Entity firstPersonVisual = CreatePlayerVisual(
                ref ecb,
                player,
                view,
                actorHandle,
                firstPerson: true,
                actorRecipeFirstPerson: false,
                hiddenPartMask: BuildFirstPersonBodyHiddenPartMask(),
                visible: true,
                contentDb,
                inventory);
            Entity thirdPersonVisual = CreatePlayerVisual(
                ref ecb,
                player,
                view,
                actorHandle,
                firstPerson: false,
                actorRecipeFirstPerson: false,
                hiddenPartMask: 0u,
                visible: false,
                contentDb,
                inventory);

            ecb.AddComponent(player, new LocalPlayerPresentationState
            {
                Mode = PlayerViewMode.FirstPerson,
                ThirdPersonDistance = 3f,
                FirstPersonVisual = firstPersonVisual,
                ThirdPersonVisual = thirdPersonVisual,
                Actor = actorHandle,
            });
            ecb.AddComponent(player, new LocalPlayerPresentationPose());
            ecb.Playback(EntityManager);
            ecb.Dispose();
        }

        Entity CreatePlayerVisual(
            ref EntityCommandBuffer ecb,
            Entity player,
            Entity view,
            ActorDefHandle actorHandle,
            bool firstPerson,
            bool actorRecipeFirstPerson,
            uint hiddenPartMask,
            bool visible,
            RuntimeContentDatabase contentDb,
            DynamicBuffer<PlayerInventoryItem> inventory)
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
            ecb.AddComponent(visual, new LocalPlayerVisual
            {
                Player = player,
                View = view,
                FirstPerson = (byte)(firstPerson ? 1 : 0),
            });
            ecb.AddComponent(visual, LocalTransform.Identity);
            ecb.AddComponent(visual, new LocalToWorld());
            ecb.AddComponent(visual, ResolveInitialMovementState(player));
            ecb.AddComponent(visual, new ActorWeaponAnimationState
            {
                WeaponType = ActorWeaponAnimationUtility.NoWeaponType,
                Phase = ActorWeaponAnimationPhase.Hidden,
            });
            ecb.AddComponent<ActorRenderVisible>(visual);
            ecb.SetComponentEnabled<ActorRenderVisible>(visual, visible);
            ecb.AddComponent<ActorShadowCasterVisible>(visual);
            ecb.SetComponentEnabled<ActorShadowCasterVisible>(visual, !firstPerson);

            if (EntityManager.HasBuffer<ActorEquipmentSlot>(player))
            {
                var playerEquipment = EntityManager.GetBuffer<ActorEquipmentSlot>(player, true);
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

        MorrowindMovementState ResolveInitialMovementState(Entity player)
        {
            if (EntityManager.HasComponent<MorrowindMovementState>(player))
                return EntityManager.GetComponentData<MorrowindMovementState>(player);

            return new MorrowindMovementState
            {
                GroundNormal = math.up(),
            };
        }

        void CleanupOrphanVisuals()
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (visual, entity) in
                     SystemAPI.Query<RefRO<LocalPlayerVisual>>()
                         .WithEntityAccess())
            {
                if (visual.ValueRO.Player == Entity.Null
                    || !EntityManager.Exists(visual.ValueRO.Player)
                    || visual.ValueRO.View == Entity.Null
                    || !EntityManager.Exists(visual.ValueRO.View))
                {
                    ecb.DestroyEntity(entity);
                }
            }

            ecb.Playback(EntityManager);
            ecb.Dispose();
        }

    }

    [UpdateInGroup(typeof(MorrowindGameplayInputSystemGroup))]
    [UpdateAfter(typeof(PlayerInputReceivingSystem))]
    public partial class LocalPlayerViewModeSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<LocalPlayerPresentationState>();
            RequireForUpdate<PlayerCharacterControl>();
        }

        protected override void OnUpdate()
        {
            var stateEntity = SystemAPI.GetSingletonEntity<LocalPlayerPresentationState>();
            var state = EntityManager.GetComponentData<LocalPlayerPresentationState>(stateEntity);
            var controlEntity = SystemAPI.GetSingletonEntity<PlayerCharacterControl>();
            var control = EntityManager.GetComponentData<PlayerCharacterControl>(controlEntity);

            if (control.ToggleViewPressed)
            {
                state.Mode = state.Mode == PlayerViewMode.FirstPerson
                    ? PlayerViewMode.ThirdPerson
                    : PlayerViewMode.FirstPerson;
                EntityManager.SetComponentData(stateEntity, state);
                control.ToggleViewPressed = false;
                EntityManager.SetComponentData(controlEntity, control);
            }

            SetVisualGpuActive(state.FirstPersonVisual);
            SetVisualGpuActive(state.ThirdPersonVisual);
        }

        void SetVisualGpuActive(Entity entity)
        {
            if (entity == Entity.Null
                || !EntityManager.Exists(entity)
                || !EntityManager.HasComponent<ActorRenderVisible>(entity))
            {
                return;
            }

            EntityManager.SetComponentEnabled<ActorRenderVisible>(entity, true);
        }
    }
}
