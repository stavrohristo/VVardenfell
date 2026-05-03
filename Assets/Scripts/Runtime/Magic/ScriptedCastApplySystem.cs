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
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptInterpreterSystem))]
    public partial class ScriptedCastApplySystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<MorrowindScriptRuntimeState>();
            RequireForUpdate<ScriptedCastRequest>();
            RequireForUpdate<LogicalRefLookup>();
        }

        protected override void OnUpdate()
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = EntityManager.GetBuffer<ScriptedCastRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            var lookup = SystemAPI.GetSingleton<LogicalRefLookup>();
            var contentDb = RuntimeContentDatabase.Active;
            if (contentDb == null)
                throw new InvalidOperationException("[VVardenfell][Magic] Cast requires active runtime content.");

            for (int i = 0; i < requests.Length; i++)
                ApplyRequest(contentDb, requests[i], lookup);

            requests.Clear();
        }

        void ApplyRequest(RuntimeContentDatabase contentDb, in ScriptedCastRequest request, in LogicalRefLookup lookup)
        {
            if (!request.Spell.IsValid
                || contentDb.Data.Spells == null
                || request.Spell.Index < 0
                || request.Spell.Index >= contentDb.Data.Spells.Length)
            {
                throw new InvalidOperationException($"[VVardenfell][Magic] Cast references invalid spell handle {request.Spell.Value}.");
            }

            Entity caster = MorrowindRuntimeTargetResolver.ResolveLiveTarget(EntityManager, request.CasterEntity, request.CasterPlacedRefId, lookup);
            if (caster == Entity.Null || !EntityManager.Exists(caster))
                throw new InvalidOperationException($"[VVardenfell][Magic] Cast caster ref={request.CasterPlacedRefId} is not loaded.");

            Entity target = MorrowindRuntimeTargetResolver.ResolveLiveTarget(EntityManager, request.TargetEntity, request.TargetPlacedRefId, lookup);
            if (target == Entity.Null || !EntityManager.Exists(target))
                throw new InvalidOperationException($"[VVardenfell][Magic] Cast target ref={request.TargetPlacedRefId} is not loaded.");

            if (!MorrowindActorMagicUtility.CanRepresentScriptedCast(contentDb, request.Spell))
                throw new InvalidOperationException($"[VVardenfell][Magic] Cast spell handle {request.Spell.Value} contains effects the scripted cast pipeline cannot represent.");

            if (!EntityManager.HasBuffer<ActorActiveMagicEffect>(target))
                throw new InvalidOperationException($"[VVardenfell][Magic] Cast target ref={request.TargetPlacedRefId} has no active magic effect state.");

            var targetEffects = EntityManager.GetBuffer<ActorActiveMagicEffect>(target);
            MorrowindActorMagicUtility.ApplyScriptedCast(contentDb, targetEffects, request.Spell);
        }
    }
}
