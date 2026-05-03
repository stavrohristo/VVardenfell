using System;
using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.MorrowindScript;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.WorldRefs;

namespace VVardenfell.Runtime.Magic
{
    [UpdateInGroup(typeof(MorrowindWorldMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptInterpreterSystem))]
    public partial class ActorSpellMutationApplySystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<MorrowindScriptRuntimeState>();
            RequireForUpdate<ActorSpellMutationRequest>();
            RequireForUpdate<LogicalRefLookup>();
        }

        protected override void OnUpdate()
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = EntityManager.GetBuffer<ActorSpellMutationRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            var lookup = SystemAPI.GetSingleton<LogicalRefLookup>();
            var contentDb = RuntimeContentDatabase.Active;
            if (contentDb == null)
                throw new InvalidOperationException("[VVardenfell][Magic] AddSpell/RemoveSpell requires active runtime content.");

            for (int i = 0; i < requests.Length; i++)
                ApplyRequest(contentDb, requests[i], lookup);

            requests.Clear();
        }

        void ApplyRequest(RuntimeContentDatabase contentDb, in ActorSpellMutationRequest request, in LogicalRefLookup lookup)
        {
            if (!request.Spell.IsValid
                || contentDb.Data.Spells == null
                || request.Spell.Index < 0
                || request.Spell.Index >= contentDb.Data.Spells.Length)
            {
                throw new InvalidOperationException($"[VVardenfell][Magic] AddSpell/RemoveSpell references invalid spell handle {request.Spell.Value}.");
            }

            Entity target = MorrowindRuntimeTargetResolver.ResolveLiveTarget(EntityManager, request.TargetEntity, request.TargetPlacedRefId, lookup);
            if (target == Entity.Null || !EntityManager.Exists(target))
                throw new InvalidOperationException($"[VVardenfell][Magic] AddSpell/RemoveSpell target ref={request.TargetPlacedRefId} is not loaded.");

            if (!EntityManager.HasBuffer<ActorKnownSpell>(target))
                throw new InvalidOperationException($"[VVardenfell][Magic] AddSpell/RemoveSpell target ref={request.TargetPlacedRefId} has no ActorKnownSpell buffer.");

            if (!EntityManager.HasBuffer<ActorActiveMagicEffect>(target))
                throw new InvalidOperationException($"[VVardenfell][Magic] AddSpell/RemoveSpell target ref={request.TargetPlacedRefId} has no ActorActiveMagicEffect buffer.");

            var knownSpells = EntityManager.GetBuffer<ActorKnownSpell>(target);
            if (request.Remove == 0)
            {
                MorrowindActorMagicUtility.AddKnownSpell(knownSpells, request.Spell);
                return;
            }

            var activeEffects = EntityManager.GetBuffer<ActorActiveMagicEffect>(target);
            MorrowindActorMagicUtility.RemoveKnownSpell(contentDb, knownSpells, activeEffects, request.Spell);
        }
    }
}
