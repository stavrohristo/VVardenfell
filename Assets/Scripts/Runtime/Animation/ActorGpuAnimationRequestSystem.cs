#if VVARDENFELL_ACTOR_GPU_ANIMATION
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Animation
{
    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    [UpdateAfter(typeof(ActorAnimationControllerSystem))]
    public partial struct ActorGpuAnimationRequestSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency = new BuildGpuAnimationRequestsJob().ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(GPUAnimation))]
        partial struct BuildGpuAnimationRequestsJob : IJobEntity
        {
            void Execute(
                [ReadOnly] DynamicBuffer<ActorAnimationLayer> layers,
                DynamicBuffer<ActorGpuAnimationRequest> requests)
            {
                requests.Clear();

                for (int i = 0; i < layers.Length; i++)
                {
                    var layer = layers[i];
                    if (layer.Playing == 0 || layer.Weight <= 0f || layer.ClipIndex < 0)
                        continue;

                    requests.Add(new ActorGpuAnimationRequest
                    {
                        ClipIndex = layer.ClipIndex,
                        ClipHash = layer.ClipHash,
                        Time = layer.Time,
                        Weight = layer.Weight,
                        Priority = layer.Priority,
                        Mask = layer.Mask,
                    });
                }
            }
        }
    }
}
#endif
