using System;
using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.MorrowindScript;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Player
{
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptInterpreterSystem))]
    public partial class PlayerReputationMutationApplySystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<MorrowindScriptRuntimeState>();
            RequireForUpdate<PlayerReputationMutationRequest>();
        }

        protected override void OnUpdate()
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = EntityManager.GetBuffer<PlayerReputationMutationRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            using var query = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadWrite<ActorIdentitySet>());
            if (query.IsEmptyIgnoreFilter)
                throw new InvalidOperationException("[VVardenfell][Player] ModReputation requires an active player identity.");

            Entity player = query.GetSingletonEntity();
            var identity = EntityManager.GetComponentData<ActorIdentitySet>(player);
            for (int i = 0; i < requests.Length; i++)
                identity.Reputation += requests[i].Delta;
            EntityManager.SetComponentData(player, identity);

            requests.Clear();
        }
    }
}
