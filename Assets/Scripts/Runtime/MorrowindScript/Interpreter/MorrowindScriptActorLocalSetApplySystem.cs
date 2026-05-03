using System;
using Unity.Collections;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.MorrowindScript
{
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptInterpreterSystem))]
    public partial class MorrowindScriptActorLocalSetApplySystem : SystemBase
    {
        EntityQuery _actorQuery;

        protected override void OnCreate()
        {
            _actorQuery = GetEntityQuery(
                ComponentType.ReadOnly<ActorSpawnSource>(),
                ComponentType.ReadWrite<MorrowindScriptLocalValue>());
            RequireForUpdate<MorrowindScriptRuntimeState>();
            RequireForUpdate<MorrowindScriptActorLocalSetRequest>();
        }

        protected override void OnUpdate()
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = EntityManager.GetBuffer<MorrowindScriptActorLocalSetRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            var contentDb = RuntimeContentDatabase.Active;
            for (int i = 0; i < requests.Length; i++)
                ApplyRequest(contentDb, requests[i]);

            requests.Clear();
        }

        void ApplyRequest(RuntimeContentDatabase contentDb, in MorrowindScriptActorLocalSetRequest request)
        {
            Entity target = ResolveUniqueActor(request.ActorHandleValue);
            if (target == Entity.Null)
            {
                string actorId = ResolveActorId(contentDb, request.ActorHandleValue);
                throw new InvalidOperationException($"[VVardenfell][MWScript] actor-local Set target '{actorId}' is not exactly one loaded actor.");
            }

            var locals = EntityManager.GetBuffer<MorrowindScriptLocalValue>(target);
            if ((uint)request.LocalIndex >= (uint)locals.Length)
            {
                string actorId = ResolveActorId(contentDb, request.ActorHandleValue);
                throw new InvalidOperationException($"[VVardenfell][MWScript] actor-local Set target '{actorId}' local index {request.LocalIndex} is outside the runtime local buffer.");
            }

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

        static string ResolveActorId(RuntimeContentDatabase contentDb, int actorHandleValue)
        {
            var handle = new ActorDefHandle { Value = actorHandleValue };
            if (contentDb == null || !handle.IsValid || (uint)handle.Index >= (uint)contentDb.ActorCount)
                return actorHandleValue.ToString();

            ref readonly var actor = ref contentDb.Get(handle);
            return string.IsNullOrWhiteSpace(actor.OriginalId) ? actor.Id : actor.OriginalId;
        }
    }
}
