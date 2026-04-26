using Unity.Burst;
using Unity.Collections;
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
                in ActorAnimationController controller,
                [ReadOnly] DynamicBuffer<ActorAnimationLayer> layers,
                DynamicBuffer<ActorGpuAnimationRequest> requests)
            {
                requests.Clear();

                int selectedLayerIndex = -1;
                for (int i = 0; i < layers.Length; i++)
                {
                    var layer = layers[i];
                    if (layer.Weight <= 0f || layer.ClipIndex < 0)
                        continue;

                    if (selectedLayerIndex < 0)
                        selectedLayerIndex = i;

                    if (layer.ClipHash == controller.CurrentClipHash)
                    {
                        selectedLayerIndex = i;
                        break;
                    }
                }

                if (selectedLayerIndex < 0)
                    return;

                var selectedLayer = layers[selectedLayerIndex];
                requests.Add(new ActorGpuAnimationRequest
                {
                    ClipIndex = selectedLayer.ClipIndex,
                    ClipHash = selectedLayer.ClipHash,
                    Time = selectedLayer.Time,
                    Weight = selectedLayer.Weight,
                    Mask = ActorAnimationBlendMask.All,
                });
            }
        }
    }
}
