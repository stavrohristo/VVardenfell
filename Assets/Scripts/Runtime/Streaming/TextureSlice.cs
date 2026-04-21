using Unity.Entities;
using Unity.Rendering;

namespace VVardenfell.Runtime.Streaming
{
    /// <summary>
    /// Per-ref slice index into the shared <c>_BaseArray</c> Texture2DArray. Written by
    /// <see cref="WorldSpawner.SpawnAll"/> from <c>RefEntry.SliceIndex</c>.
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
