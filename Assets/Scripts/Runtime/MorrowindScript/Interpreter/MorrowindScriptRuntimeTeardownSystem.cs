using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.MorrowindScript
{
    [BurstCompile]
    [UpdateInGroup(typeof(MorrowindInitializationSystemGroup))]
    [UpdateAfter(typeof(RuntimeSessionTeardownMarkSystem))]
    public partial struct MorrowindScriptRuntimeTeardownSystem : ISystem
    {
        EntityQuery _teardownQuery;

        public void OnCreate(ref SystemState state)
        {
            _teardownQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<MorrowindScriptRuntimeState>(),
                ComponentType.ReadOnly<SessionTeardown>());
        }
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (_teardownQuery.IsEmpty)
                return;

            using var entities = _teardownQuery.ToEntityArray(Allocator.Temp);
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
