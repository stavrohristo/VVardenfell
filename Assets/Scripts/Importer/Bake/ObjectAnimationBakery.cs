using System;
using System.Collections.Generic;
using VVardenfell.Core.Cache;
using VVardenfell.Importer.Nif;

namespace VVardenfell.Importer.Bake
{
    internal sealed class ObjectAnimationBakery
    {
        readonly Dictionary<string, ModelObjectAnimationDef> _animationsByModel = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, SoundDefHandle> _soundsById;

        public ObjectAnimationBakery(GameplayContentData gameplayContent)
        {
            _soundsById = BuildSoundLookup(gameplayContent);
        }

        public bool Modified { get; private set; }

        public ModelObjectAnimationDef GetOrAddModel(string modelPath, NifFile nif, ModelPrefabSource prefabSource)
        {
            modelPath ??= string.Empty;
            if (_animationsByModel.TryGetValue(modelPath, out var existing))
                return existing;

            var built = Build(modelPath, nif, prefabSource);
            _animationsByModel[modelPath] = built;
            Modified = true;
            return built;
        }

        static bool HasSupportedObjectAnimation(NifFile nif)
            => nif != null && NifObjectAnimationAnalysis.HasSupportedObjectAnimation(nif);

        ModelObjectAnimationDef Build(string modelPath, NifFile nif, ModelPrefabSource prefabSource)
        {
            if (!HasSupportedObjectAnimation(nif))
                return null;

            if (prefabSource?.Nodes == null || prefabSource.Nodes.Length == 0)
            {
                return Disabled($"Object animation model '{modelPath}' has no model-prefab node graph.");
            }

            var tracks = new List<ActorAnimationTrackDef>();
            var keys = new List<ActorAnimationKeyDef>();
            var textKeys = new List<ActorAnimationTextKeyDef>();
            var clips = NifActorAnimationExtractor.ExtractClips(nif, tracks, keys, textKeys);
            if (clips == null || clips.Length == 0)
                return null;

            var nodeLookup = BuildNodeLookup(prefabSource);
            var boundTracks = new List<ModelObjectAnimationTrackDef>(tracks.Count);
            var trackRemap = new int[tracks.Count];
            for (int i = 0; i < trackRemap.Length; i++)
                trackRemap[i] = -1;

            for (int i = 0; i < tracks.Count; i++)
            {
                var track = tracks[i];
                if (track == null || !IsSupportedTrack(track.Kind))
                    continue;

                if (!nodeLookup.TryGetValue(track.TargetName ?? string.Empty, out int nodeIndex))
                {
                    return Disabled(
                        $"Object animation model '{modelPath}' track target '{track.TargetName}' does not bind to a prefab node.");
                }

                int firstKey = track.FirstKeyIndex;
                int keyCount = track.KeyCount;
                if (keyCount <= 0 || firstKey < 0 || firstKey + keyCount > keys.Count)
                    continue;

                trackRemap[i] = boundTracks.Count;
                boundTracks.Add(new ModelObjectAnimationTrackDef
                {
                    TargetNodeIndex = nodeIndex,
                    Kind = track.Kind,
                    Interpolation = track.Interpolation,
                    AxisOrder = track.AxisOrder,
                    ControllerFlags = track.ControllerFlags,
                    Frequency = track.Frequency,
                    Phase = track.Phase,
                    TimeStart = track.TimeStart,
                    TimeStop = track.TimeStop,
                    FirstKeyIndex = firstKey,
                    KeyCount = keyCount,
                });
            }

            var markers = new List<ModelObjectAnimationTextMarkerDef>();
            var objectTracks = new List<ModelObjectAnimationTrackDef>(tracks.Count);
            var objectClips = new List<ModelObjectAnimationClipDef>(clips.Length);
            for (int i = 0; i < clips.Length; i++)
            {
                var clip = clips[i];
                if (clip == null)
                    continue;

                int firstTrack = objectTracks.Count;
                int sourceTrackEnd = Math.Min(tracks.Count, clip.FirstTrackIndex + clip.TrackCount);
                for (int sourceTrack = clip.FirstTrackIndex; sourceTrack >= 0 && sourceTrack < sourceTrackEnd; sourceTrack++)
                {
                    int mapped = trackRemap[sourceTrack];
                    if (mapped < 0)
                        continue;

                    var original = boundTracks[mapped];
                    objectTracks.Add(CloneTrack(original));
                }

                int firstMarker = markers.Count;
                AddTextMarkers(textKeys, clip, markers);
                if (objectTracks.Count == firstTrack && markers.Count == firstMarker)
                    continue;

                objectClips.Add(new ModelObjectAnimationClipDef
                {
                    Name = clip.Name ?? string.Empty,
                    Duration = clip.Duration,
                    FirstTrackIndex = firstTrack,
                    TrackCount = objectTracks.Count - firstTrack,
                    FirstTextMarkerIndex = markers.Count > firstMarker ? firstMarker : -1,
                    TextMarkerCount = markers.Count - firstMarker,
                });
            }

            if (objectClips.Count == 0)
                return null;

            return new ModelObjectAnimationDef
            {
                Status = ModelObjectAnimationStatus.Enabled,
                Clips = objectClips.ToArray(),
                Tracks = objectTracks.ToArray(),
                Keys = keys.ToArray(),
                TextMarkers = markers.ToArray(),
            };
        }

        void AddTextMarkers(
            List<ActorAnimationTextKeyDef> textKeys,
            ActorAnimationClipDef clip,
            List<ModelObjectAnimationTextMarkerDef> markers)
        {
            int keyEnd = Math.Min(textKeys.Count, clip.FirstTextKeyIndex + clip.TextKeyCount);
            for (int keyIndex = clip.FirstTextKeyIndex; keyIndex >= 0 && keyIndex < keyEnd; keyIndex++)
            {
                var key = textKeys[keyIndex];
                if (string.IsNullOrWhiteSpace(key.Text))
                    continue;

                string[] markerTexts = key.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < markerTexts.Length; i++)
                {
                    string text = markerTexts[i]?.Trim();
                    if (string.IsNullOrEmpty(text))
                        continue;

                    SplitMarker(text, out string group, out string value);
                    var marker = new ModelObjectAnimationTextMarkerDef
                    {
                        Time = key.Time,
                        Group = group,
                        Value = value,
                        Text = text,
                        Kind = ResolveMarkerKind(value),
                    };
                    if (string.Equals(group, "Sound", StringComparison.OrdinalIgnoreCase))
                        _soundsById.TryGetValue(value, out marker.Sound);
                    markers.Add(marker);
                }
            }
        }

        static Dictionary<string, int> BuildNodeLookup(ModelPrefabSource prefabSource)
        {
            var lookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var nodes = prefabSource?.Nodes ?? Array.Empty<ModelPrefabSourceNode>();
            for (int i = 0; i < nodes.Length; i++)
            {
                string name = nodes[i]?.Name;
                if (!string.IsNullOrWhiteSpace(name) && !lookup.ContainsKey(name))
                    lookup.Add(name, i);
            }
            return lookup;
        }

        static Dictionary<string, SoundDefHandle> BuildSoundLookup(GameplayContentData gameplayContent)
        {
            var sounds = gameplayContent?.Sounds ?? Array.Empty<SoundDef>();
            var lookup = new Dictionary<string, SoundDefHandle>(sounds.Length, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < sounds.Length; i++)
            {
                string id = sounds[i].Id;
                if (!string.IsNullOrWhiteSpace(id) && !lookup.ContainsKey(id))
                    lookup.Add(id, SoundDefHandle.FromIndex(i));
            }
            return lookup;
        }

        static ModelObjectAnimationTrackDef CloneTrack(ModelObjectAnimationTrackDef track)
        {
            return new ModelObjectAnimationTrackDef
            {
                TargetNodeIndex = track.TargetNodeIndex,
                Kind = track.Kind,
                Interpolation = track.Interpolation,
                AxisOrder = track.AxisOrder,
                ControllerFlags = track.ControllerFlags,
                Frequency = track.Frequency,
                Phase = track.Phase,
                TimeStart = track.TimeStart,
                TimeStop = track.TimeStop,
                FirstKeyIndex = track.FirstKeyIndex,
                KeyCount = track.KeyCount,
            };
        }

        static bool IsSupportedTrack(ActorAnimationTrackKind kind)
        {
            return kind is ActorAnimationTrackKind.Translation
                or ActorAnimationTrackKind.Rotation
                or ActorAnimationTrackKind.Scale
                or ActorAnimationTrackKind.XRotation
                or ActorAnimationTrackKind.YRotation
                or ActorAnimationTrackKind.ZRotation
                or ActorAnimationTrackKind.Visibility;
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

        static ModelObjectAnimationDef Disabled(string reason)
        {
            return new ModelObjectAnimationDef
            {
                Status = ModelObjectAnimationStatus.DisabledUnsupported,
                DisabledReason = reason ?? string.Empty,
            };
        }
    }
}
