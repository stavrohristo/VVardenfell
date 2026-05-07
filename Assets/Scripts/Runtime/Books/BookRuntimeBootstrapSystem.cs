using Unity.Entities;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Interactions;
using VVardenfell.Runtime.Shell;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Books
{
    [UpdateInGroup(typeof(MorrowindInitializationSystemGroup))]
    [UpdateAfter(typeof(RuntimeShellBootstrapSystem))]
    public partial struct BookRuntimeBootstrapSystem : ISystem
    {
        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<BookRuntimeBootstrapRequest>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            Entity runtimeEntity = RuntimeBootstrapUtility.ResolveOrCreate<RuntimeShellState, PlayerInteractionFocus>(
                systemState.EntityManager,
                "VVardenfell.BookRuntime");

            RuntimeBootstrapUtility.EnsureComponent(systemState.EntityManager, runtimeEntity, new BookReadRequest { InventoryIndex = -1 });
            RuntimeBootstrapUtility.EnsureComponent(systemState.EntityManager, runtimeEntity, new BookReaderState { InventoryIndex = -1 });
            RuntimeBootstrapUtility.EnsureComponent(systemState.EntityManager, runtimeEntity, new BookReaderRequest());
            RuntimeBootstrapUtility.EnsureComponent(systemState.EntityManager, runtimeEntity, new BookInventoryReadRequest { InventoryIndex = -1 });
            RuntimeBootstrapUtility.EnsureComponent(systemState.EntityManager, runtimeEntity, new BookSkillGrantRequest());
            RuntimeBootstrapUtility.EnsureComponent(systemState.EntityManager, runtimeEntity, new BookTakeRequest());

            RuntimeBootstrapUtility.EnsureBuffer<BookReadHistoryEntry>(systemState.EntityManager, runtimeEntity);

            RuntimeBootstrapRequestUtility.Consume<BookRuntimeBootstrapRequest>(systemState.EntityManager);
        }
    }
}
