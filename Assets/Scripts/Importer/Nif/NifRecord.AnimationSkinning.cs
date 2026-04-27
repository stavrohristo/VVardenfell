using System.Collections.Generic;
using UnityEngine;

namespace VVardenfell.Importer.Nif
{
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


    public struct NifControlledBlock
    {
        public string TargetName;
        public int Controller;
    }


    /// <summary>Morrowind KF root record. It points text keys plus per-node controller blocks at skeleton node names.</summary>
    public class NiSequence : NifRecord
    {
        public string Name;
        public string AccumRootName;
        public int TextKeys;
        public NifControlledBlock[] ControlledBlocks = System.Array.Empty<NifControlledBlock>();

        public override void Read(NifStream s)
        {
            Name = s.ReadSizedString();
            AccumRootName = s.ReadSizedString();
            TextKeys = s.ReadInt32();
            uint count = s.ReadUInt32();
            ControlledBlocks = new NifControlledBlock[count];
            for (int i = 0; i < count; i++)
            {
                ControlledBlocks[i] = new NifControlledBlock
                {
                    TargetName = s.ReadSizedString(),
                    Controller = s.ReadInt32(),
                };
            }
        }
    }


    // ------------------------------------------------------------------------
    // Animation data
    // ------------------------------------------------------------------------


    public class NiKeyframeData : NifRecord
    {
        public NifKeyGroup Rotations;
        public NifKeyGroup XRotations;
        public NifKeyGroup YRotations;
        public NifKeyGroup ZRotations;
        public NifKeyGroup Translations;
        public NifKeyGroup Scales;
        public uint AxisOrder;

        public override void Read(NifStream s)
        {
            ReadRotationsOrXyz(s);
            Translations = NifKeyReader.Read(s, NifKeyValueKind.Vec3);
            Scales = NifKeyReader.Read(s, NifKeyValueKind.Float);
        }

        /// <summary>Reads either a quaternion key-map or, if XYZ, three float key-maps with an axis-order header.</summary>
        private void ReadRotationsOrXyz(NifStream s)
        {
            uint count = s.ReadUInt32();
            if (count == 0)
            {
                Rotations = new NifKeyGroup();
                return;
            }

            var type = (NifInterpolationType)s.ReadUInt32();
            if (type == NifInterpolationType.XYZ)
            {
                AxisOrder = s.ReadUInt32();
                XRotations = NifKeyReader.Read(s, NifKeyValueKind.Float);
                YRotations = NifKeyReader.Read(s, NifKeyValueKind.Float);
                ZRotations = NifKeyReader.Read(s, NifKeyValueKind.Float);
                Rotations = new NifKeyGroup { InterpolationType = NifInterpolationType.XYZ };
                return;
            }

            Rotations = new NifKeyGroup
            {
                InterpolationType = type,
                Keys = new NifAnimationKey[count],
            };

            for (int i = 0; i < count; i++)
            {
                var key = new NifAnimationKey { Time = s.ReadFloat() };
                switch (type)
                {
                    case NifInterpolationType.Linear:
                    case NifInterpolationType.Constant:
                    case NifInterpolationType.Quadratic:
                    {
                        Quaternion value = s.ReadQuat();
                        key.Value = new Vector4(value.x, value.y, value.z, value.w);
                        break;
                    }
                    case NifInterpolationType.TBC:
                    {
                        Quaternion value = s.ReadQuat();
                        key.Value = new Vector4(value.x, value.y, value.z, value.w);
                        key.InTan.x = s.ReadFloat();
                        key.InTan.y = s.ReadFloat();
                        key.InTan.z = s.ReadFloat();
                        break;
                    }
                    default:
                        throw new System.NotSupportedException($"Unknown rotation interp {(int)type}");
                }
                Rotations.Keys[i] = key;
            }
        }
    }


    public class NiFloatData : NifRecord
    {
        public NifKeyGroup Keys;

        public override void Read(NifStream s) => Keys = NifKeyReader.Read(s, NifKeyValueKind.Float);
    }


    public class NiPosData : NifRecord
    {
        public NifKeyGroup Keys;

        public override void Read(NifStream s) => Keys = NifKeyReader.Read(s, NifKeyValueKind.Vec3);
    }


    public class NiColorData : NifRecord
    {
        public NifKeyGroup Keys;

        public override void Read(NifStream s) => Keys = NifKeyReader.Read(s, NifKeyValueKind.Vec4);
    }


    public class NiUVData : NifRecord
    {
        public NifKeyGroup[] Keys = new NifKeyGroup[4];

        public override void Read(NifStream s)
        {
            for (int i = 0; i < Keys.Length; i++)
                Keys[i] = NifKeyReader.Read(s, NifKeyValueKind.Float);
        }
    }


    public class NiVisData : NifRecord
    {
        public struct Key { public float Time; public bool Value; }
        public Key[] Keys;

        public override void Read(NifStream s)
        {
            uint n = s.ReadUInt32();
            Keys = new Key[n];
            for (int i = 0; i < n; i++)
            {
                Keys[i].Time = s.ReadFloat();
                Keys[i].Value = s.ReadByte() != 0;
            }
        }
    }


    /// <summary>Per-morph vertex deltas driven by a NiGeomMorpherController.</summary>
    public class NiMorphData : NifRecord
    {
        public sealed class Morph
        {
            public NifKeyGroup Keys;
            public Vector3[] Vertices;
        }

        public Morph[] Morphs = System.Array.Empty<Morph>();
        public uint NumVertices;
        public byte RelativeTargets;

        public override void Read(NifStream s)
        {
            uint numMorphs = s.ReadUInt32();
            NumVertices = s.ReadUInt32();
            RelativeTargets = s.ReadByte();
            Morphs = new Morph[numMorphs];
            for (int i = 0; i < numMorphs; i++)
            {
                var morph = new Morph
                {
                    Keys = NifKeyReader.Read(s, NifKeyValueKind.Float, morph: true),
                    Vertices = new Vector3[(int)NumVertices],
                };
                for (int v = 0; v < morph.Vertices.Length; v++)
                    morph.Vertices[v] = s.ReadVec3();
                Morphs[i] = morph;
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
        public struct SkinTransform
        {
            public Matrix4x4 Rotation;
            public Vector3 Translation;
            public float Scale;
        }

        public struct VertexWeight
        {
            public ushort Vertex;
            public float Weight;
        }

        public sealed class BoneInfo
        {
            public SkinTransform Transform;
            public Vector3 BoundSphereCenter;
            public float BoundSphereRadius;
            public VertexWeight[] Weights = System.Array.Empty<VertexWeight>();
        }

        public SkinTransform Transform;
        public int Partitions;
        public BoneInfo[] Bones = System.Array.Empty<BoneInfo>();

        public override void Read(NifStream s)
        {
            Transform = ReadSkinTransform(s);
            uint numBones = s.ReadUInt32();
            // MW reads partitions link here (version <= 10.1.0.0)
            Partitions = s.ReadInt32();
            // hasVertexWeights is NOT read for MW (version < 4.2.1.0), defaults to true.
            Bones = new BoneInfo[numBones];
            for (int b = 0; b < numBones; b++)
            {
                var bone = new BoneInfo
                {
                    Transform = ReadSkinTransform(s),
                    BoundSphereCenter = s.ReadVec3(),
                    BoundSphereRadius = s.ReadFloat(),
                };
                ushort numVerts = s.ReadUInt16();
                bone.Weights = new VertexWeight[numVerts];
                for (int i = 0; i < numVerts; i++)
                {
                    bone.Weights[i] = new VertexWeight
                    {
                        Vertex = s.ReadUInt16(),
                        Weight = s.ReadFloat(),
                    };
                }
                Bones[b] = bone;
            }
        }

        static SkinTransform ReadSkinTransform(NifStream s)
            => new()
            {
                Rotation = s.ReadMatrix3(),
                Translation = s.ReadVec3(),
                Scale = s.ReadFloat(),
            };
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
