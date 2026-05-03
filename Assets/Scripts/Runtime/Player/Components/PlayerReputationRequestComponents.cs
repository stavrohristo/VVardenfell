using Unity.Entities;

namespace VVardenfell.Runtime.Components
{
    public struct PlayerReputationMutationRequest : IBufferElementData
    {
        public int Delta;
    }
}
