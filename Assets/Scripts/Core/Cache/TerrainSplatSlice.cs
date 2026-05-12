using Unity.Entities;
using Unity.Rendering;

namespace VVardenfell.Runtime.Streaming
{
    [MaterialProperty("_SplatSlice")]
    public struct TerrainSplatSlice : IComponentData
    {
        public float Value;
    }
}
