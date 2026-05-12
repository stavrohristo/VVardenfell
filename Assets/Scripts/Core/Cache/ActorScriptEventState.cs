using Unity.Entities;
using VVardenfell.Core.Cache;

namespace VVardenfell.Runtime.Components
{
    public struct ActorScriptEventState : IComponentData
    {
        public byte Murdered;
        public byte Attacked;
        public byte KnockedDownOneFrame;
        public Entity LastHitAttemptActor;
        public uint LastHitAttemptActorPlacedRefId;
        public ContentReference LastHitAttemptObject;
        public ContentReference LastHitObject;
    }
}
