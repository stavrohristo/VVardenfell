using Unity.Entities;
using Unity.Mathematics;

namespace VVardenfell.Runtime.Components
{
    public struct PlayerPhysicsViewPose : IComponentData
    {
        public float3 Position;
        public quaternion Rotation;
        public uint FixedTick;
    }
}
