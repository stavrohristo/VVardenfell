using System;
using System.IO;

namespace VVardenfell.Core.Cache
{
    public enum ActorBodyPartMeshPart : byte
    {
        Head = 0,
        Hair = 1,
        Neck = 2,
        Chest = 3,
        Groin = 4,
        Hand = 5,
        Wrist = 6,
        Forearm = 7,
        Upperarm = 8,
        Foot = 9,
        Ankle = 10,
        Knee = 11,
        Upperleg = 12,
        Clavicle = 13,
        Tail = 14,
    }

    public enum ActorBodyPartMeshType : byte
    {
        Skin = 0,
        Clothing = 1,
        Armor = 2,
    }

    public struct ActorBodyPartDef
    {
        public ContentId ContentId;
        public string Id;
        public string RaceId;
        public string Model;
        public ActorBodyPartMeshPart Part;
        public ActorBodyPartMeshType Type;
        public byte Female;
        public byte Vampire;
        public byte NotPlayable;
        public byte FirstPerson;
    }

    public enum ActorAnimationTrackKind : byte
    {
        Translation = 1,
        Rotation = 2,
        Scale = 3,
        XRotation = 4,
        YRotation = 5,
        ZRotation = 6,
        Visibility = 7,
        MorphWeight = 8,
        UvTransform = 9,
        Alpha = 10,
        MaterialColor = 11,
    }

    public enum ActorAnimationInterpolation : byte
    {
        Unknown = 0,
        Linear = 1,
        Quadratic = 2,
        Tbc = 3,
        Xyz = 4,
        Constant = 5,
    }

    public struct ActorSkeletonBoneDef
    {
        public string Name;
        public int ParentIndex;
        public float PosX, PosY, PosZ;
        public float RotX, RotY, RotZ, RotW;
        public float Scale;
    }

    public sealed class ActorSkeletonDef
    {
        public string ModelPath;
        public int AccumulationBoneIndex;
        public ActorSkeletonBoneDef[] Bones = Array.Empty<ActorSkeletonBoneDef>();
    }

    public struct ActorSkinWeightDef
    {
        public ushort VertexIndex;
        public ushort BoneIndex;
        public float Weight;
    }

    public sealed class ActorSkinMeshDef
    {
        public string ModelPath;
        public string NodeName;
        public int MeshIndex;
        public int MaterialIndex = -1;
        public int TextureIndex = -1;
        public byte IsRigid;
        public int SkeletonIndex;
        public int FirstWeightIndex;
        public int WeightCount;
        public float BoundsCenterX;
        public float BoundsCenterY;
        public float BoundsCenterZ;
        public float BoundsExtentsX;
        public float BoundsExtentsY;
        public float BoundsExtentsZ;
        public int[] BoneIndices = Array.Empty<int>();
        public string[] BoneNames = Array.Empty<string>();
        public string SkinRootName;
        public float[] BindPoseMatrices = Array.Empty<float>();
        public float[] GeometryToSkeletonMatrix = Array.Empty<float>();
        public float RigidOffsetX;
        public float RigidOffsetY;
        public float RigidOffsetZ;
        public float[] VertexPositions = Array.Empty<float>();
        public float[] VertexNormals = Array.Empty<float>();
        public float[] VertexUvs = Array.Empty<float>();
        public int[] Indices = Array.Empty<int>();
    }

    public struct ActorAnimationTextKeyDef
    {
        public float Time;
        public string Text;
    }

    public enum ActorAnimationTextMarkerKind : byte
    {
        Marker = 0,
        Start = 1,
        LoopStart = 2,
        LoopStop = 3,
        Stop = 4,
    }

    public struct ActorAnimationTextMarkerDef
    {
        public float Time;
        public string Group;
        public string Value;
        public string Text;
        public ActorAnimationTextMarkerKind Kind;
    }

    public struct ActorAnimationKeyDef
    {
        public float Time;
        public float X, Y, Z, W;
        public float InX, InY, InZ, InW;
        public float OutX, OutY, OutZ, OutW;
    }

    public sealed class ActorAnimationTrackDef
    {
        public string TargetName;
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

    public sealed class ActorAnimationClipDef
    {
        public string SourcePath;
        public string Name;
        public string AccumRootName;
        public float Duration;
        public int FirstTrackIndex;
        public int TrackCount;
        public int FirstTextKeyIndex;
        public int TextKeyCount;
        public int FirstTextMarkerIndex;
        public int TextMarkerCount;
    }

    public sealed class ActorAnimationCatalogData
    {
        public ActorAnimationModelBindingDef[] ModelBindings = Array.Empty<ActorAnimationModelBindingDef>();
        public ActorSkeletonDef[] Skeletons = Array.Empty<ActorSkeletonDef>();
        public ActorSkinMeshDef[] SkinMeshes = Array.Empty<ActorSkinMeshDef>();
        public ActorSkinWeightDef[] SkinWeights = Array.Empty<ActorSkinWeightDef>();
        public ActorAnimationClipDef[] Clips = Array.Empty<ActorAnimationClipDef>();
        public ActorAnimationTrackDef[] Tracks = Array.Empty<ActorAnimationTrackDef>();
        public ActorAnimationKeyDef[] Keys = Array.Empty<ActorAnimationKeyDef>();
        public ActorAnimationTextKeyDef[] TextKeys = Array.Empty<ActorAnimationTextKeyDef>();
        public ActorAnimationTextMarkerDef[] TextMarkers = Array.Empty<ActorAnimationTextMarkerDef>();
    }

    public sealed class ActorAnimationModelBindingDef
    {
        public string ModelPath;
        public string BindReferenceSkeletonPath;
        public int SkeletonIndex = -1;
        public int FirstSkinMeshIndex = -1;
        public int SkinMeshCount;
        public int FirstClipIndex = -1;
        public int ClipCount;
    }

    public static class ActorAnimationFile
    {
        const uint Magic = 0x4D494E41u; // 'ANIM'
        const uint Version = 24u;

        public static bool TryRead(string path, out ActorAnimationCatalogData data)
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

        public static ActorAnimationCatalogData Read(string path)
        {
            using var fs = File.OpenRead(path);
            using var r = new BinaryReader(fs);

            uint magic = r.ReadUInt32();
            if (magic != Magic)
                throw new InvalidDataException($"Bad actor animation magic 0x{magic:X8} in '{path}'.");

            uint version = r.ReadUInt32();
            if (version != Version)
                throw new InvalidDataException($"Unsupported actor animation version {version} in '{path}'.");

            var data = new ActorAnimationCatalogData
            {
                ModelBindings = ReadArray(r, ReadModelBinding),
                Skeletons = ReadArray(r, ReadSkeleton),
                SkinMeshes = ReadArray(r, ReadSkinMesh),
                SkinWeights = ReadArray(r, ReadSkinWeight),
                Clips = ReadArray(r, ReadClip),
                Tracks = ReadArray(r, ReadTrack),
                Keys = ReadArray(r, ReadKey),
                TextKeys = ReadArray(r, ReadTextKey),
                TextMarkers = ReadArray(r, ReadTextMarker),
            };
            return data;
        }

        public static void Write(string path, ActorAnimationCatalogData data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
            using var fs = File.Create(path);
            using var w = new BinaryWriter(fs);
            w.Write(Magic);
            w.Write(Version);
            WriteArray(w, data?.ModelBindings, WriteModelBinding);
            WriteArray(w, data?.Skeletons, WriteSkeleton);
            WriteArray(w, data?.SkinMeshes, WriteSkinMesh);
            WriteArray(w, data?.SkinWeights, WriteSkinWeight);
            WriteArray(w, data?.Clips, WriteClip);
            WriteArray(w, data?.Tracks, WriteTrack);
            WriteArray(w, data?.Keys, WriteKey);
            WriteArray(w, data?.TextKeys, WriteTextKey);
            WriteArray(w, data?.TextMarkers, WriteTextMarker);
        }

        static void WriteModelBinding(BinaryWriter w, ActorAnimationModelBindingDef value)
        {
            w.Write(value?.ModelPath ?? string.Empty);
            w.Write(value?.BindReferenceSkeletonPath ?? string.Empty);
            w.Write(value?.SkeletonIndex ?? -1);
            w.Write(value?.FirstSkinMeshIndex ?? -1);
            w.Write(value?.SkinMeshCount ?? 0);
            w.Write(value?.FirstClipIndex ?? -1);
            w.Write(value?.ClipCount ?? 0);
        }

        static ActorAnimationModelBindingDef ReadModelBinding(BinaryReader r)
            => new()
            {
                ModelPath = r.ReadString(),
                BindReferenceSkeletonPath = r.ReadString(),
                SkeletonIndex = r.ReadInt32(),
                FirstSkinMeshIndex = r.ReadInt32(),
                SkinMeshCount = r.ReadInt32(),
                FirstClipIndex = r.ReadInt32(),
                ClipCount = r.ReadInt32(),
            };

        static void WriteSkeleton(BinaryWriter w, ActorSkeletonDef value)
        {
            w.Write(value?.ModelPath ?? string.Empty);
            w.Write(value?.AccumulationBoneIndex ?? -1);
            WriteArray(w, value?.Bones, WriteSkeletonBone);
        }

        static ActorSkeletonDef ReadSkeleton(BinaryReader r)
            => new()
            {
                ModelPath = r.ReadString(),
                AccumulationBoneIndex = r.ReadInt32(),
                Bones = ReadArray(r, ReadSkeletonBone),
            };

        static void WriteSkeletonBone(BinaryWriter w, ActorSkeletonBoneDef value)
        {
            w.Write(value.Name ?? string.Empty);
            w.Write(value.ParentIndex);
            w.Write(value.PosX); w.Write(value.PosY); w.Write(value.PosZ);
            w.Write(value.RotX); w.Write(value.RotY); w.Write(value.RotZ); w.Write(value.RotW);
            w.Write(value.Scale);
        }

        static ActorSkeletonBoneDef ReadSkeletonBone(BinaryReader r)
            => new()
            {
                Name = r.ReadString(),
                ParentIndex = r.ReadInt32(),
                PosX = r.ReadSingle(),
                PosY = r.ReadSingle(),
                PosZ = r.ReadSingle(),
                RotX = r.ReadSingle(),
                RotY = r.ReadSingle(),
                RotZ = r.ReadSingle(),
                RotW = r.ReadSingle(),
                Scale = r.ReadSingle(),
            };

        static void WriteSkinMesh(BinaryWriter w, ActorSkinMeshDef value)
        {
            w.Write(value?.ModelPath ?? string.Empty);
            w.Write(value?.NodeName ?? string.Empty);
            w.Write(value?.MeshIndex ?? -1);
            w.Write(value?.MaterialIndex ?? -1);
            w.Write(value?.TextureIndex ?? -1);
            w.Write(value?.IsRigid ?? 0);
            w.Write(value?.SkeletonIndex ?? -1);
            w.Write(value?.FirstWeightIndex ?? -1);
            w.Write(value?.WeightCount ?? 0);
            w.Write(value?.BoundsCenterX ?? 0f);
            w.Write(value?.BoundsCenterY ?? 0f);
            w.Write(value?.BoundsCenterZ ?? 0f);
            w.Write(value?.BoundsExtentsX ?? 0f);
            w.Write(value?.BoundsExtentsY ?? 0f);
            w.Write(value?.BoundsExtentsZ ?? 0f);
            WriteIntArray(w, value?.BoneIndices);
            WriteStringArray(w, value?.BoneNames);
            w.Write(value?.SkinRootName ?? string.Empty);
            WriteFloatArray(w, value?.BindPoseMatrices);
            WriteFloatArray(w, value?.GeometryToSkeletonMatrix);
            w.Write(value?.RigidOffsetX ?? 0f);
            w.Write(value?.RigidOffsetY ?? 0f);
            w.Write(value?.RigidOffsetZ ?? 0f);
            WriteFloatArray(w, value?.VertexPositions);
            WriteFloatArray(w, value?.VertexNormals);
            WriteFloatArray(w, value?.VertexUvs);
            WriteIntArray(w, value?.Indices);
        }

        static ActorSkinMeshDef ReadSkinMesh(BinaryReader r)
            => new()
            {
                ModelPath = r.ReadString(),
                NodeName = r.ReadString(),
                MeshIndex = r.ReadInt32(),
                MaterialIndex = r.ReadInt32(),
                TextureIndex = r.ReadInt32(),
                IsRigid = r.ReadByte(),
                SkeletonIndex = r.ReadInt32(),
                FirstWeightIndex = r.ReadInt32(),
                WeightCount = r.ReadInt32(),
                BoundsCenterX = r.ReadSingle(),
                BoundsCenterY = r.ReadSingle(),
                BoundsCenterZ = r.ReadSingle(),
                BoundsExtentsX = r.ReadSingle(),
                BoundsExtentsY = r.ReadSingle(),
                BoundsExtentsZ = r.ReadSingle(),
                BoneIndices = ReadIntArray(r),
                BoneNames = ReadStringArray(r),
                SkinRootName = r.ReadString(),
                BindPoseMatrices = ReadFloatArray(r),
                GeometryToSkeletonMatrix = ReadFloatArray(r),
                RigidOffsetX = r.ReadSingle(),
                RigidOffsetY = r.ReadSingle(),
                RigidOffsetZ = r.ReadSingle(),
                VertexPositions = ReadFloatArray(r),
                VertexNormals = ReadFloatArray(r),
                VertexUvs = ReadFloatArray(r),
                Indices = ReadIntArray(r),
            };

        static void WriteSkinWeight(BinaryWriter w, ActorSkinWeightDef value)
        {
            w.Write(value.VertexIndex);
            w.Write(value.BoneIndex);
            w.Write(value.Weight);
        }

        static ActorSkinWeightDef ReadSkinWeight(BinaryReader r)
            => new()
            {
                VertexIndex = r.ReadUInt16(),
                BoneIndex = r.ReadUInt16(),
                Weight = r.ReadSingle(),
            };

        static void WriteClip(BinaryWriter w, ActorAnimationClipDef value)
        {
            w.Write(value?.SourcePath ?? string.Empty);
            w.Write(value?.Name ?? string.Empty);
            w.Write(value?.AccumRootName ?? string.Empty);
            w.Write(value?.Duration ?? 0f);
            w.Write(value?.FirstTrackIndex ?? -1);
            w.Write(value?.TrackCount ?? 0);
            w.Write(value?.FirstTextKeyIndex ?? -1);
            w.Write(value?.TextKeyCount ?? 0);
            w.Write(value?.FirstTextMarkerIndex ?? -1);
            w.Write(value?.TextMarkerCount ?? 0);
        }

        static ActorAnimationClipDef ReadClip(BinaryReader r)
            => new()
            {
                SourcePath = r.ReadString(),
                Name = r.ReadString(),
                AccumRootName = r.ReadString(),
                Duration = r.ReadSingle(),
                FirstTrackIndex = r.ReadInt32(),
                TrackCount = r.ReadInt32(),
                FirstTextKeyIndex = r.ReadInt32(),
                TextKeyCount = r.ReadInt32(),
                FirstTextMarkerIndex = r.ReadInt32(),
                TextMarkerCount = r.ReadInt32(),
            };

        static void WriteTrack(BinaryWriter w, ActorAnimationTrackDef value)
        {
            w.Write(value?.TargetName ?? string.Empty);
            w.Write((byte)(value?.Kind ?? 0));
            w.Write((byte)(value?.Interpolation ?? 0));
            w.Write(value?.AxisOrder ?? 0);
            w.Write(value?.ControllerFlags ?? 0);
            w.Write(value?.Frequency ?? 1f);
            w.Write(value?.Phase ?? 0f);
            w.Write(value?.TimeStart ?? 0f);
            w.Write(value?.TimeStop ?? 0f);
            w.Write(value?.FirstKeyIndex ?? -1);
            w.Write(value?.KeyCount ?? 0);
        }

        static ActorAnimationTrackDef ReadTrack(BinaryReader r)
            => new()
            {
                TargetName = r.ReadString(),
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

        static void WriteKey(BinaryWriter w, ActorAnimationKeyDef value)
        {
            w.Write(value.Time);
            w.Write(value.X); w.Write(value.Y); w.Write(value.Z); w.Write(value.W);
            w.Write(value.InX); w.Write(value.InY); w.Write(value.InZ); w.Write(value.InW);
            w.Write(value.OutX); w.Write(value.OutY); w.Write(value.OutZ); w.Write(value.OutW);
        }

        static ActorAnimationKeyDef ReadKey(BinaryReader r)
            => new()
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

        static void WriteTextKey(BinaryWriter w, ActorAnimationTextKeyDef value)
        {
            w.Write(value.Time);
            w.Write(value.Text ?? string.Empty);
        }

        static ActorAnimationTextKeyDef ReadTextKey(BinaryReader r)
            => new() { Time = r.ReadSingle(), Text = r.ReadString() };

        static void WriteTextMarker(BinaryWriter w, ActorAnimationTextMarkerDef value)
        {
            w.Write(value.Time);
            w.Write(value.Group ?? string.Empty);
            w.Write(value.Value ?? string.Empty);
            w.Write(value.Text ?? string.Empty);
            w.Write((byte)value.Kind);
        }

        static ActorAnimationTextMarkerDef ReadTextMarker(BinaryReader r)
            => new()
            {
                Time = r.ReadSingle(),
                Group = r.ReadString(),
                Value = r.ReadString(),
                Text = r.ReadString(),
                Kind = (ActorAnimationTextMarkerKind)r.ReadByte(),
            };

        static void WriteArray<T>(BinaryWriter w, T[] values, Action<BinaryWriter, T> write)
        {
            int count = values?.Length ?? 0;
            w.Write(count);
            for (int i = 0; i < count; i++)
                write(w, values[i]);
        }

        static T[] ReadArray<T>(BinaryReader r, Func<BinaryReader, T> read)
        {
            int count = r.ReadInt32();
            if (count < 0)
                throw new InvalidDataException($"Negative actor animation array count {count}.");

            var values = new T[count];
            for (int i = 0; i < count; i++)
                values[i] = read(r);
            return values;
        }

        static void WriteIntArray(BinaryWriter w, int[] values)
        {
            int count = values?.Length ?? 0;
            w.Write(count);
            for (int i = 0; i < count; i++)
                w.Write(values[i]);
        }

        static void WriteStringArray(BinaryWriter w, string[] values)
        {
            int count = values?.Length ?? 0;
            w.Write(count);
            for (int i = 0; i < count; i++)
                w.Write(values[i] ?? string.Empty);
        }

        static int[] ReadIntArray(BinaryReader r)
        {
            int count = r.ReadInt32();
            var values = new int[count];
            for (int i = 0; i < count; i++)
                values[i] = r.ReadInt32();
            return values;
        }

        static string[] ReadStringArray(BinaryReader r)
        {
            int count = r.ReadInt32();
            var values = new string[count];
            for (int i = 0; i < count; i++)
                values[i] = r.ReadString();
            return values;
        }

        static void WriteFloatArray(BinaryWriter w, float[] values)
        {
            int count = values?.Length ?? 0;
            w.Write(count);
            for (int i = 0; i < count; i++)
                w.Write(values[i]);
        }

        static float[] ReadFloatArray(BinaryReader r)
        {
            int count = r.ReadInt32();
            var values = new float[count];
            for (int i = 0; i < count; i++)
                values[i] = r.ReadSingle();
            return values;
        }
    }
}
