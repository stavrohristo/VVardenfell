using Unity.Entities;

namespace VVardenfell.Runtime.Components
{
    public enum ActorAttributeKind : byte
    {
        None = 0,
        Strength = 1,
        Intelligence = 2,
        Willpower = 3,
        Agility = 4,
        Speed = 5,
        Endurance = 6,
        Personality = 7,
        Luck = 8,
    }

    public enum ActorAttributeMutationKind : byte
    {
        Set = 1,
        Mod = 2,
    }

    public struct ActorAttributeMutationRequest : IBufferElementData
    {
        public Entity TargetEntity;
        public uint TargetPlacedRefId;
        public float Value;
        public byte Attribute;
        public byte Kind;
    }
}
