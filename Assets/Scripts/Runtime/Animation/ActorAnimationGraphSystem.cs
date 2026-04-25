using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;
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

            int end = math.min(catalog.Clips.Length, presentation.FirstClipIndex + presentation.ClipCount);
            for (int i = end - 1; i >= presentation.FirstClipIndex; i--)
            {
                var clip = catalog.Clips[i];
                if (EqualsIgnoreCase(clip.Name, group))
                    return i;
            }

            for (int i = end - 1; i >= presentation.FirstClipIndex; i--)
            {
                var clip = catalog.Clips[i];
                if (ClipHasGroupTextKey(ref catalog, clip, group))
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

        static bool ClipHasGroupTextKey(ref ActorAnimationCatalogBlob catalog, ActorAnimationClipBlob clip, FixedString64Bytes group)
        {
            if (clip.FirstTextMarkerIndex < 0 || clip.TextMarkerCount <= 0)
                return false;

            int end = math.min(catalog.TextMarkers.Length, clip.FirstTextMarkerIndex + clip.TextMarkerCount);
            for (int i = clip.FirstTextMarkerIndex; i < end; i++)
                if (EqualsIgnoreCase(catalog.TextMarkers[i].Group, group))
                    return true;
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

            if (clip.FirstTextMarkerIndex < 0 || clip.TextMarkerCount <= 0)
                return;

            int end = math.min(catalog.TextMarkers.Length, clip.FirstTextMarkerIndex + clip.TextMarkerCount);
            bool insideGroup = false;
            bool finished = false;
            for (int i = clip.FirstTextMarkerIndex; i < end; i++)
            {
                var marker = catalog.TextMarkers[i];
                bool groupMatches = EqualsIgnoreCase(marker.Group, group);
                if (groupMatches)
                    insideGroup = true;

                if (groupMatches && marker.Kind == ActorAnimationTextMarkerKind.Start)
                {
                    startTime = marker.Time;
                    loopStart = marker.Time;
                    insideGroup = true;
                    continue;
                }

                if (!insideGroup)
                    continue;

                switch (marker.Kind)
                {
                    case ActorAnimationTextMarkerKind.LoopStart:
                        if (marker.Group.IsEmpty || groupMatches)
                            loopStart = marker.Time;
                        break;
                    case ActorAnimationTextMarkerKind.LoopStop:
                        if (marker.Group.IsEmpty || groupMatches)
                            loopStop = marker.Time;
                        break;
                    case ActorAnimationTextMarkerKind.Stop:
                        if (marker.Group.IsEmpty || groupMatches)
                        {
                            stopTime = marker.Time;
                            finished = true;
                        }
                        break;
                    case ActorAnimationTextMarkerKind.Start:
                        if (!marker.Group.IsEmpty && !groupMatches)
                        {
                            stopTime = marker.Time;
                            finished = true;
                        }
                        break;
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
            => value >= (byte)'A' && value <= (byte)'Z'
                ? (byte)(value + 32)
                : value;
    }
}
