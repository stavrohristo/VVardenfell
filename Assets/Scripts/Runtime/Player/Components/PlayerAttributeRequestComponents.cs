using Unity.Entities;

namespace VVardenfell.Runtime.Components
{
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
