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
    public partial class RuntimeSpawnBootstrapSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            Entity runtimeEntity = RuntimeBootstrapUtility.ResolveOrCreate<PlayerInteractionFocus, RuntimeSpawnState>(
                EntityManager,
                ref ecb,
                new FixedString64Bytes("VVardenfell.RuntimeSpawn"),
                out bool created);

            RuntimeBootstrapUtility.EnsureComponent(EntityManager, runtimeEntity, new RuntimeSpawnState(), ref ecb, created);
            RuntimeBootstrapUtility.EnsureComponent(EntityManager, runtimeEntity, new RuntimeSpawnResult
            {
                LogicalEntity = Entity.Null,
            }, ref ecb, created);
            RuntimeBootstrapUtility.EnsureBuffer<RuntimeSpawnRequest>(EntityManager, runtimeEntity, ref ecb, created);
            RuntimeBootstrapUtility.EnsureBuffer<RuntimeSpawnedRef>(EntityManager, runtimeEntity, ref ecb, created);
            WorldStateStructuralUtility.PlaybackAndDispose(EntityManager, ref ecb);
            Enabled = false;
        }
    }
}
