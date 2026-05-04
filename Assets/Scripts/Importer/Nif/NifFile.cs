using System;
using System.Collections.Generic;
using System.IO;

namespace VVardenfell.Importer.Nif
{
    /// <summary>
    /// Parsed NIF file: flat record array + root indices. Links between records
    /// are stored as int32 indices (-1 = null) to be resolved by the caller.
    /// </summary>
    public sealed class NifFile
    {
        public string Path;
        public uint Version;
        public NifRecord[] Records;
        public int[] Roots;

        public static NifFile Parse(string path, byte[] bytes)
        {
            using var ms = new MemoryStream(bytes, writable: false);
            using var s = new NifStream(path, ms);
            s.ReadHeader();

            uint numRecords = s.ReadUInt32();
            if (numRecords > 1_000_000)
                throw new InvalidDataException($"Absurd record count {numRecords} in {path}");

            var records = new NifRecord[numRecords];
            string lastType = "(none)";
            long lastStart = 0, lastBodyStart = 0;
            for (int i = 0; i < numRecords; i++)
            {
                long recStart = s.Position;
                string typeName;
                try { typeName = s.ReadSizedString(); }
                catch (System.Exception ex)
                {
                    throw new InvalidDataException(
                        $"Failed reading record type at index {i} (offset 0x{recStart:X}); " +
                        $"prev record #{i - 1} was '{lastType}' starting at 0x{lastStart:X} (body 0x{lastBodyStart:X}): {ex.Message}");
                }
                if (string.IsNullOrEmpty(typeName))
                    throw new InvalidDataException($"Empty record type at index {i} (offset 0x{recStart:X}) in {path}");

                long bodyStart = s.Position;
                var rec = NifFactory.Create(typeName, path, i, bodyStart);
                rec.RecordType = typeName;
                rec.RecordIndex = i;
                try { rec.Read(s); }
                catch (System.Exception ex)
                {
                    throw new InvalidDataException(
                        $"Failed reading record #{i} '{typeName}' body at 0x{bodyStart:X}: {ex.Message}");
                }
                records[i] = rec;
                lastType = typeName;
                lastStart = recStart;
                lastBodyStart = bodyStart;
            }

            uint numRoots = s.ReadUInt32();
            var roots = new int[numRoots];
            for (int i = 0; i < numRoots; i++) roots[i] = s.ReadInt32();

            return new NifFile
            {
                Path = path,
                Version = s.Version,
                Records = records,
                Roots = roots,
            };
        }
    }

    internal static class NifFactory
    {
        private delegate NifRecord Ctor();

        private static readonly Dictionary<string, Ctor> _map = new()
        {
            // Nodes
            { "NiNode", () => new NiNode() },
            { "AvoidNode", () => new AvoidNode() },
            { "RootCollisionNode", () => new RootCollisionNode() },
            { "NiBSAnimationNode", () => new NiBSAnimationNode() },
            { "NiBSParticleNode", () => new NiBSParticleNode() },
            { "NiCollisionSwitch", () => new NiCollisionSwitch() },
            { "NiBillboardNode", () => new NiBillboardNode() },
            { "NiSwitchNode", () => new NiSwitchNode() },
            { "NiLODNode", () => new NiLODNode() },
            { "NiFltAnimationNode", () => new NiFltAnimationNode() },
            { "NiSequenceStreamHelper", () => new NiSequenceStreamHelper() },
            { "NiCamera", () => new NiCamera() },

            // Geometry
            { "NiTriShape", () => new NiTriShape() },
            { "NiTriShapeData", () => new NiTriShapeData() },
            { "NiTriStrips", () => new NiTriStrips() },
            { "NiTriStripsData", () => new NiTriStripsData() },

            // Particles (parsed structurally; not rendered in first pass)
            { "NiParticles", () => new NiParticles() },
            { "NiParticlesData", () => new NiParticlesData() },
            { "NiAutoNormalParticles", () => new NiAutoNormalParticles() },
            { "NiAutoNormalParticlesData", () => new NiAutoNormalParticlesData() },
            { "NiRotatingParticles", () => new NiRotatingParticles() },
            { "NiRotatingParticlesData", () => new NiRotatingParticlesData() },

            // Controllers
            { "NiSequence", () => new NiSequence() },
            { "NiParticleSystemController", () => new NiParticleSystemController() },
            { "NiBSPArrayController", () => new NiBSPArrayController() },
            { "NiKeyframeController", () => new NiKeyframeController() },
            { "NiVisController", () => new NiVisController() },
            { "NiAlphaController", () => new NiAlphaController() },
            { "NiFlipController", () => new NiFlipController() },
            { "NiUVController", () => new NiUVController() },
            { "NiMaterialColorController", () => new NiMaterialColorController() },
            { "NiPathController", () => new NiPathController() },
            { "NiGeomMorpherController", () => new NiGeomMorpherController() },

            // Animation data
            { "NiKeyframeData", () => new NiKeyframeData() },
            { "NiFloatData", () => new NiFloatData() },
            { "NiPosData", () => new NiPosData() },
            { "NiColorData", () => new NiColorData() },
            { "NiUVData", () => new NiUVData() },
            { "NiVisData", () => new NiVisData() },
            { "NiMorphData", () => new NiMorphData() },

            // Effects
            { "NiTextureEffect", () => new NiTextureEffect() },

            // Skinning
            { "NiSkinInstance", () => new NiSkinInstance() },
            { "NiSkinData", () => new NiSkinData() },

            // Extra data
            { "NiVertWeightsExtraData", () => new NiVertWeightsExtraData() },

            // Particle modifiers
            { "NiParticleGrowFade", () => new NiParticleGrowFade() },
            { "NiParticleColorModifier", () => new NiParticleColorModifier() },
            { "NiGravity", () => new NiGravity() },
            { "NiParticleBomb", () => new NiParticleBomb() },
            { "NiPlanarCollider", () => new NiPlanarCollider() },
            { "NiSphericalCollider", () => new NiSphericalCollider() },
            { "NiParticleRotation", () => new NiParticleRotation() },

            // Properties
            { "NiTexturingProperty", () => new NiTexturingProperty() },
            { "NiMaterialProperty", () => new NiMaterialProperty() },
            { "NiAlphaProperty", () => new NiAlphaProperty() },
            { "NiZBufferProperty", () => new NiZBufferProperty() },
            { "NiVertexColorProperty", () => new NiVertexColorProperty() },
            { "NiShadeProperty", () => new NiShadeProperty() },
            { "NiSpecularProperty", () => new NiSpecularProperty() },
            { "NiWireframeProperty", () => new NiWireframeProperty() },
            { "NiDitherProperty", () => new NiDitherProperty() },
            { "NiStencilProperty", () => new NiStencilProperty() },

            // Textures
            { "NiSourceTexture", () => new NiSourceTexture() },

            // Extras
            { "NiStringExtraData", () => new NiStringExtraData() },
            { "NiTextKeyExtraData", () => new NiTextKeyExtraData() },
        };

        public static NifRecord Create(string name, string path, int index, long offset)
        {
            if (!_map.TryGetValue(name, out var ctor))
                throw new NotSupportedException(
                    $"Unsupported NIF record type '{name}' at index {index} (offset 0x{offset:X}) in {path}");
            return ctor();
        }

        public static bool IsKnown(string name) => _map.ContainsKey(name);
    }
}
