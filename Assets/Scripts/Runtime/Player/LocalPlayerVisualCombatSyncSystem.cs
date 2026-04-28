#if !VVARDENFELL_OLD_ACTOR_ANIMATION
using Unity.Entities;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Player
{
    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    [UpdateAfter(typeof(LocalPlayerVisualMovementSyncSystem))]
    [UpdateBefore(typeof(ActorWeaponAnimationSystem))]
    public partial class LocalPlayerVisualCombatSyncSystem : SystemBase
    {
        EntityQuery _playerQuery;

        protected override void OnCreate()
        {
            _playerQuery = GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadWrite<PlayerCharacterControl>());

            RequireForUpdate(_playerQuery);
            RequireForUpdate<LocalPlayerVisual>();
        }

        protected override void OnUpdate()
        {
            RuntimeContentDatabase contentDb = RuntimeContentDatabase.Active;
            Entity player = _playerQuery.GetSingletonEntity();
            var control = EntityManager.GetComponentData<PlayerCharacterControl>(player);
            byte readyWeaponTogglePressed = control.ReadyWeaponTogglePressed ? (byte)1 : (byte)0;
            byte attackPressed = control.AttackPressed ? (byte)1 : (byte)0;
            byte attackReleased = control.AttackReleased ? (byte)1 : (byte)0;
            byte attackHeld = control.AttackHeld ? (byte)1 : (byte)0;

            foreach (var (visual, weaponState, entity) in
                     SystemAPI.Query<RefRO<LocalPlayerVisual>, RefRW<ActorWeaponAnimationState>>()
                         .WithEntityAccess())
            {
                if (visual.ValueRO.Player != player)
                    continue;

                ref var state = ref weaponState.ValueRW;
                state.ReadyWeaponTogglePressed = readyWeaponTogglePressed;
                state.AttackHeld = attackHeld;
                state.AttackPressed = attackPressed;
                state.AttackReleased = attackReleased;
                state.WeaponType = ActorWeaponAnimationUtility.NoWeaponType;
                state.WeaponContent = default;

                if (EntityManager.HasBuffer<ActorEquipmentSlot>(entity))
                {
                    var equipment = EntityManager.GetBuffer<ActorEquipmentSlot>(entity, true);
                    state.WeaponType = ActorWeaponAnimationUtility.ResolveEquippedWeaponType(contentDb, equipment, out var content);
                    state.WeaponContent = content;
                }
            }

            if (control.ReadyWeaponTogglePressed || control.AttackPressed || control.AttackReleased)
            {
                control.ReadyWeaponTogglePressed = false;
                control.AttackPressed = false;
                control.AttackReleased = false;
                EntityManager.SetComponentData(player, control);
            }
        }
    }
}
#endif
