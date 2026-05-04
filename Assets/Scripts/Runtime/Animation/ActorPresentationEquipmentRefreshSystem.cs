using Unity.Entities;
using Unity.Transforms;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Rendering;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Animation
{
    [UpdateInGroup(typeof(MorrowindPresentationBuildSystemGroup))]
    [UpdateBefore(typeof(ActorPresentationSpawnSystem))]
    public partial class ActorPresentationEquipmentRefreshSystem : SystemBase
    {
        EntityQuery _dirtyQuery;

        protected override void OnCreate()
        {
            _dirtyQuery = GetEntityQuery(
                ComponentType.ReadOnly<ActorPresentationEquipmentSignature>(),
                ComponentType.ReadOnly<ActorEquipmentSlot>(),
                ComponentType.ReadOnly<ActorPresentation>(),
                ComponentType.ReadOnly<ActorSpawnSource>(),
                ComponentType.ReadOnly<ActorPresentationEquipmentDirty>());
            RequireForUpdate(_dirtyQuery);
        }

        protected override void OnUpdate()
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

                QueuePresentationRefresh(ref ecb, entity);
                queuedRefresh = true;
            }

            if (queuedRefresh || touchedDirty)
                ecb.Playback(EntityManager);

            ecb.Dispose();
        }

        void QueuePresentationRefresh(ref EntityCommandBuffer ecb, Entity actor)
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

            RemoveComponentIfPresent<ActorPresentation>(ref ecb, actor);
            RemoveComponentIfPresent<ActorPresentationEquipmentSignature>(ref ecb, actor);
            RemoveComponentIfPresent<ActorSkeleton>(ref ecb, actor);
            RemoveComponentIfPresent<ActorAnimationState>(ref ecb, actor);
            RemoveComponentIfPresent<ActorJumpAnimationState>(ref ecb, actor);
            RemoveComponentIfPresent<ActorGpuAnimationState>(ref ecb, actor);
            RemoveComponentIfPresent<ActorLocalBounds>(ref ecb, actor);

            RemoveBufferIfPresent<ActorBone>(ref ecb, actor);
            RemoveBufferIfPresent<ActorSampledBonePose>(ref ecb, actor);
            RemoveBufferIfPresent<ActorGpuAnimationRequest>(ref ecb, actor);
            RemoveBufferIfPresent<ActorAnimationOverlayState>(ref ecb, actor);
            RemoveBufferIfPresent<ActorAnimationEvent>(ref ecb, actor);
            RemoveBufferIfPresent<ActorSkinMesh>(ref ecb, actor);
            RemoveBufferIfPresent<ActorRigidEquipment>(ref ecb, actor);
            RemoveBufferIfPresent<ActorAttachmentBone>(ref ecb, actor);
            RemoveBufferIfPresent<LinkedEntityGroup>(ref ecb, actor);
        }

        void RemoveComponentIfPresent<T>(ref EntityCommandBuffer ecb, Entity entity)
            where T : unmanaged, IComponentData
        {
            if (EntityManager.HasComponent<T>(entity))
                ecb.RemoveComponent<T>(entity);
        }

        void RemoveBufferIfPresent<T>(ref EntityCommandBuffer ecb, Entity entity)
            where T : unmanaged, IBufferElementData
        {
            if (EntityManager.HasBuffer<T>(entity))
                ecb.RemoveComponent<T>(entity);
        }

    }
}
