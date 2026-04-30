using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Audio
{
    [UpdateInGroup(typeof(MorrowindAudioSimulationSystemGroup))]
    public partial class NearWaterAudioResolveSystem : SystemBase
    {
        const string NearWaterSoundId = "Water Layer";
        const float NearWaterRadiusMw = 1000f;
        const int NearWaterPoints = 8;
        const float NearWaterIndoorToleranceMw = 512f;
        const float NearWaterOutdoorToleranceMw = 1024f;

        EntityQuery _playerQuery;

        protected override void OnCreate()
        {
            _playerQuery = GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            RequireForUpdate<AudioContextState>();
            RequireForUpdate<NearWaterAudioState>();
        }

        protected override void OnUpdate()
        {
            var contentDb = RuntimeContentDatabase.Active;
            ref var state = ref SystemAPI.GetSingletonRW<NearWaterAudioState>().ValueRW;
            state = default;

            if (contentDb == null || SystemAPI.GetSingleton<AudioContextState>().Mode != AudioPlaybackMode.World)
                return;
            if (!contentDb.TryGetSoundHandle(NearWaterSoundId, out var sound) || !sound.IsValid)
                return;
            if (_playerQuery.IsEmptyIgnoreFilter)
                return;

            var playerPosition = _playerQuery.GetSingleton<LocalTransform>().Position;
            bool interior = false;
            CellData interiorCell = null;
            if (SystemAPI.HasSingleton<InteriorTransitionState>())
            {
                var transition = SystemAPI.GetSingleton<InteriorTransitionState>();
                interior = transition.InteriorActive != 0
                           && WorldResources.TryGetInteriorCell(transition.ActiveInteriorCellHash, out interiorCell)
                           && interiorCell != null;
            }

            float volume = interior
                ? ResolveInteriorVolume(interiorCell, playerPosition)
                : ResolveExteriorVolume(
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

        static float ResolveInteriorVolume(CellData cell, float3 playerPosition)
        {
            if (cell == null || cell.Environment.HasWater == 0)
                return 0f;

            float waterHeight = cell.Environment.WaterHeight * WorldScale.MwUnitsToMeters;
            float tolerance = NearWaterIndoorToleranceMw * WorldScale.MwUnitsToMeters;
            float distance = math.abs(waterHeight - playerPosition.y);
            return distance < tolerance ? (tolerance - distance) / tolerance : 0f;
        }

        static float ResolveExteriorVolume(float3 playerPosition, bool hasStreaming, StreamingConfig streaming)
        {
            if (!hasStreaming)
                return 0f;
            if (!WorldResources.Cells.TryGetValue(streaming.CameraCell, out var cell)
                || cell == null
                || cell.Environment.HasWater == 0)
                return 0f;

            float tolerance = NearWaterOutdoorToleranceMw * WorldScale.MwUnitsToMeters;
            float waterHeight = cell.Environment.WaterHeight * WorldScale.MwUnitsToMeters;
            float distance = math.abs(waterHeight - playerPosition.y);
            if (distance >= tolerance)
                return 0f;

            if (playerPosition.y < waterHeight)
                return 1f;

            return SampleExteriorWaterCoverage(playerPosition);
        }

        static float SampleExteriorWaterCoverage(float3 playerPosition)
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
                    if (TrySampleExteriorTerrainHeight(sampleX, sampleZ, out float terrainHeight)
                        && terrainHeight < 0f)
                        underwaterPoints++;
                }
            }

            return underwaterPoints * 2f / (NearWaterPoints * NearWaterPoints);
        }

        static bool TrySampleExteriorTerrainHeight(float worldX, float worldZ, out float height)
        {
            float cellMeters = LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
            int2 coord = new(
                (int)math.floor(worldX / cellMeters),
                (int)math.floor(worldZ / cellMeters));

            if (!WorldResources.Cells.TryGetValue(coord, out var cell) || cell == null)
            {
                height = 0f;
                return false;
            }

            float localX = worldX - coord.x * cellMeters;
            float localZ = worldZ - coord.y * cellMeters;
            return WorldTerrainStaticSpawnUtility.TrySampleTerrainHeight(cell, localX, localZ, out height);
        }
    }
}
