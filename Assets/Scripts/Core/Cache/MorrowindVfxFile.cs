using System;
using System.IO;

namespace VVardenfell.Core.Cache
{
    [Flags]
    public enum MorrowindVfxControllerCategory : uint
    {
        None = 0,
        Particle = 1u << 0,
        Alpha = 1u << 1,
        Uv = 1u << 2,
        Flip = 1u << 3,
        MaterialColor = 1u << 4,
        Morph = 1u << 5,
        Keyframe = 1u << 6,
        Visibility = 1u << 7,
        ParticleRotation = 1u << 8,
        Path = 1u << 9,
        Unknown = 1u << 31,
    }

    public enum MorrowindVfxParticleModifierKind : byte
    {
        None = 0,
        GrowFade = 1,
        Color = 2,
        Gravity = 3,
        Bomb = 4,
        PlanarCollider = 5,
        SphericalCollider = 6,
        ParticleRotation = 7,
    }

    public sealed class MorrowindVfxCatalogData
    {
        public MorrowindVfxEffectDef[] Effects = Array.Empty<MorrowindVfxEffectDef>();
        public MorrowindVfxParticleSystemDef[] ParticleSystems = Array.Empty<MorrowindVfxParticleSystemDef>();
        public MorrowindVfxInitialParticleDef[] InitialParticles = Array.Empty<MorrowindVfxInitialParticleDef>();
        public MorrowindVfxParticleModifierDef[] Modifiers = Array.Empty<MorrowindVfxParticleModifierDef>();
        public MorrowindVfxControllerDef[] Controllers = Array.Empty<MorrowindVfxControllerDef>();
        public string[] TexturePaths = Array.Empty<string>();
    }

    public sealed class MorrowindVfxEffectDef
    {
        public string ModelPath;
        public float Lifetime;
        public byte Required;
        public MorrowindVfxControllerCategory ControllerCategories;
        public MorrowindVfxControllerCategory UnsupportedRequiredCategories;
        public int FirstParticleSystemIndex = -1;
        public int ParticleSystemCount;
        public int FirstControllerIndex = -1;
        public int ControllerCount;
        public int FirstTexturePathIndex = -1;
        public int TexturePathCount;
    }

    public sealed class MorrowindVfxParticleSystemDef
    {
        public string NodeName;
        public string EmitterNodeName;
        public string TexturePath;
        public ushort Quota;
        public ushort ActiveCount;
        public ushort EmitFlags;
        public float Speed;
        public float SpeedVariation;
        public float Declination;
        public float DeclinationVariation;
        public float PlanarAngle;
        public float PlanarAngleVariation;
        public float InitialSize;
        public float BirthRate;
        public float Lifetime;
        public float LifetimeVariation;
        public float EmitStartTime;
        public float EmitStopTime;
        public float InitialNormalX;
        public float InitialNormalY;
        public float InitialNormalZ;
        public float InitialColorR;
        public float InitialColorG;
        public float InitialColorB;
        public float InitialColorA;
        public float EmitterDimX;
        public float EmitterDimY;
        public float EmitterDimZ;
        public int FirstInitialParticleIndex = -1;
        public int InitialParticleCount;
        public int FirstModifierIndex = -1;
        public int ModifierCount;
    }

    public struct MorrowindVfxInitialParticleDef
    {
        public float PositionX;
        public float PositionY;
        public float PositionZ;
        public float VelocityX;
        public float VelocityY;
        public float VelocityZ;
        public float Size;
        public float Age;
        public float Lifetime;
        public ushort SpawnGeneration;
        public ushort Code;
    }

    public sealed class MorrowindVfxParticleModifierDef
    {
        public MorrowindVfxParticleModifierKind Kind;
        public float X0;
        public float Y0;
        public float Z0;
        public float W0;
        public float X1;
        public float Y1;
        public float Z1;
        public float W1;
    }

    public sealed class MorrowindVfxControllerDef
    {
        public MorrowindVfxControllerCategory Category;
        public string TargetName;
        public float Frequency;
        public float Phase;
        public float TimeStart;
        public float TimeStop;
    }

    public static class MorrowindVfxFile
    {
        const uint Magic = 0x5846564Du; // 'MVFX'
        const uint Version = 4u;

        public static bool TryRead(string path, out MorrowindVfxCatalogData data)
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

        public static MorrowindVfxCatalogData Read(string path)
        {
            using var fs = File.OpenRead(path);
            using var r = new BinaryReader(fs);

            uint magic = r.ReadUInt32();
            if (magic != Magic)
                throw new InvalidDataException($"Bad VFX cache magic 0x{magic:X8} in '{path}'.");

            uint version = r.ReadUInt32();
            if (version != Version)
                throw new InvalidDataException($"Unsupported VFX cache version {version} in '{path}'.");

            return new MorrowindVfxCatalogData
            {
                Effects = ReadEffects(r),
                ParticleSystems = ReadParticleSystems(r),
                InitialParticles = ReadInitialParticles(r),
                Modifiers = ReadModifiers(r),
                Controllers = ReadControllers(r),
                TexturePaths = ReadStrings(r),
            };
        }

        public static void Write(string path, MorrowindVfxCatalogData data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
            using var fs = File.Create(path);
            using var w = new BinaryWriter(fs);

            w.Write(Magic);
            w.Write(Version);
            WriteEffects(w, data?.Effects);
            WriteParticleSystems(w, data?.ParticleSystems);
            WriteInitialParticles(w, data?.InitialParticles);
            WriteModifiers(w, data?.Modifiers);
            WriteControllers(w, data?.Controllers);
            WriteStrings(w, data?.TexturePaths);
        }

        static void WriteEffects(BinaryWriter w, MorrowindVfxEffectDef[] values)
        {
            values ??= Array.Empty<MorrowindVfxEffectDef>();
            w.Write(values.Length);
            for (int i = 0; i < values.Length; i++)
            {
                var v = values[i] ?? new MorrowindVfxEffectDef();
                w.Write(v.ModelPath ?? string.Empty);
                w.Write(v.Lifetime);
                w.Write(v.Required);
                w.Write((uint)v.ControllerCategories);
                w.Write((uint)v.UnsupportedRequiredCategories);
                w.Write(v.FirstParticleSystemIndex);
                w.Write(v.ParticleSystemCount);
                w.Write(v.FirstControllerIndex);
                w.Write(v.ControllerCount);
                w.Write(v.FirstTexturePathIndex);
                w.Write(v.TexturePathCount);
            }
        }

        static MorrowindVfxEffectDef[] ReadEffects(BinaryReader r)
        {
            int count = ReadCount(r, "VFX effect");
            var values = new MorrowindVfxEffectDef[count];
            for (int i = 0; i < count; i++)
            {
                values[i] = new MorrowindVfxEffectDef
                {
                    ModelPath = r.ReadString(),
                    Lifetime = r.ReadSingle(),
                    Required = r.ReadByte(),
                    ControllerCategories = (MorrowindVfxControllerCategory)r.ReadUInt32(),
                    UnsupportedRequiredCategories = (MorrowindVfxControllerCategory)r.ReadUInt32(),
                    FirstParticleSystemIndex = r.ReadInt32(),
                    ParticleSystemCount = r.ReadInt32(),
                    FirstControllerIndex = r.ReadInt32(),
                    ControllerCount = r.ReadInt32(),
                    FirstTexturePathIndex = r.ReadInt32(),
                    TexturePathCount = r.ReadInt32(),
                };
            }

            return values;
        }

        static void WriteParticleSystems(BinaryWriter w, MorrowindVfxParticleSystemDef[] values)
        {
            values ??= Array.Empty<MorrowindVfxParticleSystemDef>();
            w.Write(values.Length);
            for (int i = 0; i < values.Length; i++)
            {
                var v = values[i] ?? new MorrowindVfxParticleSystemDef();
                w.Write(v.NodeName ?? string.Empty);
                w.Write(v.EmitterNodeName ?? string.Empty);
                w.Write(v.TexturePath ?? string.Empty);
                w.Write(v.Quota);
                w.Write(v.ActiveCount);
                w.Write(v.EmitFlags);
                w.Write(v.Speed);
                w.Write(v.SpeedVariation);
                w.Write(v.Declination);
                w.Write(v.DeclinationVariation);
                w.Write(v.PlanarAngle);
                w.Write(v.PlanarAngleVariation);
                w.Write(v.InitialSize);
                w.Write(v.BirthRate);
                w.Write(v.Lifetime);
                w.Write(v.LifetimeVariation);
                w.Write(v.EmitStartTime);
                w.Write(v.EmitStopTime);
                w.Write(v.InitialNormalX);
                w.Write(v.InitialNormalY);
                w.Write(v.InitialNormalZ);
                w.Write(v.InitialColorR);
                w.Write(v.InitialColorG);
                w.Write(v.InitialColorB);
                w.Write(v.InitialColorA);
                w.Write(v.EmitterDimX);
                w.Write(v.EmitterDimY);
                w.Write(v.EmitterDimZ);
                w.Write(v.FirstInitialParticleIndex);
                w.Write(v.InitialParticleCount);
                w.Write(v.FirstModifierIndex);
                w.Write(v.ModifierCount);
            }
        }

        static MorrowindVfxParticleSystemDef[] ReadParticleSystems(BinaryReader r)
        {
            int count = ReadCount(r, "VFX particle system");
            var values = new MorrowindVfxParticleSystemDef[count];
            for (int i = 0; i < count; i++)
            {
                values[i] = new MorrowindVfxParticleSystemDef
                {
                    NodeName = r.ReadString(),
                    EmitterNodeName = r.ReadString(),
                    TexturePath = r.ReadString(),
                    Quota = r.ReadUInt16(),
                    ActiveCount = r.ReadUInt16(),
                    EmitFlags = r.ReadUInt16(),
                    Speed = r.ReadSingle(),
                    SpeedVariation = r.ReadSingle(),
                    Declination = r.ReadSingle(),
                    DeclinationVariation = r.ReadSingle(),
                    PlanarAngle = r.ReadSingle(),
                    PlanarAngleVariation = r.ReadSingle(),
                    InitialSize = r.ReadSingle(),
                    BirthRate = r.ReadSingle(),
                    Lifetime = r.ReadSingle(),
                    LifetimeVariation = r.ReadSingle(),
                    EmitStartTime = r.ReadSingle(),
                    EmitStopTime = r.ReadSingle(),
                    InitialNormalX = r.ReadSingle(),
                    InitialNormalY = r.ReadSingle(),
                    InitialNormalZ = r.ReadSingle(),
                    InitialColorR = r.ReadSingle(),
                    InitialColorG = r.ReadSingle(),
                    InitialColorB = r.ReadSingle(),
                    InitialColorA = r.ReadSingle(),
                    EmitterDimX = r.ReadSingle(),
                    EmitterDimY = r.ReadSingle(),
                    EmitterDimZ = r.ReadSingle(),
                    FirstInitialParticleIndex = r.ReadInt32(),
                    InitialParticleCount = r.ReadInt32(),
                    FirstModifierIndex = r.ReadInt32(),
                    ModifierCount = r.ReadInt32(),
                };
            }

            return values;
        }

        static void WriteInitialParticles(BinaryWriter w, MorrowindVfxInitialParticleDef[] values)
        {
            values ??= Array.Empty<MorrowindVfxInitialParticleDef>();
            w.Write(values.Length);
            for (int i = 0; i < values.Length; i++)
            {
                var v = values[i];
                w.Write(v.PositionX);
                w.Write(v.PositionY);
                w.Write(v.PositionZ);
                w.Write(v.VelocityX);
                w.Write(v.VelocityY);
                w.Write(v.VelocityZ);
                w.Write(v.Size);
                w.Write(v.Age);
                w.Write(v.Lifetime);
                w.Write(v.SpawnGeneration);
                w.Write(v.Code);
            }
        }

        static MorrowindVfxInitialParticleDef[] ReadInitialParticles(BinaryReader r)
        {
            int count = ReadCount(r, "VFX initial particle");
            var values = new MorrowindVfxInitialParticleDef[count];
            for (int i = 0; i < count; i++)
            {
                values[i] = new MorrowindVfxInitialParticleDef
                {
                    PositionX = r.ReadSingle(),
                    PositionY = r.ReadSingle(),
                    PositionZ = r.ReadSingle(),
                    VelocityX = r.ReadSingle(),
                    VelocityY = r.ReadSingle(),
                    VelocityZ = r.ReadSingle(),
                    Size = r.ReadSingle(),
                    Age = r.ReadSingle(),
                    Lifetime = r.ReadSingle(),
                    SpawnGeneration = r.ReadUInt16(),
                    Code = r.ReadUInt16(),
                };
            }

            return values;
        }

        static void WriteModifiers(BinaryWriter w, MorrowindVfxParticleModifierDef[] values)
        {
            values ??= Array.Empty<MorrowindVfxParticleModifierDef>();
            w.Write(values.Length);
            for (int i = 0; i < values.Length; i++)
            {
                var v = values[i] ?? new MorrowindVfxParticleModifierDef();
                w.Write((byte)v.Kind);
                w.Write(v.X0);
                w.Write(v.Y0);
                w.Write(v.Z0);
                w.Write(v.W0);
                w.Write(v.X1);
                w.Write(v.Y1);
                w.Write(v.Z1);
                w.Write(v.W1);
            }
        }

        static MorrowindVfxParticleModifierDef[] ReadModifiers(BinaryReader r)
        {
            int count = ReadCount(r, "VFX modifier");
            var values = new MorrowindVfxParticleModifierDef[count];
            for (int i = 0; i < count; i++)
            {
                values[i] = new MorrowindVfxParticleModifierDef
                {
                    Kind = (MorrowindVfxParticleModifierKind)r.ReadByte(),
                    X0 = r.ReadSingle(),
                    Y0 = r.ReadSingle(),
                    Z0 = r.ReadSingle(),
                    W0 = r.ReadSingle(),
                    X1 = r.ReadSingle(),
                    Y1 = r.ReadSingle(),
                    Z1 = r.ReadSingle(),
                    W1 = r.ReadSingle(),
                };
            }

            return values;
        }

        static void WriteControllers(BinaryWriter w, MorrowindVfxControllerDef[] values)
        {
            values ??= Array.Empty<MorrowindVfxControllerDef>();
            w.Write(values.Length);
            for (int i = 0; i < values.Length; i++)
            {
                var v = values[i] ?? new MorrowindVfxControllerDef();
                w.Write((uint)v.Category);
                w.Write(v.TargetName ?? string.Empty);
                w.Write(v.Frequency);
                w.Write(v.Phase);
                w.Write(v.TimeStart);
                w.Write(v.TimeStop);
            }
        }

        static MorrowindVfxControllerDef[] ReadControllers(BinaryReader r)
        {
            int count = ReadCount(r, "VFX controller");
            var values = new MorrowindVfxControllerDef[count];
            for (int i = 0; i < count; i++)
            {
                values[i] = new MorrowindVfxControllerDef
                {
                    Category = (MorrowindVfxControllerCategory)r.ReadUInt32(),
                    TargetName = r.ReadString(),
                    Frequency = r.ReadSingle(),
                    Phase = r.ReadSingle(),
                    TimeStart = r.ReadSingle(),
                    TimeStop = r.ReadSingle(),
                };
            }

            return values;
        }

        static void WriteStrings(BinaryWriter w, string[] values)
        {
            values ??= Array.Empty<string>();
            w.Write(values.Length);
            for (int i = 0; i < values.Length; i++)
                w.Write(values[i] ?? string.Empty);
        }

        static string[] ReadStrings(BinaryReader r)
        {
            int count = ReadCount(r, "VFX texture path");
            var values = new string[count];
            for (int i = 0; i < count; i++)
                values[i] = r.ReadString();
            return values;
        }

        static int ReadCount(BinaryReader r, string label)
        {
            int count = r.ReadInt32();
            if (count < 0)
                throw new InvalidDataException($"Negative {label} count {count}.");
            return count;
        }
    }
}
