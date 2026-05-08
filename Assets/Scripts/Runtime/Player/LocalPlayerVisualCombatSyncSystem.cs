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
                ComponentType.ReadWrite<ActorMagicCastState>(),
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
            var magicRef = _playerQuery.GetSingletonRW<ActorMagicCastState>();
            ref var magic = ref magicRef.ValueRW;
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
                if (active && readyWeaponTogglePressed != 0 && state.WeaponType == ActorWeaponAnimationUtility.SpellWeaponType)
                {
                    state.Drawn = 0;
                    state.Phase = ActorWeaponAnimationPhase.Hidden;
                    state.SpellCastPressed = 0;
                    state.SpellCastRange = 0;
                    state.SpellCastSourceKind = 0;
                    state.SpellCastSpell = default;
                    state.SpellCastEnchantment = default;
                    state.SpellCastItemContent = default;
                    state.SpellCastInventoryIndex = -1;
                    state.SpellCastReleasePending = 0;
                    state.SpellCastReleaseSourceKind = 0;
                    state.SpellCastReleaseSpell = default;
                    state.SpellCastReleaseEnchantment = default;
                    state.SpellCastReleaseItemContent = default;
                    state.SpellCastReleaseInventoryIndex = -1;
                }

                state.ReadyWeaponTogglePressed = readyWeaponTogglePressed;
                state.AttackHeld = active ? attackHeld : (byte)0;
                state.AttackPressed = active ? attackPressed : (byte)0;
                state.AttackReleased = active ? attackReleased : (byte)0;
                state.WeaponContent = default;
                bool magicActive = active && (magic.MagicReadied != 0 || magic.CastInProgress != 0);
                if (magicActive)
                {
                    state.WeaponType = ActorWeaponAnimationUtility.SpellWeaponType;
                    if (state.Drawn == 0)
                        state.ReadyWeaponTogglePressed = 1;
                    if (magic.CastRequested != 0)
                    {
                        state.SpellCastPressed = 1;
                        state.SpellCastRange = magic.CastRange;
                        state.SpellCastSourceKind = magic.CastingSourceKind;
                        state.SpellCastSpell = magic.CastingSpell;
                        state.SpellCastEnchantment = magic.CastingEnchantment;
                        state.SpellCastItemContent = magic.CastingItemContent;
                        state.SpellCastInventoryIndex = magic.CastingInventoryIndex;
                    }
                }
                else if (active && state.WeaponType == ActorWeaponAnimationUtility.SpellWeaponType && state.Drawn != 0)
                {
                    state.ReadyWeaponTogglePressed = 1;
                }
                else
                {
                    state.WeaponType = ActorWeaponAnimationUtility.NoWeaponType;
                }

                if (!active && state.MeleeHitPending != 0)
                {
                    state.MeleeHitPending = 0;
                    state.MeleeHitAttackStrength = 0f;
                    state.MeleeHitWeaponContent = default;
                }

                if (!magicActive
                    && !(active && state.WeaponType == ActorWeaponAnimationUtility.SpellWeaponType && state.Drawn != 0)
                    && systemState.EntityManager.HasBuffer<ActorEquipmentSlot>(entity))
                {
                    var equipment = systemState.EntityManager.GetBuffer<ActorEquipmentSlot>(entity, true);
                    state.WeaponType = ActorWeaponAnimationUtility.ResolveEquippedWeaponType(ref content, equipment, out var weaponContent);
                    state.WeaponContent = weaponContent;
                }
            }

            if (magic.CastRequested != 0)
                magic.CastRequested = 0;

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
