using Unity.Collections;
using Unity.Entities;
using VVardenfell.Core.Cache;

namespace VVardenfell.Runtime.Components
{
    public struct ActorAttributeSet : IComponentData
    {
        public float Strength;
        public float Intelligence;
        public float Willpower;
        public float Agility;
        public float Speed;
        public float Endurance;
        public float Personality;
        public float Luck;
    }

    public struct ActorSkillSet : IComponentData
    {
        public float Block;
        public float Armorer;
        public float MediumArmor;
        public float HeavyArmor;
        public float BluntWeapon;
        public float LongBlade;
        public float Axe;
        public float Spear;
        public float Athletics;
        public float Enchant;
        public float Destruction;
        public float Alteration;
        public float Illusion;
        public float Conjuration;
        public float Mysticism;
        public float Restoration;
        public float Alchemy;
        public float Unarmored;
        public float Security;
        public float Sneak;
        public float Acrobatics;
        public float LightArmor;
        public float ShortBlade;
        public float Marksman;
        public float Mercantile;
        public float Speechcraft;
        public float HandToHand;
    }

    public struct ActorVitalSet : IComponentData
    {
        public float CurrentHealth;
        public float ModifiedHealthBase;
        public float CurrentMagicka;
        public float ModifiedMagickaBase;
        public float CurrentFatigue;
        public float ModifiedFatigueBase;
    }

    public struct ActorEffectStatModifiers : IComponentData
    {
        public float JumpMagnitude;
        public float FeatherMagnitude;
        public float BurdenMagnitude;
    }

    public struct ActorDispositionState : IComponentData
    {
        public int BaseDisposition;
    }

    public enum ActorActiveMagicEffectSourceKind : byte
    {
        Unknown = 0,
        PassiveSpell = 1,
        TimedSpell = 2,
    }

    public struct ActorActiveMagicEffect : IBufferElementData
    {
        public short EffectId;
        public sbyte Skill;
        public sbyte Attribute;
        public float Magnitude;
        /// <summary>
        /// Permanent effects use -1 for both duration and time-left, matching OpenMW's
        /// active effect convention for effects without an expiry.
        /// </summary>
        public float DurationSeconds;
        public float TimeLeftSeconds;
        public byte Applied;
        public ActorActiveMagicEffectSourceKind SourceKind;
        public FixedString64Bytes SourceName;
        public FixedString64Bytes SourceId;
    }

    public struct ActorDerivedMovementStats : IComponentData
    {
        public float CarryCapacity;
        public float Encumbrance;
        public float NormalizedEncumbrance;
        public float FatigueTerm;
    }

    public struct ActorRuntimeStatSeed
    {
        public ActorAttributeSet Attributes;
        public ActorSkillSet Skills;
        public ActorVitalSet Vitals;
        public ActorEffectStatModifiers EffectModifiers;
    }

    public struct ActorIdentitySet : IComponentData
    {
        public FixedString64Bytes CharacterName;
        public int Level;
        public FixedString64Bytes RaceName;
        public FixedString64Bytes ClassName;
        public FixedString64Bytes BirthSignName;
        public int Reputation;

        public static ActorIdentitySet DefaultPlayer()
        {
            return new ActorIdentitySet
            {
                CharacterName = new FixedString64Bytes("Player"),
                Level = 1,
                RaceName = default,
                ClassName = default,
                BirthSignName = default,
                Reputation = 0,
            };
        }
    }

    public struct ActorKnownSpell : IBufferElementData
    {
        public SpellDefHandle Spell;
    }

    public struct PlayerInitialInventoryItem : IBufferElementData
    {
        public ContentReference Content;
        public int Count;
    }
}
