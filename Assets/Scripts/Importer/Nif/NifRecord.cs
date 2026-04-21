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

    public class NiBillboardNode : NiNode
    {
        // Morrowind billboard mode is encoded in the NiAVObject.Flags field, no extra bytes.
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
                        // NIF → OpenGL convention flips V; Unity follows OpenGL, so keep flip.
                        uvs[i] = new Vector2(u, 1f - v);
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

    internal static class NifKeyReader
    {
        /// <summary>
        /// Reads a KeyGroup: uint32 count, uint32 type if count&gt;0 (or morph is true), then keys.
        /// Returns the interpolation type (so NiKeyframeData can detect XYZ rotations).
        /// Per OpenMW, Quadratic for quats has no tangents.
        /// </summary>
        public static NifInterpolationType Read(NifStream s, NifKeyValueKind kind, bool morph = false)
        {
            uint count = s.ReadUInt32();
            if (count == 0 && !morph) return NifInterpolationType.Unknown;
            var type = (NifInterpolationType)s.ReadUInt32();
            if (count == 0) return type;

            for (int i = 0; i < count; i++)
            {
                s.ReadFloat(); // time

                switch (type)
                {
                    case NifInterpolationType.Linear:
                    case NifInterpolationType.Constant:
                        SkipValue(s, kind);
                        break;
                    case NifInterpolationType.Quadratic:
                        SkipValue(s, kind);
                        if (kind != NifKeyValueKind.Quat)
                        {
                            SkipValue(s, kind); // in tan
                            SkipValue(s, kind); // out tan
                        }
                        break;
                    case NifInterpolationType.TBC:
                        SkipValue(s, kind);
                        s.ReadFloat(); s.ReadFloat(); s.ReadFloat(); // tension, bias, continuity
                        break;
                    case NifInterpolationType.XYZ:
                        // No inline keys — caller handles the followup X/Y/Z sub-maps.
                        throw new System.InvalidOperationException("XYZ keys must be handled externally");
                    default:
                        throw new System.NotSupportedException($"Unknown key interpolation type {(int)type}");
                }
            }
            return type;
        }

        private static void SkipValue(NifStream s, NifKeyValueKind kind)
        {
            switch (kind)
            {
                case NifKeyValueKind.Float: s.ReadFloat(); break;
                case NifKeyValueKind.Vec3: s.Skip(12); break;
                case NifKeyValueKind.Vec4: s.Skip(16); break;
                case NifKeyValueKind.Quat: s.Skip(16); break;
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

    /// <summary>Animates node visibility (on/off over time).</summary>
    public class NiVisController : NiTimeController
    {
        public int Data;

        public override void Read(NifStream s)
        {
            base.Read(s);
            Data = s.ReadInt32();
        }
    }

    /// <summary>Animates alpha on NiMaterialProperty.</summary>
    public class NiAlphaController : NiTimeController
    {
        public int Data;

        public override void Read(NifStream s)
        {
            base.Read(s);
            Data = s.ReadInt32();
        }
    }

    /// <summary>Cycles through textures over time (flipbook).</summary>
    public class NiFlipController : NiTimeController
    {
        public uint TextureSlot;
        public float StartTime, Delta;
        public int[] Sources;

        public override void Read(NifStream s)
        {
            base.Read(s);
            TextureSlot = s.ReadUInt32();
            StartTime = s.ReadFloat();
            Delta = s.ReadFloat();
            uint n = s.ReadUInt32();
            Sources = new int[n];
            for (int i = 0; i < n; i++) Sources[i] = s.ReadInt32();
        }
    }

    /// <summary>Animates UVs on a geometry.</summary>
    public class NiUVController : NiTimeController
    {
        public ushort TextureSet;
        public int Data;

        public override void Read(NifStream s)
        {
            base.Read(s);
            TextureSet = s.ReadUInt16();
            Data = s.ReadInt32();
        }
    }

    /// <summary>Animates a NiMaterialProperty colour channel.</summary>
    public class NiMaterialColorController : NiTimeController
    {
        public int Data;

        public override void Read(NifStream s)
        {
            base.Read(s);
            // For MW the target-colour field is packed into NiAVObject flags; skip.
            Data = s.ReadInt32();
        }
    }

    /// <summary>Drives vertex morph animations (creature faces, bow draw, etc).</summary>
    public class NiGeomMorpherController : NiTimeController
    {
        public int Data;
        public byte AlwaysActive;

        public override void Read(NifStream s)
        {
            base.Read(s);
            // MW < VER_OB_OLD, so the 'mUpdateNormals' u16 is not present.
            Data = s.ReadInt32();
            // MW == VER_MW, so mAlwaysActive IS read.
            AlwaysActive = s.ReadByte();
            // MW <= 10.1.0.105, so we stop here (no interpolator list).
        }
    }

    // ------------------------------------------------------------------------
    // Animation data
    // ------------------------------------------------------------------------

    public class NiKeyframeData : NifRecord
    {
        public override void Read(NifStream s)
        {
            var rotType = NiReadRotationsOrXyz(s);
            NifKeyReader.Read(s, NifKeyValueKind.Vec3);   // translations
            NifKeyReader.Read(s, NifKeyValueKind.Float);  // scales
            _ = rotType;
        }

        /// <summary>Reads either a quaternion key-map or, if XYZ, three float key-maps with an axis-order header.</summary>
        private static NifInterpolationType NiReadRotationsOrXyz(NifStream s)
        {
            uint count = s.ReadUInt32();
            if (count == 0) return NifInterpolationType.Unknown;
            var type = (NifInterpolationType)s.ReadUInt32();
            if (type == NifInterpolationType.XYZ)
            {
                s.ReadUInt32(); // axis order
                NifKeyReader.Read(s, NifKeyValueKind.Float);
                NifKeyReader.Read(s, NifKeyValueKind.Float);
                NifKeyReader.Read(s, NifKeyValueKind.Float);
                return type;
            }
            for (int i = 0; i < count; i++)
            {
                s.ReadFloat(); // time
                switch (type)
                {
                    case NifInterpolationType.Linear:
                    case NifInterpolationType.Constant:
                        s.Skip(16); break; // quat value
                    case NifInterpolationType.Quadratic:
                        s.Skip(16); break; // quadratic quat has no tangents
                    case NifInterpolationType.TBC:
                        s.Skip(16 + 12); break;
                    default:
                        throw new System.NotSupportedException($"Unknown rotation interp {(int)type}");
                }
            }
            return type;
        }
    }

    public class NiFloatData : NifRecord
    {
        public override void Read(NifStream s) => NifKeyReader.Read(s, NifKeyValueKind.Float);
    }

    public class NiPosData : NifRecord
    {
        public override void Read(NifStream s) => NifKeyReader.Read(s, NifKeyValueKind.Vec3);
    }

    public class NiColorData : NifRecord
    {
        public override void Read(NifStream s) => NifKeyReader.Read(s, NifKeyValueKind.Vec4);
    }

    public class NiUVData : NifRecord
    {
        public override void Read(NifStream s)
        {
            for (int i = 0; i < 4; i++) NifKeyReader.Read(s, NifKeyValueKind.Float);
        }
    }

    public class NiVisData : NifRecord
    {
        public override void Read(NifStream s)
        {
            uint n = s.ReadUInt32();
            for (int i = 0; i < n; i++) { s.ReadFloat(); s.ReadByte(); }
        }
    }

    /// <summary>Per-morph vertex deltas driven by a NiGeomMorpherController.</summary>
    public class NiMorphData : NifRecord
    {
        public override void Read(NifStream s)
        {
            uint numMorphs = s.ReadUInt32();
            uint numVerts = s.ReadUInt32();
            s.ReadByte(); // relativeTargets
            for (int i = 0; i < numMorphs; i++)
            {
                NifKeyReader.Read(s, NifKeyValueKind.Float, morph: true);
                s.Skip(numVerts * 12); // per-vertex Vec3 delta
            }
        }
    }

    // ------------------------------------------------------------------------
    // Effects (texture projectors, lights) — parsed structurally only.
    // ------------------------------------------------------------------------

    public class NiDynamicEffect : NiAVObject
    {
        public override void Read(NifStream s)
        {
            base.Read(s);
            uint numAffected = s.ReadUInt32();
            s.Skip(numAffected * 4);
        }
    }

    public class NiTextureEffect : NiDynamicEffect
    {
        public Matrix4x4 ProjectionRotation;
        public Vector3 ProjectionPosition;
        public uint FilterMode, ClampMode, TextureType, CoordGenType;
        public int SourceTexture;
        public bool EnableClipPlane;
        public Vector4 ClipPlane;

        public override void Read(NifStream s)
        {
            base.Read(s);
            ProjectionRotation = s.ReadMatrix3();
            ProjectionPosition = s.ReadVec3();
            FilterMode = s.ReadUInt32();
            ClampMode = s.ReadUInt32();
            TextureType = s.ReadUInt32();
            CoordGenType = s.ReadUInt32();
            SourceTexture = s.ReadInt32();
            // mEnableClipPlane is a single byte (uint8_t in OpenMW), NOT a bool32.
            EnableClipPlane = s.ReadByte() != 0;
            ClipPlane = new Vector4(s.ReadFloat(), s.ReadFloat(), s.ReadFloat(), s.ReadFloat());
            s.Skip(4); // PS2-specific shorts
            s.Skip(2); // unknown short (MW version <= 4.1.0.12)
        }
    }

    // ------------------------------------------------------------------------
    // Skinning (creatures, NPCs) — parsed structurally only; not rigged yet.
    // ------------------------------------------------------------------------

    public class NiSkinInstance : NifRecord
    {
        public int Data;
        public int Root;
        public int[] Bones;

        public override void Read(NifStream s)
        {
            Data = s.ReadInt32();
            Root = s.ReadInt32();
            uint n = s.ReadUInt32();
            Bones = new int[n];
            for (int i = 0; i < n; i++) Bones[i] = s.ReadInt32();
        }
    }

    public class NiSkinData : NifRecord
    {
        public override void Read(NifStream s)
        {
            // Overall skin transform: mat3 rot + vec3 trans + float scale = 52 bytes
            s.Skip(52);
            uint numBones = s.ReadUInt32();
            // MW reads partitions link here (version <= 10.1.0.0)
            s.ReadInt32(); // partitions link, ignored
            // hasVertexWeights is NOT read for MW (version < 4.2.1.0), defaults to true.
            for (int b = 0; b < numBones; b++)
            {
                s.Skip(52);           // per-bone transform
                s.Skip(16);           // bounding sphere (vec3 + float)
                ushort numVerts = s.ReadUInt16();
                s.Skip(numVerts * 6); // each weight: uint16 vertex + float weight
            }
        }
    }

    // ------------------------------------------------------------------------
    // Particle modifiers (parsed structurally only)
    // ------------------------------------------------------------------------

    public class NiParticleModifier : NifRecord
    {
        public int Next;
        public int Controller;

        public override void Read(NifStream s)
        {
            Next = s.ReadInt32();
            Controller = s.ReadInt32(); // >= 3.3.0.13 — always true for MW
        }
    }

    public class NiParticleGrowFade : NiParticleModifier
    {
        public float GrowTime, FadeTime;

        public override void Read(NifStream s)
        {
            base.Read(s);
            GrowTime = s.ReadFloat();
            FadeTime = s.ReadFloat();
        }
    }

    public class NiParticleColorModifier : NiParticleModifier
    {
        public int Data;

        public override void Read(NifStream s)
        {
            base.Read(s);
            Data = s.ReadInt32();
        }
    }

    public class NiGravity : NiParticleModifier
    {
        public float Decay, Force;
        public uint Type;
        public Vector3 Position, Direction;

        public override void Read(NifStream s)
        {
            base.Read(s);
            Decay = s.ReadFloat();
            Force = s.ReadFloat();
            Type = s.ReadUInt32();
            Position = s.ReadVec3();
            Direction = s.ReadVec3();
        }
    }

    public class NiParticleBomb : NiParticleModifier
    {
        public float Range, Duration, Strength, StartTime;
        public uint DecayType, SymmetryType;
        public Vector3 Position, Direction;

        public override void Read(NifStream s)
        {
            base.Read(s);
            Range = s.ReadFloat();
            Duration = s.ReadFloat();
            Strength = s.ReadFloat();
            StartTime = s.ReadFloat();
            DecayType = s.ReadUInt32();
            SymmetryType = s.ReadUInt32();
            Position = s.ReadVec3();
            Direction = s.ReadVec3();
        }
    }

    public class NiParticleCollider : NiParticleModifier
    {
        public float BounceFactor;
        // For MW (version < 4.2.0.2), Spawn/Die flags are NOT read.

        public override void Read(NifStream s)
        {
            base.Read(s);
            BounceFactor = s.ReadFloat();
        }
    }

    public class NiPlanarCollider : NiParticleCollider
    {
        public Vector2 Extents;
        public Vector3 Position, XVector, YVector, PlaneNormal;
        public float PlaneDistance;

        public override void Read(NifStream s)
        {
            base.Read(s);
            Extents = new Vector2(s.ReadFloat(), s.ReadFloat());
            Position = s.ReadVec3();
            XVector = s.ReadVec3();
            YVector = s.ReadVec3();
            PlaneNormal = s.ReadVec3();
            PlaneDistance = s.ReadFloat();
        }
    }

    public class NiSphericalCollider : NiParticleCollider
    {
        public float Radius;
        public Vector3 Center;

        public override void Read(NifStream s)
        {
            base.Read(s);
            Radius = s.ReadFloat();
            Center = s.ReadVec3();
        }
    }

    public class NiParticleRotation : NiParticleModifier
    {
        public byte RandomInitialAxis;
        public Vector3 InitialAxis;
        public float RotationSpeed;

        public override void Read(NifStream s)
        {
            base.Read(s);
            RandomInitialAxis = s.ReadByte();
            InitialAxis = s.ReadVec3();
            RotationSpeed = s.ReadFloat();
        }
    }

    // ------------------------------------------------------------------------
    // Textures
    // ------------------------------------------------------------------------

    public class NiTexture : NiObjectNET { }

    public class NiSourceTexture : NiTexture
    {
        public bool External;
        public string FileName;
        public uint PixelLayout;
        public uint UseMipMaps;
        public uint AlphaFormat;
        public bool IsStatic;

        public override void Read(NifStream s)
        {
            base.Read(s);
            // mExternal is a single byte (char in OpenMW), NOT a bool32.
            External = s.ReadByte() != 0;
            if (External)
            {
                FileName = s.ReadSizedString();
            }
            else
            {
                bool hasData = s.ReadByte() != 0;
                if (hasData) s.ReadInt32(); // data link — ignore internal pixel data for now
            }
            PixelLayout = s.ReadUInt32();
            UseMipMaps = s.ReadUInt32();
            AlphaFormat = s.ReadUInt32();
            // mIsStatic is also a single byte (char), not a bool32.
            IsStatic = s.ReadByte() != 0;
        }
    }

    // ------------------------------------------------------------------------
    // Extra data
    // ------------------------------------------------------------------------

    public class NiStringExtraData : Extra
    {
        public string Data;

        public override void Read(NifStream s)
        {
            base.Read(s);
            Data = s.ReadSizedString();
        }
    }

    /// <summary>Legacy pre-skin vertex weight list. Parsed structurally only.</summary>
    public class NiVertWeightsExtraData : Extra
    {
        public override void Read(NifStream s)
        {
            base.Read(s);
            ushort n = s.ReadUInt16();
            s.Skip(n * 4);
        }
    }

    public class NiTextKeyExtraData : Extra
    {
        public struct Key { public float Time; public string Text; }
        public Key[] Keys;

        public override void Read(NifStream s)
        {
            base.Read(s);
            uint n = s.ReadUInt32();
            Keys = new Key[n];
            for (int i = 0; i < n; i++)
            {
                Keys[i].Time = s.ReadFloat();
                Keys[i].Text = s.ReadSizedString();
            }
        }
    }
}
