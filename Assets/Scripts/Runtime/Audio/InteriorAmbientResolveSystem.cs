using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;
using Unity.Transforms;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Audio
{
    [UpdateInGroup(typeof(MorrowindAudioSimulationSystemGroup))]
    [UpdateAfter(typeof(AudioContextResolveSystem))]
    public partial class InteriorAmbientResolveSystem : SystemBase
    {
        static readonly ProfilerMarker k_InteriorResolve = new("VV.Audio.ResolveInteriorAmbient");

        EntityQuery _playerQuery;
        FixedString128Bytes _lastMissingInteriorId;
        bool _loggedMissingInterior;

        protected override void OnCreate()
        {
            RequireForUpdate<AudioContextState>();
            RequireForUpdate<InteriorAmbientState>();
            _playerQuery = GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadOnly<LocalTransform>());
        }

        protected override void OnUpdate()
        {
            using var _ = k_InteriorResolve.Auto();

            var contentDb = RuntimeContentDatabase.Active;
            ref var context = ref SystemAPI.GetSingletonRW<AudioContextState>().ValueRW;
            ref var ambient = ref SystemAPI.GetSingletonRW<InteriorAmbientState>().ValueRW;

            ambient.Looping = 1;

            if (context.Mode != AudioPlaybackMode.World || contentDb == null || _playerQuery.IsEmptyIgnoreFilter)
            {
                ambient.ResolvedSound = default;
                ambient.InteriorCellId = default;
                ambient.InteriorCellHash = 0UL;
                ambient.SourcePlacedRefId = 0u;
                ambient.SourcePosition = default;
                ambient.MinDistance = 0f;
                ambient.MaxDistance = 0f;
                return;
            }

            if (!SystemAPI.HasSingleton<InteriorTransitionState>())
            {
                ambient.ResolvedSound = default;
                ambient.InteriorCellId = default;
                ambient.InteriorCellHash = 0UL;
                ambient.SourcePlacedRefId = 0u;
                ambient.SourcePosition = default;
                ambient.MinDistance = 0f;
                ambient.MaxDistance = 0f;
                return;
            }

            var transition = SystemAPI.GetSingleton<InteriorTransitionState>();
            if (transition.InteriorActive == 0)
            {
                ambient.ResolvedSound = default;
                ambient.InteriorCellId = default;
                ambient.InteriorCellHash = 0UL;
                ambient.SourcePlacedRefId = 0u;
                ambient.SourcePosition = default;
                ambient.MinDistance = 0f;
                ambient.MaxDistance = 0f;
                return;
            }

            float3 playerPosition = _playerQuery.GetSingleton<LocalTransform>().Position;
            ambient.InteriorCellId = transition.ActiveInteriorCellId;
            ambient.InteriorCellHash = transition.ActiveInteriorCellHash;
            ambient.ResolvedSound = ResolveNearestInteriorAmbient(
                transition.ActiveInteriorCellHash,
                playerPosition,
                out uint sourcePlacedRefId,
                out float3 sourcePosition);
            ambient.SourcePlacedRefId = sourcePlacedRefId;
            ambient.SourcePosition = sourcePosition;

            if (ambient.ResolvedSound.IsValid)
            {
                ref readonly var sound = ref contentDb.Get(ambient.ResolvedSound);
                ambient.MinDistance = sound.MinRange;
                ambient.MaxDistance = math.max((float)sound.MinRange, (float)sound.MaxRange);
            }
            else
            {
                ambient.MinDistance = 0f;
                ambient.MaxDistance = 0f;
            }

            if (!ambient.ResolvedSound.IsValid)
            {
                if (!_loggedMissingInterior || !_lastMissingInteriorId.Equals(transition.ActiveInteriorCellId))
                {
                    _loggedMissingInterior = true;
                    _lastMissingInteriorId = transition.ActiveInteriorCellId;
                }
            }
            else
            {
                _loggedMissingInterior = false;
            }
        }

        SoundDefHandle ResolveNearestInteriorAmbient(
            ulong interiorCellHash,
            float3 playerPosition,
            out uint sourcePlacedRefId,
            out float3 sourcePosition)
        {
            SoundDefHandle resolved = default;
            sourcePlacedRefId = 0u;
            sourcePosition = default;
            float bestDistanceSq = float.MaxValue;

            foreach (var (ambientSource, location, placedRefId, transform) in SystemAPI
                         .Query<RefRO<InteriorAmbientSourceAuthoring>, RefRO<LogicalRefLocation>, RefRO<PlacedRefIdentity>, RefRO<LocalTransform>>()
                         .WithAll<LogicalRefTag, LightSourceAuthoring>())
            {
                if (location.ValueRO.IsInterior == 0 || location.ValueRO.InteriorCellHash != interiorCellHash)
                    continue;

                float distanceSq = math.distancesq(transform.ValueRO.Position, playerPosition);
                if (distanceSq > bestDistanceSq)
                    continue;

                if (math.abs(distanceSq - bestDistanceSq) <= 0.0001f && sourcePlacedRefId != 0u && placedRefId.ValueRO.Value >= sourcePlacedRefId)
                    continue;

                bestDistanceSq = distanceSq;
                sourcePlacedRefId = placedRefId.ValueRO.Value;
                sourcePosition = transform.ValueRO.Position;
                resolved = ambientSource.ValueRO.AmbientSound;
            }

            return resolved;
        }
    }
}
