using Unity.Collections;
using Unity.Entities;

namespace VVardenfell.Runtime.Components
{
    public struct ActorHitAftermathState : IComponentData
    {
        public byte HitRecovery;
        public byte KnockedDown;
        public byte KnockedDownOneFrame;
        public byte KnockedDownOverOneFrame;
        public byte KnockedOut;
        public byte DeathAnimationFinished;
        public uint Sequence;
        public uint AnimatedSequence;
        public FixedString64Bytes DeathAnimationGroup;
    }
}
