using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Inventory;
using VVardenfell.Runtime.Movement;

namespace VVardenfell.Runtime.Combat
{
    static class MorrowindCrimeAwarenessUtility
    {
        static readonly short BlindEffectId = RequireEffectId("sEffectBlind");
        static readonly short CalmHumanoidEffectId = RequireEffectId("sEffectCalmHumanoid");
        static readonly short ChameleonEffectId = RequireEffectId("sEffectChameleon");
        static readonly short InvisibilityEffectId = RequireEffectId("sEffectInvisibility");

        public static bool AwarenessCheck(
            ref RuntimeContentBlob content,
            EntityManager entityManager,
            Entity target,
            Entity observer,
            in LocalTransform targetTransform,
            in LocalTransform observerTransform,
            uint roll0To99)
        {
            RequireAwarenessComposition(entityManager, target, "target");
            RequireAwarenessComposition(entityManager, observer, "observer");

            var observerVitals = entityManager.GetComponentData<ActorVitalSet>(observer);
            if (observerVitals.CurrentHealth <= 0f)
                return false;
            if (entityManager.HasComponent<PlacedRefRuntimeState>(observer)
                && entityManager.GetComponentData<PlacedRefRuntimeState>(observer).Disabled != 0)
            {
                return false;
            }

            var targetAttributes = entityManager.GetComponentData<ActorAttributeSet>(target);
            var targetSkills = entityManager.GetComponentData<ActorSkillSet>(target);
            var targetDerived = entityManager.GetComponentData<ActorDerivedMovementStats>(target);
            var targetEffects = entityManager.GetBuffer<ActorActiveMagicEffect>(target, true);
            var movement = entityManager.GetComponentData<MorrowindMovementState>(target);

            float sneakTerm = 0f;
            if (movement.SneakHeld)
            {
                float bootWeight = 0f;
                if (movement.Grounded)
                    bootWeight = ResolveFootwearWeight(ref content, entityManager, target);

                sneakTerm =
                    RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fSneakSkillMult) * targetSkills.Sneak
                    + 0.2f * targetAttributes.Agility
                    + 0.1f * targetAttributes.Luck
                    + bootWeight * RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fSneakBootMult);
            }

            float distanceMw = math.distance(targetTransform.Position, observerTransform.Position) / WorldScale.MwUnitsToMeters;
            float distanceTerm =
                RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fSneakDistanceBase)
                + RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fSneakDistanceMultiplier) * distanceMw;

            float x = sneakTerm * distanceTerm * targetDerived.FatigueTerm
                      + MorrowindMeleeCombatMechanics.SumEffectMagnitude(targetEffects, ChameleonEffectId);
            if (MorrowindMeleeCombatMechanics.SumEffectMagnitude(targetEffects, InvisibilityEffectId) > 0f)
                x += 100f;

            var observerAttributes = entityManager.GetComponentData<ActorAttributeSet>(observer);
            var observerSkills = entityManager.GetComponentData<ActorSkillSet>(observer);
            var observerDerived = entityManager.GetComponentData<ActorDerivedMovementStats>(observer);
            var observerEffects = entityManager.GetBuffer<ActorActiveMagicEffect>(observer, true);

            float observerTerm = observerSkills.Sneak
                                 + 0.2f * observerAttributes.Agility
                                 + 0.1f * observerAttributes.Luck
                                 - MorrowindMeleeCombatMechanics.SumEffectMagnitude(observerEffects, BlindEffectId);

            float3 toTarget = targetTransform.Position - observerTransform.Position;
            toTarget.y = 0f;
            float3 observerForward = math.normalizesafe(math.rotate(observerTransform.Rotation, new float3(0f, 0f, 1f)), new float3(0f, 0f, 1f));
            observerForward.y = 0f;
            observerForward = math.normalizesafe(observerForward, new float3(0f, 0f, 1f));
            bool targetBehindObserver = math.lengthsq(toTarget) > 0.0001f
                                        && math.dot(observerForward, math.normalize(toTarget)) < 0f;

            float viewMultiplier = targetBehindObserver
                ? RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fSneakNoViewMult)
                : RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fSneakViewMult);

            float y = observerTerm * observerDerived.FatigueTerm * viewMultiplier;
            float targetScore = x - y;
            return roll0To99 >= targetScore;
        }

        public static bool IsCalmedHumanoid(EntityManager entityManager, Entity actor)
        {
            if (!entityManager.HasBuffer<ActorActiveMagicEffect>(actor))
                throw new InvalidOperationException($"[VVardenfell][CrimeAwareness] Actor entity={actor.Index}:{actor.Version} has no ActorActiveMagicEffect buffer.");

            return MorrowindMeleeCombatMechanics.SumEffectMagnitude(entityManager.GetBuffer<ActorActiveMagicEffect>(actor, true), CalmHumanoidEffectId) > 0f;
        }

        static float ResolveFootwearWeight(ref RuntimeContentBlob content, EntityManager entityManager, Entity target)
        {
            if (!entityManager.HasBuffer<ActorEquipmentSlot>(target))
                throw new InvalidOperationException($"[VVardenfell][CrimeAwareness] Sneaking target entity={target.Index}:{target.Version} has no ActorEquipmentSlot buffer.");

            var equipment = entityManager.GetBuffer<ActorEquipmentSlot>(target, true);
            if (ActorEquipmentRuntimeUtility.TryGetEquipmentInSlot(equipment, ItemEquipmentSlot.Boots, out var boots))
                return RuntimeContentBlobUtility.RequireCarryWeight(ref content, boots.Content);
            if (ActorEquipmentRuntimeUtility.TryGetEquipmentInSlot(equipment, ItemEquipmentSlot.Shoes, out var shoes))
                return RuntimeContentBlobUtility.RequireCarryWeight(ref content, shoes.Content);
            return 0f;
        }

        static void RequireAwarenessComposition(EntityManager entityManager, Entity actor, string role)
        {
            if (actor == Entity.Null || !entityManager.Exists(actor))
                throw new InvalidOperationException($"[VVardenfell][CrimeAwareness] Awareness {role} entity is missing.");
            if (!entityManager.HasComponent<ActorAttributeSet>(actor))
                throw new InvalidOperationException($"[VVardenfell][CrimeAwareness] Awareness {role} entity={actor.Index}:{actor.Version} has no ActorAttributeSet.");
            if (!entityManager.HasComponent<ActorSkillSet>(actor))
                throw new InvalidOperationException($"[VVardenfell][CrimeAwareness] Awareness {role} entity={actor.Index}:{actor.Version} has no ActorSkillSet.");
            if (!entityManager.HasComponent<ActorVitalSet>(actor))
                throw new InvalidOperationException($"[VVardenfell][CrimeAwareness] Awareness {role} entity={actor.Index}:{actor.Version} has no ActorVitalSet.");
            if (!entityManager.HasComponent<ActorDerivedMovementStats>(actor))
                throw new InvalidOperationException($"[VVardenfell][CrimeAwareness] Awareness {role} entity={actor.Index}:{actor.Version} has no ActorDerivedMovementStats.");
            if (!entityManager.HasComponent<MorrowindMovementState>(actor))
                throw new InvalidOperationException($"[VVardenfell][CrimeAwareness] Awareness {role} entity={actor.Index}:{actor.Version} has no MorrowindMovementState.");
            if (!entityManager.HasBuffer<ActorActiveMagicEffect>(actor))
                throw new InvalidOperationException($"[VVardenfell][CrimeAwareness] Awareness {role} entity={actor.Index}:{actor.Version} has no ActorActiveMagicEffect buffer.");
        }

        static short RequireEffectId(string gmstId)
        {
            if (!MorrowindMagicEffectTextUtility.TryResolveEffectId(gmstId, out short effectId))
                throw new InvalidOperationException($"[VVardenfell][CrimeAwareness] Required magic effect GMST '{gmstId}' is not mapped.");
            return effectId;
        }
    }
}
