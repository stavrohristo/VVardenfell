using Unity.Collections;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Animation
{
    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    [UpdateAfter(typeof(ActorAnimationStateResolveSystem))]
    public partial struct ActorAnimationGraphSystem : ISystem
    {
        static readonly ProfilerMarker k_ResolveRequestedGroups = new("VV.ActorAnimationGraph.ResolveRequestedGroups");
        static readonly ProfilerMarker k_AdvanceControllers = new("VV.ActorAnimationGraph.AdvanceControllers");
        static readonly ProfilerMarker k_SyncLayerTimes = new("VV.ActorAnimationGraph.SyncLayerTimes");

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
            using (k_ResolveRequestedGroups.Auto())
            {
                state.Dependency = new ResolveRequestedGroupsJob
                {
                    Catalog = catalogRef,
                }.ScheduleParallel(state.Dependency);
            }

            using (k_AdvanceControllers.Auto())
            {
                state.Dependency = new AdvanceControllersJob
                {
                    DeltaTime = deltaTime,
                }.ScheduleParallel(state.Dependency);
            }

            using (k_SyncLayerTimes.Auto())
            {
                state.Dependency = new SyncLayerTimesJob().ScheduleParallel(state.Dependency);
            }
        }

        [BurstCompile]
        [WithAll(typeof(ActorPresentation))]
        partial struct ResolveRequestedGroupsJob : IJobEntity
        {
            [ReadOnly] public BlobAssetReference<ActorAnimationCatalogBlob> Catalog;

            void Execute(
                ref ActorAnimationController controller,
                in ActorPresentation presentation,
                DynamicBuffer<ActorAnimationLayer> layers,
                EnabledRefRW<ActorAnimationPoseDirty> poseDirty)
            {
                if (!Catalog.IsCreated)
                    return;

                if (controller.Speed <= 0f)
                    controller.Speed = 1f;

                if (controller.RequestedGroup.IsEmpty
                    || (controller.RequestedGroup.Equals(controller.CurrentGroup) && controller.Playing != 0))
                {
                    return;
                }

                ref var catalog = ref Catalog.Value;
                StartGroup(
                    ref controller,
                    layers,
                    presentation,
                    ref catalog,
                    controller.RequestedGroup);
                poseDirty.ValueRW = true;
            }
        }

        [BurstCompile]
        partial struct AdvanceControllersJob : IJobEntity
        {
            public float DeltaTime;

            void Execute(
                ref ActorAnimationController controller,
                in ActorPresentation presentation,
                DynamicBuffer<ActorAnimationLayer> layers,
                EnabledRefRW<ActorAnimationPoseDirty> poseDirty)
            {
                if (controller.Speed <= 0f)
                    controller.Speed = 1f;

                if (controller.Playing == 0)
                    return;

                float previousTime = controller.Time;
                byte previousPlaying = controller.Playing;
                var previousGroup = controller.CurrentGroup;

                float nextTime = previousTime + DeltaTime * controller.Speed;
                bool loop = controller.LoopCount > 0 && nextTime >= controller.LoopStopTime;
                if (loop)
                {
                    if (controller.LoopCount != uint.MaxValue)
                        controller.LoopCount--;
                    controller.Time = controller.LoopStartTime;
                }
                else
                {
                    controller.Time = nextTime >= controller.StopTime
                        ? controller.StopTime
                        : nextTime;
                }

                if (!loop && controller.Time >= controller.StopTime)
                {
                    controller.Playing = 0;
                    if (controller.AutoDisable != 0)
                        controller.CurrentGroup = default;
                }

                if (!MathEquals(previousTime, controller.Time)
                    || previousPlaying != controller.Playing
                    || !previousGroup.Equals(controller.CurrentGroup))
                {
                    poseDirty.ValueRW = true;
                }
            }
        }

        [BurstCompile]
        partial struct SyncLayerTimesJob : IJobEntity
        {
            void Execute(
                in ActorAnimationController controller,
                DynamicBuffer<ActorAnimationLayer> layers,
                EnabledRefRW<ActorAnimationPoseDirty> poseDirty)
            {
                if (layers.Length == 0)
                    return;

                bool changed = false;
                for (int i = 0; i < layers.Length; i++)
                {
                    var layer = layers[i];
                    if (!layer.Group.IsEmpty && !layer.Group.Equals(controller.CurrentGroup))
                        continue;

                    if (MathEquals(layer.Time, controller.Time))
                        continue;

                    layer.Time = controller.Time;
                    layers[i] = layer;
                    changed = true;
                }

                if (changed)
                    poseDirty.ValueRW = true;
            }
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
            if (EqualsIdle(group))
                return true;

            ulong hash = HashGroup(group);
            return hash == 11428733724764909075UL
                || hash == 2840003338041434093UL
                || hash == 13126534554455935837UL
                || hash == 15293321721980343883UL
                || hash == 17455567770838934929UL
                || hash == 8077834754628661618UL
                || hash == 10777559574499982535UL
                || hash == 14915820621887690617UL
                || hash == 3639883521381160167UL
                || hash == 13144730594257661592UL
                || hash == 3390669506715992788UL
                || hash == 9999511677273344816UL
                || hash == 14103176270394171182UL
                || hash == 17999522183465603107UL;
        }

        static ulong HashGroup(FixedString64Bytes value)
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            ulong hash = offset;
            for (int i = 0; i < value.Length; i++)
            {
                hash ^= ToLowerAscii(value[i]);
                hash *= prime;
            }

            return hash;
        }

        static bool EqualsIdle(FixedString64Bytes group)
            => group.Length == 4
               && ToLowerAscii(group[0]) == (byte)'i'
               && ToLowerAscii(group[1]) == (byte)'d'
               && ToLowerAscii(group[2]) == (byte)'l'
               && ToLowerAscii(group[3]) == (byte)'e';

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

        static bool MathEquals(float left, float right)
            => math.abs(left - right) <= 0.0001f;
    }
}
