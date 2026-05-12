using Unity.Entities;
using Unity.Collections;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Interactions;
using VVardenfell.Runtime.Inventory;
using VVardenfell.Runtime.Shell;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.WorldState
{
    [UpdateInGroup(typeof(MorrowindInitializationSystemGroup))]
    [UpdateAfter(typeof(RuntimeSpawnBootstrapSystem))]
    [UpdateAfter(typeof(ContainerLootBootstrapSystem))]
    [UpdateAfter(typeof(RuntimeShellBootstrapSystem))]
    public partial struct WorldJournalBootstrapSystem : ISystem
    {
        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<WorldJournalBootstrapRequest>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            Entity runtimeEntity = RuntimeBootstrapUtility.ResolveOrCreate<PlayerInteractionFocus, WorldJournalState>(
                systemState.EntityManager,
                ref ecb,
                new FixedString64Bytes("VVardenfell.WorldJournal"),
                out bool created);

            RuntimeBootstrapUtility.EnsureComponent(systemState.EntityManager, runtimeEntity, new WorldJournalState(), ref ecb, created);
            WorldStateStructuralUtility.PlaybackAndDispose(systemState.EntityManager, ref ecb);
            RuntimeBootstrapRequestUtility.Consume<WorldJournalBootstrapRequest>(systemState.EntityManager);
        }
    }
}
