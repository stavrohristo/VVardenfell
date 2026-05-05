using System;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Combat;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.Magic
{
    static class MorrowindMagicEffectIds
    {
        public static readonly short FireDamage = Require("sEffectFireDamage");
        public static readonly short ShockDamage = Require("sEffectShockDamage");
        public static readonly short FrostDamage = Require("sEffectFrostDamage");
        public static readonly short DrainHealth = Require("sEffectDrainHealth");
        public static readonly short DrainMagicka = Require("sEffectDrainSpellpoints");
        public static readonly short DrainFatigue = Require("sEffectDrainFatigue");
        public static readonly short DamageHealth = Require("sEffectDamageHealth");
        public static readonly short DamageMagicka = Require("sEffectDamageMagicka");
        public static readonly short DamageFatigue = Require("sEffectDamageFatigue");
        public static readonly short Poison = Require("sEffectPoison");
        public static readonly short Invisibility = Require("sEffectInvisibility");
        public static readonly short Chameleon = Require("sEffectChameleon");
        public static readonly short Sanctuary = Require("sEffectSanctuary");
        public static readonly short Paralyze = Require("sEffectParalyze");
        public static readonly short Silence = Require("sEffectSilence");
        public static readonly short Blind = Require("sEffectBlind");
        public static readonly short Sound = Require("sEffectSound");
        public static readonly short Dispel = Require("sEffectDispel");
        public static readonly short SpellAbsorption = Require("sEffectSpellAbsorption");
        public static readonly short Reflect = Require("sEffectReflect");
        public static readonly short CureCommonDisease = Require("sEffectCureCommonDisease");
        public static readonly short CureBlightDisease = Require("sEffectCureBlightDisease");
        public static readonly short CurePoison = Require("sEffectCurePoison");
        public static readonly short CureParalyzation = Require("sEffectCureParalyzation");
        public static readonly short RestoreHealth = Require("sEffectRestoreHealth");
        public static readonly short RestoreMagicka = Require("sEffectRestoreSpellPoints");
        public static readonly short RestoreFatigue = Require("sEffectRestoreFatigue");
        public static readonly short FortifyHealth = Require("sEffectFortifyHealth");
        public static readonly short FortifyMagicka = Require("sEffectFortifySpellpoints");
        public static readonly short FortifyFatigue = Require("sEffectFortifyFatigue");
        public static readonly short FortifyMaximumMagicka = Require("sEffectFortifyMagickaMultiplier");
        public static readonly short AbsorbHealth = Require("sEffectAbsorbHealth");
        public static readonly short AbsorbMagicka = Require("sEffectAbsorbSpellPoints");
        public static readonly short AbsorbFatigue = Require("sEffectAbsorbFatigue");
        public static readonly short ResistFire = Require("sEffectResistFire");
        public static readonly short ResistFrost = Require("sEffectResistFrost");
        public static readonly short ResistShock = Require("sEffectResistShock");
        public static readonly short ResistMagicka = Require("sEffectResistMagicka");
        public static readonly short ResistCommonDisease = Require("sEffectResistCommonDisease");
        public static readonly short ResistBlightDisease = Require("sEffectResistBlightDisease");
        public static readonly short ResistCorprusDisease = Require("sEffectResistCorprusDisease");
        public static readonly short ResistPoison = Require("sEffectResistPoison");
        public static readonly short ResistParalysis = Require("sEffectResistParalysis");
        public static readonly short RemoveCurse = Require("sEffectRemoveCurse");
        public static readonly short Shield = Require("sEffectShield");
        public static readonly short FireShield = Require("sEffectFireShield");
        public static readonly short LightningShield = Require("sEffectLightningShield");
        public static readonly short FrostShield = Require("sEffectFrostShield");
        public static readonly short Burden = Require("sEffectBurden");
        public static readonly short Feather = Require("sEffectFeather");
        public static readonly short Jump = Require("sEffectJump");
        public static readonly short StuntedMagicka = Require("sEffectStuntedMagicka");
        public static readonly short WeaknessToFire = Require("sEffectWeaknessToFire");
        public static readonly short WeaknessToFrost = Require("sEffectWeaknessToFrost");
        public static readonly short WeaknessToShock = Require("sEffectWeaknessToShock");
        public static readonly short WeaknessToMagicka = Require("sEffectWeaknessToMagicka");
        public static readonly short WeaknessToCommonDisease = Require("sEffectWeaknessToCommonDisease");
        public static readonly short WeaknessToBlightDisease = Require("sEffectWeaknessToBlightDisease");
        public static readonly short WeaknessToCorprusDisease = Require("sEffectWeaknessToCorprusDisease");
        public static readonly short WeaknessToPoison = Require("sEffectWeaknessToPoison");

        static short Require(string gmstId)
        {
            if (!MorrowindMagicEffectTextUtility.TryResolveEffectId(gmstId, out short effectId))
                throw new InvalidOperationException($"[VVardenfell][Magic] Required magic effect GMST '{gmstId}' is not mapped.");
            return effectId;
        }
    }

    static class MorrowindMagicEffectApplicationUtility
    {
        public static bool IsSupported(short effectId)
            => IsVitalEffect(effectId)
               || IsModifierOnlyEffect(effectId)
               || IsPurgeEffect(effectId);

        public static bool ApplyOrTick(
            EntityManager entityManager,
            ref RuntimeContentBlob content,
            Entity target,
            DynamicBuffer<ActorActiveSpell> activeSpells,
            DynamicBuffer<ActorActiveMagicEffect> effects,
            int index,
            float deltaSeconds)
        {
            var effect = effects[index];
            if (!IsSupported(effect.EffectId))
                throw new InvalidOperationException($"[VVardenfell][Magic] Unsupported active magic effect {effect.EffectId} from '{effect.SourceId}'.");

            bool changed = false;
            if (effect.Applied == 0)
            {
                ApplyOnce(entityManager, ref content, target, activeSpells, effects, index, ref effect);
                effect.Applied = 1;
                changed = true;
            }

            if (effect.DurationSeconds >= 0f)
            {
                float tickSeconds = math.min(math.max(0f, deltaSeconds), math.max(0f, effect.TimeLeftSeconds));
                if (tickSeconds > 0f && AppliesContinuously(effect.EffectId))
                {
                    ApplyVitalDelta(entityManager, target, ResolveCaster(entityManager, activeSpells, effect.ActiveSpellId), effect.EffectId, effect.Magnitude * tickSeconds);
                    changed = true;
                }

                effect.TimeLeftSeconds -= tickSeconds;
                if (effect.TimeLeftSeconds <= 0f)
                    effect.Remove = 1;
                changed = true;
            }

            effects[index] = effect;
            return changed;
        }

        public static float SumEffectMagnitude(DynamicBuffer<ActorActiveMagicEffect> effects, short effectId)
        {
            float magnitude = 0f;
            for (int i = 0; i < effects.Length; i++)
            {
                var effect = effects[i];
                if (effect.EffectId != effectId || effect.Applied == 0 || effect.Remove != 0)
                    continue;
                if (effect.DurationSeconds >= 0f && effect.TimeLeftSeconds <= 0f)
                    continue;
                magnitude += effect.Magnitude;
            }

            return magnitude;
        }

        public static short ResolveResistanceEffect(short effectId)
        {
            if (effectId == MorrowindMagicEffectIds.FireDamage)
                return MorrowindMagicEffectIds.ResistFire;
            if (effectId == MorrowindMagicEffectIds.FrostDamage)
                return MorrowindMagicEffectIds.ResistFrost;
            if (effectId == MorrowindMagicEffectIds.ShockDamage)
                return MorrowindMagicEffectIds.ResistShock;
            if (effectId == MorrowindMagicEffectIds.Poison)
                return MorrowindMagicEffectIds.ResistPoison;
            if (effectId == MorrowindMagicEffectIds.Paralyze)
                return MorrowindMagicEffectIds.ResistParalysis;
            return MorrowindMagicEffectIds.ResistMagicka;
        }

        public static short ResolveWeaknessEffect(short effectId)
        {
            if (effectId == MorrowindMagicEffectIds.FireDamage)
                return MorrowindMagicEffectIds.WeaknessToFire;
            if (effectId == MorrowindMagicEffectIds.FrostDamage)
                return MorrowindMagicEffectIds.WeaknessToFrost;
            if (effectId == MorrowindMagicEffectIds.ShockDamage)
                return MorrowindMagicEffectIds.WeaknessToShock;
            if (effectId == MorrowindMagicEffectIds.Poison)
                return MorrowindMagicEffectIds.WeaknessToPoison;
            return MorrowindMagicEffectIds.WeaknessToMagicka;
        }

        static void ApplyOnce(
            EntityManager entityManager,
            ref RuntimeContentBlob content,
            Entity target,
            DynamicBuffer<ActorActiveSpell> activeSpells,
            DynamicBuffer<ActorActiveMagicEffect> effects,
            int index,
            ref ActorActiveMagicEffect effect)
        {
            if (IsPurgeEffect(effect.EffectId))
            {
                ApplyPurge(ref content, effects, index, effect.EffectId);
                return;
            }

            if (!AppliesContinuously(effect.EffectId) && IsVitalEffect(effect.EffectId))
                ApplyVitalDelta(entityManager, target, ResolveCaster(entityManager, activeSpells, effect.ActiveSpellId), effect.EffectId, effect.Magnitude);
        }

        static void ApplyPurge(ref RuntimeContentBlob content, DynamicBuffer<ActorActiveMagicEffect> effects, int selfIndex, short effectId)
        {
            for (int i = effects.Length - 1; i >= 0; i--)
            {
                if (i == selfIndex)
                    continue;

                var active = effects[i];
                if (effectId == MorrowindMagicEffectIds.CurePoison && active.EffectId == MorrowindMagicEffectIds.Poison)
                    effects.RemoveAt(i);
                else if (effectId == MorrowindMagicEffectIds.CureParalyzation && active.EffectId == MorrowindMagicEffectIds.Paralyze)
                    effects.RemoveAt(i);
                else if (effectId == MorrowindMagicEffectIds.CureCommonDisease && IsSourceSpellType(ref content, active, MorrowindActorMagicUtility.SpellTypeDisease))
                    effects.RemoveAt(i);
                else if (effectId == MorrowindMagicEffectIds.CureBlightDisease && IsSourceSpellType(ref content, active, MorrowindActorMagicUtility.SpellTypeBlight))
                    effects.RemoveAt(i);
                else if (effectId == MorrowindMagicEffectIds.RemoveCurse && IsSourceSpellType(ref content, active, MorrowindActorMagicUtility.SpellTypeCurse))
                    effects.RemoveAt(i);
                else if (effectId == MorrowindMagicEffectIds.Dispel && active.SourceKind != ActorActiveMagicEffectSourceKind.PassiveSpell)
                    effects.RemoveAt(i);
            }
        }

        static bool IsSourceSpellType(ref RuntimeContentBlob content, in ActorActiveMagicEffect active, int spellType)
        {
            if (active.SourceIdHash == 0UL
                || !RuntimeContentBlobUtility.TryGetSpellHandleByIdHash(ref content, active.SourceIdHash, out var spellHandle)
                || !spellHandle.IsValid)
            {
                return false;
            }

            ref RuntimeSpellDefBlob spell = ref RuntimeContentBlobUtility.Get(ref content, spellHandle);
            return spell.SpellType == spellType;
        }

        static Entity ResolveCaster(EntityManager entityManager, DynamicBuffer<ActorActiveSpell> activeSpells, int activeSpellId)
        {
            for (int i = 0; i < activeSpells.Length; i++)
            {
                if (activeSpells[i].ActiveSpellId == activeSpellId && activeSpells[i].CasterEntity != Entity.Null && entityManager.Exists(activeSpells[i].CasterEntity))
                    return activeSpells[i].CasterEntity;
            }

            return Entity.Null;
        }

        static void ApplyVitalDelta(EntityManager entityManager, Entity target, Entity caster, short effectId, float magnitude)
        {
            if (!entityManager.HasComponent<ActorVitalSet>(target))
                throw new InvalidOperationException($"[VVardenfell][Magic] Vital magic target entity={target.Index}:{target.Version} has no ActorVitalSet.");

            var vitals = entityManager.GetComponentData<ActorVitalSet>(target);
            if (effectId == MorrowindMagicEffectIds.RestoreHealth || effectId == MorrowindMagicEffectIds.FortifyHealth)
                vitals.CurrentHealth += magnitude;
            else if (effectId == MorrowindMagicEffectIds.RestoreMagicka || effectId == MorrowindMagicEffectIds.FortifyMagicka)
                vitals.CurrentMagicka += magnitude;
            else if (effectId == MorrowindMagicEffectIds.RestoreFatigue || effectId == MorrowindMagicEffectIds.FortifyFatigue)
                vitals.CurrentFatigue += magnitude;
            else if (effectId == MorrowindMagicEffectIds.AbsorbHealth || effectId == MorrowindMagicEffectIds.DamageHealth || effectId == MorrowindMagicEffectIds.DrainHealth || effectId == MorrowindMagicEffectIds.FireDamage || effectId == MorrowindMagicEffectIds.FrostDamage || effectId == MorrowindMagicEffectIds.ShockDamage || effectId == MorrowindMagicEffectIds.Poison)
                vitals.CurrentHealth -= magnitude;
            else if (effectId == MorrowindMagicEffectIds.AbsorbMagicka || effectId == MorrowindMagicEffectIds.DamageMagicka || effectId == MorrowindMagicEffectIds.DrainMagicka)
                vitals.CurrentMagicka -= magnitude;
            else if (effectId == MorrowindMagicEffectIds.AbsorbFatigue || effectId == MorrowindMagicEffectIds.DamageFatigue || effectId == MorrowindMagicEffectIds.DrainFatigue)
                vitals.CurrentFatigue -= magnitude;

            entityManager.SetComponentData(target, vitals);
            if (IsAbsorbEffect(effectId) && caster != Entity.Null && entityManager.Exists(caster) && entityManager.HasComponent<ActorVitalSet>(caster))
                ApplyAbsorbReturn(entityManager, caster, effectId, magnitude);
            if (vitals.CurrentHealth <= 0f)
            {
                var aftermath = ActorHitAftermathStateUtility.Require(entityManager, target, $"[VVardenfell][Magic] Vital magic target entity={target.Index}:{target.Version}");
                ActorHitAftermathStateUtility.MarkDead(ref aftermath);
                entityManager.SetComponentData(target, aftermath);
            }
        }

        static void ApplyAbsorbReturn(EntityManager entityManager, Entity caster, short effectId, float magnitude)
        {
            var vitals = entityManager.GetComponentData<ActorVitalSet>(caster);
            if (effectId == MorrowindMagicEffectIds.AbsorbHealth)
                vitals.CurrentHealth += magnitude;
            else if (effectId == MorrowindMagicEffectIds.AbsorbMagicka)
                vitals.CurrentMagicka += magnitude;
            else if (effectId == MorrowindMagicEffectIds.AbsorbFatigue)
                vitals.CurrentFatigue += magnitude;
            entityManager.SetComponentData(caster, vitals);
        }

        static bool IsAbsorbEffect(short effectId)
            => effectId == MorrowindMagicEffectIds.AbsorbHealth
               || effectId == MorrowindMagicEffectIds.AbsorbMagicka
               || effectId == MorrowindMagicEffectIds.AbsorbFatigue;

        static bool IsVitalEffect(short effectId)
            => effectId == MorrowindMagicEffectIds.FireDamage
               || effectId == MorrowindMagicEffectIds.ShockDamage
               || effectId == MorrowindMagicEffectIds.FrostDamage
               || effectId == MorrowindMagicEffectIds.Poison
               || effectId == MorrowindMagicEffectIds.DrainHealth
               || effectId == MorrowindMagicEffectIds.DrainMagicka
               || effectId == MorrowindMagicEffectIds.DrainFatigue
               || effectId == MorrowindMagicEffectIds.DamageHealth
               || effectId == MorrowindMagicEffectIds.DamageMagicka
               || effectId == MorrowindMagicEffectIds.DamageFatigue
               || effectId == MorrowindMagicEffectIds.RestoreHealth
               || effectId == MorrowindMagicEffectIds.RestoreMagicka
               || effectId == MorrowindMagicEffectIds.RestoreFatigue
               || effectId == MorrowindMagicEffectIds.FortifyHealth
               || effectId == MorrowindMagicEffectIds.FortifyMagicka
               || effectId == MorrowindMagicEffectIds.FortifyFatigue
               || effectId == MorrowindMagicEffectIds.AbsorbHealth
               || effectId == MorrowindMagicEffectIds.AbsorbMagicka
               || effectId == MorrowindMagicEffectIds.AbsorbFatigue;

        static bool IsModifierOnlyEffect(short effectId)
            => effectId == MorrowindMagicEffectIds.Paralyze
               || effectId == MorrowindMagicEffectIds.Silence
               || effectId == MorrowindMagicEffectIds.Blind
               || effectId == MorrowindMagicEffectIds.Sound
               || effectId == MorrowindMagicEffectIds.Sanctuary
               || effectId == MorrowindMagicEffectIds.Chameleon
               || effectId == MorrowindMagicEffectIds.Invisibility
               || effectId == MorrowindMagicEffectIds.Shield
               || effectId == MorrowindMagicEffectIds.FireShield
               || effectId == MorrowindMagicEffectIds.FrostShield
               || effectId == MorrowindMagicEffectIds.LightningShield
               || effectId == MorrowindMagicEffectIds.FortifyMaximumMagicka
               || effectId == MorrowindMagicEffectIds.Burden
               || effectId == MorrowindMagicEffectIds.Feather
               || effectId == MorrowindMagicEffectIds.Jump
               || effectId == MorrowindMagicEffectIds.StuntedMagicka
               || effectId == MorrowindMagicEffectIds.Reflect
               || effectId == MorrowindMagicEffectIds.SpellAbsorption
               || effectId == MorrowindMagicEffectIds.ResistFire
               || effectId == MorrowindMagicEffectIds.ResistFrost
               || effectId == MorrowindMagicEffectIds.ResistShock
               || effectId == MorrowindMagicEffectIds.ResistMagicka
               || effectId == MorrowindMagicEffectIds.ResistCommonDisease
               || effectId == MorrowindMagicEffectIds.ResistBlightDisease
               || effectId == MorrowindMagicEffectIds.ResistCorprusDisease
               || effectId == MorrowindMagicEffectIds.ResistPoison
               || effectId == MorrowindMagicEffectIds.ResistParalysis
               || effectId == MorrowindMagicEffectIds.WeaknessToFire
               || effectId == MorrowindMagicEffectIds.WeaknessToFrost
               || effectId == MorrowindMagicEffectIds.WeaknessToShock
               || effectId == MorrowindMagicEffectIds.WeaknessToMagicka
               || effectId == MorrowindMagicEffectIds.WeaknessToCommonDisease
               || effectId == MorrowindMagicEffectIds.WeaknessToBlightDisease
               || effectId == MorrowindMagicEffectIds.WeaknessToCorprusDisease
               || effectId == MorrowindMagicEffectIds.WeaknessToPoison;

        static bool IsPurgeEffect(short effectId)
            => effectId == MorrowindMagicEffectIds.CureCommonDisease
               || effectId == MorrowindMagicEffectIds.CureBlightDisease
               || effectId == MorrowindMagicEffectIds.CurePoison
               || effectId == MorrowindMagicEffectIds.CureParalyzation
               || effectId == MorrowindMagicEffectIds.Dispel
               || effectId == MorrowindMagicEffectIds.RemoveCurse;

        static bool AppliesContinuously(short effectId)
            => effectId == MorrowindMagicEffectIds.FireDamage
               || effectId == MorrowindMagicEffectIds.ShockDamage
               || effectId == MorrowindMagicEffectIds.FrostDamage
               || effectId == MorrowindMagicEffectIds.Poison
               || effectId == MorrowindMagicEffectIds.DamageHealth
               || effectId == MorrowindMagicEffectIds.DamageMagicka
               || effectId == MorrowindMagicEffectIds.DamageFatigue
               || effectId == MorrowindMagicEffectIds.RestoreHealth
               || effectId == MorrowindMagicEffectIds.RestoreMagicka
               || effectId == MorrowindMagicEffectIds.RestoreFatigue
               || effectId == MorrowindMagicEffectIds.AbsorbHealth
               || effectId == MorrowindMagicEffectIds.AbsorbMagicka
               || effectId == MorrowindMagicEffectIds.AbsorbFatigue;
    }
}
