using System;
using Unity.Entities;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Combat;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.WorldRefs;

namespace VVardenfell.Runtime.MorrowindScript
{
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptInterpreterSystem))]
    public partial struct MorrowindScriptActorVitalApplySystem : ISystem
    {
        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<MorrowindScriptActorVitalRequest>();
            systemState.RequireForUpdate<LogicalRefLookup>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = systemState.EntityManager.GetBuffer<MorrowindScriptActorVitalRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            var lookup = SystemAPI.GetSingleton<LogicalRefLookup>();
            for (int i = 0; i < requests.Length; i++)
                ApplyRequest(ref systemState, requests[i], lookup);

            requests.Clear();
        }

        void ApplyRequest(ref SystemState systemState, in MorrowindScriptActorVitalRequest request, in LogicalRefLookup lookup)
        {
            Entity target = MorrowindRuntimeTargetResolver.ResolveLiveTarget(systemState.EntityManager, request.TargetEntity, request.TargetPlacedRefId, lookup);
            if (target == Entity.Null || !systemState.EntityManager.Exists(target))
                throw new InvalidOperationException($"[VVardenfell][MWScript] Actor vital target ref={request.TargetPlacedRefId} is not loaded.");

            if (!systemState.EntityManager.HasComponent<ActorVitalSet>(target))
            {
                if (request.Kind == (byte)MorrowindScriptActorVitalRequestKind.Resurrect)
                    return;

                throw new InvalidOperationException($"[VVardenfell][MWScript] Actor vital target ref={request.TargetPlacedRefId} has no ActorVitalSet.");
            }

            var vitals = systemState.EntityManager.GetComponentData<ActorVitalSet>(target);
            switch (request.Kind)
            {
                case 0:
                case (byte)MorrowindScriptActorVitalRequestKind.Health:
                    vitals.CurrentHealth = request.IsMod != 0 ? vitals.CurrentHealth + request.Value : request.Value;
                    if (vitals.CurrentHealth <= 0f)
                    {
                        var aftermath = ActorHitAftermathStateUtility.Require(
                            systemState.EntityManager,
                            target,
                            $"[VVardenfell][MWScript] Actor vital target ref={request.TargetPlacedRefId}");
                        ActorHitAftermathStateUtility.MarkDead(ref aftermath);
                        systemState.EntityManager.SetComponentData(target, aftermath);
                    }
                    break;
                case (byte)MorrowindScriptActorVitalRequestKind.Magicka:
                    vitals.CurrentMagicka = request.IsMod != 0 ? vitals.CurrentMagicka + request.Value : request.Value;
                    break;
                case (byte)MorrowindScriptActorVitalRequestKind.Fatigue:
                    vitals.CurrentFatigue = request.IsMod != 0 ? vitals.CurrentFatigue + request.Value : request.Value;
                    break;
                case (byte)MorrowindScriptActorVitalRequestKind.Resurrect:
                    if (vitals.CurrentHealth <= 0f)
                        vitals.CurrentHealth = vitals.ModifiedHealthBase > 0f ? vitals.ModifiedHealthBase : 1f;
                    if (systemState.EntityManager.HasComponent<ActorHitAftermathState>(target))
                    {
                        var aftermath = systemState.EntityManager.GetComponentData<ActorHitAftermathState>(target);
                        ActorHitAftermathStateUtility.Resurrect(ref aftermath);
                        systemState.EntityManager.SetComponentData(target, aftermath);
                    }
                    if (systemState.EntityManager.HasBuffer<ActorAnimationOverlayState>(target))
                        MorrowindHitAftermathAnimationSystem.RemoveAftermathOverlays(systemState.EntityManager.GetBuffer<ActorAnimationOverlayState>(target));
                    if (systemState.EntityManager.HasComponent<MorrowindActorDeathCounted>(target))
                        systemState.EntityManager.RemoveComponent<MorrowindActorDeathCounted>(target);
                    break;
                default:
                    throw new InvalidOperationException($"[VVardenfell][MWScript] Unknown actor vital kind {request.Kind}.");
            }
            systemState.EntityManager.SetComponentData(target, vitals);
        }
    }
}
