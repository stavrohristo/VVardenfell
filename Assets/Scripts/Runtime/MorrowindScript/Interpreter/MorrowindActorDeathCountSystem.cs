using System;
using Unity.Collections;
using Unity.Burst;
using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.MorrowindScript
{
    [BurstCompile]
    [UpdateInGroup(typeof(MorrowindGameplayMutationSystemGroup))]
    public partial struct MorrowindActorDeathCountSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<MorrowindScriptRuntimeState>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            if (!state.EntityManager.HasBuffer<MorrowindActorDeathCount>(runtimeEntity))
                throw new InvalidOperationException("Morrowind script runtime is missing actor death count state.");

            var deathCounts = state.EntityManager.GetBuffer<MorrowindActorDeathCount>(runtimeEntity);
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (source, vitals, entity)
                     in SystemAPI.Query<RefRO<ActorSpawnSource>, RefRO<ActorVitalSet>>()
                         .WithNone<MorrowindActorDeathCounted>()
                         .WithEntityAccess())
            {
                if (!state.EntityManager.HasComponent<ActorHitAftermathState>(entity))
                {
                    if (vitals.ValueRO.CurrentHealth <= 0f)
                        throw new InvalidOperationException("Actor reached zero health without ActorHitAftermathState.");
                    continue;
                }

                var aftermath = state.EntityManager.GetComponentData<ActorHitAftermathState>(entity);
                if (aftermath.Dead == 0)
                {
                    if (vitals.ValueRO.CurrentHealth <= 0f)
                        throw new InvalidOperationException("Actor reached zero health without explicit dead aftermath state.");
                    continue;
                }

                var actor = source.ValueRO.Definition;
                if (!actor.IsValid || (uint)actor.Index >= (uint)deathCounts.Length)
                    throw new InvalidOperationException("Actor death count references invalid actor handle.");

                var count = deathCounts[actor.Index];
                count.Count++;
                deathCounts[actor.Index] = count;
                ecb.AddComponent<MorrowindActorDeathCounted>(entity);
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }

    }
}
