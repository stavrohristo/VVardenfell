using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.AI;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.WorldRefs;

namespace VVardenfell.Runtime.MorrowindScript
{
    [UpdateInGroup(typeof(MorrowindWorldMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptInterpreterSystem))]
    public partial class MorrowindScriptActorAiSettingApplySystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<MorrowindScriptActorAiSettingRequest>();
            RequireForUpdate<LogicalRefLookup>();
        }

        protected override void OnUpdate()
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = EntityManager.GetBuffer<MorrowindScriptActorAiSettingRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            var lookup = SystemAPI.GetSingleton<LogicalRefLookup>();
            for (int i = 0; i < requests.Length; i++)
                TryApplyRequest(requests[i], lookup);

            requests.Clear();
        }

        void TryApplyRequest(in MorrowindScriptActorAiSettingRequest request, in LogicalRefLookup lookup)
        {
            Entity target = MorrowindRuntimeTargetResolver.ResolveLiveTarget(EntityManager, request.TargetEntity, request.TargetPlacedRefId, lookup);
            if (target == Entity.Null || !EntityManager.HasComponent<ActorAiSettingsState>(target))
                return;

            var settings = EntityManager.GetComponentData<ActorAiSettingsState>(target);
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

            EntityManager.SetComponentData(target, settings);
        }
    }
}
