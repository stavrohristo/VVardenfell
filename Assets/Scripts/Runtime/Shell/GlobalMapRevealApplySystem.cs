using System;
using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.MorrowindScript;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Shell
{
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    public partial struct GlobalMapRevealApplySystem : ISystem
    {
        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<MorrowindScriptRuntimeState>();
            systemState.RequireForUpdate<GlobalMapRevealRequest>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = systemState.EntityManager.GetBuffer<GlobalMapRevealRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            for (int i = 0; i < requests.Length; i++)
            {
                string cellNamePrefix = requests[i].CellNamePrefix.ToString();
                if (string.IsNullOrWhiteSpace(cellNamePrefix))
                    throw new InvalidOperationException("[VVardenfell][Shell] ShowMap requires a non-empty cell name prefix.");

                GlobalMapPresentationCache.AddVisitedLocationsByCellNamePrefix(cellNamePrefix);
            }

            requests.Clear();
        }
    }
}
