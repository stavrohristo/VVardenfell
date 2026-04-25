using Unity.Burst;
using Unity.Entities;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Animation
{
    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    [UpdateAfter(typeof(ActorAnimationGraphSystem))]
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
                DynamicBuffer<ActorAnimationLayer> layers,
                DynamicBuffer<ActorGpuAnimationRequest> requests)
            {
                requests.Clear();
                for (int i = 0; i < layers.Length; i++)
                {
                    var layer = layers[i];
                    if (layer.Weight <= 0f || layer.ClipIndex < 0)
                        continue;

                    requests.Add(new ActorGpuAnimationRequest
                    {
                        ClipIndex = layer.ClipIndex,
                        ClipHash = layer.ClipHash,
                        Time = layer.Time,
                        Weight = layer.Weight,
                        Mask = layer.Mask,
                    });
                }
            }
        }
    }
}
