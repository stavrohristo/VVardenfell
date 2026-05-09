using Unity.Entities;
using Unity.Burst;
using Unity.Mathematics;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Player
{
    [BurstCompile]
    [UpdateInGroup(typeof(MorrowindPhysicsPostQueryMutationSystemGroup))]
    [UpdateAfter(typeof(PlayerActorEncumbranceSystem))]
    public partial struct PlayerActorFatigueSystem : ISystem
    {
        EntityQuery _playerQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _playerQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadOnly<ActorAttributeSet>(),
                ComponentType.ReadOnly<ActorSkillSet>(),
                ComponentType.ReadWrite<ActorVitalSet>(),
                ComponentType.ReadOnly<ActorEffectStatModifiers>(),
                ComponentType.ReadOnly<ActorDerivedMovementStats>(),
                ComponentType.ReadOnly<MorrowindMovementSpeed>(),
                ComponentType.ReadOnly<MorrowindMovementInput>(),
                ComponentType.ReadOnly<MorrowindMovementState>());

            systemState.RequireForUpdate(_playerQuery);
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState systemState)
        {
            if (_playerQuery.IsEmptyIgnoreFilter)
                return;

            float dt = SystemAPI.Time.DeltaTime;
            if (dt <= 0f)
                return;
            var contentBlobReference = SystemAPI.GetSingleton<RuntimeContentBlobReference>();
            if (!contentBlobReference.Blob.IsCreated)
                throw new System.InvalidOperationException("[VVardenfell][ContentBlob] Player fatigue requires runtime content blob.");
            ref RuntimeContentBlob content = ref contentBlobReference.Blob.Value;

            var attributes = _playerQuery.GetSingleton<ActorAttributeSet>();
            var skills = _playerQuery.GetSingleton<ActorSkillSet>();
            ref var vitals = ref _playerQuery.GetSingletonRW<ActorVitalSet>().ValueRW;
            var effectModifiers = _playerQuery.GetSingleton<ActorEffectStatModifiers>();
            var derived = _playerQuery.GetSingleton<ActorDerivedMovementStats>();
            var movementSpeed = _playerQuery.GetSingleton<MorrowindMovementSpeed>();
            var movementInput = _playerQuery.GetSingleton<MorrowindMovementInput>();
            var movementState = _playerQuery.GetSingleton<MorrowindMovementState>();

            MorrowindActorMovementStats.ApplyVitalBases(ref content, attributes, effectModifiers, ref vitals, initializeMissingCurrents: false);
            var context = MorrowindPlayerSpeedResolver.Build(ref content, attributes, skills, vitals, effectModifiers, derived, movementSpeed);

            float fatigue = vitals.CurrentFatigue;
            fatigue -= context.GetMovementFatigueLossPerSecond(
                movementInput.RunHeld && !movementState.SneakHeld,
                movementState.SneakHeld,
                movementState.SpeedFactor) * dt;
            if (movementState.JumpAccepted)
                fatigue -= context.GetJumpFatigueLoss();

            if (fatigue < vitals.ModifiedFatigueBase)
                fatigue = math.min(vitals.ModifiedFatigueBase, fatigue + context.GetFatigueRestorePerSecond() * dt);

            vitals.CurrentFatigue = fatigue;
        }
    }
}
