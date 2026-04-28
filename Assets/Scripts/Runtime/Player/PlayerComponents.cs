using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Player
{
    public struct PlayerTag : IComponentData
    {
    }

    public struct PlayerCharacterComponent : IComponentData
    {
        public float StandingHeight;
        public float CrouchingHeight;
        public float StandingEyeHeight;
        public float CrouchingEyeHeight;
        public float Radius;
        public float MinPitch;
        public float MaxPitch;
        public float LookSensitivity;
    }

    public struct PlayerCharacterControl : IComponentData
    {
        public float2 MoveInput;
        public float2 LookDeltaDegrees;
        public bool JumpHeld;
        public bool SprintHeld;
        public bool CrouchHeld;
        public bool InteractPressed;
        public bool ToggleViewPressed;
        public bool ReadyWeaponTogglePressed;
        public bool AttackHeld;
        public bool AttackPressed;
        public bool AttackReleased;
        public bool JumpThisFixedTick;
        public float3 MoveVectorWorld;
        public FixedInputEvent JumpPressedEvent;
    }

    public struct PlayerCharacterState : IComponentData
    {
        public float3 WorldVelocity;
        public bool Grounded;
        public bool WasGrounded;
        public bool Crouched;
        public bool Sprinting;
        public float GroundedTime;
        public float AirborneTime;
        public uint LastJumpPressedTick;
        public uint LastGroundedTick;
    }

    public struct PlayerViewComponent : IComponentData
    {
        public Entity ControlledCharacter;
        public float LocalPitchDegrees;
        public quaternion LocalViewRotation;
        public float3 LocalEyeOffset;
    }

    public enum PlayerViewMode : byte
    {
        FirstPerson = 0,
        ThirdPerson = 1,
    }

    public struct LocalPlayerPresentationState : IComponentData
    {
        public PlayerViewMode Mode;
        public float ThirdPersonDistance;
        public Entity FirstPersonVisual;
        public Entity ThirdPersonVisual;
        public ActorDefHandle Actor;
    }

    public struct LocalPlayerPresentationPose : IComponentData
    {
        public float3 BodyPosition;
        public quaternion BodyRotation;
        public float3 ViewPosition;
        public quaternion ViewRotation;
        public float3 PreviousBodyPosition;
        public float3 TargetBodyPosition;
        public float3 PreviousViewPosition;
        public float3 TargetViewPosition;
        public uint LastFixedTick;
        public float InterpolationTime;
        public byte Initialized;
    }

    public struct LocalPlayerVisual : IComponentData
    {
        public Entity Player;
        public Entity View;
        public byte FirstPerson;
    }

    public struct PlayerStanceColliders : IComponentData
    {
        public BlobAssetReference<Collider> Standing;
        public BlobAssetReference<Collider> Crouching;
    }

    public struct FixedInputEvent
    {
        byte _wasEverSet;
        uint _lastSetTick;
        uint _lastConsumedTick;

        public void Set(uint tick)
        {
            _lastSetTick = tick;
            _wasEverSet = 1;
        }

        public bool IsSet(uint tick)
        {
            return _wasEverSet == 1 && _lastSetTick == tick && _lastConsumedTick != _lastSetTick;
        }

        public bool IsBuffered(uint tick, uint bufferTicks)
        {
            if (_wasEverSet != 1 || _lastConsumedTick == _lastSetTick || tick < _lastSetTick)
                return false;

            return tick - _lastSetTick <= bufferTicks;
        }

        public void Consume()
        {
            if (_wasEverSet == 1)
                _lastConsumedTick = _lastSetTick;
        }

        public void Clear()
        {
            _wasEverSet = 0;
            _lastSetTick = 0;
            _lastConsumedTick = 0;
        }
    }

    [BurstCompile]
    [UpdateInGroup(typeof(MorrowindPhysicsPostQueryMutationSystemGroup), OrderLast = true)]
    public partial struct FixedTickSystem : ISystem
    {
        public struct Singleton : IComponentData
        {
            public uint Tick;
        }

        public void OnCreate(ref SystemState state)
        {
            if (!SystemAPI.HasSingleton<Singleton>())
            {
                Entity singletonEntity = state.EntityManager.CreateEntity();
                state.EntityManager.SetName(singletonEntity, "VVardenfell.FixedTick");
                state.EntityManager.AddComponentData(singletonEntity, new Singleton());
            }
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            ref var singleton = ref SystemAPI.GetSingletonRW<Singleton>().ValueRW;
            singleton.Tick++;
        }
    }
}
