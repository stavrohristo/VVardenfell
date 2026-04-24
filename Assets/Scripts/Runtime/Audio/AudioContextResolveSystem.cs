using Unity.Entities;
using Unity.Profiling;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Audio
{
    [UpdateInGroup(typeof(MorrowindAudioSimulationSystemGroup))]
    public partial class AudioContextResolveSystem : SystemBase
    {
        static readonly ProfilerMarker k_ContextResolve = new("VV.Audio.ResolveContext");

        AudioPlaybackMode _lastMode;
        BootstrapAudioPhase _lastPhase;
        bool _hasLoggedContext;

        protected override void OnCreate()
        {
            RequireForUpdate<AudioContextState>();
        }

        protected override void OnUpdate()
        {
            using var _ = k_ContextResolve.Auto();

            var phase = BootstrapPresentationAudioState.CurrentPhase;
            AudioPlaybackMode mode = ResolveMode(phase);

            ref var context = ref SystemAPI.GetSingletonRW<AudioContextState>().ValueRW;
            context.Mode = mode;
            context.BootstrapPhase = (byte)phase;

            if (!_hasLoggedContext || _lastMode != mode || _lastPhase != phase)
            {
                _hasLoggedContext = true;
                _lastMode = mode;
                _lastPhase = phase;
            }
        }

        static AudioPlaybackMode ResolveMode(BootstrapAudioPhase phase)
        {
            return phase switch
            {
                BootstrapAudioPhase.IntroLogo => AudioPlaybackMode.Menu,
                BootstrapAudioPhase.Menu => AudioPlaybackMode.Menu,
                BootstrapAudioPhase.Dismissed => AudioPlaybackMode.World,
                BootstrapAudioPhase.None => AudioPlaybackMode.Bootstrap,
                _ => AudioPlaybackMode.Bootstrap,
            };
        }
    }
}
