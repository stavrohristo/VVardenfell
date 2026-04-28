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
    public partial class BookRuntimeBootstrapSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            Entity runtimeEntity = RuntimeBootstrapUtility.ResolveOrCreate<RuntimeShellState, PlayerInteractionFocus>(
                EntityManager,
                "VVardenfell.BookRuntime");

            RuntimeBootstrapUtility.EnsureComponent(EntityManager, runtimeEntity, new BookReadRequest { InventoryIndex = -1 });
            RuntimeBootstrapUtility.EnsureComponent(EntityManager, runtimeEntity, new BookReaderState { InventoryIndex = -1 });
            RuntimeBootstrapUtility.EnsureComponent(EntityManager, runtimeEntity, new BookReaderRequest());
            RuntimeBootstrapUtility.EnsureComponent(EntityManager, runtimeEntity, new BookInventoryReadRequest { InventoryIndex = -1 });
            RuntimeBootstrapUtility.EnsureComponent(EntityManager, runtimeEntity, new BookSkillGrantRequest());

            RuntimeBootstrapUtility.EnsureBuffer<BookReadHistoryEntry>(EntityManager, runtimeEntity);

            Enabled = false;
        }
    }
}
