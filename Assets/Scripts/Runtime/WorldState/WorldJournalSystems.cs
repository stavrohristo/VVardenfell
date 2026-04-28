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
    public partial class WorldJournalBootstrapSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            Entity runtimeEntity = RuntimeBootstrapUtility.ResolveOrCreate<PlayerInteractionFocus, WorldJournalState>(
                EntityManager,
                ref ecb,
                new FixedString64Bytes("VVardenfell.WorldJournal"),
                out bool created);

            RuntimeBootstrapUtility.EnsureComponent(EntityManager, runtimeEntity, new WorldJournalState(), ref ecb, created);
            RuntimeBootstrapUtility.EnsureBuffer<WorldJournalEntry>(EntityManager, runtimeEntity, ref ecb, created);
            ecb.Playback(EntityManager);
            ecb.Dispose();
            Enabled = false;
        }
    }
}
