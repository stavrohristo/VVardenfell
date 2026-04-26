using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
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
                visible: true,
                contentDb,
                inventory);
            Entity thirdPersonVisual = CreatePlayerVisual(
                ref ecb,
                player,
                view,
                actorHandle,
                firstPerson: false,
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
            ecb.Playback(EntityManager);
            ecb.Dispose();
        }

        Entity CreatePlayerVisual(
            ref EntityCommandBuffer ecb,
            Entity player,
            Entity view,
            ActorDefHandle actorHandle,
            bool firstPerson,
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
                FirstPerson = (byte)(firstPerson ? 1 : 0),
            });
            ecb.AddComponent(visual, new LocalPlayerVisual
            {
                Player = player,
                View = view,
                FirstPerson = (byte)(firstPerson ? 1 : 0),
            });
            ecb.AddComponent(visual, new Parent { Value = firstPerson ? view : player });
            ecb.AddComponent(visual, LocalTransform.Identity);
            ecb.AddComponent(visual, new LocalToWorld());
            ecb.AddComponent<ActorRenderVisible>(visual);
            ecb.SetComponentEnabled<ActorRenderVisible>(visual, visible);

            var actorInventory = ecb.AddBuffer<ActorInventoryItem>(visual);
            var equipment = ecb.AddBuffer<ActorEquipmentSlot>(visual);
            HydratePlayerVisualEquipment(contentDb, inventory, actorInventory, equipment);
            return visual;
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

        static void HydratePlayerVisualEquipment(
            RuntimeContentDatabase contentDb,
            DynamicBuffer<PlayerInventoryItem> playerInventory,
            DynamicBuffer<ActorInventoryItem> actorInventory,
            DynamicBuffer<ActorEquipmentSlot> equipment)
        {
            if (contentDb == null)
                return;
            const int SlotCapacity = 32;
            var bestScores = new long[SlotCapacity];
            var bestInventoryIndices = new int[SlotCapacity];
            for (int i = 0; i < SlotCapacity; i++)
            {
                bestScores[i] = long.MinValue;
                bestInventoryIndices[i] = -1;
            }

            for (int i = 0; i < playerInventory.Length; i++)
            {
                var item = playerInventory[i];
                if (item.Count <= 0 || !item.Content.IsValid)
                    continue;

                int inventoryIndex = actorInventory.Length;
                actorInventory.Add(new ActorInventoryItem
                {
                    Content = item.Content,
                    Count = item.Count,
                    AuthoredOrder = i,
                });

                if (item.Content.Kind != ContentReferenceKind.Item)
                    continue;

                var itemHandle = new ItemDefHandle { Value = item.Content.HandleValue };
                if (!contentDb.TryGetItemEquipment(itemHandle, out var itemEquipment))
                    continue;
                int slot = (int)itemEquipment.Slot;
                if ((uint)slot >= SlotCapacity || slot == (int)ItemEquipmentSlot.None)
                    continue;

                long score = ScoreInitialEquipment(itemEquipment, i);
                if (score <= bestScores[slot])
                    continue;

                bestScores[slot] = score;
                bestInventoryIndices[slot] = inventoryIndex;
            }

            for (int slot = 0; slot < SlotCapacity; slot++)
            {
                int inventoryIndex = bestInventoryIndices[slot];
                if (inventoryIndex < 0 || inventoryIndex >= actorInventory.Length)
                    continue;

                var item = actorInventory[inventoryIndex];
                var itemHandle = new ItemDefHandle { Value = item.Content.HandleValue };
                if (!contentDb.TryGetItemEquipment(itemHandle, out var itemEquipment))
                    continue;

                equipment.Add(new ActorEquipmentSlot
                {
                    Slot = (ItemEquipmentSlot)slot,
                    Content = item.Content,
                    InventoryIndex = inventoryIndex,
                    VisualMode = ResolveEquipmentVisualMode(itemEquipment),
                });
            }
        }

        static long ScoreInitialEquipment(in ItemEquipmentDef equipment, int authoredOrder)
        {
            long tieBreaker = 1000 - System.Math.Min(999, authoredOrder);
            return equipment.Kind switch
            {
                ItemEquipmentKind.Weapon => 3_000_000_000L + equipment.DamageMax * 1_000_000L + equipment.Value * 100L + tieBreaker,
                ItemEquipmentKind.Armor => 2_000_000_000L + equipment.Armor * 1_000_000L + equipment.Value * 100L + tieBreaker,
                ItemEquipmentKind.Clothing => 1_000_000_000L + equipment.Value * 100L + tieBreaker,
                _ => tieBreaker,
            };
        }

        static byte ResolveEquipmentVisualMode(in ItemEquipmentDef equipment)
        {
            if (equipment.Kind == ItemEquipmentKind.Weapon || equipment.Slot == ItemEquipmentSlot.Shield)
                return 2;
            if (equipment.Kind == ItemEquipmentKind.Armor || equipment.Kind == ItemEquipmentKind.Clothing)
                return 1;
            return 0;
        }
    }

    [UpdateInGroup(typeof(MorrowindInputSystemGroup))]
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

            SetVisualVisible(state.FirstPersonVisual, state.Mode == PlayerViewMode.FirstPerson);
            SetVisualVisible(state.ThirdPersonVisual, state.Mode == PlayerViewMode.ThirdPerson);
        }

        void SetVisualVisible(Entity entity, bool visible)
        {
            if (entity == Entity.Null
                || !EntityManager.Exists(entity)
                || !EntityManager.HasComponent<ActorRenderVisible>(entity))
            {
                return;
            }

            EntityManager.SetComponentEnabled<ActorRenderVisible>(entity, visible);
        }
    }

    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    [UpdateAfter(typeof(ActorAnimationStateResolveSystem))]
    [UpdateBefore(typeof(ActorAnimationGraphSystem))]
    public partial class LocalPlayerVisualAnimationSyncSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<LocalPlayerVisual>();
            RequireForUpdate<PlayerCharacterControl>();
        }

        protected override void OnUpdate()
        {
            var control = SystemAPI.GetSingleton<PlayerCharacterControl>();
            FixedString64Bytes group = ResolveMovementGroup(control);

            foreach (var controller in
                     SystemAPI.Query<RefRW<ActorAnimationController>>()
                         .WithAll<LocalPlayerVisual>())
            {
                controller.ValueRW.RequestedGroup = group;
                controller.ValueRW.Speed = control.SprintHeld ? 1.15f : 1f;
            }
        }

        static FixedString64Bytes ResolveMovementGroup(in PlayerCharacterControl control)
        {
            float2 move = control.MoveInput;
            if (math.lengthsq(move) < 0.0001f)
                return Fixed("idle");

            bool lateral = math.abs(move.x) > math.abs(move.y);
            if (control.CrouchHeld)
            {
                if (lateral)
                    return move.x >= 0f ? Fixed("sneakright") : Fixed("sneakleft");
                return move.y >= 0f ? Fixed("sneakforward") : Fixed("sneakback");
            }

            if (control.SprintHeld)
            {
                if (lateral)
                    return move.x >= 0f ? Fixed("runright") : Fixed("runleft");
                return move.y >= 0f ? Fixed("runforward") : Fixed("runback");
            }

            if (lateral)
                return move.x >= 0f ? Fixed("walkright") : Fixed("walkleft");
            return move.y >= 0f ? Fixed("walkforward") : Fixed("walkback");
        }

        static FixedString64Bytes Fixed(string value)
        {
            FixedString64Bytes result = default;
            for (int i = 0; i < value.Length; i++)
                result.Append(value[i]);
            return result;
        }
    }
}
