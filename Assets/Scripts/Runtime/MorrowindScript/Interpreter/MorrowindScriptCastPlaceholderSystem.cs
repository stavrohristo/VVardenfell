using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.MorrowindScript
{
    [UpdateInGroup(typeof(MorrowindWorldMutationSystemGroup))]
    public partial class MorrowindScriptCastPlaceholderSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<MorrowindScriptRuntimeState>();
            RequireForUpdate<MorrowindScriptCastRequest>();
        }

        protected override void OnUpdate()
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            DynamicBuffer<MorrowindScriptCastRequest> requests = EntityManager.GetBuffer<MorrowindScriptCastRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            // TODO: Replace this placeholder with the real scripted cast pipeline once actor spellcasting exists.
            requests.Clear();
        }
    }
}
