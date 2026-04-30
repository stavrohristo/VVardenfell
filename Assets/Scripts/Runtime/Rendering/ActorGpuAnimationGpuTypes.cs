using Unity.Mathematics;

namespace VVardenfell.Runtime.Rendering
{
    public struct ActorGpuAnimationVertexGpu
    {
        public float3 Position;
        public float3 Normal;
        public float2 Uv;
        public int4 BoneIndices0;
        public int4 BoneIndices1;
        public float4 Weights0;
        public float4 Weights1;
    }

    public struct ActorGpuAnimationMatrixGpu
    {
        public float4 Row0;
        public float4 Row1;
        public float4 Row2;
    }
}
