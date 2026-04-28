#if VVARDENFELL_ACTOR_ANIMATION_EVENTS
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Animation
{
    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    [UpdateAfter(typeof(ActorAnimationControllerSystem))]
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
                in ActorAnimationController controller,
                ref ActorAnimationEventCursor cursor,
                DynamicBuffer<ActorAnimationLayer> layers,
                DynamicBuffer<ActorAnimationEvent> events)
            {
                events.Clear();
                if (!Catalog.IsCreated || controller.CurrentGroup.IsEmpty)
                    return;

                ref var catalog = ref Catalog.Value;
                bool groupChanged = !controller.CurrentGroup.Equals(cursor.LastGroup);
                float fromTime = groupChanged ? controller.StartTime : cursor.LastTime;
                float toTime = controller.Time;
                int clipIndex = ResolveClipIndex(controller, layers);

                if (groupChanged)
                {
                    EmitStartEvent(controller, events);
                    cursor.LastGroup = controller.CurrentGroup;
                    cursor.LastTime = toTime;
                }

                if ((uint)clipIndex < (uint)catalog.Clips.Length)
                {
                    var clip = catalog.Clips[clipIndex];
                    if (!groupChanged && toTime < fromTime)
                    {
                        EmitTextMarkers(ref catalog, clip, controller.CurrentGroup, fromTime, clip.Duration, events);
                        EmitTextMarkers(ref catalog, clip, controller.CurrentGroup, 0f, toTime, events);
                    }
                    else
                    {
                        EmitTextMarkers(ref catalog, clip, controller.CurrentGroup, fromTime, toTime, events);
                    }
                }

                cursor.LastTime = toTime;
            }

            static int ResolveClipIndex(in ActorAnimationController controller, DynamicBuffer<ActorAnimationLayer> layers)
            {
                for (int i = 0; i < layers.Length; i++)
                {
                    var layer = layers[i];
                    if (layer.ClipHash == controller.CurrentClipHash)
                        return layer.ClipIndex;
                }
                return layers.Length > 0 ? layers[0].ClipIndex : -1;
            }

            static void EmitStartEvent(in ActorAnimationController controller, DynamicBuffer<ActorAnimationEvent> events)
            {
                FixedString128Bytes text = default;
                text.Append(controller.CurrentGroup);
                text.Append((byte)':');
                text.Append((byte)' ');
                text.Append((byte)'s');
                text.Append((byte)'t');
                text.Append((byte)'a');
                text.Append((byte)'r');
                text.Append((byte)'t');
                events.Add(new ActorAnimationEvent
                {
                    Group = controller.CurrentGroup,
                    Value = StartValue(),
                    Text = text,
                    Time = controller.Time,
                    Kind = ActorAnimationTextMarkerKind.Start,
                });
            }

            static FixedString64Bytes StartValue()
            {
                FixedString64Bytes value = default;
                value.Append((byte)'s');
                value.Append((byte)'t');
                value.Append((byte)'a');
                value.Append((byte)'r');
                value.Append((byte)'t');
                return value;
            }

            static void EmitTextMarkers(
                ref ActorAnimationCatalogBlob catalog,
                ActorAnimationClipBlob clip,
                FixedString64Bytes group,
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

                    if (marker.Kind == ActorAnimationTextMarkerKind.Start
                        && !marker.Group.IsEmpty
                        && !EqualsIgnoreCase(marker.Group, group))
                        continue;

                    events.Add(new ActorAnimationEvent
                    {
                        Group = group,
                        Value = marker.Value,
                        Text = marker.Text,
                        Time = marker.Time,
                        Kind = marker.Kind,
                    });
                }
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
}
#endif
