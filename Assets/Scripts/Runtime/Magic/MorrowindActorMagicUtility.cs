using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Movement;

namespace VVardenfell.Runtime.Magic
{
    static class MorrowindActorMagicUtility
    {
        public const short CureCommonDiseaseEffectId = 69;
        public const short CureBlightDiseaseEffectId = 70;

        const int SpellTypeAbility = 1;
        const int SpellTypeBlight = 2;
        const int SpellTypeDisease = 3;
        const int SpellTypeCurse = 4;

        public static bool AddKnownSpell(DynamicBuffer<ActorKnownSpell> knownSpells, SpellDefHandle spellHandle)
        {
            if (!spellHandle.IsValid)
                return false;

            if (FindKnownSpellIndex(knownSpells, spellHandle) >= 0)
                return true;

            knownSpells.Add(new ActorKnownSpell
            {
                Spell = spellHandle,
            });
            return true;
        }

        public static bool RemoveKnownSpell(
            RuntimeContentDatabase contentDb,
            DynamicBuffer<ActorKnownSpell> knownSpells,
            DynamicBuffer<ActorActiveMagicEffect> activeEffects,
            SpellDefHandle spellHandle)
        {
            if (contentDb == null || !spellHandle.IsValid)
                return false;

            int index = FindKnownSpellIndex(knownSpells, spellHandle);
            if (index >= 0)
                knownSpells.RemoveAt(index);

            ref readonly var spell = ref contentDb.Get(spellHandle);
            if (IsPassiveSpellType(spell.SpellType))
                RemoveActiveEffectsBySource(activeEffects, spell.Id);

            return true;
        }

        public static bool CanRepresentScriptedCast(RuntimeContentDatabase contentDb, SpellDefHandle spellHandle)
        {
            if (contentDb == null || !spellHandle.IsValid)
                return false;

            ref readonly var spell = ref contentDb.Get(spellHandle);
            if (!TryGetSpellEffects(contentDb, spell, out var effects))
                return false;

            for (int i = 0; i < effects.Length; i++)
            {
                if (!CanRepresentScriptedEffect(effects[i].EffectId))
                    return false;
            }

            return true;
        }

        public static bool ApplyScriptedCast(
            RuntimeContentDatabase contentDb,
            DynamicBuffer<ActorActiveMagicEffect> targetEffects,
            SpellDefHandle spellHandle)
        {
            if (contentDb == null || !spellHandle.IsValid)
                return false;

            ref readonly var spell = ref contentDb.Get(spellHandle);
            if (!TryGetSpellEffects(contentDb, spell, out var effects))
                return false;

            if (!CanRepresentScriptedCast(contentDb, spellHandle))
                return false;

            for (int i = 0; i < effects.Length; i++)
            {
                var effect = effects[i];
                switch (effect.EffectId)
                {
                    case CureCommonDiseaseEffectId:
                        RemoveEffectsBySpellType(contentDb, targetEffects, SpellTypeDisease);
                        break;
                    case CureBlightDiseaseEffectId:
                        RemoveEffectsBySpellType(contentDb, targetEffects, SpellTypeBlight);
                        break;
                    default:
                        AppendRepresentedEffect(targetEffects, spell, effect);
                        break;
                }
            }

            return true;
        }

        public static bool IsPassiveSpellType(int spellType)
            => spellType is SpellTypeAbility or SpellTypeBlight or SpellTypeDisease or SpellTypeCurse;

        static bool CanRepresentScriptedEffect(short effectId)
            => effectId is CureCommonDiseaseEffectId or CureBlightDiseaseEffectId;

        static int FindKnownSpellIndex(DynamicBuffer<ActorKnownSpell> knownSpells, SpellDefHandle spellHandle)
        {
            for (int i = 0; i < knownSpells.Length; i++)
            {
                if (knownSpells[i].Spell.Value == spellHandle.Value)
                    return i;
            }

            return -1;
        }

        static bool TryGetSpellEffects(
            RuntimeContentDatabase contentDb,
            in SpellDef spell,
            out ReadOnlySpan<MagicEffectInstanceDef> effects)
        {
            effects = ReadOnlySpan<MagicEffectInstanceDef>.Empty;
            if (contentDb?.Data.MagicEffectInstances == null || spell.EffectStartIndex < 0 || spell.EffectCount <= 0)
                return false;

            int available = math.max(0, contentDb.Data.MagicEffectInstances.Length - spell.EffectStartIndex);
            int count = math.min(spell.EffectCount, available);
            if (count <= 0)
                return false;

            effects = new ReadOnlySpan<MagicEffectInstanceDef>(contentDb.Data.MagicEffectInstances, spell.EffectStartIndex, count);
            return true;
        }

        static void AppendRepresentedEffect(
            DynamicBuffer<ActorActiveMagicEffect> targetEffects,
            in SpellDef spell,
            in MagicEffectInstanceDef effect)
        {
            float duration = effect.Duration <= 0 ? -1f : effect.Duration;
            targetEffects.Add(new ActorActiveMagicEffect
            {
                EffectId = effect.EffectId,
                Skill = effect.Skill,
                Attribute = effect.Attribute,
                Magnitude = math.max(effect.MagnitudeMin, effect.MagnitudeMax),
                DurationSeconds = duration,
                TimeLeftSeconds = duration,
                Applied = 1,
                SourceKind = duration < 0f ? ActorActiveMagicEffectSourceKind.PassiveSpell : ActorActiveMagicEffectSourceKind.TimedSpell,
                SourceName = RuntimeFixedStringUtility.ToFixed64OrDefaultWhiteSpace(string.IsNullOrWhiteSpace(spell.Name) ? spell.Id : spell.Name.Trim()),
                SourceId = RuntimeFixedStringUtility.ToFixed64OrDefaultWhiteSpace(spell.Id),
            });
        }

        static void RemoveActiveEffectsBySource(DynamicBuffer<ActorActiveMagicEffect> activeEffects, string sourceId)
        {
            FixedString64Bytes fixedSourceId = RuntimeFixedStringUtility.ToFixed64OrDefaultWhiteSpace(sourceId);
            for (int i = activeEffects.Length - 1; i >= 0; i--)
            {
                if (activeEffects[i].SourceId.Equals(fixedSourceId))
                    activeEffects.RemoveAt(i);
            }
        }

        static void RemoveEffectsBySpellType(
            RuntimeContentDatabase contentDb,
            DynamicBuffer<ActorActiveMagicEffect> activeEffects,
            int spellType)
        {
            for (int i = activeEffects.Length - 1; i >= 0; i--)
            {
                string sourceId = activeEffects[i].SourceId.ToString();
                if (string.IsNullOrWhiteSpace(sourceId)
                    || !contentDb.TryGetSpellHandle(sourceId, out var sourceHandle)
                    || !sourceHandle.IsValid)
                {
                    continue;
                }

                ref readonly var sourceSpell = ref contentDb.Get(sourceHandle);
                if (sourceSpell.SpellType == spellType)
                    activeEffects.RemoveAt(i);
            }
        }
    }
}
