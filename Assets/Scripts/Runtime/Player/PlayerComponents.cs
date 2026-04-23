using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
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
        public float SkinWidth;
        public float MaxStepHeight;
        public float GroundProbeDistance;
        public float MaxSlopeCosine;
        public float GroundMaxSpeed;
        public float SprintSpeedMultiplier;
        public float CrouchSpeedMultiplier;
        public float GroundedMovementSharpness;
        public float AirAcceleration;
        public float AirMaxSpeed;
        public float AirDrag;
        public float JumpSpeed;
        public float Gravity;
        public float CoyoteTime;
        public float JumpBufferTime;
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
    [UpdateInGroup(typeof(MorrowindFixedPostPhysicsSystemGroup), OrderLast = true)]
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
