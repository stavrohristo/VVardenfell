using Unity.Burst;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.AI;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.WorldRefs;

namespace VVardenfell.Runtime.MorrowindScript
{
    [BurstCompile]
    [UpdateInGroup(typeof(MorrowindGameplayMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptInterpreterSystem))]
    public partial struct MorrowindScriptActorAiSettingApplySystem : ISystem
    {
        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<MorrowindScriptActorAiSettingRequest>();
            systemState.RequireForUpdate<LogicalRefLookup>();
        }
        [BurstCompile]
        public void OnUpdate(ref SystemState systemState)
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = systemState.EntityManager.GetBuffer<MorrowindScriptActorAiSettingRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            var lookup = SystemAPI.GetSingleton<LogicalRefLookup>();
            for (int i = 0; i < requests.Length; i++)
                TryApplyRequest(ref systemState, requests[i], lookup);

            requests.Clear();
        }

        void TryApplyRequest(ref SystemState systemState, in MorrowindScriptActorAiSettingRequest request, in LogicalRefLookup lookup)
        {
            Entity target = MorrowindRuntimeTargetResolver.ResolveLiveTarget(systemState.EntityManager, request.TargetEntity, request.TargetPlacedRefId, lookup);
            if (target == Entity.Null || !systemState.EntityManager.HasComponent<ActorAiSettingsState>(target))
                return;

            var settings = systemState.EntityManager.GetComponentData<ActorAiSettingsState>(target);
            switch ((MorrowindScriptActorAiSettingKind)request.Kind)
            {
                case MorrowindScriptActorAiSettingKind.Hello:
                    settings.Hello = request.IsMod != 0 ? settings.Hello + request.Value : request.Value;
                    break;
                case MorrowindScriptActorAiSettingKind.Fight:
                    settings.Fight = request.IsMod != 0 ? settings.Fight + request.Value : request.Value;
                    break;
                case MorrowindScriptActorAiSettingKind.Flee:
                    settings.Flee = request.IsMod != 0 ? settings.Flee + request.Value : request.Value;
                    break;
                case MorrowindScriptActorAiSettingKind.Alarm:
                    settings.Alarm = request.IsMod != 0 ? settings.Alarm + request.Value : request.Value;
                    break;
                default:
                    return;
            }

            systemState.EntityManager.SetComponentData(target, settings);
        }
    }
}
