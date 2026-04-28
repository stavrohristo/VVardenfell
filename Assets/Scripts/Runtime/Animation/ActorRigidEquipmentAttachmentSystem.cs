using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Animation
{
    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    [UpdateAfter(typeof(ActorPoseSamplingSystem))]
    public partial struct ActorRigidEquipmentAttachmentSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ActorRigidEquipmentAttachment>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var job = new UpdateRigidEquipmentAttachmentJob
            {
                ActorWorlds = SystemAPI.GetComponentLookup<LocalToWorld>(true),
                ActorBones = SystemAPI.GetBufferLookup<ActorBone>(true),
            };
            state.Dependency = job.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        partial struct UpdateRigidEquipmentAttachmentJob : IJobEntity
        {
            [ReadOnly] public ComponentLookup<LocalToWorld> ActorWorlds;
            [ReadOnly] public BufferLookup<ActorBone> ActorBones;

            void Execute(ref LocalTransform transform, in ActorRigidEquipmentAttachment attachment)
            {
                if (attachment.Actor == Entity.Null
                    || !ActorWorlds.HasComponent(attachment.Actor)
                    || !ActorBones.HasBuffer(attachment.Actor))
                {
                    return;
                }

                var bones = ActorBones[attachment.Actor];
                if ((uint)attachment.BoneIndex >= (uint)bones.Length)
                    return;

                float4x4 actorWorld = ActorWorlds[attachment.Actor].Value;
                float4x4 boneWorld = math.mul(actorWorld, bones[attachment.BoneIndex].LocalToRoot);
                float4x4 correction = float4x4.TRS(
                    attachment.LocalPosition,
                    attachment.LocalRotation,
                    new float3(math.max(0.0001f, attachment.LocalScale)));
                float4x4 world = math.mul(boneWorld, correction);

                transform.Position = world.c3.xyz;
                transform.Rotation = ExtractRotation(world);
                transform.Scale = ExtractUniformScale(actorWorld) * math.max(0.0001f, attachment.LocalScale);
            }

            static quaternion ExtractRotation(float4x4 matrix)
            {
                float3 c0 = math.normalizesafe(matrix.c0.xyz, new float3(1f, 0f, 0f));
                float3 c1 = math.normalizesafe(matrix.c1.xyz, new float3(0f, 1f, 0f));
                float3 c2 = math.normalizesafe(matrix.c2.xyz, new float3(0f, 0f, 1f));
                return new quaternion(new float3x3(c0, c1, c2));
            }

            static float ExtractUniformScale(float4x4 matrix)
            {
                float scale = math.length(matrix.c0.xyz);
                return scale > 0.0001f ? scale : 1f;
            }
        }
    }
}
