using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;

namespace VVardenfell.Runtime.Components
{    public struct InteriorTransitionState : IComponentData
    {
        public byte InteriorActive;
        public byte TransitionInProgress;
        public FixedString128Bytes ActiveInteriorCellId;
    }

    public struct InteriorCellMember : IComponentData
    {
    }

    public struct InteriorSpawnedEntity : IBufferElementData
    {
        public Entity Value;
    }
}
