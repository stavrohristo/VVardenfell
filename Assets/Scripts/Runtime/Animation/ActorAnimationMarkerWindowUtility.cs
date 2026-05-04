using Unity.Collections;
using Unity.Mathematics;

namespace VVardenfell.Runtime.Animation
{
    internal static class ActorAnimationMarkerWindowUtility
    {
        public static bool TryResolveWindow(
            ref ActorAnimationCatalogBlob catalog,
            in ActorAnimationGroupBlob group,
            FixedString64Bytes startValue,
            FixedString64Bytes stopValue,
            out float start,
            out float stop)
        {
            start = 0f;
            stop = 0f;
            if ((uint)group.ClipIndex >= (uint)catalog.Clips.Length)
                return false;

            var clip = catalog.Clips[group.ClipIndex];
            if (clip.FirstTextMarkerIndex < 0 || clip.TextMarkerCount <= 0)
                return false;

            ulong startHash = ActorAnimationGroupHash.Hash(startValue);
            ulong stopHash = ActorAnimationGroupHash.Hash(stopValue);
            bool foundStart = false;
            bool foundStop = false;
            int end = math.min(catalog.TextMarkers.Length, clip.FirstTextMarkerIndex + clip.TextMarkerCount);
            for (int i = clip.FirstTextMarkerIndex; i < end; i++)
            {
                var marker = catalog.TextMarkers[i];
                if (marker.GroupHash != group.GroupHash)
                    continue;

                if (!foundStart && marker.ValueHash == startHash)
                {
                    start = marker.Time;
                    foundStart = true;
                    continue;
                }

                if (foundStart && marker.ValueHash == stopHash)
                {
                    stop = marker.Time;
                    foundStop = true;
                    break;
                }
            }

            return foundStart && foundStop && stop > start;
        }

        public static bool TryResolveMarker(
            ref ActorAnimationCatalogBlob catalog,
            in ActorAnimationGroupBlob group,
            FixedString64Bytes value,
            out float time)
        {
            time = 0f;
            if ((uint)group.ClipIndex >= (uint)catalog.Clips.Length)
                return false;

            var clip = catalog.Clips[group.ClipIndex];
            if (clip.FirstTextMarkerIndex < 0 || clip.TextMarkerCount <= 0)
                return false;

            ulong valueHash = ActorAnimationGroupHash.Hash(value);
            int end = math.min(catalog.TextMarkers.Length, clip.FirstTextMarkerIndex + clip.TextMarkerCount);
            for (int i = clip.FirstTextMarkerIndex; i < end; i++)
            {
                var marker = catalog.TextMarkers[i];
                if (marker.GroupHash == group.GroupHash && marker.ValueHash == valueHash)
                {
                    time = marker.Time;
                    return true;
                }
            }

            return false;
        }
    }
}
