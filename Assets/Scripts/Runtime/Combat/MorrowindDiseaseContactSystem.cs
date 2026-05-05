using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
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
    public partial class MorrowindDiseaseContactSystem : SystemBase
    {
        static readonly short CorprusEffectId = RequireEffectId("sEffectCorpus");
        static readonly short ResistCommonDiseaseEffectId = RequireEffectId("sEffectResistCommonDisease");
        static readonly short WeaknessToCommonDiseaseEffectId = RequireEffectId("sEffectWeaknessToCommonDisease");
        static readonly short ResistBlightDiseaseEffectId = RequireEffectId("sEffectResistBlightDisease");
        static readonly short WeaknessToBlightDiseaseEffectId = RequireEffectId("sEffectWeaknessToBlightDisease");
        static readonly short ResistCorprusDiseaseEffectId = RequireEffectId("sEffectResistCorprusDisease");
        static readonly short WeaknessToCorprusDiseaseEffectId = RequireEffectId("sEffectWeaknessToCorprusDisease");

        protected override void OnCreate()
        {
            RequireForUpdate<MorrowindPendingDamageEvent>();
            RequireForUpdate<MorrowindCombatRuntimeState>();
            RequireForUpdate<MorrowindScriptRuntimeState>();
            RequireForUpdate<ShellMessageBoxRequest>();
        }

        protected override void OnUpdate()
        {
            RuntimeContentDatabase contentDb = RuntimeContentDatabase.Active
                ?? throw new InvalidOperationException("[VVardenfell][Disease] Runtime content database is not loaded.");
            if (contentDb.Data.Spells == null)
                throw new InvalidOperationException("[VVardenfell][Disease] Runtime content has no spell table.");
            if (contentDb.Data.MagicEffectInstances == null)
                throw new InvalidOperationException("[VVardenfell][Disease] Runtime content has no spell effect instance table.");

            float transferChance = contentDb.RequireGameSettingFloat("fDiseaseXferChance");
            string contractDiseaseMessage = contentDb.RequireGameSettingString("sMagicContractDisease");

            Entity scriptRuntimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            if (!EntityManager.HasBuffer<ShellMessageBoxRequest>(scriptRuntimeEntity))
                throw new InvalidOperationException("[VVardenfell][Disease] Script runtime has no ShellMessageBoxRequest buffer.");

            var messageBoxes = EntityManager.GetBuffer<ShellMessageBoxRequest>(scriptRuntimeEntity);
            ref var combatState = ref SystemAPI.GetSingletonRW<MorrowindCombatRuntimeState>().ValueRW;
            var random = new Unity.Mathematics.Random(combatState.RandomState == 0u ? 0x6E624EB7u : combatState.RandomState);

            foreach (var damage in SystemAPI.Query<RefRO<MorrowindPendingDamageEvent>>())
            {
                if (!IsMeleeContact(damage.ValueRO.SourceKind))
                    continue;

                TryTransferDisease(
                    contentDb,
                    damage.ValueRO.Attacker,
                    damage.ValueRO.Target,
                    transferChance,
                    contractDiseaseMessage,
                    messageBoxes,
                    ref random);
            }

            combatState.RandomState = random.state == 0u ? 0x6E624EB7u : random.state;
        }

        void TryTransferDisease(
            RuntimeContentDatabase contentDb,
            Entity carrier,
            Entity victim,
            float transferChance,
            string contractDiseaseMessage,
            DynamicBuffer<ShellMessageBoxRequest> messageBoxes,
            ref Unity.Mathematics.Random random)
        {
            if (carrier == Entity.Null || !EntityManager.Exists(carrier))
                throw new InvalidOperationException("[VVardenfell][Disease] Disease carrier entity is missing.");
            if (victim == Entity.Null || !EntityManager.Exists(victim))
                throw new InvalidOperationException("[VVardenfell][Disease] Disease victim entity is missing.");

            if (EntityManager.HasComponent<PlayerTag>(carrier))
                return;

            if (!EntityManager.HasBuffer<ActorKnownSpell>(carrier))
                throw new InvalidOperationException($"[VVardenfell][Disease] Disease carrier ref={PlacedRefId(carrier)} has no ActorKnownSpell buffer.");
            if (!EntityManager.HasBuffer<ActorKnownSpell>(victim))
                throw new InvalidOperationException($"[VVardenfell][Disease] Disease victim ref={PlacedRefId(victim)} has no ActorKnownSpell buffer.");
            if (!EntityManager.HasBuffer<ActorActiveMagicEffect>(victim))
                throw new InvalidOperationException($"[VVardenfell][Disease] Disease victim ref={PlacedRefId(victim)} has no ActorActiveMagicEffect buffer.");
            if (!EntityManager.HasComponent<ActorActiveMagicEffectDirty>(victim))
                throw new InvalidOperationException($"[VVardenfell][Disease] Disease victim ref={PlacedRefId(victim)} has no ActorActiveMagicEffectDirty marker.");

            var carrierSpells = EntityManager.GetBuffer<ActorKnownSpell>(carrier, true);
            var victimSpells = EntityManager.GetBuffer<ActorKnownSpell>(victim);
            var victimEffects = EntityManager.GetBuffer<ActorActiveMagicEffect>(victim, true);
            bool victimIsPlayer = EntityManager.HasComponent<PlayerTag>(victim);
            bool addedDisease = false;

            for (int i = 0; i < carrierSpells.Length; i++)
            {
                var spellHandle = carrierSpells[i].Spell;
                if (!spellHandle.IsValid || spellHandle.Index < 0 || spellHandle.Index >= contentDb.Data.Spells.Length)
                    throw new InvalidOperationException($"[VVardenfell][Disease] Disease carrier ref={PlacedRefId(carrier)} references invalid spell handle {spellHandle.Value}.");

                if (MorrowindActorMagicUtility.HasKnownSpell(victimSpells, spellHandle))
                    continue;

                ref readonly var spell = ref contentDb.Get(spellHandle);
                if (!TryResolveDiseaseResistancePair(contentDb, spell, out short resistEffectId, out short weaknessEffectId))
                    continue;

                float resist = 1f - (0.01f * (ResolveEffectMagnitude(victimEffects, resistEffectId) - ResolveEffectMagnitude(victimEffects, weaknessEffectId)));
                int threshold = (int)(transferChance * 100f * resist);
                if (random.NextInt(10000) >= threshold)
                    continue;

                MorrowindActorMagicUtility.AddKnownSpell(victimSpells, spellHandle);
                addedDisease = true;

                if (victimIsPlayer)
                    QueueContractDiseaseMessage(messageBoxes, contractDiseaseMessage, spell);
            }

            if (addedDisease)
                EntityManager.SetComponentEnabled<ActorActiveMagicEffectDirty>(victim, true);
        }

        static bool TryResolveDiseaseResistancePair(
            RuntimeContentDatabase contentDb,
            in SpellDef spell,
            out short resistEffectId,
            out short weaknessEffectId)
        {
            if (HasCorprusEffect(contentDb, spell))
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

        static bool HasCorprusEffect(RuntimeContentDatabase contentDb, in SpellDef spell)
        {
            if (!MorrowindActorMagicUtility.TryGetSpellEffects(contentDb, spell, out var effects))
                return false;

            for (int i = 0; i < effects.Length; i++)
            {
                if (effects[i].EffectId == CorprusEffectId)
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

        static void QueueContractDiseaseMessage(
            DynamicBuffer<ShellMessageBoxRequest> messageBoxes,
            string contractDiseaseMessage,
            in SpellDef spell)
        {
            string spellName = string.IsNullOrWhiteSpace(spell.Name) ? spell.Id : spell.Name.Trim();
            messageBoxes.Add(new ShellMessageBoxRequest
            {
                Body = RuntimeFixedStringUtility.ToFixed512OrDefault(FormatDiseaseMessage(contractDiseaseMessage, spellName)),
            });
        }

        static string FormatDiseaseMessage(string message, string spellName)
        {
            if (string.IsNullOrEmpty(message))
                return spellName;

            int placeholder = message.IndexOf("%s", StringComparison.Ordinal);
            return placeholder >= 0
                ? message.Substring(0, placeholder) + spellName + message.Substring(placeholder + 2)
                : message + " " + spellName;
        }

        static bool IsMeleeContact(MorrowindDamageSourceKind sourceKind)
            => sourceKind is MorrowindDamageSourceKind.Weapon or MorrowindDamageSourceKind.HandToHand;

        uint PlacedRefId(Entity entity)
            => EntityManager.HasComponent<PlacedRefIdentity>(entity)
                ? EntityManager.GetComponentData<PlacedRefIdentity>(entity).Value
                : 0u;

        static short RequireEffectId(string gmstId)
        {
            if (!MorrowindMagicEffectTextUtility.TryResolveEffectId(gmstId, out short effectId))
                throw new InvalidOperationException($"[VVardenfell][Disease] Required magic effect GMST '{gmstId}' is not mapped.");
            return effectId;
        }
    }
}
