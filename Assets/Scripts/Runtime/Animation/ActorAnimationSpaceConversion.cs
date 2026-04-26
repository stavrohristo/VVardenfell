using Unity.Mathematics;
using VVardenfell.Core;

namespace VVardenfell.Runtime.Animation
{
    static class ActorAnimationSpaceConversion
    {
        const float QuaternionEpsilon = 0.000001f;

        public static float3 SourceTranslationToUnity(float3 source)
            => new float3(source.x, source.z, source.y) * WorldScale.MwUnitsToMeters;

        public static quaternion SourceQuaternionToUnity(quaternion source)
        {
            quaternion converted = new(-source.value.x, -source.value.z, -source.value.y, source.value.w);
            return math.lengthsq(converted.value) > QuaternionEpsilon
                ? math.normalize(converted)
                : quaternion.identity;
        }

        public static float4x4 SourceAffineToUnity(float4x4 source)
        {
            return new float4x4(
                new float4(source.c0.x, source.c0.z, source.c0.y, 0f),
                new float4(source.c2.x, source.c2.z, source.c2.y, 0f),
                new float4(source.c1.x, source.c1.z, source.c1.y, 0f),
                new float4(
                    source.c3.x * WorldScale.MwUnitsToMeters,
                    source.c3.z * WorldScale.MwUnitsToMeters,
                    source.c3.y * WorldScale.MwUnitsToMeters,
                    1f));
        }
    }
}
