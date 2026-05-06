using System;
using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.MorrowindScript
{
    [UpdateInGroup(typeof(MorrowindGameplayMutationSystemGroup))]
    [UpdateBefore(typeof(MorrowindScriptInterpreterSystem))]
    public partial struct MorrowindKnockdownOneFrameSystem : ISystem
    {
        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<ActorHitAftermathState>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            foreach (var (aftermath, entity) in
                     SystemAPI.Query<RefRW<ActorHitAftermathState>>()
                         .WithEntityAccess())
            {
                if (!systemState.EntityManager.HasComponent<ActorScriptEventState>(entity))
                    throw new InvalidOperationException($"[VVardenfell][Aftermath] Actor ref={PlacedRefId(ref systemState, entity)} has no ActorScriptEventState.");

                if (aftermath.ValueRO.Dead != 0 || aftermath.ValueRO.KnockedDown == 0)
                {
                    aftermath.ValueRW.KnockedDownOneFrame = 0;
                    aftermath.ValueRW.KnockedDownOverOneFrame = 0;
                }
                else if (aftermath.ValueRO.KnockedDownOneFrame == 0
                         && aftermath.ValueRO.KnockedDownOverOneFrame == 0)
                {
                    aftermath.ValueRW.KnockedDownOneFrame = 1;
                }
                else if (aftermath.ValueRO.KnockedDownOneFrame != 0
                         && aftermath.ValueRO.KnockedDownOverOneFrame == 0)
                {
                    aftermath.ValueRW.KnockedDownOneFrame = 0;
                    aftermath.ValueRW.KnockedDownOverOneFrame = 1;
                }

                var scriptState = systemState.EntityManager.GetComponentData<ActorScriptEventState>(entity);
                scriptState.KnockedDownOneFrame = aftermath.ValueRW.KnockedDownOneFrame;
                systemState.EntityManager.SetComponentData(entity, scriptState);
            }
        }

        uint PlacedRefId(ref SystemState systemState, Entity entity)
            => systemState.EntityManager.HasComponent<PlacedRefIdentity>(entity)
                ? systemState.EntityManager.GetComponentData<PlacedRefIdentity>(entity).Value
                : 0u;
    }
}
