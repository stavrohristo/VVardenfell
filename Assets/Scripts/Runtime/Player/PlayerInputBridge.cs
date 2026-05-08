using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Player
{
    [UpdateInGroup(typeof(MorrowindGameplayInputSystemGroup))]
    public partial struct PlayerInputReceivingSystem : ISystem
    {
        EntityQuery _playerQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _playerQuery = systemState.GetEntityQuery(
                ComponentType.ReadWrite<PlayerTag>(),
                ComponentType.ReadWrite<PlayerCharacterComponent>(),
                ComponentType.ReadWrite<PlayerCharacterControl>(),
                ComponentType.ReadWrite<PlayerCharacterState>(),
                ComponentType.ReadWrite<ActorMagicCastState>(),
                ComponentType.ReadWrite<MorrowindMovementInput>());
            systemState.RequireForUpdate(_playerQuery);
            systemState.RequireForUpdate<FixedTickSystem.Singleton>();
            systemState.RequireForUpdate<RuntimeShellState>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            uint fixedTick = SystemAPI.GetSingleton<FixedTickSystem.Singleton>().Tick;
            var character = _playerQuery.GetSingleton<PlayerCharacterComponent>();
            Entity player = _playerQuery.GetSingletonEntity();
            var controlRef = _playerQuery.GetSingletonRW<PlayerCharacterControl>();
            var inputRef = _playerQuery.GetSingletonRW<MorrowindMovementInput>();
            var stateRef = _playerQuery.GetSingletonRW<PlayerCharacterState>();
            var magicRef = _playerQuery.GetSingletonRW<ActorMagicCastState>();
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            ref var control = ref controlRef.ValueRW;
            ref var movementInput = ref inputRef.ValueRW;
            ref var state = ref stateRef.ValueRW;
            ref var magic = ref magicRef.ValueRW;
            var shell = SystemAPI.GetSingleton<RuntimeShellState>();

            bool shellBlocksInput = BootstrapPresentationGate.BlocksGameplayInput
                || SystemAPI.HasSingleton<RuntimeShellUiInputBlocker>();
            bool movementInputAllowed = !shellBlocksInput && shell.PlayerControlsDisabled == 0;
            bool lookInputAllowed = !shellBlocksInput && shell.PlayerLookingDisabled == 0;

            float2 frameLookDelta = lookInputAllowed && mouse != null
                ? (float2)(Vector2)mouse.delta.ReadValue() * character.LookSensitivity
                : float2.zero;

            if (!movementInputAllowed || kb == null)
            {
                control.MoveInput = float2.zero;
                control.LookDeltaDegrees = lookInputAllowed ? control.LookDeltaDegrees + frameLookDelta : float2.zero;
                control.JumpHeld = false;
                control.SprintHeld = false;
                control.CrouchHeld = false;
                control.InteractPressed = false;
                control.ToggleViewPressed = false;
                control.ReadyWeaponTogglePressed = false;
                control.ReadyMagicTogglePressed = false;
                control.CastMagicPressed = false;
                control.AttackHeld = false;
                control.AttackPressed = false;
                control.AttackReleased = false;
                movementInput = default;
                return;
            }

            float2 move = float2.zero;
            if (kb.wKey.isPressed) move.y += 1f;
            if (kb.sKey.isPressed) move.y -= 1f;
            if (kb.dKey.isPressed) move.x += 1f;
            if (kb.aKey.isPressed) move.x -= 1f;
            float moveLengthSq = math.lengthsq(move);
            if (moveLengthSq > 1f)
                move *= math.rsqrt(moveLengthSq);

            bool jumpPressedThisFrame = kb.spaceKey.wasPressedThisFrame;
            bool interactPressedThisFrame = kb.eKey.wasPressedThisFrame;
            bool toggleViewPressedThisFrame = kb.vKey.wasPressedThisFrame;
            bool readyWeaponTogglePressedThisFrame = kb.fKey.wasPressedThisFrame;
            bool readyMagicTogglePressedThisFrame = kb.rKey.wasPressedThisFrame;
            bool attackHeld = mouse != null && mouse.leftButton.isPressed;
            bool attackPressedThisFrame = mouse != null && mouse.leftButton.wasPressedThisFrame;
            bool attackReleasedThisFrame = mouse != null && mouse.leftButton.wasReleasedThisFrame;
            if (shell.PlayerJumpingDisabled != 0)
                jumpPressedThisFrame = false;
            if (shell.PlayerViewSwitchDisabled != 0)
                toggleViewPressedThisFrame = false;
            if (shell.PlayerFightingDisabled != 0)
            {
                readyWeaponTogglePressedThisFrame = false;
            }
            if (shell.PlayerMagicDisabled != 0)
                readyMagicTogglePressedThisFrame = false;

            if (readyWeaponTogglePressedThisFrame)
            {
                magic.MagicReadied = 0;
                magic.CastInProgress = 0;
                magic.CastRequested = 0;
                magic.CastRange = 0;
                magic.CastingSourceKind = 0;
                magic.CastingSpell = default;
                magic.CastingEnchantment = default;
                magic.CastingItemContent = default;
                magic.CastingInventoryIndex = -1;
            }

            bool magicConsumesAttack = magic.MagicReadied != 0 && shell.PlayerMagicDisabled == 0;
            bool castMagicPressedThisFrame = magicConsumesAttack && attackPressedThisFrame;
            if (shell.PlayerFightingDisabled != 0 || magicConsumesAttack)
            {
                attackHeld = false;
                attackPressedThisFrame = false;
                attackReleasedThisFrame = false;
            }

            control.MoveInput = move;
            control.LookDeltaDegrees += frameLookDelta;
            control.JumpHeld = shell.PlayerJumpingDisabled == 0 && kb.spaceKey.isPressed;
            control.SprintHeld = kb.leftShiftKey.isPressed;
            control.CrouchHeld = kb.leftCtrlKey.isPressed || kb.cKey.isPressed;
            control.InteractPressed |= interactPressedThisFrame;
            control.ToggleViewPressed |= toggleViewPressedThisFrame;
            if (toggleViewPressedThisFrame)
                systemState.EntityManager.SetComponentEnabled<LocalPlayerViewModeDirty>(player, true);
            control.ReadyWeaponTogglePressed |= readyWeaponTogglePressedThisFrame;
            control.ReadyMagicTogglePressed |= readyMagicTogglePressedThisFrame;
            control.CastMagicPressed |= castMagicPressedThisFrame;
            control.AttackHeld = attackHeld;
            control.AttackPressed |= attackPressedThisFrame;
            control.AttackReleased |= attackReleasedThisFrame;

            movementInput.LocalMove = move;
            movementInput.JumpPressed = movementInput.JumpPressed || jumpPressedThisFrame;
            movementInput.RunHeld = control.SprintHeld;
            movementInput.SneakHeld = control.CrouchHeld;

            if (jumpPressedThisFrame)
            {
                control.JumpPressedEvent.Set(fixedTick);
                state.LastJumpPressedTick = fixedTick;
            }
        }
    }
}
