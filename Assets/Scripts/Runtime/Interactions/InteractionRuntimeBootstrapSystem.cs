using Unity.Collections;
using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Interactions
{
    [UpdateInGroup(typeof(MorrowindInitializationSystemGroup))]
    public partial class InteractionRuntimeBootstrapSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            Entity runtimeEntity;
            bool created = false;
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            if (SystemAPI.HasSingleton<PlayerInteractionFocus>())
            {
                runtimeEntity = SystemAPI.GetSingletonEntity<PlayerInteractionFocus>();
            }
            else
            {
                runtimeEntity = ecb.CreateEntity();
                ecb.SetName(runtimeEntity, new FixedString64Bytes("VVardenfell.InteractionRuntime"));
                created = true;
            }

            EnsureComponent(runtimeEntity, new PlayerInteractionFocus
            {
                TargetEntity = Entity.Null,
            }, ref ecb, created);
            EnsureComponent(runtimeEntity, new PlayerInteractionRaycastHit
            {
                HitEntity = Entity.Null,
                ProxyHitEntity = Entity.Null,
                SolidHitEntity = Entity.Null,
            }, ref ecb, created);
            EnsureComponent(runtimeEntity, new InteractionRuntimeState(), ref ecb, created);
            EnsureComponent(runtimeEntity, new InteractionActivationRequest
            {
                TargetEntity = Entity.Null,
            }, ref ecb, created);
            EnsureComponent(runtimeEntity, new InteractionActivationResult(), ref ecb, created);
            EnsureComponent(runtimeEntity, new InteractionPresentationState
            {
                ShowCrosshair = 1,
            }, ref ecb, created);
            EnsureComponent(runtimeEntity, new DialogueReadinessState
            {
                PendingTargetEntity = Entity.Null,
            }, ref ecb, created);
            EnsureComponent(runtimeEntity, new InteriorTransitionState(), ref ecb, created);
            EnsureBuffer<InteriorSpawnedEntity>(runtimeEntity, ref ecb, created);
            EnsureBuffer<PlayerInventoryItem>(runtimeEntity, ref ecb, created);
            EnsureBuffer<PickedItemRecord>(runtimeEntity, ref ecb, created);
            ecb.Playback(EntityManager);
            ecb.Dispose();
            Enabled = false;
        }

        void EnsureComponent<T>(Entity entity, T value, ref EntityCommandBuffer ecb, bool created)
            where T : unmanaged, IComponentData
        {
            if (created || !EntityManager.HasComponent<T>(entity))
                ecb.AddComponent(entity, value);
        }

        void EnsureBuffer<T>(Entity entity, ref EntityCommandBuffer ecb, bool created)
            where T : unmanaged, IBufferElementData
        {
            if (created || !EntityManager.HasBuffer<T>(entity))
                ecb.AddBuffer<T>(entity);
        }
    }
}
