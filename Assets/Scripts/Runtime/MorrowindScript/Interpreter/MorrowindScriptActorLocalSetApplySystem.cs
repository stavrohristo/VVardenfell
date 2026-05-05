using System;
using Unity.Collections;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
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
            RequireForUpdate<RuntimeContentBlobReference>();
        }

        protected override void OnUpdate()
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = EntityManager.GetBuffer<MorrowindScriptActorLocalSetRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            ref RuntimeContentBlob contentBlob = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;
            for (int i = 0; i < requests.Length; i++)
                ApplyRequest(ref contentBlob, requests[i]);

            requests.Clear();
        }

        void ApplyRequest(ref RuntimeContentBlob contentBlob, in MorrowindScriptActorLocalSetRequest request)
        {
            Entity target = ResolveUniqueActor(request.ActorHandleValue);
            if (target == Entity.Null)
            {
                ulong actorIdHash = ResolveActorIdHash(ref contentBlob, request.ActorHandleValue);
                throw new InvalidOperationException($"[VVardenfell][MWScript] actor-local Set target hash {actorIdHash} is not exactly one loaded actor.");
            }

            var locals = EntityManager.GetBuffer<MorrowindScriptLocalValue>(target);
            if ((uint)request.LocalIndex >= (uint)locals.Length)
            {
                ulong actorIdHash = ResolveActorIdHash(ref contentBlob, request.ActorHandleValue);
                throw new InvalidOperationException($"[VVardenfell][MWScript] actor-local Set target hash {actorIdHash} local index {request.LocalIndex} is outside the runtime local buffer.");
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

        static ulong ResolveActorIdHash(ref RuntimeContentBlob contentBlob, int actorHandleValue)
        {
            var handle = new ActorDefHandle { Value = actorHandleValue };
            if ( !handle.IsValid || (uint)handle.Index >= (uint)contentBlob.Actors.Length)
                return actorHandleValue > 0 ? (ulong)actorHandleValue : 0UL;

            ref var actor = ref RuntimeContentBlobUtility.Get(ref contentBlob, handle);
            return actor.OriginalIdHash != 0UL ? actor.OriginalIdHash : actor.IdHash;
        }
    }
}
