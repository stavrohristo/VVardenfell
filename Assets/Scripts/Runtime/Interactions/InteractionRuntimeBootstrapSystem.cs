using Unity.Collections;
using Unity.Entities;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Interactions
{
    [UpdateInGroup(typeof(MorrowindInitializationSystemGroup))]
    public partial struct InteractionRuntimeBootstrapSystem : ISystem
    {
        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<InteractionRuntimeBootstrapRequest>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            Entity runtimeEntity = RuntimeBootstrapUtility.ResolveOrCreate<PlayerInteractionFocus>(
                systemState.EntityManager,
                "VVardenfell.InteractionRuntime");

            RuntimeBootstrapUtility.SetOrAddComponent(systemState.EntityManager, runtimeEntity, new PlayerInteractionFocus
            {
                TargetEntity = Entity.Null,
            });
            RuntimeBootstrapUtility.SetOrAddComponent(systemState.EntityManager, runtimeEntity, new PlayerInteractionRaycastHit
            {
                HitEntity = Entity.Null,
                ProxyHitEntity = Entity.Null,
                SolidHitEntity = Entity.Null,
            });
            RuntimeBootstrapUtility.SetOrAddComponent(systemState.EntityManager, runtimeEntity, new InteractionRuntimeState());
            RuntimeBootstrapUtility.EnsureClearedBuffer<ScriptActivationEvent>(systemState.EntityManager, runtimeEntity);
            RuntimeBootstrapUtility.EnsureClearedBuffer<ScriptDefaultActivationRequest>(systemState.EntityManager, runtimeEntity);
            RuntimeBootstrapUtility.SetOrAddComponent(systemState.EntityManager, runtimeEntity, new InteractionActivationRequest
            {
                TargetEntity = Entity.Null,
            });
            RuntimeBootstrapUtility.SetOrAddComponent(systemState.EntityManager, runtimeEntity, new InteractionActivationResult());
            RuntimeBootstrapUtility.SetOrAddComponent(systemState.EntityManager, runtimeEntity, new InteractionPresentationState
            {
                ShowCrosshair = 1,
            });
            RuntimeBootstrapUtility.SetOrAddComponent(systemState.EntityManager, runtimeEntity, new DialogueReadinessState
            {
                PendingTargetEntity = Entity.Null,
            });
            RuntimeBootstrapUtility.SetOrAddComponent(systemState.EntityManager, runtimeEntity, new InteriorTransitionState());
            RuntimeBootstrapUtility.EnsureClearedBuffer<InteriorSpawnedEntity>(systemState.EntityManager, runtimeEntity);
            RuntimeBootstrapUtility.EnsureClearedBuffer<PickedItemRecord>(systemState.EntityManager, runtimeEntity);
            RuntimeBootstrapRequestUtility.Consume<InteractionRuntimeBootstrapRequest>(systemState.EntityManager);
        }
    }
}
