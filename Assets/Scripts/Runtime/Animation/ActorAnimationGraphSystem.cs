using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Animation
{
    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    [UpdateAfter(typeof(ActorAnimationStateResolveSystem))]
    public partial struct ActorAnimationGraphSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ActorAnimationBlobCatalog>();
        }

        public void OnUpdate(ref SystemState state)
        {
            float deltaTime = SystemAPI.Time.DeltaTime;
            var catalogRef = SystemAPI.GetSingleton<ActorAnimationBlobCatalog>().Blob;
            if (!catalogRef.IsCreated)
                return;

            ref var catalog = ref catalogRef.Value;
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (controller, presentation, layers, entity) in
                     SystemAPI.Query<RefRW<ActorAnimationController>, RefRO<ActorPresentation>, DynamicBuffer<ActorAnimationLayer>>()
                         .WithAll<ActorPresentation>()
                         .WithEntityAccess())
            {
                if (controller.ValueRO.Speed <= 0f)
                    controller.ValueRW.Speed = 1f;

                if (!controller.ValueRO.RequestedGroup.IsEmpty
                    && (!controller.ValueRO.RequestedGroup.Equals(controller.ValueRO.CurrentGroup)
                        || controller.ValueRO.Playing == 0))
                {
                    StartGroup(
                        ref controller.ValueRW,
                        layers,
                        presentation.ValueRO,
                        ref catalog,
                        controller.ValueRO.RequestedGroup);
                    if (state.EntityManager.HasComponent<ActorAnimationPoseDirty>(entity))
                        ecb.SetComponentEnabled<ActorAnimationPoseDirty>(entity, true);
                    else
                        ecb.AddComponent<ActorAnimationPoseDirty>(entity);
                }

                if (controller.ValueRO.Playing == 0)
                    continue;

                float previousTime = controller.ValueRO.Time;
                float nextTime = previousTime + deltaTime * controller.ValueRO.Speed;
                bool loop = controller.ValueRO.LoopCount > 0 && nextTime >= controller.ValueRO.LoopStopTime;
                if (loop)
                {
                    if (controller.ValueRO.LoopCount != uint.MaxValue)
                        controller.ValueRW.LoopCount--;
                    controller.ValueRW.Time = controller.ValueRO.LoopStartTime;
                }
                else
                {
                    controller.ValueRW.Time = nextTime >= controller.ValueRO.StopTime
                        ? controller.ValueRO.StopTime
                        : nextTime;
                }

                if (!loop && controller.ValueRW.Time >= controller.ValueRO.StopTime)
                {
                    controller.ValueRW.Playing = 0;
                    if (controller.ValueRO.AutoDisable != 0)
                        controller.ValueRW.CurrentGroup = default;
                }

                SyncLayerTimes(layers, controller.ValueRW);
                if (state.EntityManager.HasComponent<ActorAnimationPoseDirty>(entity))
                    ecb.SetComponentEnabled<ActorAnimationPoseDirty>(entity, true);
                else
                    ecb.AddComponent<ActorAnimationPoseDirty>(entity);
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }

        static void StartGroup(
            ref ActorAnimationController controller,
            DynamicBuffer<ActorAnimationLayer> layers,
            in ActorPresentation presentation,
            ref ActorAnimationCatalogBlob catalog,
            FixedString64Bytes group)
        {
            int clipIndex = ResolveClipIndex(ref catalog, presentation, group);
            ulong clipHash = ResolveClipHash(ref catalog, clipIndex);
            ResolveGroupWindow(ref catalog, clipIndex, group, out float startTime, out float loopStart, out float loopStop, out float stopTime);

            controller.CurrentGroup = group;
            controller.CurrentClipHash = clipHash;
            controller.Time = startTime;
            controller.Speed = controller.Speed <= 0f ? 1f : controller.Speed;
            controller.StartTime = startTime;
            controller.LoopStartTime = loopStart;
            controller.LoopStopTime = loopStop;
            controller.StopTime = stopTime;
            controller.LoopCount = IsLooping(group) ? uint.MaxValue : 0u;
            controller.Playing = 1;
            controller.AutoDisable = 0;
            controller.ActiveMask = ActorAnimationBlendMask.All;

            if (clipIndex < 0)
                return;

            if (layers.Length == 0)
            {
                layers.Add(new ActorAnimationLayer
                {
                    Group = group,
                    ClipIndex = clipIndex,
                    ClipHash = clipHash,
                    Time = startTime,
                    Weight = 1f,
                    Priority = 0,
                    Mask = ActorAnimationBlendMask.All,
                });
                return;
            }

            var layer = layers[0];
            layer.Group = group;
            layer.ClipIndex = clipIndex;
            layer.ClipHash = clipHash;
            layer.Time = startTime;
            layer.Weight = 1f;
            layer.Priority = 0;
            layer.Mask = ActorAnimationBlendMask.All;
            layers[0] = layer;
        }

        static void SyncLayerTimes(DynamicBuffer<ActorAnimationLayer> layers, in ActorAnimationController controller)
        {
            for (int i = 0; i < layers.Length; i++)
            {
                var layer = layers[i];
                if (layer.Group.IsEmpty || layer.Group.Equals(controller.CurrentGroup))
                    layer.Time = controller.Time;
                layers[i] = layer;
            }
        }

        static int ResolveClipIndex(
            ref ActorAnimationCatalogBlob catalog,
            in ActorPresentation presentation,
            FixedString64Bytes group)
        {
            if (presentation.FirstClipIndex < 0 || presentation.ClipCount <= 0)
                return -1;

            string groupName = group.ToString();
            int end = math.min(catalog.Clips.Length, presentation.FirstClipIndex + presentation.ClipCount);
            for (int i = end - 1; i >= presentation.FirstClipIndex; i--)
            {
                var clip = catalog.Clips[i];
                if (string.Equals(clip.Name.ToString(), groupName, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            for (int i = end - 1; i >= presentation.FirstClipIndex; i--)
            {
                var clip = catalog.Clips[i];
                if (ClipHasGroupTextKey(ref catalog, clip, groupName))
                    return i;
            }

            return presentation.FirstClipIndex < catalog.Clips.Length ? presentation.FirstClipIndex : -1;
        }

        static ulong ResolveClipHash(ref ActorAnimationCatalogBlob catalog, int clipIndex)
        {
            if ((uint)clipIndex >= (uint)catalog.Clips.Length)
                return 0UL;
            return catalog.Clips[clipIndex].AnimationHash;
        }

        static bool ClipHasGroupTextKey(ref ActorAnimationCatalogBlob catalog, ActorAnimationClipBlob clip, string groupName)
        {
            if (clip.FirstTextKeyIndex < 0 || clip.TextKeyCount <= 0)
                return false;

            int end = math.min(catalog.TextKeys.Length, clip.FirstTextKeyIndex + clip.TextKeyCount);
            string prefix = groupName + ":";
            for (int i = clip.FirstTextKeyIndex; i < end; i++)
            {
                string text = catalog.TextKeys[i].Text.ToString();
                foreach (string marker in SplitTextKeyMarkers(text))
                    if (marker.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        return true;
            }
            return false;
        }

        static void ResolveGroupWindow(
            ref ActorAnimationCatalogBlob catalog,
            int clipIndex,
            FixedString64Bytes group,
            out float startTime,
            out float loopStart,
            out float loopStop,
            out float stopTime)
        {
            startTime = 0f;
            loopStart = 0f;
            loopStop = 1f;
            stopTime = 1f;

            if ((uint)clipIndex >= (uint)catalog.Clips.Length)
                return;

            var clip = catalog.Clips[clipIndex];
            float duration = clip.Duration > 0f ? clip.Duration : 1f;
            loopStop = duration;
            stopTime = duration;

            if (clip.FirstTextKeyIndex < 0 || clip.TextKeyCount <= 0)
                return;

            string groupName = group.ToString();
            string startMarker = groupName + ": start";
            string groupPrefix = groupName + ":";
            int end = math.min(catalog.TextKeys.Length, clip.FirstTextKeyIndex + clip.TextKeyCount);
            bool insideGroup = false;
            bool finished = false;
            for (int i = clip.FirstTextKeyIndex; i < end; i++)
            {
                var key = catalog.TextKeys[i];
                foreach (string text in SplitTextKeyMarkers(key.Text.ToString()))
                {
                    if (text.StartsWith(groupPrefix, StringComparison.OrdinalIgnoreCase))
                        insideGroup = true;

                    if (string.Equals(text, startMarker, StringComparison.OrdinalIgnoreCase))
                    {
                        startTime = key.Time;
                        loopStart = key.Time;
                        insideGroup = true;
                        continue;
                    }

                    if (!insideGroup)
                        continue;

                    string marker = text;
                    if (text.StartsWith(groupPrefix, StringComparison.OrdinalIgnoreCase))
                        marker = text.Substring(groupPrefix.Length).Trim();

                    if (string.Equals(marker, "loop start", StringComparison.OrdinalIgnoreCase))
                        loopStart = key.Time;
                    else if (string.Equals(marker, "loop stop", StringComparison.OrdinalIgnoreCase))
                        loopStop = key.Time;
                    else if (string.Equals(marker, "stop", StringComparison.OrdinalIgnoreCase))
                    {
                        stopTime = key.Time;
                        finished = true;
                        break;
                    }
                    else if (text.EndsWith(": start", StringComparison.OrdinalIgnoreCase)
                             && !text.StartsWith(groupPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        stopTime = key.Time;
                        finished = true;
                        break;
                    }
                }

                if (finished)
                    break;
            }

            if (stopTime <= startTime)
                stopTime = duration;
            if (loopStart < startTime)
                loopStart = startTime;
            if (loopStop <= loopStart || loopStop > stopTime)
                loopStop = stopTime;
        }

        static string[] SplitTextKeyMarkers(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return Array.Empty<string>();

            string[] markers = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < markers.Length; i++)
                markers[i] = markers[i].Trim();
            return markers;
        }

        static bool IsLooping(FixedString64Bytes group)
        {
            return group.Equals(new FixedString64Bytes("idle"))
                || group.Equals(new FixedString64Bytes("idlesneak"))
                || group.Equals(new FixedString64Bytes("walkforward"))
                || group.Equals(new FixedString64Bytes("walkback"))
                || group.Equals(new FixedString64Bytes("walkleft"))
                || group.Equals(new FixedString64Bytes("walkright"))
                || group.Equals(new FixedString64Bytes("runforward"))
                || group.Equals(new FixedString64Bytes("runback"))
                || group.Equals(new FixedString64Bytes("runleft"))
                || group.Equals(new FixedString64Bytes("runright"))
                || group.Equals(new FixedString64Bytes("sneakforward"))
                || group.Equals(new FixedString64Bytes("sneakback"))
                || group.Equals(new FixedString64Bytes("sneakleft"))
                || group.Equals(new FixedString64Bytes("sneakright"));
        }
    }
}
