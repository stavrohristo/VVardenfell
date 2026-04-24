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
            if (SystemAPI.HasSingleton<PlayerInteractionFocus>())
            {
                runtimeEntity = SystemAPI.GetSingletonEntity<PlayerInteractionFocus>();
            }
            else
            {
                runtimeEntity = EntityManager.CreateEntity();
                EntityManager.SetName(runtimeEntity, "VVardenfell.InteractionRuntime");
            }

            EnsureComponent(runtimeEntity, new PlayerInteractionFocus
            {
                TargetEntity = Entity.Null,
            });
            EnsureComponent(runtimeEntity, new PlayerInteractionRaycastHit
            {
                HitEntity = Entity.Null,
                ProxyHitEntity = Entity.Null,
                SolidHitEntity = Entity.Null,
            });
            EnsureComponent(runtimeEntity, new InteractionRuntimeState());
            EnsureComponent(runtimeEntity, new InteractionActivationRequest
            {
                TargetEntity = Entity.Null,
            });
            EnsureComponent(runtimeEntity, new InteractionActivationResult());
            EnsureComponent(runtimeEntity, new InteractionPresentationState
            {
                ShowCrosshair = 1,
            });
            EnsureComponent(runtimeEntity, new DialogueReadinessState
            {
                PendingTargetEntity = Entity.Null,
            });
            EnsureComponent(runtimeEntity, new InteriorTransitionState());
            EnsureBuffer<InteriorSpawnedEntity>(runtimeEntity);
            EnsureBuffer<PlayerInventoryItem>(runtimeEntity);
            EnsureBuffer<PickedItemRecord>(runtimeEntity);
            Enabled = false;
        }

        void EnsureComponent<T>(Entity entity, T value)
            where T : unmanaged, IComponentData
        {
            if (!EntityManager.HasComponent<T>(entity))
                EntityManager.AddComponentData(entity, value);
        }

        void EnsureBuffer<T>(Entity entity)
            where T : unmanaged, IBufferElementData
        {
            if (!EntityManager.HasBuffer<T>(entity))
                EntityManager.AddBuffer<T>(entity);
        }
    }
}
