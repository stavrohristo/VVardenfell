using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;

namespace VVardenfell.Runtime.Animation
{
    public static class ObjectAnimationBlobBuilder
    {
        public static BlobAssetReference<ObjectAnimationCatalogBlob> Build(ModelPrefabCatalogData source)
        {
            var models = source?.Records ?? Array.Empty<ModelPrefabDef>();
            CountPayload(models, out int nodeCount, out int clipCount, out int markerCount, out int trackCount, out int keyCount);

            using var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<ObjectAnimationCatalogBlob>();
            var dstModels = builder.Allocate(ref root.Models, models.Length);
            var dstNodes = builder.Allocate(ref root.Nodes, nodeCount);
            var dstClips = builder.Allocate(ref root.Clips, clipCount);
            var dstMarkers = builder.Allocate(ref root.TextMarkers, markerCount);
            var dstTracks = builder.Allocate(ref root.Tracks, trackCount);
            var dstKeys = builder.Allocate(ref root.Keys, keyCount);

            int nextNode = 0;
            int nextClip = 0;
            int nextMarker = 0;
            int nextTrack = 0;
            int nextKey = 0;
            for (int modelIndex = 0; modelIndex < models.Length; modelIndex++)
            {
                var model = models[modelIndex];
                var animation = model?.ObjectAnimation;
                var nodes = model?.Nodes ?? Array.Empty<ModelPrefabNodeDef>();
                bool enabled = animation?.IsEnabled == true;
                int firstNode = nextNode;
                int firstClip = nextClip;

                for (int nodeIndex = 0; nodeIndex < nodes.Length; nodeIndex++)
                {
                    dstNodes[nextNode++] = new ObjectAnimationNodeBlob
                    {
                        ParentIndex = nodes[nodeIndex]?.ParentIndex ?? -1,
                    };
                }

                if (enabled)
                    CopyAnimation(animation, dstClips, dstMarkers, dstTracks, dstKeys, ref nextClip, ref nextMarker, ref nextTrack, ref nextKey);

                dstModels[modelIndex] = new ObjectAnimationModelBlob
                {
                    Enabled = (byte)(enabled ? 1 : 0),
                    FirstNodeIndex = firstNode,
                    NodeCount = nodes.Length,
                    FirstClipIndex = firstClip,
                    ClipCount = enabled ? animation.Clips.Length : 0,
                };
            }

            return builder.CreateBlobAssetReference<ObjectAnimationCatalogBlob>(Allocator.Persistent);
        }

        static void CountPayload(ModelPrefabDef[] models, out int nodeCount, out int clipCount, out int markerCount, out int trackCount, out int keyCount)
        {
            nodeCount = 0;
            clipCount = 0;
            markerCount = 0;
            trackCount = 0;
            keyCount = 0;

            for (int i = 0; i < models.Length; i++)
            {
                var model = models[i];
                nodeCount += model?.Nodes?.Length ?? 0;
                var animation = model?.ObjectAnimation;
                if (animation?.IsEnabled != true)
                    continue;

                clipCount += animation.Clips?.Length ?? 0;
                markerCount += animation.TextMarkers?.Length ?? 0;
                trackCount += animation.Tracks?.Length ?? 0;
                keyCount += animation.Keys?.Length ?? 0;
            }
        }

        static void CopyAnimation(
            ModelObjectAnimationDef animation,
            BlobBuilderArray<ObjectAnimationClipBlob> dstClips,
            BlobBuilderArray<ObjectAnimationTextMarkerBlob> dstMarkers,
            BlobBuilderArray<ObjectAnimationTrackBlob> dstTracks,
            BlobBuilderArray<ActorAnimationKeyBlob> dstKeys,
            ref int nextClip,
            ref int nextMarker,
            ref int nextTrack,
            ref int nextKey)
        {
            int markerBase = nextMarker;
            var markers = animation.TextMarkers ?? Array.Empty<ModelObjectAnimationTextMarkerDef>();
            for (int i = 0; i < markers.Length; i++)
            {
                dstMarkers[nextMarker++] = new ObjectAnimationTextMarkerBlob
                {
                    Group = Fixed64(markers[i].Group),
                    Value = Fixed64(markers[i].Value),
                    Text = Fixed128(markers[i].Text),
                    Time = markers[i].Time,
                    Kind = markers[i].Kind,
                    Sound = markers[i].Sound,
                };
            }

            int trackBase = nextTrack;
            var tracks = animation.Tracks ?? Array.Empty<ModelObjectAnimationTrackDef>();
            for (int i = 0; i < tracks.Length; i++)
            {
                dstTracks[nextTrack++] = new ObjectAnimationTrackBlob
                {
                    TargetNodeIndex = tracks[i].TargetNodeIndex,
                    Kind = tracks[i].Kind,
                    Interpolation = tracks[i].Interpolation,
                    AxisOrder = tracks[i].AxisOrder,
                    ControllerFlags = tracks[i].ControllerFlags,
                    Frequency = tracks[i].Frequency,
                    Phase = tracks[i].Phase,
                    TimeStart = tracks[i].TimeStart,
                    TimeStop = tracks[i].TimeStop,
                    FirstKeyIndex = tracks[i].FirstKeyIndex < 0 ? -1 : nextKey + tracks[i].FirstKeyIndex,
                    KeyCount = tracks[i].KeyCount,
                };
            }

            var keys = animation.Keys ?? Array.Empty<ActorAnimationKeyDef>();
            for (int i = 0; i < keys.Length; i++)
            {
                dstKeys[nextKey++] = new ActorAnimationKeyBlob
                {
                    Time = keys[i].Time,
                    Value = new float4(keys[i].X, keys[i].Y, keys[i].Z, keys[i].W),
                    InTangent = new float4(keys[i].InX, keys[i].InY, keys[i].InZ, keys[i].InW),
                    OutTangent = new float4(keys[i].OutX, keys[i].OutY, keys[i].OutZ, keys[i].OutW),
                };
            }

            var clips = animation.Clips ?? Array.Empty<ModelObjectAnimationClipDef>();
            for (int i = 0; i < clips.Length; i++)
            {
                var clip = clips[i];
                dstClips[nextClip++] = new ObjectAnimationClipBlob
                {
                    Name = Fixed64(clip?.Name),
                    Duration = clip?.Duration ?? 0f,
                    FirstTrackIndex = clip?.FirstTrackIndex >= 0 ? trackBase + clip.FirstTrackIndex : -1,
                    TrackCount = clip?.TrackCount ?? 0,
                    FirstTextMarkerIndex = clip?.FirstTextMarkerIndex >= 0 ? markerBase + clip.FirstTextMarkerIndex : -1,
                    TextMarkerCount = clip?.TextMarkerCount ?? 0,
                };
            }
        }

        static FixedString64Bytes Fixed64(string value)
        {
            FixedString64Bytes result = default;
            if (!string.IsNullOrEmpty(value))
                result.CopyFromTruncated(value);
            return result;
        }

        static FixedString128Bytes Fixed128(string value)
        {
            FixedString128Bytes result = default;
            if (!string.IsNullOrEmpty(value))
                result.CopyFromTruncated(value);
            return result;
        }
    }
}
