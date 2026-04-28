using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;

namespace VVardenfell.Runtime.Animation
{
    public static partial class ActorAnimationBlobBuilder
    {
        static void BuildClips(
            ActorAnimationCatalogData source,
            int[] clipRigFamilyIndices,
            int[] clipTextMarkerStarts,
            int[] clipTextMarkerCounts,
            ref BlobBuilder builder,
            ref ActorAnimationCatalogBlob root)
        {
            var clips = source.Clips ?? Array.Empty<ActorAnimationClipDef>();
            var rigFamilies = source.RigFamilies ?? Array.Empty<ActorRigFamilyDef>();
            BlobBuilderArray<ActorAnimationClipBlob> dst = builder.Allocate(ref root.Clips, clips.Length);
            for (int i = 0; i < clips.Length; i++)
            {
                var clip = clips[i];
                string modelPath = string.Empty;
                int rigFamilyIndex = (uint)i < (uint)clipRigFamilyIndices.Length ? clipRigFamilyIndices[i] : -1;
                if ((uint)rigFamilyIndex < (uint)rigFamilies.Length)
                    modelPath = rigFamilies[rigFamilyIndex]?.SkeletonModelPath;

                dst[i] = new ActorAnimationClipBlob
                {
                    SourcePath = Fixed128(clip?.SourcePath),
                    Name = Fixed64(clip?.Name),
                    AccumRootName = Fixed64(clip?.AccumRootName),
                    AnimationHash = ActorAnimationHash.Clip(modelPath, clip?.Name),
                    Duration = clip?.Duration ?? 0f,
                    FirstTrackIndex = clip?.FirstTrackIndex ?? -1,
                    TrackCount = clip?.TrackCount ?? 0,
                    FirstTextMarkerIndex = (uint)i < (uint)(clipTextMarkerStarts?.Length ?? 0) ? clipTextMarkerStarts[i] : -1,
                    TextMarkerCount = (uint)i < (uint)(clipTextMarkerCounts?.Length ?? 0) ? clipTextMarkerCounts[i] : 0,
                };
            }
        }

        static void BuildTextMarkers(
            RuntimeTextMarker[] markers,
            ref BlobBuilder builder,
            ref ActorAnimationCatalogBlob root)
        {
            markers ??= Array.Empty<RuntimeTextMarker>();
            BlobBuilderArray<ActorAnimationTextMarkerBlob> dst = builder.Allocate(ref root.TextMarkers, markers.Length);
            for (int i = 0; i < markers.Length; i++)
            {
                FixedString64Bytes group = Fixed64(markers[i].Group);
                FixedString64Bytes value = Fixed64(markers[i].Value);
                dst[i] = new ActorAnimationTextMarkerBlob
                {
                    Group = group,
                    Value = value,
                    Text = Fixed128(markers[i].Text),
                    GroupHash = group.IsEmpty ? 0UL : ActorAnimationGroupHash.Hash(group),
                    ValueHash = value.IsEmpty ? 0UL : ActorAnimationGroupHash.Hash(value),
                    Time = markers[i].Time,
                    Kind = markers[i].Kind,
                };
            }
        }

        static void BuildTracks(
            ActorAnimationCatalogData source,
            int[] targetBoneIndices,
            ushort[] blendMasks,
            ref BlobBuilder builder,
            ref ActorAnimationCatalogBlob root)
        {
            var tracks = source.Tracks ?? Array.Empty<ActorAnimationTrackDef>();
            BlobBuilderArray<ActorAnimationTrackBlob> dst = builder.Allocate(ref root.Tracks, tracks.Length);
            for (int i = 0; i < tracks.Length; i++)
            {
                var track = tracks[i];
                dst[i] = new ActorAnimationTrackBlob
                {
                    TargetName = Fixed64(track?.TargetName),
                    TargetBoneIndex = (uint)i < (uint)targetBoneIndices.Length ? targetBoneIndices[i] : -1,
                    Kind = track?.Kind ?? default,
                    Interpolation = track?.Interpolation ?? default,
                    AxisOrder = track?.AxisOrder ?? 0,
                    ControllerFlags = track?.ControllerFlags ?? 0,
                    Frequency = track?.Frequency ?? 1f,
                    Phase = track?.Phase ?? 0f,
                    TimeStart = track?.TimeStart ?? 0f,
                    TimeStop = track?.TimeStop ?? 0f,
                    FirstKeyIndex = track?.FirstKeyIndex ?? -1,
                    KeyCount = track?.KeyCount ?? 0,
                    BlendMask = (uint)i < (uint)blendMasks.Length
                        ? blendMasks[i]
                        : (ushort)ActorAnimationBlendMask.LowerBody,
                };
            }
        }

        static void BuildKeys(
            ActorAnimationCatalogData source,
            ref BlobBuilder builder,
            ref ActorAnimationCatalogBlob root)
        {
            var keys = source.Keys ?? Array.Empty<ActorAnimationKeyDef>();
            BlobBuilderArray<ActorAnimationKeyBlob> dst = builder.Allocate(ref root.Keys, keys.Length);
            for (int i = 0; i < keys.Length; i++)
            {
                var key = keys[i];
                dst[i] = new ActorAnimationKeyBlob
                {
                    Time = key.Time,
                    Value = new float4(key.X, key.Y, key.Z, key.W),
                    InTangent = new float4(key.InX, key.InY, key.InZ, key.InW),
                    OutTangent = new float4(key.OutX, key.OutY, key.OutZ, key.OutW),
                };
            }
        }

        static void BuildGroups(
            RuntimeAnimationGroup[] groups,
            ref BlobBuilder builder,
            ref ActorAnimationCatalogBlob root)
        {
            groups ??= Array.Empty<RuntimeAnimationGroup>();
            BlobBuilderArray<ActorAnimationGroupBlob> dst = builder.Allocate(ref root.Groups, groups.Length);
            for (int i = 0; i < groups.Length; i++)
            {
                dst[i] = new ActorAnimationGroupBlob
                {
                    GroupHash = groups[i].GroupHash,
                    ClipHash = groups[i].ClipHash,
                    ClipIndex = groups[i].ClipIndex,
                    RigFamilyIndex = groups[i].RigFamilyIndex,
                    Velocity = groups[i].Velocity,
                    StartTime = groups[i].StartTime,
                    LoopStartTime = groups[i].LoopStartTime,
                    LoopStopTime = groups[i].LoopStopTime,
                    StopTime = groups[i].StopTime,
                    Looping = groups[i].Looping,
                };
            }
        }

        static void BuildRigFamilyAnimationIndexes(
            RuntimeRigFamilyAnimationIndex[] indexes,
            ref BlobBuilder builder,
            ref ActorAnimationCatalogBlob root)
        {
            indexes ??= Array.Empty<RuntimeRigFamilyAnimationIndex>();
            BlobBuilderArray<ActorRigFamilyAnimationIndexBlob> dst = builder.Allocate(ref root.RigFamilyAnimationIndexes, indexes.Length);
            for (int i = 0; i < indexes.Length; i++)
            {
                dst[i] = new ActorRigFamilyAnimationIndexBlob
                {
                    FirstGroupLookupIndex = indexes[i].FirstGroupLookupIndex,
                    GroupLookupCount = indexes[i].GroupLookupCount,
                };
            }
        }

        static void BuildGroupLookups(
            RuntimeGroupLookup[] lookups,
            ref BlobBuilder builder,
            ref ActorAnimationCatalogBlob root)
        {
            lookups ??= Array.Empty<RuntimeGroupLookup>();
            BlobBuilderArray<ActorAnimationGroupLookupBlob> dst = builder.Allocate(ref root.GroupLookups, lookups.Length);
            for (int i = 0; i < lookups.Length; i++)
                dst[i] = new ActorAnimationGroupLookupBlob { GroupHash = lookups[i].GroupHash, GroupIndex = lookups[i].GroupIndex };
        }

        static int[] BuildClipRigFamilyIndices(ActorAnimationCatalogData source)
        {
            int clipCount = source.Clips?.Length ?? 0;
            var result = new int[clipCount];
            Array.Fill(result, -1);
            var rigFamilies = source.RigFamilies ?? Array.Empty<ActorRigFamilyDef>();
            for (int rigFamilyIndex = 0; rigFamilyIndex < rigFamilies.Length; rigFamilyIndex++)
            {
                var rigFamily = rigFamilies[rigFamilyIndex];
                int first = rigFamily?.FirstClipIndex ?? -1;
                int count = rigFamily?.ClipCount ?? 0;
                if (first < 0 || count <= 0)
                    continue;

                int end = math.min(clipCount, first + count);
                for (int clipIndex = first; clipIndex < end; clipIndex++)
                    if (result[clipIndex] < 0)
                        result[clipIndex] = rigFamilyIndex;
            }
            return result;
        }

        static int[] BuildTrackTargetBoneIndices(ActorAnimationCatalogData source, int[] clipRigFamilyIndices)
        {
            int trackCount = source.Tracks?.Length ?? 0;
            var result = new int[trackCount];
            Array.Fill(result, -1);

            var clips = source.Clips ?? Array.Empty<ActorAnimationClipDef>();
            var rigFamilies = source.RigFamilies ?? Array.Empty<ActorRigFamilyDef>();
            for (int clipIndex = 0; clipIndex < clips.Length; clipIndex++)
            {
                int rigFamilyIndex = (uint)clipIndex < (uint)clipRigFamilyIndices.Length ? clipRigFamilyIndices[clipIndex] : -1;
                int skeletonIndex = (uint)rigFamilyIndex < (uint)rigFamilies.Length ? rigFamilies[rigFamilyIndex]?.SkeletonIndex ?? -1 : -1;
                var bones = ResolveBones(source, skeletonIndex);
                var clip = clips[clipIndex];
                if (clip == null || bones == null || clip.FirstTrackIndex < 0 || clip.TrackCount <= 0)
                    continue;

                int end = math.min(trackCount, clip.FirstTrackIndex + clip.TrackCount);
                for (int trackIndex = clip.FirstTrackIndex; trackIndex < end; trackIndex++)
                {
                    var track = source.Tracks[trackIndex];
                    int boneIndex = FindBoneIndex(bones, track?.TargetName);
                    result[trackIndex] = boneIndex;
                }
            }
            return result;
        }

        static ushort[] BuildTrackBlendMasks(
            ActorAnimationCatalogData source,
            int[] clipRigFamilyIndices,
            int[] targetBoneIndices)
        {
            int trackCount = source.Tracks?.Length ?? 0;
            var result = new ushort[trackCount];
            var clips = source.Clips ?? Array.Empty<ActorAnimationClipDef>();
            var rigFamilies = source.RigFamilies ?? Array.Empty<ActorRigFamilyDef>();
            for (int clipIndex = 0; clipIndex < clips.Length; clipIndex++)
            {
                int rigFamilyIndex = (uint)clipIndex < (uint)clipRigFamilyIndices.Length ? clipRigFamilyIndices[clipIndex] : -1;
                int skeletonIndex = (uint)rigFamilyIndex < (uint)rigFamilies.Length ? rigFamilies[rigFamilyIndex]?.SkeletonIndex ?? -1 : -1;
                var bones = ResolveBones(source, skeletonIndex);
                var clip = clips[clipIndex];
                if (clip == null || clip.FirstTrackIndex < 0 || clip.TrackCount <= 0)
                    continue;

                MaskRootIndices maskRoots = ResolveMaskRoots(bones);
                int end = math.min(trackCount, clip.FirstTrackIndex + clip.TrackCount);
                for (int trackIndex = clip.FirstTrackIndex; trackIndex < end; trackIndex++)
                {
                    int boneIndex = (uint)trackIndex < (uint)targetBoneIndices.Length ? targetBoneIndices[trackIndex] : -1;
                    result[trackIndex] = (ushort)ClassifyBlendMask(bones, maskRoots, boneIndex);
                }
            }
            return result;
        }

        readonly struct MaskRootIndices
        {
            public readonly int Torso;
            public readonly int LeftArm;
            public readonly int RightArm;

            public MaskRootIndices(int torso, int leftArm, int rightArm)
            {
                Torso = torso;
                LeftArm = leftArm;
                RightArm = rightArm;
            }

            public bool HasOpenMwRoots => Torso >= 0 || LeftArm >= 0 || RightArm >= 0;
        }

        static MaskRootIndices ResolveMaskRoots(ActorSkeletonBoneDef[] bones)
        {
            if (bones == null || bones.Length == 0)
                return new MaskRootIndices(-1, -1, -1);

            return new MaskRootIndices(
                FindBoneIndex(bones, "Bip01 Spine1"),
                FindBoneIndex(bones, "Bip01 L Clavicle"),
                FindBoneIndex(bones, "Bip01 R Clavicle"));
        }

        static ActorAnimationBlendMask ClassifyBlendMask(
            ActorSkeletonBoneDef[] bones,
            MaskRootIndices roots,
            int boneIndex)
        {
            if (bones == null
                || !roots.HasOpenMwRoots
                || (uint)boneIndex >= (uint)bones.Length)
            {
                return ActorAnimationBlendMask.LowerBody;
            }

            if (IsSelfOrDescendantOf(bones, boneIndex, roots.LeftArm))
                return ActorAnimationBlendMask.LeftArm;

            if (IsSelfOrDescendantOf(bones, boneIndex, roots.RightArm))
                return ActorAnimationBlendMask.RightArm;

            if (IsSelfOrDescendantOf(bones, boneIndex, roots.Torso))
                return ActorAnimationBlendMask.Torso;

            return ActorAnimationBlendMask.LowerBody;
        }

        static bool IsSelfOrDescendantOf(ActorSkeletonBoneDef[] bones, int boneIndex, int ancestorIndex)
        {
            if ((uint)ancestorIndex >= (uint)(bones?.Length ?? 0))
                return false;

            return boneIndex == ancestorIndex || IsDescendantOf(bones, boneIndex, ancestorIndex);
        }

        static bool IsPoseTrack(ActorAnimationTrackDef track)
        {
            return track != null
                   && track.Kind is ActorAnimationTrackKind.Translation
                       or ActorAnimationTrackKind.Rotation
                       or ActorAnimationTrackKind.Scale
                       or ActorAnimationTrackKind.XRotation
                       or ActorAnimationTrackKind.YRotation
                       or ActorAnimationTrackKind.ZRotation;
        }

        static ActorSkeletonBoneDef[] ResolveBones(ActorAnimationCatalogData source, int skeletonIndex)
        {
            var skeletons = source.Skeletons ?? Array.Empty<ActorSkeletonDef>();
            if ((uint)skeletonIndex >= (uint)skeletons.Length)
                return null;
            return skeletons[skeletonIndex]?.Bones;
        }

        static int ResolveSubtreeEnd(ActorSkeletonBoneDef[] bones, int rootIndex)
        {
            if (bones == null || (uint)rootIndex >= (uint)bones.Length)
                return -1;

            int end = rootIndex + 1;
            for (int i = rootIndex + 1; i < bones.Length; i++)
            {
                if (IsDescendantOf(bones, i, rootIndex))
                    end = i + 1;
            }
            return end;
        }

        static bool IsDescendantOf(ActorSkeletonBoneDef[] bones, int boneIndex, int ancestorIndex)
        {
            int parent = bones[boneIndex].ParentIndex;
            while ((uint)parent < (uint)bones.Length)
            {
                if (parent == ancestorIndex)
                    return true;
                parent = bones[parent].ParentIndex;
            }
            return false;
        }

        static int FindBoneIndex(ActorSkeletonBoneDef[] bones, string name)
        {
            if (bones == null || string.IsNullOrEmpty(name))
                return -1;

            for (int i = 0; i < bones.Length; i++)
                if (string.Equals(bones[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    return i;

            string canonical = CanonicalBoneName(name);
            for (int i = 0; i < bones.Length; i++)
                if (string.Equals(CanonicalBoneName(bones[i].Name), canonical, StringComparison.OrdinalIgnoreCase))
                    return i;

            return -1;
        }

        static string CanonicalBoneName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            string value = name.Trim().ToLowerInvariant();
            while (value.Contains("  ", StringComparison.Ordinal))
                value = value.Replace("  ", " ");

            return value;
        }
    }
}
