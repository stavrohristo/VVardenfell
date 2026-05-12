using Unity.Entities;

namespace VVardenfell.Runtime.AI
{
    public enum ActorAiGreetingPhase : byte
    {
        None = 0,
        InProgress = 1,
        Done = 2,
    }

    public struct ActorAiGreetingState : IComponentData
    {
        public float Timer;
        public byte Phase;
    }
}
