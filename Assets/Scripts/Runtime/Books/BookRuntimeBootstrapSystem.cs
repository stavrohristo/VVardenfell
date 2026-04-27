using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Interactions;
using VVardenfell.Runtime.Shell;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Books
{
    [UpdateInGroup(typeof(MorrowindInitializationSystemGroup))]
    [UpdateAfter(typeof(RuntimeShellBootstrapSystem))]
    public partial class BookRuntimeBootstrapSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            Entity runtimeEntity;
            if (SystemAPI.HasSingleton<RuntimeShellState>())
                runtimeEntity = SystemAPI.GetSingletonEntity<RuntimeShellState>();
            else if (SystemAPI.HasSingleton<PlayerInteractionFocus>())
                runtimeEntity = SystemAPI.GetSingletonEntity<PlayerInteractionFocus>();
            else
            {
                runtimeEntity = EntityManager.CreateEntity();
                EntityManager.SetName(runtimeEntity, "VVardenfell.BookRuntime");
            }

            EnsureComponent(runtimeEntity, new BookReadRequest { InventoryIndex = -1 });
            EnsureComponent(runtimeEntity, new BookReaderState { InventoryIndex = -1 });
            EnsureComponent(runtimeEntity, new BookReaderRequest());
            EnsureComponent(runtimeEntity, new BookInventoryReadRequest { InventoryIndex = -1 });
            EnsureComponent(runtimeEntity, new BookSkillGrantRequest());

            if (!EntityManager.HasBuffer<BookReadHistoryEntry>(runtimeEntity))
                EntityManager.AddBuffer<BookReadHistoryEntry>(runtimeEntity);

            Enabled = false;
        }

        void EnsureComponent<T>(Entity entity, T value)
            where T : unmanaged, IComponentData
        {
            if (!EntityManager.HasComponent<T>(entity))
                EntityManager.AddComponentData(entity, value);
        }
    }
}
