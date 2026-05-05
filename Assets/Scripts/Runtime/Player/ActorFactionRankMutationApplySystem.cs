using System;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.MorrowindScript;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.WorldRefs;

namespace VVardenfell.Runtime.Player
{
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptInterpreterSystem))]
    public partial class ActorFactionRankMutationApplySystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<MorrowindScriptRuntimeState>();
            RequireForUpdate<ActorFactionRankMutationRequest>();
            RequireForUpdate<LogicalRefLookup>();
            RequireForUpdate<RuntimeContentBlobReference>();
        }

        protected override void OnUpdate()
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = EntityManager.GetBuffer<ActorFactionRankMutationRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            ref RuntimeContentBlob contentBlob = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;
            var lookup = SystemAPI.GetSingleton<LogicalRefLookup>();
            for (int i = 0; i < requests.Length; i++)
                ApplyRequest(ref contentBlob, requests[i], lookup);

            requests.Clear();
        }

        void ApplyRequest(ref RuntimeContentBlob contentBlob, in ActorFactionRankMutationRequest request, in LogicalRefLookup lookup)
        {
            Entity target = MorrowindRuntimeTargetResolver.ResolveLiveTarget(EntityManager, request.TargetEntity, request.TargetPlacedRefId, lookup);
            if (target == Entity.Null)
                throw new InvalidOperationException($"[VVardenfell][Player] RaiseRank target ref={request.TargetPlacedRefId} is not loaded.");

            if (MorrowindRuntimeTargetResolver.IsPlayerEntity(EntityManager, target))
                return;

            if (!EntityManager.HasComponent<ActorSpawnSource>(target))
                throw new InvalidOperationException("[VVardenfell][Player] RaiseRank target is not backed by an actor spawn source.");

            ActorDefHandle actorHandle = EntityManager.GetComponentData<ActorSpawnSource>(target).Definition;
            if (!actorHandle.IsValid)
                throw new InvalidOperationException("[VVardenfell][Player] RaiseRank target has an invalid actor definition.");

            ref RuntimeActorDefBlob actor = ref RuntimeContentBlobUtility.Get(ref contentBlob, actorHandle);
            string factionId = actor.FactionId.ToString();
            string actorId = actor.Id.ToString();
            if (string.IsNullOrWhiteSpace(factionId))
                return;

            if (!RuntimeContentBlobUtility.TryGetFactionHandleByIdHash(ref contentBlob, RuntimeContentStableHash.HashId(factionId), out var factionHandle) || !factionHandle.IsValid)
                throw new InvalidOperationException($"[VVardenfell][Player] RaiseRank target actor '{actorId}' references unknown faction '{factionId}'.");

            if (!EntityManager.HasBuffer<ActorFactionMembership>(target))
                throw new InvalidOperationException($"[VVardenfell][Player] RaiseRank target actor '{actorId}' has no actor faction state.");

            var factions = EntityManager.GetBuffer<ActorFactionMembership>(target);
            int index = FindActorFactionIndex(factions, factionHandle.Index);
            if (index >= 0)
            {
                var membership = factions[index];
                membership.Joined = 1;
                membership.Rank += 1;
                factions[index] = membership;
                return;
            }

            factions.Add(new ActorFactionMembership
            {
                FactionIndex = factionHandle.Index,
                Rank = actor.Rank + 1,
                Joined = 1,
            });
        }

        static int FindActorFactionIndex(DynamicBuffer<ActorFactionMembership> factions, int factionIndex)
        {
            for (int i = 0; i < factions.Length; i++)
            {
                if (factions[i].FactionIndex == factionIndex)
                    return i;
            }

            return -1;
        }
    }
}
