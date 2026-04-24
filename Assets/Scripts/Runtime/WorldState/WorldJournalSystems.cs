using Unity.Entities;
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
            Entity runtimeEntity;
            if (SystemAPI.HasSingleton<PlayerInteractionFocus>())
            {
                runtimeEntity = SystemAPI.GetSingletonEntity<PlayerInteractionFocus>();
            }
            else if (SystemAPI.HasSingleton<WorldJournalState>())
            {
                runtimeEntity = SystemAPI.GetSingletonEntity<WorldJournalState>();
            }
            else
            {
                runtimeEntity = EntityManager.CreateEntity();
                EntityManager.SetName(runtimeEntity, "VVardenfell.WorldJournal");
            }

            EnsureComponent(runtimeEntity, new WorldJournalState());
            EnsureBuffer<WorldJournalEntry>(runtimeEntity);
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
