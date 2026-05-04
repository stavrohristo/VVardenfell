using System;
using System.IO;

namespace VVardenfell.Core.Cache
{
    public enum ModelPrefabNodeKind : byte
    {
        None = 0,
        SyntheticRoot = 1,
        Transform = 2,
        RenderLeaf = 3,
        Billboard = 4,
        Switch = 5,
        Lod = 6,
        FltAnimation = 7,
        BsAnimation = 8,
        BsParticle = 9,
        RootCollision = 10,
        Avoid = 11,
        CollisionSwitch = 12,
    }

    public sealed class ModelPrefabNodeDef
    {
        public ModelPrefabNodeKind Kind;
        public string Name;
        public int ParentIndex;
        public int FirstChildIndex;
        public int ChildCount;
        public int SelectedChildIndex;
        public int GlobalMeshIndex;
        public int MaterialIndex;
        public int TextureIndex;
        public int PickColliderIndex = -1;
        public float PosX;
        public float PosY;
        public float PosZ;
        public float RotX;
        public float RotY;
        public float RotZ;
        public float RotW;
        public float Scale = 1f;
        public float BoundsCenterX;
        public float BoundsCenterY;
        public float BoundsCenterZ;
        public float BoundsExtentsX;
        public float BoundsExtentsY;
        public float BoundsExtentsZ;
        public ushort Flags;
    }

    public sealed class ModelPrefabDef
    {
        public string ModelPath;
        public int RootNodeIndex;
        public int CollisionIndex;
        public int ActorSkeletonIndex = -1;
        public int FirstActorSkinMeshIndex = -1;
        public int ActorSkinMeshCount;
        public int FirstActorClipIndex = -1;
        public int ActorClipCount;
        public float EffectControllerStopTime;
        public ModelObjectAnimationDef ObjectAnimation;
        public ModelPrefabNodeDef[] Nodes = Array.Empty<ModelPrefabNodeDef>();
        public int[] ChildIndices = Array.Empty<int>();
    }

    public sealed class ModelPrefabCatalogData
    {
        public ModelPrefabDef[] Records = Array.Empty<ModelPrefabDef>();
    }

    public static class ModelPrefabFile
    {
        const uint Magic = 0x50464D44u; // 'DMFP'
        const uint Version = 6u;

        public static bool TryRead(string path, out ModelPrefabCatalogData data)
        {
            data = null;
            if (!File.Exists(path))
                return false;

            try
            {
                data = Read(path);
                return true;
            }
            catch
            {
                data = null;
                return false;
            }
        }

        public static ModelPrefabCatalogData Read(string path)
        {
            using var fs = File.OpenRead(path);
            using var r = new BinaryReader(fs);

            uint magic = r.ReadUInt32();
            if (magic != Magic)
                throw new InvalidDataException($"Bad model prefab magic 0x{magic:X8} in '{path}'.");

            uint version = r.ReadUInt32();
            if (version != Version)
                throw new InvalidDataException($"Unsupported model prefab version {version} in '{path}'.");

            int count = r.ReadInt32();
            if (count < 0)
                throw new InvalidDataException($"Negative model prefab count {count} in '{path}'.");

            var records = new ModelPrefabDef[count];
            for (int i = 0; i < count; i++)
                records[i] = ReadDef(r, path, i);

            return new ModelPrefabCatalogData
            {
                Records = records,
            };
        }

        public static void Write(string path, ModelPrefabCatalogData data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);

            using var fs = File.Create(path);
            using var w = new BinaryWriter(fs);
            w.Write(Magic);
            w.Write(Version);

            var records = data?.Records ?? Array.Empty<ModelPrefabDef>();
            w.Write(records.Length);
            for (int i = 0; i < records.Length; i++)
                WriteDef(w, records[i]);
        }

        static ModelPrefabDef ReadDef(BinaryReader r, string path, int index)
        {
            int nodeCount = r.ReadInt32();
            int childIndexCount = r.ReadInt32();
            if (nodeCount < 0 || childIndexCount < 0)
                throw new InvalidDataException($"Invalid model prefab table sizes in '{path}' for record {index}.");

            var nodes = new ModelPrefabNodeDef[nodeCount];
            for (int i = 0; i < nodeCount; i++)
                nodes[i] = ReadNode(r);

            var childIndices = new int[childIndexCount];
            for (int i = 0; i < childIndexCount; i++)
                childIndices[i] = r.ReadInt32();

            return new ModelPrefabDef
            {
                ModelPath = r.ReadString(),
                RootNodeIndex = r.ReadInt32(),
                CollisionIndex = r.ReadInt32(),
                ActorSkeletonIndex = r.ReadInt32(),
                FirstActorSkinMeshIndex = r.ReadInt32(),
                ActorSkinMeshCount = r.ReadInt32(),
                FirstActorClipIndex = r.ReadInt32(),
                ActorClipCount = r.ReadInt32(),
                EffectControllerStopTime = r.ReadSingle(),
                ObjectAnimation = ReadObjectAnimation(r),
                Nodes = nodes,
                ChildIndices = childIndices,
            };
        }

        static ModelPrefabNodeDef ReadNode(BinaryReader r)
        {
            return new ModelPrefabNodeDef
            {
                Kind = (ModelPrefabNodeKind)r.ReadByte(),
                Name = r.ReadString(),
                ParentIndex = r.ReadInt32(),
                FirstChildIndex = r.ReadInt32(),
                ChildCount = r.ReadInt32(),
                SelectedChildIndex = r.ReadInt32(),
                GlobalMeshIndex = r.ReadInt32(),
                MaterialIndex = r.ReadInt32(),
                TextureIndex = r.ReadInt32(),
                PickColliderIndex = r.ReadInt32(),
                PosX = r.ReadSingle(),
                PosY = r.ReadSingle(),
                PosZ = r.ReadSingle(),
                RotX = r.ReadSingle(),
                RotY = r.ReadSingle(),
                RotZ = r.ReadSingle(),
                RotW = r.ReadSingle(),
                Scale = r.ReadSingle(),
                BoundsCenterX = r.ReadSingle(),
                BoundsCenterY = r.ReadSingle(),
                BoundsCenterZ = r.ReadSingle(),
                BoundsExtentsX = r.ReadSingle(),
                BoundsExtentsY = r.ReadSingle(),
                BoundsExtentsZ = r.ReadSingle(),
                Flags = r.ReadUInt16(),
            };
        }

        static void WriteDef(BinaryWriter w, ModelPrefabDef value)
        {
            var nodes = value?.Nodes ?? Array.Empty<ModelPrefabNodeDef>();
            var childIndices = value?.ChildIndices ?? Array.Empty<int>();

            w.Write(nodes.Length);
            w.Write(childIndices.Length);
            for (int i = 0; i < nodes.Length; i++)
                WriteNode(w, nodes[i]);
            for (int i = 0; i < childIndices.Length; i++)
                w.Write(childIndices[i]);

            w.Write(value?.ModelPath ?? string.Empty);
            w.Write(value?.RootNodeIndex ?? -1);
            w.Write(value?.CollisionIndex ?? -1);
            w.Write(value?.ActorSkeletonIndex ?? -1);
            w.Write(value?.FirstActorSkinMeshIndex ?? -1);
            w.Write(value?.ActorSkinMeshCount ?? 0);
            w.Write(value?.FirstActorClipIndex ?? -1);
            w.Write(value?.ActorClipCount ?? 0);
            w.Write(value?.EffectControllerStopTime ?? 0f);
            WriteObjectAnimation(w, value?.ObjectAnimation);
        }

        static void WriteNode(BinaryWriter w, ModelPrefabNodeDef value)
        {
            w.Write((byte)(value?.Kind ?? ModelPrefabNodeKind.None));
            w.Write(value?.Name ?? string.Empty);
            w.Write(value?.ParentIndex ?? -1);
            w.Write(value?.FirstChildIndex ?? -1);
            w.Write(value?.ChildCount ?? 0);
            w.Write(value?.SelectedChildIndex ?? -1);
            w.Write(value?.GlobalMeshIndex ?? -1);
            w.Write(value?.MaterialIndex ?? -1);
            w.Write(value?.TextureIndex ?? -1);
            w.Write(value?.PickColliderIndex ?? -1);
            w.Write(value?.PosX ?? 0f);
            w.Write(value?.PosY ?? 0f);
            w.Write(value?.PosZ ?? 0f);
            w.Write(value?.RotX ?? 0f);
            w.Write(value?.RotY ?? 0f);
            w.Write(value?.RotZ ?? 0f);
            w.Write(value?.RotW ?? 1f);
            w.Write(value?.Scale ?? 1f);
            w.Write(value?.BoundsCenterX ?? 0f);
            w.Write(value?.BoundsCenterY ?? 0f);
            w.Write(value?.BoundsCenterZ ?? 0f);
            w.Write(value?.BoundsExtentsX ?? 0f);
            w.Write(value?.BoundsExtentsY ?? 0f);
            w.Write(value?.BoundsExtentsZ ?? 0f);
            w.Write(value?.Flags ?? 0);
        }

        static ModelObjectAnimationDef ReadObjectAnimation(BinaryReader r)
        {
            var status = (ModelObjectAnimationStatus)r.ReadByte();
            string disabledReason = r.ReadString();
            int clipCount = r.ReadInt32();
            int trackCount = r.ReadInt32();
            int keyCount = r.ReadInt32();
            int markerCount = r.ReadInt32();
            if (clipCount < 0 || trackCount < 0 || keyCount < 0 || markerCount < 0)
                throw new InvalidDataException("Invalid object animation table sizes.");

            var clips = new ModelObjectAnimationClipDef[clipCount];
            for (int i = 0; i < clips.Length; i++)
            {
                clips[i] = new ModelObjectAnimationClipDef
                {
                    Name = r.ReadString(),
                    Duration = r.ReadSingle(),
                    FirstTrackIndex = r.ReadInt32(),
                    TrackCount = r.ReadInt32(),
                    FirstTextMarkerIndex = r.ReadInt32(),
                    TextMarkerCount = r.ReadInt32(),
                };
            }

            var tracks = new ModelObjectAnimationTrackDef[trackCount];
            for (int i = 0; i < tracks.Length; i++)
            {
                tracks[i] = new ModelObjectAnimationTrackDef
                {
                    TargetNodeIndex = r.ReadInt32(),
                    Kind = (ActorAnimationTrackKind)r.ReadByte(),
                    Interpolation = (ActorAnimationInterpolation)r.ReadByte(),
                    AxisOrder = r.ReadInt32(),
                    ControllerFlags = r.ReadUInt16(),
                    Frequency = r.ReadSingle(),
                    Phase = r.ReadSingle(),
                    TimeStart = r.ReadSingle(),
                    TimeStop = r.ReadSingle(),
                    FirstKeyIndex = r.ReadInt32(),
                    KeyCount = r.ReadInt32(),
                };
            }

            var keys = new ActorAnimationKeyDef[keyCount];
            for (int i = 0; i < keys.Length; i++)
            {
                keys[i] = new ActorAnimationKeyDef
                {
                    Time = r.ReadSingle(),
                    X = r.ReadSingle(),
                    Y = r.ReadSingle(),
                    Z = r.ReadSingle(),
                    W = r.ReadSingle(),
                    InX = r.ReadSingle(),
                    InY = r.ReadSingle(),
                    InZ = r.ReadSingle(),
                    InW = r.ReadSingle(),
                    OutX = r.ReadSingle(),
                    OutY = r.ReadSingle(),
                    OutZ = r.ReadSingle(),
                    OutW = r.ReadSingle(),
                };
            }

            var markers = new ModelObjectAnimationTextMarkerDef[markerCount];
            for (int i = 0; i < markers.Length; i++)
            {
                markers[i] = new ModelObjectAnimationTextMarkerDef
                {
                    Time = r.ReadSingle(),
                    Group = r.ReadString(),
                    Value = r.ReadString(),
                    Text = r.ReadString(),
                    Kind = (ActorAnimationTextMarkerKind)r.ReadByte(),
                    Sound = new SoundDefHandle { Value = r.ReadInt32() },
                };
            }

            return new ModelObjectAnimationDef
            {
                Status = status,
                DisabledReason = disabledReason,
                Clips = clips,
                Tracks = tracks,
                Keys = keys,
                TextMarkers = markers,
            };
        }

        static void WriteObjectAnimation(BinaryWriter w, ModelObjectAnimationDef value)
        {
            var clips = value?.Clips ?? Array.Empty<ModelObjectAnimationClipDef>();
            var tracks = value?.Tracks ?? Array.Empty<ModelObjectAnimationTrackDef>();
            var keys = value?.Keys ?? Array.Empty<ActorAnimationKeyDef>();
            var markers = value?.TextMarkers ?? Array.Empty<ModelObjectAnimationTextMarkerDef>();

            w.Write((byte)(value?.Status ?? ModelObjectAnimationStatus.None));
            w.Write(value?.DisabledReason ?? string.Empty);
            w.Write(clips.Length);
            w.Write(tracks.Length);
            w.Write(keys.Length);
            w.Write(markers.Length);

            for (int i = 0; i < clips.Length; i++)
            {
                var clip = clips[i];
                w.Write(clip?.Name ?? string.Empty);
                w.Write(clip?.Duration ?? 0f);
                w.Write(clip?.FirstTrackIndex ?? -1);
                w.Write(clip?.TrackCount ?? 0);
                w.Write(clip?.FirstTextMarkerIndex ?? -1);
                w.Write(clip?.TextMarkerCount ?? 0);
            }

            for (int i = 0; i < tracks.Length; i++)
            {
                var track = tracks[i];
                w.Write(track?.TargetNodeIndex ?? -1);
                w.Write((byte)(track?.Kind ?? ActorAnimationTrackKind.Translation));
                w.Write((byte)(track?.Interpolation ?? ActorAnimationInterpolation.Linear));
                w.Write(track?.AxisOrder ?? 0);
                w.Write(track?.ControllerFlags ?? 0);
                w.Write(track?.Frequency ?? 0f);
                w.Write(track?.Phase ?? 0f);
                w.Write(track?.TimeStart ?? 0f);
                w.Write(track?.TimeStop ?? 0f);
                w.Write(track?.FirstKeyIndex ?? -1);
                w.Write(track?.KeyCount ?? 0);
            }

            for (int i = 0; i < keys.Length; i++)
            {
                var key = keys[i];
                w.Write(key.Time);
                w.Write(key.X);
                w.Write(key.Y);
                w.Write(key.Z);
                w.Write(key.W);
                w.Write(key.InX);
                w.Write(key.InY);
                w.Write(key.InZ);
                w.Write(key.InW);
                w.Write(key.OutX);
                w.Write(key.OutY);
                w.Write(key.OutZ);
                w.Write(key.OutW);
            }

            for (int i = 0; i < markers.Length; i++)
            {
                var marker = markers[i];
                w.Write(marker.Time);
                w.Write(marker.Group ?? string.Empty);
                w.Write(marker.Value ?? string.Empty);
                w.Write(marker.Text ?? string.Empty);
                w.Write((byte)marker.Kind);
                w.Write(marker.Sound.Value);
            }
        }
    }
}
