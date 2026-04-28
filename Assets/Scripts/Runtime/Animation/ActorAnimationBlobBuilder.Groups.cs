using System;
using Unity.Collections;
using VVardenfell.Core.Cache;

namespace VVardenfell.Runtime.Animation
{
    public static partial class ActorAnimationBlobBuilder
    {
        struct RuntimeAnimationGroup
        {
            public ulong GroupHash;
            public ulong ClipHash;
            public int ClipIndex;
            public int RigFamilyIndex;
            public float StartTime;
            public float LoopStartTime;
            public float LoopStopTime;
            public float StopTime;
            public byte Looping;
        }

        struct RuntimeTextMarker
        {
            public float Time;
            public string Group;
            public string Value;
            public string Text;
            public ActorAnimationTextMarkerKind Kind;
        }

        struct RuntimeRigFamilyAnimationIndex
        {
            public int FirstGroupLookupIndex;
            public int GroupLookupCount;
        }

        struct RuntimeGroupLookup
        {
            public ulong GroupHash;
            public int GroupIndex;
        }

        static RuntimeTextMarker[] BuildRuntimeTextMarkers(
            ActorAnimationCatalogData source,
            out int[] clipTextMarkerStarts,
            out int[] clipTextMarkerCounts)
        {
            var clips = source.Clips ?? Array.Empty<ActorAnimationClipDef>();
            var sourceMarkers = source.TextMarkers ?? Array.Empty<ActorAnimationTextMarkerDef>();
            var textKeys = source.TextKeys ?? Array.Empty<ActorAnimationTextKeyDef>();
            clipTextMarkerStarts = new int[clips.Length];
            clipTextMarkerCounts = new int[clips.Length];
            Array.Fill(clipTextMarkerStarts, -1);

            var markers = new System.Collections.Generic.List<RuntimeTextMarker>(sourceMarkers.Length);
            for (int clipIndex = 0; clipIndex < clips.Length; clipIndex++)
            {
                var clip = clips[clipIndex];
                if (clip == null)
                    continue;

                int firstMarker = markers.Count;
                int markerEnd = Math.Min(sourceMarkers.Length, clip.FirstTextMarkerIndex + clip.TextMarkerCount);
                if (clip.FirstTextMarkerIndex >= 0 && clip.TextMarkerCount > 0)
                {
                    for (int markerIndex = clip.FirstTextMarkerIndex; markerIndex < markerEnd; markerIndex++)
                    {
                        var marker = sourceMarkers[markerIndex];
                        markers.Add(new RuntimeTextMarker
                        {
                            Time = marker.Time,
                            Group = marker.Group,
                            Value = marker.Value,
                            Text = marker.Text,
                            Kind = marker.Kind,
                        });
                    }
                }
                else if (clip.FirstTextKeyIndex >= 0 && clip.TextKeyCount > 0)
                {
                    int keyEnd = Math.Min(textKeys.Length, clip.FirstTextKeyIndex + clip.TextKeyCount);
                    for (int keyIndex = clip.FirstTextKeyIndex; keyIndex < keyEnd; keyIndex++)
                        AddRuntimeTextMarkers(textKeys[keyIndex], markers);
                }

                int markerCount = markers.Count - firstMarker;
                if (markerCount <= 0)
                    continue;

                clipTextMarkerStarts[clipIndex] = firstMarker;
                clipTextMarkerCounts[clipIndex] = markerCount;
            }

            return markers.ToArray();
        }

        static RuntimeAnimationGroup[] BuildRuntimeGroups(
            ActorAnimationCatalogData source,
            RuntimeTextMarker[] runtimeTextMarkers,
            int[] clipTextMarkerStarts,
            int[] clipTextMarkerCounts)
        {
            var clips = source.Clips ?? Array.Empty<ActorAnimationClipDef>();
            var rigFamilies = source.RigFamilies ?? Array.Empty<ActorRigFamilyDef>();
            var groups = new System.Collections.Generic.List<RuntimeAnimationGroup>(clips.Length);
            runtimeTextMarkers ??= Array.Empty<RuntimeTextMarker>();

            for (int rigFamilyIndex = 0; rigFamilyIndex < rigFamilies.Length; rigFamilyIndex++)
            {
                var rigFamily = rigFamilies[rigFamilyIndex];
                int firstClipIndex = rigFamily?.FirstClipIndex ?? -1;
                int clipCount = rigFamily?.ClipCount ?? 0;
                if (firstClipIndex < 0 || clipCount <= 0)
                    continue;

                int clipEnd = Math.Min(clips.Length, firstClipIndex + clipCount);
                string modelPath = rigFamily?.SkeletonModelPath;
                for (int clipIndex = firstClipIndex; clipIndex < clipEnd; clipIndex++)
                {
                    var clip = clips[clipIndex];
                    if (clip == null)
                        continue;

                    ulong clipHash = ActorAnimationHash.Clip(modelPath, clip.Name);
                    if (!string.IsNullOrWhiteSpace(clip.Name))
                        AddRuntimeGroup(groups, source, clipIndex, rigFamilyIndex, clipHash, clip.Name, runtimeTextMarkers, clipTextMarkerStarts, clipTextMarkerCounts);

                    int markerStart = (uint)clipIndex < (uint)clipTextMarkerStarts.Length ? clipTextMarkerStarts[clipIndex] : -1;
                    int markerCount = (uint)clipIndex < (uint)clipTextMarkerCounts.Length ? clipTextMarkerCounts[clipIndex] : 0;
                    int markerEnd = markerStart >= 0 ? Math.Min(runtimeTextMarkers.Length, markerStart + markerCount) : -1;
                    for (int markerIndex = markerStart; markerIndex >= 0 && markerIndex < markerEnd; markerIndex++)
                    {
                        string group = runtimeTextMarkers[markerIndex].Group;
                        if (string.IsNullOrWhiteSpace(group))
                            continue;
                        AddRuntimeGroup(groups, source, clipIndex, rigFamilyIndex, clipHash, group, runtimeTextMarkers, clipTextMarkerStarts, clipTextMarkerCounts);
                    }
                }
            }

            return groups.ToArray();
        }

        static void BuildRuntimeGroupIndexes(
            ActorAnimationCatalogData source,
            RuntimeAnimationGroup[] groups,
            out RuntimeRigFamilyAnimationIndex[] rigFamilyIndexes,
            out RuntimeGroupLookup[] groupLookups)
        {
            int rigFamilyCount = source.RigFamilies?.Length ?? 0;
            groups ??= Array.Empty<RuntimeAnimationGroup>();

            rigFamilyIndexes = new RuntimeRigFamilyAnimationIndex[rigFamilyCount];
            var lookupList = new System.Collections.Generic.List<RuntimeGroupLookup>(groups.Length);

            for (int rigFamilyIndex = 0; rigFamilyIndex < rigFamilyCount; rigFamilyIndex++)
            {
                var index = default(RuntimeRigFamilyAnimationIndex);

                index.FirstGroupLookupIndex = lookupList.Count;
                for (int groupIndex = 0; groupIndex < groups.Length; groupIndex++)
                {
                    if (groups[groupIndex].RigFamilyIndex != rigFamilyIndex)
                        continue;

                    lookupList.Add(new RuntimeGroupLookup { GroupHash = groups[groupIndex].GroupHash, GroupIndex = groupIndex });
                }
                index.GroupLookupCount = lookupList.Count - index.FirstGroupLookupIndex;
                if (index.GroupLookupCount > 1)
                {
                    lookupList.Sort(
                        index.FirstGroupLookupIndex,
                        index.GroupLookupCount,
                        RuntimeGroupLookupComparer.Instance);
                }
                rigFamilyIndexes[rigFamilyIndex] = index;
            }

            groupLookups = lookupList.ToArray();
        }

        sealed class RuntimeGroupLookupComparer : System.Collections.Generic.IComparer<RuntimeGroupLookup>
        {
            public static readonly RuntimeGroupLookupComparer Instance = new();

            public int Compare(RuntimeGroupLookup left, RuntimeGroupLookup right)
            {
                int hashCompare = left.GroupHash.CompareTo(right.GroupHash);
                return hashCompare != 0
                    ? hashCompare
                    : left.GroupIndex.CompareTo(right.GroupIndex);
            }
        }

        static void AddRuntimeGroup(
            System.Collections.Generic.List<RuntimeAnimationGroup> groups,
            ActorAnimationCatalogData source,
            int clipIndex,
            int rigFamilyIndex,
            ulong clipHash,
            string group,
            RuntimeTextMarker[] runtimeTextMarkers,
            int[] clipTextMarkerStarts,
            int[] clipTextMarkerCounts)
        {
            FixedString64Bytes fixedGroup = Fixed64(group);
            if (fixedGroup.IsEmpty)
                return;

            ulong groupHash = ActorAnimationGroupHash.Hash(fixedGroup);
            for (int i = 0; i < groups.Count; i++)
            {
                if (groups[i].RigFamilyIndex == rigFamilyIndex
                    && groups[i].ClipIndex == clipIndex
                    && groups[i].GroupHash == groupHash)
                {
                    return;
                }
            }

            ResolveGroupWindow(source, clipIndex, group, runtimeTextMarkers, clipTextMarkerStarts, clipTextMarkerCounts, out float start, out float loopStart, out float loopStop, out float stop);
            groups.Add(new RuntimeAnimationGroup
            {
                GroupHash = groupHash,
                ClipHash = clipHash,
                ClipIndex = clipIndex,
                RigFamilyIndex = rigFamilyIndex,
                StartTime = start,
                LoopStartTime = loopStart,
                LoopStopTime = loopStop,
                StopTime = stop,
                Looping = IsLoopingGroup(fixedGroup, clipIndex, runtimeTextMarkers, clipTextMarkerStarts, clipTextMarkerCounts) ? (byte)1 : (byte)0,
            });
        }

        static void ResolveGroupWindow(
            ActorAnimationCatalogData source,
            int clipIndex,
            string group,
            RuntimeTextMarker[] runtimeTextMarkers,
            int[] clipTextMarkerStarts,
            int[] clipTextMarkerCounts,
            out float start,
            out float loopStart,
            out float loopStop,
            out float stop)
        {
            var clips = source.Clips ?? Array.Empty<ActorAnimationClipDef>();
            var clip = (uint)clipIndex < (uint)clips.Length ? clips[clipIndex] : null;
            float duration = clip?.Duration > 0f ? clip.Duration : 1f;
            start = 0f;
            loopStart = 0f;
            loopStop = duration;
            stop = duration;

            int markerStart = (uint)clipIndex < (uint)clipTextMarkerStarts.Length ? clipTextMarkerStarts[clipIndex] : -1;
            int markerCount = (uint)clipIndex < (uint)clipTextMarkerCounts.Length ? clipTextMarkerCounts[clipIndex] : 0;
            int markerEnd = markerStart >= 0 ? Math.Min(runtimeTextMarkers.Length, markerStart + markerCount) : -1;
            bool insideGroup = false;
            for (int markerIndex = markerStart; markerIndex >= 0 && markerIndex < markerEnd; markerIndex++)
            {
                var marker = runtimeTextMarkers[markerIndex];
                bool groupMatches = string.Equals(marker.Group, group, StringComparison.OrdinalIgnoreCase);
                if (groupMatches)
                    insideGroup = true;

                if (groupMatches && marker.Kind == ActorAnimationTextMarkerKind.Start)
                {
                    start = marker.Time;
                    loopStart = marker.Time;
                    insideGroup = true;
                    continue;
                }

                if (!insideGroup)
                    continue;

                switch (marker.Kind)
                {
                    case ActorAnimationTextMarkerKind.LoopStart:
                        if (string.IsNullOrEmpty(marker.Group) || groupMatches)
                            loopStart = marker.Time;
                        break;
                    case ActorAnimationTextMarkerKind.LoopStop:
                        if (string.IsNullOrEmpty(marker.Group) || groupMatches)
                            loopStop = marker.Time;
                        break;
                    case ActorAnimationTextMarkerKind.Stop:
                        if (string.IsNullOrEmpty(marker.Group) || groupMatches)
                        {
                            stop = marker.Time;
                            markerIndex = markerEnd;
                        }
                        break;
                    case ActorAnimationTextMarkerKind.Start:
                        if (!string.IsNullOrEmpty(marker.Group) && !groupMatches)
                        {
                            stop = marker.Time;
                            markerIndex = markerEnd;
                        }
                        break;
                }
            }

            if (stop <= start)
                stop = duration;
            if (loopStart < start)
                loopStart = start;
            if (loopStop <= loopStart || loopStop > stop)
                loopStop = stop;
        }

        static bool IsLoopingGroup(
            FixedString64Bytes group,
            int clipIndex,
            RuntimeTextMarker[] runtimeTextMarkers,
            int[] clipTextMarkerStarts,
            int[] clipTextMarkerCounts)
        {
            if (IsKnownLoopingGroup(group))
                return true;

            int markerStart = (uint)clipIndex < (uint)clipTextMarkerStarts.Length ? clipTextMarkerStarts[clipIndex] : -1;
            int markerCount = (uint)clipIndex < (uint)clipTextMarkerCounts.Length ? clipTextMarkerCounts[clipIndex] : 0;
            int markerEnd = markerStart >= 0 ? Math.Min(runtimeTextMarkers.Length, markerStart + markerCount) : -1;
            for (int markerIndex = markerStart; markerIndex >= 0 && markerIndex < markerEnd; markerIndex++)
            {
                var marker = runtimeTextMarkers[markerIndex];
                if (marker.Kind == ActorAnimationTextMarkerKind.LoopStart || marker.Kind == ActorAnimationTextMarkerKind.LoopStop)
                    return true;
            }

            return false;
        }

        static void AddRuntimeTextMarkers(
            ActorAnimationTextKeyDef key,
            System.Collections.Generic.List<RuntimeTextMarker> markers)
        {
            if (string.IsNullOrWhiteSpace(key.Text))
                return;

            string[] parts = key.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string text = parts[i]?.Trim();
                if (string.IsNullOrEmpty(text))
                    continue;

                SplitMarker(text, out string group, out string value);
                markers.Add(new RuntimeTextMarker
                {
                    Time = key.Time,
                    Group = group,
                    Value = value,
                    Text = text,
                    Kind = ResolveMarkerKind(value),
                });
            }
        }

        static void SplitMarker(string text, out string group, out string value)
        {
            int colon = text.IndexOf(':');
            if (colon < 0)
            {
                group = string.Empty;
                value = text.Trim();
                return;
            }

            group = text.Substring(0, colon).Trim();
            value = text.Substring(colon + 1).Trim();
        }

        static ActorAnimationTextMarkerKind ResolveMarkerKind(string value)
        {
            if (string.Equals(value, "start", StringComparison.OrdinalIgnoreCase))
                return ActorAnimationTextMarkerKind.Start;
            if (string.Equals(value, "loop start", StringComparison.OrdinalIgnoreCase))
                return ActorAnimationTextMarkerKind.LoopStart;
            if (string.Equals(value, "loop stop", StringComparison.OrdinalIgnoreCase))
                return ActorAnimationTextMarkerKind.LoopStop;
            if (string.Equals(value, "stop", StringComparison.OrdinalIgnoreCase))
                return ActorAnimationTextMarkerKind.Stop;
            return ActorAnimationTextMarkerKind.Marker;
        }
    }
}
