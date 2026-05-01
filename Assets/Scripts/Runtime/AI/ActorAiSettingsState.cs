using Unity.Entities;

namespace VVardenfell.Runtime.AI
{
    public struct ActorAiSettingsState : IComponentData
    {
        public int Hello;
        public int Fight;
        public int Flee;
        public int Alarm;
    }
}
