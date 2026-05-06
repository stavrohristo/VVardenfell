using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Audio
{
    [UpdateInGroup(typeof(MorrowindAudioSimulationSystemGroup))]
    public partial struct RegionAmbientResolveSystem : ISystem
    {
        static readonly ProfilerMarker k_RegionResolve = new("VV.Audio.ResolveRegionAmbient");

        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<AudioContextState>();
            systemState.RequireForUpdate<RegionAmbientState>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
            systemState.RequireForUpdate<RuntimeWorldCellBlobReference>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            using var _ = k_RegionResolve.Auto();

            ref RuntimeContentBlob contentBlob = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;
            var worldCellReference = SystemAPI.GetSingleton<RuntimeWorldCellBlobReference>();
            if (!worldCellReference.Blob.IsCreated)
                throw new InvalidOperationException("[VVardenfell][WorldCellBlob] region ambient resolve requires runtime world cell blob.");
            ref RuntimeWorldCellBlob worldCells = ref worldCellReference.Blob.Value;
            ref var context = ref SystemAPI.GetSingletonRW<AudioContextState>().ValueRW;
            ref var regionState = ref SystemAPI.GetSingletonRW<RegionAmbientState>().ValueRW;

            regionState.PendingEventSound = default;

            if (context.Mode != AudioPlaybackMode.World)
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

            regionState.Region = ResolveExteriorRegion(ref contentBlob, ref worldCells, streaming.CameraCell, environment);
        }

        static RegionDefHandle ResolveExteriorRegion(ref RuntimeContentBlob contentBlob, ref RuntimeWorldCellBlob worldCells, int2 cameraCell, ActiveEnvironmentState environment)
        {
            if (RuntimeWorldCellBlobUtility.TryGetExteriorCellIndex(ref worldCells, cameraCell, out int cellIndex))
            {
                ref RuntimeWorldCellDefBlob cell = ref RuntimeWorldCellBlobUtility.RequireCell(ref worldCells, cellIndex);
                if (cell.Environment.RegionIdHash != 0UL
                    && RuntimeContentBlobUtility.TryGetRegionHandleByIdHash(ref contentBlob, cell.Environment.RegionIdHash, out var regionHandle)
                    && regionHandle.IsValid)
                {
                    return regionHandle;
                }
            }

            if (environment.RegionHandleValue > 0)
                return new RegionDefHandle { Value = environment.RegionHandleValue };

            return default;
        }
    }
}
