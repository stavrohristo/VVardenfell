using Unity.Entities;

namespace VVardenfell.Runtime.Components
{
    public struct RuntimeEnemyHealthBarState : IComponentData
    {
        public Entity Target;
        public uint TargetPlacedRefId;
        public float SecondsRemaining;
        public float FadeSeconds;
        public float LastKnownHealthFill;
        public byte Visible;
    }
}
