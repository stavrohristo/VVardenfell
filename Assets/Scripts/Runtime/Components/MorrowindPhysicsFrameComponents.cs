using Unity.Entities;

namespace VVardenfell.Runtime.Components
{
    public struct MorrowindPhysicsFrameState : IComponentData
    {
        public uint FixedTick;
        public uint SnapshotTick;
        public uint BuildSequence;
        public uint QuerySequence;
        public byte AutoPhysicsDisabled;
        public byte BootLogged;
    }
}
