using System;
using System.Text;
using Unity.Collections;
using Unity.Mathematics;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Magic;

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
            return ActorMagicStatUtility.InitializeAuthoritativeState(seed);
        }

        public static ActorRuntimeStatSeed CreateSeedFromActor(ref RuntimeContentBlob content, ref RuntimeActorDefBlob actor)
        {
            if (actor.Kind == ActorDefKind.Npc && actor.AutoCalculatedStats != 0)
                return CreateAutoCalculatedNpcSeed(ref content, ref actor);

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

            return ActorMagicStatUtility.InitializeAuthoritativeState(new ActorRuntimeStatSeed
            {
                Attributes = attributes,
                Skills = skills,
                Vitals = vitals,
                EffectModifiers = new ActorEffectStatModifiers(),
            });
        }

        static ActorRuntimeStatSeed CreateAutoCalculatedNpcSeed(ref RuntimeContentBlob content, ref RuntimeActorDefBlob actor)
        {
            if (actor.RaceIdHash == 0UL)
                throw new InvalidOperationException($"[VVardenfell][Movement] auto-calculated NPC '{actor.Id}' has no race.");
            if (actor.ClassIdHash == 0UL)
                throw new InvalidOperationException($"[VVardenfell][Movement] auto-calculated NPC '{actor.Id}' has no class.");
            if (!RuntimeContentBlobUtility.TryGetRaceHandleByIdHash(ref content, actor.RaceIdHash, out var raceHandle) || !raceHandle.IsValid)
                throw new InvalidOperationException($"[VVardenfell][Movement] auto-calculated NPC '{actor.Id}' references unresolved race '{actor.RaceId}'.");
            if (!RuntimeContentBlobUtility.TryGetClassHandleByIdHash(ref content, actor.ClassIdHash, out var classHandle) || !classHandle.IsValid)
                throw new InvalidOperationException($"[VVardenfell][Movement] auto-calculated NPC '{actor.Id}' references unresolved class '{actor.ClassId}'.");

            ref RuntimeRaceDefBlob race = ref RuntimeContentBlobUtility.GetRace(ref content, raceHandle);
            ref RuntimeClassDefBlob npcClass = ref RuntimeContentBlobUtility.GetClass(ref content, classHandle);
            var attributes = AutoCalculateNpcAttributes(ref content, ref actor, ref race, ref npcClass);
            var skills = AutoCalculateNpcSkills(ref content, ref actor, ref race, ref npcClass);
            var vitals = AutoCalculateNpcVitals(ref content, actor.Level, npcClass.Specialization, in attributes, ref npcClass);

            return ActorMagicStatUtility.InitializeAuthoritativeState(new ActorRuntimeStatSeed
            {
                Attributes = attributes,
                Skills = skills,
                Vitals = vitals,
                EffectModifiers = new ActorEffectStatModifiers(),
            });
        }

        public static ActorIdentitySet CreateIdentityFromActor(ref RuntimeActorDefBlob actor)
        {
            FixedString64Bytes characterName = RuntimeFixedStringUtility.ToFixed64OrDefault(ref actor.Name);
            if (characterName.IsEmpty)
                characterName = actor.IdHash == RuntimeContentKnownHashes.player
                    ? new FixedString64Bytes("Player")
                    : RuntimeFixedStringUtility.ToFixed64OrDefault(ref actor.Id);

            return new ActorIdentitySet
            {
                CharacterName = characterName,
                Level = math.max(1, actor.Level),
                RaceName = RuntimeFixedStringUtility.ToFixed64OrDefault(ref actor.RaceId),
                ClassName = RuntimeFixedStringUtility.ToFixed64OrDefault(ref actor.ClassId),
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

            int maxSpellCount = actor.SpellCount + (hasRacePowers ? RuntimeContentBlobUtility.GetRace(ref content, raceHandle).PowerSpellIdCount : 0);
            var results = new ActorKnownSpell[maxSpellCount];
            int resultCount = 0;
            for (int i = 0; i < actor.SpellCount; i++)
                AddKnownSpell(ref content, content.ActorSpells[actor.FirstSpellIndex + i].SpellIdHash, results, ref resultCount);

            if (hasRacePowers)
            {
                ref RuntimeRaceDefBlob race = ref RuntimeContentBlobUtility.GetRace(ref content, raceHandle);
                for (int i = 0; i < race.PowerSpellIdCount; i++)
                {
                    FixedString128Bytes spellId = RuntimeFixedStringUtility.ToFixed128OrDefault(ref content.RacePowerSpellIds[race.FirstPowerSpellIdIndex + i].Value);
                    AddKnownSpell(ref content, RuntimeContentStableHash.HashId(spellId), results, ref resultCount);
                }
            }

            if (resultCount == results.Length)
                return results;

            var compact = new ActorKnownSpell[resultCount];
            if (resultCount > 0)
                Array.Copy(results, compact, resultCount);
            return compact;
        }

        static void AddKnownSpell(ref RuntimeContentBlob content, ulong spellIdHash, ActorKnownSpell[] results, ref int resultCount)
        {
            if (spellIdHash == 0UL || !TryResolveKnownSpell(ref content, spellIdHash, out SpellDefHandle spellHandle) || !spellHandle.IsValid)
                return;

            AddKnownSpell(results, ref resultCount, spellHandle);
        }

        static bool TryResolveKnownSpell(ref RuntimeContentBlob content, ulong spellIdHash, out SpellDefHandle spellHandle)
            => RuntimeContentBlobUtility.TryGetSpellHandleByIdHash(ref content, spellIdHash, out spellHandle);

        static void AddKnownSpell(ActorKnownSpell[] results, ref int resultCount, SpellDefHandle spellHandle)
        {
            for (int i = 0; i < resultCount; i++)
            {
                if (results[i].Spell.Value == spellHandle.Value)
                    return;
            }

            if ((uint)resultCount >= (uint)results.Length)
                throw new InvalidOperationException("[VVardenfell][Movement] known spell result buffer overflow.");

            results[resultCount++] = new ActorKnownSpell { Spell = spellHandle };
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

            var results = new PlayerInitialInventoryItem[actor.InventoryCount];
            int resultCount = 0;
            for (int i = 0; i < actor.InventoryCount; i++)
            {
                ref RuntimeContainerItemDefBlob item = ref content.ActorInventoryItems[actor.FirstInventoryIndex + i];
                if (item.Count <= 0)
                    continue;
                if (item.ItemIdHash == 0UL)
                    throw new InvalidOperationException($"[VVardenfell][Player] actor hash {actor.IdHash} has an authored inventory item with no id at offset {i}.");
                if (!RuntimeContentBlobUtility.TryResolvePlaceableByIdHash(ref content, item.ItemIdHash, out ContentReference contentRef) || !contentRef.IsValid)
                    throw new InvalidOperationException($"[VVardenfell][Player] actor hash {actor.IdHash} references unresolved inventory item hash {item.ItemIdHash}.");

                results[resultCount++] = new PlayerInitialInventoryItem
                {
                    Content = contentRef,
                    Count = item.Count,
                };
            }

            if (resultCount == results.Length)
                return results;

            var compact = new PlayerInitialInventoryItem[resultCount];
            if (resultCount > 0)
                Array.Copy(results, compact, resultCount);
            return compact;
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

        static ActorAttributeSet AutoCalculateNpcAttributes(
            ref RuntimeContentBlob content,
            ref RuntimeActorDefBlob actor,
            ref RuntimeRaceDefBlob race,
            ref RuntimeClassDefBlob npcClass)
        {
            bool male = (actor.Flags & 1u) == 0u;
            var attributes = BuildRaceAttributes(ref content, ref race, male);
            AddClassAttributeBonus(ref attributes, npcClass.FavoredAttribute0);
            AddClassAttributeBonus(ref attributes, npcClass.FavoredAttribute1);

            int level = actor.Level;
            for (int attributeIndex = 0; attributeIndex < 8; attributeIndex++)
            {
                float modifierSum = 0f;
                for (int skillIndex = 0; skillIndex < 27; skillIndex++)
                {
                    if (ResolveSkillAttributeIndex(ref content, skillIndex) != attributeIndex)
                        continue;

                    float add = 0.2f;
                    if (ClassHasMinorSkill(ref content, ref npcClass, skillIndex))
                        add = 0.5f;
                    if (ClassHasMajorSkill(ref content, ref npcClass, skillIndex))
                        add = 1f;
                    modifierSum += add;
                }

                SetAttributeByIndex(
                    ref attributes,
                    attributeIndex,
                    math.min(RoundIeee754(GetAttributeByIndex(attributes, attributeIndex) + (level - 1) * modifierSum), 100f));
            }

            return attributes;
        }

        static ActorSkillSet AutoCalculateNpcSkills(
            ref RuntimeContentBlob content,
            ref RuntimeActorDefBlob actor,
            ref RuntimeRaceDefBlob race,
            ref RuntimeClassDefBlob npcClass)
        {
            var skills = new ActorSkillSet();
            AddClassSkillBonuses(ref content, ref npcClass, ref skills);

            int level = actor.Level;
            for (int skillIndex = 0; skillIndex < 27; skillIndex++)
            {
                float majorMultiplier = ClassHasAnySkill(ref content, ref npcClass, skillIndex) ? 1f : 0.1f;
                float specMultiplier = 0f;
                int specBonus = 0;
                if (ResolveSkillSpecialization(ref content, skillIndex) == npcClass.Specialization)
                {
                    specMultiplier = 0.5f;
                    specBonus = 5;
                }

                float value = GetSkillByIndex(skills, skillIndex)
                              + 5f
                              + ResolveRaceSkillBonus(ref content, ref race, skillIndex)
                              + specBonus
                              + (level - 1) * (majorMultiplier + specMultiplier);
                SetSkillByIndex(ref skills, skillIndex, math.min(RoundIeee754(value), 100f));
            }

            return skills;
        }

        static ActorVitalSet AutoCalculateNpcVitals(
            ref RuntimeContentBlob content,
            int level,
            int classSpecialization,
            in ActorAttributeSet attributes,
            ref RuntimeClassDefBlob npcClass)
        {
            int multiplier = 3;
            if (classSpecialization == 0)
                multiplier += 2;
            else if (classSpecialization == 2)
                multiplier += 1;
            if (npcClass.FavoredAttribute0 == 5 || npcClass.FavoredAttribute1 == 5)
                multiplier += 1;

            float health = math.floor(0.5f * (attributes.Strength + attributes.Endurance)) + multiplier * (level - 1);
            float magicka = math.max(0f, attributes.Intelligence * RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentStableHash.HashId("fNPCbaseMagickaMult")));
            float fatigue = ComputeModifiedFatigueBase(attributes);
            return new ActorVitalSet
            {
                CurrentHealth = health,
                ModifiedHealthBase = health,
                CurrentMagicka = magicka,
                ModifiedMagickaBase = magicka,
                CurrentFatigue = fatigue,
                ModifiedFatigueBase = fatigue,
            };
        }

        static ActorAttributeSet BuildRaceAttributes(ref RuntimeContentBlob content, ref RuntimeRaceDefBlob race, bool male)
        {
            int first = male ? race.FirstMaleAttributeIndex : race.FirstFemaleAttributeIndex;
            int count = male ? race.MaleAttributeCount : race.FemaleAttributeCount;
            RuntimeContentBlobUtility.RequireRange(first, count, male ? content.RaceMaleAttributes.Length : content.RaceFemaleAttributes.Length, "race attributes");
            if (count < 8)
                throw new InvalidOperationException($"[VVardenfell][Movement] race '{race.Id}' has {count} {(male ? "male" : "female")} attributes; 8 required.");

            if (male)
            {
                return new ActorAttributeSet
                {
                    Strength = content.RaceMaleAttributes[first],
                    Intelligence = content.RaceMaleAttributes[first + 1],
                    Willpower = content.RaceMaleAttributes[first + 2],
                    Agility = content.RaceMaleAttributes[first + 3],
                    Speed = content.RaceMaleAttributes[first + 4],
                    Endurance = content.RaceMaleAttributes[first + 5],
                    Personality = content.RaceMaleAttributes[first + 6],
                    Luck = content.RaceMaleAttributes[first + 7],
                };
            }

            return new ActorAttributeSet
            {
                Strength = content.RaceFemaleAttributes[first],
                Intelligence = content.RaceFemaleAttributes[first + 1],
                Willpower = content.RaceFemaleAttributes[first + 2],
                Agility = content.RaceFemaleAttributes[first + 3],
                Speed = content.RaceFemaleAttributes[first + 4],
                Endurance = content.RaceFemaleAttributes[first + 5],
                Personality = content.RaceFemaleAttributes[first + 6],
                Luck = content.RaceFemaleAttributes[first + 7],
            };
        }

        static void AddClassAttributeBonus(ref ActorAttributeSet attributes, int attributeIndex)
        {
            if (attributeIndex < 0 || attributeIndex >= 8)
                return;
            SetAttributeByIndex(ref attributes, attributeIndex, GetAttributeByIndex(attributes, attributeIndex) + 10f);
        }

        static void AddClassSkillBonuses(ref RuntimeContentBlob content, ref RuntimeClassDefBlob npcClass, ref ActorSkillSet skills)
        {
            RuntimeContentBlobUtility.RequireRange(npcClass.FirstMinorSkillIndex, npcClass.MinorSkillCount, content.ClassMinorSkills.Length, "class minor skills");
            RuntimeContentBlobUtility.RequireRange(npcClass.FirstMajorSkillIndex, npcClass.MajorSkillCount, content.ClassMajorSkills.Length, "class major skills");
            for (int i = 0; i < npcClass.MinorSkillCount; i++)
                AddSkillByIndex(ref skills, content.ClassMinorSkills[npcClass.FirstMinorSkillIndex + i], 10f);
            for (int i = 0; i < npcClass.MajorSkillCount; i++)
                AddSkillByIndex(ref skills, content.ClassMajorSkills[npcClass.FirstMajorSkillIndex + i], 25f);
        }

        static bool ClassHasAnySkill(ref RuntimeContentBlob content, ref RuntimeClassDefBlob npcClass, int skillIndex)
            => ClassHasMinorSkill(ref content, ref npcClass, skillIndex)
               || ClassHasMajorSkill(ref content, ref npcClass, skillIndex);

        static bool ClassHasMinorSkill(ref RuntimeContentBlob content, ref RuntimeClassDefBlob npcClass, int skillIndex)
        {
            RuntimeContentBlobUtility.RequireRange(npcClass.FirstMinorSkillIndex, npcClass.MinorSkillCount, content.ClassMinorSkills.Length, "class minor skills");
            for (int i = 0; i < npcClass.MinorSkillCount; i++)
            {
                if (content.ClassMinorSkills[npcClass.FirstMinorSkillIndex + i] == skillIndex)
                    return true;
            }

            return false;
        }

        static bool ClassHasMajorSkill(ref RuntimeContentBlob content, ref RuntimeClassDefBlob npcClass, int skillIndex)
        {
            RuntimeContentBlobUtility.RequireRange(npcClass.FirstMajorSkillIndex, npcClass.MajorSkillCount, content.ClassMajorSkills.Length, "class major skills");
            for (int i = 0; i < npcClass.MajorSkillCount; i++)
            {
                if (content.ClassMajorSkills[npcClass.FirstMajorSkillIndex + i] == skillIndex)
                    return true;
            }

            return false;
        }

        static int ResolveRaceSkillBonus(ref RuntimeContentBlob content, ref RuntimeRaceDefBlob race, int skillIndex)
        {
            RuntimeContentBlobUtility.RequireRange(race.FirstSkillBonusIndex, race.SkillBonusCount, content.RaceSkillBonuses.Length, "race skill bonus");
            for (int i = 0; i < race.SkillBonusCount; i++)
            {
                var bonus = content.RaceSkillBonuses[race.FirstSkillBonusIndex + i];
                if (bonus.Skill == skillIndex)
                    return bonus.Bonus;
            }

            return 0;
        }

        static int ResolveSkillAttributeIndex(ref RuntimeContentBlob content, int skillIndex)
        {
            if (TryResolveParsedSkillMetadata(ref content, skillIndex, out int attributeIndex, out _))
                return attributeIndex;
            return CanonicalSkillAttributeIndex(skillIndex);
        }

        static int ResolveSkillSpecialization(ref RuntimeContentBlob content, int skillIndex)
        {
            if (TryResolveParsedSkillMetadata(ref content, skillIndex, out _, out int specialization))
                return specialization;
            return CanonicalSkillSpecialization(skillIndex);
        }

        static bool TryResolveParsedSkillMetadata(ref RuntimeContentBlob content, int skillIndex, out int attributeIndex, out int specialization)
        {
            attributeIndex = 0;
            specialization = 0;
            for (int i = 0; i < content.Skills.Length; i++)
            {
                ref RuntimeGenericRecordDefBlob skill = ref content.Skills[i];
                if (skill.Int0 != skillIndex)
                    continue;
                if (skill.Float1 <= 0.5f)
                    return false;
                if (skill.Int1 < 0 || skill.Int1 >= 8)
                    throw new InvalidOperationException($"[VVardenfell][Movement] skill '{skill.Id}' has invalid governing attribute {skill.Int1}.");
                if (skill.Int2 < 0 || skill.Int2 >= 3)
                    throw new InvalidOperationException($"[VVardenfell][Movement] skill '{skill.Id}' has invalid specialization {skill.Int2}.");

                attributeIndex = skill.Int1;
                specialization = skill.Int2;
                return true;
            }

            return false;
        }

        static int CanonicalSkillAttributeIndex(int skillIndex)
            => skillIndex switch
            {
                0 => 3,
                1 => 0,
                2 => 5,
                3 => 5,
                4 => 0,
                5 => 0,
                6 => 0,
                7 => 5,
                8 => 4,
                9 => 1,
                10 => 2,
                11 => 2,
                12 => 6,
                13 => 1,
                14 => 2,
                15 => 2,
                16 => 1,
                17 => 4,
                18 => 1,
                19 => 3,
                20 => 0,
                21 => 3,
                22 => 4,
                23 => 3,
                24 => 6,
                25 => 6,
                26 => 4,
                _ => throw new InvalidOperationException($"[VVardenfell][Movement] invalid skill index {skillIndex}."),
            };

        static int CanonicalSkillSpecialization(int skillIndex)
            => skillIndex switch
            {
                >= 0 and <= 8 => 0,
                >= 9 and <= 17 => 1,
                >= 18 and <= 26 => 2,
                _ => throw new InvalidOperationException($"[VVardenfell][Movement] invalid skill index {skillIndex}."),
            };

        static float RoundIeee754(float value)
        {
            float whole = math.floor(value);
            float fraction = value - whole;
            if (fraction < 0.5f)
                return whole;
            if (fraction > 0.5f)
                return whole + 1f;

            return IsEven(whole) ? whole : whole + 1f;
        }

        static bool IsEven(float value)
        {
            double d = value;
            double half = Math.Truncate(d / 2.0);
            return 2.0 * half == d;
        }

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

        static float GetAttributeByIndex(in ActorAttributeSet attributes, int index)
            => index switch
            {
                0 => attributes.Strength,
                1 => attributes.Intelligence,
                2 => attributes.Willpower,
                3 => attributes.Agility,
                4 => attributes.Speed,
                5 => attributes.Endurance,
                6 => attributes.Personality,
                7 => attributes.Luck,
                _ => throw new InvalidOperationException($"[VVardenfell][Movement] invalid attribute index {index}."),
            };

        static void SetAttributeByIndex(ref ActorAttributeSet attributes, int index, float value)
        {
            switch (index)
            {
                case 0: attributes.Strength = value; break;
                case 1: attributes.Intelligence = value; break;
                case 2: attributes.Willpower = value; break;
                case 3: attributes.Agility = value; break;
                case 4: attributes.Speed = value; break;
                case 5: attributes.Endurance = value; break;
                case 6: attributes.Personality = value; break;
                case 7: attributes.Luck = value; break;
                default: throw new InvalidOperationException($"[VVardenfell][Movement] invalid attribute index {index}.");
            }
        }

        static float GetSkillByIndex(in ActorSkillSet skills, int index)
            => index switch
            {
                0 => skills.Block,
                1 => skills.Armorer,
                2 => skills.MediumArmor,
                3 => skills.HeavyArmor,
                4 => skills.BluntWeapon,
                5 => skills.LongBlade,
                6 => skills.Axe,
                7 => skills.Spear,
                8 => skills.Athletics,
                9 => skills.Enchant,
                10 => skills.Destruction,
                11 => skills.Alteration,
                12 => skills.Illusion,
                13 => skills.Conjuration,
                14 => skills.Mysticism,
                15 => skills.Restoration,
                16 => skills.Alchemy,
                17 => skills.Unarmored,
                18 => skills.Security,
                19 => skills.Sneak,
                20 => skills.Acrobatics,
                21 => skills.LightArmor,
                22 => skills.ShortBlade,
                23 => skills.Marksman,
                24 => skills.Mercantile,
                25 => skills.Speechcraft,
                26 => skills.HandToHand,
                _ => throw new InvalidOperationException($"[VVardenfell][Movement] invalid skill index {index}."),
            };

        static void AddSkillByIndex(ref ActorSkillSet skills, int index, float value)
            => SetSkillByIndex(ref skills, index, GetSkillByIndex(skills, index) + value);

        static void SetSkillByIndex(ref ActorSkillSet skills, int index, float value)
        {
            switch (index)
            {
                case 0: skills.Block = value; break;
                case 1: skills.Armorer = value; break;
                case 2: skills.MediumArmor = value; break;
                case 3: skills.HeavyArmor = value; break;
                case 4: skills.BluntWeapon = value; break;
                case 5: skills.LongBlade = value; break;
                case 6: skills.Axe = value; break;
                case 7: skills.Spear = value; break;
                case 8: skills.Athletics = value; break;
                case 9: skills.Enchant = value; break;
                case 10: skills.Destruction = value; break;
                case 11: skills.Alteration = value; break;
                case 12: skills.Illusion = value; break;
                case 13: skills.Conjuration = value; break;
                case 14: skills.Mysticism = value; break;
                case 15: skills.Restoration = value; break;
                case 16: skills.Alchemy = value; break;
                case 17: skills.Unarmored = value; break;
                case 18: skills.Security = value; break;
                case 19: skills.Sneak = value; break;
                case 20: skills.Acrobatics = value; break;
                case 21: skills.LightArmor = value; break;
                case 22: skills.ShortBlade = value; break;
                case 23: skills.Marksman = value; break;
                case 24: skills.Mercantile = value; break;
                case 25: skills.Speechcraft = value; break;
                case 26: skills.HandToHand = value; break;
                default: throw new InvalidOperationException($"[VVardenfell][Movement] invalid skill index {index}.");
            }
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

