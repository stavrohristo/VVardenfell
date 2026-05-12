using Unity.Entities;
using Unity.Rendering;

namespace VVardenfell.Runtime.Streaming
{
    /// <summary>
    /// Per-render-leaf slice index into the shared <c>_BaseArray</c> Texture2DArray.
    /// </summary>
    [MaterialProperty("_Slice")]
    public struct TextureSlice : IComponentData
    {
        public float Value;
    }
}
