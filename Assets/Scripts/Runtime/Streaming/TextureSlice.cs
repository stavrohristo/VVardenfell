using Unity.Entities;
using Unity.Rendering;

namespace VVardenfell.Runtime.Streaming
{
    /// <summary>
    /// Per-render-leaf slice index into the shared <c>_BaseArray</c> Texture2DArray.
    ///
    /// The <see cref="MaterialPropertyAttribute"/> tells Entities.Graphics to copy this
    /// component's value into the per-instance <c>_Slice</c> uniform before each draw.
    /// The shader reads it via <c>UNITY_ACCESS_DOTS_INSTANCED_PROP(float, _Slice)</c>.
    /// Floats exactly represent integers up to 2^24 — well past our slice count.
    /// </summary>
    [MaterialProperty("_Slice")]
    public struct TextureSlice : IComponentData
    {
        public float Value;
    }
}
