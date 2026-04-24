using Unity.Entities;
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
            Entity runtimeEntity;
            if (SystemAPI.HasSingleton<PlayerInteractionFocus>())
            {
                runtimeEntity = SystemAPI.GetSingletonEntity<PlayerInteractionFocus>();
            }
            else if (SystemAPI.HasSingleton<RuntimeSpawnState>())
            {
                runtimeEntity = SystemAPI.GetSingletonEntity<RuntimeSpawnState>();
            }
            else
            {
                runtimeEntity = EntityManager.CreateEntity();
                EntityManager.SetName(runtimeEntity, "VVardenfell.RuntimeSpawn");
            }

            EnsureComponent(runtimeEntity, new RuntimeSpawnState());
            EnsureComponent(runtimeEntity, new RuntimeSpawnResult
            {
                LogicalEntity = Entity.Null,
            });
            EnsureBuffer<RuntimeSpawnRequest>(runtimeEntity);
            EnsureBuffer<RuntimeSpawnedRef>(runtimeEntity);
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
