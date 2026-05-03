using Unity.Entities;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Interactions;
using VVardenfell.Runtime.Shell;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Inventory
{
    [UpdateInGroup(typeof(MorrowindInitializationSystemGroup))]
    [UpdateAfter(typeof(InteractionRuntimeBootstrapSystem))]
    [UpdateAfter(typeof(RuntimeShellBootstrapSystem))]
    public partial class ContainerLootBootstrapSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            Entity runtimeEntity = RuntimeBootstrapUtility.ResolveOrCreate<PlayerInteractionFocus>(EntityManager);

            RuntimeBootstrapUtility.EnsureBuffer<ContainerSessionHeader>(EntityManager, runtimeEntity);
            RuntimeBootstrapUtility.EnsureBuffer<ContainerSessionItem>(EntityManager, runtimeEntity);
            RuntimeBootstrapUtility.EnsureComponent(EntityManager, runtimeEntity, new ContainerWindowState
            {
                Rect = new RuntimeWindowRect
                {
                    X = 0.3425f,
                    Y = 0.6422f,
                    Width = 0.3125f,
                    Height = 0.2778f,
                },
                SelectedItemIndex = -1,
            });
            RuntimeBootstrapUtility.EnsureComponent(EntityManager, runtimeEntity, new ContainerWindowRequest());
            Enabled = false;
        }
    }
}
