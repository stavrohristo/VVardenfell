using System;
using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.WorldState
{
    [UpdateInGroup(typeof(MorrowindGameplayMutationSystemGroup))]
    public partial struct ScriptVisibleSaveStateProjectionSystem : ISystem
    {
        EntityQuery _pendingQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _pendingQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<LogicalRefTag>(),
                ComponentType.ReadOnly<PlacedRefIdentity>(),
                ComponentType.Exclude<PlacedRefSavedStateProjectionApplied>());
            systemState.RequireForUpdate<MorrowindScriptRuntimeState>();
            systemState.RequireForUpdate(_pendingQuery);
        }

        public void OnUpdate(ref SystemState systemState)
        {
            if (!ScriptVisibleSaveStateUtility.TryProjectSavedStateForLiveRefs(systemState.EntityManager, out string error))
                throw new InvalidOperationException(error);
        }
    }
}
