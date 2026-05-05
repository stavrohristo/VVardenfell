using Unity.Collections;
using Unity.Entities;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Interactions
{
    [UpdateInGroup(typeof(MorrowindInitializationSystemGroup))]
    public partial class InteractionRuntimeBootstrapSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<InteractionRuntimeBootstrapRequest>();
        }

        protected override void OnUpdate()
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            Entity runtimeEntity = RuntimeBootstrapUtility.ResolveOrCreate<PlayerInteractionFocus>(
                EntityManager,
                ref ecb,
                new FixedString64Bytes("VVardenfell.InteractionRuntime"),
                out bool created);

            RuntimeBootstrapUtility.EnsureComponent(EntityManager, runtimeEntity, new PlayerInteractionFocus
            {
                TargetEntity = Entity.Null,
            }, ref ecb, created);
            RuntimeBootstrapUtility.EnsureComponent(EntityManager, runtimeEntity, new PlayerInteractionRaycastHit
            {
                HitEntity = Entity.Null,
                ProxyHitEntity = Entity.Null,
                SolidHitEntity = Entity.Null,
            }, ref ecb, created);
            RuntimeBootstrapUtility.EnsureComponent(EntityManager, runtimeEntity, new InteractionRuntimeState(), ref ecb, created);
            RuntimeBootstrapUtility.EnsureBuffer<ScriptActivationEvent>(EntityManager, runtimeEntity, ref ecb, created);
            RuntimeBootstrapUtility.EnsureBuffer<ScriptDefaultActivationRequest>(EntityManager, runtimeEntity, ref ecb, created);
            RuntimeBootstrapUtility.EnsureComponent(EntityManager, runtimeEntity, new InteractionActivationRequest
            {
                TargetEntity = Entity.Null,
            }, ref ecb, created);
            RuntimeBootstrapUtility.EnsureComponent(EntityManager, runtimeEntity, new InteractionActivationResult(), ref ecb, created);
            RuntimeBootstrapUtility.EnsureComponent(EntityManager, runtimeEntity, new InteractionPresentationState
            {
                ShowCrosshair = 1,
            }, ref ecb, created);
            RuntimeBootstrapUtility.EnsureComponent(EntityManager, runtimeEntity, new DialogueReadinessState
            {
                PendingTargetEntity = Entity.Null,
            }, ref ecb, created);
            RuntimeBootstrapUtility.EnsureComponent(EntityManager, runtimeEntity, new InteriorTransitionState(), ref ecb, created);
            RuntimeBootstrapUtility.EnsureBuffer<InteriorSpawnedEntity>(EntityManager, runtimeEntity, ref ecb, created);
            RuntimeBootstrapUtility.EnsureBuffer<PickedItemRecord>(EntityManager, runtimeEntity, ref ecb, created);
            ecb.Playback(EntityManager);
            ecb.Dispose();
            RuntimeBootstrapRequestUtility.Consume<InteractionRuntimeBootstrapRequest>(EntityManager);
        }
    }
}
