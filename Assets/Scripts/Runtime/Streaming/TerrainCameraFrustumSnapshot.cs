using Unity.Entities;
using Unity.Mathematics;

namespace VVardenfell.Runtime.Streaming
{
    public struct TerrainCameraFrustumSnapshot : IComponentData
    {
        public float3 Position;
        public float3 Forward;
        public float3 Right;
        public float3 Up;
        public float VerticalFovRadians;
        public float Aspect;
        public float NearClip;
        public float FarClip;
        public byte Valid;
    }
}
