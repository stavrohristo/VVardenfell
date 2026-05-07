using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Rendering;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Animation
{
    [BurstCompile]
    [UpdateInGroup(typeof(MorrowindPresentationBuildSystemGroup))]
    [UpdateBefore(typeof(ActorPresentationSpawnSystem))]
    public partial struct ActorPresentationEquipmentRefreshSystem : ISystem
    {
        EntityQuery _dirtyQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _dirtyQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<ActorPresentationEquipmentSignature>(),
                ComponentType.ReadOnly<ActorEquipmentSlot>(),
                ComponentType.ReadOnly<ActorPresentation>(),
                ComponentType.ReadOnly<ActorSpawnSource>(),
                ComponentType.ReadOnly<ActorPresentationEquipmentDirty>());
            systemState.RequireForUpdate(_dirtyQuery);
        }
        [BurstCompile]
        public void OnUpdate(ref SystemState systemState)
        {
            var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
            bool queuedRefresh = false;
            bool touchedDirty = false;

            foreach (var (signature, equipment, entity) in
                     SystemAPI.Query<RefRO<ActorPresentationEquipmentSignature>, DynamicBuffer<ActorEquipmentSlot>>()
                         .WithAll<ActorPresentation, ActorSpawnSource, ActorPresentationEquipmentDirty>()
                         .WithEntityAccess())
            {
                touchedDirty = true;
                ulong current = ActorPresentationEquipmentUtility.BuildEquipmentSignature(equipment);
                if (signature.ValueRO.Value == current)
                {
                    ecb.SetComponentEnabled<ActorPresentationEquipmentDirty>(entity, false);
                    continue;
                }

                QueuePresentationRefresh(ref systemState, ref ecb, entity);
                queuedRefresh = true;
            }

            if (queuedRefresh || touchedDirty)
                ecb.Playback(systemState.EntityManager);

            ecb.Dispose();
        }

        void QueuePresentationRefresh(ref SystemState systemState, ref EntityCommandBuffer ecb, Entity actor)
        {
            foreach (var (instance, entity) in
                     SystemAPI.Query<RefRO<ActorRenderMeshInstance>>()
                         .WithEntityAccess())
            {
                if (instance.ValueRO.Actor == actor)
                    ecb.DestroyEntity(entity);
            }

            foreach (var (attachment, entity) in
                     SystemAPI.Query<RefRO<ActorRigidEquipmentAttachment>>()
                         .WithEntityAccess())
            {
                if (attachment.ValueRO.Actor == actor)
                    ecb.DestroyEntity(entity);
            }

            RemoveComponentIfPresent<ActorPresentation>(ref systemState, ref ecb, actor);
            RemoveComponentIfPresent<ActorPresentationEquipmentSignature>(ref systemState, ref ecb, actor);
            RemoveComponentIfPresent<ActorSkeleton>(ref systemState, ref ecb, actor);
            RemoveComponentIfPresent<ActorAnimationState>(ref systemState, ref ecb, actor);
            RemoveComponentIfPresent<ActorJumpAnimationState>(ref systemState, ref ecb, actor);
            RemoveComponentIfPresent<ActorAnimationMotionState>(ref systemState, ref ecb, actor);
            RemoveComponentIfPresent<ActorHeadAnimationState>(ref systemState, ref ecb, actor);
            RemoveComponentIfPresent<ActorGpuAnimationState>(ref systemState, ref ecb, actor);
            RemoveComponentIfPresent<ActorLocalBounds>(ref systemState, ref ecb, actor);

            RemoveBufferIfPresent<ActorBone>(ref systemState, ref ecb, actor);
            RemoveBufferIfPresent<ActorSampledBonePose>(ref systemState, ref ecb, actor);
            RemoveBufferIfPresent<ActorGpuAnimationRequest>(ref systemState, ref ecb, actor);
            RemoveBufferIfPresent<ActorAnimationOverlayState>(ref systemState, ref ecb, actor);
            RemoveBufferIfPresent<ActorAnimationEvent>(ref systemState, ref ecb, actor);
            RemoveBufferIfPresent<ActorSkinMesh>(ref systemState, ref ecb, actor);
            RemoveBufferIfPresent<ActorRigidEquipment>(ref systemState, ref ecb, actor);
            RemoveBufferIfPresent<ActorAttachmentBone>(ref systemState, ref ecb, actor);
            RemoveBufferIfPresent<LinkedEntityGroup>(ref systemState, ref ecb, actor);
        }

        void RemoveComponentIfPresent<T>(ref SystemState systemState, ref EntityCommandBuffer ecb, Entity entity)
            where T : unmanaged, IComponentData
        {
            if (systemState.EntityManager.HasComponent<T>(entity))
                ecb.RemoveComponent<T>(entity);
        }

        void RemoveBufferIfPresent<T>(ref SystemState systemState, ref EntityCommandBuffer ecb, Entity entity)
            where T : unmanaged, IBufferElementData
        {
            if (systemState.EntityManager.HasBuffer<T>(entity))
                ecb.RemoveComponent<T>(entity);
        }

    }
}
