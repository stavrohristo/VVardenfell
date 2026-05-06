using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Animation
{
    [BurstCompile]
    [UpdateInGroup(typeof(MorrowindPresentationBuildSystemGroup))]
    [UpdateAfter(typeof(ActorGpuAnimationRequestSystem))]
    public partial struct ActorAnimationEventSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ActorAnimationBlobCatalog>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var catalogRef = SystemAPI.GetSingleton<ActorAnimationBlobCatalog>().Blob;
            if (!catalogRef.IsCreated)
                return;

            state.Dependency = new EmitActorAnimationEventsJob
            {
                Catalog = catalogRef,
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ActorPresentation))]
        partial struct EmitActorAnimationEventsJob : IJobEntity
        {
            [ReadOnly] public BlobAssetReference<ActorAnimationCatalogBlob> Catalog;

            void Execute(
                [ReadOnly] DynamicBuffer<ActorGpuAnimationRequest> requests,
                DynamicBuffer<ActorAnimationEvent> events)
            {
                events.Clear();
                if (!Catalog.IsCreated)
                    return;

                ref var catalog = ref Catalog.Value;
                for (int i = 0; i < requests.Length; i++)
                {
                    var request = requests[i];
                    if (request.Weight <= 0f
                        || request.SampleKey == 0
                        || (uint)request.ClipIndex >= (uint)catalog.Clips.Length
                        || IsDuplicateSample(requests, i, request))
                    {
                        continue;
                    }

                    var clip = catalog.Clips[request.ClipIndex];
                    float startTime = request.StartTime;
                    float stopTime = request.StopTime > startTime ? request.StopTime : clip.Duration;
                    float loopStartTime = request.LoopStartTime >= startTime ? request.LoopStartTime : startTime;
                    float loopStopTime = request.LoopStopTime > loopStartTime ? request.LoopStopTime : stopTime;
                    if (loopStopTime > stopTime)
                        loopStopTime = stopTime;

                    float fromTime = Unity.Mathematics.math.clamp(request.PreviousTime, startTime, stopTime);
                    float toTime = Unity.Mathematics.math.clamp(request.Time, startTime, stopTime);

                    if (toTime < fromTime)
                    {
                        EmitTextMarkers(ref catalog, clip, fromTime, loopStopTime, events);
                        EmitTextMarkers(ref catalog, clip, loopStartTime, toTime, events);
                    }
                    else
                    {
                        EmitTextMarkers(ref catalog, clip, fromTime, toTime, events);
                    }
                }
            }

            static bool IsDuplicateSample(
                DynamicBuffer<ActorGpuAnimationRequest> requests,
                int requestIndex,
                ActorGpuAnimationRequest request)
            {
                for (int i = 0; i < requestIndex; i++)
                {
                    var candidate = requests[i];
                    if (candidate.SampleKey == request.SampleKey
                        && candidate.ClipHash == request.ClipHash
                        && candidate.ClipIndex == request.ClipIndex
                        && candidate.PreviousTime == request.PreviousTime
                        && candidate.Time == request.Time)
                    {
                        return true;
                    }
                }

                return false;
            }

            static void EmitTextMarkers(
                ref ActorAnimationCatalogBlob catalog,
                ActorAnimationClipBlob clip,
                float fromTime,
                float toTime,
                DynamicBuffer<ActorAnimationEvent> events)
            {
                if (clip.FirstTextMarkerIndex < 0 || clip.TextMarkerCount <= 0 || toTime < fromTime)
                    return;

                int end = Unity.Mathematics.math.min(catalog.TextMarkers.Length, clip.FirstTextMarkerIndex + clip.TextMarkerCount);
                for (int i = clip.FirstTextMarkerIndex; i < end; i++)
                {
                    var marker = catalog.TextMarkers[i];
                    if (marker.Time <= fromTime || marker.Time > toTime)
                        continue;

                    events.Add(new ActorAnimationEvent
                    {
                        Group = marker.Group,
                        Value = marker.Value,
                        Text = marker.Text,
                        Time = marker.Time,
                        Kind = marker.Kind,
                    });
                }
            }
        }
    }
}
