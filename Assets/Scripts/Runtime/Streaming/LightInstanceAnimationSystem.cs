using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Streaming
{
    [UpdateInGroup(typeof(MorrowindEnvironmentSystemGroup))]
    [UpdateAfter(typeof(LightingEnvironmentResolveSystem))]
    public partial struct LightInstanceAnimationSystem : ISystem
    {
        static readonly ProfilerMarker k_AnimateLights = new("VV.Lighting.AnimateInstances");

        EntityQuery _animatedLightQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _animatedLightQuery = systemState.GetEntityQuery(
                ComponentType.ReadWrite<LightInstanceState>(),
                ComponentType.ReadOnly<LightInstanceFlags>(),
                ComponentType.ReadOnly<LightInstanceAnimated>());

            systemState.RequireForUpdate(_animatedLightQuery);
        }

        public void OnUpdate(ref SystemState systemState)
        {
            using var _ = k_AnimateLights.Auto();

            float deltaTime = SystemAPI.Time.DeltaTime;
            foreach (var (stateRef, flagsRef) in SystemAPI.Query<RefRW<LightInstanceState>, RefRO<LightInstanceFlags>>()
                         .WithAll<LightInstanceAnimated>())
            {
                ref var state = ref stateRef.ValueRW;
                ref readonly var flags = ref flagsRef.ValueRO;

                state.AnimationTime += deltaTime;

                if (state.Enabled == 0)
                {
                    state.CurrentIntensity = 0f;
                    state.CurrentRange = state.BaseRange;
                    continue;
                }

                float modulation = 1f;
                if (flags.FlickerSlow != 0)
                    modulation = ComputeFlicker(state.AnimationTime, 2.7f, 0.72f);
                else if (flags.Flicker != 0)
                    modulation = ComputeFlicker(state.AnimationTime, 6.5f, 0.65f);
                else if (flags.PulseSlow != 0)
                    modulation = ComputePulse(state.AnimationTime, 1.6f, 0.22f);
                else if (flags.Pulse != 0)
                    modulation = ComputePulse(state.AnimationTime, 3.2f, 0.26f);

                state.CurrentIntensity = state.BaseIntensity * modulation;
                state.CurrentRange = state.BaseRange * math.lerp(0.92f, 1.06f, modulation);
            }
        }

        static float ComputePulse(float time, float speed, float amplitude)
        {
            float wave = 0.5f + 0.5f * math.sin(time * speed);
            return math.lerp(1f - amplitude, 1f + amplitude, wave);
        }

        static float ComputeFlicker(float time, float speed, float floor)
        {
            float a = 0.5f + 0.5f * math.sin(time * speed);
            float b = 0.5f + 0.5f * math.sin(time * (speed * 2.31f) + 1.13f);
            float c = 0.5f + 0.5f * math.sin(time * (speed * 4.13f) + 2.47f);
            float wave = math.saturate(a * 0.55f + b * 0.30f + c * 0.15f);
            return math.lerp(floor, 1.05f, wave);
        }
    }
}
