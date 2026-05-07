using Unity.Mathematics;

namespace VVardenfell.Runtime.Player
{
    public static class CharacterGenerationRacePreviewRuntimeUtility
    {
        public static readonly float3 Position = new(50020f, 50000f, 50000f);
        public static readonly quaternion Rotation = quaternion.identity;
        public static readonly float3 LookAt = Position + new float3(0f, 1.08f, 0f);
        public static readonly float3 CameraOffset = new(0f, 0f, 3.2f);
    }
}
