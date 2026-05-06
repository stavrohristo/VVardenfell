using System;
using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.MorrowindScript;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Player
{
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptInterpreterSystem))]
    public partial struct PlayerReputationMutationApplySystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<MorrowindScriptRuntimeState>();
            state.RequireForUpdate<PlayerReputationMutationRequest>();
        }

        public void OnUpdate(ref SystemState state)
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = state.EntityManager.GetBuffer<PlayerReputationMutationRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            using var query = state.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadWrite<ActorIdentitySet>());
            if (query.IsEmptyIgnoreFilter)
                throw new InvalidOperationException("[VVardenfell][Player] ModReputation requires an active player identity.");

            Entity player = query.GetSingletonEntity();
            var identity = state.EntityManager.GetComponentData<ActorIdentitySet>(player);
            for (int i = 0; i < requests.Length; i++)
                identity.Reputation += requests[i].Delta;
            state.EntityManager.SetComponentData(player, identity);

            requests.Clear();
        }
    }
}
