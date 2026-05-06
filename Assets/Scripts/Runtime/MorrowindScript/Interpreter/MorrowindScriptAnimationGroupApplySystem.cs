using System;
using Unity.Collections;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.WorldRefs;

namespace VVardenfell.Runtime.MorrowindScript
{
    [BurstCompile]
    [UpdateInGroup(typeof(MorrowindGameplayMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptInterpreterSystem))]
    public partial struct MorrowindScriptAnimationGroupApplySystem : ISystem
    {
        const int ScriptedActorAnimationPriority = 10000;

        EntityQuery _runtimeQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _runtimeQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<MorrowindScriptRuntimeState>(),
                ComponentType.ReadWrite<MorrowindScriptAnimationGroupRequest>());

            systemState.RequireForUpdate(_runtimeQuery);
            systemState.RequireForUpdate<MorrowindScriptRuntimeCatalog>();
            systemState.RequireForUpdate<LogicalRefLookup>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState systemState)
        {
            Entity runtimeEntity = _runtimeQuery.GetSingletonEntity();
            var requests = systemState.EntityManager.GetBuffer<MorrowindScriptAnimationGroupRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            var scriptCatalog = SystemAPI.GetSingleton<MorrowindScriptRuntimeCatalog>();
            if (!scriptCatalog.IsCreated)
                throw new InvalidOperationException("[VVardenfell][MWScript] Animation group request has no script runtime catalog.");

            var lookup = SystemAPI.GetSingleton<LogicalRefLookup>();
            for (int i = 0; i < requests.Length; i++)
                ApplyRequest(ref systemState, scriptCatalog.Messages, lookup, requests[i]);

            requests.Clear();
        }

        void ApplyRequest(ref SystemState systemState, NativeArray<FixedString512Bytes> messages, in LogicalRefLookup lookup, in MorrowindScriptAnimationGroupRequest request)
        {
            if ((uint)request.GroupMessageIndex >= (uint)messages.Length)
                throw new InvalidOperationException("[VVardenfell][MWScript] Animation group request has invalid group message index.");

            FixedString64Bytes group = ToFixed64(messages[request.GroupMessageIndex]);
            if (group.IsEmpty)
                throw new InvalidOperationException("[VVardenfell][MWScript] Animation group request has empty group.");

            Entity target = ResolveTarget(ref systemState, request, lookup);
            if (target == Entity.Null || !systemState.EntityManager.Exists(target))
                throw new InvalidOperationException("[VVardenfell][MWScript] Animation group target is not live.");

            if (systemState.EntityManager.HasComponent<PlacedRefRuntimeState>(target)
                && systemState.EntityManager.GetComponentData<PlacedRefRuntimeState>(target).Disabled != 0)
            {
                return;
            }

            if (systemState.EntityManager.HasComponent<ObjectAnimationState>(target))
            {
                ApplyObjectAnimation(ref systemState, target, group, request);
                return;
            }

            if (systemState.EntityManager.HasComponent<ActorPresentation>(target)
                && systemState.EntityManager.HasComponent<ActorAnimationState>(target))
            {
                ApplyActorAnimation(ref systemState, target, group, request);
                return;
            }

            throw new InvalidOperationException("[VVardenfell][MWScript] Animation group target has no supported animation state.");
        }

        Entity ResolveTarget(ref SystemState systemState, in MorrowindScriptAnimationGroupRequest request, in LogicalRefLookup lookup)
        {
            if (request.TargetEntity != Entity.Null && systemState.EntityManager.Exists(request.TargetEntity))
                return request.TargetEntity;

            if (request.TargetPlacedRefId != 0u && lookup.Map.IsCreated && lookup.Map.TryGetValue(request.TargetPlacedRefId, out Entity target))
                return target;

            return Entity.Null;
        }

        void ApplyActorAnimation(ref SystemState systemState, Entity target, FixedString64Bytes group, in MorrowindScriptAnimationGroupRequest request)
        {
            if (IsIdleGroup(group))
            {
                ClearScriptedActorOverlays(ref systemState, target);
                return;
            }

            if (!SystemAPI.HasSingleton<ActorAnimationBlobCatalog>())
                throw new InvalidOperationException("[VVardenfell][MWScript] Actor animation group has no actor animation catalog.");

            var catalogRef = SystemAPI.GetSingleton<ActorAnimationBlobCatalog>().Blob;
            if (!catalogRef.IsCreated)
                throw new InvalidOperationException("[VVardenfell][MWScript] Actor animation group has no actor animation catalog blob.");

            var presentation = systemState.EntityManager.GetComponentData<ActorPresentation>(target);
            ref var catalog = ref catalogRef.Value;
            ulong groupHash = ActorAnimationGroupHash.Hash(group);
            if (!ActorAnimationGroupLookupUtility.TryResolveGroup(ref catalog, presentation, groupHash, out var resolvedGroup))
                throw new InvalidOperationException("[VVardenfell][MWScript] Actor animation group is not present on target.");

            if (!systemState.EntityManager.HasBuffer<ActorAnimationOverlayState>(target))
                throw new InvalidOperationException("[VVardenfell][MWScript] Actor animation target has no overlay buffer.");

            var overlays = systemState.EntityManager.GetBuffer<ActorAnimationOverlayState>(target);
            RemoveScriptedActorOverlays(overlays);

            ActorAnimationPlaybackState playback = default;
            ActorAnimationPlaybackUtility.Start(ref playback, resolvedGroup, ResolveActorLoopCount(request));
            playback.Speed = 1f;
            if (request.Mode == 2 && resolvedGroup.LoopStartTime > resolvedGroup.StartTime)
            {
                playback.PreviousTime = resolvedGroup.LoopStartTime;
                playback.Time = resolvedGroup.LoopStartTime;
            }

            overlays.Add(new ActorAnimationOverlayState
            {
                Playback = playback,
                Weight = 1f,
                Priority = ScriptedActorAnimationPriority,
                Mask = ActorAnimationBlendMask.All,
            });
        }

        void ClearScriptedActorOverlays(ref SystemState systemState, Entity target)
        {
            if (!systemState.EntityManager.HasBuffer<ActorAnimationOverlayState>(target))
                return;

            RemoveScriptedActorOverlays(systemState.EntityManager.GetBuffer<ActorAnimationOverlayState>(target));
        }

        static void RemoveScriptedActorOverlays(DynamicBuffer<ActorAnimationOverlayState> overlays)
        {
            for (int i = overlays.Length - 1; i >= 0; i--)
            {
                if (overlays[i].Priority == ScriptedActorAnimationPriority)
                    overlays.RemoveAt(i);
            }
        }

        static uint ResolveActorLoopCount(in MorrowindScriptAnimationGroupRequest request)
        {
            if (request.Operation == (byte)MorrowindScriptAnimationGroupOperation.Play)
                return uint.MaxValue;

            return request.LoopCount > 0u ? request.LoopCount - 1u : 0u;
        }

        void ApplyObjectAnimation(ref SystemState systemState, Entity target, FixedString64Bytes group, in MorrowindScriptAnimationGroupRequest request)
        {
            var state = systemState.EntityManager.GetComponentData<ObjectAnimationState>(target);
            if (IsIdleGroup(group))
            {
                state.Scripted = 0;
                state.LoopCount = 0u;
                systemState.EntityManager.SetComponentData(target, state);
                return;
            }

            if (!SystemAPI.HasSingleton<ObjectAnimationBlobCatalog>())
                throw new InvalidOperationException("[VVardenfell][MWScript] Object animation group has no object animation catalog.");

            var catalogRef = SystemAPI.GetSingleton<ObjectAnimationBlobCatalog>().Blob;
            if (!catalogRef.IsCreated)
                throw new InvalidOperationException("[VVardenfell][MWScript] Object animation group has no object animation catalog blob.");

            ref var catalog = ref catalogRef.Value;
            if (!TryResolveObjectGroup(ref catalog, state.ModelPrefabIndex, group, out var resolved))
                throw new InvalidOperationException("[VVardenfell][MWScript] Object animation group is not present on target.");

            float start = request.Mode == 2 && resolved.LoopStartTime > resolved.StartTime
                ? resolved.LoopStartTime
                : resolved.StartTime;
            state.ClipIndex = resolved.LocalClipIndex;
            state.PreviousTime = start;
            state.CurrentTime = start;
            state.StartTime = resolved.StartTime;
            state.LoopStartTime = resolved.LoopStartTime;
            state.LoopStopTime = resolved.LoopStopTime;
            state.StopTime = resolved.StopTime;
            state.LoopCount = ResolveObjectLoopCount(request);
            state.Scripted = 1;
            systemState.EntityManager.SetComponentData(target, state);
        }

        static uint ResolveObjectLoopCount(in MorrowindScriptAnimationGroupRequest request)
        {
            if (request.Operation == (byte)MorrowindScriptAnimationGroupOperation.Play)
                return uint.MaxValue;

            return request.LoopCount;
        }

        static bool TryResolveObjectGroup(
            ref ObjectAnimationCatalogBlob catalog,
            int modelPrefabIndex,
            FixedString64Bytes group,
            out ResolvedObjectAnimationGroup resolved)
        {
            resolved = default;
            if ((uint)modelPrefabIndex >= (uint)catalog.Models.Length)
                return false;

            var model = catalog.Models[modelPrefabIndex];
            if (model.Enabled == 0 || model.ClipCount <= 0)
                return false;

            int clipEnd = math.min(catalog.Clips.Length, model.FirstClipIndex + model.ClipCount);
            for (int globalClipIndex = model.FirstClipIndex; globalClipIndex < clipEnd; globalClipIndex++)
            {
                var clip = catalog.Clips[globalClipIndex];
                bool found = EqualsIgnoreCase(clip.Name, group);
                float start = 0f;
                float loopStart = 0f;
                float loopStop = clip.Duration;
                float stop = clip.Duration;

                int markerEnd = clip.FirstTextMarkerIndex < 0
                    ? -1
                    : math.min(catalog.TextMarkers.Length, clip.FirstTextMarkerIndex + clip.TextMarkerCount);
                for (int markerIndex = clip.FirstTextMarkerIndex; markerIndex >= 0 && markerIndex < markerEnd; markerIndex++)
                {
                    var marker = catalog.TextMarkers[markerIndex];
                    if (!EqualsIgnoreCase(marker.Group, group))
                        continue;

                    found = true;
                    switch (marker.Kind)
                    {
                        case ActorAnimationTextMarkerKind.Start:
                            start = marker.Time;
                            break;
                        case ActorAnimationTextMarkerKind.LoopStart:
                            loopStart = marker.Time;
                            break;
                        case ActorAnimationTextMarkerKind.LoopStop:
                            loopStop = marker.Time;
                            break;
                        case ActorAnimationTextMarkerKind.Stop:
                            stop = marker.Time;
                            break;
                    }
                }

                if (!found)
                    continue;

                resolved = new ResolvedObjectAnimationGroup
                {
                    LocalClipIndex = globalClipIndex - model.FirstClipIndex,
                    StartTime = math.clamp(start, 0f, clip.Duration),
                    LoopStartTime = math.clamp(loopStart > 0f ? loopStart : start, 0f, clip.Duration),
                    LoopStopTime = math.clamp(loopStop > 0f ? loopStop : stop, 0f, clip.Duration),
                    StopTime = math.clamp(stop > 0f ? stop : clip.Duration, 0f, clip.Duration),
                };
                if (resolved.LoopStopTime < resolved.LoopStartTime)
                    resolved.LoopStopTime = resolved.LoopStartTime;
                if (resolved.StopTime < resolved.StartTime)
                    resolved.StopTime = resolved.StartTime;
                return true;
            }

            return false;
        }

        static FixedString64Bytes ToFixed64(FixedString512Bytes value)
        {
            FixedString64Bytes result = default;
            result.Append(value);
            return result;
        }

        static bool IsIdleGroup(FixedString64Bytes value)
            => value.Length == 4
               && ToLowerAscii(value[0]) == (byte)'i'
               && ToLowerAscii(value[1]) == (byte)'d'
               && ToLowerAscii(value[2]) == (byte)'l'
               && ToLowerAscii(value[3]) == (byte)'e';

        static bool EqualsIgnoreCase(FixedString64Bytes left, FixedString64Bytes right)
        {
            if (left.Length != right.Length)
                return false;

            for (int i = 0; i < left.Length; i++)
            {
                byte a = ToLowerAscii(left[i]);
                byte b = ToLowerAscii(right[i]);
                if (a != b)
                    return false;
            }

            return true;
        }

        static byte ToLowerAscii(byte value)
            => value >= (byte)'A' && value <= (byte)'Z' ? (byte)(value + 32) : value;

        struct ResolvedObjectAnimationGroup
        {
            public int LocalClipIndex;
            public float StartTime;
            public float LoopStartTime;
            public float LoopStopTime;
            public float StopTime;
        }
    }
}
