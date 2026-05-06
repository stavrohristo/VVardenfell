using System;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.MorrowindScript;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.WorldRefs;

namespace VVardenfell.Runtime.Magic
{
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptInterpreterSystem))]
    public partial struct ActorSpellMutationApplySystem : ISystem
    {
        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<MorrowindScriptRuntimeState>();
            systemState.RequireForUpdate<ActorSpellMutationRequest>();
            systemState.RequireForUpdate<LogicalRefLookup>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = systemState.EntityManager.GetBuffer<ActorSpellMutationRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            var lookup = SystemAPI.GetSingleton<LogicalRefLookup>();
            var contentBlobReference = SystemAPI.GetSingleton<RuntimeContentBlobReference>();
            if (!contentBlobReference.Blob.IsCreated)
                throw new InvalidOperationException("[VVardenfell][ContentBlob] AddSpell/RemoveSpell requires runtime content blob.");
            ref RuntimeContentBlob content = ref contentBlobReference.Blob.Value;

            for (int i = 0; i < requests.Length; i++)
                ApplyRequest(ref systemState, ref content, requests[i], lookup);

            requests.Clear();
        }

        void ApplyRequest(ref SystemState systemState, ref RuntimeContentBlob content, in ActorSpellMutationRequest request, in LogicalRefLookup lookup)
        {
            if (!request.Spell.IsValid
                || request.Spell.Index < 0
                || request.Spell.Index >= content.Spells.Length)
            {
                throw new InvalidOperationException($"[VVardenfell][Magic] AddSpell/RemoveSpell references invalid spell handle {request.Spell.Value}.");
            }

            Entity target = MorrowindRuntimeTargetResolver.ResolveLiveTarget(systemState.EntityManager, request.TargetEntity, request.TargetPlacedRefId, lookup);
            if (target == Entity.Null || !systemState.EntityManager.Exists(target))
                throw new InvalidOperationException($"[VVardenfell][Magic] AddSpell/RemoveSpell target ref={request.TargetPlacedRefId} is not loaded.");

            if (!systemState.EntityManager.HasBuffer<ActorKnownSpell>(target))
                throw new InvalidOperationException($"[VVardenfell][Magic] AddSpell/RemoveSpell target ref={request.TargetPlacedRefId} has no ActorKnownSpell buffer.");

            if (!systemState.EntityManager.HasBuffer<ActorActiveMagicEffect>(target))
                throw new InvalidOperationException($"[VVardenfell][Magic] AddSpell/RemoveSpell target ref={request.TargetPlacedRefId} has no ActorActiveMagicEffect buffer.");

            if (!systemState.EntityManager.HasComponent<ActorActiveMagicEffectDirty>(target))
                throw new InvalidOperationException($"[VVardenfell][Magic] AddSpell/RemoveSpell target ref={request.TargetPlacedRefId} has no ActorActiveMagicEffectDirty marker.");

            var knownSpells = systemState.EntityManager.GetBuffer<ActorKnownSpell>(target);
            if (request.Remove == 0)
            {
                MorrowindActorMagicUtility.AddKnownSpell(knownSpells, request.Spell);
                systemState.EntityManager.SetComponentEnabled<ActorActiveMagicEffectDirty>(target, true);
                return;
            }

            var activeEffects = systemState.EntityManager.GetBuffer<ActorActiveMagicEffect>(target);
            MorrowindActorMagicUtility.RemoveKnownSpell(ref content, knownSpells, activeEffects, request.Spell);
            systemState.EntityManager.SetComponentEnabled<ActorActiveMagicEffectDirty>(target, true);
        }
    }
}
