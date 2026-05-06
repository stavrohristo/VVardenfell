using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Magic;
using VVardenfell.Runtime.MorrowindScript;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Shell;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Combat
{
    [UpdateInGroup(typeof(MorrowindDamageSystemGroup))]
    [UpdateAfter(typeof(MorrowindDifficultyDamageSystem))]
    [UpdateBefore(typeof(MorrowindDamageApplySystem))]
    public partial struct MorrowindDiseaseContactSystem : ISystem
    {
        static readonly short CorprusEffectId = RequireEffectId("sEffectCorpus");
        static readonly short ResistCommonDiseaseEffectId = RequireEffectId("sEffectResistCommonDisease");
        static readonly short WeaknessToCommonDiseaseEffectId = RequireEffectId("sEffectWeaknessToCommonDisease");
        static readonly short ResistBlightDiseaseEffectId = RequireEffectId("sEffectResistBlightDisease");
        static readonly short WeaknessToBlightDiseaseEffectId = RequireEffectId("sEffectWeaknessToBlightDisease");
        static readonly short ResistCorprusDiseaseEffectId = RequireEffectId("sEffectResistCorprusDisease");
        static readonly short WeaknessToCorprusDiseaseEffectId = RequireEffectId("sEffectWeaknessToCorprusDisease");

        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<MorrowindPendingDamageEvent>();
            systemState.RequireForUpdate<MorrowindCombatRuntimeState>();
            systemState.RequireForUpdate<MorrowindScriptRuntimeState>();
            systemState.RequireForUpdate<ShellMessageBoxRequest>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            var contentBlobReference = SystemAPI.GetSingleton<RuntimeContentBlobReference>();
            if (!contentBlobReference.Blob.IsCreated)
                throw new InvalidOperationException("[VVardenfell][ContentBlob] Disease transfer requires runtime content blob.");
            ref RuntimeContentBlob content = ref contentBlobReference.Blob.Value;

            float transferChance = RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fDiseaseXferChance);
            string contractDiseaseMessage = RuntimeContentBlobUtility.RequireGameSettingStringByIdHash(ref content, RuntimeContentKnownHashes.sMagicContractDisease);

            Entity scriptRuntimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            if (!systemState.EntityManager.HasBuffer<ShellMessageBoxRequest>(scriptRuntimeEntity))
                throw new InvalidOperationException("[VVardenfell][Disease] Script runtime has no ShellMessageBoxRequest buffer.");

            var messageBoxes = systemState.EntityManager.GetBuffer<ShellMessageBoxRequest>(scriptRuntimeEntity);
            ref var combatState = ref SystemAPI.GetSingletonRW<MorrowindCombatRuntimeState>().ValueRW;
            var random = new Unity.Mathematics.Random(combatState.RandomState == 0u ? 0x6E624EB7u : combatState.RandomState);

            foreach (var damage in SystemAPI.Query<RefRO<MorrowindPendingDamageEvent>>())
            {
                if (!IsMeleeContact(damage.ValueRO.SourceKind))
                    continue;

                TryTransferDisease(ref systemState, 
                    ref content,
                    damage.ValueRO.Attacker,
                    damage.ValueRO.Target,
                    transferChance,
                    contractDiseaseMessage,
                    messageBoxes,
                    ref random);
            }

            combatState.RandomState = random.state == 0u ? 0x6E624EB7u : random.state;
        }

        void TryTransferDisease(ref SystemState systemState, 
            ref RuntimeContentBlob content,
            Entity carrier,
            Entity victim,
            float transferChance,
            string contractDiseaseMessage,
            DynamicBuffer<ShellMessageBoxRequest> messageBoxes,
            ref Unity.Mathematics.Random random)
        {
            if (carrier == Entity.Null || !systemState.EntityManager.Exists(carrier))
                throw new InvalidOperationException("[VVardenfell][Disease] Disease carrier entity is missing.");
            if (victim == Entity.Null || !systemState.EntityManager.Exists(victim))
                throw new InvalidOperationException("[VVardenfell][Disease] Disease victim entity is missing.");

            if (systemState.EntityManager.HasComponent<PlayerTag>(carrier))
                return;

            if (!systemState.EntityManager.HasBuffer<ActorKnownSpell>(carrier))
                throw new InvalidOperationException($"[VVardenfell][Disease] Disease carrier ref={PlacedRefId(ref systemState, carrier)} has no ActorKnownSpell buffer.");
            if (!systemState.EntityManager.HasBuffer<ActorKnownSpell>(victim))
                throw new InvalidOperationException($"[VVardenfell][Disease] Disease victim ref={PlacedRefId(ref systemState, victim)} has no ActorKnownSpell buffer.");
            if (!systemState.EntityManager.HasBuffer<ActorActiveMagicEffect>(victim))
                throw new InvalidOperationException($"[VVardenfell][Disease] Disease victim ref={PlacedRefId(ref systemState, victim)} has no ActorActiveMagicEffect buffer.");
            if (!systemState.EntityManager.HasComponent<ActorActiveMagicEffectDirty>(victim))
                throw new InvalidOperationException($"[VVardenfell][Disease] Disease victim ref={PlacedRefId(ref systemState, victim)} has no ActorActiveMagicEffectDirty marker.");

            var carrierSpells = systemState.EntityManager.GetBuffer<ActorKnownSpell>(carrier, true);
            var victimSpells = systemState.EntityManager.GetBuffer<ActorKnownSpell>(victim);
            var victimEffects = systemState.EntityManager.GetBuffer<ActorActiveMagicEffect>(victim, true);
            bool victimIsPlayer = systemState.EntityManager.HasComponent<PlayerTag>(victim);
            bool addedDisease = false;

            for (int i = 0; i < carrierSpells.Length; i++)
            {
                var spellHandle = carrierSpells[i].Spell;
                if (!spellHandle.IsValid || spellHandle.Index < 0 || spellHandle.Index >= content.Spells.Length)
                    throw new InvalidOperationException($"[VVardenfell][Disease] Disease carrier ref={PlacedRefId(ref systemState, carrier)} references invalid spell handle {spellHandle.Value}.");

                if (MorrowindActorMagicUtility.HasKnownSpell(victimSpells, spellHandle))
                    continue;

                ref RuntimeSpellDefBlob spell = ref RuntimeContentBlobUtility.Get(ref content, spellHandle);
                if (!TryResolveDiseaseResistancePair(ref content, ref spell, out short resistEffectId, out short weaknessEffectId))
                    continue;

                float resist = 1f - (0.01f * (ResolveEffectMagnitude(victimEffects, resistEffectId) - ResolveEffectMagnitude(victimEffects, weaknessEffectId)));
                int threshold = (int)(transferChance * 100f * resist);
                if (random.NextInt(10000) >= threshold)
                    continue;

                MorrowindActorMagicUtility.AddKnownSpell(victimSpells, spellHandle);
                addedDisease = true;

                if (victimIsPlayer)
                    ShellDiseaseMessageBoxUtility.QueueContractDiseaseMessage(messageBoxes, contractDiseaseMessage, ref spell);
            }

            if (addedDisease)
                systemState.EntityManager.SetComponentEnabled<ActorActiveMagicEffectDirty>(victim, true);
        }

        static bool TryResolveDiseaseResistancePair(
            ref RuntimeContentBlob content,
            ref RuntimeSpellDefBlob spell,
            out short resistEffectId,
            out short weaknessEffectId)
        {
            if (HasCorprusEffect(ref content, ref spell))
            {
                resistEffectId = ResistCorprusDiseaseEffectId;
                weaknessEffectId = WeaknessToCorprusDiseaseEffectId;
                return true;
            }

            switch (spell.SpellType)
            {
                case MorrowindActorMagicUtility.SpellTypeDisease:
                    resistEffectId = ResistCommonDiseaseEffectId;
                    weaknessEffectId = WeaknessToCommonDiseaseEffectId;
                    return true;
                case MorrowindActorMagicUtility.SpellTypeBlight:
                    resistEffectId = ResistBlightDiseaseEffectId;
                    weaknessEffectId = WeaknessToBlightDiseaseEffectId;
                    return true;
                default:
                    resistEffectId = 0;
                    weaknessEffectId = 0;
                    return false;
            }
        }

        static bool HasCorprusEffect(ref RuntimeContentBlob content, ref RuntimeSpellDefBlob spell)
        {
            MorrowindActorMagicUtility.RequireSpellEffectRange(ref content, ref spell);

            for (int i = 0; i < spell.EffectCount; i++)
            {
                if (content.MagicEffectInstances[spell.EffectStartIndex + i].EffectId == CorprusEffectId)
                    return true;
            }

            return false;
        }

        static float ResolveEffectMagnitude(DynamicBuffer<ActorActiveMagicEffect> effects, short effectId)
        {
            float magnitude = 0f;
            for (int i = 0; i < effects.Length; i++)
            {
                var effect = effects[i];
                if (effect.EffectId != effectId || effect.Applied == 0)
                    continue;
                if (effect.DurationSeconds >= 0f && effect.TimeLeftSeconds <= 0f)
                    continue;

                magnitude += effect.Magnitude;
            }

            return magnitude;
        }

        static bool IsMeleeContact(MorrowindDamageSourceKind sourceKind)
            => sourceKind is MorrowindDamageSourceKind.Weapon or MorrowindDamageSourceKind.HandToHand;

        uint PlacedRefId(ref SystemState systemState, Entity entity)
            => systemState.EntityManager.HasComponent<PlacedRefIdentity>(entity)
                ? systemState.EntityManager.GetComponentData<PlacedRefIdentity>(entity).Value
                : 0u;

        static short RequireEffectId(string gmstId)
        {
            if (!MorrowindMagicEffectTextUtility.TryResolveEffectId(gmstId, out short effectId))
                throw new InvalidOperationException($"[VVardenfell][Disease] Required magic effect GMST '{gmstId}' is not mapped.");
            return effectId;
        }
    }
}
