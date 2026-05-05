using System;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Mathematics;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.Movement
{
    public static partial class MorrowindActorMovementStats
    {
        public readonly struct Context
        {
            readonly MovementGmstSet _gmsts;
            readonly ActorAttributeSet _attributes;
            readonly ActorVitalSet _vitals;
            readonly ActorDerivedMovementStats _derived;
            readonly MorrowindMovementSpeed _speed;

            public Context(
                in MovementGmstSet gmsts,
                in ActorAttributeSet attributes,
                in ActorSkillSet skills,
                in ActorVitalSet vitals,
                in ActorEffectStatModifiers effectModifiers,
                in ActorDerivedMovementStats derived,
                in MorrowindMovementSpeed speed)
            {
                _gmsts = gmsts;
                _attributes = attributes;
                _vitals = vitals;
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
                    fatigueLoss = _gmsts.FatigueSneakBase + _derived.NormalizedEncumbrance * _gmsts.FatigueSneakMult;
                else if (running)
                    fatigueLoss = _gmsts.FatigueRunBase + _derived.NormalizedEncumbrance * _gmsts.FatigueRunMult;

                return fatigueLoss * math.saturate(speedFactor);
            }

            public float GetJumpFatigueLoss()
            {
                float normalizedEncumbrance = math.min(1f, _derived.NormalizedEncumbrance);
                return _gmsts.FatigueJumpBase + normalizedEncumbrance * _gmsts.FatigueJumpMult;
            }

            public float GetFatigueRestorePerSecond()
            {
                if (_vitals.CurrentFatigue >= _vitals.ModifiedFatigueBase)
                    return 0f;

                float normalizedEncumbrance = math.min(1f, _derived.NormalizedEncumbrance);
                return (_gmsts.FatigueReturnBase + _gmsts.FatigueReturnMult * (1f - normalizedEncumbrance))
                    * (_gmsts.EndFatigueMult * _attributes.Endurance);
            }
        }

        public struct MovementGmstSet
        {
            public float PCBaseMagickaMult;
            public float EncumbranceStrMult;
            public float FatigueBase;
            public float FatigueMult;
            public float MinWalkSpeed;
            public float MaxWalkSpeed;
            public float MinWalkSpeedCreature;
            public float MaxWalkSpeedCreature;
            public float EncumberedMoveEffect;
            public float AthleticsRunBonus;
            public float BaseRunMultiplier;
            public float SneakSpeedMultiplier;
            public float JumpRunMultiplier;
            public float JumpAcrobaticsBase;
            public float JumpAcroMultiplier;
            public float JumpEncumbranceBase;
            public float JumpEncumbranceMultiplier;
            public float JumpMoveBase;
            public float JumpMoveMult;
            public float FatigueSneakBase;
            public float FatigueSneakMult;
            public float FatigueRunBase;
            public float FatigueRunMult;
            public float FatigueJumpBase;
            public float FatigueJumpMult;
            public float FatigueReturnBase;
            public float FatigueReturnMult;
            public float EndFatigueMult;
        }

        public static MovementGmstSet LoadGmsts(ref RuntimeContentBlob content)
        {
            return new MovementGmstSet
            {
                PCBaseMagickaMult = Gmst(ref content, RuntimeContentKnownHashes.fPCbaseMagickaMult),
                EncumbranceStrMult = Gmst(ref content, RuntimeContentKnownHashes.fEncumbranceStrMult),
                FatigueBase = Gmst(ref content, RuntimeContentKnownHashes.fFatigueBase),
                FatigueMult = Gmst(ref content, RuntimeContentKnownHashes.fFatigueMult),
                MinWalkSpeed = Gmst(ref content, RuntimeContentKnownHashes.fMinWalkSpeed),
                MaxWalkSpeed = Gmst(ref content, RuntimeContentKnownHashes.fMaxWalkSpeed),
                MinWalkSpeedCreature = Gmst(ref content, RuntimeContentKnownHashes.fMinWalkSpeedCreature),
                MaxWalkSpeedCreature = Gmst(ref content, RuntimeContentKnownHashes.fMaxWalkSpeedCreature),
                EncumberedMoveEffect = Gmst(ref content, RuntimeContentKnownHashes.fEncumberedMoveEffect),
                AthleticsRunBonus = Gmst(ref content, RuntimeContentKnownHashes.fAthleticsRunBonus),
                BaseRunMultiplier = Gmst(ref content, RuntimeContentKnownHashes.fBaseRunMultiplier),
                SneakSpeedMultiplier = Gmst(ref content, RuntimeContentKnownHashes.fSneakSpeedMultiplier),
                JumpRunMultiplier = Gmst(ref content, RuntimeContentKnownHashes.fJumpRunMultiplier),
                JumpAcrobaticsBase = Gmst(ref content, RuntimeContentKnownHashes.fJumpAcrobaticsBase),
                JumpAcroMultiplier = Gmst(ref content, RuntimeContentKnownHashes.fJumpAcroMultiplier),
                JumpEncumbranceBase = Gmst(ref content, RuntimeContentKnownHashes.fJumpEncumbranceBase),
                JumpEncumbranceMultiplier = Gmst(ref content, RuntimeContentKnownHashes.fJumpEncumbranceMultiplier),
                JumpMoveBase = Gmst(ref content, RuntimeContentKnownHashes.fJumpMoveBase),
                JumpMoveMult = Gmst(ref content, RuntimeContentKnownHashes.fJumpMoveMult),
                FatigueSneakBase = Gmst(ref content, RuntimeContentKnownHashes.fFatigueSneakBase),
                FatigueSneakMult = Gmst(ref content, RuntimeContentKnownHashes.fFatigueSneakMult),
                FatigueRunBase = Gmst(ref content, RuntimeContentKnownHashes.fFatigueRunBase),
                FatigueRunMult = Gmst(ref content, RuntimeContentKnownHashes.fFatigueRunMult),
                FatigueJumpBase = Gmst(ref content, RuntimeContentKnownHashes.fFatigueJumpBase),
                FatigueJumpMult = Gmst(ref content, RuntimeContentKnownHashes.fFatigueJumpMult),
                FatigueReturnBase = Gmst(ref content, RuntimeContentKnownHashes.fFatigueReturnBase),
                FatigueReturnMult = Gmst(ref content, RuntimeContentKnownHashes.fFatigueReturnMult),
                EndFatigueMult = Gmst(ref content, RuntimeContentKnownHashes.fEndFatigueMult),
            };
        }

        public static ActorRuntimeStatSeed CreateDefaultPlayerSeed(ref RuntimeContentBlob content)
        {
            var attributes = DefaultPlayerAttributes();
            var seed = new ActorRuntimeStatSeed
            {
                Attributes = attributes,
                Skills = DefaultPlayerSkills(),
                EffectModifiers = new ActorEffectStatModifiers(),
            };

            ApplyVitalBases(ref content, attributes, ref seed.Vitals, initializeMissingCurrents: true);
            return seed;
        }

        public static ActorRuntimeStatSeed CreateSeedFromActor(ref RuntimeContentBlob content, ref RuntimeActorDefBlob actor)
        {
            var attributes = ToAttributeSet(actor.Attributes);
            var skills = ToSkillSet(actor.Skills);
            var vitals = new ActorVitalSet
            {
                CurrentHealth = actor.Vitals.Health,
                ModifiedHealthBase = actor.Vitals.Health,
                CurrentMagicka = actor.Vitals.Magicka,
                ModifiedMagickaBase = actor.Vitals.Magicka,
                CurrentFatigue = actor.Vitals.Fatigue,
                ModifiedFatigueBase = actor.Vitals.Fatigue,
            };

            if (actor.AutoCalculatedStats != 0 || vitals.ModifiedHealthBase <= 0f || vitals.ModifiedFatigueBase <= 0f)
                ApplyVitalBases(ref content, attributes, ref vitals, initializeMissingCurrents: true);

            return new ActorRuntimeStatSeed
            {
                Attributes = attributes,
                Skills = skills,
                Vitals = vitals,
                EffectModifiers = new ActorEffectStatModifiers(),
            };
        }

        public static ActorIdentitySet CreateIdentityFromActor(ref RuntimeActorDefBlob actor)
        {
            string id = actor.Id.ToString();
            string name = actor.Name.ToString();
            string characterName = string.IsNullOrWhiteSpace(name)
                ? (string.Equals(id, "player", StringComparison.OrdinalIgnoreCase) ? "Player" : id)
                : name;

            return new ActorIdentitySet
            {
                CharacterName = RuntimeFixedStringUtility.ToFixed64OrDefaultWhiteSpace(characterName),
                Level = math.max(1, actor.Level),
                RaceName = RuntimeFixedStringUtility.ToFixed64OrDefaultWhiteSpace(actor.RaceId.ToString()),
                ClassName = RuntimeFixedStringUtility.ToFixed64OrDefaultWhiteSpace(actor.ClassId.ToString()),
                BirthSignName = default,
                Reputation = actor.Reputation,
            };
        }

        public static ActorKnownSpell[] BuildKnownSpellListFromActor(ref RuntimeContentBlob content, ActorDefHandle actorHandle)
        {
            if (!actorHandle.IsValid)
                return Array.Empty<ActorKnownSpell>();

            ref RuntimeActorDefBlob actor = ref RuntimeContentBlobUtility.Get(ref content, actorHandle);
            RuntimeContentBlobUtility.RequireRange(actor.FirstSpellIndex, actor.SpellCount, content.ActorSpells.Length, "actor spell");

            GenericRecordDefHandle raceHandle = default;
            bool hasRacePowers = false;
            if (actor.RaceIdHash != 0UL
                && RuntimeContentBlobUtility.TryGetRaceHandleByIdHash(ref content, actor.RaceIdHash, out raceHandle)
                && raceHandle.IsValid)
            {
                ref RuntimeRaceDefBlob race = ref RuntimeContentBlobUtility.GetRace(ref content, raceHandle);
                RuntimeContentBlobUtility.RequireRange(race.FirstPowerSpellIdIndex, race.PowerSpellIdCount, content.RacePowerSpellIds.Length, "race power spell");
                hasRacePowers = race.PowerSpellIdCount > 0;
            }

            if (actor.SpellCount == 0 && !hasRacePowers)
                return Array.Empty<ActorKnownSpell>();

            var results = new List<ActorKnownSpell>(actor.SpellCount + (hasRacePowers ? RuntimeContentBlobUtility.GetRace(ref content, raceHandle).PowerSpellIdCount : 0));
            for (int i = 0; i < actor.SpellCount; i++)
                AddKnownSpell(ref content, content.ActorSpells[actor.FirstSpellIndex + i].SpellIdHash, results);

            if (hasRacePowers)
            {
                ref RuntimeRaceDefBlob race = ref RuntimeContentBlobUtility.GetRace(ref content, raceHandle);
                for (int i = 0; i < race.PowerSpellIdCount; i++)
                    AddKnownSpell(ref content, content.RacePowerSpellIds[race.FirstPowerSpellIdIndex + i].Value.ToString(), results);
            }

            return results.ToArray();
        }

        static void AddKnownSpell(ref RuntimeContentBlob content, string spellId, List<ActorKnownSpell> results)
        {
            if (string.IsNullOrWhiteSpace(spellId)
                || !TryResolveKnownSpell(ref content, RuntimeContentStableHash.HashId(spellId), out SpellDefHandle spellHandle)
                || !spellHandle.IsValid)
            {
                return;
            }

            AddKnownSpell(results, spellHandle);
        }

        static void AddKnownSpell(ref RuntimeContentBlob content, ulong spellIdHash, List<ActorKnownSpell> results)
        {
            if (spellIdHash == 0UL || !TryResolveKnownSpell(ref content, spellIdHash, out SpellDefHandle spellHandle) || !spellHandle.IsValid)
                return;

            AddKnownSpell(results, spellHandle);
        }

        static bool TryResolveKnownSpell(ref RuntimeContentBlob content, ulong spellIdHash, out SpellDefHandle spellHandle)
            => RuntimeContentBlobUtility.TryGetSpellHandleByIdHash(ref content, spellIdHash, out spellHandle);

        static void AddKnownSpell(List<ActorKnownSpell> results, SpellDefHandle spellHandle)
        {
            for (int i = 0; i < results.Count; i++)
            {
                if (results[i].Spell.Value == spellHandle.Value)
                    return;
            }

            results.Add(new ActorKnownSpell { Spell = spellHandle });
        }

        public static bool TryCreatePlayerSeedFromContent(
            ref RuntimeContentBlob content,
            out ActorRuntimeStatSeed stats,
            out ActorIdentitySet identity,
            out ActorKnownSpell[] knownSpells,
            out PlayerInitialInventoryItem[] initialInventory)
        {
            stats = CreateDefaultPlayerSeed(ref content);
            identity = ActorIdentitySet.DefaultPlayer();
            knownSpells = Array.Empty<ActorKnownSpell>();
            initialInventory = Array.Empty<PlayerInitialInventoryItem>();

            if (!RuntimeContentBlobUtility.TryGetActorHandleByIdHash(ref content, RuntimeContentKnownHashes.player, out ActorDefHandle actorHandle)
                || !actorHandle.IsValid)
            {
                return false;
            }

            ref RuntimeActorDefBlob actor = ref RuntimeContentBlobUtility.Get(ref content, actorHandle);
            if (actor.Kind != ActorDefKind.Npc)
                return false;

            stats = HasManualActorStats(ref actor) ? CreateSeedFromActor(ref content, ref actor) : CreateDefaultPlayerSeed(ref content);
            identity = CreateIdentityFromActor(ref actor);
            knownSpells = BuildKnownSpellListFromActor(ref content, actorHandle);
            initialInventory = BuildInitialInventoryListFromActor(ref content, actorHandle);
            return true;
        }

        public static PlayerInitialInventoryItem[] BuildInitialInventoryListFromActor(ref RuntimeContentBlob content, ActorDefHandle actorHandle)
        {
            if (!actorHandle.IsValid)
                return Array.Empty<PlayerInitialInventoryItem>();

            ref RuntimeActorDefBlob actor = ref RuntimeContentBlobUtility.Get(ref content, actorHandle);
            RuntimeContentBlobUtility.RequireRange(actor.FirstInventoryIndex, actor.InventoryCount, content.ActorInventoryItems.Length, "actor inventory");
            if (actor.InventoryCount == 0)
                return Array.Empty<PlayerInitialInventoryItem>();

            var results = new List<PlayerInitialInventoryItem>(actor.InventoryCount);
            for (int i = 0; i < actor.InventoryCount; i++)
            {
                ref RuntimeContainerItemDefBlob item = ref content.ActorInventoryItems[actor.FirstInventoryIndex + i];
                if (item.Count <= 0)
                    continue;
                if (item.ItemIdHash == 0UL)
                    throw new InvalidOperationException($"[VVardenfell][Player] actor '{actor.Id.ToString()}' has an authored inventory item with no id at offset {i}.");
                if (!RuntimeContentBlobUtility.TryResolvePlaceableByIdHash(ref content, item.ItemIdHash, out ContentReference contentRef) || !contentRef.IsValid)
                    throw new InvalidOperationException($"[VVardenfell][Player] actor '{actor.Id.ToString()}' references unresolved inventory item '{item.ItemId.ToString()}'.");

                results.Add(new PlayerInitialInventoryItem
                {
                    Content = contentRef,
                    Count = item.Count,
                });
            }

            return results.ToArray();
        }

        static bool HasManualActorStats(ref RuntimeActorDefBlob actor)
        {
            if (actor.AutoCalculatedStats != 0)
                return false;

            return HasAnyAttribute(actor.Attributes)
                && HasAnySkill(actor.Skills)
                && (actor.Vitals.Health > 0 || actor.Vitals.Magicka > 0 || actor.Vitals.Fatigue > 0);
        }

        public static ActorDerivedMovementStats BuildDerived(
            ref RuntimeContentBlob content,
            in ActorAttributeSet attributes,
            in ActorSkillSet skills,
            in ActorVitalSet vitals,
            in ActorEffectStatModifiers effectModifiers,
            float inventoryWeight)
        {
            MovementGmstSet gmsts = LoadGmsts(ref content);
            var derived = new ActorDerivedMovementStats
            {
                CarryCapacity = ComputeCarryCapacity(in gmsts, attributes),
                Encumbrance = ComputeEncumbrance(effectModifiers, inventoryWeight),
            };
            derived.NormalizedEncumbrance = ComputeNormalizedEncumbrance(derived.Encumbrance, derived.CarryCapacity);
            ApplyMovementDerived(in gmsts, vitals, ref derived);
            return derived;
        }

        public static MorrowindMovementSpeed BuildMovementSpeed(
            ref RuntimeContentBlob content,
            ActorDefKind actorKind,
            in ActorAttributeSet attributes,
            in ActorSkillSet skills,
            in ActorVitalSet vitals,
            in ActorEffectStatModifiers effectModifiers,
            in ActorDerivedMovementStats derived)
        {
            MovementGmstSet gmsts = LoadGmsts(ref content);
            return BuildMovementSpeed(in gmsts, actorKind, attributes, skills, vitals, effectModifiers, derived);
        }

        static MorrowindMovementSpeed BuildMovementSpeed(
            in MovementGmstSet gmsts,
            ActorDefKind actorKind,
            in ActorAttributeSet attributes,
            in ActorSkillSet skills,
            in ActorVitalSet vitals,
            in ActorEffectStatModifiers effectModifiers,
            in ActorDerivedMovementStats derived)
        {
            float jumpSpeed = ComputeJumpSpeed(in gmsts, skills, effectModifiers, derived);
            float jumpMoveFactor = ComputeJumpMoveFactor(in gmsts, skills);
            if (actorKind == ActorDefKind.Creature)
            {
                float walkSpeed = gmsts.MinWalkSpeedCreature
                    + 0.01f * attributes.Speed * (gmsts.MaxWalkSpeedCreature - gmsts.MinWalkSpeedCreature);

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

            float walkSpeedNpc = ComputeWalkSpeed(in gmsts, attributes, derived);
            float runSpeedNpc = walkSpeedNpc * (0.01f * skills.Athletics * gmsts.AthleticsRunBonus + gmsts.BaseRunMultiplier);
            float sneakWalkSpeedNpc = walkSpeedNpc * gmsts.SneakSpeedMultiplier;

            return new MorrowindMovementSpeed
            {
                WalkSpeed = walkSpeedNpc,
                RunSpeed = math.max(0f, runSpeedNpc),
                SneakWalkSpeed = math.max(0f, sneakWalkSpeedNpc),
                JumpSpeed = jumpSpeed,
                JumpRunMultiplier = gmsts.JumpRunMultiplier,
                JumpMoveFactor = jumpMoveFactor,
            };
        }

        public static MorrowindMovementSpeed BuildPlayerMovementSpeed(
            ref RuntimeContentBlob content,
            in ActorAttributeSet attributes,
            in ActorSkillSet skills,
            in ActorVitalSet vitals,
            in ActorEffectStatModifiers effectModifiers,
            in ActorDerivedMovementStats derived)
            => BuildMovementSpeed(ref content, ActorDefKind.Npc, attributes, skills, vitals, effectModifiers, derived);

        public static float ComputeModifiedFatigueBase(in ActorAttributeSet attributes)
            => math.max(0f, attributes.Strength + attributes.Willpower + attributes.Agility + attributes.Endurance);

        public static float ComputeModifiedHealthBase(in ActorAttributeSet attributes)
            => math.max(1f, (attributes.Strength + attributes.Endurance) * 0.5f);

        public static float ComputeModifiedMagickaBase(ref RuntimeContentBlob content, in ActorAttributeSet attributes)
            => math.max(0f, attributes.Intelligence * Gmst(ref content, RuntimeContentKnownHashes.fPCbaseMagickaMult));

        public static float ComputeModifiedMagickaBase(
            ref RuntimeContentBlob content,
            in ActorAttributeSet attributes,
            in ActorEffectStatModifiers effectModifiers)
            => math.max(0f, attributes.Intelligence * (Gmst(ref content, RuntimeContentKnownHashes.fPCbaseMagickaMult) + effectModifiers.FortifyMaximumMagickaMagnitude * 0.1f));

        public static void ApplyVitalBases(
            ref RuntimeContentBlob content,
            in ActorAttributeSet attributes,
            ref ActorVitalSet vitals,
            bool initializeMissingCurrents)
        {
            vitals.ModifiedHealthBase = ComputeModifiedHealthBase(attributes);
            vitals.ModifiedMagickaBase = ComputeModifiedMagickaBase(ref content, attributes);
            vitals.ModifiedFatigueBase = ComputeModifiedFatigueBase(attributes);
            ClampVitals(ref vitals, initializeMissingCurrents);
        }

        public static void ApplyVitalBases(
            ref RuntimeContentBlob content,
            in ActorAttributeSet attributes,
            in ActorEffectStatModifiers effectModifiers,
            ref ActorVitalSet vitals,
            bool initializeMissingCurrents)
        {
            vitals.ModifiedHealthBase = ComputeModifiedHealthBase(attributes);
            vitals.ModifiedMagickaBase = ComputeModifiedMagickaBase(ref content, attributes, effectModifiers);
            vitals.ModifiedFatigueBase = ComputeModifiedFatigueBase(attributes);
            ClampVitals(ref vitals, initializeMissingCurrents);
        }

        public static float ComputeCarryCapacity(ref RuntimeContentBlob content, in ActorAttributeSet attributes)
            => math.max(0f, attributes.Strength * Gmst(ref content, RuntimeContentKnownHashes.fEncumbranceStrMult));

        static float ComputeCarryCapacity(in MovementGmstSet gmsts, in ActorAttributeSet attributes)
            => math.max(0f, attributes.Strength * gmsts.EncumbranceStrMult);

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
            ref RuntimeContentBlob content,
            in ActorAttributeSet attributes,
            in ActorSkillSet skills,
            in ActorVitalSet vitals,
            in ActorEffectStatModifiers effectModifiers,
            ref ActorDerivedMovementStats derived)
        {
            MovementGmstSet gmsts = LoadGmsts(ref content);
            ApplyMovementDerived(in gmsts, vitals, ref derived);
        }

        static void ApplyMovementDerived(in MovementGmstSet gmsts, in ActorVitalSet vitals, ref ActorDerivedMovementStats derived)
        {
            float modifiedFatigueBase = math.max(0f, vitals.ModifiedFatigueBase);
            float normalizedFatigue = modifiedFatigueBase <= 0f
                ? 1f
                : math.max(0f, vitals.CurrentFatigue / modifiedFatigueBase);
            derived.FatigueTerm = gmsts.FatigueBase - gmsts.FatigueMult * (1f - normalizedFatigue);
        }

        public static Context Build(
            ref RuntimeContentBlob content,
            in ActorAttributeSet attributes,
            in ActorSkillSet skills,
            in ActorVitalSet vitals,
            in ActorEffectStatModifiers effectModifiers,
            in ActorDerivedMovementStats derived,
            in MorrowindMovementSpeed speed)
        {
            MovementGmstSet gmsts = LoadGmsts(ref content);
            return new Context(in gmsts, attributes, skills, vitals, effectModifiers, derived, speed);
        }

        public static string DescribeRuntimeState(
            ref RuntimeContentBlob content,
            in ActorAttributeSet attributes,
            in ActorSkillSet skills,
            in ActorVitalSet vitals,
            in ActorEffectStatModifiers effectModifiers,
            in ActorDerivedMovementStats derived)
        {
            var speed = BuildPlayerMovementSpeed(ref content, attributes, skills, vitals, effectModifiers, derived);
            var context = Build(ref content, attributes, skills, vitals, effectModifiers, derived, speed);
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
            builder.Append(" gmst=blob");
            return builder.ToString();
        }

        static float ComputeWalkSpeed(in MovementGmstSet gmsts, in ActorAttributeSet attributes, in ActorDerivedMovementStats derived)
        {
            float walkSpeed = gmsts.MinWalkSpeed + 0.01f * attributes.Speed * (gmsts.MaxWalkSpeed - gmsts.MinWalkSpeed);
            walkSpeed *= 1f - gmsts.EncumberedMoveEffect * derived.NormalizedEncumbrance;
            return math.max(0f, walkSpeed);
        }

        static float ComputeJumpSpeed(
            in MovementGmstSet gmsts,
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

            float jump = gmsts.JumpAcrobaticsBase + math.pow(a / 15f, gmsts.JumpAcroMultiplier);
            jump += 3f * b * gmsts.JumpAcroMultiplier;
            jump += effectModifiers.JumpMagnitude * 64f;
            jump *= gmsts.JumpEncumbranceBase + gmsts.JumpEncumbranceMultiplier * (1f - derived.NormalizedEncumbrance);
            jump *= derived.FatigueTerm;
            jump += 8.96f * (1f / WorldScale.MwUnitsToMeters);
            jump /= 3f;
            return math.max(0f, jump);
        }

        static float ComputeJumpMoveFactor(in MovementGmstSet gmsts, in ActorSkillSet skills)
            => math.min(1f, gmsts.JumpMoveBase + gmsts.JumpMoveMult * skills.Acrobatics / 100f);

        static float Gmst(ref RuntimeContentBlob content, ulong idHash)
            => RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, idHash);

        static void ClampVitals(ref ActorVitalSet vitals, bool initializeMissingCurrents)
        {
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

        static float ClampVital(float current, float max)
        {
            if (max <= 0f)
                return 0f;
            if (float.IsNaN(current) || float.IsInfinity(current))
                return max;
            return math.clamp(current, 0f, max);
        }

        static ActorAttributeSet DefaultPlayerAttributes()
        {
            return new ActorAttributeSet
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
        }

        static ActorSkillSet DefaultPlayerSkills()
        {
            return new ActorSkillSet
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
            };
        }

        static ActorAttributeSet ToAttributeSet(in ActorAttributeDef attributes)
        {
            return new ActorAttributeSet
            {
                Strength = attributes.Strength,
                Intelligence = attributes.Intelligence,
                Willpower = attributes.Willpower,
                Agility = attributes.Agility,
                Speed = attributes.Speed,
                Endurance = attributes.Endurance,
                Personality = attributes.Personality,
                Luck = attributes.Luck,
            };
        }

        static ActorSkillSet ToSkillSet(in ActorSkillDef skills)
        {
            return new ActorSkillSet
            {
                Block = skills.Block,
                Armorer = skills.Armorer,
                MediumArmor = skills.MediumArmor,
                HeavyArmor = skills.HeavyArmor,
                BluntWeapon = skills.BluntWeapon,
                LongBlade = skills.LongBlade,
                Axe = skills.Axe,
                Spear = skills.Spear,
                Athletics = skills.Athletics,
                Enchant = skills.Enchant,
                Destruction = skills.Destruction,
                Alteration = skills.Alteration,
                Illusion = skills.Illusion,
                Conjuration = skills.Conjuration,
                Mysticism = skills.Mysticism,
                Restoration = skills.Restoration,
                Alchemy = skills.Alchemy,
                Unarmored = skills.Unarmored,
                Security = skills.Security,
                Sneak = skills.Sneak,
                Acrobatics = skills.Acrobatics,
                LightArmor = skills.LightArmor,
                ShortBlade = skills.ShortBlade,
                Marksman = skills.Marksman,
                Mercantile = skills.Mercantile,
                Speechcraft = skills.Speechcraft,
                HandToHand = skills.HandToHand,
            };
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
    }

    public static partial class MorrowindPlayerSpeedResolver
    {
        public static MorrowindActorMovementStats.Context Build(
            ref RuntimeContentBlob content,
            in ActorAttributeSet attributes,
            in ActorSkillSet skills,
            in ActorVitalSet vitals,
            in ActorEffectStatModifiers effectModifiers,
            in ActorDerivedMovementStats derived,
            in MorrowindMovementSpeed speed)
            => MorrowindActorMovementStats.Build(ref content, attributes, skills, vitals, effectModifiers, derived, speed);

        public static string DescribeRuntimeState(
            ref RuntimeContentBlob content,
            in ActorAttributeSet attributes,
            in ActorSkillSet skills,
            in ActorVitalSet vitals,
            in ActorEffectStatModifiers effectModifiers,
            in ActorDerivedMovementStats derived)
            => MorrowindActorMovementStats.DescribeRuntimeState(ref content, attributes, skills, vitals, effectModifiers, derived);
    }
}

