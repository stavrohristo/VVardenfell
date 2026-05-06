using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Animation
{
    [BurstCompile]
    [UpdateInGroup(typeof(MorrowindPresentationBuildSystemGroup))]
    public partial struct ObjectAnimationPlaybackSystem : ISystem
    {
        const int AudioSequenceStride = 64;

        EntityQuery _query;

        public void OnCreate(ref SystemState state)
        {
            _query = state.GetEntityQuery(
                ComponentType.ReadWrite<ObjectAnimationState>(),
                ComponentType.ReadOnly<LogicalRefLocation>(),
                ComponentType.ReadOnly<PlacedRefIdentity>(),
                ComponentType.ReadOnly<LocalTransform>());
            state.RequireForUpdate<ObjectAnimationBlobCatalog>();
            state.RequireForUpdate<LoadedCellsMap>();
            state.RequireForUpdate(_query);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var catalogRef = SystemAPI.GetSingleton<ObjectAnimationBlobCatalog>().Blob;
            if (!catalogRef.IsCreated)
                return;

            int objectCount = _query.CalculateEntityCount();
            uint sequenceBase = 0;
            byte emitAudio = 0;
            if (SystemAPI.HasSingleton<InteractionAudioRequestState>())
            {
                ref var audioState = ref SystemAPI.GetSingletonRW<InteractionAudioRequestState>().ValueRW;
                sequenceBase = audioState.NextSequence;
                audioState.NextSequence += (uint)math.max(1, objectCount * AudioSequenceStride + 1);
                emitAudio = 1;
            }

            var loadedCells = SystemAPI.GetSingleton<LoadedCellsMap>();
            byte paused = SystemAPI.HasSingleton<MorrowindRuntimePaused>() ? (byte)1 : (byte)0;
            uint openContainerPlacedRefId = 0u;
            if (paused != 0
                && SystemAPI.TryGetSingleton<ContainerWindowState>(out var containerWindow)
                && SystemAPI.TryGetSingleton<RuntimeShellState>(out var shell)
                && shell.ContainerOpen != 0)
            {
                openContainerPlacedRefId = containerWindow.OpenPlacedRefId;
            }
            byte interiorActive = 0;
            ulong activeInteriorCellHash = 0;
            if (SystemAPI.TryGetSingleton<InteriorTransitionState>(out var interiorTransition) && interiorTransition.InteriorActive != 0)
            {
                interiorActive = 1;
                activeInteriorCellHash = interiorTransition.ActiveInteriorCellHash;
            }

            var ecb = new EntityCommandBuffer(Allocator.TempJob);
            state.Dependency = new AdvanceObjectAnimationJob
            {
                Catalog = catalogRef,
                ActiveExteriorCells = loadedCells.Active,
                ActiveInteriorCellHash = activeInteriorCellHash,
                InteriorActive = interiorActive,
                HasActiveExteriorCells = loadedCells.Active.IsCreated ? (byte)1 : (byte)0,
                DeltaTime = SystemAPI.Time.DeltaTime,
                Paused = paused,
                OpenContainerPlacedRefId = openContainerPlacedRefId,
                Containers = SystemAPI.GetComponentLookup<ContainerAuthoring>(true),
                AudioSequenceBase = sequenceBase,
                EmitAudio = emitAudio,
                Ecb = ecb.AsParallelWriter(),
            }.ScheduleParallel(_query, state.Dependency);
            state.Dependency.Complete();
            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }

        [BurstCompile]
        partial struct AdvanceObjectAnimationJob : IJobEntity
        {
            [ReadOnly] public BlobAssetReference<ObjectAnimationCatalogBlob> Catalog;
            [ReadOnly] public NativeHashSet<int2> ActiveExteriorCells;
            public ulong ActiveInteriorCellHash;
            public byte InteriorActive;
            public byte HasActiveExteriorCells;
            public float DeltaTime;
            public byte Paused;
            public uint OpenContainerPlacedRefId;
            [ReadOnly] public ComponentLookup<ContainerAuthoring> Containers;
            public uint AudioSequenceBase;
            public byte EmitAudio;
            public EntityCommandBuffer.ParallelWriter Ecb;

            void Execute(
                [EntityIndexInQuery] int sortKey,
                Entity entity,
                ref ObjectAnimationState animation,
                in LogicalRefLocation location,
                in PlacedRefIdentity placedRef,
                in LocalTransform transform)
            {
                if (Paused != 0
                    && (OpenContainerPlacedRefId == 0u
                        || placedRef.Value != OpenContainerPlacedRefId
                        || !Containers.HasComponent(entity)))
                {
                    return;
                }

                animation.Active = IsLocationActive(location) ? (byte)1 : (byte)0;
                if (animation.Active == 0 || !Catalog.IsCreated)
                    return;

                ref var catalog = ref Catalog.Value;
                if ((uint)animation.ModelPrefabIndex >= (uint)catalog.Models.Length)
                    return;

                var model = catalog.Models[animation.ModelPrefabIndex];
                if (model.Enabled == 0 || model.ClipCount <= 0)
                    return;

                int clipIndex = animation.ClipIndex;
                if ((uint)clipIndex >= (uint)model.ClipCount)
                    clipIndex = 0;

                int globalClipIndex = model.FirstClipIndex + clipIndex;
                if ((uint)globalClipIndex >= (uint)catalog.Clips.Length)
                    return;

                var clip = catalog.Clips[globalClipIndex];
                float duration = math.max(0f, clip.Duration);
                if (duration <= 0f)
                    return;

                float previousTime = animation.CurrentTime;
                float currentTime;
                bool wrapped;
                if (animation.Scripted != 0)
                    AdvanceScripted(ref animation, duration, out currentTime, out wrapped);
                else
                    AdvanceLooping(ref animation, duration, out currentTime, out wrapped);

                animation.ClipIndex = clipIndex;
                animation.PreviousTime = previousTime;
                animation.CurrentTime = currentTime;

                if (EmitAudio != 0)
                    EmitSoundMarkers(ref catalog, clip, previousTime, currentTime, wrapped, sortKey, entity, placedRef.Value, transform.Position);
            }

            void AdvanceLooping(ref ObjectAnimationState animation, float duration, out float currentTime, out bool wrapped)
            {
                currentTime = animation.CurrentTime + math.max(0f, DeltaTime);
                wrapped = currentTime > duration;
                if (wrapped)
                    currentTime -= math.floor(currentTime / duration) * duration;
            }

            void AdvanceScripted(ref ObjectAnimationState animation, float duration, out float currentTime, out bool wrapped)
            {
                float start = math.clamp(animation.StartTime, 0f, duration);
                float loopStart = math.clamp(animation.LoopStartTime, 0f, duration);
                float loopStop = math.clamp(animation.LoopStopTime, loopStart, duration);
                float stop = animation.StopTime > 0f ? math.clamp(animation.StopTime, start, duration) : duration;
                float nextTime = animation.CurrentTime + math.max(0f, DeltaTime);
                wrapped = false;

                bool canLoop = animation.LoopCount > 0u && loopStop > loopStart;
                if (canLoop && nextTime >= loopStop)
                {
                    if (animation.LoopCount != uint.MaxValue)
                        animation.LoopCount--;

                    float loopDuration = loopStop - loopStart;
                    currentTime = loopStart + math.fmod(nextTime - loopStart, loopDuration);
                    wrapped = true;
                    return;
                }

                currentTime = nextTime >= stop ? stop : nextTime;
            }

            bool IsLocationActive(in LogicalRefLocation location)
            {
                if (InteriorActive != 0)
                    return location.IsInterior != 0 && location.InteriorCellHash == ActiveInteriorCellHash;

                if (location.IsInterior != 0 || HasActiveExteriorCells == 0)
                    return false;

                return ActiveExteriorCells.Contains(location.ExteriorCell);
            }

            void EmitSoundMarkers(
                ref ObjectAnimationCatalogBlob catalog,
                ObjectAnimationClipBlob clip,
                float previousTime,
                float currentTime,
                bool wrapped,
                int sortKey,
                Entity sourceEntity,
                uint placedRefId,
                float3 position)
            {
                if (clip.FirstTextMarkerIndex < 0 || clip.TextMarkerCount <= 0)
                    return;

                int emitted = 0;
                int end = math.min(catalog.TextMarkers.Length, clip.FirstTextMarkerIndex + clip.TextMarkerCount);
                for (int i = clip.FirstTextMarkerIndex; i < end; i++)
                {
                    var marker = catalog.TextMarkers[i];
                    if (!marker.Sound.IsValid || !CrossedMarker(marker.Time, previousTime, currentTime, wrapped))
                        continue;

                    Entity requestEntity = Ecb.CreateEntity(sortKey);
                    Ecb.AddComponent(sortKey, requestEntity, new MorrowindScriptAudioRequest
                    {
                        Sequence = AudioSequenceBase + (uint)(sortKey * AudioSequenceStride + emitted + 1),
                        Sound = marker.Sound,
                        SourceEntity = sourceEntity,
                        SourcePlacedRefId = placedRefId,
                        Position = position,
                        Volume = 1f,
                        Pitch = 1f,
                        Kind = (byte)MorrowindScriptAudioKind.PlaySound3DVP,
                        Spatial = 1,
                        Looping = 0,
                    });
                    emitted++;
                    if (emitted >= AudioSequenceStride)
                        return;
                }
            }

            static bool CrossedMarker(float markerTime, float previousTime, float currentTime, bool wrapped)
            {
                return wrapped
                    ? markerTime > previousTime || markerTime <= currentTime
                    : markerTime > previousTime && markerTime <= currentTime;
            }
        }
    }
}
