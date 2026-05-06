#if !VVARDENFELL_OLD_ACTOR_ANIMATION
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Player
{
    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    [UpdateAfter(typeof(LocalPlayerVisualMovementSyncSystem))]
    [UpdateBefore(typeof(ActorWeaponAnimationSystem))]
    public partial struct LocalPlayerVisualCombatSyncSystem : ISystem
    {
        EntityQuery _playerQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _playerQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadOnly<LocalPlayerPresentationState>(),
                ComponentType.ReadWrite<PlayerCharacterControl>());

            systemState.RequireForUpdate(_playerQuery);
            systemState.RequireForUpdate<LocalPlayerVisual>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            var contentBlobReference = SystemAPI.GetSingleton<RuntimeContentBlobReference>();
            if (!contentBlobReference.Blob.IsCreated)
                throw new System.InvalidOperationException("[VVardenfell][Player] Local player visual combat sync requires runtime content blob.");
            ref RuntimeContentBlob content = ref contentBlobReference.Blob.Value;
            Entity player = _playerQuery.GetSingletonEntity();
            var presentation = _playerQuery.GetSingleton<LocalPlayerPresentationState>();
            Entity activeVisual = presentation.Mode == PlayerViewMode.FirstPerson
                ? presentation.FirstPersonVisual
                : presentation.ThirdPersonVisual;
            var control = systemState.EntityManager.GetComponentData<PlayerCharacterControl>(player);
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
                bool active = entity == activeVisual;
                state.ReadyWeaponTogglePressed = readyWeaponTogglePressed;
                state.AttackHeld = active ? attackHeld : (byte)0;
                state.AttackPressed = active ? attackPressed : (byte)0;
                state.AttackReleased = active ? attackReleased : (byte)0;
                state.WeaponType = ActorWeaponAnimationUtility.NoWeaponType;
                state.WeaponContent = default;
                if (!active && state.MeleeHitPending != 0)
                {
                    state.MeleeHitPending = 0;
                    state.MeleeHitAttackStrength = 0f;
                    state.MeleeHitWeaponContent = default;
                }

                if (systemState.EntityManager.HasBuffer<ActorEquipmentSlot>(entity))
                {
                    var equipment = systemState.EntityManager.GetBuffer<ActorEquipmentSlot>(entity, true);
                    state.WeaponType = ActorWeaponAnimationUtility.ResolveEquippedWeaponType(ref content, equipment, out var weaponContent);
                    state.WeaponContent = weaponContent;
                }
            }

            if (control.ReadyWeaponTogglePressed || control.AttackPressed || control.AttackReleased)
            {
                control.ReadyWeaponTogglePressed = false;
                control.AttackPressed = false;
                control.AttackReleased = false;
                systemState.EntityManager.SetComponentData(player, control);
            }
        }
    }
}
#endif
