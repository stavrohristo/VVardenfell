using Unity.Entities;

namespace VVardenfell.Runtime.Components
{
    public enum PlayerSkillMutationKind : byte
    {
        Set = 1,
        Mod = 2,
    }

    public struct PlayerSkillMutationRequest : IBufferElementData
    {
        public float Value;
        public byte Skill;
        public byte Kind;
    }
}
