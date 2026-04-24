using Unity.Entities;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.Shell
{
    readonly struct PlayerPresentationStats
    {
        public readonly bool HasPlayer;
        public readonly Entity PlayerEntity;
        public readonly ActorIdentitySet Identity;
        public readonly ActorAttributeSet Attributes;
        public readonly ActorSkillSet Skills;
        public readonly ActorVitalSet Vitals;
        public readonly ActorDerivedMovementStats DerivedMovement;
        public readonly float FatigueFill;
        public readonly float EncumbranceFill;

        public PlayerPresentationStats(
            bool hasPlayer,
            Entity playerEntity,
            in ActorIdentitySet identity,
            in ActorAttributeSet attributes,
            in ActorSkillSet skills,
            in ActorVitalSet vitals,
            in ActorDerivedMovementStats derivedMovement,
            float fatigueFill,
            float encumbranceFill)
        {
            HasPlayer = hasPlayer;
            PlayerEntity = playerEntity;
            Identity = identity;
            Attributes = attributes;
            Skills = skills;
            Vitals = vitals;
            DerivedMovement = derivedMovement;
            FatigueFill = fatigueFill;
            EncumbranceFill = encumbranceFill;
        }
    }

    struct LocationPresentation
    {
        public bool InteriorActive;
        public string DisplayName;
        public string RegionText;
        public string CellText;
        public string StreamingText;
    }
}

