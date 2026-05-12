using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Runtime.AI;
using VVardenfell.Runtime.Combat;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Movement;

namespace VVardenfell.Core.Cache
{
    public static class ActorBaselineAuthoringUtility
    {
        public static void QueueBaseline(
            ref EntityCommandBuffer ecb,
            Entity actorEntity,
            ref RuntimeContentBlob content,
            ActorDefHandle actorHandle,
            ref RuntimeActorDefBlob actor,
            float3 worldPosition,
            bool isInterior,
            int2 exteriorCell,
            FixedString128Bytes interiorCellId)
        {
            var statSeed = MorrowindActorMovementStats.CreateSeedFromActor(ref content, ref actor);
            ecb.AddComponent(actorEntity, statSeed.Attributes);
            ecb.AddComponent(actorEntity, new ActorAttributeBaseSet { Value = statSeed.AttributeBase });
            ecb.AddComponent(actorEntity, new ActorAttributeDamageSet { Value = statSeed.AttributeDamage });
            ecb.AddComponent(actorEntity, new ActorAttributeModifierSet { Value = statSeed.AttributeModifiers });
            ecb.AddComponent(actorEntity, statSeed.Skills);
            ecb.AddComponent(actorEntity, new ActorSkillBaseSet { Value = statSeed.SkillBase });
            ecb.AddComponent(actorEntity, new ActorSkillDamageSet { Value = statSeed.SkillDamage });
            ecb.AddComponent(actorEntity, new ActorSkillModifierSet { Value = statSeed.SkillModifiers });
            ecb.AddComponent(actorEntity, statSeed.Vitals);
            ecb.AddComponent(actorEntity, statSeed.VitalBase);
            ecb.AddComponent(actorEntity, statSeed.VitalModifiers);
            ecb.AddComponent(actorEntity, statSeed.EffectModifiers);
            ecb.AddComponent(actorEntity, new ActorDispositionState
            {
                BaseDisposition = actor.Disposition,
            });
            ecb.AddComponent(actorEntity, new ActorAiSettingsState
            {
                Hello = actor.AiData.Hello,
                Fight = actor.AiData.Fight,
                Flee = actor.AiData.Flee,
                Alarm = actor.AiData.Alarm,
            });
            ecb.AddComponent(actorEntity, new ActorScriptEventState());
            ecb.AddComponent(actorEntity, new ActorHitAftermathState());
            ecb.AddComponent<ActorHitAftermathAnimationActive>(actorEntity);
            ecb.SetComponentEnabled<ActorHitAftermathAnimationActive>(actorEntity, false);
            ecb.AddComponent<ActorDead>(actorEntity);
            ecb.SetComponentEnabled<ActorDead>(actorEntity, false);
            ecb.AddBuffer<ActorCombatTarget>(actorEntity);
            ecb.AddComponent(actorEntity, new ActorActiveCombatTarget());
            ecb.SetComponentEnabled<ActorActiveCombatTarget>(actorEntity, false);
            ecb.AddComponent(actorEntity, ActorCrimeState.Default);
            ecb.AddComponent(actorEntity, new ActorFriendlyHitState());
            ecb.AddComponent(actorEntity, new ActorBlockState());
            ecb.AddComponent(actorEntity, new ActorMeleeCombatAiState());
            ecb.AddComponent(actorEntity, new ActorCombatMovementState());
            ecb.AddComponent(actorEntity, new ActorAiGreetingState());
            var derivedMovement = MorrowindActorMovementStats.BuildDerived(
                ref content,
                statSeed.Attributes,
                statSeed.Skills,
                statSeed.Vitals,
                statSeed.EffectModifiers,
                inventoryWeight: 0f);
            ecb.AddComponent(actorEntity, derivedMovement);
            var knownSpells = ecb.AddBuffer<ActorKnownSpell>(actorEntity);
            var actorSpells = MorrowindActorMovementStats.BuildKnownSpellListFromActor(ref content, actorHandle);
            for (int i = 0; i < actorSpells.Length; i++)
                knownSpells.Add(actorSpells[i]);
            ecb.AddBuffer<ActorActiveMagicEffect>(actorEntity);
            ecb.AddBuffer<ActorActiveSpell>(actorEntity);
            ecb.AddBuffer<ActorUsedPower>(actorEntity);
            ecb.AddComponent(actorEntity, new ActorMagicCastState());
            ecb.AddComponent<ActorActiveMagicEffectDirty>(actorEntity);
            ecb.AddComponent<ActorActiveMagicEffectTicking>(actorEntity);
            ecb.SetComponentEnabled<ActorActiveMagicEffectTicking>(actorEntity, false);
            QueueActorFactionMembership(ref ecb, actorEntity, ref content, ref actor);

            if (ActorAiRuntimeAuthoringUtility.HasPackage(ref content, actorHandle))
            {
                var anchor = BuildActorAiAnchor(ref content, isInterior, exteriorCell, interiorCellId);
                ecb.AddComponent(actorEntity, new ActorAiState
                {
                    HomePosition = worldPosition,
                    CurrentNodeIndex = -1,
                    GoalNodeIndex = -1,
                    RandomSeed = BuildActorAiSeed(actorHandle, worldPosition, exteriorCell, isInterior),
                    Status = (byte)ActorAiPlannerStatus.Idle,
                });
                ecb.AddComponent(actorEntity, anchor);
                ecb.AddComponent<ActorAiNavigationAnchorDirty>(actorEntity);
                ecb.SetComponentEnabled<ActorAiNavigationAnchorDirty>(actorEntity, false);
                var packages = ecb.AddBuffer<ActorAiPackageRuntime>(actorEntity);
                ActorAiRuntimeAuthoringUtility.HydratePackages(ref content, actorHandle, anchor, packages);
                if (packages.Length > 0)
                {
                    ActorMovementAuthoringUtility.QueueEnsureMovableActor(
                        ref ecb,
                        actorEntity,
                        MorrowindActorMovementStats.BuildMovementSpeed(
                            ref content,
                            actor.Kind,
                            statSeed.Attributes,
                            statSeed.Skills,
                            statSeed.Vitals,
                            statSeed.EffectModifiers,
                            derivedMovement));
                }
            }
        }

        static void QueueActorFactionMembership(
            ref EntityCommandBuffer ecb,
            Entity actorEntity,
            ref RuntimeContentBlob content,
            ref RuntimeActorDefBlob actor)
        {
            if (actor.FactionIdHash == 0UL)
                return;

            if (!RuntimeContentBlobUtility.TryGetFactionHandleByIdHash(ref content, actor.FactionIdHash, out var factionHandle) || !factionHandle.IsValid)
                throw new InvalidOperationException($"Actor hash {actor.IdHash} references unknown faction hash {actor.FactionIdHash}.");

            var factions = ecb.AddBuffer<ActorFactionMembership>(actorEntity);
            factions.Add(new ActorFactionMembership
            {
                FactionIndex = factionHandle.Index,
                Rank = actor.Rank,
                Joined = 1,
            });
        }

        static ActorAiNavigationAnchor BuildActorAiAnchor(
            ref RuntimeContentBlob content,
            bool isInterior,
            int2 exteriorCell,
            FixedString128Bytes interiorCellId)
        {
            var anchor = new ActorAiNavigationAnchor
            {
                PathGridIndex = -1,
                GridX = exteriorCell.x,
                GridY = exteriorCell.y,
                IsInterior = (byte)(isInterior ? 1 : 0),
            };

            if (isInterior)
            {
                ulong interiorCellHash = RuntimeContentStableHash.HashInteriorCellId(interiorCellId.ToString());
                anchor.InteriorCellHash = interiorCellHash;
                if (RuntimeContentBlobUtility.TryGetInteriorPathGridHandleByCellHash(ref content, interiorCellHash, out var handle) && handle.IsValid)
                {
                    anchor.PathGridIndex = handle.Index;
                    anchor.IsResolved = 1;
                }
            }
            else if (RuntimeContentBlobUtility.TryGetExteriorPathGridHandle(ref content, exteriorCell.x, exteriorCell.y, out var handle) && handle.IsValid)
            {
                anchor.PathGridIndex = handle.Index;
                anchor.IsResolved = 1;
            }

            return anchor;
        }

        static uint BuildActorAiSeed(ActorDefHandle actor, float3 position, int2 exteriorCell, bool isInterior)
        {
            uint seed = math.hash(new uint4(
                (uint)actor.Value,
                math.asuint(position.x) ^ math.asuint(position.z),
                (uint)exteriorCell.x ^ ((uint)exteriorCell.y << 16),
                isInterior ? 1u : 0u));
            return seed == 0u ? 1u : seed;
        }
    }
}
