using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Animation
{
    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    [UpdateAfter(typeof(ActorPoseSamplingSystem))]
    public partial struct ActorRootMotionSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            foreach (var (rootMotion, controller, skeleton, bones) in
                     SystemAPI.Query<RefRW<ActorRootMotion>, RefRO<ActorAnimationController>, RefRO<ActorSkeleton>, DynamicBuffer<ActorBone>>()
                         .WithAll<ActorPresentation, CPUAnimation>())
            {
                int accum = skeleton.ValueRO.AccumulationBoneIndex;
                if ((uint)accum >= (uint)bones.Length)
                {
                    ResetRootMotion(ref rootMotion.ValueRW);
                    continue;
                }

                var accumBone = bones[accum];
                float3 currentPosition = ExtractTranslation(accumBone.LocalToRoot);
                quaternion currentRotation = ExtractRotation(accumBone.LocalToRoot);
                bool resetHistory = rootMotion.ValueRO.Initialized == 0
                    || !controller.ValueRO.CurrentGroup.Equals(rootMotion.ValueRO.LastGroup)
                    || controller.ValueRO.Time < rootMotion.ValueRO.LastTime;

                if (resetHistory)
                {
                    rootMotion.ValueRW.Delta = float3.zero;
                    rootMotion.ValueRW.DeltaRotation = quaternion.identity;
                    rootMotion.ValueRW.HasDelta = 0;
                }
                else
                {
                    float3 delta = currentPosition - rootMotion.ValueRO.PreviousAccumulationPosition;
                    quaternion previousRotation = SafeNormalize(rootMotion.ValueRO.PreviousAccumulationRotation);
                    quaternion deltaRotation = math.mul(currentRotation, math.inverse(previousRotation));

                    rootMotion.ValueRW.Delta = delta;
                    rootMotion.ValueRW.DeltaRotation = SafeNormalize(deltaRotation);
                    rootMotion.ValueRW.HasDelta = (byte)(math.lengthsq(delta) > 0.0000001f
                        || math.lengthsq(rootMotion.ValueRW.DeltaRotation.value - quaternion.identity.value) > 0.0000001f
                            ? 1
                            : 0);
                }

                rootMotion.ValueRW.PreviousAccumulationPosition = currentPosition;
                rootMotion.ValueRW.PreviousAccumulationRotation = currentRotation;
                rootMotion.ValueRW.LastGroup = controller.ValueRO.CurrentGroup;
                rootMotion.ValueRW.LastTime = controller.ValueRO.Time;
                rootMotion.ValueRW.Initialized = 1;

                StripAccumulationMotion(bones, accum);
            }
        }

        static void ResetRootMotion(ref ActorRootMotion rootMotion)
        {
            FixedString64Bytes lastGroup = rootMotion.LastGroup;
            rootMotion = default;
            rootMotion.DeltaRotation = quaternion.identity;
            rootMotion.PreviousAccumulationRotation = quaternion.identity;
            rootMotion.LastGroup = lastGroup;
        }

        static void StripAccumulationMotion(DynamicBuffer<ActorBone> bones, int accumulationBoneIndex)
        {
            var accumBone = bones[accumulationBoneIndex];
            accumBone.LocalPosition = accumBone.BindPosition;
            accumBone.LocalRotation = SafeNormalize(accumBone.BindRotation);
            bones[accumulationBoneIndex] = accumBone;

            for (int i = accumulationBoneIndex; i < bones.Length; i++)
            {
                var bone = bones[i];
                float scale = bone.LocalScale <= 0f ? bone.BindScale : bone.LocalScale;
                if (scale <= 0f)
                    scale = 1f;

                float4x4 local = float4x4.TRS(
                    bone.LocalPosition,
                    SafeNormalize(bone.LocalRotation),
                    new float3(scale));
                bone.LocalToRoot = bone.ParentIndex >= 0 && bone.ParentIndex < i
                    ? math.mul(bones[bone.ParentIndex].LocalToRoot, local)
                    : local;
                bone.SkinMatrix = bone.LocalToRoot;
                bones[i] = bone;
            }
        }

        static float3 ExtractTranslation(float4x4 matrix)
            => matrix.c3.xyz;

        static quaternion ExtractRotation(float4x4 matrix)
        {
            float3 forward = math.normalizesafe(matrix.c2.xyz, new float3(0f, 0f, 1f));
            float3 up = math.normalizesafe(matrix.c1.xyz, new float3(0f, 1f, 0f));
            return SafeNormalize(quaternion.LookRotationSafe(forward, up));
        }

        static quaternion SafeNormalize(quaternion value)
            => math.lengthsq(value.value) > 0.000001f
                ? math.normalize(value)
                : quaternion.identity;
    }
}
