using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.WorldState;

namespace VVardenfell.Runtime.MorrowindScript
{
    [UpdateInGroup(typeof(MorrowindGameplayMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptInterpreterSystem))]
    public partial struct MorrowindScriptObjectStateSaveSyncSystem : ISystem
    {
        EntityQuery _scriptQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _scriptQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<PlacedRefIdentity>(),
                ComponentType.ReadOnly<MorrowindScriptInstance>());
            systemState.RequireForUpdate<MorrowindScriptRuntimeState>();
            systemState.RequireForUpdate(_scriptQuery);
        }

        public void OnUpdate(ref SystemState systemState)
        {
            foreach (var (identityRef, entity) in
                     SystemAPI.Query<RefRO<PlacedRefIdentity>>()
                         .WithAll<MorrowindScriptInstance>()
                         .WithEntityAccess())
            {
                uint placedRefId = identityRef.ValueRO.Value;
                if (placedRefId == 0u)
                    continue;

                ScriptVisibleSaveStateUtility.UpsertObjectScript(systemState.EntityManager, entity, placedRefId);
            }
        }
    }
}
