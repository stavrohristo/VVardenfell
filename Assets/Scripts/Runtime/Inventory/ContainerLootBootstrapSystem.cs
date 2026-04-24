using Unity.Entities;
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
            Entity runtimeEntity;
            if (SystemAPI.HasSingleton<PlayerInteractionFocus>())
                runtimeEntity = SystemAPI.GetSingletonEntity<PlayerInteractionFocus>();
            else
                runtimeEntity = EntityManager.CreateEntity();

            EnsureBuffer<ContainerSessionHeader>(runtimeEntity);
            EnsureBuffer<ContainerSessionItem>(runtimeEntity);
            EnsureComponent(runtimeEntity, new ContainerWindowState
            {
                NormalizedX = 0.49f,
                NormalizedY = 0.54f,
                NormalizedWidth = 0.39f,
                NormalizedHeight = 0.38f,
                SelectedItemIndex = -1,
            });
            EnsureComponent(runtimeEntity, new ContainerWindowRequest());
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
