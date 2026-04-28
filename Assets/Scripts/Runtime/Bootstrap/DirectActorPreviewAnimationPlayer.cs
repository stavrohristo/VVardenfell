using System;
using System.Collections.Generic;
using UnityEngine;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Content;

namespace VVardenfell.Runtime.Bootstrap
{
    static partial class DirectActorPreviewBootstrap
    {
        static void AttachPreviewAnimation(
            CacheLoader cache,
            Transform actorRoot,
            Dictionary<string, Transform> skeletonNodes,
            string actorId,
            bool firstPerson)
        {
            if (actorRoot == null || skeletonNodes == null || skeletonNodes.Count == 0)
                return;
            if (!TryResolvePreviewAnimationSet(cache, actorId, firstPerson, out var catalog, out var skeleton, out int firstClip, out int clipCount))
                return;

            var player = actorRoot.gameObject.AddComponent<DirectActorPreviewAnimationPlayer>();
            player.Initialize(catalog, skeleton, firstClip, clipCount, skeletonNodes);
        }

        static bool TryResolvePreviewAnimationSet(
            CacheLoader cache,
            string actorId,
            bool firstPerson,
            out ActorAnimationCatalogData catalog,
            out ActorSkeletonDef skeleton,
            out int firstClip,
            out int clipCount)
        {
            catalog = cache?.ActorAnimationCatalog;
            skeleton = null;
            firstClip = -1;
            clipCount = 0;
            if (catalog == null || cache.ContentDatabase == null)
                return false;
            if (!cache.ContentDatabase.TryGetActorHandle(actorId, out var actorHandle) || !actorHandle.IsValid)
                return false;

            ref readonly var actor = ref cache.ContentDatabase.Get(actorHandle);
            if (!cache.TryGetActorVisualRecipe(actor.ContentId, firstPerson, out var recipe) || recipe == null)
                return false;

            var rigFamilies = catalog.RigFamilies ?? Array.Empty<ActorRigFamilyDef>();
            if ((uint)recipe.RigFamilyIndex >= (uint)rigFamilies.Length)
                return false;

            var rigFamily = rigFamilies[recipe.RigFamilyIndex];
            var skeletons = catalog.Skeletons ?? Array.Empty<ActorSkeletonDef>();
            if (rigFamily == null || (uint)rigFamily.SkeletonIndex >= (uint)skeletons.Length)
                return false;

            skeleton = skeletons[rigFamily.SkeletonIndex];
            firstClip = rigFamily.FirstClipIndex;
            clipCount = rigFamily.ClipCount;
            return skeleton != null && firstClip >= 0 && clipCount > 0;
        }
    }

    sealed class DirectActorPreviewAnimationPlayer : MonoBehaviour
    {
        const float QuaternionEpsilon = 0.000001f;

        ActorAnimationCatalogData _catalog;
        ActorSkeletonDef _skeleton;
        ActorAnimationClipDef _clip;
        string _group;
        float _time;
        float _startTime;
        float _loopStart;
        float _loopStop;
        float _stopTime;
        Transform[] _bones = Array.Empty<Transform>();
        Vector3[] _bindPositions = Array.Empty<Vector3>();
        Quaternion[] _bindRotations = Array.Empty<Quaternion>();
        Vector3[] _bindScales = Array.Empty<Vector3>();
        Vector3[] _axisAngles = Array.Empty<Vector3>();
        int[] _axisFlags = Array.Empty<int>();
        int[] _axisOrders = Array.Empty<int>();

        public void Initialize(
            ActorAnimationCatalogData catalog,
            ActorSkeletonDef skeleton,
            int firstClip,
            int clipCount,
            Dictionary<string, Transform> skeletonNodes)
        {
            _catalog = catalog;
            _skeleton = skeleton;
            if (!ResolveClip(firstClip, clipCount, "idle") && !ResolveClip(firstClip, clipCount, "walkforward"))
                return;

            var sourceBones = skeleton.Bones ?? Array.Empty<ActorSkeletonBoneDef>();
            _bones = new Transform[sourceBones.Length];
            _bindPositions = new Vector3[sourceBones.Length];
            _bindRotations = new Quaternion[sourceBones.Length];
            _bindScales = new Vector3[sourceBones.Length];
            _axisAngles = new Vector3[sourceBones.Length];
            _axisFlags = new int[sourceBones.Length];
            _axisOrders = new int[sourceBones.Length];

            for (int i = 0; i < sourceBones.Length; i++)
            {
                string name = sourceBones[i].Name;
                if (string.IsNullOrEmpty(name) || !skeletonNodes.TryGetValue(name, out var bone) || bone == null)
                    continue;

                _bones[i] = bone;
                _bindPositions[i] = bone.localPosition;
                _bindRotations[i] = bone.localRotation;
                _bindScales[i] = bone.localScale;
            }
        }

        void Update()
        {
            if (_catalog == null || _clip == null || _bones.Length == 0)
                return;

            AdvanceTime(Time.deltaTime);
            ResetPose();
            SampleClip(_time);
            ApplyXyzRotations();
        }

        bool ResolveClip(int firstClip, int clipCount, string group)
        {
            var clips = _catalog.Clips ?? Array.Empty<ActorAnimationClipDef>();
            int end = Math.Min(clips.Length, firstClip + clipCount);
            for (int i = firstClip; i >= 0 && i < end; i++)
            {
                var clip = clips[i];
                if (clip != null && string.Equals(clip.Name, group, StringComparison.OrdinalIgnoreCase))
                    return SetClip(clip, group);
            }

            for (int i = firstClip; i >= 0 && i < end; i++)
            {
                var clip = clips[i];
                if (ClipHasGroup(clip, group))
                    return SetClip(clip, group);
            }

            return false;
        }

        bool SetClip(ActorAnimationClipDef clip, string group)
        {
            _clip = clip;
            _group = group;
            ResolveWindow(clip, group, out _startTime, out _loopStart, out _loopStop, out _stopTime);
            _time = _startTime;
            return true;
        }

        bool ClipHasGroup(ActorAnimationClipDef clip, string group)
        {
            if (clip == null || clip.FirstTextMarkerIndex < 0 || clip.TextMarkerCount <= 0)
                return false;

            var markers = _catalog.TextMarkers ?? Array.Empty<ActorAnimationTextMarkerDef>();
            int end = Math.Min(markers.Length, clip.FirstTextMarkerIndex + clip.TextMarkerCount);
            for (int i = clip.FirstTextMarkerIndex; i < end; i++)
                if (string.Equals(markers[i].Group, group, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        void ResolveWindow(ActorAnimationClipDef clip, string group, out float start, out float loopStart, out float loopStop, out float stop)
        {
            float duration = clip.Duration > 0f ? clip.Duration : 1f;
            start = 0f;
            loopStart = 0f;
            loopStop = duration;
            stop = duration;
            if (clip.FirstTextMarkerIndex < 0 || clip.TextMarkerCount <= 0)
                return;

            var markers = _catalog.TextMarkers ?? Array.Empty<ActorAnimationTextMarkerDef>();
            int end = Math.Min(markers.Length, clip.FirstTextMarkerIndex + clip.TextMarkerCount);
            bool inside = false;
            for (int i = clip.FirstTextMarkerIndex; i < end; i++)
            {
                var marker = markers[i];
                bool groupMatches = string.Equals(marker.Group, group, StringComparison.OrdinalIgnoreCase);
                if (groupMatches && marker.Kind == ActorAnimationTextMarkerKind.Start)
                {
                    start = marker.Time;
                    loopStart = marker.Time;
                    inside = true;
                    continue;
                }

                if (!inside && groupMatches)
                    inside = true;
                if (!inside)
                    continue;

                if ((string.IsNullOrEmpty(marker.Group) || groupMatches) && marker.Kind == ActorAnimationTextMarkerKind.LoopStart)
                    loopStart = marker.Time;
                else if ((string.IsNullOrEmpty(marker.Group) || groupMatches) && marker.Kind == ActorAnimationTextMarkerKind.LoopStop)
                    loopStop = marker.Time;
                else if ((string.IsNullOrEmpty(marker.Group) || groupMatches) && marker.Kind == ActorAnimationTextMarkerKind.Stop)
                {
                    stop = marker.Time;
                    break;
                }
                else if (!string.IsNullOrEmpty(marker.Group) && !groupMatches && marker.Kind == ActorAnimationTextMarkerKind.Start)
                {
                    stop = marker.Time;
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

        void AdvanceTime(float deltaTime)
        {
            _time += deltaTime;
            if (_loopStop > _loopStart && _time >= _loopStop)
                _time = _loopStart + Mathf.Repeat(_time - _loopStart, _loopStop - _loopStart);
            else if (_time >= _stopTime)
                _time = _startTime;
        }

        void ResetPose()
        {
            Array.Clear(_axisAngles, 0, _axisAngles.Length);
            Array.Clear(_axisFlags, 0, _axisFlags.Length);
            Array.Clear(_axisOrders, 0, _axisOrders.Length);

            for (int i = 0; i < _bones.Length; i++)
            {
                var bone = _bones[i];
                if (bone == null)
                    continue;

                bone.localPosition = _bindPositions[i];
                bone.localRotation = _bindRotations[i];
                bone.localScale = _bindScales[i];
            }
        }

        void SampleClip(float time)
        {
            var tracks = _catalog.Tracks ?? Array.Empty<ActorAnimationTrackDef>();
            int trackEnd = Math.Min(tracks.Length, _clip.FirstTrackIndex + _clip.TrackCount);
            for (int trackIndex = _clip.FirstTrackIndex; trackIndex >= 0 && trackIndex < trackEnd; trackIndex++)
            {
                var track = tracks[trackIndex];
                if (track == null || track.KeyCount <= 0 || track.FirstKeyIndex < 0)
                    continue;

                int boneIndex = ResolveBoneIndex(track.TargetName);
                if ((uint)boneIndex >= (uint)_bones.Length || _bones[boneIndex] == null)
                    continue;

                float trackTime = MapTrackTime(time, track);
                switch (track.Kind)
                {
                    case ActorAnimationTrackKind.Translation:
                        _bones[boneIndex].localPosition = SourceTranslationToUnity(SampleValue(track, trackTime));
                        break;
                    case ActorAnimationTrackKind.Rotation:
                        _bones[boneIndex].localRotation = SampleRotation(track, trackTime);
                        break;
                    case ActorAnimationTrackKind.Scale:
                    {
                        float scale = SampleValue(track, trackTime).x;
                        if (scale <= 0f)
                            scale = 1f;
                        Vector3 sign = new(Mathf.Sign(_bindScales[boneIndex].x), Mathf.Sign(_bindScales[boneIndex].y), Mathf.Sign(_bindScales[boneIndex].z));
                        _bones[boneIndex].localScale = new Vector3(sign.x * scale, sign.y * scale, sign.z * scale);
                        break;
                    }
                    case ActorAnimationTrackKind.XRotation:
                        _axisAngles[boneIndex].x = SampleValue(track, trackTime).x;
                        _axisFlags[boneIndex] |= 1;
                        _axisOrders[boneIndex] = track.AxisOrder;
                        break;
                    case ActorAnimationTrackKind.YRotation:
                        _axisAngles[boneIndex].y = SampleValue(track, trackTime).x;
                        _axisFlags[boneIndex] |= 2;
                        _axisOrders[boneIndex] = track.AxisOrder;
                        break;
                    case ActorAnimationTrackKind.ZRotation:
                        _axisAngles[boneIndex].z = SampleValue(track, trackTime).x;
                        _axisFlags[boneIndex] |= 4;
                        _axisOrders[boneIndex] = track.AxisOrder;
                        break;
                }
            }
        }

        int ResolveBoneIndex(string targetName)
        {
            var bones = _skeleton.Bones ?? Array.Empty<ActorSkeletonBoneDef>();
            for (int i = 0; i < bones.Length; i++)
                if (string.Equals(bones[i].Name, targetName, StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }

        float MapTrackTime(float layerTime, ActorAnimationTrackDef track)
        {
            float frequency = track.Frequency == 0f ? 1f : track.Frequency;
            float time = layerTime * frequency + track.Phase;
            if (track.TimeStop <= track.TimeStart || (time >= track.TimeStart && time <= track.TimeStop))
                return time;

            ushort extrapolation = (ushort)(track.ControllerFlags & 0x6);
            if (extrapolation == 0x2)
            {
                float duration = track.TimeStop - track.TimeStart;
                float cycles = (time - track.TimeStart) / duration;
                float cycleFloor = Mathf.Floor(cycles);
                float remainder = (cycles - cycleFloor) * duration;
                return ((int)Mathf.Abs(cycleFloor) & 1) == 0
                    ? track.TimeStart + remainder
                    : track.TimeStop - remainder;
            }

            if (extrapolation != 0x4)
            {
                float duration = track.TimeStop - track.TimeStart;
                float cycles = (time - track.TimeStart) / duration;
                return track.TimeStart + (cycles - Mathf.Floor(cycles)) * duration;
            }

            return time < track.TimeStart ? track.TimeStart : track.TimeStop;
        }

        Vector4 SampleValue(ActorAnimationTrackDef track, float time)
        {
            var keys = _catalog.Keys ?? Array.Empty<ActorAnimationKeyDef>();
            int first = track.FirstKeyIndex;
            int last = Math.Min(keys.Length - 1, first + track.KeyCount - 1);
            if (first >= last || time <= keys[first].Time)
                return KeyValue(keys[first]);
            if (time >= keys[last].Time)
                return KeyValue(keys[last]);

            int right = first + 1;
            while (right <= last && keys[right].Time < time)
                right++;

            int left = Math.Max(first, right - 1);
            var a = keys[left];
            var b = keys[right];
            if (track.Interpolation == ActorAnimationInterpolation.Constant)
                return KeyValue(a);

            float span = Mathf.Max(0.00001f, b.Time - a.Time);
            float t = Mathf.Clamp01((time - a.Time) / span);
            return track.Interpolation == ActorAnimationInterpolation.Quadratic
                ? Hermite(a, b, t, span)
                : Vector4.LerpUnclamped(KeyValue(a), KeyValue(b), t);
        }

        Quaternion SampleRotation(ActorAnimationTrackDef track, float time)
        {
            var keys = _catalog.Keys ?? Array.Empty<ActorAnimationKeyDef>();
            int first = track.FirstKeyIndex;
            int last = Math.Min(keys.Length - 1, first + track.KeyCount - 1);
            if (first >= last || time <= keys[first].Time)
                return SourceQuaternionToUnity(KeyQuaternion(keys[first]));
            if (time >= keys[last].Time)
                return SourceQuaternionToUnity(KeyQuaternion(keys[last]));

            int right = first + 1;
            while (right <= last && keys[right].Time < time)
                right++;

            int left = Math.Max(first, right - 1);
            var a = keys[left];
            var b = keys[right];
            if (track.Interpolation == ActorAnimationInterpolation.Constant)
                return SourceQuaternionToUnity(KeyQuaternion(a));

            float span = Mathf.Max(0.00001f, b.Time - a.Time);
            float t = Mathf.Clamp01((time - a.Time) / span);
            return SourceQuaternionToUnity(Quaternion.SlerpUnclamped(KeyQuaternion(a), KeyQuaternion(b), t));
        }

        void ApplyXyzRotations()
        {
            for (int i = 0; i < _bones.Length; i++)
            {
                if (_axisFlags[i] == 0 || _bones[i] == null)
                    continue;

                _bones[i].localRotation = ComposeSourceXyzRotation(_axisAngles[i], _axisOrders[i]);
            }
        }

        static Vector4 KeyValue(ActorAnimationKeyDef key)
            => new(key.X, key.Y, key.Z, key.W);

        static Quaternion KeyQuaternion(ActorAnimationKeyDef key)
            => SafeNormalize(new Quaternion(key.X, key.Y, key.Z, key.W));

        static Vector4 Hermite(ActorAnimationKeyDef a, ActorAnimationKeyDef b, float t, float span)
        {
            Vector4 p0 = KeyValue(a);
            Vector4 p1 = KeyValue(b);
            Vector4 m0 = new Vector4(a.OutX, a.OutY, a.OutZ, a.OutW) * span;
            Vector4 m1 = new Vector4(b.InX, b.InY, b.InZ, b.InW) * span;
            float t2 = t * t;
            float t3 = t2 * t;
            return (2f * t3 - 3f * t2 + 1f) * p0
                   + (t3 - 2f * t2 + t) * m0
                   + (-2f * t3 + 3f * t2) * p1
                   + (t3 - t2) * m1;
        }

        static Vector3 SourceTranslationToUnity(Vector4 source)
            => new Vector3(source.x, source.z, source.y) * WorldScale.MwUnitsToMeters;

        static Quaternion SourceQuaternionToUnity(Quaternion source)
            => SafeNormalize(new Quaternion(-source.x, -source.z, -source.y, source.w));

        static Quaternion ComposeSourceXyzRotation(Vector3 angles, int axisOrder)
        {
            Quaternion x = Quaternion.AngleAxis(angles.x * Mathf.Rad2Deg, Vector3.right);
            Quaternion y = Quaternion.AngleAxis(angles.y * Mathf.Rad2Deg, Vector3.up);
            Quaternion z = Quaternion.AngleAxis(angles.z * Mathf.Rad2Deg, Vector3.forward);
            Quaternion raw = axisOrder switch
            {
                1 => x * z * y,
                2 => y * z * x,
                3 => y * x * z,
                4 => z * x * y,
                5 => z * y * x,
                _ => x * y * z,
            };
            return SourceQuaternionToUnity(raw);
        }

        static Quaternion SafeNormalize(Quaternion value)
        {
            float lengthSq = value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w;
            return lengthSq > QuaternionEpsilon ? Normalize(value, lengthSq) : Quaternion.identity;
        }

        static Quaternion Normalize(Quaternion value, float lengthSq)
        {
            float inv = 1f / Mathf.Sqrt(lengthSq);
            return new Quaternion(value.x * inv, value.y * inv, value.z * inv, value.w * inv);
        }
    }
}
