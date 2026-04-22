using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using VVardenfell.Runtime.Bootstrap;

namespace VVardenfell.Runtime.Player
{
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    [UpdateBefore(typeof(FixedStepSimulationSystemGroup))]
    public partial class PlayerInputReceivingSystem : SystemBase
    {
        EntityQuery _playerQuery;

        protected override void OnCreate()
        {
            _playerQuery = GetEntityQuery(
                ComponentType.ReadWrite<PlayerTag>(),
                ComponentType.ReadWrite<PlayerCharacterComponent>(),
                ComponentType.ReadWrite<PlayerCharacterControl>(),
                ComponentType.ReadWrite<PlayerCharacterState>());
            RequireForUpdate(_playerQuery);
            RequireForUpdate<FixedTickSystem.Singleton>();
        }

        protected override void OnStartRunning()
        {
            ApplyCursorState(!BootstrapPresentationGate.BlocksGameplayInput);
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
            var stateRef = _playerQuery.GetSingletonRW<PlayerCharacterState>();
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            ref var control = ref controlRef.ValueRW;
            ref var state = ref stateRef.ValueRW;

            bool gameplayInputAllowed = !BootstrapPresentationGate.BlocksGameplayInput;
            ApplyCursorState(gameplayInputAllowed);

            if (!gameplayInputAllowed || kb == null)
            {
                control.MoveInput = float2.zero;
                control.LookDeltaDegrees = float2.zero;
                control.JumpHeld = false;
                control.SprintHeld = false;
                control.CrouchHeld = false;
                control.InteractPressed = false;
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

            control.MoveInput = move;
            control.LookDeltaDegrees = mouse != null
                ? (float2)(Vector2)mouse.delta.ReadValue() * character.LookSensitivity
                : float2.zero;
            control.JumpHeld = kb.spaceKey.isPressed;
            control.SprintHeld = kb.leftShiftKey.isPressed;
            control.CrouchHeld = kb.leftCtrlKey.isPressed || kb.cKey.isPressed;
            control.InteractPressed = kb.eKey.wasPressedThisFrame;

            if (kb.spaceKey.wasPressedThisFrame)
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
