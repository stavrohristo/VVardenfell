using System;
using Unity.Burst;
using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.MorrowindScript
{
    [BurstCompile]
    [UpdateInGroup(typeof(MorrowindGameplayMutationSystemGroup))]
    [UpdateBefore(typeof(MorrowindScriptInterpreterSystem))]
    public partial struct MorrowindKnockdownOneFrameSystem : ISystem
    {
        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<ActorHitAftermathState>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState systemState)
        {
            foreach (var (aftermath, entity) in
                     SystemAPI.Query<RefRW<ActorHitAftermathState>>()
                         .WithEntityAccess())
            {
                if (!systemState.EntityManager.HasComponent<ActorScriptEventState>(entity))
                    throw new InvalidOperationException("[VVardenfell][Aftermath] Knockdown actor has no ActorScriptEventState.");

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

    }
}
