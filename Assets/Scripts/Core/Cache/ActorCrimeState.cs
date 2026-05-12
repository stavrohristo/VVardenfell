using Unity.Entities;

namespace VVardenfell.Runtime.Combat
{
    public struct ActorCrimeState : IComponentData
    {
        public int CrimeId;
        public int CrimeDispositionModifier;
        public byte Alarmed;

        public static ActorCrimeState Default => new()
        {
            CrimeId = -1,
        };
    }
}
