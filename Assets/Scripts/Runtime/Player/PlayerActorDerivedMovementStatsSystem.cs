using Unity.Entities;
using Unity.Burst;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Player
{
    [BurstCompile]
    [UpdateInGroup(typeof(MorrowindPhysicsPostQueryMutationSystemGroup))]
    [UpdateAfter(typeof(PlayerActorFatigueSystem))]
    public partial struct PlayerActorDerivedMovementStatsSystem : ISystem
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
                ComponentType.ReadWrite<ActorDerivedMovementStats>(),
                ComponentType.ReadWrite<MorrowindMovementSpeed>());

            systemState.RequireForUpdate(_playerQuery);
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState systemState)
        {
            if (_playerQuery.IsEmptyIgnoreFilter)
                return;

            var attributes = _playerQuery.GetSingleton<ActorAttributeSet>();
            var skills = _playerQuery.GetSingleton<ActorSkillSet>();
            ref var vitals = ref _playerQuery.GetSingletonRW<ActorVitalSet>().ValueRW;
            var effectModifiers = _playerQuery.GetSingleton<ActorEffectStatModifiers>();
            ref var derived = ref _playerQuery.GetSingletonRW<ActorDerivedMovementStats>().ValueRW;
            var contentBlobReference = SystemAPI.GetSingleton<RuntimeContentBlobReference>();
            if (!contentBlobReference.Blob.IsCreated)
                throw new System.InvalidOperationException("[VVardenfell][ContentBlob] Player movement derived stats require runtime content blob.");
            ref RuntimeContentBlob content = ref contentBlobReference.Blob.Value;
            MorrowindActorMovementStats.ApplyVitalBases(ref content, attributes, effectModifiers, ref vitals, initializeMissingCurrents: false);
            MorrowindActorMovementStats.ApplyMovementDerived(ref content, attributes, skills, vitals, effectModifiers, ref derived);
            ref var movementSpeed = ref _playerQuery.GetSingletonRW<MorrowindMovementSpeed>().ValueRW;
            movementSpeed = MorrowindActorMovementStats.BuildPlayerMovementSpeed(
                ref content,
                attributes,
                skills,
                vitals,
                effectModifiers,
                derived);
        }
    }
}
