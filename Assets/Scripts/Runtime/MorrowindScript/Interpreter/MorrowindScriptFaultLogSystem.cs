using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.MorrowindScript
{
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptInterpreterSystem))]
    public partial struct MorrowindScriptFaultLogSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<MorrowindScriptInstance>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (instanceRef, entity) in SystemAPI.Query<RefRO<MorrowindScriptInstance>>()
                         .WithNone<MorrowindScriptFaultReported>()
                         .WithEntityAccess())
            {
                var instance = instanceRef.ValueRO;
                if (instance.Status != (byte)MorrowindScriptInstanceStatus.Faulted)
                    continue;

                uint placedRefId = SystemAPI.HasComponent<PlacedRefIdentity>(entity)
                    ? SystemAPI.GetComponent<PlacedRefIdentity>(entity).Value
                    : 0u;
                Debug.LogError($"[VVardenfell][MWScript][Validation] script fault entity={entity.Index}:{entity.Version} placedRef=0x{placedRefId:X8} programIndex={instance.ProgramIndex} reason='{instance.DisabledReason}'.");
                ecb.AddComponent<MorrowindScriptFaultReported>(entity);
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }
    }
}
