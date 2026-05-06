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
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            Entity runtimeEntity = RuntimeBootstrapUtility.ResolveOrCreate<PlayerInteractionFocus>(
                systemState.EntityManager,
                ref ecb,
                new FixedString64Bytes("VVardenfell.InteractionRuntime"),
                out bool created);

            RuntimeBootstrapUtility.EnsureComponent(systemState.EntityManager, runtimeEntity, new PlayerInteractionFocus
            {
                TargetEntity = Entity.Null,
            }, ref ecb, created);
            RuntimeBootstrapUtility.EnsureComponent(systemState.EntityManager, runtimeEntity, new PlayerInteractionRaycastHit
            {
                HitEntity = Entity.Null,
                ProxyHitEntity = Entity.Null,
                SolidHitEntity = Entity.Null,
            }, ref ecb, created);
            RuntimeBootstrapUtility.EnsureComponent(systemState.EntityManager, runtimeEntity, new InteractionRuntimeState(), ref ecb, created);
            RuntimeBootstrapUtility.EnsureBuffer<ScriptActivationEvent>(systemState.EntityManager, runtimeEntity, ref ecb, created);
            RuntimeBootstrapUtility.EnsureBuffer<ScriptDefaultActivationRequest>(systemState.EntityManager, runtimeEntity, ref ecb, created);
            RuntimeBootstrapUtility.EnsureComponent(systemState.EntityManager, runtimeEntity, new InteractionActivationRequest
            {
                TargetEntity = Entity.Null,
            }, ref ecb, created);
            RuntimeBootstrapUtility.EnsureComponent(systemState.EntityManager, runtimeEntity, new InteractionActivationResult(), ref ecb, created);
            RuntimeBootstrapUtility.EnsureComponent(systemState.EntityManager, runtimeEntity, new InteractionPresentationState
            {
                ShowCrosshair = 1,
            }, ref ecb, created);
            RuntimeBootstrapUtility.EnsureComponent(systemState.EntityManager, runtimeEntity, new DialogueReadinessState
            {
                PendingTargetEntity = Entity.Null,
            }, ref ecb, created);
            RuntimeBootstrapUtility.EnsureComponent(systemState.EntityManager, runtimeEntity, new InteriorTransitionState(), ref ecb, created);
            RuntimeBootstrapUtility.EnsureBuffer<InteriorSpawnedEntity>(systemState.EntityManager, runtimeEntity, ref ecb, created);
            RuntimeBootstrapUtility.EnsureBuffer<PickedItemRecord>(systemState.EntityManager, runtimeEntity, ref ecb, created);
            ecb.Playback(systemState.EntityManager);
            ecb.Dispose();
            RuntimeBootstrapRequestUtility.Consume<InteractionRuntimeBootstrapRequest>(systemState.EntityManager);
        }
    }
}
