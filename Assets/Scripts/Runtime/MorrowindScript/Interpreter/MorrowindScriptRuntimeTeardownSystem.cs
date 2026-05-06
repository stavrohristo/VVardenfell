using Unity.Collections;
using Unity.Entities;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.MorrowindScript
{
    [UpdateInGroup(typeof(MorrowindInitializationSystemGroup))]
    [UpdateAfter(typeof(RuntimeSessionTeardownMarkSystem))]
    public partial struct MorrowindScriptRuntimeTeardownSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            using var query = state.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<MorrowindScriptRuntimeState>(),
                ComponentType.ReadOnly<SessionTeardown>());

            if (query.IsEmpty)
                return;

            using var entities = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
                DisposeAndDestroy(state.EntityManager, entities[i]);
        }

        static void DisposeAndDestroy(EntityManager entityManager, Entity runtimeEntity)
        {
            if (!entityManager.Exists(runtimeEntity))
                return;

            if (entityManager.HasComponent<MorrowindScriptRuntimeCatalog>(runtimeEntity))
            {
                var catalog = entityManager.GetComponentData<MorrowindScriptRuntimeCatalog>(runtimeEntity);
                catalog.Dispose();
                entityManager.SetComponentData(runtimeEntity, default(MorrowindScriptRuntimeCatalog));
            }

            if (entityManager.HasComponent<MorrowindScriptInterpreterScratch>(runtimeEntity))
            {
                var scratch = entityManager.GetComponentData<MorrowindScriptInterpreterScratch>(runtimeEntity);
                scratch.Dispose();
                entityManager.SetComponentData(runtimeEntity, default(MorrowindScriptInterpreterScratch));
            }

            entityManager.DestroyEntity(runtimeEntity);
        }
    }
}
