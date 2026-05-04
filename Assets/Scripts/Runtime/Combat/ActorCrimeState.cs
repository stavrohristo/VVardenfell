using Unity.Entities;

namespace VVardenfell.Runtime.Combat
{
    public struct ActorCrimeState : IComponentData
    {
        public int CrimeId;

        public static ActorCrimeState Default => new()
        {
            CrimeId = -1,
        };
    }
}
