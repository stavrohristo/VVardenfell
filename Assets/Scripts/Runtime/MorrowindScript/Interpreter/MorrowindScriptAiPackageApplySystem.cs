using Unity.Entities;
using Unity.Collections;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.MorrowindScript
{
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptInterpreterSystem))]
    public partial class MorrowindScriptAiPackageApplySystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<MorrowindScriptAiPackageRequest>();
            RequireForUpdate<LogicalRefLookup>();
            RequireForUpdate<RuntimeContentBlobReference>();
        }

        protected override void OnUpdate()
        {
            ref RuntimeContentBlob contentBlob = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;

            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = EntityManager.GetBuffer<MorrowindScriptAiPackageRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            var requestCopy = new NativeArray<MorrowindScriptAiPackageRequest>(requests.Length, Allocator.Temp);
            for (int i = 0; i < requests.Length; i++)
                requestCopy[i] = requests[i];
            requests.Clear();

            var lookup = SystemAPI.GetSingleton<LogicalRefLookup>();
            for (int i = 0; i < requestCopy.Length; i++)
                MorrowindScriptAiPackageUtility.TryApplyRequest(ref contentBlob, EntityManager, requestCopy[i], lookup);
            requestCopy.Dispose();
        }
    }
}
