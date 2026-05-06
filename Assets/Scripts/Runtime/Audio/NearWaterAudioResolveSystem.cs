using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Audio
{
    [UpdateInGroup(typeof(MorrowindAudioSimulationSystemGroup))]
    public partial struct NearWaterAudioResolveSystem : ISystem
    {
        const string NearWaterSoundId = "Water Layer";
        const float NearWaterRadiusMw = 1000f;
        const int NearWaterPoints = 8;
        const float NearWaterIndoorToleranceMw = 512f;
        const float NearWaterOutdoorToleranceMw = 1024f;

        EntityQuery _playerQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _playerQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadOnly<LocalToWorld>());
            systemState.RequireForUpdate<AudioContextState>();
            systemState.RequireForUpdate<NearWaterAudioState>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
            systemState.RequireForUpdate<RuntimeWorldCellBlobReference>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            ref RuntimeContentBlob contentBlob = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;
            var worldCellReference = SystemAPI.GetSingleton<RuntimeWorldCellBlobReference>();
            if (!worldCellReference.Blob.IsCreated)
                throw new System.InvalidOperationException("[VVardenfell][WorldCellBlob] near-water audio resolve requires runtime world cell blob.");
            ref RuntimeWorldCellBlob worldCells = ref worldCellReference.Blob.Value;
            ref var state = ref SystemAPI.GetSingletonRW<NearWaterAudioState>().ValueRW;
            state = default;

            if (SystemAPI.GetSingleton<AudioContextState>().Mode != AudioPlaybackMode.World)
                return;
            if (!RuntimeContentBlobUtility.TryGetSoundHandleByIdHash(ref contentBlob, RuntimeContentStableHash.HashId(NearWaterSoundId), out var sound) || !sound.IsValid)
                return;

            systemState.EntityManager.CompleteDependencyBeforeRO<LocalToWorld>();
            if (_playerQuery.IsEmptyIgnoreFilter)
                return;

            var playerPosition = _playerQuery.GetSingleton<LocalToWorld>().Position;
            bool interior = false;
            int interiorCellIndex = -1;
            if (SystemAPI.HasSingleton<InteriorTransitionState>())
            {
                var transition = SystemAPI.GetSingleton<InteriorTransitionState>();
                interior = transition.InteriorActive != 0
                           && RuntimeWorldCellBlobUtility.TryGetInteriorCellIndex(ref worldCells, transition.ActiveInteriorCellHash, out interiorCellIndex);
            }

            float volume = interior
                ? ResolveInteriorVolume(ref worldCells, interiorCellIndex, playerPosition)
                : ResolveExteriorVolume(
                    ref worldCells,
                    playerPosition,
                    SystemAPI.HasSingleton<StreamingConfig>(),
                    SystemAPI.HasSingleton<StreamingConfig>() ? SystemAPI.GetSingleton<StreamingConfig>() : default);

            if (volume <= 0f)
                return;

            state.ResolvedLoopSound = sound;
            state.Volume = math.saturate(volume);
            state.Looping = 1;
            state.IsInterior = (byte)(interior ? 1 : 0);
        }

        static float ResolveInteriorVolume(ref RuntimeWorldCellBlob worldCells, int cellIndex, float3 playerPosition)
        {
            ref RuntimeWorldCellDefBlob cell = ref RuntimeWorldCellBlobUtility.RequireCell(ref worldCells, cellIndex);
            if (cell.Environment.HasWater == 0)
                return 0f;

            float waterHeight = cell.Environment.WaterHeight * WorldScale.MwUnitsToMeters;
            float tolerance = NearWaterIndoorToleranceMw * WorldScale.MwUnitsToMeters;
            float distance = math.abs(waterHeight - playerPosition.y);
            return distance < tolerance ? (tolerance - distance) / tolerance : 0f;
        }

        static float ResolveExteriorVolume(ref RuntimeWorldCellBlob worldCells, float3 playerPosition, bool hasStreaming, StreamingConfig streaming)
        {
            if (!hasStreaming)
                return 0f;
            if (!RuntimeWorldCellBlobUtility.TryGetExteriorCellIndex(ref worldCells, streaming.CameraCell, out int cellIndex))
                return 0f;

            ref RuntimeWorldCellDefBlob cell = ref RuntimeWorldCellBlobUtility.RequireCell(ref worldCells, cellIndex);
            if (cell.Environment.HasWater == 0)
                return 0f;

            float tolerance = NearWaterOutdoorToleranceMw * WorldScale.MwUnitsToMeters;
            float waterHeight = cell.Environment.WaterHeight * WorldScale.MwUnitsToMeters;
            float distance = math.abs(waterHeight - playerPosition.y);
            if (distance >= tolerance)
                return 0f;

            if (playerPosition.y < waterHeight)
                return 1f;

            return SampleExteriorWaterCoverage(ref worldCells, playerPosition);
        }

        static float SampleExteriorWaterCoverage(ref RuntimeWorldCellBlob worldCells, float3 playerPosition)
        {
            float radius = NearWaterRadiusMw * WorldScale.MwUnitsToMeters;
            float step = radius * 2f / (NearWaterPoints - 1);
            int underwaterPoints = 0;

            for (int x = 0; x < NearWaterPoints; x++)
            {
                for (int z = 0; z < NearWaterPoints; z++)
                {
                    float sampleX = playerPosition.x - radius + x * step;
                    float sampleZ = playerPosition.z - radius + z * step;
                    if (TrySampleExteriorTerrainHeight(ref worldCells, sampleX, sampleZ, out float terrainHeight)
                        && terrainHeight < 0f)
                        underwaterPoints++;
                }
            }

            return underwaterPoints * 2f / (NearWaterPoints * NearWaterPoints);
        }

        static bool TrySampleExteriorTerrainHeight(ref RuntimeWorldCellBlob worldCells, float worldX, float worldZ, out float height)
        {
            float cellMeters = LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
            int2 coord = new(
                (int)math.floor(worldX / cellMeters),
                (int)math.floor(worldZ / cellMeters));

            float localX = worldX - coord.x * cellMeters;
            float localZ = worldZ - coord.y * cellMeters;
            return RuntimeWorldCellBlobUtility.TrySampleTerrainHeight(ref worldCells, coord, localX, localZ, out height);
        }
    }
}
