using System;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.AI;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.MorrowindScript;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Combat
{
    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    [UpdateBefore(typeof(ActorAiPackageTargetResolveSystem))]
    public partial struct MorrowindCrimeWitnessCleanupSystem : ISystem
    {
        EntityQuery _playerCrimeQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _playerCrimeQuery = systemState.GetEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<PlayerCrimeState>());
            systemState.RequireForUpdate(_playerCrimeQuery);
            systemState.RequireForUpdate<ActorCrimeState>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            var playerCrime = _playerCrimeQuery.GetSingleton<PlayerCrimeState>();
            if (playerCrime.PaidCrimeId < 0)
                return;

            var contentBlobReference = SystemAPI.GetSingleton<RuntimeContentBlobReference>();
            if (!contentBlobReference.Blob.IsCreated)
                throw new InvalidOperationException("[VVardenfell][CrimeCleanup] Crime cleanup requires runtime content blob.");
            ref RuntimeContentBlob content = ref contentBlobReference.Blob.Value;

            foreach (var (crimeRef, source, entity) in SystemAPI
                         .Query<RefRW<ActorCrimeState>, RefRO<ActorSpawnSource>>()
                         .WithEntityAccess())
            {
                if (systemState.EntityManager.HasComponent<PlayerTag>(entity))
                    continue;

                ref var crime = ref crimeRef.ValueRW;
                if (crime.CrimeId < 0 || crime.CrimeId > playerCrime.PaidCrimeId)
                    continue;

                if (!source.ValueRO.Definition.IsValid)
                    throw new InvalidOperationException($"[VVardenfell][CrimeCleanup] Actor ref={PlacedRefIdOrZero(ref systemState, entity)} has invalid actor definition.");

                crime.CrimeId = -1;
                crime.Alarmed = 0;
                crime.CrimeDispositionModifier = 0;

                if (systemState.EntityManager.HasComponent<ActorScriptEventState>(entity))
                {
                    var eventState = systemState.EntityManager.GetComponentData<ActorScriptEventState>(entity);
                    eventState.Attacked = 0;
                    eventState.LastHitAttemptActor = Entity.Null;
                    eventState.LastHitAttemptActorPlacedRefId = 0u;
                    eventState.LastHitAttemptObject = default;
                    systemState.EntityManager.SetComponentData(entity, eventState);
                }

                if (systemState.EntityManager.HasComponent<ActorAiSettingsState>(entity))
                {
                    ref RuntimeActorDefBlob actor = ref RuntimeContentBlobUtility.Get(ref content, source.ValueRO.Definition);
                    var settings = systemState.EntityManager.GetComponentData<ActorAiSettingsState>(entity);
                    settings.Fight = actor.AiData.Fight;
                    systemState.EntityManager.SetComponentData(entity, settings);
                }

                if (systemState.EntityManager.HasComponent<ActorCombatTargetState>(entity)
                    && systemState.EntityManager.GetComponentData<ActorCombatTargetState>(entity).Active != 0)
                {
                    if (!MorrowindCombatTargetUtility.TryStopCombat(systemState.EntityManager, entity))
                        throw new InvalidOperationException($"[VVardenfell][CrimeCleanup] Failed to stop paid-crime combat for actor ref={PlacedRefIdOrZero(ref systemState, entity)}.");
                }
                else if (HasCurrentPackage(ref systemState, entity, ActorAiRuntimePackageType.Pursue))
                {
                    MorrowindScriptAiPackageUtility.ClearActorPackages(systemState.EntityManager, entity);
                }
            }
        }

        static bool HasCurrentPackage(ref SystemState systemState, Entity actor, ActorAiRuntimePackageType type)
        {
            if (!systemState.EntityManager.HasComponent<ActorAiState>(actor) || !systemState.EntityManager.HasBuffer<ActorAiPackageRuntime>(actor))
                return false;

            var aiState = systemState.EntityManager.GetComponentData<ActorAiState>(actor);
            var packages = systemState.EntityManager.GetBuffer<ActorAiPackageRuntime>(actor, true);
            if ((uint)aiState.CurrentPackageIndex >= (uint)packages.Length)
                return false;

            return packages[aiState.CurrentPackageIndex].Type == (byte)type;
        }

        static uint PlacedRefIdOrZero(ref SystemState systemState, Entity entity)
        {
            if (entity == Entity.Null || !systemState.EntityManager.Exists(entity) || !systemState.EntityManager.HasComponent<PlacedRefIdentity>(entity))
                return 0u;

            return systemState.EntityManager.GetComponentData<PlacedRefIdentity>(entity).Value;
        }
    }
}
