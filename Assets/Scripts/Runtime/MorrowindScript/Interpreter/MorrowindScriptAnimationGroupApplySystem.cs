using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.WorldRefs;

namespace VVardenfell.Runtime.MorrowindScript
{
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptInterpreterSystem))]
    public partial class MorrowindScriptAnimationGroupApplySystem : SystemBase
    {
        const int ScriptedActorAnimationPriority = 10000;

        EntityQuery _runtimeQuery;

        protected override void OnCreate()
        {
            _runtimeQuery = GetEntityQuery(
                ComponentType.ReadOnly<MorrowindScriptRuntimeState>(),
                ComponentType.ReadWrite<MorrowindScriptAnimationGroupRequest>());

            RequireForUpdate(_runtimeQuery);
            RequireForUpdate<MorrowindScriptRuntimeCatalog>();
            RequireForUpdate<LogicalRefLookup>();
        }

        protected override void OnUpdate()
        {
            Entity runtimeEntity = _runtimeQuery.GetSingletonEntity();
            var requests = EntityManager.GetBuffer<MorrowindScriptAnimationGroupRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            var scriptCatalog = SystemAPI.GetSingleton<MorrowindScriptRuntimeCatalog>();
            if (!scriptCatalog.IsCreated)
                throw new InvalidOperationException("[VVardenfell][MWScript] Animation group request has no script runtime catalog.");

            var lookup = SystemAPI.GetSingleton<LogicalRefLookup>();
            for (int i = 0; i < requests.Length; i++)
                ApplyRequest(scriptCatalog.Messages, lookup, requests[i]);

            requests.Clear();
        }

        void ApplyRequest(NativeArray<FixedString512Bytes> messages, in LogicalRefLookup lookup, in MorrowindScriptAnimationGroupRequest request)
        {
            if ((uint)request.GroupMessageIndex >= (uint)messages.Length)
                throw new InvalidOperationException($"[VVardenfell][MWScript] Animation group request has invalid group message index {request.GroupMessageIndex}.");

            FixedString64Bytes group = ToFixed64(messages[request.GroupMessageIndex]);
            if (group.IsEmpty)
                throw new InvalidOperationException("[VVardenfell][MWScript] Animation group request has empty group.");

            Entity target = ResolveTarget(request, lookup);
            if (target == Entity.Null || !EntityManager.Exists(target))
                throw new InvalidOperationException($"[VVardenfell][MWScript] Animation group target ref={request.TargetPlacedRefId} is not live.");

            if (EntityManager.HasComponent<PlacedRefRuntimeState>(target)
                && EntityManager.GetComponentData<PlacedRefRuntimeState>(target).Disabled != 0)
            {
                return;
            }

            if (EntityManager.HasComponent<ObjectAnimationState>(target))
            {
                ApplyObjectAnimation(target, group, request);
                return;
            }

            if (EntityManager.HasComponent<ActorPresentation>(target)
                && EntityManager.HasComponent<ActorAnimationState>(target))
            {
                ApplyActorAnimation(target, group, request);
                return;
            }

            throw new InvalidOperationException($"[VVardenfell][MWScript] Animation group target ref={request.TargetPlacedRefId} has no supported animation state.");
        }

        Entity ResolveTarget(in MorrowindScriptAnimationGroupRequest request, in LogicalRefLookup lookup)
        {
            if (request.TargetEntity != Entity.Null && EntityManager.Exists(request.TargetEntity))
                return request.TargetEntity;

            if (request.TargetPlacedRefId != 0u && lookup.Map.IsCreated && lookup.Map.TryGetValue(request.TargetPlacedRefId, out Entity target))
                return target;

            return Entity.Null;
        }

        void ApplyActorAnimation(Entity target, FixedString64Bytes group, in MorrowindScriptAnimationGroupRequest request)
        {
            if (EqualsIgnoreCase(group, "idle"))
            {
                ClearScriptedActorOverlays(target);
                return;
            }

            if (!SystemAPI.HasSingleton<ActorAnimationBlobCatalog>())
                throw new InvalidOperationException($"[VVardenfell][MWScript] Actor animation group '{group}' has no actor animation catalog.");

            var catalogRef = SystemAPI.GetSingleton<ActorAnimationBlobCatalog>().Blob;
            if (!catalogRef.IsCreated)
                throw new InvalidOperationException($"[VVardenfell][MWScript] Actor animation group '{group}' has no actor animation catalog blob.");

            var presentation = EntityManager.GetComponentData<ActorPresentation>(target);
            ref var catalog = ref catalogRef.Value;
            ulong groupHash = ActorAnimationGroupHash.Hash(group);
            if (!ActorAnimationGroupLookupUtility.TryResolveGroup(ref catalog, presentation, groupHash, out var resolvedGroup))
                throw new InvalidOperationException($"[VVardenfell][MWScript] Actor animation group '{group}' is not present on target ref={request.TargetPlacedRefId}.");

            if (!EntityManager.HasBuffer<ActorAnimationOverlayState>(target))
                throw new InvalidOperationException($"[VVardenfell][MWScript] Actor animation target ref={request.TargetPlacedRefId} has no overlay buffer.");

            var overlays = EntityManager.GetBuffer<ActorAnimationOverlayState>(target);
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

        void ClearScriptedActorOverlays(Entity target)
        {
            if (!EntityManager.HasBuffer<ActorAnimationOverlayState>(target))
                return;

            RemoveScriptedActorOverlays(EntityManager.GetBuffer<ActorAnimationOverlayState>(target));
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

        void ApplyObjectAnimation(Entity target, FixedString64Bytes group, in MorrowindScriptAnimationGroupRequest request)
        {
            var state = EntityManager.GetComponentData<ObjectAnimationState>(target);
            if (EqualsIgnoreCase(group, "idle"))
            {
                state.Scripted = 0;
                state.LoopCount = 0u;
                EntityManager.SetComponentData(target, state);
                return;
            }

            if (!SystemAPI.HasSingleton<ObjectAnimationBlobCatalog>())
                throw new InvalidOperationException($"[VVardenfell][MWScript] Object animation group '{group}' has no object animation catalog.");

            var catalogRef = SystemAPI.GetSingleton<ObjectAnimationBlobCatalog>().Blob;
            if (!catalogRef.IsCreated)
                throw new InvalidOperationException($"[VVardenfell][MWScript] Object animation group '{group}' has no object animation catalog blob.");

            ref var catalog = ref catalogRef.Value;
            if (!TryResolveObjectGroup(ref catalog, state.ModelPrefabIndex, group, out var resolved))
                throw new InvalidOperationException($"[VVardenfell][MWScript] Object animation group '{group}' is not present on target ref={request.TargetPlacedRefId}.");

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
            EntityManager.SetComponentData(target, state);
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

        static bool EqualsIgnoreCase(FixedString64Bytes left, string right)
        {
            FixedString64Bytes fixedRight = default;
            fixedRight.CopyFromTruncated(right);
            return EqualsIgnoreCase(left, fixedRight);
        }

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
