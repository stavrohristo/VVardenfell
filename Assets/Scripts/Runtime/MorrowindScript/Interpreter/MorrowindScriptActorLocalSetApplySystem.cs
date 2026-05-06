using System;
using Unity.Collections;
using Unity.Burst;
using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.MorrowindScript
{
    [BurstCompile]
    [UpdateInGroup(typeof(MorrowindGameplayMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptInterpreterSystem))]
    public partial struct MorrowindScriptActorLocalSetApplySystem : ISystem
    {
        EntityQuery _actorQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _actorQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<ActorSpawnSource>(),
                ComponentType.ReadWrite<MorrowindScriptLocalValue>());
            systemState.RequireForUpdate<MorrowindScriptRuntimeState>();
            systemState.RequireForUpdate<MorrowindScriptActorLocalSetRequest>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState systemState)
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = systemState.EntityManager.GetBuffer<MorrowindScriptActorLocalSetRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            for (int i = 0; i < requests.Length; i++)
                ApplyRequest(ref systemState, requests[i]);

            requests.Clear();
        }

        void ApplyRequest(ref SystemState systemState, in MorrowindScriptActorLocalSetRequest request)
        {
            Entity target = ResolveUniqueActor(request.ActorHandleValue);
            if (target == Entity.Null)
                throw new InvalidOperationException("[VVardenfell][MWScript] actor-local Set target is not exactly one loaded actor.");

            var locals = systemState.EntityManager.GetBuffer<MorrowindScriptLocalValue>(target);
            if ((uint)request.LocalIndex >= (uint)locals.Length)
                throw new InvalidOperationException("[VVardenfell][MWScript] actor-local Set local index is outside the runtime local buffer.");

            locals[request.LocalIndex] = request.Value;
        }

        Entity ResolveUniqueActor(int actorHandleValue)
        {
            Entity match = Entity.Null;
            int count = 0;
            using var entities = _actorQuery.ToEntityArray(Allocator.Temp);
            using var sources = _actorQuery.ToComponentDataArray<ActorSpawnSource>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                if (sources[i].Definition.Value != actorHandleValue)
                    continue;

                match = entities[i];
                count++;
                if (count > 1)
                    return Entity.Null;
            }

            return count == 1 ? match : Entity.Null;
        }
    }
}
