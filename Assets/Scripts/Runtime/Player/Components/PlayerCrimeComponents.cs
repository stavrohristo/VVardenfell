using Unity.Entities;

namespace VVardenfell.Runtime.Components
{
    public struct PlayerCrimeState : IComponentData
    {
        public int Bounty;
        public int CurrentCrimeId;
        public int PaidCrimeId;

        public static PlayerCrimeState Default => new()
        {
            CurrentCrimeId = -1,
            PaidCrimeId = -1,
        };
    }
}
