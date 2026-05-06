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
    public partial struct ContainerLootBootstrapSystem : ISystem
    {
        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<ContainerLootBootstrapRequest>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            Entity runtimeEntity = RuntimeBootstrapUtility.ResolveOrCreate<PlayerInteractionFocus>(systemState.EntityManager);

            RuntimeBootstrapUtility.EnsureBuffer<ContainerSessionHeader>(systemState.EntityManager, runtimeEntity);
            RuntimeBootstrapUtility.EnsureBuffer<ContainerSessionItem>(systemState.EntityManager, runtimeEntity);
            RuntimeBootstrapUtility.EnsureComponent(systemState.EntityManager, runtimeEntity, new ContainerWindowState
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
            RuntimeBootstrapUtility.EnsureComponent(systemState.EntityManager, runtimeEntity, new ContainerWindowRequest());
            RuntimeBootstrapRequestUtility.Consume<ContainerLootBootstrapRequest>(systemState.EntityManager);
        }
    }
}
