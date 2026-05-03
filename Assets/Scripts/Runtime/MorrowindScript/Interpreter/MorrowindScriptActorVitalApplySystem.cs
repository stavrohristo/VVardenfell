using System;
using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.WorldRefs;

namespace VVardenfell.Runtime.MorrowindScript
{
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptInterpreterSystem))]
    public partial class MorrowindScriptActorVitalApplySystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<MorrowindScriptActorVitalRequest>();
            RequireForUpdate<LogicalRefLookup>();
        }

        protected override void OnUpdate()
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = EntityManager.GetBuffer<MorrowindScriptActorVitalRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            var lookup = SystemAPI.GetSingleton<LogicalRefLookup>();
            for (int i = 0; i < requests.Length; i++)
                ApplyRequest(requests[i], lookup);

            requests.Clear();
        }

        void ApplyRequest(in MorrowindScriptActorVitalRequest request, in LogicalRefLookup lookup)
        {
            Entity target = MorrowindRuntimeTargetResolver.ResolveLiveTarget(EntityManager, request.TargetEntity, request.TargetPlacedRefId, lookup);
            if (target == Entity.Null || !EntityManager.Exists(target))
                throw new InvalidOperationException($"[VVardenfell][MWScript] Actor vital target ref={request.TargetPlacedRefId} is not loaded.");

            if (!EntityManager.HasComponent<ActorVitalSet>(target))
            {
                if (request.Kind == (byte)MorrowindScriptActorVitalRequestKind.Resurrect)
                    return;

                throw new InvalidOperationException($"[VVardenfell][MWScript] Actor vital target ref={request.TargetPlacedRefId} has no ActorVitalSet.");
            }

            var vitals = EntityManager.GetComponentData<ActorVitalSet>(target);
            switch (request.Kind)
            {
                case 0:
                case (byte)MorrowindScriptActorVitalRequestKind.Health:
                    vitals.CurrentHealth = request.IsMod != 0 ? vitals.CurrentHealth + request.Value : request.Value;
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
                    if (EntityManager.HasComponent<MorrowindActorDeathCounted>(target))
                        EntityManager.RemoveComponent<MorrowindActorDeathCounted>(target);
                    break;
                default:
                    throw new InvalidOperationException($"[VVardenfell][MWScript] Unknown actor vital kind {request.Kind}.");
            }
            EntityManager.SetComponentData(target, vitals);
        }
    }
}
