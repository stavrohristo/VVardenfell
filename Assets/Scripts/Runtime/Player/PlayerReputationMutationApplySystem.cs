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
        EntityQuery _playerQuery;

        public void OnCreate(ref SystemState state)
        {
            _playerQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadWrite<ActorIdentitySet>());
            state.RequireForUpdate<MorrowindScriptRuntimeState>();
            state.RequireForUpdate<PlayerReputationMutationRequest>();
            state.RequireForUpdate(_playerQuery);
        }

        public void OnUpdate(ref SystemState state)
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = state.EntityManager.GetBuffer<PlayerReputationMutationRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            Entity player = _playerQuery.GetSingletonEntity();
            var identity = state.EntityManager.GetComponentData<ActorIdentitySet>(player);
            for (int i = 0; i < requests.Length; i++)
                identity.Reputation += requests[i].Delta;
            state.EntityManager.SetComponentData(player, identity);

            requests.Clear();
        }
    }
}
