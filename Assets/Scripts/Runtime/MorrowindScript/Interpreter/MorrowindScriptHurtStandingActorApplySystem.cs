using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Shell;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.WorldRefs;

namespace VVardenfell.Runtime.MorrowindScript
{
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptInterpreterSystem))]
    public partial class MorrowindScriptHurtStandingActorApplySystem : SystemBase
    {
        EntityQuery _standingActorQuery;

        protected override void OnCreate()
        {
            _standingActorQuery = GetEntityQuery(
                ComponentType.ReadOnly<MorrowindMovementState>(),
                ComponentType.ReadWrite<ActorVitalSet>());
            RequireForUpdate<MorrowindScriptHurtStandingActorRequest>();
            RequireForUpdate<LogicalRefLookup>();
        }

        protected override void OnUpdate()
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = EntityManager.GetBuffer<MorrowindScriptHurtStandingActorRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            if (IsGuiMode())
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
                ApplyRequest(requests[i], lookup, deltaSeconds);

            requests.Clear();
        }

        void ApplyRequest(in MorrowindScriptHurtStandingActorRequest request, in LogicalRefLookup lookup, float deltaSeconds)
        {
            Entity standingTarget = MorrowindRuntimeTargetResolver.ResolveLiveTarget(EntityManager, request.TargetEntity, request.TargetPlacedRefId, lookup);
            if (standingTarget == Entity.Null || !EntityManager.Exists(standingTarget))
                throw new InvalidOperationException($"[VVardenfell][MWScript] HurtStandingActor target ref={request.TargetPlacedRefId} is not loaded.");

            float healthDelta = request.HealthPerSecond * deltaSeconds;
            if (healthDelta == 0f)
                return;

            using NativeArray<Entity> entities = _standingActorQuery.ToEntityArray(Allocator.Temp);
            using NativeArray<MorrowindMovementState> movementStates = _standingActorQuery.ToComponentDataArray<MorrowindMovementState>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                if (entities[i] == standingTarget || movementStates[i].StandingOn != standingTarget)
                    continue;

                var vitals = EntityManager.GetComponentData<ActorVitalSet>(entities[i]);
                if (vitals.CurrentHealth <= 0f)
                    continue;

                vitals.CurrentHealth -= healthDelta;
                EntityManager.SetComponentData(entities[i], vitals);
                if (vitals.CurrentHealth <= 0f)
                {
                    var aftermath = ActorHitAftermathStateUtility.Require(
                        EntityManager,
                        entities[i],
                        $"[VVardenfell][MWScript] HurtStandingActor affected actor ref={PlacedRefId(entities[i])}");
                    ActorHitAftermathStateUtility.MarkDead(ref aftermath);
                    EntityManager.SetComponentData(entities[i], aftermath);
                }
            }
        }

        uint PlacedRefId(Entity entity)
            => EntityManager.HasComponent<PlacedRefIdentity>(entity)
                ? EntityManager.GetComponentData<PlacedRefIdentity>(entity).Value
                : 0u;

        bool IsGuiMode()
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
