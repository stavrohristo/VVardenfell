using System;
using Unity.Collections;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Shell;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.WorldRefs;

namespace VVardenfell.Runtime.MorrowindScript
{
    [BurstCompile]
    [UpdateInGroup(typeof(MorrowindGameplayMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptInterpreterSystem))]
    public partial struct MorrowindScriptHurtStandingActorApplySystem : ISystem
    {
        EntityQuery _standingActorQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _standingActorQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<MorrowindMovementState>(),
                ComponentType.ReadWrite<ActorVitalSet>());
            systemState.RequireForUpdate<MorrowindScriptHurtStandingActorRequest>();
            systemState.RequireForUpdate<LogicalRefLookup>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState systemState)
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = systemState.EntityManager.GetBuffer<MorrowindScriptHurtStandingActorRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            if (IsGuiMode(ref systemState))
            {
                requests.Clear();
                return;
            }

            float deltaSeconds = SystemAPI.Time.DeltaTime;
            if (deltaSeconds <= 0f)
            {
                requests.Clear();
                return;
            }

            var lookup = SystemAPI.GetSingleton<LogicalRefLookup>();
            for (int i = 0; i < requests.Length; i++)
                ApplyRequest(ref systemState, requests[i], lookup, deltaSeconds);

            requests.Clear();
        }

        void ApplyRequest(ref SystemState systemState, in MorrowindScriptHurtStandingActorRequest request, in LogicalRefLookup lookup, float deltaSeconds)
        {
            Entity standingTarget = MorrowindRuntimeTargetResolver.ResolveLiveTarget(systemState.EntityManager, request.TargetEntity, request.TargetPlacedRefId, lookup);
            if (standingTarget == Entity.Null || !systemState.EntityManager.Exists(standingTarget))
                throw new InvalidOperationException("[VVardenfell][MWScript] HurtStandingActor target is not loaded.");

            float healthDelta = request.HealthPerSecond * deltaSeconds;
            if (healthDelta == 0f)
                return;

            using NativeArray<Entity> entities = _standingActorQuery.ToEntityArray(Allocator.Temp);
            using NativeArray<MorrowindMovementState> movementStates = _standingActorQuery.ToComponentDataArray<MorrowindMovementState>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                if (entities[i] == standingTarget || movementStates[i].StandingOn != standingTarget)
                    continue;

                var vitals = systemState.EntityManager.GetComponentData<ActorVitalSet>(entities[i]);
                if (vitals.CurrentHealth <= 0f)
                    continue;

                vitals.CurrentHealth -= healthDelta;
                systemState.EntityManager.SetComponentData(entities[i], vitals);
                if (vitals.CurrentHealth <= 0f)
                {
                    var aftermath = ActorHitAftermathStateUtility.Require(
                        systemState.EntityManager,
                        entities[i]);
                    ActorHitAftermathStateUtility.MarkDead(systemState.EntityManager, entities[i], ref vitals, ref aftermath);
                    systemState.EntityManager.SetComponentData(entities[i], vitals);
                    systemState.EntityManager.SetComponentData(entities[i], aftermath);
                }
            }
        }

        bool IsGuiMode(ref SystemState systemState)
        {
            if (!SystemAPI.TryGetSingleton<RuntimeShellState>(out var shell))
                return false;

            return shell.InventoryOpen != 0
                || shell.ContainerOpen != 0
                || shell.PauseMenuOpen != 0
                || shell.ModalOpen != 0
                || shell.SaveLoadBrowserOpen != 0
                || shell.OptionsOpen != 0
                || shell.JournalOpen != 0
                || shell.DialogueOpen != 0;
        }
    }
}
