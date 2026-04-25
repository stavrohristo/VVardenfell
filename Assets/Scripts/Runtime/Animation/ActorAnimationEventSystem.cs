using System;
using Unity.Collections;
using Unity.Entities;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Animation
{
    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    [UpdateAfter(typeof(ActorAnimationGraphSystem))]
    public partial struct ActorAnimationEventSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ActorAnimationBlobCatalog>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var catalogRef = SystemAPI.GetSingleton<ActorAnimationBlobCatalog>().Blob;
            if (!catalogRef.IsCreated)
                return;

            ref var catalog = ref catalogRef.Value;
            foreach (var (controller, cursor, layers, events) in
                     SystemAPI.Query<RefRO<ActorAnimationController>, RefRW<ActorAnimationEventCursor>, DynamicBuffer<ActorAnimationLayer>, DynamicBuffer<ActorAnimationEvent>>()
                         .WithAll<ActorPresentation>())
            {
                if (controller.ValueRO.CurrentGroup.IsEmpty)
                    continue;

                bool groupChanged = !controller.ValueRO.CurrentGroup.Equals(cursor.ValueRO.LastGroup);
                float fromTime = groupChanged ? controller.ValueRO.StartTime : cursor.ValueRO.LastTime;
                float toTime = controller.ValueRO.Time;
                int clipIndex = ResolveClipIndex(controller.ValueRO, layers);

                if (groupChanged)
                {
                    EmitStartEvent(controller.ValueRO, events);
                    cursor.ValueRW.LastGroup = controller.ValueRO.CurrentGroup;
                    cursor.ValueRW.LastTime = toTime;
                }

                if ((uint)clipIndex < (uint)catalog.Clips.Length)
                {
                    var clip = catalog.Clips[clipIndex];
                    if (!groupChanged && toTime < fromTime)
                    {
                        EmitTextKeys(ref catalog, clip, controller.ValueRO.CurrentGroup, fromTime, clip.Duration, events);
                        EmitTextKeys(ref catalog, clip, controller.ValueRO.CurrentGroup, 0f, toTime, events);
                    }
                    else
                    {
                        EmitTextKeys(ref catalog, clip, controller.ValueRO.CurrentGroup, fromTime, toTime, events);
                    }
                }

                cursor.ValueRW.LastTime = toTime;
            }
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
            events.Add(new ActorAnimationEvent
            {
                Group = controller.CurrentGroup,
                Text = new FixedString128Bytes($"{controller.CurrentGroup}: start"),
                Time = controller.Time,
            });
        }

        static void EmitTextKeys(
            ref ActorAnimationCatalogBlob catalog,
            ActorAnimationClipBlob clip,
            FixedString64Bytes group,
            float fromTime,
            float toTime,
            DynamicBuffer<ActorAnimationEvent> events)
        {
            if (clip.FirstTextKeyIndex < 0 || clip.TextKeyCount <= 0 || toTime < fromTime)
                return;

            int end = Unity.Mathematics.math.min(catalog.TextKeys.Length, clip.FirstTextKeyIndex + clip.TextKeyCount);
            string groupPrefix = group.ToString() + ":";
            for (int i = clip.FirstTextKeyIndex; i < end; i++)
            {
                var key = catalog.TextKeys[i];
                if (key.Time <= fromTime || key.Time > toTime)
                    continue;

                string text = key.Text.ToString();
                foreach (string marker in SplitTextKeyMarkers(text))
                {
                    if (marker.EndsWith(": start", StringComparison.OrdinalIgnoreCase)
                        && !marker.StartsWith(groupPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    events.Add(new ActorAnimationEvent
                    {
                        Group = group,
                        Text = new FixedString128Bytes(marker),
                        Time = key.Time,
                    });
                }
            }
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
    }
}
