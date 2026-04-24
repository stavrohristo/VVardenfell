using Unity.Entities;
using Unity.Mathematics;
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
        public readonly float3 Position;
        public readonly quaternion Rotation;
        public readonly float2 CellNormalizedPosition;
        public readonly int2 ExteriorCell;
        public readonly float HeadingDegrees;
        public readonly float HealthFill;
        public readonly float MagickaFill;
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
            float3 position,
            quaternion rotation,
            float2 cellNormalizedPosition,
            int2 exteriorCell,
            float headingDegrees,
            float healthFill,
            float magickaFill,
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
            Position = position;
            Rotation = rotation;
            CellNormalizedPosition = cellNormalizedPosition;
            ExteriorCell = exteriorCell;
            HeadingDegrees = headingDegrees;
            HealthFill = healthFill;
            MagickaFill = magickaFill;
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
