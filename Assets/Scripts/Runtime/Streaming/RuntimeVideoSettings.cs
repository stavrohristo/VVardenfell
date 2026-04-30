using Unity.Entities;
using Unity.Mathematics;

namespace VVardenfell.Runtime.Streaming
{
    public struct RuntimeVideoSettings : IComponentData
    {
        public const float DefaultFogDistanceScale = 0.15f;
        public const float MinFogDistanceScale = 0.05f;
        public const float MaxFogDistanceScale = 1.25f;

        public float FogDistanceScale;

        public static RuntimeVideoSettings CreateDefault()
        {
            return new RuntimeVideoSettings
            {
                FogDistanceScale = DefaultFogDistanceScale,
            };
        }

        public static float NormalizeFogDistanceScale(float value)
        {
            return math.clamp(
                value > 0f ? value : DefaultFogDistanceScale,
                MinFogDistanceScale,
                MaxFogDistanceScale);
        }
    }
}
