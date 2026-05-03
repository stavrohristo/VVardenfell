using System;
using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.MorrowindScript;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Shell
{
    [UpdateInGroup(typeof(MorrowindWorldMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptInterpreterSystem))]
    public partial class GlobalMapRevealApplySystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<MorrowindScriptRuntimeState>();
            RequireForUpdate<GlobalMapRevealRequest>();
        }

        protected override void OnUpdate()
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = EntityManager.GetBuffer<GlobalMapRevealRequest>(runtimeEntity);
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
