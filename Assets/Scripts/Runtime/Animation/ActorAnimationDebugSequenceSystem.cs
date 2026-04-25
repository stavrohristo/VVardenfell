using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Animation
{
    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    [UpdateAfter(typeof(ActorAnimationStateResolveSystem))]
    [UpdateBefore(typeof(ActorAnimationGraphSystem))]
    public partial class ActorAnimationDebugSequenceSystem : SystemBase
    {
        const bool Enabled = true;
        const double SecondsPerGroup = 2.5;

        readonly Dictionary<int, FixedString64Bytes[]> _groupsByBinding = new();

        protected override void OnCreate()
        {
            RequireForUpdate<ActorAnimationBlobCatalog>();
        }

        protected override void OnUpdate()
        {
            if (!Enabled)
                return;

            var catalogRef = SystemAPI.GetSingleton<ActorAnimationBlobCatalog>().Blob;
            if (!catalogRef.IsCreated)
                return;

            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (presentation, entity) in
                     SystemAPI.Query<RefRO<ActorPresentation>>()
                         .WithNone<ActorAnimationDebugSequence>()
                         .WithEntityAccess())
            {
                if (presentation.ValueRO.IsNpc == 0)
                    continue;

                ecb.AddComponent(entity, new ActorAnimationDebugSequence
                {
                    GroupIndex = -1,
                    NextSwitchTime = 0,
                });
            }

            ecb.Playback(EntityManager);
            ecb.Dispose();

            double now = SystemAPI.Time.ElapsedTime;
            ref var catalog = ref catalogRef.Value;
            foreach (var (presentation, controller, sequence) in
                     SystemAPI.Query<RefRO<ActorPresentation>, RefRW<ActorAnimationController>, RefRW<ActorAnimationDebugSequence>>())
            {
                if (presentation.ValueRO.IsNpc == 0)
                    continue;

                FixedString64Bytes[] groups = GetGroups(ref catalog, presentation.ValueRO);
                if (groups.Length == 0)
                    continue;

                if (sequence.ValueRO.CurrentGroup.IsEmpty || now >= sequence.ValueRO.NextSwitchTime)
                {
                    int next = (sequence.ValueRO.GroupIndex + 1) % groups.Length;
                    sequence.ValueRW.GroupIndex = next;
                    sequence.ValueRW.CurrentGroup = groups[next];
                    sequence.ValueRW.NextSwitchTime = now + SecondsPerGroup;
                }

                controller.ValueRW.RequestedGroup = sequence.ValueRO.CurrentGroup;
            }
        }

        FixedString64Bytes[] GetGroups(ref ActorAnimationCatalogBlob catalog, in ActorPresentation presentation)
        {
            int key = presentation.ModelBindingIndex;
            if (_groupsByBinding.TryGetValue(key, out var cached))
                return cached;

            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int first = presentation.FirstClipIndex;
            int count = presentation.ClipCount;
            int end = first >= 0 ? Unity.Mathematics.math.min(catalog.Clips.Length, first + count) : -1;
            for (int clipIndex = first; clipIndex >= 0 && clipIndex < end; clipIndex++)
            {
                var clip = catalog.Clips[clipIndex];
                int firstKey = clip.FirstTextKeyIndex;
                int keyEnd = firstKey >= 0
                    ? Unity.Mathematics.math.min(catalog.TextKeys.Length, firstKey + clip.TextKeyCount)
                    : -1;

                for (int keyIndex = firstKey; keyIndex >= 0 && keyIndex < keyEnd; keyIndex++)
                {
                    foreach (string marker in SplitTextKeyMarkers(catalog.TextKeys[keyIndex].Text.ToString()))
                        AddStartGroup(marker, names, seen);
                }
            }

            if (names.Count == 0 && first >= 0 && first < catalog.Clips.Length)
                AddGroupName(catalog.Clips[first].Name.ToString(), names, seen);

            var result = new FixedString64Bytes[names.Count];
            for (int i = 0; i < names.Count; i++)
                result[i] = new FixedString64Bytes(names[i].ToLowerInvariant());

            _groupsByBinding[key] = result;
            return result;
        }

        static void AddStartGroup(string marker, List<string> names, HashSet<string> seen)
        {
            int colon = marker.IndexOf(':');
            if (colon <= 0 || colon >= marker.Length - 1)
                return;

            string group = marker.Substring(0, colon).Trim();
            string action = marker.Substring(colon + 1).Trim();
            if (!string.Equals(action, "start", StringComparison.OrdinalIgnoreCase))
                return;

            AddGroupName(group, names, seen);
        }

        static void AddGroupName(string group, List<string> names, HashSet<string> seen)
        {
            if (string.IsNullOrWhiteSpace(group) || !seen.Add(group))
                return;

            names.Add(group);
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
