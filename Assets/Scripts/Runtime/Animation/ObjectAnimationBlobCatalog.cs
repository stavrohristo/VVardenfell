using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;

namespace VVardenfell.Runtime.Animation
{
    public struct ObjectAnimationBlobCatalog : IComponentData
    {
        public BlobAssetReference<ObjectAnimationCatalogBlob> Blob;
    }

    public struct ObjectAnimationCatalogBlob
    {
        public BlobArray<ObjectAnimationModelBlob> Models;
        public BlobArray<ObjectAnimationNodeBlob> Nodes;
        public BlobArray<ObjectAnimationClipBlob> Clips;
        public BlobArray<ObjectAnimationTextMarkerBlob> TextMarkers;
        public BlobArray<ObjectAnimationTrackBlob> Tracks;
        public BlobArray<ActorAnimationKeyBlob> Keys;
    }

    public struct ObjectAnimationModelBlob
    {
        public byte Enabled;
        public int FirstNodeIndex;
        public int NodeCount;
        public int FirstClipIndex;
        public int ClipCount;
    }

    public struct ObjectAnimationNodeBlob
    {
        public int ParentIndex;
    }

    public struct ObjectAnimationClipBlob
    {
        public FixedString64Bytes Name;
        public float Duration;
        public int FirstTrackIndex;
        public int TrackCount;
        public int FirstTextMarkerIndex;
        public int TextMarkerCount;
    }

    public struct ObjectAnimationTextMarkerBlob
    {
        public FixedString64Bytes Group;
        public FixedString64Bytes Value;
        public FixedString128Bytes Text;
        public float Time;
        public ActorAnimationTextMarkerKind Kind;
        public SoundDefHandle Sound;
    }

    public struct ObjectAnimationTrackBlob
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
}
