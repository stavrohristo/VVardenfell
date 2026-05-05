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
    public partial class PlayerFactionMutationApplySystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<MorrowindScriptRuntimeState>();
            RequireForUpdate<PlayerFactionMutationRequest>();
            RequireForUpdate<LogicalRefLookup>();
            RequireForUpdate<RuntimeContentBlobReference>();
        }

        protected override void OnUpdate()
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = EntityManager.GetBuffer<PlayerFactionMutationRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            ref RuntimeContentBlob contentBlob = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;

            using var query = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadWrite<PlayerFactionMembership>());
            if (query.IsEmptyIgnoreFilter)
                throw new InvalidOperationException("[VVardenfell][Player] Player faction mutation requires an active player faction buffer.");

            Entity player = query.GetSingletonEntity();
            var factions = EntityManager.GetBuffer<PlayerFactionMembership>(player);
            var lookup = SystemAPI.GetSingleton<LogicalRefLookup>();
            for (int i = 0; i < requests.Length; i++)
                ApplyRequest(ref contentBlob, factions, requests[i], lookup);

            requests.Clear();
        }

        void ApplyRequest(ref RuntimeContentBlob contentBlob, DynamicBuffer<PlayerFactionMembership> factions, in PlayerFactionMutationRequest request, in LogicalRefLookup lookup)
        {
            int factionIndex = request.FactionIndex;
            if (factionIndex < 0)
                factionIndex = ResolveSourceFactionIndex(ref contentBlob, request, lookup);

            int index = FindPlayerFactionIndex(factions, factionIndex);
            switch ((PlayerFactionMutationKind)request.Kind)
            {
                case PlayerFactionMutationKind.Expel:
                    if (index < 0)
                    {
                        factions.Add(new PlayerFactionMembership
                        {
                            FactionIndex = factionIndex,
                            Rank = -1,
                            Expelled = 1,
                        });
                    }
                    else
                    {
                        var membership = factions[index];
                        membership.Expelled = 1;
                        factions[index] = membership;
                    }
                    return;

                case PlayerFactionMutationKind.ClearExpelled:
                    if (index >= 0)
                    {
                        var membership = factions[index];
                        membership.Expelled = 0;
                        factions[index] = membership;
                    }
                    return;

                case PlayerFactionMutationKind.ModReputation:
                    if (index < 0)
                    {
                        factions.Add(new PlayerFactionMembership
                        {
                            FactionIndex = factionIndex,
                            Rank = -1,
                            Reputation = request.Value,
                        });
                    }
                    else
                    {
                        var membership = factions[index];
                        membership.Reputation += request.Value;
                        factions[index] = membership;
                    }
                    return;

                case PlayerFactionMutationKind.Join:
                    if (index < 0)
                    {
                        factions.Add(new PlayerFactionMembership
                        {
                            FactionIndex = factionIndex,
                            Rank = 0,
                            Joined = 1,
                        });
                        return;
                    }

                    var joined = factions[index];
                    joined.Joined = 1;
                    if (joined.Rank < 0)
                        joined.Rank = 0;
                    factions[index] = joined;
                    return;

                case PlayerFactionMutationKind.RaiseRank:
                    if (index < 0)
                    {
                        factions.Add(new PlayerFactionMembership
                        {
                            FactionIndex = factionIndex,
                            Rank = 0,
                            Joined = 1,
                        });
                        return;
                    }

                    var raised = factions[index];
                    raised.Joined = 1;
                    if (raised.Rank < 0)
                        raised.Rank = 0;
                    else
                        raised.Rank += 1;
                    factions[index] = raised;
                    return;

                default:
                    throw new InvalidOperationException($"[VVardenfell][Player] Unknown player faction mutation kind {request.Kind}.");
            }
        }

        int ResolveSourceFactionIndex(ref RuntimeContentBlob contentBlob, in PlayerFactionMutationRequest request, in LogicalRefLookup lookup)
        {
            Entity source = MorrowindRuntimeTargetResolver.ResolveLiveTarget(EntityManager, request.SourceEntity, request.SourcePlacedRefId, lookup);
            if (source == Entity.Null || !EntityManager.HasComponent<ActorSpawnSource>(source))
                throw new InvalidOperationException("[VVardenfell][Player] Player faction mutation without explicit faction requires an actor source.");

            ActorDefHandle actorHandle = EntityManager.GetComponentData<ActorSpawnSource>(source).Definition;
            if (!actorHandle.IsValid)
                throw new InvalidOperationException("[VVardenfell][Player] Player faction mutation source has an invalid actor definition.");

            ref RuntimeActorDefBlob actor = ref RuntimeContentBlobUtility.Get(ref contentBlob, actorHandle);
            string factionId = actor.FactionId.ToString();
            if (string.IsNullOrWhiteSpace(factionId)
                || !RuntimeContentBlobUtility.TryGetFactionHandleByIdHash(ref contentBlob, RuntimeContentStableHash.HashId(factionId), out var factionHandle)
                || !factionHandle.IsValid)
            {
                throw new InvalidOperationException($"[VVardenfell][Player] Player faction mutation source actor '{actor.Id.ToString()}' has no valid faction.");
            }

            return factionHandle.Index;
        }

        static int FindPlayerFactionIndex(DynamicBuffer<PlayerFactionMembership> factions, int factionIndex)
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
