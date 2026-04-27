using System.Collections.Generic;
using UnityEngine;

namespace VVardenfell.Importer.Nif
{
    /// <summary>
    /// Base class for every NIF record. Concrete subclasses implement <see cref="Read"/>
    /// against a <see cref="NifStream"/>. References between records are int32 indices
    /// (-1 = null) resolved after the full array is loaded; we keep them as ints.
    ///
    /// Scope: Morrowind NIF v4.0.0.2 only. Layout follows OpenMW components/nif/* but is
    /// reimplemented from the version-gated Morrowind paths.
    /// </summary>
    public abstract class NifRecord
    {
        public string RecordType;   // as read from file (type-name string)
        public int RecordIndex;     // position in NifFile.Records

        public abstract void Read(NifStream s);
    }


    // ------------------------------------------------------------------------
    // Bases
    // ------------------------------------------------------------------------


    /// <summary>Extra data record — an optional linked list attached to NiObjectNET.</summary>
    public class Extra : NifRecord
    {
        public int NextExtra;      // link
        public uint RecordSize;

        public override void Read(NifStream s)
        {
            // Morrowind: version <= 4.2.2.0 path (no mName at this level)
            NextExtra = s.ReadInt32();
            RecordSize = s.ReadUInt32();
        }
    }


    public class NiObjectNET : NifRecord
    {
        public string Name;
        public int ExtraData;      // link (single extra for MW)
        public int Controller;     // link

        public override void Read(NifStream s)
        {
            Name = s.ReadSizedString();
            ExtraData = s.ReadInt32();
            Controller = s.ReadInt32();
        }
    }


    /// <summary>Morrowind KF root: text keys live in extra data and target names in paired NiStringExtraData records.</summary>
    public class NiSequenceStreamHelper : NiObjectNET { }


    public class NiAVObject : NiObjectNET
    {
        public ushort Flags;
        public Vector3 Translation;
        public Matrix4x4 Rotation;
        public float Scale;
        public Vector3 Velocity;
        public int[] PropertyLinks;
        public bool HasBounds;
        public BoundingVolume Bounds;

        public override void Read(NifStream s)
        {
            base.Read(s);
            Flags = s.ReadUInt16();
            Translation = s.ReadVec3();
            Rotation = s.ReadMatrix3();
            Scale = s.ReadFloat();
            Velocity = s.ReadVec3();
            PropertyLinks = ReadLinkArray(s);
            HasBounds = s.ReadBool32();
            if (HasBounds)
            {
                Bounds = new BoundingVolume();
                Bounds.Read(s);
            }
        }

        protected static int[] ReadLinkArray(NifStream s)
        {
            uint n = s.ReadUInt32();
            var arr = new int[n];
            for (int i = 0; i < n; i++) arr[i] = s.ReadInt32();
            return arr;
        }
    }


    public class BoundingVolume
    {
        public uint Type;
        public Vector3 SphereCenter;
        public float SphereRadius;
        public Vector3 BoxCenter;
        public Matrix4x4 BoxAxes;
        public Vector3 BoxExtents;

        public void Read(NifStream s)
        {
            Type = s.ReadUInt32();
            switch (Type)
            {
                case 0xFFFFFFFF: // BASE_BV — no data
                    break;
                case 0: // SPHERE_BV
                    SphereCenter = s.ReadVec3();
                    SphereRadius = s.ReadFloat();
                    break;
                case 1: // BOX_BV
                    BoxCenter = s.ReadVec3();
                    BoxAxes = s.ReadMatrix3();
                    BoxExtents = s.ReadVec3();
                    break;
                default:
                    throw new System.IO.InvalidDataException(
                        $"Unsupported BoundingVolume type {Type} at 0x{s.Position - 4:X}");
            }
        }
    }


    // ------------------------------------------------------------------------
    // Scene graph
    // ------------------------------------------------------------------------


    public class NiNode : NiAVObject
    {
        public int[] Children;
        public int[] Effects;

        public override void Read(NifStream s)
        {
            base.Read(s);
            Children = ReadLinkArray(s);
            Effects = ReadLinkArray(s);
        }
    }


    // AvoidNode, RootCollisionNode, NiBSAnimationNode, NiBSParticleNode, NiCollisionSwitch
    // all use NiNode layout in Morrowind.
    public class AvoidNode : NiNode { }
    public class RootCollisionNode : NiNode { }
    public class NiBSAnimationNode : NiNode { }
    public class NiBSParticleNode : NiNode { }
    public class NiCollisionSwitch : NiNode { }


    public class NiBillboardNode : NiNode
    {
        // Morrowind billboard mode is encoded in the NiAVObject.Flags field, no extra bytes.
    }


    public class NiCamera : NiAVObject
    {
        public float Left;
        public float Right;
        public float Top;
        public float Bottom;
        public float NearDistance;
        public float FarDistance;
        public float ViewportLeft;
        public float ViewportRight;
        public float ViewportTop;
        public float ViewportBottom;
        public float LodAdjust;
        public int Scene;

        public override void Read(NifStream s)
        {
            base.Read(s);
            Left = s.ReadFloat();
            Right = s.ReadFloat();
            Top = s.ReadFloat();
            Bottom = s.ReadFloat();
            NearDistance = s.ReadFloat();
            FarDistance = s.ReadFloat();
            ViewportLeft = s.ReadFloat();
            ViewportRight = s.ReadFloat();
            ViewportTop = s.ReadFloat();
            ViewportBottom = s.ReadFloat();
            LodAdjust = s.ReadFloat();
            Scene = s.ReadInt32();
            s.Skip(4);
        }
    }


    public class NiSwitchNode : NiNode
    {
        public uint InitialIndex;

        public override void Read(NifStream s)
        {
            base.Read(s);
            InitialIndex = s.ReadUInt32();
        }
    }


    public class NiLODNode : NiSwitchNode
    {
        public Vector3 LodCenter;
        public LodRange[] Levels;

        public struct LodRange
        {
            public float MinRange;
            public float MaxRange;
        }

        public override void Read(NifStream s)
        {
            base.Read(s);
            LodCenter = s.ReadVec3();
            uint count = s.ReadUInt32();
            Levels = new LodRange[count];
            for (int i = 0; i < count; i++)
            {
                Levels[i] = new LodRange
                {
                    MinRange = s.ReadFloat(),
                    MaxRange = s.ReadFloat(),
                };
            }
        }
    }


    public class NiFltAnimationNode : NiSwitchNode
    {
        public float Duration;

        public override void Read(NifStream s)
        {
            base.Read(s);
            Duration = s.ReadFloat();
        }
    }


    // ------------------------------------------------------------------------
    // Geometry
    // ------------------------------------------------------------------------


    public class NiGeometry : NiAVObject
    {
        public int Data;
        public int Skin;

        public override void Read(NifStream s)
        {
            base.Read(s);
            Data = s.ReadInt32();
            Skin = s.ReadInt32();
            // MaterialData — Morrowind (version < 10.0.1.0) reads nothing, skip.
        }
    }


    public class NiTriShape : NiGeometry { }
    public class NiTriStrips : NiGeometry { }
    public class NiLines : NiGeometry { }
    public class NiParticles : NiGeometry { }
    public class NiAutoNormalParticles : NiParticles { }
    public class NiRotatingParticles : NiParticles { }


    /// <summary>Morrowind particle geometry data — same shape as NiGeometryData + particle counts.</summary>
    public class NiParticlesData : NiGeometryData
    {
        public ushort NumParticles;
        public float[] Radii;     // size 1 in MW
        public ushort ActiveCount;
        public float[] Sizes;     // optional

        public override void Read(NifStream s)
        {
            base.Read(s);
            NumParticles = s.ReadUInt16();
            // numRadii = 1 for MW (pre-10.0.1.0 path)
            Radii = new float[1];
            Radii[0] = s.ReadFloat();
            ActiveCount = s.ReadUInt16();
            bool hasSizes = s.ReadBool32();
            if (hasSizes)
            {
                Sizes = new float[NumVertices];
                for (int i = 0; i < NumVertices; i++) Sizes[i] = s.ReadFloat();
            }
        }
    }


    public class NiAutoNormalParticlesData : NiParticlesData { }


    public class NiRotatingParticlesData : NiParticlesData
    {
        public Quaternion[] Rotations;

        public override void Read(NifStream s)
        {
            base.Read(s);
            bool hasRotations = s.ReadBool32();
            if (hasRotations)
            {
                Rotations = new Quaternion[NumVertices];
                for (int i = 0; i < NumVertices; i++) Rotations[i] = s.ReadQuat();
            }
        }
    }


    public class NiGeometryData : NifRecord
    {
        public ushort NumVertices;
        public Vector3[] Vertices;
        public Vector3[] Normals;
        public Vector3 BoundSphereCenter;
        public float BoundSphereRadius;
        public Color[] Colors;
        public ushort DataFlags; // in MW, this IS the UV-set count
        public Vector2[][] UvSets;

        public override void Read(NifStream s)
        {
            NumVertices = s.ReadUInt16();
            bool hasVerts = s.ReadBool32();
            if (hasVerts)
            {
                Vertices = new Vector3[NumVertices];
                for (int i = 0; i < NumVertices; i++) Vertices[i] = s.ReadVec3();
            }
            bool hasNormals = s.ReadBool32();
            if (hasNormals)
            {
                Normals = new Vector3[NumVertices];
                for (int i = 0; i < NumVertices; i++) Normals[i] = s.ReadVec3();
            }
            BoundSphereCenter = s.ReadVec3();
            BoundSphereRadius = s.ReadFloat();
            bool hasColors = s.ReadBool32();
            if (hasColors)
            {
                Colors = new Color[NumVertices];
                for (int i = 0; i < NumVertices; i++)
                    Colors[i] = new Color(s.ReadFloat(), s.ReadFloat(), s.ReadFloat(), s.ReadFloat());
            }
            DataFlags = s.ReadUInt16(); // pre-4.2.2.0 path: this is numUVSets for MW
            int numUVs = DataFlags;
            bool hasUV = s.ReadBool32();
            if (!hasUV) numUVs = 0;
            if (numUVs > 0)
            {
                UvSets = new Vector2[numUVs][];
                for (int set = 0; set < numUVs; set++)
                {
                    var uvs = new Vector2[NumVertices];
                    for (int i = 0; i < NumVertices; i++)
                    {
                        float u = s.ReadFloat();
                        float v = s.ReadFloat();
                        // Morrowind/OpenMW consume NIF UVs as-authored. Texture upload owns image orientation.
                        uvs[i] = new Vector2(u, v);
                    }
                    UvSets[set] = uvs;
                }
            }
            else
            {
                UvSets = System.Array.Empty<Vector2[]>();
            }
        }
    }


    public class NiTriBasedGeomData : NiGeometryData
    {
        public ushort NumTriangles;

        public override void Read(NifStream s)
        {
            base.Read(s);
            NumTriangles = s.ReadUInt16();
        }
    }


    public class NiTriShapeData : NiTriBasedGeomData
    {
        public ushort[] Triangles;
        public ushort[][] MatchGroups;

        public override void Read(NifStream s)
        {
            base.Read(s);
            uint numIndices = s.ReadUInt32();
            Triangles = new ushort[numIndices];
            for (int i = 0; i < numIndices; i++) Triangles[i] = s.ReadUInt16();

            ushort numGroups = s.ReadUInt16();
            MatchGroups = new ushort[numGroups][];
            for (int g = 0; g < numGroups; g++)
            {
                ushort len = s.ReadUInt16();
                var grp = new ushort[len];
                for (int k = 0; k < len; k++) grp[k] = s.ReadUInt16();
                MatchGroups[g] = grp;
            }
        }
    }


    public class NiTriStripsData : NiTriBasedGeomData
    {
        public ushort[][] Strips;

        public override void Read(NifStream s)
        {
            base.Read(s);

            ushort numStrips = s.ReadUInt16();
            var lengths = new ushort[numStrips];
            for (int i = 0; i < numStrips; i++)
                lengths[i] = s.ReadUInt16();

            Strips = new ushort[numStrips][];
            for (int i = 0; i < numStrips; i++)
            {
                var strip = new ushort[lengths[i]];
                for (int j = 0; j < strip.Length; j++)
                    strip[j] = s.ReadUInt16();
                Strips[i] = strip;
            }
        }
    }


    // ------------------------------------------------------------------------
    // Properties
    // ------------------------------------------------------------------------


    public class NiProperty : NiObjectNET { }


    public class NiTexturingProperty : NiProperty
    {
        public ushort Flags;   // Morrowind reads this — version <= VER_OB_OLD (10.0.1.2)
        public uint ApplyMode;
        public TextureSlot[] Textures;

        public class TextureSlot
        {
            public bool Enabled;
            public int SourceTexture; // link
            public uint Clamp;
            public uint Filter;
            public uint UVSet;

            public void Read(NifStream s)
            {
                Enabled = s.ReadBool32();
                if (!Enabled) return;

                SourceTexture = s.ReadInt32();
                Clamp = s.ReadUInt32();
                Filter = s.ReadUInt32();
                UVSet = s.ReadUInt32();
                s.Skip(4); // PS2 filtering settings
                s.Skip(2); // Unknown (MW, version <= 4.1.0.12)
            }
        }

        public override void Read(NifStream s)
        {
            base.Read(s);
            Flags = s.ReadUInt16();
            ApplyMode = s.ReadUInt32();
            uint count = s.ReadUInt32();
            Textures = new TextureSlot[count];
            for (int i = 0; i < count; i++)
            {
                Textures[i] = new TextureSlot();
                Textures[i].Read(s);
                // MW doesn't have envmap/parallax secondary reads we need here.
            }
        }
    }


    public class NiMaterialProperty : NiProperty
    {
        public ushort Flags;
        public Vector3 Ambient;
        public Vector3 Diffuse;
        public Vector3 Specular;
        public Vector3 Emissive;
        public float Glossiness;
        public float Alpha;

        public override void Read(NifStream s)
        {
            base.Read(s);
            Flags = s.ReadUInt16();
            Ambient = s.ReadVec3();
            Diffuse = s.ReadVec3();
            Specular = s.ReadVec3();
            Emissive = s.ReadVec3();
            Glossiness = s.ReadFloat();
            Alpha = s.ReadFloat();
        }
    }


    public class NiAlphaProperty : NiProperty
    {
        public ushort Flags;
        public byte Threshold;

        public override void Read(NifStream s)
        {
            base.Read(s);
            Flags = s.ReadUInt16();
            Threshold = s.ReadByte();
        }
    }


    public class NiZBufferProperty : NiProperty
    {
        public ushort Flags;

        public override void Read(NifStream s)
        {
            base.Read(s);
            Flags = s.ReadUInt16();
            // Morrowind (version < 4.1.0.12) does not read mTestFunction here.
        }
    }


    public class NiVertexColorProperty : NiProperty
    {
        public ushort Flags;
        public uint VertexMode;
        public uint LightingMode;

        public override void Read(NifStream s)
        {
            base.Read(s);
            Flags = s.ReadUInt16();
            VertexMode = s.ReadUInt32();
            LightingMode = s.ReadUInt32();
        }
    }


    public class NiShadeProperty : NiProperty
    {
        public ushort Flags;

        public override void Read(NifStream s)
        {
            base.Read(s);
            Flags = s.ReadUInt16();
        }
    }


    public class NiSpecularProperty : NiProperty
    {
        public ushort Flags;

        public override void Read(NifStream s)
        {
            base.Read(s);
            Flags = s.ReadUInt16();
        }
    }


    public class NiWireframeProperty : NiProperty
    {
        public ushort Flags;

        public override void Read(NifStream s)
        {
            base.Read(s);
            Flags = s.ReadUInt16();
        }
    }


    public class NiDitherProperty : NiProperty
    {
        public ushort Flags;

        public override void Read(NifStream s)
        {
            base.Read(s);
            Flags = s.ReadUInt16();
        }
    }


    public class NiStencilProperty : NiProperty
    {
        public ushort Flags;
        public byte Enabled;
        public uint TestFunction, StencilRef, StencilMask, FailAction, ZFailAction, PassAction, DrawMode;

        public override void Read(NifStream s)
        {
            base.Read(s);
            Flags = s.ReadUInt16();
            Enabled = s.ReadByte();
            TestFunction = s.ReadUInt32();
            StencilRef = s.ReadUInt32();
            StencilMask = s.ReadUInt32();
            FailAction = s.ReadUInt32();
            ZFailAction = s.ReadUInt32();
            PassAction = s.ReadUInt32();
            DrawMode = s.ReadUInt32();
        }
    }


    // ------------------------------------------------------------------------
    // Animation keys — used by controllers and NiColorData/NiPosData/NiFloatData.
    // We don't consume the data yet, just parse the shape to stay in sync.
    // ------------------------------------------------------------------------


    public enum NifInterpolationType : uint
    {
        Unknown = 0, Linear = 1, Quadratic = 2, TBC = 3, XYZ = 4, Constant = 5,
    }


    internal enum NifKeyValueKind { Float, Vec3, Vec4, Quat }


    public struct NifAnimationKey
    {
        public float Time;
        public Vector4 Value;
        public Vector4 InTan;
        public Vector4 OutTan;
    }


    public sealed class NifKeyGroup
    {
        public NifInterpolationType InterpolationType;
        public NifAnimationKey[] Keys = System.Array.Empty<NifAnimationKey>();
    }


    internal static class NifKeyReader
    {
        /// <summary>
        /// Reads a KeyGroup: uint32 count, uint32 type if count&gt;0 (or morph is true), then keys.
        /// Returns the interpolation type (so NiKeyframeData can detect XYZ rotations).
        /// Per OpenMW, Quadratic for quats has no tangents.
        /// </summary>
        public static NifKeyGroup Read(NifStream s, NifKeyValueKind kind, bool morph = false)
        {
            var group = new NifKeyGroup();
            uint count = s.ReadUInt32();
            if (count == 0 && !morph) return group;
            var type = (NifInterpolationType)s.ReadUInt32();
            group.InterpolationType = type;
            if (count == 0) return group;

            group.Keys = new NifAnimationKey[count];
            for (int i = 0; i < count; i++)
            {
                var key = new NifAnimationKey { Time = s.ReadFloat() };

                switch (type)
                {
                    case NifInterpolationType.Linear:
                    case NifInterpolationType.Constant:
                        key.Value = ReadValue(s, kind);
                        break;
                    case NifInterpolationType.Quadratic:
                        key.Value = ReadValue(s, kind);
                        if (kind != NifKeyValueKind.Quat)
                        {
                            key.InTan = ReadValue(s, kind);
                            key.OutTan = ReadValue(s, kind);
                        }
                        break;
                    case NifInterpolationType.TBC:
                        key.Value = ReadValue(s, kind);
                        key.InTan.x = s.ReadFloat(); // tension
                        key.InTan.y = s.ReadFloat(); // bias
                        key.InTan.z = s.ReadFloat(); // continuity
                        break;
                    case NifInterpolationType.XYZ:
                        // No inline keys — caller handles the followup X/Y/Z sub-maps.
                        throw new System.InvalidOperationException("XYZ keys must be handled externally");
                    default:
                        throw new System.NotSupportedException($"Unknown key interpolation type {(int)type}");
                }
                group.Keys[i] = key;
            }
            return group;
        }

        private static Vector4 ReadValue(NifStream s, NifKeyValueKind kind)
        {
            switch (kind)
            {
                case NifKeyValueKind.Float:
                    return new Vector4(s.ReadFloat(), 0f, 0f, 0f);
                case NifKeyValueKind.Vec3:
                {
                    Vector3 value = s.ReadVec3();
                    return new Vector4(value.x, value.y, value.z, 0f);
                }
                case NifKeyValueKind.Vec4:
                    return new Vector4(s.ReadFloat(), s.ReadFloat(), s.ReadFloat(), s.ReadFloat());
                case NifKeyValueKind.Quat:
                {
                    Quaternion value = s.ReadQuat();
                    return new Vector4(value.x, value.y, value.z, value.w);
                }
                default:
                    return default;
            }
        }
    }


    // ------------------------------------------------------------------------
    // Controllers (parsed structurally; not driven in first pass)
    // ------------------------------------------------------------------------


    public class NiTimeController : NifRecord
    {
        public int NextController;
        public ushort Flags;
        public float Frequency, Phase, TimeStart, TimeStop;
        public int Target;

        public override void Read(NifStream s)
        {
            NextController = s.ReadInt32();
            Flags = s.ReadUInt16();
            Frequency = s.ReadFloat();
            Phase = s.ReadFloat();
            TimeStart = s.ReadFloat();
            TimeStop = s.ReadFloat();
            Target = s.ReadInt32(); // >= 3.3.0.13 — always true for MW
        }
    }


    public class NiParticleSystemController : NiTimeController
    {
        public float Speed, SpeedVariation, Declination, DeclinationVariation, PlanarAngle, PlanarAngleVariation;
        public Vector3 InitialNormal;
        public Vector4 InitialColor;
        public float InitialSize, EmitStartTime, EmitStopTime;
        public byte ResetParticleSystem;
        public float BirthRate, Lifetime, LifetimeVariation;
        public ushort EmitFlags;
        public Vector3 EmitterDimensions;
        public int Emitter;
        public ushort NumSpawnGenerations;
        public float PercentageSpawned;
        public ushort SpawnMultiplier;
        public float SpawnSpeedChaos, SpawnDirChaos;
        public ushort NumParticles, NumValid;
        public int Modifier;
        public int Collider;
        public byte StaticTargetBound;

        public override void Read(NifStream s)
        {
            base.Read(s);
            Speed = s.ReadFloat();
            SpeedVariation = s.ReadFloat();
            Declination = s.ReadFloat();
            DeclinationVariation = s.ReadFloat();
            PlanarAngle = s.ReadFloat();
            PlanarAngleVariation = s.ReadFloat();
            InitialNormal = s.ReadVec3();
            InitialColor = new Vector4(s.ReadFloat(), s.ReadFloat(), s.ReadFloat(), s.ReadFloat());
            InitialSize = s.ReadFloat();
            EmitStartTime = s.ReadFloat();
            EmitStopTime = s.ReadFloat();
            ResetParticleSystem = s.ReadByte();
            BirthRate = s.ReadFloat();
            Lifetime = s.ReadFloat();
            LifetimeVariation = s.ReadFloat();
            EmitFlags = s.ReadUInt16();
            EmitterDimensions = s.ReadVec3();
            Emitter = s.ReadInt32();
            NumSpawnGenerations = s.ReadUInt16();
            PercentageSpawned = s.ReadFloat();
            SpawnMultiplier = s.ReadUInt16();
            SpawnSpeedChaos = s.ReadFloat();
            SpawnDirChaos = s.ReadFloat();
            NumParticles = s.ReadUInt16();
            NumValid = s.ReadUInt16();
            // Particle array: NumParticles × 40 bytes (vec3 velocity + vec3 rotAxis + 3 floats + 2 uint16)
            for (int i = 0; i < NumParticles; i++)
            {
                s.Skip(12 + 12 + 4 + 4 + 4 + 2 + 2); // 40 bytes
            }
            s.Skip(4); // NiEmitterModifier link
            Modifier = s.ReadInt32();
            Collider = s.ReadInt32();
            StaticTargetBound = s.ReadByte(); // >= 3.3.0.15 — always true for MW
        }
    }


    // NiBSPArrayController shares layout with NiParticleSystemController.
    public class NiBSPArrayController : NiParticleSystemController { }


    /// <summary>Animates node transforms (rotation/translation/scale) — common on creatures.</summary>
    public class NiKeyframeController : NiTimeController
    {
        public int Data;

        public override void Read(NifStream s)
        {
            base.Read(s);
            Data = s.ReadInt32(); // <= 10.1.0.103 — always true for MW
        }
    }


}
