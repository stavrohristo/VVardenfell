using Unity.Burst;
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
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency = new ApplyActorRootMotionJob().ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ActorPresentation), typeof(CPUAnimation))]
        partial struct ApplyActorRootMotionJob : IJobEntity
        {
            void Execute(
                ref ActorRootMotion rootMotion,
                in ActorAnimationController controller,
                in ActorSkeleton skeleton,
                DynamicBuffer<ActorBone> bones)
            {
                int accum = skeleton.AccumulationBoneIndex;
                if ((uint)accum >= (uint)bones.Length)
                {
                    ResetRootMotion(ref rootMotion);
                    return;
                }

                var accumBone = bones[accum];
                float3 currentPosition = ExtractTranslation(accumBone.LocalToRoot);
                quaternion currentRotation = ExtractRotation(accumBone.LocalToRoot);
                bool resetHistory = rootMotion.Initialized == 0
                    || !controller.CurrentGroup.Equals(rootMotion.LastGroup)
                    || controller.Time < rootMotion.LastTime;

                if (resetHistory)
                {
                    rootMotion.Delta = float3.zero;
                    rootMotion.DeltaRotation = quaternion.identity;
                    rootMotion.HasDelta = 0;
                }
                else
                {
                    float3 delta = currentPosition - rootMotion.PreviousAccumulationPosition;
                    quaternion previousRotation = SafeNormalize(rootMotion.PreviousAccumulationRotation);
                    quaternion deltaRotation = math.mul(currentRotation, math.inverse(previousRotation));

                    rootMotion.Delta = delta;
                    rootMotion.DeltaRotation = SafeNormalize(deltaRotation);
                    rootMotion.HasDelta = (byte)(math.lengthsq(delta) > 0.0000001f
                        || math.lengthsq(rootMotion.DeltaRotation.value - quaternion.identity.value) > 0.0000001f
                            ? 1
                            : 0);
                }

                rootMotion.PreviousAccumulationPosition = currentPosition;
                rootMotion.PreviousAccumulationRotation = currentRotation;
                rootMotion.LastGroup = controller.CurrentGroup;
                rootMotion.LastTime = controller.Time;
                rootMotion.Initialized = 1;

                StripAccumulationMotion(bones, accum, skeleton.AccumulationSubtreeEndIndex);
            }

            static void ResetRootMotion(ref ActorRootMotion rootMotion)
            {
                FixedString64Bytes lastGroup = rootMotion.LastGroup;
                rootMotion = default;
                rootMotion.DeltaRotation = quaternion.identity;
                rootMotion.PreviousAccumulationRotation = quaternion.identity;
                rootMotion.LastGroup = lastGroup;
            }

            static void StripAccumulationMotion(DynamicBuffer<ActorBone> bones, int accumulationBoneIndex, int subtreeEndIndex)
            {
                var accumBone = bones[accumulationBoneIndex];
                accumBone.LocalPosition = accumBone.BindPosition;
                accumBone.LocalRotation = SafeNormalize(accumBone.BindRotation);
                bones[accumulationBoneIndex] = accumBone;

                int end = (uint)subtreeEndIndex <= (uint)bones.Length && subtreeEndIndex > accumulationBoneIndex
                    ? subtreeEndIndex
                    : bones.Length;
                for (int i = accumulationBoneIndex; i < end; i++)
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
}
