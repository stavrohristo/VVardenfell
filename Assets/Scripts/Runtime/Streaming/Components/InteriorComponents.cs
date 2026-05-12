using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;

namespace VVardenfell.Runtime.Components
{
    public struct InteriorTransitionState : IComponentData
    {
        public byte InteriorActive;
        public byte TransitionInProgress;
        public FixedString128Bytes ActiveInteriorCellId;
        public ulong ActiveInteriorCellHash;
    }

    public struct InteriorSpawnedEntity : IBufferElementData
    {
        // Transient runtime-spawned interior entities only. Baked resident interior
        // section entities are owned by RuntimeSectionRegistry and must not be added here.
        public Entity Value;
    }
}
