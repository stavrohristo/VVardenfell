using System;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Mathematics;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;

namespace VVardenfell.Runtime.Movement
{
    public static class MorrowindActorMovementStats
    {
        public readonly struct Context
        {
            readonly RuntimeContentDatabase _contentDb;
            readonly ActorAttributeSet _attributes;
            readonly ActorSkillSet _skills;
            readonly ActorVitalSet _vitals;
            readonly ActorEffectStatModifiers _effectModifiers;
            readonly ActorDerivedMovementStats _derived;
            readonly MorrowindMovementSpeed _speed;

            public Context(
                RuntimeContentDatabase contentDb,
                in ActorAttributeSet attributes,
                in ActorSkillSet skills,
                in ActorVitalSet vitals,
                in ActorEffectStatModifiers effectModifiers,
                in ActorDerivedMovementStats derived,
                in MorrowindMovementSpeed speed)
            {
                _contentDb = contentDb;
                _attributes = attributes;
                _skills = skills;
                _vitals = vitals;
                _effectModifiers = effectModifiers;
                _derived = derived;
                _speed = speed;
            }

            public float GetCurrentSpeed(bool running, bool sneaking, bool inAir, float speedFactor, bool strafing)
            {
                float speed = running && !sneaking
                    ? _speed.RunSpeed
                    : (sneaking ? _speed.SneakWalkSpeed : _speed.WalkSpeed);

                speed *= math.saturate(speedFactor);
                if (strafing)
                    speed *= 0.75f;

                return speed * WorldScale.MwUnitsToMeters;
            }

            public float GetJumpSpeed(bool running)
            {
                if (running)
                    return _speed.JumpSpeed * _speed.JumpRunMultiplier * WorldScale.MwUnitsToMeters;

                return _speed.JumpSpeed * WorldScale.MwUnitsToMeters;
            }

            public float GetJumpMoveFactor() => _speed.JumpMoveFactor;

            public float GetMovementFatigueLossPerSecond(bool running, bool sneaking, float speedFactor)
            {
                if (speedFactor <= 0f || _derived.Encumbrance > _derived.CarryCapacity)
                    return 0f;

                float fatigueLoss = 0f;
                if (sneaking)
                {
                    fatigueLoss = Gmst("fFatigueSneakBase", 0f)
                        + _derived.NormalizedEncumbrance * Gmst("fFatigueSneakMult", 0f);
                }
                else if (running)
                {
                    fatigueLoss = Gmst("fFatigueRunBase", 0f)
                        + _derived.NormalizedEncumbrance * Gmst("fFatigueRunMult", 0f);
                }

                return fatigueLoss * math.saturate(speedFactor);
            }

            public float GetJumpFatigueLoss()
            {
                float normalizedEncumbrance = math.min(1f, _derived.NormalizedEncumbrance);
                return Gmst("fFatigueJumpBase", 0f)
                    + normalizedEncumbrance * Gmst("fFatigueJumpMult", 0f);
            }

            public float GetFatigueRestorePerSecond()
            {
                if (_vitals.CurrentFatigue >= _vitals.ModifiedFatigueBase)
                    return 0f;

                float normalizedEncumbrance = _derived.NormalizedEncumbrance;
                if (normalizedEncumbrance > 1f)
                    normalizedEncumbrance = 1f;

                return (Gmst("fFatigueReturnBase", 0f) + Gmst("fFatigueReturnMult", 0f) * (1f - normalizedEncumbrance))
                    * (Gmst("fEndFatigueMult", 1f) * _attributes.Endurance);
            }

            float Gmst(string id, float fallback)
            {
                if (_contentDb != null && _contentDb.TryGetGameSettingFloat(id, out float value))
                    return value;
                return fallback;
            }
        }

        public static ActorRuntimeStatSeed CreateDefaultPlayerSeed()
        {
            var attributes = new ActorAttributeSet
            {
                Strength = 40f,
                Intelligence = 40f,
                Willpower = 40f,
                Agility = 40f,
                Speed = 40f,
                Endurance = 40f,
                Personality = 40f,
                Luck = 40f,
            };

            var vitals = new ActorVitalSet();
            ApplyVitalBases(null, attributes, ref vitals, initializeMissingCurrents: true);
            return new ActorRuntimeStatSeed
            {
                Attributes = attributes,
                Skills = new ActorSkillSet
                {
                    Block = 30f,
                    Armorer = 30f,
                    MediumArmor = 30f,
                    HeavyArmor = 30f,
                    BluntWeapon = 30f,
                    LongBlade = 30f,
                    Axe = 30f,
                    Spear = 30f,
                    Athletics = 30f,
                    Enchant = 30f,
                    Destruction = 30f,
                    Alteration = 30f,
                    Illusion = 30f,
                    Conjuration = 30f,
                    Mysticism = 30f,
                    Restoration = 30f,
                    Alchemy = 30f,
                    Unarmored = 30f,
                    Security = 30f,
                    Sneak = 30f,
                    Acrobatics = 30f,
                    LightArmor = 30f,
                    ShortBlade = 30f,
                    Marksman = 30f,
                    Mercantile = 30f,
                    Speechcraft = 30f,
                    HandToHand = 30f,
                },
                Vitals = vitals,
                EffectModifiers = new ActorEffectStatModifiers
                {
                    JumpMagnitude = 0f,
                    FeatherMagnitude = 0f,
                    BurdenMagnitude = 0f,
                },
            };
        }

        public static ActorRuntimeStatSeed CreateSeedFromActor(RuntimeContentDatabase contentDb, in ActorDef actor)
        {
            var attributes = new ActorAttributeSet
            {
                Strength = actor.Attributes.Strength,
                Intelligence = actor.Attributes.Intelligence,
                Willpower = actor.Attributes.Willpower,
                Agility = actor.Attributes.Agility,
                Speed = actor.Attributes.Speed,
                Endurance = actor.Attributes.Endurance,
                Personality = actor.Attributes.Personality,
                Luck = actor.Attributes.Luck,
            };

            var skills = new ActorSkillSet
            {
                Block = actor.Skills.Block,
                Armorer = actor.Skills.Armorer,
                MediumArmor = actor.Skills.MediumArmor,
                HeavyArmor = actor.Skills.HeavyArmor,
                BluntWeapon = actor.Skills.BluntWeapon,
                LongBlade = actor.Skills.LongBlade,
                Axe = actor.Skills.Axe,
                Spear = actor.Skills.Spear,
                Athletics = actor.Skills.Athletics,
                Enchant = actor.Skills.Enchant,
                Destruction = actor.Skills.Destruction,
                Alteration = actor.Skills.Alteration,
                Illusion = actor.Skills.Illusion,
                Conjuration = actor.Skills.Conjuration,
                Mysticism = actor.Skills.Mysticism,
                Restoration = actor.Skills.Restoration,
                Alchemy = actor.Skills.Alchemy,
                Unarmored = actor.Skills.Unarmored,
                Security = actor.Skills.Security,
                Sneak = actor.Skills.Sneak,
                Acrobatics = actor.Skills.Acrobatics,
                LightArmor = actor.Skills.LightArmor,
                ShortBlade = actor.Skills.ShortBlade,
                Marksman = actor.Skills.Marksman,
                Mercantile = actor.Skills.Mercantile,
                Speechcraft = actor.Skills.Speechcraft,
                HandToHand = actor.Skills.HandToHand,
            };

            var vitals = new ActorVitalSet
            {
                CurrentHealth = actor.Vitals.Health,
                ModifiedHealthBase = actor.Vitals.Health,
                CurrentMagicka = actor.Vitals.Magicka,
                ModifiedMagickaBase = actor.Vitals.Magicka,
                CurrentFatigue = actor.Vitals.Fatigue,
                ModifiedFatigueBase = actor.Vitals.Fatigue,
            };

            if (actor.AutoCalculatedStats != 0
                || vitals.ModifiedHealthBase <= 0f
                || vitals.ModifiedFatigueBase <= 0f)
            {
                ApplyVitalBases(contentDb, attributes, ref vitals, initializeMissingCurrents: true);
            }

            return new ActorRuntimeStatSeed
            {
                Attributes = attributes,
                Skills = skills,
                Vitals = vitals,
                EffectModifiers = new ActorEffectStatModifiers(),
            };
        }

        public static ActorIdentitySet CreateIdentityFromActor(in ActorDef actor)
        {
            string characterName = string.IsNullOrWhiteSpace(actor.Name)
                ? (string.Equals(actor.Id, "player", StringComparison.OrdinalIgnoreCase) ? "Player" : actor.Id)
                : actor.Name;

            return new ActorIdentitySet
            {
                CharacterName = ToFixed64(characterName),
                Level = math.max(1, actor.Level),
                RaceName = ToFixed64(actor.RaceId),
                ClassName = ToFixed64(actor.ClassId),
                BirthSignName = default,
                Reputation = actor.Reputation,
            };
        }

        public static PlayerKnownSpell[] BuildKnownSpellListFromActor(RuntimeContentDatabase contentDb, ActorDefHandle actorHandle)
        {
            if (contentDb == null || !actorHandle.IsValid)
                return Array.Empty<PlayerKnownSpell>();

            ref readonly var actor = ref contentDb.Get(actorHandle);
            var actorSpells = contentDb.GetActorSpells(actorHandle);
            var raceHandle = default(GenericRecordDefHandle);
            bool hasRacePowers = !string.IsNullOrWhiteSpace(actor.RaceId)
                && contentDb.TryGetRaceHandle(actor.RaceId, out raceHandle)
                && raceHandle.IsValid
                && contentDb.GetRace(raceHandle).PowerSpellIds != null
                && contentDb.GetRace(raceHandle).PowerSpellIds.Length > 0;

            if (actorSpells.Length == 0 && !hasRacePowers)
                return Array.Empty<PlayerKnownSpell>();

            var results = new List<PlayerKnownSpell>(actorSpells.Length + (hasRacePowers ? contentDb.GetRace(raceHandle).PowerSpellIds.Length : 0));
            for (int i = 0; i < actorSpells.Length; i++)
                AddKnownSpell(contentDb, actorSpells[i].SpellId, results);

            if (hasRacePowers)
            {
                ref readonly var race = ref contentDb.GetRace(raceHandle);
                for (int i = 0; i < race.PowerSpellIds.Length; i++)
                    AddKnownSpell(contentDb, race.PowerSpellIds[i], results);
            }

            return results.ToArray();
        }

        static void AddKnownSpell(RuntimeContentDatabase contentDb, string spellId, List<PlayerKnownSpell> results)
        {
            if (string.IsNullOrWhiteSpace(spellId) || !contentDb.TryGetSpellHandle(spellId, out var spellHandle) || !spellHandle.IsValid)
                return;

            for (int i = 0; i < results.Count; i++)
            {
                if (results[i].Spell.Value == spellHandle.Value)
                    return;
            }

            results.Add(new PlayerKnownSpell
            {
                Spell = spellHandle,
            });
        }

        public static bool TryCreatePlayerSeedFromContent(
            RuntimeContentDatabase contentDb,
            out ActorRuntimeStatSeed stats,
            out ActorIdentitySet identity,
            out PlayerKnownSpell[] knownSpells,
            out PlayerInitialInventoryItem[] initialInventory)
        {
            stats = CreateDefaultPlayerSeed();
            identity = ActorIdentitySet.DefaultPlayer();
            knownSpells = Array.Empty<PlayerKnownSpell>();
            initialInventory = Array.Empty<PlayerInitialInventoryItem>();

            if (contentDb == null || !contentDb.TryGetActorHandle("player", out var actorHandle) || !actorHandle.IsValid)
                return false;

            ref readonly var actor = ref contentDb.Get(actorHandle);
            if (actor.Kind != ActorDefKind.Npc)
                return false;

            stats = HasManualActorStats(actor)
                ? CreateSeedFromActor(contentDb, actor)
                : CreateDefaultPlayerSeed(contentDb);
            identity = CreateIdentityFromActor(actor);
            knownSpells = BuildKnownSpellListFromActor(contentDb, actorHandle);
            initialInventory = BuildInitialInventoryListFromActor(contentDb, actorHandle);
            return true;
        }

        static ActorRuntimeStatSeed CreateDefaultPlayerSeed(RuntimeContentDatabase contentDb)
        {
            var seed = CreateDefaultPlayerSeed();
            var vitals = new ActorVitalSet();
            ApplyVitalBases(contentDb, seed.Attributes, ref vitals, initializeMissingCurrents: true);
            seed.Vitals = vitals;
            return seed;
        }

        public static PlayerInitialInventoryItem[] BuildInitialInventoryListFromActor(RuntimeContentDatabase contentDb, ActorDefHandle actorHandle)
        {
            if (contentDb == null || !actorHandle.IsValid)
                return Array.Empty<PlayerInitialInventoryItem>();

            var actorItems = contentDb.GetActorInventoryItems(actorHandle);
            if (actorItems.Length == 0)
                return Array.Empty<PlayerInitialInventoryItem>();

            var results = new List<PlayerInitialInventoryItem>(actorItems.Length);
            for (int i = 0; i < actorItems.Length; i++)
            {
                var item = actorItems[i];
                if (item.Count <= 0 || string.IsNullOrWhiteSpace(item.ItemId))
                    continue;

                if (!contentDb.TryResolvePlaceable(item.ItemId, out var contentRef) || !contentRef.IsValid)
                    continue;

                results.Add(new PlayerInitialInventoryItem
                {
                    Content = contentRef,
                    Count = item.Count,
                });
            }

            return results.ToArray();
        }

        static bool HasManualActorStats(in ActorDef actor)
        {
            if (actor.AutoCalculatedStats != 0)
                return false;

            return HasAnyAttribute(actor.Attributes)
                && HasAnySkill(actor.Skills)
                && (actor.Vitals.Health > 0 || actor.Vitals.Magicka > 0 || actor.Vitals.Fatigue > 0);
        }

        static bool HasAnyAttribute(in ActorAttributeDef attributes)
            => attributes.Strength != 0
               || attributes.Intelligence != 0
               || attributes.Willpower != 0
               || attributes.Agility != 0
               || attributes.Speed != 0
               || attributes.Endurance != 0
               || attributes.Personality != 0
               || attributes.Luck != 0;

        static bool HasAnySkill(in ActorSkillDef skills)
            => skills.Block != 0
               || skills.Armorer != 0
               || skills.MediumArmor != 0
               || skills.HeavyArmor != 0
               || skills.BluntWeapon != 0
               || skills.LongBlade != 0
               || skills.Axe != 0
               || skills.Spear != 0
               || skills.Athletics != 0
               || skills.Enchant != 0
               || skills.Destruction != 0
               || skills.Alteration != 0
               || skills.Illusion != 0
               || skills.Conjuration != 0
               || skills.Mysticism != 0
               || skills.Restoration != 0
               || skills.Alchemy != 0
               || skills.Unarmored != 0
               || skills.Security != 0
               || skills.Sneak != 0
               || skills.Acrobatics != 0
               || skills.LightArmor != 0
               || skills.ShortBlade != 0
               || skills.Marksman != 0
               || skills.Mercantile != 0
               || skills.Speechcraft != 0
               || skills.HandToHand != 0;

        public static ActorDerivedMovementStats BuildDerived(
            RuntimeContentDatabase contentDb,
            in ActorAttributeSet attributes,
            in ActorSkillSet skills,
            in ActorVitalSet vitals,
            in ActorEffectStatModifiers effectModifiers,
            float inventoryWeight)
        {
            var derived = new ActorDerivedMovementStats
            {
                CarryCapacity = ComputeCarryCapacity(contentDb, attributes),
                Encumbrance = ComputeEncumbrance(effectModifiers, inventoryWeight),
            };
            derived.NormalizedEncumbrance = ComputeNormalizedEncumbrance(derived.Encumbrance, derived.CarryCapacity);
            ApplyMovementDerived(contentDb, attributes, skills, vitals, effectModifiers, ref derived);
            return derived;
        }

        public static MorrowindMovementSpeed BuildMovementSpeed(
            RuntimeContentDatabase contentDb,
            ActorDefKind actorKind,
            in ActorAttributeSet attributes,
            in ActorSkillSet skills,
            in ActorVitalSet vitals,
            in ActorEffectStatModifiers effectModifiers,
            in ActorDerivedMovementStats derived)
        {
            float jumpSpeed = ComputeJumpSpeed(contentDb, skills, effectModifiers, derived);
            float jumpMoveFactor = ComputeJumpMoveFactor(contentDb, skills);
            if (actorKind == ActorDefKind.Creature)
            {
                float walkSpeed = Gmst(contentDb, "fMinWalkSpeedCreature", 5f)
                    + 0.01f * attributes.Speed * (Gmst(contentDb, "fMaxWalkSpeedCreature", 300f) - Gmst(contentDb, "fMinWalkSpeedCreature", 5f));

                return new MorrowindMovementSpeed
                {
                    WalkSpeed = math.max(0f, walkSpeed),
                    RunSpeed = math.max(0f, walkSpeed),
                    SneakWalkSpeed = math.max(0f, walkSpeed),
                    JumpSpeed = jumpSpeed,
                    JumpRunMultiplier = 1f,
                    JumpMoveFactor = jumpMoveFactor,
                };
            }

            float walkSpeedNpc = ComputeWalkSpeed(contentDb, attributes, derived);
            float runSpeedNpc = walkSpeedNpc
                * (0.01f * skills.Athletics * Gmst(contentDb, "fAthleticsRunBonus", 1f)
                    + Gmst(contentDb, "fBaseRunMultiplier", 1.75f));
            float sneakWalkSpeedNpc = walkSpeedNpc * Gmst(contentDb, "fSneakSpeedMultiplier", 0.5f);

            return new MorrowindMovementSpeed
            {
                WalkSpeed = walkSpeedNpc,
                RunSpeed = math.max(0f, runSpeedNpc),
                SneakWalkSpeed = math.max(0f, sneakWalkSpeedNpc),
                JumpSpeed = jumpSpeed,
                JumpRunMultiplier = Gmst(contentDb, "fJumpRunMultiplier", 1f),
                JumpMoveFactor = jumpMoveFactor,
            };
        }

        public static MorrowindMovementSpeed BuildPlayerMovementSpeed(
            RuntimeContentDatabase contentDb,
            in ActorAttributeSet attributes,
            in ActorSkillSet skills,
            in ActorVitalSet vitals,
            in ActorEffectStatModifiers effectModifiers,
            in ActorDerivedMovementStats derived)
            => BuildMovementSpeed(contentDb, ActorDefKind.Npc, attributes, skills, vitals, effectModifiers, derived);

        public static float ComputeModifiedFatigueBase(in ActorAttributeSet attributes)
            => math.max(0f, attributes.Strength + attributes.Willpower + attributes.Agility + attributes.Endurance);

        public static float ComputeModifiedHealthBase(in ActorAttributeSet attributes)
            => math.max(1f, (attributes.Strength + attributes.Endurance) * 0.5f);

        public static float ComputeModifiedMagickaBase(RuntimeContentDatabase contentDb, in ActorAttributeSet attributes)
            => math.max(0f, attributes.Intelligence * Gmst(contentDb, "fPCbaseMagickaMult", 1f));

        public static void ApplyVitalBases(
            RuntimeContentDatabase contentDb,
            in ActorAttributeSet attributes,
            ref ActorVitalSet vitals,
            bool initializeMissingCurrents)
        {
            vitals.ModifiedHealthBase = ComputeModifiedHealthBase(attributes);
            vitals.ModifiedMagickaBase = ComputeModifiedMagickaBase(contentDb, attributes);
            vitals.ModifiedFatigueBase = ComputeModifiedFatigueBase(attributes);

            if (initializeMissingCurrents)
            {
                if (vitals.CurrentHealth <= 0f)
                    vitals.CurrentHealth = vitals.ModifiedHealthBase;
                if (vitals.CurrentMagicka <= 0f)
                    vitals.CurrentMagicka = vitals.ModifiedMagickaBase;
                if (vitals.CurrentFatigue <= 0f)
                    vitals.CurrentFatigue = vitals.ModifiedFatigueBase;
            }

            vitals.CurrentHealth = ClampVital(vitals.CurrentHealth, vitals.ModifiedHealthBase);
            vitals.CurrentMagicka = ClampVital(vitals.CurrentMagicka, vitals.ModifiedMagickaBase);
            vitals.CurrentFatigue = ClampVital(vitals.CurrentFatigue, vitals.ModifiedFatigueBase);
        }

        public static float ComputeCarryCapacity(RuntimeContentDatabase contentDb, in ActorAttributeSet attributes)
            => math.max(0f, attributes.Strength * Gmst(contentDb, "fEncumbranceStrMult", 5f));

        public static float ComputeEncumbrance(in ActorEffectStatModifiers effectModifiers, float inventoryWeight)
            => math.max(0f, inventoryWeight - effectModifiers.FeatherMagnitude + effectModifiers.BurdenMagnitude);

        public static float ComputeNormalizedEncumbrance(float encumbrance, float carryCapacity)
        {
            if (encumbrance == 0f)
                return 0f;
            if (carryCapacity == 0f)
                return 1f;
            return encumbrance / carryCapacity;
        }

        public static void ApplyMovementDerived(
            RuntimeContentDatabase contentDb,
            in ActorAttributeSet attributes,
            in ActorSkillSet skills,
            in ActorVitalSet vitals,
            in ActorEffectStatModifiers effectModifiers,
            ref ActorDerivedMovementStats derived)
        {
            float modifiedFatigueBase = math.max(0f, vitals.ModifiedFatigueBase);
            float normalizedFatigue = modifiedFatigueBase <= 0f
                ? 1f
                : math.max(0f, vitals.CurrentFatigue / modifiedFatigueBase);
            derived.FatigueTerm = Gmst(contentDb, "fFatigueBase", 1f)
                - Gmst(contentDb, "fFatigueMult", 1f) * (1f - normalizedFatigue);
        }

        static float ComputeWalkSpeed(
            RuntimeContentDatabase contentDb,
            in ActorAttributeSet attributes,
            in ActorDerivedMovementStats derived)
        {
            float walkSpeed = Gmst(contentDb, "fMinWalkSpeed", 100f)
                + 0.01f * attributes.Speed * (Gmst(contentDb, "fMaxWalkSpeed", 200f) - Gmst(contentDb, "fMinWalkSpeed", 100f));
            walkSpeed *= 1f - Gmst(contentDb, "fEncumberedMoveEffect", 0.5f) * derived.NormalizedEncumbrance;
            return math.max(0f, walkSpeed);
        }

        static float ComputeJumpSpeed(
            RuntimeContentDatabase contentDb,
            in ActorSkillSet skills,
            in ActorEffectStatModifiers effectModifiers,
            in ActorDerivedMovementStats derived)
        {
            float a = skills.Acrobatics;
            float b = 0f;
            if (a > 50f)
            {
                b = a - 50f;
                a = 50f;
            }

            float jump = Gmst(contentDb, "fJumpAcrobaticsBase", 128f)
                + math.pow(a / 15f, Gmst(contentDb, "fJumpAcroMultiplier", 1f));
            jump += 3f * b * Gmst(contentDb, "fJumpAcroMultiplier", 1f);
            jump += effectModifiers.JumpMagnitude * 64f;
            jump *= Gmst(contentDb, "fJumpEncumbranceBase", 0f)
                + Gmst(contentDb, "fJumpEncumbranceMultiplier", 1f) * (1f - derived.NormalizedEncumbrance);
            jump *= derived.FatigueTerm;
            jump += 8.96f * (1f / WorldScale.MwUnitsToMeters);
            jump /= 3f;
            return math.max(0f, jump);
        }

        static float ComputeJumpMoveFactor(RuntimeContentDatabase contentDb, in ActorSkillSet skills)
        {
            return math.min(1f,
                Gmst(contentDb, "fJumpMoveBase", 0f)
                + Gmst(contentDb, "fJumpMoveMult", 1f) * skills.Acrobatics / 100f);
        }

        public static Context Build(
            RuntimeContentDatabase contentDb,
            in ActorAttributeSet attributes,
            in ActorSkillSet skills,
            in ActorVitalSet vitals,
            in ActorEffectStatModifiers effectModifiers,
            in ActorDerivedMovementStats derived,
            in MorrowindMovementSpeed speed)
        {
            return new Context(contentDb, attributes, skills, vitals, effectModifiers, derived, speed);
        }

        public static string DescribeRuntimeState(
            RuntimeContentDatabase contentDb,
            in ActorAttributeSet attributes,
            in ActorSkillSet skills,
            in ActorVitalSet vitals,
            in ActorEffectStatModifiers effectModifiers,
            in ActorDerivedMovementStats derived)
        {
            var speed = BuildPlayerMovementSpeed(contentDb, attributes, skills, vitals, effectModifiers, derived);
            var context = Build(contentDb, attributes, skills, vitals, effectModifiers, derived, speed);
            var builder = new StringBuilder(384);
            builder.Append("stats str=");
            builder.Append(attributes.Strength.ToString("F0"));
            builder.Append(" wil=");
            builder.Append(attributes.Willpower.ToString("F0"));
            builder.Append(" agi=");
            builder.Append(attributes.Agility.ToString("F0"));
            builder.Append(" end=");
            builder.Append(attributes.Endurance.ToString("F0"));
            builder.Append(" spd=");
            builder.Append(attributes.Speed.ToString("F0"));
            builder.Append(" ath=");
            builder.Append(skills.Athletics.ToString("F0"));
            builder.Append(" acro=");
            builder.Append(skills.Acrobatics.ToString("F0"));
            builder.Append(" health=");
            builder.Append(vitals.CurrentHealth.ToString("F1"));
            builder.Append("/");
            builder.Append(vitals.ModifiedHealthBase.ToString("F1"));
            builder.Append(" magicka=");
            builder.Append(vitals.CurrentMagicka.ToString("F1"));
            builder.Append("/");
            builder.Append(vitals.ModifiedMagickaBase.ToString("F1"));
            builder.Append(" fatigue=");
            builder.Append(vitals.CurrentFatigue.ToString("F1"));
            builder.Append("/");
            builder.Append(vitals.ModifiedFatigueBase.ToString("F1"));
            builder.Append(" fatigueTerm=");
            builder.Append(derived.FatigueTerm.ToString("F2"));
            builder.Append(" enc=");
            builder.Append(derived.Encumbrance.ToString("F1"));
            builder.Append("/");
            builder.Append(derived.CarryCapacity.ToString("F1"));
            builder.Append(" normEnc=");
            builder.Append(derived.NormalizedEncumbrance.ToString("F2"));
            builder.Append(" walk=");
            builder.Append(context.GetCurrentSpeed(false, false, true, 1f, false).ToString("F2"));
            builder.Append(" run=");
            builder.Append(context.GetCurrentSpeed(true, false, true, 1f, false).ToString("F2"));
            builder.Append(" sneak=");
            builder.Append(context.GetCurrentSpeed(false, true, true, 1f, false).ToString("F2"));
            builder.Append(" jump=");
            builder.Append(context.GetJumpSpeed(true).ToString("F2"));
            builder.Append(" jumpMove=");
            builder.Append(context.GetJumpMoveFactor().ToString("F2"));
            builder.Append(" gmst=");
            builder.Append(contentDb != null ? "runtime" : "fallback");
            return builder.ToString();
        }

        static float Gmst(RuntimeContentDatabase contentDb, string id, float fallback)
        {
            if (contentDb != null && contentDb.TryGetGameSettingFloat(id, out float value))
                return value;
            return fallback;
        }

        static float ClampVital(float current, float max)
        {
            if (max <= 0f)
                return 0f;

            if (float.IsNaN(current) || float.IsInfinity(current))
                return max;

            return math.clamp(current, 0f, max);
        }

        static FixedString64Bytes ToFixed64(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return default;

            var result = default(FixedString64Bytes);
            result.CopyFromTruncated(value);
            return result;
        }
    }

    public static class MorrowindPlayerSpeedResolver
    {
        public static MorrowindActorMovementStats.Context Build(
            RuntimeContentDatabase contentDb,
            in ActorAttributeSet attributes,
            in ActorSkillSet skills,
            in ActorVitalSet vitals,
            in ActorEffectStatModifiers effectModifiers,
            in ActorDerivedMovementStats derived,
            in MorrowindMovementSpeed speed)
            => MorrowindActorMovementStats.Build(contentDb, attributes, skills, vitals, effectModifiers, derived, speed);

        public static string DescribeRuntimeState(
            RuntimeContentDatabase contentDb,
            in ActorAttributeSet attributes,
            in ActorSkillSet skills,
            in ActorVitalSet vitals,
            in ActorEffectStatModifiers effectModifiers,
            in ActorDerivedMovementStats derived)
            => MorrowindActorMovementStats.DescribeRuntimeState(contentDb, attributes, skills, vitals, effectModifiers, derived);
    }
}
