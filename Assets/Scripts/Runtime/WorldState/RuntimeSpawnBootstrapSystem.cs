using Unity.Entities;
using Unity.Collections;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Interactions;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.WorldState
{
    [UpdateInGroup(typeof(MorrowindInitializationSystemGroup))]
    [UpdateAfter(typeof(InteractionRuntimeBootstrapSystem))]
    public partial struct RuntimeSpawnBootstrapSystem : ISystem
    {
        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<RuntimeSpawnBootstrapRequest>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            Entity runtimeEntity = RuntimeBootstrapUtility.ResolveOrCreate<PlayerInteractionFocus, RuntimeSpawnState>(
                systemState.EntityManager,
                ref ecb,
                new FixedString64Bytes("VVardenfell.RuntimeSpawn"),
                out bool created);

            RuntimeBootstrapUtility.EnsureComponent(systemState.EntityManager, runtimeEntity, new RuntimeSpawnState(), ref ecb, created);
            RuntimeBootstrapUtility.EnsureComponent(systemState.EntityManager, runtimeEntity, new RuntimeSpawnResult
            {
                LogicalEntity = Entity.Null,
            }, ref ecb, created);
            RuntimeBootstrapUtility.EnsureBuffer<RuntimeSpawnRequest>(systemState.EntityManager, runtimeEntity, ref ecb, created);
            RuntimeBootstrapUtility.EnsureBuffer<RuntimeSpawnedRef>(systemState.EntityManager, runtimeEntity, ref ecb, created);
            WorldStateStructuralUtility.PlaybackAndDispose(systemState.EntityManager, ref ecb);
            RuntimeBootstrapRequestUtility.Consume<RuntimeSpawnBootstrapRequest>(systemState.EntityManager);
        }
    }
}
