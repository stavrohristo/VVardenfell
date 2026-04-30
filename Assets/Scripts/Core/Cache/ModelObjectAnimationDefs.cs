using System;

namespace VVardenfell.Core.Cache
{
    public enum ModelObjectAnimationStatus : byte
    {
        None = 0,
        Enabled = 1,
        DisabledUnsupported = 2,
    }

    public sealed class ModelObjectAnimationDef
    {
        public ModelObjectAnimationStatus Status;
        public string DisabledReason;
        public ModelObjectAnimationClipDef[] Clips = Array.Empty<ModelObjectAnimationClipDef>();
        public ModelObjectAnimationTrackDef[] Tracks = Array.Empty<ModelObjectAnimationTrackDef>();
        public ActorAnimationKeyDef[] Keys = Array.Empty<ActorAnimationKeyDef>();
        public ModelObjectAnimationTextMarkerDef[] TextMarkers = Array.Empty<ModelObjectAnimationTextMarkerDef>();

        public bool IsEnabled => Status == ModelObjectAnimationStatus.Enabled && Clips != null && Clips.Length > 0;
    }

    public sealed class ModelObjectAnimationClipDef
    {
        public string Name;
        public float Duration;
        public int FirstTrackIndex;
        public int TrackCount;
        public int FirstTextMarkerIndex;
        public int TextMarkerCount;
    }

    public sealed class ModelObjectAnimationTrackDef
    {
        public int TargetNodeIndex;
        public ActorAnimationTrackKind Kind;
        public ActorAnimationInterpolation Interpolation;
        public int AxisOrder;
        public ushort ControllerFlags;
        public float Frequency;
        public float Phase;
        public float TimeStart;
        public float TimeStop;
        public int FirstKeyIndex;
        public int KeyCount;
    }

    public struct ModelObjectAnimationTextMarkerDef
    {
        public float Time;
        public string Group;
        public string Value;
        public string Text;
        public ActorAnimationTextMarkerKind Kind;
        public SoundDefHandle Sound;
    }
}
