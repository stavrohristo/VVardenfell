using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Player
{
    [UpdateInGroup(typeof(MorrowindInputSystemGroup))]
    public partial class PlayerInputReceivingSystem : SystemBase
    {
        EntityQuery _playerQuery;

        protected override void OnCreate()
        {
            _playerQuery = GetEntityQuery(
                ComponentType.ReadWrite<PlayerTag>(),
                ComponentType.ReadWrite<PlayerCharacterComponent>(),
                ComponentType.ReadWrite<PlayerCharacterControl>(),
                ComponentType.ReadWrite<PlayerCharacterState>(),
                ComponentType.ReadWrite<MorrowindMovementIntent>());
            RequireForUpdate(_playerQuery);
            RequireForUpdate<FixedTickSystem.Singleton>();
        }

        protected override void OnStartRunning()
        {
            ApplyCursorState(!GameplayInputGate.BlocksGameplayInput);
        }

        protected override void OnStopRunning()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        protected override void OnUpdate()
        {
            uint fixedTick = SystemAPI.GetSingleton<FixedTickSystem.Singleton>().Tick;
            var character = _playerQuery.GetSingleton<PlayerCharacterComponent>();
            var controlRef = _playerQuery.GetSingletonRW<PlayerCharacterControl>();
            var intentRef = _playerQuery.GetSingletonRW<MorrowindMovementIntent>();
            var stateRef = _playerQuery.GetSingletonRW<PlayerCharacterState>();
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            ref var control = ref controlRef.ValueRW;
            ref var intent = ref intentRef.ValueRW;
            ref var state = ref stateRef.ValueRW;

            bool gameplayInputAllowed = !GameplayInputGate.BlocksGameplayInput;
            ApplyCursorState(gameplayInputAllowed);

            if (!gameplayInputAllowed || kb == null)
            {
                control.MoveInput = float2.zero;
                control.LookDeltaDegrees = float2.zero;
                control.JumpHeld = false;
                control.SprintHeld = false;
                control.CrouchHeld = false;
                control.InteractPressed = false;
                intent = default;
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

            float2 frameLookDelta = mouse != null
                ? (float2)(Vector2)mouse.delta.ReadValue() * character.LookSensitivity
                : float2.zero;
            bool jumpPressedThisFrame = kb.spaceKey.wasPressedThisFrame;
            bool interactPressedThisFrame = kb.eKey.wasPressedThisFrame;

            control.MoveInput = move;
            control.LookDeltaDegrees += frameLookDelta;
            control.JumpHeld = kb.spaceKey.isPressed;
            control.SprintHeld = kb.leftShiftKey.isPressed;
            control.CrouchHeld = kb.leftCtrlKey.isPressed || kb.cKey.isPressed;
            control.InteractPressed |= interactPressedThisFrame;

            intent.LocalMove = new float3(move.x, move.y, math.max(intent.LocalMove.z, jumpPressedThisFrame ? 1f : 0f));
            intent.LookDeltaDegrees = control.LookDeltaDegrees;
            intent.JumpHeld = control.JumpHeld;
            intent.RunHeld = control.SprintHeld;
            intent.SneakHeld = control.CrouchHeld;
            intent.InteractPressed = control.InteractPressed;
            intent.SpeedFactor = math.saturate(math.length(move));
            intent.IsStrafing = math.abs(move.x) > math.abs(move.y) * 2f;

            if (jumpPressedThisFrame)
            {
                control.JumpPressedEvent.Set(fixedTick);
                state.LastJumpPressedTick = fixedTick;
            }
        }

        private static void ApplyCursorState(bool gameplayInputAllowed)
        {
            if (gameplayInputAllowed)
            {
                if (Cursor.lockState != CursorLockMode.Locked)
                    Cursor.lockState = CursorLockMode.Locked;
                if (Cursor.visible)
                    Cursor.visible = false;
            }
            else
            {
                if (Cursor.lockState != CursorLockMode.None)
                    Cursor.lockState = CursorLockMode.None;
                if (!Cursor.visible)
                    Cursor.visible = true;
            }
        }
    }
}
