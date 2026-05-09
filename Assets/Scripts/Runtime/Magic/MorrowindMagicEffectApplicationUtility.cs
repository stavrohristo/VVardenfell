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
        public static readonly short WaterBreathing = Require("sEffectWaterBreathing");
        public static readonly short SwiftSwim = Require("sEffectSwiftSwim");
        public static readonly short WaterWalking = Require("sEffectWaterWalking");
        public static readonly short ShockDamage = Require("sEffectShockDamage");
        public static readonly short FrostDamage = Require("sEffectFrostDamage");
        public static readonly short SunDamage = Require("sEffectSunDamage");
        public static readonly short DrainAttribute = Require("sEffectDrainAttribute");
        public static readonly short DrainHealth = Require("sEffectDrainHealth");
        public static readonly short DrainMagicka = Require("sEffectDrainSpellpoints");
        public static readonly short DrainFatigue = Require("sEffectDrainFatigue");
        public static readonly short DrainSkill = Require("sEffectDrainSkill");
        public static readonly short DamageAttribute = Require("sEffectDamageAttribute");
        public static readonly short DamageHealth = Require("sEffectDamageHealth");
        public static readonly short DamageMagicka = Require("sEffectDamageMagicka");
        public static readonly short DamageFatigue = Require("sEffectDamageFatigue");
        public static readonly short DamageSkill = Require("sEffectDamageSkill");
        public static readonly short Poison = Require("sEffectPoison");
        public static readonly short Invisibility = Require("sEffectInvisibility");
        public static readonly short Chameleon = Require("sEffectChameleon");
        public static readonly short Light = Require("sEffectLight");
        public static readonly short NightEye = Require("sEffectNightEye");
        public static readonly short Sanctuary = Require("sEffectSanctuary");
        public static readonly short Charm = Require("sEffectCharm");
        public static readonly short Paralyze = Require("sEffectParalyze");
        public static readonly short Silence = Require("sEffectSilence");
        public static readonly short Blind = Require("sEffectBlind");
        public static readonly short Sound = Require("sEffectSound");
        public static readonly short CalmHumanoid = Require("sEffectCalmHumanoid");
        public static readonly short CalmCreature = Require("sEffectCalmCreature");
        public static readonly short FrenzyHumanoid = Require("sEffectFrenzyHumanoid");
        public static readonly short FrenzyCreature = Require("sEffectFrenzyCreature");
        public static readonly short DemoralizeHumanoid = Require("sEffectDemoralizeHumanoid");
        public static readonly short DemoralizeCreature = Require("sEffectDemoralizeCreature");
        public static readonly short RallyHumanoid = Require("sEffectRallyHumanoid");
        public static readonly short RallyCreature = Require("sEffectRallyCreature");
        public static readonly short Dispel = Require("sEffectDispel");
        public static readonly short Telekinesis = Require("sEffectTelekinesis");
        public static readonly short DetectAnimal = Require("sEffectDetectAnimal");
        public static readonly short DetectEnchantment = Require("sEffectDetectEnchantment");
        public static readonly short DetectKey = Require("sEffectDetectKey");
        public static readonly short SpellAbsorption = Require("sEffectSpellAbsorption");
        public static readonly short Reflect = Require("sEffectReflect");
        public static readonly short CureCommonDisease = Require("sEffectCureCommonDisease");
        public static readonly short CureBlightDisease = Require("sEffectCureBlightDisease");
        public static readonly short CureCorprusDisease = Require("sEffectCureCorprusDisease");
        public static readonly short CurePoison = Require("sEffectCurePoison");
        public static readonly short CureParalyzation = Require("sEffectCureParalyzation");
        public static readonly short RestoreAttribute = Require("sEffectRestoreAttribute");
        public static readonly short RestoreHealth = Require("sEffectRestoreHealth");
        public static readonly short RestoreMagicka = Require("sEffectRestoreSpellPoints");
        public static readonly short RestoreFatigue = Require("sEffectRestoreFatigue");
        public static readonly short RestoreSkill = Require("sEffectRestoreSkill");
        public static readonly short FortifyAttribute = Require("sEffectFortifyAttribute");
        public static readonly short FortifyHealth = Require("sEffectFortifyHealth");
        public static readonly short FortifyMagicka = Require("sEffectFortifySpellpoints");
        public static readonly short FortifyFatigue = Require("sEffectFortifyFatigue");
        public static readonly short FortifySkill = Require("sEffectFortifySkill");
        public static readonly short FortifyMaximumMagicka = Require("sEffectFortifyMagickaMultiplier");
        public static readonly short AbsorbAttribute = Require("sEffectAbsorbAttribute");
        public static readonly short AbsorbHealth = Require("sEffectAbsorbHealth");
        public static readonly short AbsorbMagicka = Require("sEffectAbsorbSpellPoints");
        public static readonly short AbsorbFatigue = Require("sEffectAbsorbFatigue");
        public static readonly short AbsorbSkill = Require("sEffectAbsorbSkill");
        public static readonly short ResistFire = Require("sEffectResistFire");
        public static readonly short ResistFrost = Require("sEffectResistFrost");
        public static readonly short ResistShock = Require("sEffectResistShock");
        public static readonly short ResistMagicka = Require("sEffectResistMagicka");
        public static readonly short ResistCommonDisease = Require("sEffectResistCommonDisease");
        public static readonly short ResistBlightDisease = Require("sEffectResistBlightDisease");
        public static readonly short ResistCorprusDisease = Require("sEffectResistCorprusDisease");
        public static readonly short ResistPoison = Require("sEffectResistPoison");
        public static readonly short ResistNormalWeapons = Require("sEffectResistNormalWeapons");
        public static readonly short ResistParalysis = Require("sEffectResistParalysis");
        public static readonly short RemoveCurse = Require("sEffectRemoveCurse");
        public static readonly short TurnUndead = Require("sEffectTurnUndead");
        public static readonly short CommandCreature = Require("sEffectCommandCreatures");
        public static readonly short CommandHumanoid = Require("sEffectCommandHumanoids");
        public static readonly short Shield = Require("sEffectShield");
        public static readonly short FireShield = Require("sEffectFireShield");
        public static readonly short LightningShield = Require("sEffectLightningShield");
        public static readonly short FrostShield = Require("sEffectFrostShield");
        public static readonly short FortifyAttack = Require("sEffectFortifyAttackBonus");
        public static readonly short Burden = Require("sEffectBurden");
        public static readonly short Feather = Require("sEffectFeather");
        public static readonly short Jump = Require("sEffectJump");
        public static readonly short Levitate = Require("sEffectLevitate");
        public static readonly short SlowFall = Require("sEffectSlowFall");
        public static readonly short StuntedMagicka = Require("sEffectStuntedMagicka");
        public static readonly short Corprus = Require("sEffectCorpus");
        public static readonly short Vampirism = Require("sEffectVampirism");
        public static readonly short WeaknessToFire = Require("sEffectWeaknessToFire");
        public static readonly short WeaknessToFrost = Require("sEffectWeaknessToFrost");
        public static readonly short WeaknessToShock = Require("sEffectWeaknessToShock");
        public static readonly short WeaknessToMagicka = Require("sEffectWeaknessToMagicka");
        public static readonly short WeaknessToCommonDisease = Require("sEffectWeaknessToCommonDisease");
        public static readonly short WeaknessToBlightDisease = Require("sEffectWeaknessToBlightDisease");
        public static readonly short WeaknessToCorprusDisease = Require("sEffectWeaknessToCorprusDisease");
        public static readonly short WeaknessToPoison = Require("sEffectWeaknessToPoison");
        public static readonly short WeaknessToNormalWeapons = Require("sEffectWeaknessToNormalWeapons");
        public static readonly short Lock = Require("sEffectLock");
        public static readonly short Open = Require("sEffectOpen");
        public static readonly short Mark = Require("sEffectMark");
        public static readonly short Recall = Require("sEffectRecall");
        public static readonly short DivineIntervention = Require("sEffectDivineIntervention");
        public static readonly short AlmsiviIntervention = Require("sEffectAlmsiviIntervention");
        public static readonly short Soultrap = Require("sEffectSoultrap");
        public static readonly short DisintegrateWeapon = Require("sEffectDisintegrateWeapon");
        public static readonly short DisintegrateArmor = Require("sEffectDisintegrateArmor");
        public static readonly short ExtraSpell = Require("sEffectExtraSpell");

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
               || IsAttributeEffect(effectId)
               || IsSkillEffect(effectId)
               || IsModifierOnlyEffect(effectId)
               || IsPurgeEffect(effectId)
               || IsAdjacentSystemEffect(effectId);

        public static bool ApplyOrTick(
            EntityManager entityManager,
            ref RuntimeContentBlob content,
            Entity target,
            DynamicBuffer<ActorActiveSpell> activeSpells,
            DynamicBuffer<ActorKnownSpell> knownSpells,
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
                ApplyOnce(entityManager, ref content, target, activeSpells, knownSpells, effects, index, ref effect);
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

        public static float ResolveResistanceAttribute(DynamicBuffer<ActorActiveMagicEffect> effects, short effectId)
        {
            float resistance = SumEffectMagnitude(effects, ResolveResistanceEffect(effectId));
            resistance -= SumEffectMagnitude(effects, ResolveWeaknessEffect(effectId));
            if (effectId == MorrowindMagicEffectIds.FireDamage)
                resistance += SumEffectMagnitude(effects, MorrowindMagicEffectIds.FireShield);
            else if (effectId == MorrowindMagicEffectIds.ShockDamage)
                resistance += SumEffectMagnitude(effects, MorrowindMagicEffectIds.LightningShield);
            else if (effectId == MorrowindMagicEffectIds.FrostDamage)
                resistance += SumEffectMagnitude(effects, MorrowindMagicEffectIds.FrostShield);
            return resistance;
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
            DynamicBuffer<ActorKnownSpell> knownSpells,
            DynamicBuffer<ActorActiveMagicEffect> effects,
            int index,
            ref ActorActiveMagicEffect effect)
        {
            if (IsPurgeEffect(effect.EffectId))
            {
                ApplyPurge(ref content, activeSpells, knownSpells, effects, index, effect.EffectId);
                return;
            }

            if (IsAdjacentSystemEffect(effect.EffectId))
                throw new InvalidOperationException($"[VVardenfell][Magic] Effect {effect.EffectId} from '{effect.SourceId}' requires an adjacent runtime system that is not wired into active spell application yet.");

            if (IsAttributeAppliedOnceEffect(effect.EffectId))
            {
                ApplyAttributeDamageDelta(entityManager, target, effect.EffectId, effect.Attribute, effect.Magnitude);
                return;
            }

            if (IsSkillAppliedOnceEffect(effect.EffectId))
            {
                ApplySkillDamageDelta(entityManager, target, effect.EffectId, effect.Skill, effect.Magnitude);
                return;
            }

            if (IsVitalAppliedOnceEffect(effect.EffectId))
                ApplyVitalDelta(entityManager, target, ResolveCaster(entityManager, activeSpells, effect.ActiveSpellId), effect.EffectId, effect.Magnitude);
        }

        static void ApplyPurge(
            ref RuntimeContentBlob content,
            DynamicBuffer<ActorActiveSpell> activeSpells,
            DynamicBuffer<ActorKnownSpell> knownSpells,
            DynamicBuffer<ActorActiveMagicEffect> effects,
            int selfIndex,
            short effectId)
        {
            for (int i = effects.Length - 1; i >= 0; i--)
            {
                if (i == selfIndex)
                    continue;

                var active = effects[i];
                if (effectId == MorrowindMagicEffectIds.CurePoison && active.EffectId == MorrowindMagicEffectIds.Poison)
                    RemoveWholeActiveSpell(activeSpells, effects, active.ActiveSpellId, i);
                else if (effectId == MorrowindMagicEffectIds.CureParalyzation && active.EffectId == MorrowindMagicEffectIds.Paralyze)
                    RemoveWholeActiveSpell(activeSpells, effects, active.ActiveSpellId, i);
                else if (effectId == MorrowindMagicEffectIds.CureCommonDisease && IsSourceSpellType(ref content, active, MorrowindActorMagicUtility.SpellTypeDisease))
                    RemoveKnownAndActiveSource(ref content, activeSpells, knownSpells, effects, active.SourceIdHash, i);
                else if (effectId == MorrowindMagicEffectIds.CureBlightDisease && IsSourceSpellType(ref content, active, MorrowindActorMagicUtility.SpellTypeBlight))
                    RemoveKnownAndActiveSource(ref content, activeSpells, knownSpells, effects, active.SourceIdHash, i);
                else if (effectId == MorrowindMagicEffectIds.CureCorprusDisease && active.EffectId == MorrowindMagicEffectIds.Corprus)
                    RemoveWholeActiveSpell(activeSpells, effects, active.ActiveSpellId, i);
                else if (effectId == MorrowindMagicEffectIds.RemoveCurse && IsSourceSpellType(ref content, active, MorrowindActorMagicUtility.SpellTypeCurse))
                    RemoveKnownAndActiveSource(ref content, activeSpells, knownSpells, effects, active.SourceIdHash, i);
                else if (effectId == MorrowindMagicEffectIds.Dispel && active.SourceKind != ActorActiveMagicEffectSourceKind.PassiveSpell)
                    RemoveWholeActiveSpell(activeSpells, effects, active.ActiveSpellId, i);
            }
        }

        static void RemoveKnownAndActiveSource(
            ref RuntimeContentBlob content,
            DynamicBuffer<ActorActiveSpell> activeSpells,
            DynamicBuffer<ActorKnownSpell> knownSpells,
            DynamicBuffer<ActorActiveMagicEffect> effects,
            ulong sourceIdHash,
            int currentIndex)
        {
            if (sourceIdHash == 0UL)
                return;

            for (int i = knownSpells.Length - 1; i >= 0; i--)
            {
                var spell = knownSpells[i].Spell;
                if (!spell.IsValid)
                    continue;
                ref RuntimeSpellDefBlob spellDef = ref RuntimeContentBlobUtility.Get(ref content, spell);
                if (spellDef.IdHash == sourceIdHash)
                    knownSpells.RemoveAt(i);
            }

            int activeSpellId = currentIndex >= 0 && currentIndex < effects.Length ? effects[currentIndex].ActiveSpellId : 0;
            if (activeSpellId != 0)
                RemoveWholeActiveSpell(activeSpells, effects, activeSpellId, currentIndex);

            for (int i = effects.Length - 1; i >= 0; i--)
            {
                if (effects[i].SourceIdHash == sourceIdHash)
                    RemoveWholeActiveSpell(activeSpells, effects, effects[i].ActiveSpellId, i);
            }
        }

        static void RemoveWholeActiveSpell(
            DynamicBuffer<ActorActiveSpell> activeSpells,
            DynamicBuffer<ActorActiveMagicEffect> effects,
            int activeSpellId,
            int fallbackEffectIndex)
        {
            if (activeSpellId == 0)
            {
                if ((uint)fallbackEffectIndex < (uint)effects.Length)
                    effects.RemoveAt(fallbackEffectIndex);
                return;
            }

            for (int i = effects.Length - 1; i >= 0; i--)
            {
                if (effects[i].ActiveSpellId == activeSpellId)
                    effects.RemoveAt(i);
            }

            for (int i = activeSpells.Length - 1; i >= 0; i--)
            {
                if (activeSpells[i].ActiveSpellId == activeSpellId)
                    activeSpells.RemoveAt(i);
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
            else if (effectId == MorrowindMagicEffectIds.AbsorbHealth || effectId == MorrowindMagicEffectIds.DamageHealth || effectId == MorrowindMagicEffectIds.DrainHealth || effectId == MorrowindMagicEffectIds.FireDamage || effectId == MorrowindMagicEffectIds.FrostDamage || effectId == MorrowindMagicEffectIds.ShockDamage || effectId == MorrowindMagicEffectIds.Poison || effectId == MorrowindMagicEffectIds.SunDamage)
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
                ActorHitAftermathStateUtility.MarkDead(entityManager, target, ref vitals, ref aftermath);
                entityManager.SetComponentData(target, vitals);
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

        static void ApplyAttributeDamageDelta(EntityManager entityManager, Entity target, short effectId, sbyte attribute, float magnitude)
        {
            if (!entityManager.HasComponent<ActorAttributeDamageSet>(target))
                throw new InvalidOperationException($"[VVardenfell][Magic] Attribute effect target entity={target.Index}:{target.Version} has no ActorAttributeDamageSet.");

            var damage = entityManager.GetComponentData<ActorAttributeDamageSet>(target);
            float delta = effectId == MorrowindMagicEffectIds.RestoreAttribute ? -magnitude : magnitude;
            ActorMagicStatUtility.AddAttribute(ref damage.Value, attribute, delta);
            entityManager.SetComponentData(target, damage);
        }

        static void ApplySkillDamageDelta(EntityManager entityManager, Entity target, short effectId, sbyte skill, float magnitude)
        {
            if (!entityManager.HasComponent<ActorSkillDamageSet>(target))
                throw new InvalidOperationException($"[VVardenfell][Magic] Skill effect target entity={target.Index}:{target.Version} has no ActorSkillDamageSet.");

            var damage = entityManager.GetComponentData<ActorSkillDamageSet>(target);
            float delta = effectId == MorrowindMagicEffectIds.RestoreSkill ? -magnitude : magnitude;
            ActorMagicStatUtility.AddSkill(ref damage.Value, skill, delta);
            entityManager.SetComponentData(target, damage);
        }

        static bool IsAbsorbEffect(short effectId)
            => effectId == MorrowindMagicEffectIds.AbsorbHealth
               || effectId == MorrowindMagicEffectIds.AbsorbMagicka
               || effectId == MorrowindMagicEffectIds.AbsorbFatigue;

        static bool IsVitalEffect(short effectId)
            => effectId == MorrowindMagicEffectIds.FireDamage
               || effectId == MorrowindMagicEffectIds.ShockDamage
               || effectId == MorrowindMagicEffectIds.FrostDamage
               || effectId == MorrowindMagicEffectIds.SunDamage
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

        static bool IsAttributeEffect(short effectId)
            => effectId == MorrowindMagicEffectIds.DamageAttribute
               || effectId == MorrowindMagicEffectIds.DrainAttribute
               || effectId == MorrowindMagicEffectIds.RestoreAttribute
               || effectId == MorrowindMagicEffectIds.FortifyAttribute
               || effectId == MorrowindMagicEffectIds.AbsorbAttribute;

        static bool IsSkillEffect(short effectId)
            => effectId == MorrowindMagicEffectIds.DamageSkill
               || effectId == MorrowindMagicEffectIds.DrainSkill
               || effectId == MorrowindMagicEffectIds.RestoreSkill
               || effectId == MorrowindMagicEffectIds.FortifySkill
               || effectId == MorrowindMagicEffectIds.AbsorbSkill;

        static bool IsAttributeAppliedOnceEffect(short effectId)
            => effectId == MorrowindMagicEffectIds.DamageAttribute
               || effectId == MorrowindMagicEffectIds.RestoreAttribute;

        static bool IsSkillAppliedOnceEffect(short effectId)
            => effectId == MorrowindMagicEffectIds.DamageSkill
               || effectId == MorrowindMagicEffectIds.RestoreSkill;

        static bool IsVitalAppliedOnceEffect(short effectId)
            => effectId == MorrowindMagicEffectIds.RestoreHealth
               || effectId == MorrowindMagicEffectIds.RestoreMagicka
               || effectId == MorrowindMagicEffectIds.RestoreFatigue;

        static bool IsModifierOnlyEffect(short effectId)
            => effectId == MorrowindMagicEffectIds.Paralyze
               || effectId == MorrowindMagicEffectIds.WaterBreathing
               || effectId == MorrowindMagicEffectIds.WaterWalking
               || effectId == MorrowindMagicEffectIds.SwiftSwim
               || effectId == MorrowindMagicEffectIds.Levitate
               || effectId == MorrowindMagicEffectIds.SlowFall
               || effectId == MorrowindMagicEffectIds.Light
               || effectId == MorrowindMagicEffectIds.NightEye
               || effectId == MorrowindMagicEffectIds.Telekinesis
               || effectId == MorrowindMagicEffectIds.DetectAnimal
               || effectId == MorrowindMagicEffectIds.DetectEnchantment
               || effectId == MorrowindMagicEffectIds.DetectKey
               || effectId == MorrowindMagicEffectIds.Silence
               || effectId == MorrowindMagicEffectIds.Blind
               || effectId == MorrowindMagicEffectIds.Sound
               || effectId == MorrowindMagicEffectIds.Charm
               || effectId == MorrowindMagicEffectIds.CalmHumanoid
               || effectId == MorrowindMagicEffectIds.CalmCreature
               || effectId == MorrowindMagicEffectIds.FrenzyHumanoid
               || effectId == MorrowindMagicEffectIds.FrenzyCreature
               || effectId == MorrowindMagicEffectIds.DemoralizeHumanoid
               || effectId == MorrowindMagicEffectIds.DemoralizeCreature
               || effectId == MorrowindMagicEffectIds.RallyHumanoid
               || effectId == MorrowindMagicEffectIds.RallyCreature
               || effectId == MorrowindMagicEffectIds.TurnUndead
               || effectId == MorrowindMagicEffectIds.CommandCreature
               || effectId == MorrowindMagicEffectIds.CommandHumanoid
               || effectId == MorrowindMagicEffectIds.Sanctuary
               || effectId == MorrowindMagicEffectIds.Chameleon
               || effectId == MorrowindMagicEffectIds.Invisibility
               || effectId == MorrowindMagicEffectIds.Shield
               || effectId == MorrowindMagicEffectIds.FireShield
               || effectId == MorrowindMagicEffectIds.FrostShield
               || effectId == MorrowindMagicEffectIds.LightningShield
               || effectId == MorrowindMagicEffectIds.FortifyAttack
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
               || effectId == MorrowindMagicEffectIds.ResistNormalWeapons
               || effectId == MorrowindMagicEffectIds.ResistParalysis
               || effectId == MorrowindMagicEffectIds.WeaknessToFire
               || effectId == MorrowindMagicEffectIds.WeaknessToFrost
               || effectId == MorrowindMagicEffectIds.WeaknessToShock
               || effectId == MorrowindMagicEffectIds.WeaknessToMagicka
               || effectId == MorrowindMagicEffectIds.WeaknessToCommonDisease
               || effectId == MorrowindMagicEffectIds.WeaknessToBlightDisease
               || effectId == MorrowindMagicEffectIds.WeaknessToCorprusDisease
               || effectId == MorrowindMagicEffectIds.WeaknessToPoison
               || effectId == MorrowindMagicEffectIds.WeaknessToNormalWeapons
               || effectId == MorrowindMagicEffectIds.Corprus
               || effectId == MorrowindMagicEffectIds.Vampirism;

        static bool IsPurgeEffect(short effectId)
            => effectId == MorrowindMagicEffectIds.CureCommonDisease
               || effectId == MorrowindMagicEffectIds.CureBlightDisease
               || effectId == MorrowindMagicEffectIds.CureCorprusDisease
               || effectId == MorrowindMagicEffectIds.CurePoison
               || effectId == MorrowindMagicEffectIds.CureParalyzation
               || effectId == MorrowindMagicEffectIds.Dispel
               || effectId == MorrowindMagicEffectIds.RemoveCurse;

        static bool AppliesContinuously(short effectId)
            => effectId == MorrowindMagicEffectIds.FireDamage
               || effectId == MorrowindMagicEffectIds.ShockDamage
               || effectId == MorrowindMagicEffectIds.FrostDamage
               || effectId == MorrowindMagicEffectIds.SunDamage
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

        static bool IsAdjacentSystemEffect(short effectId)
            => effectId == MorrowindMagicEffectIds.Lock
               || effectId == MorrowindMagicEffectIds.Open
               || effectId == MorrowindMagicEffectIds.Mark
               || effectId == MorrowindMagicEffectIds.Recall
               || effectId == MorrowindMagicEffectIds.DivineIntervention
               || effectId == MorrowindMagicEffectIds.AlmsiviIntervention
               || effectId == MorrowindMagicEffectIds.Soultrap
               || effectId == MorrowindMagicEffectIds.DisintegrateWeapon
               || effectId == MorrowindMagicEffectIds.DisintegrateArmor
               || effectId == MorrowindMagicEffectIds.ExtraSpell
               || IsSummonEffect(effectId)
               || IsBoundEffect(effectId);

        static bool IsSummonEffect(short effectId)
            => effectId >= 102 && effectId <= 116 || effectId == 134 || effectId >= 138 && effectId <= 142;

        static bool IsBoundEffect(short effectId)
            => effectId >= 119 && effectId <= 132;
    }
}
