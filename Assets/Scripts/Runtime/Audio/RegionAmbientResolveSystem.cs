using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Audio
{
    [UpdateInGroup(typeof(MorrowindAudioSimulationSystemGroup))]
    public partial class RegionAmbientResolveSystem : SystemBase
    {
        static readonly ProfilerMarker k_RegionResolve = new("VV.Audio.ResolveRegionAmbient");

        protected override void OnCreate()
        {
            RequireForUpdate<AudioContextState>();
            RequireForUpdate<RegionAmbientState>();
        }

        protected override void OnUpdate()
        {
            using var _ = k_RegionResolve.Auto();

            var contentDb = RuntimeContentDatabase.Active;
            ref var context = ref SystemAPI.GetSingletonRW<AudioContextState>().ValueRW;
            ref var regionState = ref SystemAPI.GetSingletonRW<RegionAmbientState>().ValueRW;

            regionState.PendingEventSound = default;

            if (context.Mode != AudioPlaybackMode.World || contentDb == null)
            {
                regionState.Region = default;
                return;
            }

            if (SystemAPI.HasSingleton<InteriorTransitionState>() && SystemAPI.GetSingleton<InteriorTransitionState>().InteriorActive != 0)
            {
                regionState.Region = default;
                return;
            }

            var streaming = SystemAPI.HasSingleton<StreamingConfig>()
                ? SystemAPI.GetSingleton<StreamingConfig>()
                : default;
            var environment = SystemAPI.HasSingleton<ActiveEnvironmentState>()
                ? SystemAPI.GetSingleton<ActiveEnvironmentState>()
                : default;

            regionState.Region = ResolveExteriorRegion(contentDb, streaming.CameraCell, environment);
        }

        static RegionDefHandle ResolveExteriorRegion(RuntimeContentDatabase contentDb, int2 cameraCell, ActiveEnvironmentState environment)
        {
            if (contentDb == null)
                return default;

            if (WorldResources.Cells.TryGetValue(cameraCell, out var cell)
                && cell != null
                && !string.IsNullOrWhiteSpace(cell.Environment.RegionId)
                && contentDb.TryGetRegionHandle(cell.Environment.RegionId, out var regionHandle))
                return regionHandle;

            if (environment.RegionHandleValue > 0)
                return new RegionDefHandle { Value = environment.RegionHandleValue };

            return default;
        }
    }
}
